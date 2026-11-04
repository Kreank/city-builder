using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Shipping
{
    /// <summary>
    /// Klappbrücken: niedrige Straßenbrücke über der Fahrrinne, deren Mittelstück (<see cref="RoadSegmentFlags.Drawbridge"/>)
    /// für Schiffe hochklappt. Ablauf: Schiff meldet sich an → Ampel rot, Schranken senken sich, die Klappe wird geräumt →
    /// Klappe hebt → Schiffe fahren durch → Klappe senkt → Schranken öffnen. Zwischen zwei Öffnungen bleibt die Straße eine
    /// Mindestzeit frei. Im Verkehr wirkt die geschlossene Schranke wie eine Haltelinie am Ende der Zufahrten (Rückstau, wie
    /// am Bahnübergang). Abgeleitet aus dem Straßennetz, Zustand gespeichert (über die Lage).
    /// </summary>
    public sealed partial class ShippingSystem
    {
        readonly List<Drawbridge> drawbridges = new List<Drawbridge>();
        /// <summary>Straßenstück → Haltelinien (Klappbrücke, Fahrtrichtung der Spuren zur Klappe).</summary>
        readonly Dictionary<int, List<(Drawbridge bridge, bool along)>> barriersBySegment = new Dictionary<int, List<(Drawbridge, bool)>>();
        /// <summary>Zustände aus dem Spielstand (Lage → Zustand), bis die Klappbrücken neu abgeleitet sind.</summary>
        readonly List<Drawbridge> loadedBridgeStates = new List<Drawbridge>();

        public IReadOnlyList<Drawbridge> Drawbridges { get { EnsureCrossings(); return drawbridges; } }

        public Drawbridge DrawbridgeOf(int segmentId)
        {
            foreach (var d in drawbridges) if (d.SegmentId == segmentId) return d;
            return null;
        }

        /// <summary>Klappbrücke im Umkreis eines Punkts (Inspektor), null = keine.</summary>
        public Drawbridge DrawbridgeNear(Vector2 p, float radius)
        {
            Drawbridge best = null;
            float bestD = radius;
            foreach (var d in drawbridges)
            {
                float dist = Vector2.Distance(d.Position, p);
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }

        public static string StateText(Drawbridge d)
        {
            switch (d.State)
            {
                case DrawbridgeState.Warning: return "Ampel rot, Schranken senken sich";
                case DrawbridgeState.Raising: return "Klappe hebt sich";
                case DrawbridgeState.Open: return "offen – Schiffe fahren durch";
                case DrawbridgeState.Lowering: return "Klappe senkt sich";
                default: return "geschlossen – Straße frei";
            }
        }

        void RebuildDrawbridges()
        {
            var old = new List<Drawbridge>(drawbridges);
            old.AddRange(loadedBridgeStates);
            loadedBridgeStates.Clear();
            drawbridges.Clear();
            barriersBySegment.Clear();
            foreach (var seg in roads.Segments)
            {
                if (!seg.IsDrawbridge) continue;
                WaterwayCrossing hit = null;
                foreach (var w in waterways)
                {
                    hit = CrossSegment(w, seg.Start.Position, seg.Control, seg.End.Position, seg.Length, seg.HeightAt, false);
                    if (hit != null) break;
                }
                if (hit == null) continue;
                float t = seg.ClosestT(hit.Position);
                var d = new Drawbridge
                {
                    SegmentId = seg.Id, Position = hit.Position, RoadT = t, Height = seg.HeightAt(t),
                    ClosedClearance = hit.Clearance, RoadOpenSince = double.MinValue,
                };
                foreach (var o in old)
                    if (Vector2.Distance(o.Position, d.Position) < 3f)
                    {
                        d.State = o.State; d.StateSince = o.StateSince; d.RoadOpenSince = o.RoadOpenSince;
                        d.OpeningsToday = o.OpeningsToday; d.OpeningsYesterday = o.OpeningsYesterday; d.OpeningsTotal = o.OpeningsTotal;
                        d.ClosedSecondsToday = o.ClosedSecondsToday;
                        break;
                    }
                d.Lift = LiftOf(d, Now);
                drawbridges.Add(d);
                // Haltelinien am Ende jeder Zufahrt (alle anderen Straßen an den Enden der Klappe, Spuren zur Klappe hin)
                foreach (var node in new[] { seg.Start, seg.End })
                    foreach (int id in node.SegmentIds)
                    {
                        if (id == seg.Id) continue;
                        var other = roads.GetSegment(id);
                        if (other == null) continue;
                        if (!barriersBySegment.TryGetValue(id, out var list)) barriersBySegment[id] = list = new List<(Drawbridge, bool)>();
                        list.Add((d, other.End == node));
                    }
            }
        }

        /// <summary>Hook für den Verkehr: Haltelinie vor der nächsten geschlossenen Klappbrücke auf dieser Spur (−1 = keine).</summary>
        float BarrierAhead(Lane lane, float s)
        {
            if (drawbridges.Count == 0 || !barriersBySegment.TryGetValue(lane.Segment.Id, out var list)) return -1f;
            float best = -1f;
            foreach (var (d, along) in list)
            {
                if (!d.RoadClosed || along != lane.Along) continue;
                float stop = lane.Length - ShippingRules.DrawbridgeStopLine;
                if (stop < s - 0.5f) continue;
                if (best < 0f || stop < best) best = stop;
            }
            return best;
        }

        static float LiftOf(Drawbridge d, double now)
        {
            switch (d.State)
            {
                case DrawbridgeState.Raising: return Math.Clamp((float)((now - d.StateSince) / ShippingRules.DrawbridgeRaise), 0f, 1f);
                case DrawbridgeState.Open: return 1f;
                case DrawbridgeState.Lowering: return Math.Clamp(1f - (float)((now - d.StateSince) / ShippingRules.DrawbridgeLower), 0f, 1f);
                default: return 0f;
            }
        }

        /// <summary>Fahrzeuge auf der Klappe (Spuren des Klappen-Stücks)?</summary>
        bool SpanOccupied(Drawbridge d)
        {
            var traffic = world.Traffic;
            if (traffic == null) return false;
            foreach (var lane in traffic.Lanes.GetLanes(d.SegmentId))
                if (lane.Vehicles.Count > 0) return true;
            return false;
        }

        void UpdateDrawbridges(double now, float dt)
        {
            if (drawbridges.Count == 0) return;
            foreach (var d in drawbridges)
            {
                CountRequests(d, out int requesting, out int committed, out int waiting);
                d.ShipsRequesting = requesting;
                d.ShipsWaiting = waiting;
                double since = now - d.StateSince;
                switch (d.State)
                {
                    case DrawbridgeState.RoadOpen:
                        if (requesting > 0 && now - d.RoadOpenSince >= ShippingRules.DrawbridgeMinRoadOpen)
                        {
                            d.State = DrawbridgeState.Warning; d.StateSince = now;
                            d.OpeningsToday++; d.OpeningsTotal++;
                            Version++;
                        }
                        break;
                    case DrawbridgeState.Warning:
                        if (since >= ShippingRules.DrawbridgeWarn && (!SpanOccupied(d) || since >= ShippingRules.DrawbridgeWarn + ShippingRules.DrawbridgeClearPatience))
                        { d.State = DrawbridgeState.Raising; d.StateSince = now; }
                        break;
                    case DrawbridgeState.Raising:
                        if (since >= ShippingRules.DrawbridgeRaise) { d.State = DrawbridgeState.Open; d.StateSince = now; }
                        break;
                    case DrawbridgeState.Open:
                        bool overdue = since >= ShippingRules.DrawbridgeMaxOpen;
                        if (committed == 0 && (requesting == 0 || overdue)) { d.State = DrawbridgeState.Lowering; d.StateSince = now; }
                        break;
                    case DrawbridgeState.Lowering:
                        if (since >= ShippingRules.DrawbridgeLower + ShippingRules.DrawbridgeClear)
                        {
                            d.State = DrawbridgeState.RoadOpen; d.StateSince = now; d.RoadOpenSince = now;
                            Version++;
                        }
                        break;
                }
                if (d.RoadClosed) d.ClosedSecondsToday += dt;
                d.Lift = LiftOf(d, now);
            }
        }

        /// <summary>
        /// Schiffe, die diese Klappbrücke brauchen: angemeldet (Bug ≤ Anmeldeabstand davor bis Heck dahinter), fest unterwegs
        /// unter/vor der Klappe (kann nicht mehr anhalten) und davor wartend.
        /// </summary>
        void CountRequests(Drawbridge d, out int requesting, out int committed, out int waiting)
        {
            requesting = committed = waiting = 0;
            foreach (var s in ships)
            {
                if (s.State == ShipState.Waiting || s.State == ShipState.Moored) continue;
                foreach (var (x, sx) in CrossingsAhead(s))
                {
                    if (x.Bridge != d || !x.NeedsOpening(s.Class)) continue;
                    float ahead = sx - s.S;                       // Bug bis Brückenmitte
                    float half = ChannelHalfAt(s, sx);
                    float behind = s.S - s.Class.Length - sx;      // Heck hinter der Brücke
                    if (behind > half + 5f) continue;              // schon durch
                    if (ahead <= ShippingRules.DrawbridgeRequestDistance) requesting++;
                    float stop = sx - half - ShippingRules.DrawbridgeStopGap;
                    float brake = s.Speed * s.Speed / (2f * ShippingRules.Deceleration);
                    if (s.S > stop - brake - 2f && d.State == DrawbridgeState.Open) committed++;
                    if (s.State == ShipState.WaitingBridge && s.WaitingForBridge == d.SegmentId) waiting++;
                }
            }
        }

        // ------------------------------------------------------------ Planen/Bauen

        /// <summary>
        /// Klappbrücke planen: gerade Stadtstraße von <paramref name="a"/> nach <paramref name="b"/> quer über die Fahrrinne.
        /// Ergibt drei Stücke: Zufahrt (geländefolgend), Klappe (flach, <see cref="ShippingRules.DrawbridgeDeck"/> über Wasser,
        /// so lang wie die Rinne breit ist) und Zufahrt. Die Fahrrinne verlangt hier keine Mindesthöhe (die Klappe öffnet ja).
        /// </summary>
        public DrawbridgePlan PlanDrawbridge(RoadType type, Vector2 a, Vector2 b)
        {
            var plan = new DrawbridgePlan { Type = type, Start = a, End = b };
            if (!Active) { plan.Error = RoadBuildError.DrawbridgeNoFairway; return plan; }
            if (type == null || type.Category != RoadCategory.City) { plan.Error = RoadBuildError.InvalidType; return plan; }
            // Anker wie beim Straßenbau (an vorhandene Straßen anschließen)
            var sa = RoadBuilder.ResolveAnchor(roads, a, 8f);
            var ea = RoadBuilder.ResolveAnchor(roads, b, 8f);
            a = sa.Position; b = ea.Position;
            plan.Start = a; plan.End = b;
            float len = Vector2.Distance(a, b);
            if (len < 2f * ShippingRules.DrawbridgeMinApproach + ShippingRules.DrawbridgeMinSpan) { plan.Error = RoadBuildError.TooShort; return plan; }
            var u = (b - a) / len;
            // genau eine Querung (Rinnen auf demselben Fluss zählen als eine, wenn sie dicht beieinander queren)
            Waterway hitW = null;
            float hitS = 0f, hitAlong = 0f;
            int clusters = 0;
            foreach (var w in waterways)
                for (int j = 0; j + 1 < w.Points.Length; j++)
                {
                    if (!Bezier.LineSegmentsIntersect(a, b, w.Points[j], w.Points[j + 1], out float uu, out float vv)) continue;
                    float along = uu * len;
                    if (hitW != null && MathF.Abs(along - hitAlong) < 40f) continue;
                    clusters++;
                    if (hitW == null || w.Kind == WaterwayKind.Through) { hitW = w; hitAlong = along; hitS = w.Cum[j] + (w.Cum[j + 1] - w.Cum[j]) * vv; }
                }
            if (hitW == null || clusters != 1) { plan.Error = RoadBuildError.DrawbridgeNoFairway; return plan; }
            plan.Waterway = hitW;
            hitW.Sample(hitS, out var cp, out var wdir);
            float angle = RoadGeometry.Angle(u, wdir);
            if (angle > 90f) angle = 180f - angle;
            if (angle < ShippingRules.DrawbridgeMinAngle) { plan.Error = RoadBuildError.AngleTooSharp; return plan; }
            float sin = MathF.Sin(angle * MathF.PI / 180f);
            float span = Math.Clamp(2f * MathF.Min(hitW.ChannelHalfWidth(hitS), ShippingRules.DrawbridgeMaxHalfChannel) / sin + 2f * ShippingRules.DrawbridgeMargin, ShippingRules.DrawbridgeMinSpan, ShippingRules.DrawbridgeMaxSpan);
            var c = a + u * hitAlong;
            float startAlong = hitAlong - span * 0.5f, endAlong = hitAlong + span * 0.5f;
            if (startAlong < ShippingRules.DrawbridgeMinApproach || len - endAlong < ShippingRules.DrawbridgeMinApproach) { plan.Error = RoadBuildError.TooShort; return plan; }
            plan.SpanStart = a + u * startAlong;
            plan.SpanEnd = a + u * endAlong;
            float deck = terrain.WaterLevel + ShippingRules.DrawbridgeDeck;
            deck = MathF.Max(deck, MathF.Max(terrain.HeightAt(plan.SpanStart), terrain.HeightAt(plan.SpanEnd)) + 0.5f);
            // hohe Ufer (Tal): Klappe so hoch, dass die Zufahrten mit der erlaubten Steigung hinunterkommen
            float ha = sa.Kind == AnchorKind.Free ? terrain.HeightAt(a) : sa.Height;
            float hb = ea.Kind == AnchorKind.Free ? terrain.HeightAt(b) : ea.Height;
            float g = type.MaxSlope * 0.85f;
            deck = MathF.Max(deck, MathF.Max(ha - g * startAlong, hb - g * (len - endAlong)));
            plan.Deck = deck;

            suppressFloor = true;
            try
            {
                plan.ApproachA = RoadBuilder.Plan(roads, a, 8f, null, plan.SpanStart, 0f, type, terrain.HeightAt(a), deck, RoadSegmentFlags.None, true);
                plan.Span = RoadBuilder.Plan(roads, plan.SpanStart, 0f, null, plan.SpanEnd, 0f, type, deck, deck, RoadSegmentFlags.Drawbridge, false);
                plan.ApproachB = RoadBuilder.Plan(roads, plan.SpanEnd, 0f, null, b, 8f, type, deck, terrain.HeightAt(b), RoadSegmentFlags.None, true);
            }
            finally { suppressFloor = false; }
            foreach (var p in new[] { plan.ApproachA, plan.Span, plan.ApproachB })
                if (!p.IsValid && p.Error != RoadBuildError.InsufficientFunds) { plan.Error = p.Error; break; }
            plan.Cost = plan.ApproachA.Cost + plan.Span.Cost + plan.ApproachB.Cost + ShippingRules.DrawbridgeCost;
            if (plan.Error == RoadBuildError.None && !Affordable(plan.Cost)) plan.Error = RoadBuildError.InsufficientFunds;
            return plan;
        }

        /// <summary>Klappe abreißen (Fahrbahn wie beim Straßenabriss, Mechanik 25 % zurück); die Zufahrten bleiben.</summary>
        public bool DemolishDrawbridge(int segmentId)
        {
            var seg = roads.GetSegment(segmentId);
            if (seg == null || !seg.IsDrawbridge) return false;
            if (!RoadBuilder.Demolish(roads, seg)) return false;
            Budget?.Refund(ShippingRules.DrawbridgeCost * Economy.EconomyRules.DemolitionRefundShare);
            crossingsDirty = true;
            EnsureCrossings();
            return true;
        }

        /// <summary>Baut die drei Stücke nacheinander (Klappe und zweite Zufahrt rasten am gebauten Ende ein); null bei Fehler.</summary>
        public RoadSegment BuildDrawbridge(DrawbridgePlan plan)
        {
            if (plan == null || !plan.IsValid || !Affordable(plan.Cost)) return null;
            RoadSegment spanSeg = null;
            suppressFloor = true;
            try
            {
                var builtA = RoadBuilder.Build(roads, plan.ApproachA);
                if (builtA.Count == 0) return null;
                var span = RoadBuilder.Plan(roads, plan.SpanStart, 1.5f, null, plan.SpanEnd, 0f, plan.Type, plan.Deck, plan.Deck, RoadSegmentFlags.Drawbridge, false);
                if (!span.IsValid) return null;
                var builtSpan = RoadBuilder.Build(roads, span);
                if (builtSpan.Count == 0) return null;
                spanSeg = builtSpan[0];
                var bPlan = RoadBuilder.Plan(roads, plan.SpanEnd, 1.5f, null, plan.End, 8f, plan.Type, plan.Deck, terrain.HeightAt(plan.End), RoadSegmentFlags.None, true);
                if (bPlan.IsValid) RoadBuilder.Build(roads, bPlan);
            }
            finally { suppressFloor = false; }
            Budget?.Spend(Economy.BudgetCategory.HarborConstruction, ShippingRules.DrawbridgeCost);
            crossingsDirty = true;
            EnsureCrossings();
            return spanSeg;
        }
    }
}
