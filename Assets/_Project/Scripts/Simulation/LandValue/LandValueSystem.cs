using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.LandValue
{
    /// <summary>
    /// G6 Bodenwert: Raster (<see cref="Layer"/>, gleiches Gitter wie die Umwelt-Raster) aus Erreichbarkeit,
    /// Diensten, Parks, Wasser-/Aussichtslage, Umweltbelastung, Nachbarschaft und Dichte (Formeln:
    /// <see cref="LandValueRules"/>). Gestaffelt: je Durchlauf (alle 30 Spielminuten) ein 1/48-Anteil der Zellen →
    /// jede Zelle einmal am Spieltag; Werte laufen träge zum Ziel.
    /// <para>
    /// Hilfsraster: Straßenabstand (fein, bei Netzänderung neu), Autobahnnähe sowie Arbeitsplätze, Dichte,
    /// Nachbarschafts-Zufriedenheit und Müll (grob, 64 m, zu Beginn jeder Runde aus den Gebäuden gestempelt).
    /// </para>
    /// Ausbaustufen und Gebäudezustand: <c>LandValueSystem.Levels.cs</c> (täglich).
    /// </summary>
    public sealed partial class LandValueSystem : ISimulationSystem
    {
        /// <summary>Alle 5 Ticks = 30 Spielminuten (48 Durchläufe je Spieltag).</summary>
        public int TickInterval => 5;
        const int SlicesPerCycle = SimulationClock.TicksPerDay / 5;
        const float CoarseCell = 64f;

        readonly SimulationWorld world;
        readonly float[] known;           // 1 = Zelle wurde schon berechnet
        readonly float[] roadDist;        // Abstand zur nächsten Stadtstraße (m), gekappt
        readonly CoarseGrid highway, jobs, units, satSum, satWeight, garbage;
        readonly byte[] terrainKnown;     // 0 = neu berechnen, 1 = Land, 2 = Wasser
        readonly float[] shoreCache, viewCache;
        readonly List<Vector2> externals = new List<Vector2>();
        int cursor;
        int builtRoadVersion = -1;

        /// <summary>Bodenwert je Zelle 0..1 (für Overlay/Abfragen).</summary>
        public GridLayer Layer { get; }
        /// <summary>Abgeschlossene Berechnungsrunden (alle Zellen einmal).</summary>
        public int Cycles { get; private set; }

        /// <summary>
        /// G3 erweitert (Hook, gesetzt in <c>CreateDefault</c>): Verschmutzung des Gewässers in Ufernähe 0..1
        /// (<c>WaterQualitySystem.PollutionNear</c>). null = sauber.
        /// </summary>
        public Func<Vector2, float> WaterPollutionNear { get; set; }

        /// <summary>Sicherheit (Hook): frische Brände + Kriminalität am Punkt 0..1 (null = 0). Gesetzt vom SafetySystem.</summary>
        public Func<Vector2, float> SafetyPenaltyAt { get; set; }

        public LandValueSystem(SimulationWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            var env = world.Environment.Noise;
            Layer = new GridLayer("Bodenwert", env.Origin, env.Width, env.Height, env.CellSize);
            known = new float[env.Width * env.Height];
            roadDist = new float[known.Length];
            float size = env.Width * env.CellSize;
            highway = new CoarseGrid(env.Origin, size, CoarseCell);
            jobs = new CoarseGrid(env.Origin, size, CoarseCell);
            units = new CoarseGrid(env.Origin, size, CoarseCell);
            satSum = new CoarseGrid(env.Origin, size, CoarseCell);
            satWeight = new CoarseGrid(env.Origin, size, CoarseCell);
            garbage = new CoarseGrid(env.Origin, size, CoarseCell);
            terrainKnown = new byte[known.Length];
            shoreCache = new float[known.Length];
            viewCache = new float[known.Length];
            world.Terrain.Changed += OnTerrainChanged;
            world.City.BuildingRemoved += b => states.Remove(b.Id);
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld w)
        {
            UpdateSlice();
            if (world.Clock.Tick % SimulationClock.TicksPerDay == 0) EvaluateBuildings();
        }

        void UpdateSlice()
        {
            if (world.Roads.Version != builtRoadVersion) RebuildRoadRasters();
            if (cursor == 0) RebuildBuildingRasters();
            int n = known.Length;
            int batch = (n + SlicesPerCycle - 1) / SlicesPerCycle;
            for (int k = 0; k < batch && cursor < n; k++, cursor++) UpdateCell(cursor, smooth: true);
            if (cursor >= n) { cursor = 0; Cycles++; }
        }

        /// <summary>Für Tests/Werkzeuge (z. B. nach dem Laden): alle Hilfsraster und Zellen sofort neu, ungeglättet.</summary>
        public void ForceRecalculate()
        {
            RebuildRoadRasters();
            RebuildBuildingRasters();
            for (int i = 0; i < known.Length; i++) UpdateCell(i, smooth: false);
            cursor = 0;
            Cycles++;
        }

        void UpdateCell(int index, bool smooth)
        {
            int x = index % Layer.Width, y = index / Layer.Width;
            var f = LandValueRules.Evaluate(Gather(Layer.CellCenter(x, y), index));
            float old = Layer[x, y];
            float v = smooth && known[index] > 0f ? old + (f.Target - old) * LandValueRules.Smoothing : f.Target;
            known[index] = 1f;
            if (v != old) Layer[x, y] = v;
        }

        // ------------------------------------------------------------ Faktoren sammeln

        LandValueInputs Gather(Vector2 p, int index)
        {
            var i = new LandValueInputs { Neighborhood = -1f };
            // Gelände (Wasser, Ufer, Aussicht) je Zelle zwischengespeichert, neu nur nach Geländeänderung
            if (index >= 0)
            {
                if (terrainKnown[index] == 0) CacheTerrain(index, p);
                if (terrainKnown[index] == 2) { i.IsWater = true; return i; }
                i.Shore = shoreCache[index];
                i.View = viewCache[index];
            }
            else
            {
                if (world.Terrain.IsWater(p)) { i.IsWater = true; return i; }
                i.Shore = ShoreFactor(p);
                i.View = ViewFactor(p);
            }
            // G3 erweitert: Verschmutzung des nahen Gewässers (nur am Ufer abgefragt)
            if (i.Shore > 0f && WaterPollutionNear != null) i.WaterPollution = WaterPollutionNear(p);

            float rd = index >= 0 ? roadDist[index] : RoadDistance(p);
            i.RoadAccess = LandValueRules.Falloff(rd, LandValueRules.RoadFullDistance, LandValueRules.RoadZeroDistance);
            i.JobAccess = LandValueRules.Saturate(jobs.Sample(p), LandValueRules.JobSaturation);
            i.HighwayAccess = highway.Sample(p);
            if (externals.Count > 0)
            {
                float best = float.MaxValue;
                foreach (var e in externals) best = MathF.Min(best, Vector2.Distance(p, e));
                i.ExternalAccess = LandValueRules.Falloff(best, 0f, LandValueRules.ExternalZeroDistance);
            }

            // Dienste: Gebäude auf der Zelle → dessen Abdeckung (schnell); sonst Punktabfrage, nur mit Straße in der Nähe
            var services = world.Services;
            if (services != null)
            {
                var b = world.City.BuildingAt(p);
                bool road = rd <= LandValueRules.RoadZeroDistance;
                float Cov(string kind) => b != null ? services.Coverage(kind, b) : road ? services.CoverageAt(kind, p) : 0f;
                i.Police = Cov(ServiceKinds.Police);
                i.Fire = Cov(ServiceKinds.Fire);
                i.Health = Cov(ServiceKinds.Health);
                i.Education = Cov(ServiceKinds.Education);
                i.Library = Cov(ServiceKinds.Library);
                i.Park = services.CoverageAt(ServiceKinds.Park, p); // Fußweg, braucht keine Straße
                i.Landmark = services.CoverageAt(ServiceKinds.Landmark, p); // Eigene Assets: Wahrzeichen über Radius
            }

            i.Density = LandValueRules.Saturate(units.Sample(p), LandValueRules.DensitySaturation);
            float sw = satWeight.Sample(p);
            if (sw > 0.5f) i.Neighborhood = satSum.Sample(p) / sw;

            var env = world.Environment;
            i.Noise = env.Noise.Sample(p);
            i.Air = env.AirPollution.Sample(p);
            i.Garbage = Math.Clamp(garbage.Sample(p), 0f, 1f);
            if (SafetyPenaltyAt != null) i.Safety = Math.Clamp(SafetyPenaltyAt(p), 0f, 1f); // Sicherheit
            return i;
        }

        static readonly Vector2[] Directions =
        {
            new Vector2(1, 0), new Vector2(0.7071f, 0.7071f), new Vector2(0, 1), new Vector2(-0.7071f, 0.7071f),
            new Vector2(-1, 0), new Vector2(-0.7071f, -0.7071f), new Vector2(0, -1), new Vector2(0.7071f, -0.7071f),
        };
        static readonly float[] ShoreRings = { 12f, 28f, 48f, 72f };

        void CacheTerrain(int index, Vector2 p)
        {
            bool water = world.Terrain.IsWater(p);
            terrainKnown[index] = (byte)(water ? 2 : 1);
            shoreCache[index] = water ? 0f : ShoreFactor(p);
            viewCache[index] = water ? 0f : ViewFactor(p);
        }

        /// <summary>Geländeänderung (Straßenböschung, Terraforming): Zwischenspeicher im Rechteck + Aussichtsring verwerfen.</summary>
        void OnTerrainChanged(Vector2 min, Vector2 max)
        {
            float m = LandValueRules.ViewRingDistance + Layer.CellSize;
            Layer.TryWorldToCell(min - new Vector2(m), out int x0, out int y0);
            Layer.TryWorldToCell(max + new Vector2(m), out int x1, out int y1);
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
            x1 = Math.Min(Layer.Width - 1, x1); y1 = Math.Min(Layer.Height - 1, y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                terrainKnown[y * Layer.Width + x] = 0;
        }

        float ShoreFactor(Vector2 p)
        {
            var t = world.Terrain;
            foreach (float r in ShoreRings)
                foreach (var d in Directions)
                    if (t.IsWater(p + d * r))
                        return LandValueRules.Falloff(r, LandValueRules.ShoreFullDistance, LandValueRules.ShoreZeroDistance);
            return 0f;
        }

        float ViewFactor(Vector2 p)
        {
            var t = world.Terrain;
            float h = MathF.Max(t.HeightAt(p), t.WaterLevel);
            float sum = 0f;
            foreach (var d in Directions) sum += MathF.Max(t.HeightAt(p + d * LandValueRules.ViewRingDistance), t.WaterLevel);
            float above = h - sum / Directions.Length - 2f;
            return Math.Clamp(above / LandValueRules.ViewFullHeight, 0f, 1f);
        }

        float RoadDistance(Vector2 p)
        {
            var seg = world.Roads.FindNearestSegment(p, LandValueRules.RoadZeroDistance);
            if (seg == null || seg.Type.Category != RoadCategory.City) return float.MaxValue;
            return Vector2.Distance(p, seg.Evaluate(seg.ClosestT(p)));
        }

        // ------------------------------------------------------------ Hilfsraster

        void RebuildRoadRasters()
        {
            builtRoadVersion = world.Roads.Version;
            for (int k = 0; k < roadDist.Length; k++) roadDist[k] = float.MaxValue;
            highway.Clear();
            float reach = LandValueRules.RoadZeroDistance;
            float cs = Layer.CellSize;
            foreach (var s in world.Roads.Segments)
            {
                bool city = s.Type.Category == RoadCategory.City;
                float step = city ? cs * 0.5f : 32f;
                int samples = Math.Max(1, (int)MathF.Ceiling(s.Length / step));
                for (int k = 0; k <= samples; k++)
                {
                    var q = s.Evaluate(k / (float)samples);
                    if (!city) { highway.StampMax(q, LandValueRules.HighwayZeroDistance, 1f); continue; }
                    int x0 = Math.Max(0, (int)MathF.Floor((q.X - reach - Layer.Origin.X) / cs));
                    int x1 = Math.Min(Layer.Width - 1, (int)MathF.Floor((q.X + reach - Layer.Origin.X) / cs));
                    int y0 = Math.Max(0, (int)MathF.Floor((q.Y - reach - Layer.Origin.Y) / cs));
                    int y1 = Math.Min(Layer.Height - 1, (int)MathF.Floor((q.Y + reach - Layer.Origin.Y) / cs));
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = MathF.Max(0f, Vector2.Distance(Layer.CellCenter(x, y), q) - s.Type.HalfWidth);
                        int idx = y * Layer.Width + x;
                        if (d < roadDist[idx]) roadDist[idx] = d;
                    }
                }
            }
            externals.Clear();
            if (world.Traffic != null)
                foreach (var n in world.Traffic.ExternalConnections) externals.Add(n.Position);
        }

        void RebuildBuildingRasters()
        {
            jobs.Clear(); units.Clear(); satSum.Clear(); satWeight.Clear(); garbage.Clear();
            var citizens = world.Citizens;
            var garbageLevel = citizens?.GarbageLevel;
            foreach (var b in world.City.Buildings)
            {
                if (!b.IsConstructed) continue;
                if (b.Jobs > 0) jobs.AddLinear(b.Position, LandValueRules.JobRadius, b.Jobs);
                int u = b.Households + b.Jobs;
                if (u > 0) units.AddLinear(b.Position, LandValueRules.DensityRadius, u);
                if (citizens != null)
                {
                    float rs = citizens.ResidentSatisfaction(b.Id), ws = citizens.WorkerSatisfaction(b.Id);
                    if (rs >= 0f && b.Residents > 0)
                    {
                        satSum.AddLinear(b.Position, LandValueRules.NeighborhoodRadius, rs * b.Residents);
                        satWeight.AddLinear(b.Position, LandValueRules.NeighborhoodRadius, b.Residents);
                    }
                    if (ws >= 0f && b.Workers > 0)
                    {
                        satSum.AddLinear(b.Position, LandValueRules.NeighborhoodRadius, ws * b.Workers * 0.5f);
                        satWeight.AddLinear(b.Position, LandValueRules.NeighborhoodRadius, b.Workers * 0.5f);
                    }
                }
                if (garbageLevel != null)
                {
                    float g = Math.Clamp(garbageLevel(b), 0f, 1f);
                    if (g > 0f) garbage.StampMax(b.Position, LandValueRules.GarbageRadius, g);
                }
            }
        }

        // ------------------------------------------------------------ Abfragen (Inspektor/HUD/Overlay)

        /// <summary>Aktueller Bodenwert am Ort 0..1; −1 = noch nicht berechnet bzw. außerhalb.</summary>
        public float ValueAt(Vector2 p)
        {
            if (!Layer.TryWorldToCell(p, out int x, out int y)) return -1f;
            int i = y * Layer.Width + x;
            return known[i] > 0f ? Layer[x, y] : -1f;
        }

        /// <summary>Bodenwert eines Grundstücks: Mittel aus Mitte und vier inneren Eckpunkten (−1 = unbekannt).</summary>
        public float ValueOf(Building b)
        {
            if (b == null) return -1f;
            float sum = 0f;
            int n = 0;
            var side = b.Side * (b.Width * 0.3f);
            var front = b.Front * (b.Depth * 0.3f);
            void Add(Vector2 p) { float v = ValueAt(p); if (v >= 0f) { sum += v; n++; } }
            Add(b.Position);
            Add(b.Position + side + front);
            Add(b.Position + side - front);
            Add(b.Position - side + front);
            Add(b.Position - side - front);
            return n > 0 ? sum / n : -1f;
        }

        /// <summary>Gründe: Beiträge aller Faktoren am Ort (frisch berechnet, Ziel) plus aktueller Wert.</summary>
        public LandValueFactors Explain(Vector2 p)
        {
            var f = LandValueRules.Evaluate(Gather(p, Layer.TryWorldToCell(p, out int x, out int y) ? y * Layer.Width + x : -1));
            f.Current = ValueAt(p);
            return f;
        }

        public LandValueFactors Explain(Building b)
        {
            if (b == null) return new LandValueFactors { Current = -1f };
            var f = Explain(b.Position);
            f.Current = ValueOf(b);
            return f;
        }

        /// <summary>Autobahnnähe 0..1 (Standortwahl Industrie).</summary>
        public float HighwayAccessAt(Vector2 p) => highway.Sample(p);

        /// <summary>Hook für das <see cref="CitySystem"/>: Vorliebe der Bauherren (s. <see cref="LandValueRules.GrowthPreference"/>).</summary>
        public float GrowthPreference(ZoneType zone, Vector2 p) =>
            LandValueRules.GrowthPreference(zone, MathF.Max(0f, ValueAt(p)), HighwayAccessAt(p));

        /// <summary>Hook für die Stadtkasse: Faktor der Grundsteuer je Gebäude (1, solange der Bodenwert unbekannt ist).</summary>
        public float PropertyTaxFactor(Building b)
        {
            float v = ValueOf(b);
            return v < 0f ? 1f : Economy.EconomyRules.PropertyTaxFactor(v);
        }

        // ------------------------------------------------------------ Grobraster

        /// <summary>Einfaches Grobraster mit bilinearer Abtastung (nur intern).</summary>
        sealed partial class CoarseGrid
        {
            readonly float[] v;
            readonly int n;
            readonly float cell;
            readonly Vector2 origin;

            public CoarseGrid(Vector2 origin, float size, float cell)
            {
                this.origin = origin;
                this.cell = cell;
                n = Math.Max(1, (int)MathF.Ceiling(size / cell));
                v = new float[n * n];
            }

            public void Clear() => Array.Clear(v, 0, v.Length);

            void ForEach(Vector2 c, float radius, Action<int, float> act)
            {
                int x0 = Math.Max(0, (int)MathF.Floor((c.X - radius - origin.X) / cell));
                int x1 = Math.Min(n - 1, (int)MathF.Floor((c.X + radius - origin.X) / cell));
                int y0 = Math.Max(0, (int)MathF.Floor((c.Y - radius - origin.Y) / cell));
                int y1 = Math.Min(n - 1, (int)MathF.Floor((c.Y + radius - origin.Y) / cell));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var center = origin + new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);
                    float d = Vector2.Distance(center, c);
                    if (d <= radius) act(y * n + x, 1f - d / radius);
                }
            }

            public void AddLinear(Vector2 c, float radius, float amount) => ForEach(c, radius, (i, f) => v[i] += amount * f);

            public void StampMax(Vector2 c, float radius, float amount) => ForEach(c, radius, (i, f) => { if (amount * f > v[i]) v[i] = amount * f; });

            public float Sample(Vector2 p)
            {
                float fx = (p.X - origin.X) / cell - 0.5f, fy = (p.Y - origin.Y) / cell - 0.5f;
                int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
                float tx = fx - x0, ty = fy - y0;
                float a = At(x0, y0), b = At(x0 + 1, y0), c = At(x0, y0 + 1), d = At(x0 + 1, y0 + 1);
                return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
            }

            float At(int x, int y)
            {
                x = Math.Clamp(x, 0, n - 1);
                y = Math.Clamp(y, 0, n - 1);
                return v[y * n + x];
            }
        }
    }
}
