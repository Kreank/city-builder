using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Tiles
{
    /// <summary>Kacheln: alle Zahlen an einem Ort (Platzhalter fürs Balancing).</summary>
    public static class TileRules
    {
        /// <summary>Preis der ersten zugekauften Kachel (€).</summary>
        public const double BasePrice = 150000.0;
        /// <summary>Jede weitere Kachel kostet so viel mehr (€) – wie in Cities: Skylines steigt der Preis mit der Größe der Stadt.</summary>
        public const double PriceStep = 90000.0;

        /// <summary>Preis der nächsten Kachel, wenn schon <paramref name="purchases"/> zugekauft wurden.</summary>
        public static double Price(int purchases) => BasePrice + PriceStep * Math.Max(0, purchases);

        /// <summary>Kacheln je Seite: round(Kartengröße / Kachelgröße), mindestens 1.</summary>
        public static int TilesPerSide(float sizeMeters, float tileSize)
        {
            if (!(tileSize > 0f)) return 1;
            return Math.Max(1, (int)MathF.Floor(sizeMeters / tileSize + 0.5f));
        }
    }

    public enum TileState { Owned, Purchasable, Locked }

    public enum TilePurchaseError { None, OutOfMap, AlreadyOwned, NotAdjacent, InsufficientFunds }

    public sealed class TilePurchasePlan
    {
        public int X, Y;
        public double Cost;
        public TilePurchaseError Error;
        public bool IsValid => Error == TilePurchaseError.None;
    }

    /// <summary>Kurzbeschreibung einer Kachel fürs Kauf-Fenster (nur lesend aus Gelände/Ressourcen/Netzen berechnet).</summary>
    public sealed class TileInfo
    {
        public int X, Y;
        public string Name;
        public float WaterShare, MinHeight, MaxHeight, SteepShare, ForestShare;
        /// <summary>Ergiebigste Vorkommen (Id, Anzeigename, mittlere Güte 0..1), absteigend.</summary>
        public readonly List<(string Id, string Name, float Quality)> Deposits = new List<(string, string, float)>();
        public bool Highway, Railway;
    }

    /// <summary>
    /// <b>Kacheln</b> (Spielerentscheidung Kartengröße): Die Stadtkarte besteht aus N × N Kacheln (Standard 2 048 m). Gebaut werden
    /// darf nur auf gekauften Kacheln; zugekauft werden Nachbarkacheln (4er-Nachbarschaft) über die Stadtkasse, jede weitere
    /// teurer. Bei einer Kachel (2-km-Karte, Tests, alte Stände) ist alles gekauft und nichts ändert sich.
    /// <para>
    /// Zentrale Prüfung: das System ist <see cref="RoadNetwork.BuildArea"/> von Straßen- und Gleisnetz; alle Plan-Funktionen fragen
    /// über <see cref="BuildArea"/> nach. Welt-Infrastruktur (Autobahn, Fernbahn) baut die Welt mit <see cref="Unrestricted"/>.
    /// </para>
    /// </summary>
    public sealed partial class TileSystem : IBuildArea
    {
        readonly bool[] owned;
        int suspend;

        public TileSystem(Vector2 origin, float sizeMeters, float tileSize)
        {
            Origin = origin;
            SizeMeters = sizeMeters;
            TilesPerSide = TileRules.TilesPerSide(sizeMeters, tileSize);
            TileSize = sizeMeters / TilesPerSide;
            owned = new bool[TilesPerSide * TilesPerSide];
            ResetToStart();
        }

        /// <summary>Untere linke Ecke der Karte.</summary>
        public Vector2 Origin { get; }
        public float SizeMeters { get; }
        /// <summary>Kacheln je Seite (N).</summary>
        public int TilesPerSide { get; }
        /// <summary>Tatsächliche Kantenlänge einer Kachel (Karte / N).</summary>
        public float TileSize { get; }
        public int Count => owned.Length;

        /// <summary>Gibt es überhaupt etwas zu kaufen (mehr als eine Kachel)?</summary>
        public bool Restricted => TilesPerSide > 1;
        /// <summary>Wird gerade geprüft (nicht während die Welt ihre Infrastruktur baut)?</summary>
        public bool Enforced => Restricted && suspend == 0;

        /// <summary>Erhöht sich bei jedem Kauf (Darstellung, Caches der aktiven Zellen).</summary>
        public int Version { get; private set; }
        /// <summary>Anzahl zugekaufter Kacheln (ohne Startkacheln) – bestimmt den nächsten Preis.</summary>
        public int Purchases { get; private set; }
        /// <summary>Summe aller Kaufpreise (€).</summary>
        public double TotalSpent { get; private set; }
        public int OwnedCount { get { int n = 0; foreach (bool o in owned) if (o) n++; return n; } }

        /// <summary>Kasse (gesetzt in <c>CreateDefault</c>; null = kostenlos).</summary>
        public IConstructionBudget Budget { get; set; }

        /// <summary>Eine Kachel wurde gekauft (x, y).</summary>
        public event Action<int, int> Purchased;

        /// <summary>Startzustand: die Kachel(n) um die Kartenmitte (ungerades N: die mittlere, gerades N: die mittleren 2 × 2).</summary>
        void ResetToStart()
        {
            Array.Clear(owned, 0, owned.Length);
            if (!Restricted) { owned[0] = true; return; }
            var mid = Origin + new Vector2(SizeMeters * 0.5f);
            foreach (var d in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) })
                if (TryTileAt(mid + d, out int x, out int y)) owned[y * TilesPerSide + x] = true;
            Purchases = 0;
            TotalSpent = 0.0;
        }

        // ------------------------------------------------------------ Abfragen

        public bool InMap(int x, int y) => x >= 0 && y >= 0 && x < TilesPerSide && y < TilesPerSide;

        /// <summary>Kachel unter einem Punkt (false außerhalb der Karte).</summary>
        public bool TryTileAt(Vector2 p, out int x, out int y)
        {
            x = (int)MathF.Floor((p.X - Origin.X) / TileSize);
            y = (int)MathF.Floor((p.Y - Origin.Y) / TileSize);
            return InMap(x, y);
        }

        public bool IsOwned(int x, int y) => InMap(x, y) && owned[y * TilesPerSide + x];

        /// <summary>Liegt der Punkt auf gekauftem Land? Außerhalb der Karte zählt die nächste Randkachel.</summary>
        public bool OwnedAt(Vector2 p)
        {
            if (!Restricted) return true;
            int x = Math.Clamp((int)MathF.Floor((p.X - Origin.X) / TileSize), 0, TilesPerSide - 1);
            int y = Math.Clamp((int)MathF.Floor((p.Y - Origin.Y) / TileSize), 0, TilesPerSide - 1);
            return owned[y * TilesPerSide + x];
        }

        /// <summary><see cref="IBuildArea"/>: baubar, wenn nicht geprüft wird oder die Kachel gekauft ist.</summary>
        public bool IsBuildable(Vector2 p) => !Enforced || OwnedAt(p);

        /// <summary>Liegt das achsparallele Rechteck ganz auf gekauftem Land?</summary>
        public bool RectOwned(Vector2 min, Vector2 max)
        {
            if (!Restricted) return true;
            int x0 = Math.Clamp((int)MathF.Floor((min.X - Origin.X) / TileSize), 0, TilesPerSide - 1);
            int y0 = Math.Clamp((int)MathF.Floor((min.Y - Origin.Y) / TileSize), 0, TilesPerSide - 1);
            int x1 = Math.Clamp((int)MathF.Floor((max.X - Origin.X) / TileSize), 0, TilesPerSide - 1);
            int y1 = Math.Clamp((int)MathF.Floor((max.Y - Origin.Y) / TileSize), 0, TilesPerSide - 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (!owned[y * TilesPerSide + x]) return false;
            return true;
        }

        /// <summary>Berührt das Rechteck gekauftes Land (inkl. Rand)? Leistung: nur dort rechnen gestaffelte Raster.</summary>
        public bool RectTouchesOwned(Vector2 min, Vector2 max)
        {
            if (!Restricted) return true;
            int x0 = Math.Clamp((int)MathF.Floor((min.X - Origin.X) / TileSize), 0, TilesPerSide - 1);
            int y0 = Math.Clamp((int)MathF.Floor((min.Y - Origin.Y) / TileSize), 0, TilesPerSide - 1);
            int x1 = Math.Clamp((int)MathF.Floor((max.X - Origin.X) / TileSize), 0, TilesPerSide - 1);
            int y1 = Math.Clamp((int)MathF.Floor((max.Y - Origin.Y) / TileSize), 0, TilesPerSide - 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (owned[y * TilesPerSide + x]) return true;
            return false;
        }

        public void TileBounds(int x, int y, out Vector2 min, out Vector2 max)
        {
            min = Origin + new Vector2(x * TileSize, y * TileSize);
            max = min + new Vector2(TileSize);
        }

        public Vector2 TileCenter(int x, int y) => Origin + new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);

        public bool IsAdjacentToOwned(int x, int y) =>
            IsOwned(x - 1, y) || IsOwned(x + 1, y) || IsOwned(x, y - 1) || IsOwned(x, y + 1);

        public TileState StateOf(int x, int y)
        {
            if (IsOwned(x, y)) return TileState.Owned;
            return InMap(x, y) && IsAdjacentToOwned(x, y) ? TileState.Purchasable : TileState.Locked;
        }

        /// <summary>Preis der nächsten Kachel.</summary>
        public double NextPrice => TileRules.Price(Purchases);

        /// <summary>Name „B2“: Spalte als Buchstabe (West → Ost), Zeile als Zahl (Nord → Süd, Unity +z = Norden).</summary>
        public string NameOf(int x, int y) => $"{(char)('A' + x)}{TilesPerSide - y}";

        /// <summary>Himmelsrichtung von der Kartenmitte („Mitte“, „Nordosten“ …).</summary>
        public string DirectionOf(int x, int y)
        {
            float c = (TilesPerSide - 1) * 0.5f;
            float dx = x - c, dy = y - c;
            if (MathF.Abs(dx) < 0.25f && MathF.Abs(dy) < 0.25f) return "Mitte";
            string ns = dy > 0.25f ? "Nord" : dy < -0.25f ? "Süd" : "";
            string ew = dx > 0.25f ? "ost" : dx < -0.25f ? "west" : "";
            if (ns.Length == 0) return ew == "ost" ? "Osten" : "Westen";
            if (ew.Length == 0) return ns + "en";
            return ns + ew + "en";
        }

        // ------------------------------------------------------------ Kaufen

        public TilePurchasePlan PlanPurchase(int x, int y)
        {
            var plan = new TilePurchasePlan { X = x, Y = y, Cost = NextPrice };
            if (!InMap(x, y)) plan.Error = TilePurchaseError.OutOfMap;
            else if (IsOwned(x, y)) plan.Error = TilePurchaseError.AlreadyOwned;
            else if (!IsAdjacentToOwned(x, y)) plan.Error = TilePurchaseError.NotAdjacent;
            else if (Budget != null && !Budget.CanAfford(plan.Cost)) plan.Error = TilePurchaseError.InsufficientFunds;
            return plan;
        }

        /// <summary>Kauft die Kachel (Kasse „Kauf von Kacheln“). Prüft erneut.</summary>
        public bool Purchase(TilePurchasePlan plan)
        {
            if (plan == null) return false;
            var check = PlanPurchase(plan.X, plan.Y);
            if (!check.IsValid) { plan.Error = check.Error; return false; }
            Budget?.Spend(BudgetCategory.TilePurchase, check.Cost);
            owned[plan.Y * TilesPerSide + plan.X] = true;
            Purchases++;
            TotalSpent += check.Cost;
            Version++;
            Purchased?.Invoke(plan.X, plan.Y);
            return true;
        }

        public bool Purchase(int x, int y) => Purchase(PlanPurchase(x, y));

        public static string ErrorText(TilePurchaseError e)
        {
            switch (e)
            {
                case TilePurchaseError.OutOfMap: return "Außerhalb der Karte";
                case TilePurchaseError.AlreadyOwned: return "Schon gekauft";
                case TilePurchaseError.NotAdjacent: return "Nur Kacheln neben gekauftem Land sind kaufbar";
                case TilePurchaseError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                default: return null;
            }
        }

        // ------------------------------------------------------------ Welt-Infrastruktur

        /// <summary>
        /// Prüfung aussetzen, solange die Welt ihre eigene Infrastruktur baut (Autobahn, Fernbahn über die ganze Karte).
        /// <c>using (world.Tiles.Unrestricted()) { … }</c>
        /// </summary>
        public IDisposable Unrestricted()
        {
            suspend++;
            return new Releaser(this);
        }

        sealed class Releaser : IDisposable
        {
            TileSystem owner;
            public Releaser(TileSystem o) => owner = o;
            public void Dispose()
            {
                if (owner == null) return;
                owner.suspend = Math.Max(0, owner.suspend - 1);
                owner = null;
            }
        }

        // ------------------------------------------------------------ Beschreibung (Kauf-Fenster)

        /// <summary>Gelände, Wasser, Wald und Vorkommen einer Kachel (Raster 64 m), dazu Autobahn/Bahn der Welt.</summary>
        public TileInfo Describe(SimulationWorld world, int x, int y)
        {
            var info = new TileInfo { X = x, Y = y, Name = NameOf(x, y), MinHeight = float.MaxValue, MaxHeight = float.MinValue };
            if (world == null || !InMap(x, y)) return info;
            TileBounds(x, y, out var min, out _);
            const float step = 64f;
            int n = Math.Max(1, (int)(TileSize / step));
            int samples = 0, water = 0, steep = 0;
            float forest = 0f;
            var ids = World.ResourceIds.All;
            var sums = new float[ids.Length];
            for (int iy = 0; iy < n; iy++)
            for (int ix = 0; ix < n; ix++)
            {
                var p = min + new Vector2((ix + 0.5f) * TileSize / n, (iy + 0.5f) * TileSize / n);
                samples++;
                float h = world.Terrain.HeightAt(p);
                info.MinHeight = MathF.Min(info.MinHeight, h);
                info.MaxHeight = MathF.Max(info.MaxHeight, h);
                if (world.Terrain.IsWater(p)) { water++; continue; }
                float dh = MathF.Abs(world.Terrain.HeightAt(p + new Vector2(8f, 0f)) - h) + MathF.Abs(world.Terrain.HeightAt(p + new Vector2(0f, 8f)) - h);
                if (dh > 4f) steep++;
                if (world.Resources == null) continue;
                for (int k = 0; k < ids.Length; k++) sums[k] += world.Resources.DepositAt(ids[k], p);
                forest += world.Resources.DepositAt(World.ResourceIds.Wood, p) >= 0.35f ? 1f : 0f;
            }
            int land = Math.Max(1, samples - water);
            info.WaterShare = water / (float)Math.Max(1, samples);
            info.SteepShare = steep / (float)land;
            info.ForestShare = forest / land;
            if (world.Resources != null)
            {
                for (int k = 0; k < ids.Length; k++)
                {
                    if (ids[k] == World.ResourceIds.Wood || ids[k] == World.ResourceIds.Groundwater) continue;
                    float q = sums[k] / land;
                    if (q >= 0.08f) info.Deposits.Add((ids[k], world.Resources.Kind(ids[k])?.DisplayName ?? ids[k], q));
                }
                info.Deposits.Sort((a, b) => b.Quality.CompareTo(a.Quality));
            }
            TileBounds(x, y, out var tmin, out var tmax);
            foreach (var s in world.Roads.Segments)
                if (s.IsLocked && (Inside(s.Start.Position, tmin, tmax) || Inside(s.End.Position, tmin, tmax))) { info.Highway = true; break; }
            if (world.Rail != null)
                foreach (var s in world.Rail.Tracks.Segments)
                    if (Inside(s.Start.Position, tmin, tmax) || Inside(s.End.Position, tmin, tmax)) { info.Railway = true; break; }
            return info;
        }

        static bool Inside(Vector2 p, Vector2 min, Vector2 max) => p.X >= min.X && p.Y >= min.Y && p.X <= max.X && p.Y <= max.Y;
    }
}
