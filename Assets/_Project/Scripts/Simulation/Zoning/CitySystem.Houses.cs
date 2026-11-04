using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>Warum ein Wohnhaus hier nicht gesetzt werden kann.</summary>
    public enum HouseBuildError
    {
        None,
        OutOfWorld,
        OnWater,
        TooSteep,
        OverlapsRoad,
        /// <summary>Dienst, Versorger, Depot oder Strommast im Weg (<see cref="HousePlan.BlockerName"/>).</summary>
        OverlapsStructure,
        /// <summary>Anderes Gebäude im Weg (Zonengebäude werden nicht still abgerissen – erst selbst abreißen).</summary>
        OverlapsBuilding,
        /// <summary>Keine ebenerdige Stadtstraße vor der Mitte der Vorderseite (Rand ≤ <see cref="CitySystem.HouseRoadReach"/>).</summary>
        NoRoadAccess,
        /// <summary>Zu wenig Geld in der Stadtkasse (Kontostand + Kreditrahmen) oder Baustopp.</summary>
        InsufficientFunds,
    }

    /// <summary>Geplantes Wohnhaus (Vorschau) – erst <see cref="CitySystem.BuildHouse"/> baut es.</summary>
    public sealed class HousePlan
    {
        public HouseType Type;
        public Vector2 Position;
        public Vector2 Front;
        public float Height;
        public float Cost;
        public HouseBuildError Error;
        public int AccessSegmentId = -1;
        public float AccessT;
        /// <summary>Zur Straße ausgerichtet (Vorderkante am Gehweg).</summary>
        public bool Aligned;
        /// <summary>Verschieben: Id des Hauses (0 = Neubau); <see cref="Cost"/> ist dann der Umzugspreis.</summary>
        public int MovingId;
        /// <summary>Im Weg: Gebäude (bei <see cref="HouseBuildError.OverlapsBuilding"/>).</summary>
        public Building BlockingBuilding;
        /// <summary>Im Weg: Name des Bauwerks/Gebäudes für den Hinweis.</summary>
        public string BlockerName;
        public bool IsValid => Error == HouseBuildError.None;
        public Footprint Footprint => new Footprint(Position, Front, Type.Width, Type.Depth);
        public Vector2 FrontMid => Position + Front * (Type.Depth * 0.5f);
    }

    /// <summary>
    /// Wohnhäuser einzeln setzen (Anno-Stil, Entscheidung des Spielers): Bauart wählen, <see cref="PlanHouse"/> richtet die
    /// Vorderseite zur Straße aus (<see cref="RoadFront.TryAlign"/>) und prüft die Platzregeln wie bei Diensten,
    /// <see cref="BuildHouse"/> bucht über die Stadtkasse. Gesetzte Häuser sind normale Wohnungen fürs Bürgersystem, werden
    /// von keiner Zonenlogik abgerissen und sperren Straßen (Bauwerks-Sperre, Art „house“). Stufen: <see cref="HouseRules"/>.
    /// </summary>
    public sealed partial class CitySystem
    {
        /// <summary>Zufahrt: Straßenrand höchstens so weit vor der Mitte der Vorderseite (wie Dienste).</summary>
        public const float HouseRoadReach = 8f;
        /// <summary>Ausrichten: Cursor bis Tiefe + so viele Meter vom Straßenrand.</summary>
        public const float HouseAlignReach = 16f;
        /// <summary>Größter Höhenunterschied auf dem Grundstück (wird eingeebnet).</summary>
        public const float HouseMaxSlope = 4f;
        /// <summary>Umzugspreis als Anteil der Baukosten.</summary>
        public const float HouseMoveCostShare = 0.1f;

        Vector2 worldMin, worldMax;
        int playerHouses;

        /// <summary>Stadtkasse für Baukosten der gesetzten Häuser (null = kostenlos). Gesetzt in <see cref="SimulationWorld.CreateDefault"/>.</summary>
        public Economy.IConstructionBudget Budget { get; set; }

        /// <summary>Anzahl gesetzter Wohnhäuser.</summary>
        public int PlayerHouseCount => playerHouses;

        /// <summary>Gesetztes Haus wurde verschoben (gleiche Id, Bewohner bleiben).</summary>
        public event Action<Building> BuildingMoved;

        IEnumerable<StructureBlock> HouseBlocks()
        {
            if (playerHouses == 0) yield break;
            foreach (var b in buildings.Values) if (b.IsPlayerPlaced) yield return b.Block;
        }

        bool PlayerHouseNear(Vector2 p, float margin)
        {
            foreach (var key in NearKeys(p, margin))
            {
                if (!buildingBuckets.TryGetValue(key, out var list)) continue;
                foreach (var b in list) if (b.IsPlayerPlaced && b.Contains(p, margin)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ Planen

        /// <summary>
        /// Planen am Mauszeiger: richtet die Vorderseite zur nächsten Stadtstraße aus (Vorderkante am Gehweg, entlang der
        /// Straße verschiebbar); <paramref name="rotationDegrees"/> dreht zusätzlich. Ohne Straße in der Nähe: absolute Drehung
        /// am Cursor → <see cref="HouseBuildError.NoRoadAccess"/>. <paramref name="movingId"/> &gt; 0 = Verschieben.
        /// </summary>
        public HousePlan PlanHouse(HouseType type, Vector2 cursor, float rotationDegrees = 0f, int movingId = 0)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (RoadFront.TryAlign(network, cursor, type.Width, type.Depth, rotationDegrees, type.Depth + HouseAlignReach, out var center, out var front))
            {
                var plan = PlanHouseAt(type, center, front, movingId);
                plan.Aligned = true;
                return plan;
            }
            return PlanHouseAt(type, cursor, Footprint.FrontFromDegrees(rotationDegrees), movingId);
        }

        /// <summary>Planen mit genauer Lage (Mitte, Vorderseite) – Tests, Verschieben.</summary>
        public HousePlan PlanHouseAt(HouseType type, Vector2 position, Vector2 front, int movingId = 0)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            front = front.LengthSquared() > 1e-8f ? Vector2.Normalize(front) : Vector2.UnitY;
            var plan = new HousePlan
            {
                Type = type, Position = position, Front = front, MovingId = movingId,
                Cost = movingId > 0 ? type.Cost * HouseMoveCostShare : type.Cost,
            };
            plan.Height = MathF.Max(terrain.HeightAt(position), terrain.WaterLevel + 0.5f);
            plan.Error = CheckHouse(plan);
            if (plan.Error == HouseBuildError.None && Budget != null && !Budget.CanAfford(plan.Cost)) plan.Error = HouseBuildError.InsufficientFunds;
            return plan;
        }

        HouseBuildError CheckHouse(HousePlan plan)
        {
            var type = plan.Type;
            var fp = plan.Footprint;
            var corners = fp.Corners();
            foreach (var c in corners)
                if (c.X < worldMin.X || c.Y < worldMin.Y || c.X > worldMax.X || c.Y > worldMax.Y) return HouseBuildError.OutOfWorld;
            if (terrain.IsWater(plan.Position)) return HouseBuildError.OnWater;
            float lo = terrain.HeightAt(plan.Position), hi = lo;
            foreach (var c in corners)
            {
                if (terrain.IsWater(c)) return HouseBuildError.OnWater;
                float h = terrain.HeightAt(c);
                lo = MathF.Min(lo, h);
                hi = MathF.Max(hi, h);
            }
            if (hi - lo > HouseMaxSlope) return HouseBuildError.TooSteep;

            foreach (var s in network.Segments)
                if (ServiceGeometry.RoadCrossesRect(s, plan.Position, plan.Front, type.Width, type.Depth, 0f)) return HouseBuildError.OverlapsRoad;

            // Bauwerke (Dienste, Versorger, Depots) – genau über die Grundflächen der Bauwerks-Sperre, mit Namen
            StructureBlock blocker = null;
            foreach (var sb in network.Structures)
            {
                if (sb.Kind == "house") continue; // Häuser prüfen wir unten über die Gebäudeliste
                var o = sb.Footprint;
                if (Vector2.DistanceSquared(o.Center, plan.Position) > (o.Radius + fp.Radius) * (o.Radius + fp.Radius)) continue;
                if (!ServiceGeometry.RectsOverlap(plan.Position, plan.Front, type.Width, type.Depth, o.Center, o.Front, o.Width, o.Depth)) continue;
                if (blocker == null || string.CompareOrdinal(sb.Kind, blocker.Kind) < 0 || (sb.Kind == blocker.Kind && sb.Id < blocker.Id)) blocker = sb;
            }
            if (blocker != null) { plan.BlockerName = blocker.Name; return HouseBuildError.OverlapsStructure; }
            // --- Flughafen (Hook): Hindernisfreiheit – zu hoch für die Einflugschneise (Text als „… im Weg“)
            string placement = PlacementCheck?.Invoke(plan);
            if (placement != null) { plan.BlockerName = placement; return HouseBuildError.OverlapsStructure; }
            // --- Ende Flughafen
            // Strommasten u. a. über die Grundstücks-Sperre (prüft mit halber Zellbreite Rand → Punkte 4 m innerhalb der Kanten)
            if (LotBlocker != null)
                foreach (var p in InsetSamples(plan.Position, plan.Front, type.Width, type.Depth))
                    if (LotBlocker(p)) { plan.BlockerName = "Strommast oder Bauwerk"; return HouseBuildError.OverlapsStructure; }

            // Andere Gebäude: lieber ungültig mit Hinweis als still abreißen
            Building hit = null;
            foreach (var key in NearKeys(plan.Position, fp.Radius + 24f))
            {
                if (!buildingBuckets.TryGetValue(key, out var list)) continue;
                foreach (var b in list)
                {
                    if (b.Id == plan.MovingId || (hit != null && b.Id >= hit.Id)) continue;
                    if (ServiceGeometry.RectsOverlap(plan.Position, plan.Front, type.Width, type.Depth, b.Position, b.Front, b.Width, b.Depth)) hit = b;
                }
            }
            if (hit != null)
            {
                plan.BlockingBuilding = hit;
                plan.BlockerName = HouseCatalog.NameOf(hit);
                return HouseBuildError.OverlapsBuilding;
            }

            if (!RoadFront.FindAccess(network, fp, HouseRoadReach, out int seg, out float t)) return HouseBuildError.NoRoadAccess;
            plan.AccessSegmentId = seg;
            plan.AccessT = t;
            // Bodenplatte auf Höhe der Zufahrt (wie gewachsene Gebäude), nie unter Wasser
            var road = network.GetSegment(seg);
            if (road != null) plan.Height = MathF.Max(road.HeightAt(t), terrain.WaterLevel + 0.5f);
            return HouseBuildError.None;
        }

        /// <summary>
        /// Prüfpunkte für <see cref="LotBlocker"/> (der um eine halbe Zelle = 4 m erweitert prüft): Raster ≤ 4 m, 4 m innerhalb
        /// der Kanten – so liegt jeder Punkt der Grundfläche höchstens ~5 m von einem Prüfpunkt (Mast 2×2 m + Rand wird erkannt).
        /// </summary>
        static IEnumerable<Vector2> InsetSamples(Vector2 center, Vector2 front, float width, float depth)
        {
            var side = new Vector2(-front.Y, front.X);
            float inset = ZoneGrid.CellSize * 0.5f;
            float hw = MathF.Max(0f, width * 0.5f - inset), hd = MathF.Max(0f, depth * 0.5f - inset);
            int nx = Math.Max(1, (int)MathF.Ceiling(2f * hw / 4f) + 1), ny = Math.Max(1, (int)MathF.Ceiling(2f * hd / 4f) + 1);
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                float x = nx == 1 ? 0f : -hw + 2f * hw * i / (nx - 1);
                float y = ny == 1 ? 0f : -hd + 2f * hd * j / (ny - 1);
                yield return center + side * x + front * y;
            }
        }

        // ------------------------------------------------------------ Bauen, Abreißen, Verschieben

        /// <summary>Baut einen gültigen Plan (bucht die Baukosten, ebnet ein). Das Haus wächst wie ein Zonengebäude (Baufortschritt).</summary>
        public Building BuildHouse(HousePlan plan)
        {
            if (plan == null || !plan.IsValid || plan.MovingId > 0) return null;
            if (Budget != null && !Budget.CanAfford(plan.Cost)) return null; // Kasse kann sich seit der Vorschau geändert haben
            Budget?.Spend(Economy.BudgetCategory.HouseConstruction, plan.Cost);
            return PlaceBuilding(plan);
        }

        /// <summary>Gebäude aus einem geprüften Plan anlegen (ohne Buchung) – Einzelbau und Reihe.</summary>
        Building PlaceBuilding(HousePlan plan)
        {
            var t = plan.Type;
            var b = new Building
            {
                Id = nextId++,
                Zone = t.Zone,
                Origin = BuildingOrigin.Player,
                WidthCells = t.WidthCells,
                DepthCells = t.DepthCells,
                Position = plan.Position,
                Front = plan.Front,
                Height = plan.Height,
                FrontSegmentId = plan.AccessSegmentId,
                FrontT = plan.AccessT,
                Variant = rng.Next(1 << 20),
                Households = t.HasHomes ? t.HouseholdsAtLevel(1) : 0,
                Jobs = t.JobsAtLevel(1), // Setzen statt Zonen: Läden/Büros/Hallen (Wohnhäuser: 0)
            };
            terrain.FlattenArea(b.Position, b.Front, b.Width, b.Depth, b.Height);
            AddBuilding(b);
            return b;
        }

        /// <summary>Spieler-Abriss eines gesetzten Hauses: 25 % der Baukosten zurück. Bewohner ziehen um oder weg (wie bei jedem Abriss).</summary>
        public bool DemolishHouse(int id)
        {
            var b = GetBuilding(id);
            var type = HouseCatalog.Of(b);
            if (type == null) return false;
            RemoveBuilding(b);
            Budget?.Refund(type.Cost * Economy.EconomyRules.DemolitionRefundShare);
            return true;
        }

        /// <summary>Umzugsplan für ein gesetztes Haus (gleiche Platzregeln, eigene alte Fläche zählt nicht).</summary>
        public HousePlan PlanMoveHouse(int id, Vector2 cursor, float rotationDegrees = 0f)
        {
            var type = HouseCatalog.Of(GetBuilding(id));
            return type == null ? null : PlanHouse(type, cursor, rotationDegrees, id);
        }

        /// <summary>Verschiebt ein gesetztes Haus (Id, Stufe, Bewohner bleiben; neue Fläche eingeebnet, 10 % Umzugspreis).</summary>
        public bool MoveHouse(HousePlan plan)
        {
            if (plan == null || !plan.IsValid || plan.MovingId <= 0) return false;
            var b = GetBuilding(plan.MovingId);
            if (b == null || !b.IsPlayerPlaced || HouseCatalog.Of(b) != plan.Type) return false;
            if (Budget != null && !Budget.CanAfford(plan.Cost)) return false;
            Budget?.Spend(Economy.BudgetCategory.HouseConstruction, plan.Cost);
            RemoveFromIndex(b);
            b.Position = plan.Position;
            b.Front = plan.Front;
            b.Height = plan.Height;
            b.FrontSegmentId = plan.AccessSegmentId;
            b.FrontT = plan.AccessT;
            b.InvalidateBlock();
            AddToIndex(b);
            terrain.FlattenArea(b.Position, b.Front, b.Width, b.Depth, b.Height);
            Version++;
            BuildingMoved?.Invoke(b);
            NotifyBuildingChanged(b);
            return true;
        }

        /// <summary>Nach Netzänderungen: Zufahrt eines gesetzten Hauses neu suchen; ohne Straße bleibt es stehen (Zufahrt −1).</summary>
        void RevalidateHouseAccess(Building b)
        {
            if (RoadFront.FindAccess(network, b.Footprint, HouseRoadReach, out int seg, out float t))
            {
                b.FrontSegmentId = seg;
                b.FrontT = t;
            }
            else b.FrontSegmentId = -1;
        }

        /// <summary>Hat das gesetzte Haus eine Zufahrt?</summary>
        public bool HasRoadAccess(Building b) => b != null && network.GetSegment(b.FrontSegmentId) != null;
    }
}
