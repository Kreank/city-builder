using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Simulation.Shipping
{
    /// <summary>
    /// Schiffe: Fahrplan (Durchgangsschiffe auf dem Fluss – Binnenschiffe rund um die Uhr, Fahrgastschiffe und Segelyachten
    /// im Sommer tagsüber; Hafenanläufe je Hafen), Fahren auf der Fahrrinne (träge Beschleunigung, Bremskurve, Abstand zum
    /// Vordermann, Halt vor geschlossenen Klappbrücken), Liegeplätze mit Reede, Umschlag am Kai, Abfahrt zum Wasser-Gateway.
    /// Feste Brücken unter der Masthöhe sperren die Rinne für die Klasse (Schiff kommt nicht, wird gezählt).
    /// </summary>
    public sealed partial class ShippingSystem
    {
        // ------------------------------------------------------------ Fahrplan

        static bool InSeason(int dayOfYear, int firstMonth, int lastMonth)
        {
            int month = dayOfYear / 30; // 0 = Januar (360-Tage-Jahr)
            return month >= firstMonth && month <= lastMonth;
        }

        void Schedule(long now, int hour)
        {
            int doy = world.Clock.DayOfYear;
            foreach (var w in trunks)
            {
                if (w.GatewayA == 0 || w.GatewayB == 0) continue;
                // Binnenschiffe: A→B zu geraden, B→A zu ungeraden Stunden
                if (hour % ShippingRules.BargeEveryHours == 0) SpawnThrough(w, ShipCatalog.Barge, true, now);
                if (hour % ShippingRules.BargeEveryHours == 1) SpawnThrough(w, ShipCatalog.Barge, false, now);
                if (InSeason(doy, 3, 9) && hour >= ShippingRules.ExcursionFirstHour && hour <= ShippingRules.ExcursionLastHour)
                    SpawnThrough(w, ShipCatalog.Excursion, hour % 2 == 0, now);
                if (InSeason(doy, 4, 8) && hour >= ShippingRules.YachtFirstHour && hour <= ShippingRules.YachtLastHour && hour % 3 != 2)
                    SpawnThrough(w, ShipCatalog.Yacht, hour % 3 == 0, now);
            }
            foreach (var h in harbors)
            {
                if (!h.InService) continue;
                if ((hour + h.Id) % ShippingRules.HarborCallEveryHours != 0) continue;
                SpawnCall(h, now);
            }
        }

        /// <summary>Zusätzlicher Anlauf, wenn mehr Fracht wartet, als ein Schiff fasst, und gerade keins kommt.</summary>
        void ExtraCalls(long now)
        {
            foreach (var h in harbors)
            {
                if (!h.InService || h.PendingLoads < h.ShipClass.Capacity) continue;
                if (now - h.LastCallTick < TimeModel.TicksPerHour) continue;
                bool coming = false;
                foreach (var s in ships) if (s.HarborId == h.Id && s.Purpose == ShipPurpose.Inbound) { coming = true; break; }
                if (!coming) SpawnCall(h, now);
            }
        }

        void SpawnThrough(Waterway w, ShipClass c, bool forward, long now)
        {
            if (!w.DeepEnough(c)) return;
            if (!w.Passable(c)) { CountBlocked(c); return; }
            uint hash = SimRandom.Hash((int)(now & 0x7fffffff), unchecked(w.Id * 31 + c.Id.Length * 7 + (forward ? 1 : 0)));
            var s = NewShip(c, ShipPurpose.Through, w, forward);
            s.FromGatewayId = forward ? w.GatewayA : w.GatewayB;
            s.ToGatewayId = forward ? w.GatewayB : w.GatewayA;
            if (c.Role == ShipRole.Passenger) s.Passengers = 60 + (int)(hash % 180u);
            else if (c.Role == ShipRole.Leisure) s.Passengers = 2 + (int)(hash % 5u);
            else s.Loads = c.Capacity / 2 + (int)(hash % (uint)Math.Max(1, c.Capacity / 2));
            ships.Add(s);
        }

        void SpawnCall(Harbor h, long now)
        {
            var list = WaterwaysOf(h);
            // abwechselnd über die freien Rinnen (Gateways)
            var free = new List<Waterway>();
            foreach (var w in list) if (w.GatewayA != 0 && w.Passable(h.ShipClass)) free.Add(w);
            if (free.Count == 0)
            {
                foreach (var w in list) if (w.DeepEnough(h.ShipClass)) { CountBlocked(h.ShipClass); break; }
                return;
            }
            int active = 0;
            foreach (var s in ships) if (s.HarborId == h.Id) active++;
            if (active >= h.Type.Berths + 1) return; // Reede voll
            var way = free[h.CallCounter % free.Count];
            h.CallCounter++;
            h.LastCallTick = now;
            var ship = NewShip(h.ShipClass, ShipPurpose.Inbound, way, true);
            ship.HarborId = h.Id;
            ship.FromGatewayId = way.GatewayA;
            ship.ToGatewayId = way.GatewayA;
            uint hash = SimRandom.Hash((int)(now & 0x7fffffff), h.Id * 17 + h.CallCounter);
            ship.Loads = h.ShipClass.Capacity / 3 + (int)(hash % (uint)Math.Max(1, h.ShipClass.Capacity / 2)); // Importladung (Anzeige)
            ships.Add(ship);
        }

        /// <summary>Durchgangsschiff sofort einreihen (Tests, Werkzeug); null ohne passende Rinne.</summary>
        public Ship QueueThrough(ShipClass c, bool forward)
        {
            EnsureLinks();
            EnsureCrossings();
            foreach (var w in trunks)
            {
                if (!w.DeepEnough(c)) continue;
                int before = ships.Count;
                SpawnThrough(w, c, forward, world.Clock.Tick);
                return ships.Count > before ? ships[ships.Count - 1] : null;
            }
            return null;
        }

        /// <summary>Anlauf eines Hafens sofort einreihen (Tests); null, wenn der Hafen nicht in Betrieb/erreichbar ist.</summary>
        public Ship QueueCall(Harbor h)
        {
            EnsureLinks();
            EnsureCrossings();
            EnsureAccess();
            if (h == null || !h.InService) return null;
            int before = ships.Count;
            SpawnCall(h, world.Clock.Tick);
            return ships.Count > before ? ships[ships.Count - 1] : null;
        }

        Ship NewShip(ShipClass c, ShipPurpose purpose, Waterway w, bool forward)
        {
            var s = new Ship
            {
                Id = nextShipId++, Class = c, Purpose = purpose, Waterway = w, Forward = forward, State = ShipState.Waiting,
                S = 0f, PrevS = 0f, Speed = 0f, SpawnTime = Now, WaitSince = Now,
            };
            ShipsSpawned++;
            Version++;
            return s;
        }

        // ------------------------------------------------------------ Lage auf der Fahrt

        /// <summary>Gesamtlänge der gerichteten Fahrt (Hafenschiffe: Fahrrinne + Weg vom Anfahrpunkt zum Liegeplatz).</summary>
        internal float RouteLength(Ship s) => (s.Waterway?.Length ?? 0f) + LegLength(s);

        float LegLength(Ship s)
        {
            if (s.Purpose == ShipPurpose.Through || s.Berth < 0) return 0f;
            var h = GetHarbor(s.HarborId);
            if (h == null) return 0f;
            return Vector2.Distance(h.Approach, h.Berth(s.Berth, s.Class));
        }

        /// <summary>Strecke auf der Fahrrinne (Punktreihenfolge) zu einer Fahrt-Strecke; außerhalb der Rinne (Liegeplatz-Weg) NaN.</summary>
        float WaterwayS(Ship s, float route)
        {
            var w = s.Waterway;
            switch (s.Purpose)
            {
                case ShipPurpose.Inbound: return route <= w.Length ? route : float.NaN;
                case ShipPurpose.Outbound: { float leg = LegLength(s); return route < leg ? float.NaN : w.Length - (route - leg); }
                default: return s.Forward ? route : w.Length - route;
            }
        }

        /// <summary>Fahrt-Strecke zu einer Strecke auf der Fahrrinne.</summary>
        float RouteS(Ship s, float waterwayS)
        {
            var w = s.Waterway;
            switch (s.Purpose)
            {
                case ShipPurpose.Inbound: return waterwayS;
                case ShipPurpose.Outbound: return LegLength(s) + (w.Length - waterwayS);
                default: return s.Forward ? waterwayS : w.Length - waterwayS;
            }
        }

        /// <summary>Lage und Fahrtrichtung eines Punkts der Fahrt (Bug = <c>s.S</c>, Heck = <c>s.S − Länge</c>).</summary>
        public void Sample(Ship s, float route, out Vector2 position, out Vector2 direction)
        {
            var w = s.Waterway;
            if (w == null) { position = default; direction = Vector2.UnitX; return; }
            var h = s.Purpose == ShipPurpose.Through ? null : GetHarbor(s.HarborId);
            float leg = LegLength(s);
            if (s.Purpose == ShipPurpose.Inbound && h != null && s.Berth >= 0 && route > w.Length)
            {
                var a = h.Approach; var b = h.Berth(s.Berth, s.Class);
                float u = leg > 1e-3f ? Math.Clamp((route - w.Length) / leg, 0f, 1f) : 1f;
                position = Vector2.Lerp(a, b, u);
                direction = leg > 1e-3f ? Vector2.Normalize(b - a) : -h.Front;
                return;
            }
            if (s.Purpose == ShipPurpose.Outbound && h != null && s.Berth >= 0 && route < leg)
            {
                var a = h.Berth(s.Berth, s.Class); var b = h.Approach;
                float u = leg > 1e-3f ? Math.Clamp(route / leg, 0f, 1f) : 1f;
                position = Vector2.Lerp(a, b, u);
                direction = leg > 1e-3f ? Vector2.Normalize(b - a) : -h.Front;
                return;
            }
            float ws = WaterwayS(s, route);
            if (float.IsNaN(ws)) ws = s.Purpose == ShipPurpose.Outbound ? w.Length : w.Length;
            w.Sample(ws, out position, out direction);
            bool reverse = s.Purpose == ShipPurpose.Outbound || (s.Purpose == ShipPurpose.Through && !s.Forward);
            if (reverse) direction = -direction;
            // Rechtsfahrgebot: Gegenverkehr auf derselben Rinne weicht nach Steuerbord aus (zum Ende hin zurück zur Mitte)
            float lateral = MathF.Min(s.Waterway.ChannelHalfWidth(ws) * 0.45f, 18f);
            float fade = MathF.Min(1f, MathF.Min(route, RouteLength(s) - route) / 120f);
            if (s.Purpose != ShipPurpose.Through) fade = MathF.Min(fade, MathF.Max(0f, (ws < w.Length - 150f ? 1f : (w.Length - ws) / 150f)));
            var right = new Vector2(direction.Y, -direction.X);
            position += right * (lateral * MathF.Max(0f, fade));
        }

        /// <summary>Querungen voraus und hinter dem Schiff (Strecke auf der Fahrt).</summary>
        IEnumerable<(WaterwayCrossing x, float s)> CrossingsAhead(Ship s)
        {
            var w = s.Waterway;
            if (w == null) yield break;
            foreach (var x in w.CrossingList)
                yield return (x, RouteS(s, x.S));
        }

        float ChannelHalfAt(Ship s, float route)
        {
            float ws = WaterwayS(s, route);
            return float.IsNaN(ws) ? ShippingRules.MinChannelHalfWidth : s.Waterway.ChannelHalfWidth(ws);
        }

        // ------------------------------------------------------------ Einsetzen / Fahren

        void TrySpawn(Ship s, double now)
        {
            // erster Abschnitt frei? (kein Schiff derselben Richtung mit Heck im Bereich der eigenen Länge + Abstand)
            float need = s.Class.Length + ShippingRules.FollowGap;
            foreach (var o in ships)
            {
                if (o == s || o.State == ShipState.Waiting || o.Waterway != s.Waterway || SameDirection(o, s) == false) continue;
                if (o.S - o.Class.Length < need) { if (now - s.WaitSince > ShippingRules.SpawnPatience) Cancel(s); return; }
            }
            s.State = ShipState.Underway;
            s.Speed = s.Class.Speed * 0.8f;
            s.S = 0f;
            s.PrevS = 0f;
            Version++;
        }

        static bool SameDirection(Ship a, Ship b)
        {
            bool fa = a.Purpose == ShipPurpose.Inbound || (a.Purpose == ShipPurpose.Through && a.Forward);
            bool fb = b.Purpose == ShipPurpose.Inbound || (b.Purpose == ShipPurpose.Through && b.Forward);
            return fa == fb;
        }

        void Cancel(Ship s)
        {
            ShipsCancelled++;
            removedShips.Add(s);
        }

        void StepShip(Ship s, double now, float dt)
        {
            s.PrevS = s.S;
            if (s.State == ShipState.Moored)
            {
                if (now >= s.DwellUntil) Depart(s, now);
                return;
            }
            var c = s.Class;
            float total = RouteLength(s);
            float stop = float.MaxValue;
            ShipState waitState = ShipState.Underway;
            int waitBridge = 0;

            // Ende der Fahrt: Hafenschiffe halten am Liegeplatz (bzw. auf Reede ohne freien Platz)
            if (s.Purpose == ShipPurpose.Inbound)
            {
                if (s.Berth < 0)
                {
                    int berth = FreeBerth(s);
                    if (berth >= 0 && s.S >= s.Waterway.Length - 400f) { s.Berth = berth; total = RouteLength(s); }
                }
                if (s.Berth < 0) { stop = s.Waterway.Length - 160f; if (s.S >= stop - 5f) waitState = ShipState.WaitingBerth; }
                else stop = total;
            }

            // Klappbrücken voraus, die (noch) nicht offen sind; feste Brücken, die nach dem Ablegen zu niedrig gebaut wurden
            bool blockedByFixed = false;
            foreach (var (x, sx) in CrossingsAhead(s))
            {
                if (!x.IsDrawbridge && !x.Passable(c))
                {
                    float stopFixed = sx - ChannelHalfAt(s, sx) - ShippingRules.DrawbridgeStopGap - 20f;
                    if (s.S > stopFixed + 1f) continue;
                    if (stopFixed < stop)
                    {
                        stop = stopFixed;
                        if (s.S >= stopFixed - 30f) { blockedByFixed = true; waitState = ShipState.WaitingBridge; waitBridge = x.SegmentId; }
                    }
                    continue;
                }
                if (!x.NeedsOpening(c)) continue;
                float half = ChannelHalfAt(s, sx);
                float stopAt = sx - half - ShippingRules.DrawbridgeStopGap;
                if (s.S > stopAt + 1f) continue; // schon unter/an der Brücke (sie bleibt offen, solange wir drunter sind)
                if (x.Bridge.State == DrawbridgeState.Open) continue;
                if (stopAt < stop) { stop = stopAt; if (s.S >= stopAt - 30f) { waitState = ShipState.WaitingBridge; waitBridge = x.SegmentId; } }
            }

            // Vordermann auf derselben Rinne in gleicher Richtung
            foreach (var o in ships)
            {
                if (o == s || o.State == ShipState.Waiting || o.Waterway != s.Waterway || !SameDirection(o, s) || o.Purpose != s.Purpose && s.Purpose != ShipPurpose.Through) continue;
                if (o.S <= s.S) continue;
                float tail = o.S - o.Class.Length - ShippingRules.FollowGap;
                if (tail < stop) stop = tail;
            }

            float allowed = stop == float.MaxValue ? float.MaxValue : MathF.Sqrt(2f * ShippingRules.Deceleration * MathF.Max(0f, stop - s.S));
            float v = MathF.Min(s.Speed + c.Acceleration * dt, MathF.Min(c.Speed, allowed));
            if (v < 0f) v = 0f;
            s.Speed = v;
            s.S += v * dt;
            if (stop != float.MaxValue && s.S > stop) { s.S = MathF.Max(s.PrevS, stop); if (s.S >= stop) s.Speed = MathF.Min(s.Speed, 0.05f); }

            bool stopped = s.Speed < 0.15f;
            if (stopped && waitState != ShipState.Underway)
            {
                if (s.State != waitState) s.WaitSince = now;
                s.State = waitState;
                s.WaitingForBridge = waitBridge;
                s.WaitedSeconds += dt;
            }
            else if (s.State != ShipState.Underway) { s.State = ShipState.Underway; s.WaitingForBridge = 0; }
            // vor einer zu niedrigen festen Brücke: nach einer Weile umkehren (fällt aus, wird als gesperrt gezählt)
            if (blockedByFixed && stopped && s.State == ShipState.WaitingBridge && now - s.WaitSince > ShippingRules.TurnBackAfter)
            {
                CountBlocked(c);
                Cancel(s);
                return;
            }

            // Ankunft am Liegeplatz
            if (s.Purpose == ShipPurpose.Inbound && s.Berth >= 0 && s.S >= total - 0.5f && s.Speed < 0.3f)
            {
                Moor(s, now);
                return;
            }
            // Fahrt zu Ende (draußen)
            if (s.Purpose != ShipPurpose.Inbound && s.S >= total + c.Length)
            {
                ShipsCompleted++;
                if (s.Purpose == ShipPurpose.Through) ThroughToday++;
                removedShips.Add(s);
            }
        }

        int FreeBerth(Ship s)
        {
            var h = GetHarbor(s.HarborId);
            if (h == null) return -1;
            for (int b = 0; b < h.Type.Berths; b++)
            {
                bool used = false;
                foreach (var o in ships)
                    if (o != s && o.HarborId == h.Id && o.Berth == b && (o.Purpose == ShipPurpose.Inbound || (o.Purpose == ShipPurpose.Outbound && o.S < LegLength(o) + o.Class.Length)))
                    { used = true; break; }
                if (!used) return b;
            }
            return -1;
        }

        void Moor(Ship s, double now)
        {
            var h = GetHarbor(s.HarborId);
            s.State = ShipState.Moored;
            s.Speed = 0f;
            s.S = RouteLength(s);
            if (h == null) { s.DwellUntil = now; return; }
            int handled = Math.Min(h.PendingLoads, s.Class.Capacity);
            h.PendingLoads -= handled;
            h.CallsToday++;
            s.DwellUntil = now + ShippingRules.DwellBase + ShippingRules.DwellPerLoad * (handled + s.Loads);
            pendingFees += (s.Class == ShipCatalog.Coaster ? ShippingRules.CoasterCallFee : ShippingRules.BargeCallFee) + ShippingRules.LoadFee * (handled + s.Loads);
            s.Loads = handled; // nimmt die Exportladungen mit
            Version++;
        }

        void Depart(Ship s, double now)
        {
            s.Purpose = ShipPurpose.Outbound;
            s.Forward = false;
            s.State = ShipState.Underway;
            s.S = 0f;
            s.PrevS = 0f;
            s.Speed = 0.2f;
            Version++;
        }

        /// <summary>Schiffe, die gerade vor einer Brücke warten.</summary>
        public int ShipsWaitingAtBridges
        {
            get { int n = 0; foreach (var s in ships) if (s.State == ShipState.WaitingBridge) n++; return n; }
        }
    }
}
