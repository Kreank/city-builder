using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Handel und Lieferverkehr: Bestellungen (lokal, wenn günstiger als Weltmarkt + Transport, sonst Import),
    /// Export von Überschüssen, Lieferungen als Lkw-Fahrt im <see cref="TrafficSystem"/> oder als Platzhalter.
    /// </summary>
    public sealed partial class GoodsSystem
    {
        readonly List<Shipment> shipments = new List<Shipment>();
        readonly Dictionary<int, Shipment> byTrip = new Dictionary<int, Shipment>();
        int nextShipmentId = 1, nextTripId = -1;
        int shipmentsToday, trucksToday, deliveredToday, truckFailedToday, truckSkippedToday;
        int activeTrucks;

        // Je Stunde neu gesammelt
        readonly List<Firm>[] suppliers = NewSupplierLists();
        readonly Dictionary<int, int> componentCache = new Dictionary<int, int>();
        readonly List<External> externals = new List<External>();

        struct External
        {
            public RoadNode Node;
            public int Component;
            /// <summary>Losfahren möglich (Import) bzw. Ankommen möglich (Export).</summary>
            public bool CanStart, CanArrive;
        }

        /// <summary>Weg zum/vom Weltmarkt: Außenanschluss im selben Netzteil oder (ohne jeden Anschluss) das Umland.</summary>
        readonly struct Route
        {
            public readonly bool Ok;
            public readonly bool Hinterland;
            public readonly RoadNode Node;
            public readonly float Distance;
            public Route(bool ok, bool hinterland, RoadNode node, float distance) { Ok = ok; Hinterland = hinterland; Node = node; Distance = distance; }
            public double FreightShare => Hinterland ? GoodsRules.HinterlandFreightShare : GoodsRules.WorldFreightShare;
        }

        static List<Firm>[] NewSupplierLists()
        {
            var l = new List<Firm>[GoodsCatalog.Count];
            for (int i = 0; i < l.Length; i++) l[i] = new List<Firm>();
            return l;
        }

        // ------------------------------------------------------------ Abfragen

        /// <summary>Laufende Lieferungen (Lkw und Platzhalter).</summary>
        public IReadOnlyList<Shipment> Shipments => shipments;

        /// <summary>Lieferung zu einer Lkw-Fahrt (<see cref="Vehicle.TripId"/> &lt; 0); null = keine Lieferung (Bürger/Durchgang).</summary>
        public Shipment ShipmentForTrip(int tripId) => tripId < 0 && byTrip.TryGetValue(tripId, out var s) ? s : null;

        /// <summary>Laufende Lieferungen von/zu einem Gebäude.</summary>
        public void ShipmentsOf(int buildingId, List<Shipment> result)
        {
            result.Clear();
            foreach (var s in shipments)
                if (!s.Done && (s.FromBuildingId == buildingId || s.ToBuildingId == buildingId)) result.Add(s);
        }

        /// <summary>Transportkosten einer Lkw-Ladung über die Luftlinie (geschätzte Verkehrszeit × Kostensatz).</summary>
        public static double TruckCost(float distance) =>
            TruckCostForSeconds(distance * GoodsRules.DetourFactor / GoodsRules.AverageTruckSpeed + 20f);

        public static double TruckCostForSeconds(float seconds) =>
            GoodsRules.TruckFixedCost + GoodsRules.TruckCostPerSecond * Math.Max(0f, seconds);

        // ------------------------------------------------------------ Vorbereitung je Stunde

        void PrepareTrade()
        {
            componentCache.Clear();
            foreach (var l in suppliers) l.Clear();
            foreach (var f in firms)
            {
                if (!f.Building.IsConstructed || !f.Recipe.IsIndustry) continue;
                foreach (var o in f.Recipe.Outputs) suppliers[o.Good.Index].Add(f);
            }
            externals.Clear();
            if (traffic == null) return;
            foreach (var n in traffic.ExternalConnections)
                externals.Add(new External
                {
                    Node = n,
                    Component = components.OfNode(n.Id),
                    CanStart = traffic.IsExternalUsable(n, true),
                    CanArrive = traffic.IsExternalUsable(n, false),
                });
        }

        int Comp(Firm f)
        {
            if (componentCache.TryGetValue(f.BuildingId, out int c)) return c;
            c = ComponentOf(f.Building);
            componentCache[f.BuildingId] = c;
            return c;
        }

        Route WorldRoute(Firm f, bool import)
        {
            if (externals.Count == 0) return new Route(true, true, null, GoodsRules.HinterlandDistance);
            int comp = Comp(f);
            if (comp < 0) return default;
            RoadNode best = null;
            float bestD = float.MaxValue;
            var p = f.Building.Entrance;
            foreach (var e in externals)
            {
                if (e.Component != comp || !(import ? e.CanStart : e.CanArrive)) continue;
                float d = Vector2.Distance(p, e.Node.Position);
                if (d < bestD) { bestD = d; best = e.Node; }
            }
            return best == null ? default : new Route(true, false, best, bestD);
        }

        // ------------------------------------------------------------ Bestellungen

        void PlaceOrders(long now)
        {
            PrepareTrade();
            foreach (var buyer in firms)
            {
                if (!buyer.Building.IsConstructed || buyer.Role == FirmRole.Office) continue;
                if (buyer.Recipe.IsIndustry && buyer.Workers <= 0) continue; // leere Fabrik hortet nichts
                foreach (var i in buyer.Recipe.Inputs) Order(buyer, i.Good, now);
            }
        }

        void Order(Firm buyer, GoodType g, long now)
        {
            int k = g.Index;
            float cap = buyer.InputCapacity[k];
            if (cap <= 0f) return;
            float have = buyer.InputStock[k] + buyer.Incoming[k];
            if (have >= cap * GoodsRules.ReorderLevel) return;
            float want = cap - have;
            var m = markets[k];
            int comp = Comp(buyer);
            Route route = default;
            bool routeKnown = false;

            for (int load = 0; load < GoodsRules.MaxLoadsPerOrder && want >= GoodsRules.MinShipment; load++)
            {
                if (buyer.Cash < -buyer.CreditLimit) return; // Kreditrahmen ausgeschöpft
                float amount = MathF.Min(want, g.LoadUnits);

                // Günstigster lokaler Anbieter (Preis + Transport je Einheit)
                Firm best = null;
                double bestUnit = double.MaxValue;
                float bestAmount = 0f, bestDist = 0f;
                if (comp >= 0)
                    foreach (var s in suppliers[k])
                    {
                        if (s == buyer) continue;
                        float avail = s.OutputStock[k];
                        if (avail < GoodsRules.MinShipment || Comp(s) != comp) continue;
                        float a = MathF.Min(amount, avail);
                        float d = Vector2.Distance(s.Building.Entrance, buyer.Building.Entrance);
                        double unit = m.LocalPrice + TruckCost(d) / a;
                        if (unit < bestUnit) { bestUnit = unit; best = s; bestAmount = a; bestDist = d; }
                    }

                // Import: Weltmarkt + Fracht + Transport ab Außenanschluss
                if (!routeKnown) { route = WorldRoute(buyer, true); routeKnown = true; }
                double tariffRate = TradeGateway?.SpotTariffRate(g) ?? 0.0; // Etappe 8: Zoll des Spielers
                double importUnit = route.Ok
                    ? m.WorldPrice * (1.0 + route.FreightShare + tariffRate) + TruckCost(route.Distance) / amount
                    : double.MaxValue;

                // --- Etappe 8 Handel: Vertragsware im Grenzlager (Lkw ab Außenanschluss, Fernfracht trug der Verkäufer)
                float contractAmount = 0f;
                double contractUnit = double.MaxValue;
                if (TradeGateway != null && route.Ok)
                {
                    float avail = TradeGateway.ImportAvailable(g);
                    if (avail >= GoodsRules.MinShipment)
                    {
                        contractAmount = MathF.Min(amount, avail);
                        contractUnit = TradeGateway.ImportUnitPrice(g) + TruckCost(route.Distance) / contractAmount;
                    }
                }
                // --- Ende Etappe 8

                if (best == null && !route.Ok) return;
                float shipped;
                if (contractAmount > 0f && contractUnit <= Math.Min(importUnit, best != null ? bestUnit : double.MaxValue) * ContractPreference)
                    shipped = DispatchContractImport(buyer, g, contractAmount, route, now); // Etappe 8
                else if (best != null && bestUnit <= importUnit) shipped = DispatchLocal(best, buyer, g, bestAmount, bestDist, now);
                else shipped = DispatchImport(buyer, g, amount, route, now);
                want -= shipped;
            }
        }

        float DispatchLocal(Firm seller, Firm buyer, GoodType g, float amount, float dist, long now)
        {
            int k = g.Index;
            var m = markets[k];
            double value = m.LocalPrice * (double)amount;
            seller.OutputStock[k] -= amount;
            if (seller.OutputStock[k] < 1e-5f) seller.OutputStock[k] = 0f;
            seller.Cash += value;
            seller.Today.Revenue += value;
            seller.Today.Sold += amount;
            buyer.Cash -= value;
            buyer.Today.Purchases += value;
            buyer.Incoming[k] += amount;
            double truck = TruckCost(dist);
            Charge(buyer, truck);
            m.Today.LocalTraded += amount;

            var s = new Shipment
            {
                Kind = ShipmentKind.Local, Good = g, Amount = amount,
                FromBuildingId = seller.BuildingId, ToBuildingId = buyer.BuildingId,
                From = seller.Building.Entrance, To = buyer.Building.Entrance,
                Value = value, TransportCost = truck, TruckEstimate = truck, PayerBuildingId = buyer.BuildingId,
            };
            Launch(s, seller.Building, null, buyer.Building, null, dist, false, false, now);
            return amount;
        }

        float DispatchImport(Firm buyer, GoodType g, float amount, Route route, long now)
        {
            int k = g.Index;
            var m = markets[k];
            double value = m.WorldPrice * (double)amount;
            double truck = TruckCost(route.Distance);
            double freight = value * route.FreightShare + truck;
            // --- Etappe 8: Zoll auf Weltmarkt-Importe (Stadtkasse), zählt zum Einkaufswert der Firma
            double tariff = TradeGateway != null ? value * TradeGateway.SpotTariffRate(g) : 0.0;
            if (tariff > 0) TradeGateway.BookTariff(tariff);
            // --- Ende Etappe 8
            buyer.Cash -= value + tariff;
            buyer.Today.Purchases += value + tariff;
            buyer.Today.Imported += amount;
            buyer.Incoming[k] += amount;
            Charge(buyer, freight);
            m.Today.Imported += amount;
            m.Today.ImportValue += value;

            var from = route.Node != null ? route.Node.Position : buyer.Building.Entrance + new Vector2(route.Distance, 0f);
            var s = new Shipment
            {
                Kind = ShipmentKind.Import, Good = g, Amount = amount,
                FromBuildingId = 0, ToBuildingId = buyer.BuildingId, ConnectionNodeId = route.Node?.Id ?? 0,
                From = from, To = buyer.Building.Entrance,
                Value = value + tariff, TransportCost = freight, TruckEstimate = truck, PayerBuildingId = buyer.BuildingId,
            };
            Launch(s, null, route.Node, buyer.Building, null, route.Distance, true, route.Hinterland, now);
            return amount;
        }

        // ------------------------------------------------------------ Etappe 8 Handel: Vertragsware

        /// <summary>Vertragsware wird bis zu diesem Faktor teurer als die beste Alternative noch genommen (liegt schon bereit).</summary>
        const double ContractPreference = 1.05;
        /// <summary>Exportverträge werden bis herunter auf diesen Füllstand des Ausgangslagers beliefert.</summary>
        const float ContractKeepFill = 0.1f;

        float DispatchContractImport(Firm buyer, GoodType g, float amount, Route route, long now)
        {
            int k = g.Index;
            var m = markets[k];
            double value = TradeGateway.TakeImport(g, amount);
            double truck = TruckCost(route.Distance);
            buyer.Cash -= value;
            buyer.Today.Purchases += value;
            buyer.Today.Imported += amount;
            buyer.Incoming[k] += amount;
            Charge(buyer, truck);
            m.Today.Imported += amount;
            m.Today.ImportValue += value;

            var from = route.Node != null ? route.Node.Position : buyer.Building.Entrance + new Vector2(route.Distance, 0f);
            var s = new Shipment
            {
                Kind = ShipmentKind.Import, Good = g, Amount = amount, IsContract = true,
                FromBuildingId = 0, ToBuildingId = buyer.BuildingId, ConnectionNodeId = route.Node?.Id ?? 0,
                From = from, To = buyer.Building.Entrance,
                Value = value, TransportCost = truck, TruckEstimate = truck, PayerBuildingId = buyer.BuildingId,
            };
            Launch(s, null, route.Node, buyer.Building, null, route.Distance, true, route.Hinterland, now);
            return amount;
        }

        /// <summary>Beliefert offene Exportverträge (vor dem Weltmarkt-Export): Lkw zum Außenanschluss, die Stadtkasse zahlt den Vertragspreis.</summary>
        void ContractExport(Firm seller, GoodType g, float cap, ref Route route, ref bool routeKnown, long now)
        {
            int k = g.Index;
            var m = markets[k];
            for (int load = 0; load < 2; load++)
            {
                float wanted = TradeGateway.ExportWanted(g);
                float avail = seller.OutputStock[k] - cap * ContractKeepFill;
                float amount = MathF.Min(g.LoadUnits, MathF.Min(wanted, avail));
                if (amount < GoodsRules.MinShipment) return;
                if (!routeKnown) { route = WorldRoute(seller, false); routeKnown = true; }
                if (!route.Ok) return;
                double truck = TruckCost(route.Distance);
                double net = TradeGateway.ExportUnitPrice(g) - truck / amount;
                // wie beim Weltmarkt-Export: nur wenn es sich lohnt – volle Lager immer
                if (net < m.LocalPrice * GoodsRules.ExportMinPriceRatio && seller.OutputStock[k] < cap * GoodsRules.DumpFill) return;
                double value = TradeGateway.ExportDispatched(g, amount);
                seller.OutputStock[k] -= amount;
                if (seller.OutputStock[k] < 1e-5f) seller.OutputStock[k] = 0f;
                seller.Cash += value;
                seller.Today.Revenue += value;
                seller.Today.Sold += amount;
                seller.Today.Exported += amount;
                Charge(seller, truck);
                m.Today.Exported += amount;
                m.Today.ExportValue += value;

                var to = route.Node != null ? route.Node.Position : seller.Building.Entrance + new Vector2(route.Distance, 0f);
                var s = new Shipment
                {
                    Kind = ShipmentKind.Export, Good = g, Amount = amount, IsContract = true,
                    FromBuildingId = seller.BuildingId, ToBuildingId = 0, ConnectionNodeId = route.Node?.Id ?? 0,
                    From = seller.Building.Entrance, To = to,
                    Value = value, TransportCost = truck, TruckEstimate = truck, PayerBuildingId = seller.BuildingId,
                };
                Launch(s, seller.Building, null, null, route.Node, route.Distance, true, route.Hinterland, now);
            }
        }
        // ------------------------------------------------------------ Ende Etappe 8

        // ------------------------------------------------------------ Export

        void ExportSurplus(long now)
        {
            foreach (var seller in firms)
            {
                if (!seller.Building.IsConstructed || !seller.Recipe.IsIndustry) continue;
                Route route = default;
                bool routeKnown = false;
                foreach (var o in seller.Recipe.Outputs)
                {
                    var g = o.Good;
                    int k = g.Index;
                    float cap = seller.OutputCapacity[k];
                    if (TradeGateway != null && cap > 0f) ContractExport(seller, g, cap, ref route, ref routeKnown, now); // Etappe 8: Verträge zuerst
                    if (cap <= 0f || seller.OutputStock[k] < cap * GoodsRules.ExportFill) continue;
                    if (!routeKnown) { route = WorldRoute(seller, false); routeKnown = true; }
                    if (!route.Ok) break;
                    var m = markets[k];
                    for (int load = 0; load < 2; load++)
                    {
                        float stock = seller.OutputStock[k];
                        float fill = stock / cap;
                        float amount = MathF.Min(g.LoadUnits, stock - cap * GoodsRules.ExportKeepFill);
                        if (fill < GoodsRules.ExportFill || amount < GoodsRules.MinShipment) break;
                        double value = m.WorldPrice * (double)amount;
                        double truck = TruckCost(route.Distance);
                        double freight = value * route.FreightShare + truck;
                        double netUnit = (value - freight) / amount;
                        // Export nur, wenn der Weltmarkt (abzgl. Fracht) etwa so viel bringt wie der lokale Preis – volle Lager immer
                        if (netUnit < m.LocalPrice * GoodsRules.ExportMinPriceRatio && fill < GoodsRules.DumpFill) break;

                        seller.OutputStock[k] -= amount;
                        seller.Cash += value;
                        seller.Today.Revenue += value;
                        seller.Today.Sold += amount;
                        seller.Today.Exported += amount;
                        Charge(seller, freight);
                        m.Today.Exported += amount;
                        m.Today.ExportValue += value;

                        var to = route.Node != null ? route.Node.Position : seller.Building.Entrance + new Vector2(route.Distance, 0f);
                        var s = new Shipment
                        {
                            Kind = ShipmentKind.Export, Good = g, Amount = amount,
                            FromBuildingId = seller.BuildingId, ToBuildingId = 0, ConnectionNodeId = route.Node?.Id ?? 0,
                            From = seller.Building.Entrance, To = to,
                            Value = value, TransportCost = freight, TruckEstimate = truck, PayerBuildingId = seller.BuildingId,
                        };
                        Launch(s, seller.Building, null, null, route.Node, route.Distance, true, route.Hinterland, now);
                    }
                }
            }
        }

        static void Charge(Firm f, double transport)
        {
            f.Cash -= transport;
            f.Today.Transport += transport;
        }

        // ------------------------------------------------------------ Fahrten

        /// <summary>
        /// Schickt eine Lieferung los: mit Anteil <see cref="GoodsRules.TruckShare"/> × Verkehrsaufkommen als echter Lkw
        /// (solange unter dem Fahrzeuglimit), sonst als Platzhalter mit Fahrzeit aus der Luftlinie. Lkw-Abfahrten werden
        /// über die Stunde verteilt (Warteschlange, je Tick abgearbeitet); findet sich kein Weg/Platz, wird es ein Platzhalter.
        /// </summary>
        void Launch(Shipment s, Building fromB, RoadNode fromNode, Building toB, RoadNode toNode, float dist, bool outside,
                    bool hinterland, long now)
        {
            s.Id = nextShipmentId++;
            s.DispatchTick = now;
            shipmentsToday++;
            int ticks = GoodsRules.PlaceholderTicks(dist, outside);
            s.FallbackTicks = ticks;
            shipments.Add(s);
            if (traffic != null && !hinterland && activeTrucks + pendingTrucks.Count < GoodsRules.MaxTruckShare * traffic.MaxVehicles &&
                rng.Chance(GoodsRules.TruckShare * traffic.Demand))
            {
                s.FromNode = fromNode;
                s.ToNode = toNode;
                s.LaunchTick = now + pendingSpread++ % GoodsRules.TickInterval;
                s.ArriveTick = long.MaxValue; // wartet auf die Abfahrt
                pendingTrucks.Add(s);
                return;
            }
            if (traffic != null && !hinterland) truckSkippedToday++;
            s.ArriveTick = now + ticks;
        }

        readonly List<Shipment> pendingTrucks = new List<Shipment>();
        int pendingSpread;

        /// <summary>Wartende Lkw abfahren lassen (je Tick; <paramref name="upTo"/> = bis zu welchem Abfahrts-Tick).</summary>
        void LaunchPending(long now, long upTo)
        {
            if (pendingTrucks.Count == 0) return;
            int kept = 0;
            for (int i = 0; i < pendingTrucks.Count; i++)
            {
                var s = pendingTrucks[i];
                if (s.LaunchTick > upTo) { pendingTrucks[kept++] = s; continue; }
                if (TryStartTruck(s, now)) continue;
                // Kein Platz an der Einfahrt: bis zu 2 Stunden (600 s Verkehrszeit, 20 Versuche) erneut versuchen
                if (now - s.DispatchTick < 2 * GoodsRules.TickInterval) { s.LaunchTick = now + 1; pendingTrucks[kept++] = s; continue; }
                s.ArriveTick = now + s.FallbackTicks;
                truckFailedToday++;
            }
            pendingTrucks.RemoveRange(kept, pendingTrucks.Count - kept);
        }

        bool TryStartTruck(Shipment s, long now)
        {
            if (traffic == null || traffic.Vehicles.Count >= traffic.MaxVehicles) return false;
            TripEnd from, to;
            if (s.FromNode != null) from = TripEnd.AtNode(s.FromNode);
            else if (!TryTripEnd(city.GetBuilding(s.FromBuildingId), out from)) return false;
            if (s.ToNode != null) to = TripEnd.AtNode(s.ToNode);
            else if (!TryTripEnd(city.GetBuilding(s.ToBuildingId), out to)) return false;
            int id = nextTripId--;
            if (nextTripId == int.MinValue) nextTripId = -1;
            var v = traffic.RequestTrip(from, to, VehicleClass.Truck, id);
            if (v == null) return false;
            s.IsTruck = true;
            s.TripId = id;
            s.StartTrafficTime = traffic.Time;
            s.ArriveTick = now + GoodsRules.TruckTimeoutTicks;
            byTrip[id] = s;
            activeTrucks++;
            trucksToday++;
            return true;
        }

        /// <summary>Lkw angekommen (oder abgebrochen – die Ware kommt trotzdem an). Läuft im Verkehrs-Tick: nur Zustand ändern.</summary>
        void OnTripEnded(Vehicle v, bool arrived)
        {
            if (v.TripId >= 0 || !byTrip.TryGetValue(v.TripId, out var s)) return;
            byTrip.Remove(v.TripId);
            activeTrucks--;
            // Echte Fahrzeit statt Schätzung: Stau kostet Geld (Differenz trägt der Zahler)
            double actual = TruckCostForSeconds((float)(traffic.Time - s.StartTrafficTime));
            double delta = actual - s.TruckEstimate;
            s.TransportCost += delta;
            s.TruckEstimate = actual;
            var payer = FirmAt(s.PayerBuildingId);
            if (payer != null) Charge(payer, delta);
            Deliver(s);
        }

        void DeliverDue(long now)
        {
            foreach (var s in shipments)
            {
                if (s.Done || now < s.ArriveTick) continue;
                if (s.IsTruck && byTrip.Remove(s.TripId)) activeTrucks--; // Zeitüberschreitung: Lkw fährt ggf. weiter
                Deliver(s);
            }
            shipments.RemoveAll(x => x.Done);
            Stats.ActiveShipments = shipments.Count;
            Stats.ActiveTrucks = activeTrucks;
        }

        void Deliver(Shipment s)
        {
            if (s.Done) return;
            s.Done = true;
            deliveredToday++;
            if (s.Kind == ShipmentKind.Export)
            {
                if (s.IsContract) TradeGateway?.ExportArrived(s.Good, s.Amount); // Etappe 8: am Außenanschluss → Grenzlager
                return; // Erlös wurde beim Abschicken gebucht
            }
            var f = FirmAt(s.ToBuildingId);
            if (f == null) { Stats.LostShipments++; return; }
            int k = s.Good.Index;
            f.Incoming[k] = MathF.Max(0f, f.Incoming[k] - s.Amount);
            f.InputStock[k] += s.Amount;
            f.InputValue[k] += s.Value;
        }
    }
}
