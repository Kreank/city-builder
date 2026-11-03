using System;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Trade;

namespace CityBuilder.Simulation.Shipping
{
    /// <summary>
    /// Güter über den Hafen (G5/Etappe 8): Ist ein Hafen in Betrieb, laufen Import und Export der Firmen im selben
    /// Straßennetzteil über ihn, soweit der Handelspartner über den <b>Seeweg</b> erreicht wird (Landweg bleibt bei Bahn/Lkw;
    /// ohne Weltkarte nimmt der Hafen alles) – Lkw nur noch zwischen Firma und Kai, die Fernfracht kostet
    /// <see cref="ShippingRules.SeaFreightShare"/> bzw. <see cref="ShippingRules.InlandFreightShare"/> statt 8 % des Warenwerts.
    /// Kapazität je Tag = Anläufe × Laderaum; die Ladungen warten am Kai auf das nächste Schiff (Umschlag, Hafengeld).
    /// </summary>
    public sealed partial class ShippingSystem
    {
        int lastShipmentId;

        void ConnectGoods()
        {
            var goods = world.Goods;
            if (goods == null) return;
            var previous = goods.PreferredGateway;
            if (previous == null) return; // ohne Gateways gibt es keine Wasserstraßen
            goods.PreferredGateway = (g, import, contract, comp) => HarborNode(g, import, contract, comp) ?? previous(g, import, contract, comp);
            var previousShare = goods.FreightShareFor;
            goods.FreightShareFor = node =>
            {
                var h = HarborOfNode(node?.Id ?? 0);
                if (h != null) return h.ShipClass == ShipCatalog.Coaster ? ShippingRules.SeaFreightShare : ShippingRules.InlandFreightShare;
                return previousShare?.Invoke(node) ?? -1.0;
            };
        }

        void ConnectGateways()
        {
            var gw = world.Gateways;
            if (gw == null) return;
            gw.WaterGatewayOfNode = id =>
            {
                var h = HarborOfNode(id);
                if (h == null) return null;
                foreach (var w in WaterwaysOf(h)) if (w.GatewayA != 0 && w.Passable(h.ShipClass)) return gw.Get(w.GatewayA);
                return null;
            };
            gw.HarborRouteText = country =>
            {
                foreach (var h in harbors)
                    if (h.InService) return $"Seeweg über {h.Name} ({h.ShipClass.DisplayName}, Fracht {(h.ShipClass == ShipCatalog.Coaster ? ShippingRules.SeaFreightShare : ShippingRules.InlandFreightShare) * 100:0.#} % des Warenwerts)";
                return null;
            };
        }

        /// <summary>Ladungen je Tag: Anläufe (alle 3 h) × Laderaum der Schiffsklasse.</summary>
        public int CapacityPerDay(Harbor h) => h == null ? 0 : (24 / ShippingRules.HarborCallEveryHours) * h.ShipClass.Capacity;

        Harbor HarborOfNode(int nodeId)
        {
            if (nodeId == 0) return null;
            foreach (var h in harbors) if (h.InService && h.AccessNodeId == nodeId) return h;
            return null;
        }

        /// <summary>Hafen (in Betrieb, Kapazität frei) im Netzteil → Zufahrtsknoten; nur für Seeweg-Partner (mit Weltkarte).</summary>
        RoadNode HarborNode(GoodType g, bool import, bool contract, int component)
        {
            if (harbors.Count == 0 || g == null) return null;
            var comps = world.Citizens?.Components;
            if (comps == null || component < 0) return null;
            var trade = world.Trade;
            var gw = world.Gateways;
            if (trade != null && gw != null)
            {
                int partner = contract ? gw.ContractPartner(g, import) : gw.SpotPartner(g, import);
                if (partner < 0) return null;
                var route = trade.Geography.Route(trade.PlayerId, partner);
                if (route == null || route.Mode != TransportMode.Sea) return null;
            }
            EnsureAccess();
            CountFreight();
            foreach (var h in harbors)
            {
                if (!h.InService || h.AccessNodeId == 0) continue;
                if (comps.OfNode(h.AccessNodeId) != component) continue;
                if (h.LoadsToday >= CapacityPerDay(h)) continue;
                return roads.GetNode(h.AccessNodeId);
            }
            return null;
        }

        /// <summary>Neue Lieferungen über einen Hafen zählen (Umschlag beim nächsten Schiff).</summary>
        void CountFreight()
        {
            var goods = world.Goods;
            if (goods == null || harbors.Count == 0) return;
            var list = goods.Shipments;
            int i = list.Count - 1;
            while (i >= 0 && list[i].Id > lastShipmentId) i--;
            for (int k = i + 1; k < list.Count; k++)
            {
                var sh = list[k];
                lastShipmentId = Math.Max(lastShipmentId, sh.Id);
                if (sh.Kind != ShipmentKind.Import && sh.Kind != ShipmentKind.Export) continue;
                foreach (var h in harbors)
                {
                    if (h.AccessNodeId == 0 || h.AccessNodeId != sh.ConnectionNodeId) continue;
                    h.LoadsToday++;
                    h.TotalLoads++;
                    h.PendingLoads++;
                    break;
                }
            }
        }
    }
}
