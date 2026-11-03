using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Environment
{
    public enum NoiseBarrierError
    {
        None,
        TooShort,
        TooLong,
        OutOfWorld,
        OnWater,
        /// <summary>Liegt auf Fahrbahn/Gehweg einer ebenerdigen Straße.</summary>
        OnRoad,
        /// <summary>Kreuzt ein Bauwerk (Dienst, Versorger, Depot) oder eine andere Wand.</summary>
        OverlapsStructure,
        /// <summary>Kreuzt ein Gebäude (wird nicht abgerissen – wie bei Strommasten).</summary>
        OverlapsBuilding,
        InsufficientFunds,
    }

    /// <summary>Ein gerades Stück Lärmschutzwand (Linienbauwerk, Modell <c>Models/Infrastructure/NoiseBarrier.fbx</c> als 16-m-Kacheln).</summary>
    public sealed class NoiseBarrier
    {
        public int Id { get; internal set; }
        public Vector2 A { get; internal set; }
        public Vector2 B { get; internal set; }
        public float Length => Vector2.Distance(A, B);
        public Vector2 Direction => Length > 1e-4f ? Vector2.Normalize(B - A) : Vector2.UnitX;
        /// <summary>Normale (links der Richtung A → B).</summary>
        public Vector2 Normal => new Vector2(-Direction.Y, Direction.X);
        public Vector2 Mid => (A + B) * 0.5f;

        StructureBlock block;
        /// <summary>Bauwerks-Sperre: Straßen dürfen die Wand nicht durchqueren.</summary>
        internal StructureBlock Block => block ??= new StructureBlock("barrier", Id, "Lärmschutzwand",
            new Footprint(Mid, Normal, Length, NoiseBarrierSystem.Thickness), NoiseBarrierSystem.WallHeight);

        public float DistanceTo(Vector2 p) => NoiseBarrierSystem.SegmentDistance(p, A, B);
        public override string ToString() => $"Lärmschutzwand #{Id} ({Length:0} m)";
    }

    /// <summary>Geplantes Wandstück (Vorschau) – erst <see cref="NoiseBarrierSystem.Build"/> baut es.</summary>
    public sealed class NoiseBarrierPlan
    {
        public Vector2 A, B;
        public float Cost;
        public NoiseBarrierError Error;
        public bool IsValid => Error == NoiseBarrierError.None;
        public float Length => Vector2.Distance(A, B);
    }

    /// <summary>
    /// Lärmschutzwände (Eigene Assets): Linienbauwerke entlang von Straßen, frei als gerade Stücke gesetzt (Werkzeug wie
    /// Leitungen, Einrasten neben dem Straßenrand über <see cref="SnapToRoad"/>). Sie <b>dämpfen den Lärm dahinter</b>: Das
    /// <see cref="NoiseSystem"/> fragt je Quelle → Rasterzelle <see cref="Attenuation"/> ab – kreuzt die Sichtlinie eine Wand,
    /// kommt nur ein Rest an (direkt dahinter <see cref="ShadowResidual"/>, mit dem Abstand wieder mehr). Vor der Wand ändert
    /// sich nichts. Kosten je Meter (Stadtkasse „Straßenbau“), Unterhalt je Woche, Abriss 25 % zurück; Straßen dürfen Wände
    /// nicht durchqueren (Bauwerks-Sperre, Art „barrier“), auf ihren Zellen wächst nichts. Ohne Wände ist alles bitgleich wie vorher.
    /// </summary>
    public sealed partial class NoiseBarrierSystem : ISimulationSystem, Economy.IUpkeepSource
    {
        public const float CostPerMeter = 60f;
        /// <summary>Unterhalt je Meter und Woche.</summary>
        public const float UpkeepPerMeterWeek = 0.35f;
        public const float MinLength = 4f, MaxLength = 400f;
        /// <summary>Höhe (Modell 4 m) und Dicke der Grundfläche.</summary>
        public const float WallHeight = 4f, Thickness = 0.6f;
        /// <summary>Abstand der Wand vom Straßenrand (Gehweg-Außenkante) beim Einrasten.</summary>
        public const float RoadGap = 1f;
        /// <summary>Einrasten an Straßen bis so weit vom Rand.</summary>
        public const float SnapReach = 24f;
        /// <summary>Lärm-Rest direkt hinter der Wand (0,3 = −70 %) …</summary>
        public const float ShadowResidual = 0.3f;
        /// <summary>… und so weit hinter der Wand (m) wieder voller Rest <see cref="FarResidual"/>.</summary>
        public const float ShadowLength = 160f, FarResidual = 0.7f;

        readonly RoadNetwork roads;
        readonly TerrainMap terrain;
        readonly CitySystem city;
        readonly Vector2 min, max;
        readonly Core.StableMap<int, NoiseBarrier> barriers = new Core.StableMap<int, NoiseBarrier>();
        int nextId = 1;

        /// <summary>Wird bei jeder Änderung erhöht (Darstellung, Lärm-Neuberechnung).</summary>
        public int Version { get; private set; }
        public IEnumerable<NoiseBarrier> Barriers => barriers.Values;
        public int Count => barriers.Count;
        public float TotalLength { get { float s = 0f; foreach (var b in barriers.Values) s += b.Length; return s; } }

        public event Action<NoiseBarrier> BarrierAdded;
        public event Action<NoiseBarrier> BarrierRemoved;

        /// <summary>Stadtkasse (null = kostenlos, Tests). Gesetzt in <see cref="SimulationWorld.CreateDefault"/>.</summary>
        public Economy.IConstructionBudget Budget { get; set; }

        /// <summary>Hängt nur ab, wenn es nichts zu tun gibt – die Arbeit macht das <see cref="NoiseSystem"/> über <see cref="Attenuation"/>.</summary>
        public int TickInterval => SimulationClock.TicksPerDay;
        public void Tick(SimulationWorld world) { }

        public NoiseBarrierSystem(RoadNetwork roads, TerrainMap terrain, CitySystem city, Vector2 min, Vector2 max)
        {
            this.roads = roads ?? throw new ArgumentNullException(nameof(roads));
            this.terrain = terrain;
            this.city = city;
            this.min = min;
            this.max = max;
            roads.RegisterStructures(StructureBlocks);
            if (city != null)
            {
                var previous = city.LotBlocker;
                city.LotBlocker = p => (previous != null && previous(p)) || BarrierNear(p, ZoneGrid.CellSize * 0.5f) != null;
            }
        }

        IEnumerable<StructureBlock> StructureBlocks()
        {
            foreach (var b in barriers.Values) yield return b.Block;
        }

        public NoiseBarrier Get(int id) => barriers.TryGetValue(id, out var b) ? b : null;

        /// <summary>Nächste Wand im Abstand ≤ margin (oder null).</summary>
        public NoiseBarrier BarrierNear(Vector2 p, float margin)
        {
            NoiseBarrier best = null;
            float bestD = margin;
            foreach (var b in barriers.Values)
            {
                float d = b.DistanceTo(p);
                if (d <= bestD) { bestD = d; best = b; }
            }
            return best;
        }

        // ------------------------------------------------------------ Kosten

        public string Name => "Lärmschutz";
        public double WeeklyUpkeep() => TotalLength * UpkeepPerMeterWeek;
        public static float CostOf(float length) => length * CostPerMeter;

        // ------------------------------------------------------------ Einrasten

        /// <summary>
        /// Rastet einen Punkt neben der nächsten ebenerdigen Straße ein (Rand ≤ <see cref="SnapReach"/>): auf der Seite des
        /// Punktes, <see cref="RoadGap"/> hinter der Gehweg-Außenkante. Liefert false ohne Straße in der Nähe.
        /// </summary>
        public bool SnapToRoad(Vector2 p, out Vector2 snapped, out Vector2 tangent)
        {
            snapped = p;
            tangent = Vector2.UnitX;
            RoadSegment best = null;
            float bestD = SnapReach, bestT = 0f;
            foreach (var s in roads.Segments)
            {
                float reach = s.Type.HalfWidth + SnapReach;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                if (p.X < lo.X - reach || p.Y < lo.Y - reach || p.X > hi.X + reach || p.Y > hi.Y + reach) continue;
                float t = s.ClosestT(p);
                if (MathF.Abs(s.ElevationAt(t)) > RoadFront.AtGradeElevation) continue;
                float d = Vector2.Distance(p, s.Evaluate(t)) - s.Type.HalfWidth;
                if (d < bestD || (d == bestD && best != null && s.Id < best.Id)) { bestD = d; best = s; bestT = t; }
            }
            if (best == null) return false;
            var c = best.Evaluate(bestT);
            var right = best.Right(bestT);
            float side = Vector2.Dot(p - c, right) >= 0f ? 1f : -1f;
            snapped = c + right * side * (best.Type.HalfWidth + RoadGap);
            tangent = best.Tangent(bestT);
            return true;
        }

        // ------------------------------------------------------------ Planen/Bauen/Abreißen

        public NoiseBarrierPlan Plan(Vector2 a, Vector2 b)
        {
            var plan = new NoiseBarrierPlan { A = a, B = b };
            float len = plan.Length;
            plan.Cost = CostOf(len);
            plan.Error = Check(a, b, len);
            if (plan.Error == NoiseBarrierError.None && Budget != null && !Budget.CanAfford(plan.Cost))
                plan.Error = NoiseBarrierError.InsufficientFunds;
            return plan;
        }

        NoiseBarrierError Check(Vector2 a, Vector2 b, float len)
        {
            if (len < MinLength) return NoiseBarrierError.TooShort;
            if (len > MaxLength) return NoiseBarrierError.TooLong;
            if (!InWorld(a) || !InWorld(b)) return NoiseBarrierError.OutOfWorld;
            int samples = Math.Max(2, (int)MathF.Ceiling(len / 2f));
            var pts = new Vector2[samples + 1];
            for (int i = 0; i <= samples; i++) pts[i] = Vector2.Lerp(a, b, i / (float)samples);
            if (terrain != null)
                foreach (var p in pts) if (terrain.IsWater(p)) return NoiseBarrierError.OnWater;
            // Fahrbahn inkl. Gehweg (ebenerdig) – Brücken/Tunnel gehen drüber/drunter
            foreach (var s in roads.Segments)
            {
                float reach = s.Type.HalfWidth + 1f;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                foreach (var p in pts)
                {
                    if (p.X < lo.X - reach || p.Y < lo.Y - reach || p.X > hi.X + reach || p.Y > hi.Y + reach) continue;
                    float t = s.ClosestT(p);
                    if (MathF.Abs(s.ElevationAt(t)) > RoadFront.AtGradeElevation) continue;
                    if (Vector2.Distance(p, s.Evaluate(t)) < s.Type.HalfWidth + 0.2f) return NoiseBarrierError.OnRoad;
                }
            }
            foreach (var blk in roads.Structures)
            {
                if (blk.Kind == "barrier") continue; // Wände dürfen sich an den Enden berühren (Kette)
                foreach (var p in pts) if (blk.Footprint.Contains(p, Thickness * 0.5f)) return NoiseBarrierError.OverlapsStructure;
            }
            if (city != null)
                foreach (var bld in city.Buildings)
                {
                    var fp = new Footprint(bld.Position, bld.Front, bld.Width, bld.Depth);
                    if (Vector2.Distance(fp.Center, (a + b) * 0.5f) > fp.Radius + len * 0.5f + 1f) continue;
                    foreach (var p in pts) if (fp.Contains(p, 0.2f)) return NoiseBarrierError.OverlapsBuilding;
                }
            return NoiseBarrierError.None;
        }

        public NoiseBarrier Build(NoiseBarrierPlan plan)
        {
            if (plan == null || !plan.IsValid) return null;
            if (Budget != null && !Budget.CanAfford(plan.Cost)) return null;
            Budget?.Spend(Economy.BudgetCategory.RoadConstruction, plan.Cost);
            var b = new NoiseBarrier { Id = nextId++, A = plan.A, B = plan.B };
            barriers[b.Id] = b;
            Version++;
            BarrierAdded?.Invoke(b);
            return b;
        }

        /// <summary>Kurzform für Werkzeuge/Tests.</summary>
        public NoiseBarrier TryBuild(Vector2 a, Vector2 b) => Build(Plan(a, b));

        /// <summary>Spieler-Abriss: 25 % der Baukosten zurück.</summary>
        public bool Demolish(int id)
        {
            if (!barriers.TryGetValue(id, out var b)) return false;
            barriers.Remove(id);
            Version++;
            Budget?.Refund(CostOf(b.Length) * Economy.EconomyRules.DemolitionRefundShare);
            BarrierRemoved?.Invoke(b);
            return true;
        }

        // ------------------------------------------------------------ Lärm

        /// <summary>Liegt irgendeine Wand im Kreis (Mitte, Radius)? Schnelle Vorprüfung für das <see cref="NoiseSystem"/>.</summary>
        public bool AnyNear(Vector2 center, float radius)
        {
            foreach (var b in barriers.Values)
                if (b.DistanceTo(center) <= radius) return true;
            return false;
        }

        /// <summary>
        /// Anteil des Lärms, der von <paramref name="source"/> bei <paramref name="receiver"/> ankommt (1 = ungedämpft):
        /// kreuzt die Sichtlinie eine Wand, bleibt <see cref="ShadowResidual"/> direkt dahinter, mit dem Abstand hinter der
        /// Wand linear bis <see cref="FarResidual"/> nach <see cref="ShadowLength"/> m. Mehrere Wände: die stärkste zählt.
        /// </summary>
        public float Attenuation(Vector2 source, Vector2 receiver)
        {
            float best = 1f;
            foreach (var b in barriers.Values)
            {
                if (!SegmentsIntersect(source, receiver, b.A, b.B)) continue;
                float behind = b.DistanceTo(receiver);
                float f = ShadowResidual + (FarResidual - ShadowResidual) * Math.Clamp(behind / ShadowLength, 0f, 1f);
                if (f < best) best = f;
            }
            return best;
        }

        // ------------------------------------------------------------ Geometrie

        internal static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float l2 = ab.LengthSquared();
            float t = l2 > 1e-8f ? Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>Schneiden sich die Strecken p1-p2 und q1-q2 (echt oder an einem Ende)?</summary>
        public static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            var r = p2 - p1;
            var s = q2 - q1;
            float denom = Cross(r, s);
            var qp = q1 - p1;
            if (MathF.Abs(denom) < 1e-8f) return false; // parallel → kein Schatten
            float t = Cross(qp, s) / denom;
            float u = Cross(qp, r) / denom;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        bool InWorld(Vector2 p) => p.X >= min.X && p.Y >= min.Y && p.X <= max.X && p.Y <= max.Y;
    }
}
