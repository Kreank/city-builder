using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Abwasser-Bilanz und Wasserqualität (G3 erweitert, <c>world.WaterQuality</c>, alle 30 min nach dem <see cref="UtilitySystem"/>).
    /// <para>
    /// <b>Abwasser je Kanalnetz:</b> abgenommene Menge (<see cref="UtilityNetwork.Delivered"/>) wird zuerst von Kläranlagen
    /// geklärt (bis zu ihrer Leistung, anteilig), der Rest fließt ungeklärt über die Auslässe ins Gewässer (anteilig).
    /// </para>
    /// <para>
    /// <b>Wasserverschmutzung</b> (<see cref="Pollution"/>, <see cref="GridLayer"/> im Umwelt-Gitter, 0 = sauber, ab 1 stark):
    /// Auslässe geben je Durchlauf <see cref="PollutionPerCubicMeter"/> × m³/h auf die Wasserzellen im Umkreis
    /// <see cref="OutletRadius"/>. Danach Transport <b>nur flussabwärts</b> (<see cref="TerrainMap.FlowDirection"/>, Hauptachse
    /// des Gitters): <see cref="AdvectShare"/> wandert zur nächsten Wasserzelle stromab (gerade, sonst schräg), etwas
    /// <see cref="LateralShare"/> quer über die Flussbreite; nie stromauf. Selbstreinigung <see cref="SelfCleaning"/> je Durchlauf.
    /// Land hat keine Verschmutzung. Läuft nur, solange es Verschmutzung oder Auslässe gibt.
    /// </para>
    /// <para>
    /// <b>Folgen:</b> Pumpen saugen das Wasser an ihrem Ufer an (<see cref="PumpQuality"/> = 1 − Verschmutzung im Ansaugbereich),
    /// je Wassernetz mischt sich die Qualität nach Leistung (Brunnen = 1) → <see cref="DrinkingWaterQuality"/> für das
    /// Bürgersystem (Gesundheit). Bodenwert am Ufer: <see cref="PollutionNear"/>.
    /// </para>
    /// </summary>
    public sealed partial class WaterQualitySystem : ISimulationSystem
    {
        public int TickInterval => 5;

        /// <summary>Verschmutzung je Durchlauf und m³/h ungeklärtem Abwasser (auf die Wasserzellen am Auslass verteilt).</summary>
        public float PollutionPerCubicMeter { get; set; } = 0.015f;
        public const float OutletRadius = 40f;
        /// <summary>Anteil, der je Durchlauf zur nächsten Zelle stromab fließt.</summary>
        public const float AdvectShare = 0.6f;
        /// <summary>Anteil je Seite, der sich je Durchlauf quer zum Fluss verteilt.</summary>
        public const float LateralShare = 0.04f;
        /// <summary>Selbstreinigung je Durchlauf.</summary>
        public const float SelfCleaning = 0.01f;
        /// <summary>Ansaugbereich einer Pumpe über ihre halbe Grundfläche hinaus (wie die Platzregel „Wasser in Reichweite“).</summary>
        public const float IntakeReach = 20f;
        /// <summary>Unterhalb dieser Trinkwasserqualität sorgen sich Bürger („Wasser schmeckt komisch“).</summary>
        public const float BadDrinkingWater = 0.8f;

        readonly UtilitySystem utilities;
        readonly TerrainMap terrain;
        bool[] water;
        int waterVersion = -1;
        float[] scratch;
        bool active;

        readonly Dictionary<int, float> treated = new Dictionary<int, float>();
        readonly Dictionary<int, float> untreated = new Dictionary<int, float>();
        readonly Dictionary<int, float> pumpQuality = new Dictionary<int, float>();
        readonly Dictionary<int, float> networkQuality = new Dictionary<int, float>();

        /// <summary>Wasserverschmutzung je Zelle (0 = sauber). Gleiches Gitter wie Lärm/Abgas.</summary>
        public GridLayer Pollution { get; }
        /// <summary>Geklärtes bzw. ungeklärt eingeleitetes Abwasser der ganzen Stadt in m³/h (letzter Durchlauf).</summary>
        public float TotalTreated { get; private set; }
        public float TotalUntreated { get; private set; }
        /// <summary>Wird erhöht, wenn sich Pumpen-/Netzqualität geändert hat.</summary>
        public int Version { get; private set; }

        public WaterQualitySystem(UtilitySystem utilities, TerrainMap terrain, Vector2 origin, float sizeMeters, float cellSize)
        {
            this.utilities = utilities ?? throw new ArgumentNullException(nameof(utilities));
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            int n = Math.Max(1, (int)MathF.Ceiling(sizeMeters / cellSize));
            Pollution = new GridLayer("Wasserverschmutzung", origin, n, n, cellSize);
        }

        // ------------------------------------------------------------ Abfragen

        /// <summary>Verschmutzung am Punkt (0 an Land).</summary>
        public float PollutionAt(Vector2 p) => Pollution.Sample(p);

        /// <summary>Wasserqualität am Punkt 0..1 (1 = sauber).</summary>
        public float QualityAt(Vector2 p) => Math.Clamp(1f - PollutionAt(p), 0f, 1f);

        /// <summary>Stärkste Verschmutzung im Umkreis (Ringe à 8 Richtungen) – für den Bodenwert am Ufer.</summary>
        public float PollutionNear(Vector2 p, float radius)
        {
            if (!active && TotalUntreated <= 0f) return 0f;
            float best = Pollution.Sample(p);
            float step = Pollution.CellSize;
            for (float r = step; r <= radius + 1e-3f; r += step)
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI / 4f;
                    best = MathF.Max(best, Pollution.Sample(p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r));
                }
            return best;
        }

        /// <summary>Qualität des angesaugten Wassers einer Pumpe 0..1 (andere Bauwerke: 1).</summary>
        /// <summary>Umwelt (Etappe 6, Hook): Qualität des Grundwassers an einem Brunnen 0..1 (null = 1). Gesetzt vom EcologySystem.</summary>
        public Func<UtilityFacility, float> WellQuality { get; set; }

        public float PumpQuality(UtilityFacility f) => f != null && pumpQuality.TryGetValue(f.Id, out float q) ? q : 1f;

        /// <summary>
        /// Trinkwasserqualität eines Gebäudes 0..1: Mischung seines Wassernetzes nach Leistung (Pumpe = Flusswasser,
        /// Brunnen = 1). Ohne Wasserversorgung 1 (das Fehlen wirkt über die Versorgung, nicht doppelt).
        /// </summary>
        public float DrinkingWaterQuality(Building b)
        {
            if (b == null || !utilities.IsSupplied(b, UtilityKind.Water)) return 1f;
            var net = utilities.NetworkOf(b, UtilityKind.Water);
            return net != null && networkQuality.TryGetValue(net.Id, out float q) ? q : 1f;
        }

        /// <summary>Geklärte Menge einer Kläranlage in m³/h.</summary>
        public float TreatedBy(UtilityFacility f) => f != null && treated.TryGetValue(f.Id, out float v) ? v : 0f;

        /// <summary>Ungeklärt eingeleitete Menge eines Auslasses in m³/h.</summary>
        public float DischargeOf(UtilityFacility f) => f != null && untreated.TryGetValue(f.Id, out float v) ? v : 0f;

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld world)
        {
            if (water == null || waterVersion != terrain.Version) RebuildWaterMask();
            ComputeDischarge();
            if (active || TotalUntreated > 0f)
            {
                Emit();
                Advect();
                active = Pollution.MaxValue() > 1e-4f || TotalUntreated > 0f;
                if (!active) Pollution.Clear();
            }
            UpdatePumps();
        }

        /// <summary>Für Tests: sofort einen Durchlauf rechnen.</summary>
        public void ForceTick() => Tick(null);

        void RebuildWaterMask()
        {
            int n = Pollution.Width * Pollution.Height;
            if (water == null || water.Length != n) water = new bool[n];
            for (int y = 0; y < Pollution.Height; y++)
            for (int x = 0; x < Pollution.Width; x++)
                water[y * Pollution.Width + x] = terrain.IsWater(Pollution.CellCenter(x, y));
            waterVersion = terrain.Version;
        }

        void ComputeDischarge()
        {
            treated.Clear();
            untreated.Clear();
            TotalTreated = TotalUntreated = 0f;
            var nets = utilities.Networks(UtilityKind.Sewage);
            if (nets.Count == 0) return;
            var plants = new List<UtilityFacility>();
            var outlets = new List<UtilityFacility>();
            foreach (var f in utilities.Facilities)
                if (f.Kind == UtilityKind.Sewage) (f.Type.Untreated ? outlets : plants).Add(f);
            plants.Sort((a, b) => a.Id.CompareTo(b.Id));
            outlets.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var n in nets)
            {
                if (n.Delivered <= 0f) continue;
                float plantCap = 0f, outletCap = 0f;
                foreach (var f in plants) if (f.NetworkId == n.Id) plantCap += f.Output;
                foreach (var f in outlets) if (f.NetworkId == n.Id) outletCap += f.Output;
                float clean = MathF.Min(n.Delivered, plantCap);
                float dirty = n.Delivered - clean;
                if (plantCap > 0f)
                    foreach (var f in plants) if (f.NetworkId == n.Id) treated[f.Id] = clean * f.Output / plantCap;
                if (outletCap > 0f && dirty > 0f)
                    foreach (var f in outlets) if (f.NetworkId == n.Id) untreated[f.Id] = dirty * f.Output / outletCap;
                TotalTreated += clean;
                TotalUntreated += outletCap > 0f ? dirty : 0f;
            }
        }

        void Emit()
        {
            var cells = Pollution.RawValues;
            var idx = new List<int>();
            foreach (var kv in SortedKeys(untreated))
            {
                var f = utilities.GetFacility(kv);
                float amount = untreated[kv] * PollutionPerCubicMeter;
                if (f == null || amount <= 0f) continue;
                idx.Clear();
                CellsInRadius(f.Position, OutletRadius + f.Radius, idx, waterOnly: true);
                if (idx.Count == 0) continue;
                float share = amount / idx.Count;
                foreach (int i in idx) cells[i] += share;
            }
            Pollution.MarkChanged();
        }

        static List<int> SortedKeys(Dictionary<int, float> d)
        {
            var keys = new List<int>(d.Keys);
            keys.Sort();
            return keys;
        }

        /// <summary>Transport flussabwärts + quer, Selbstreinigung. Nie stromauf.</summary>
        void Advect()
        {
            int w = Pollution.Width, h = Pollution.Height;
            var cells = Pollution.RawValues;
            if (scratch == null || scratch.Length != cells.Length) scratch = new float[cells.Length];
            Array.Copy(cells, scratch, cells.Length);
            var flow = terrain.FlowDirection;
            bool alongY = MathF.Abs(flow.Y) >= MathF.Abs(flow.X);
            int sx = alongY ? 0 : (flow.X >= 0f ? 1 : -1), sy = alongY ? (flow.Y >= 0f ? 1 : -1) : 0;
            // quer zur Fließrichtung
            int lx = alongY ? 1 : 0, ly = alongY ? 0 : 1;

            bool Water(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && water[y * w + x];
            bool Outside(int x, int y) => x < 0 || y < 0 || x >= w || y >= h;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float v = scratch[i];
                if (v <= 0f) continue;
                if (!water[i]) { cells[i] -= v; continue; } // an Land keine Verschmutzung (z. B. nach Aufschütten)

                // stromab: gerade, sonst schräg (beide Seiten, wenn möglich), sonst bleibt es stehen
                float move = v * AdvectShare;
                int dx = x + sx, dy = y + sy;
                if (Outside(dx, dy)) cells[i] -= move; // verlässt die Karte
                else if (water[dy * w + dx]) { cells[i] -= move; cells[dy * w + dx] += move; }
                else
                {
                    bool a = Water(dx + lx, dy + ly), b = Water(dx - lx, dy - ly);
                    if (a && b)
                    {
                        cells[i] -= move;
                        cells[(dy + ly) * w + dx + lx] += move * 0.5f;
                        cells[(dy - ly) * w + dx - lx] += move * 0.5f;
                    }
                    else if (a) { cells[i] -= move; cells[(dy + ly) * w + dx + lx] += move; }
                    else if (b) { cells[i] -= move; cells[(dy - ly) * w + dx - lx] += move; }
                }

                // quer über die Flussbreite
                float side = v * LateralShare;
                if (Water(x + lx, y + ly)) { cells[i] -= side; cells[(y + ly) * w + x + lx] += side; }
                if (Water(x - lx, y - ly)) { cells[i] -= side; cells[(y - ly) * w + x - lx] += side; }
            }
            float keep = 1f - SelfCleaning;
            for (int i = 0; i < cells.Length; i++)
            {
                float v = cells[i] * keep;
                cells[i] = v < 1e-6f ? 0f : MathF.Min(v, 4f);
            }
            Pollution.MarkChanged();
        }

        void UpdatePumps()
        {
            bool changed = false;
            var seen = new HashSet<int>();
            var sum = new Dictionary<int, float>();
            var weight = new Dictionary<int, float>();
            var idx = new List<int>();
            var cells = Pollution.RawValues;
            var sorted = new List<UtilityFacility>();
            foreach (var f in utilities.Facilities) if (f.Kind == UtilityKind.Water) sorted.Add(f);
            sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var f in sorted)
            {
                float q = 1f;
                if (f.Type.NeedsWater)
                {
                    float worst = 0f;
                    if (active)
                    {
                        idx.Clear();
                        CellsInRadius(f.Position, MathF.Max(f.Type.Width, f.Type.Depth) * 0.5f + IntakeReach, idx, waterOnly: true);
                        foreach (int i in idx) worst = MathF.Max(worst, cells[i]);
                    }
                    q = Math.Clamp(1f - worst, 0f, 1f);
                    seen.Add(f.Id);
                    if (!pumpQuality.TryGetValue(f.Id, out float old) || MathF.Abs(old - q) > 1e-3f) changed = true;
                    pumpQuality[f.Id] = q;
                }
                // --- Umwelt (Etappe 6): Brunnen saugen Grundwasser an (Deponie/Industrie verschmutzen es); 1 = wie bisher
                else if (WellQuality != null)
                {
                    float wq = Math.Clamp(WellQuality(f), 0f, 1f);
                    if (wq < 1f || pumpQuality.ContainsKey(f.Id))
                    {
                        q = wq;
                        seen.Add(f.Id);
                        if (!pumpQuality.TryGetValue(f.Id, out float old) || MathF.Abs(old - q) > 1e-3f) changed = true;
                        pumpQuality[f.Id] = q;
                    }
                }
                // --- Ende Umwelt
                if (f.NetworkId < 0 || f.Output <= 0f) continue;
                sum.TryGetValue(f.NetworkId, out float s);
                weight.TryGetValue(f.NetworkId, out float wsum);
                sum[f.NetworkId] = s + q * f.Output;
                weight[f.NetworkId] = wsum + f.Output;
            }
            var stale = new List<int>();
            foreach (int id in pumpQuality.Keys) if (!seen.Contains(id)) stale.Add(id);
            foreach (int id in stale) { pumpQuality.Remove(id); changed = true; }

            if (networkQuality.Count != weight.Count) changed = true;
            networkQuality.Clear();
            foreach (var kv in weight)
                networkQuality[kv.Key] = kv.Value > 0f ? sum[kv.Key] / kv.Value : 1f;
            if (changed) Version++;
        }

        void CellsInRadius(Vector2 center, float radius, List<int> into, bool waterOnly)
        {
            var g = Pollution;
            int x0 = Math.Max(0, (int)MathF.Floor((center.X - radius - g.Origin.X) / g.CellSize));
            int x1 = Math.Min(g.Width - 1, (int)MathF.Floor((center.X + radius - g.Origin.X) / g.CellSize));
            int y0 = Math.Max(0, (int)MathF.Floor((center.Y - radius - g.Origin.Y) / g.CellSize));
            int y1 = Math.Min(g.Height - 1, (int)MathF.Floor((center.Y + radius - g.Origin.Y) / g.CellSize));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * g.Width + x;
                if (waterOnly && !water[i]) continue;
                if (Vector2.Distance(g.CellCenter(x, y), center) <= radius) into.Add(i);
            }
        }
    }
}
