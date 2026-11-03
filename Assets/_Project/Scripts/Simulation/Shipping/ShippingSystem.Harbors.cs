using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;

namespace CityBuilder.Simulation.Shipping
{
    /// <summary>
    /// Häfen: Güterhafen am Ufer (Kaimauer an der Rückseite reicht bis an tiefes Wasser – dazwischen wird aufgeschüttet –,
    /// Vorderseite zur Straße). In Betrieb, wenn eine Stadtstraße vor der Vorderseite liegt und eine Fahrrinne für die
    /// Schiffsklasse des Hafens frei zu einem Wasser-Gateway führt (keine zu niedrige feste Brücke). Dann ist der Wasser-Gateway
    /// befahrbar.
    /// </summary>
    public sealed partial class ShippingSystem
    {
        // ------------------------------------------------------------ Planen

        /// <summary>
        /// Hafen planen: Richtung zum Wasser aus der Umgebung des Mauszeigers, Kaikante dort, wo das Wasser vor dem Liegeplatz
        /// tief genug ist (höchstens 60 m vom Ufer), Hafen dahinter an Land; folgt dem Mauszeiger entlang des Ufers.
        /// </summary>
        public HarborPlan PlanHarbor(HarborType type, Vector2 cursor)
        {
            var plan = new HarborPlan { Type = type, Position = cursor, Front = Vector2.UnitY, Cost = type.Cost, ShipClass = ShipCatalog.Barge };
            if (!Active) { plan.Error = HarborBuildError.NoShore; return plan; }
            // Richtung zum Wasser: Mittel über Wasserpunkte im Umkreis
            var sum = Vector2.Zero;
            int wet = 0;
            float r = ShippingRules.HarborSnapRadius;
            for (int i = -6; i <= 6; i++)
                for (int j = -6; j <= 6; j++)
                {
                    var o = new Vector2(i, j) * (r / 6f);
                    if (o.Length() > r || o.LengthSquared() < 1f) continue;
                    if (!terrain.IsWater(cursor + o)) continue;
                    sum += Vector2.Normalize(o) / MathF.Max(8f, o.Length());
                    wet++;
                }
            if (wet == 0 || sum.LengthSquared() < 1e-8f) { plan.Error = HarborBuildError.NoShore; return plan; }
            var n = Vector2.Normalize(sum); // zum Wasser
            // Uferlinie entlang n
            float shoreAlong = -1f;
            for (float d = -r; d <= r * 1.5f; d += 2f)
                if (terrain.IsWater(cursor + n * d)) { shoreAlong = d; break; }
            if (shoreAlong < -r + 0.5f) { plan.Error = HarborBuildError.OnWater; return plan; }
            if (shoreAlong < 0f) shoreAlong = 0f;
            var shore = cursor + n * shoreAlong;
            // Kaikante: so weit hinaus, bis der Liegeplatz tief genug ist (Binnenschiff), höchstens 60 m
            float berthOffset = ShipCatalog.Barge.Beam * 0.5f + ShippingRules.BerthGap;
            float quay = -1f;
            for (float d = ShippingRules.QuayOverhang; d <= 60f; d += 2f)
            {
                if (MinDepthAlongQuay(shore + n * (d + berthOffset), n, type.Width * 0.8f) >= ShipCatalog.Barge.RequiredDepth) { quay = d; break; }
            }
            if (quay < 0f) { plan.Error = HarborBuildError.TooShallow; plan.BerthDepth = MinDepthAlongQuay(shore + n * (60f + berthOffset), n, type.Width * 0.8f); return plan; }
            var quayLine = shore + n * quay;
            plan.Front = -n;
            plan.Position = quayLine - n * (type.Depth * 0.5f);
            plan.BerthDepth = MinDepthAlongQuay(quayLine + n * (ShipCatalog.Coaster.Beam * 0.5f + ShippingRules.BerthGap), n, type.Width * 0.8f);
            plan.ShipClass = plan.BerthDepth >= ShipCatalog.Coaster.RequiredDepth ? ShipCatalog.Coaster : ShipCatalog.Barge;
            plan.Error = ValidateHarbor(plan);
            return plan;
        }

        float MinDepthAlongQuay(Vector2 mid, Vector2 n, float length)
        {
            var side = new Vector2(-n.Y, n.X);
            float min = float.MaxValue;
            for (int k = -4; k <= 4; k++)
            {
                var p = mid + side * (length * 0.5f * k / 4f);
                min = MathF.Min(min, terrain.WaterLevel - terrain.NaturalHeightAt(p));
            }
            return min;
        }

        float BerthDepth(Vector2 position, Vector2 front, HarborType type)
        {
            var n = -front;
            var quayLine = position + n * (type.Depth * 0.5f);
            return MinDepthAlongQuay(quayLine + n * (ShipCatalog.Coaster.Beam * 0.5f + ShippingRules.BerthGap), n, type.Width * 0.8f);
        }

        HarborBuildError ValidateHarbor(HarborPlan plan)
        {
            var type = plan.Type;
            var fp = new Footprint(plan.Position, plan.Front, type.Width, type.Depth);
            foreach (var c in fp.Corners())
                if (!world.Contains(c)) return HarborBuildError.OutOfWorld;
            // Landseite (vordere Hälfte) muss Land sein, aufgeschüttet wird höchstens die hintere Hälfte
            float lo = float.MaxValue, hi = float.MinValue;
            int water = 0, total = 0;
            for (int i = 0; i <= 4; i++)
                for (int j = 0; j <= 4; j++)
                {
                    var p = plan.Position + fp.Side * (type.Width * (i / 4f - 0.5f)) + plan.Front * (type.Depth * (j / 4f - 0.5f));
                    total++;
                    if (terrain.IsWater(p)) { water++; if (j >= 2) return HarborBuildError.OnWater; continue; }
                    float h = terrain.HeightAt(p);
                    lo = MathF.Min(lo, h); hi = MathF.Max(hi, h);
                }
            if (lo == float.MaxValue) return HarborBuildError.OnWater;
            if (hi - lo > ShippingRules.HarborMaxSlope) return HarborBuildError.TooSteep;
            plan.Height = terrain.WaterLevel + ShippingRules.QuayHeight; // Kai knapp über dem Wasser, Gelände dahinter abgetragen/aufgeschüttet
            foreach (var s in roads.Segments)
                if (ServiceGeometry.RoadCrossesRect(s, plan.Position, plan.Front, type.Width, type.Depth, -0.5f)) return HarborBuildError.OverlapsRoad;
            if (world.Rail != null)
                foreach (var s in world.Rail.Tracks.Segments)
                    if (ServiceGeometry.RoadCrossesRect(s, plan.Position, plan.Front, type.Width, type.Depth, -0.5f)) return HarborBuildError.OverlapsTrack;
            foreach (var b in roads.Structures)
            {
                var o = b.Footprint;
                if (ServiceGeometry.RectsOverlap(plan.Position, plan.Front, type.Width, type.Depth, o.Center, o.Front, o.Width, o.Depth))
                {
                    plan.BlockedBy = b;
                    return HarborBuildError.OverlapsStructure;
                }
            }
            // Liegeplatz muss über die Fahrrinne erreichbar sein
            var approach = plan.Position - plan.Front * (type.Depth * 0.5f + ShippingRules.HarborApproach);
            if (Grid.NearestNavigable(approach, ShipCatalog.Barge.RequiredDepth, 90f) < 0) return HarborBuildError.NoFairway;
            plan.HasRoadAccess = RoadFront.FindAccess(roads, fp, ShippingRules.HarborRoadReach, out _, out _);
            if (!Affordable(plan.Cost)) return HarborBuildError.InsufficientFunds;
            return HarborBuildError.None;
        }

        // ------------------------------------------------------------ Bauen / Abriss

        public Harbor BuildHarbor(HarborPlan plan)
        {
            if (plan == null || !plan.IsValid || !Affordable(plan.Cost)) return null;
            Budget?.Spend(Economy.BudgetCategory.HarborConstruction, plan.Cost);
            var h = new Harbor { Id = nextHarborId++, Type = plan.Type, Position = plan.Position, Front = plan.Front, Height = plan.Height };
            h.Name = HarborName(h);
            harbors.Add(h);
            PrepareSite(h);
            BuildHarborWaterways(h);
            accessDirty = true;
            EnsureCrossings();
            EnsureAccess();
            UpdateGatewayStatus();
            Version++;
            return h;
        }

        public Harbor TryBuildHarbor(HarborType type, Vector2 cursor) => BuildHarbor(PlanHarbor(type, cursor));

        public bool DemolishHarbor(int id)
        {
            var h = GetHarbor(id);
            if (h == null) return false;
            // Schiffe zum Hafen drehen um (fahren auf ihrer Rinne hinaus), liegende Schiffe legen ab
            foreach (var s in ships)
            {
                if (s.HarborId != id) continue;
                if (s.Purpose == ShipPurpose.Inbound)
                {
                    if (s.State == ShipState.Moored || s.State == ShipState.Waiting) { s.Purpose = ShipPurpose.Outbound; s.Berth = -1; s.S = 0f; s.PrevS = 0f; s.State = ShipState.Underway; }
                    else { float ws = MathF.Min(s.S, s.Waterway.Length); s.Purpose = ShipPurpose.Outbound; s.Berth = -1; s.S = s.Waterway.Length - ws; s.PrevS = s.S; }
                }
                s.HarborId = 0;
                s.Berth = -1;
            }
            harbors.Remove(h);
            RemoveHarborWaterways(id);
            Budget?.Refund(h.Type.Cost * Economy.EconomyRules.DemolitionRefundShare);
            linkedGateways = -1;
            UpdateGatewayStatus();
            Version++;
            return true;
        }

        void PrepareSite(Harbor h)
        {
            // Aufschüttung + Einebnen (Kaihöhe), Zonen-Gebäude weichen
            terrain.FillArea(h.Position, h.Front, h.Type.Width, h.Type.Depth, h.Height);
            var city = world.City;
            if (city == null) return;
            var remove = new List<Zoning.Building>();
            foreach (var b in city.Buildings)
            {
                if (b.IsPlayerPlaced) continue;
                if (ServiceGeometry.RectsOverlap(h.Position, h.Front, h.Type.Width, h.Type.Depth, b.Position, b.Front, b.Width, b.Depth)) remove.Add(b);
            }
            foreach (var b in remove) city.RemoveBuilding(b);
        }

        string HarborName(Harbor h)
        {
            string city = world.Realm?.CityName ?? "Neustadt";
            int same = harbors.Count;
            if (same == 0) return "Hafen " + city;
            return "Hafen " + city + " " + GatewayRules.Compass(h.Position);
        }

        // ------------------------------------------------------------ Zufahrt, Erreichbarkeit, Gateways (abgeleitet)

        bool accessDirty = true;
        int accessRoadVersion = -1;

        void EnsureAccess()
        {
            if (!accessDirty && accessRoadVersion == roads.Version) return;
            accessDirty = false;
            accessRoadVersion = roads.Version;
            foreach (var h in harbors)
            {
                var fp = h.Footprint;
                if (RoadFront.FindAccess(roads, fp, ShippingRules.HarborRoadReach, out int seg, out float t))
                {
                    h.AccessSegmentId = seg;
                    var road = roads.GetSegment(seg);
                    h.AccessNodeId = Vector2.Distance(road.Start.Position, fp.FrontMid) <= Vector2.Distance(road.End.Position, fp.FrontMid) ? road.Start.Id : road.End.Id;
                }
                else { h.AccessSegmentId = -1; h.AccessNodeId = 0; }
                bool reachable = false;
                string reason = null;
                var list = WaterwaysOf(h);
                if (list.Count == 0) reason = "keine Fahrrinne zum Kartenrand (Wasser zu flach)";
                foreach (var w in list)
                {
                    if (w.GatewayA == 0) continue;
                    var x = w.FirstBlocking(h.ShipClass);
                    if (!w.DeepEnough(h.ShipClass)) { reason ??= "Fahrrinne zu flach"; continue; }
                    if (x == null) { reachable = true; break; }
                    reason = $"{CrossingName(x)} zu niedrig ({MathF.Max(0f, x.Clearance):0.0} m, {h.ShipClass.DisplayName} braucht {h.ShipClass.RequiredClearance:0.0} m) – Schiffe kommen nicht durch";
                }
                h.Reachable = reachable;
                h.BlockedReason = reachable ? null : reason;
            }
        }

        void UpdateGatewayStatus()
        {
            var gw = world.Gateways;
            if (gw == null) return;
            EnsureLinks();
            foreach (var g in gw.All)
            {
                if (g.Kind != GatewayKind.Water) continue;
                bool operational = false;
                string note = null;
                foreach (var h in harbors)
                    foreach (var w in WaterwaysOf(h))
                    {
                        if (w.GatewayA != g.Id) continue;
                        if (h.InService && w.Passable(h.ShipClass)) operational = true;
                        else if (h.HasAccess && w.DeepEnough(h.ShipClass) && note == null) note = "Fahrrinne gesperrt (Brücke zu niedrig)";
                    }
                if (!operational && note == null)
                {
                    note = harbors.Count == 0 ? "noch nicht befahrbar (Hafen fehlt)" : "noch nicht befahrbar (Hafen nicht in Betrieb)";
                    foreach (var w in trunks)
                        if ((w.GatewayA == g.Id || w.GatewayB == g.Id) && w.Passable(ShipCatalog.Barge)) { note += " · Durchgangsschiffe fahren"; break; }
                }
                var status = operational ? GatewayStatus.Operational : GatewayStatus.Planned;
                if (g.Status != status) { g.Status = status; Version++; }
                g.ShippingNote = note;
            }
        }

        /// <summary>Hafenplätze als Text für das Werkzeug.</summary>
        public static string ErrorText(HarborPlan plan)
        {
            switch (plan.Error)
            {
                case HarborBuildError.NoShore: return $"Hafen ans Ufer setzen (Wasser im Umkreis von {ShippingRules.HarborSnapRadius:0} m)";
                case HarborBuildError.OnWater: return "Zu weit im Wasser – näher ans Ufer";
                case HarborBuildError.TooShallow: return $"Wasser vor dem Kai zu flach ({MathF.Max(0f, plan.BerthDepth):0.0} m, Binnenschiff braucht {ShipCatalog.Barge.RequiredDepth:0.0} m)";
                case HarborBuildError.NoFairway: return "Liegeplatz nicht an der Fahrrinne";
                case HarborBuildError.TooSteep: return $"Ufer zu steil (max. {ShippingRules.HarborMaxSlope:0} m Höhenunterschied)";
                case HarborBuildError.OverlapsRoad: return "Steht auf einer Straße";
                case HarborBuildError.OverlapsTrack: return "Steht auf einem Gleis";
                case HarborBuildError.OverlapsStructure: return plan.BlockedBy != null ? $"Überschneidet {plan.BlockedBy.Name}" : "Überschneidet ein Bauwerk";
                case HarborBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                case HarborBuildError.OutOfWorld: return "Außerhalb der Karte";
                default: return plan.Error.ToString();
            }
        }
    }
}
