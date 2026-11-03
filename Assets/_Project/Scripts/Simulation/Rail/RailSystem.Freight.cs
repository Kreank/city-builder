using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Güter über die Bahn (G5/Etappe 8): Ist ein Güterbahnhof an der Hauptstrecke in Betrieb, laufen Import und Export der
    /// Firmen im selben Straßennetzteil über ihn – Lkw nur noch zwischen Firma und Güterbahnhof, die Fernfracht kostet
    /// <see cref="RailRules.RailFreightShare"/> statt 8 % des Warenwerts (Bahn ist über weite Strecken billiger). Kapazität
    /// je Tag = Güterzughalte × Wagen × Ladungen; darüber wieder per Lkw über die Autobahn. Umschlagentgelt an die Kasse.
    /// Zuzug/Handel/Gateway-Zählung: Ankunftsknoten der Bahnhöfe gehören zum Rail-Gateway.
    /// </summary>
    public sealed partial class RailSystem
    {
        int lastShipmentId;

        void ConnectGoods()
        {
            var goods = world.Goods;
            if (goods == null) return;
            var previous = goods.PreferredGateway;
            if (previous == null) return; // ohne Gateways (Tests/Standardwelt) gibt es keine Hauptstrecke → alles wie bisher
            goods.PreferredGateway = (g, import, contract, comp) => FreightNode(comp) ?? previous(g, import, contract, comp);
            goods.FreightShareFor = node => IsFreightNode(node?.Id ?? 0) ? RailRules.RailFreightShare : -1.0;
        }

        void ConnectGateways()
        {
            var gw = world.Gateways;
            if (gw == null) return;
            gw.RailEntryFor = ArrivalNodeFor;
            gw.RailGatewayOfNode = id => IsArrivalNode(id) || IsFreightNode(id) ? (gw.Get(GatewayA) ?? gw.Get(GatewayB)) : null;
        }

        /// <summary>Güterbahnhof (in Betrieb, Kapazität frei) im Netzteil → sein Zugangsknoten; sonst null (Lkw über die Autobahn).</summary>
        RoadNode FreightNode(int component)
        {
            if (!HasMainline) return null;
            var comps = world.Citizens?.Components;
            if (comps == null || component < 0) return null;
            CountFreight();
            foreach (var s in stations)
            {
                if (s.IsPassenger || !s.InService || s.AccessNodeId == 0) continue;
                if (comps.OfNode(s.AccessNodeId) != component) continue;
                if (s.LoadsToday >= FreightCapacityPerDay) continue;
                return roads.GetNode(s.AccessNodeId);
            }
            return null;
        }

        /// <summary>Ladungen je Tag: Güterzughalte (Fahrplan: je Richtung alle 2 h) × Wagen × Ladungen je Wagen.</summary>
        public int FreightCapacityPerDay => 2 * (24 / RailRules.FreightEveryHours) * TrainCatalog.Freight.Wagons * RailRules.LoadsPerWagon;

        bool IsFreightNode(int nodeId)
        {
            if (nodeId == 0) return false;
            foreach (var s in stations) if (!s.IsPassenger && s.InService && s.AccessNodeId == nodeId) return true;
            return false;
        }

        /// <summary>Neue Lieferungen über einen Güterbahnhof zählen (Umschlag, Entgelt, Ladungen für den nächsten Güterzug).</summary>
        void CountFreight()
        {
            var goods = world.Goods;
            if (goods == null) return;
            var list = goods.Shipments;
            int i = list.Count - 1;
            while (i >= 0 && list[i].Id > lastShipmentId) i--;
            for (int k = i + 1; k < list.Count; k++)
            {
                var sh = list[k];
                lastShipmentId = Math.Max(lastShipmentId, sh.Id);
                if (sh.Kind != ShipmentKind.Import && sh.Kind != ShipmentKind.Export) continue;
                foreach (var s in stations)
                {
                    if (s.IsPassenger || s.AccessNodeId == 0 || s.AccessNodeId != sh.ConnectionNodeId) continue;
                    s.LoadsToday++;
                    s.PendingLoads++;
                    pendingFares += RailRules.FreightLoadFee;
                    break;
                }
            }
        }
    }
}
