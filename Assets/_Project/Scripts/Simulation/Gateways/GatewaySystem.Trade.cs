using System;
using System.Numerics;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Trade;

namespace CityBuilder.Simulation.Gateways
{
    /// <summary>
    /// Handel über Anschlüsse: G5-Import/Export und Etappe-8-Verträge fahren über den Straßen-Gateway in Richtung des
    /// Partnerlandes. Landweg → Gateway Richtung Hauptstadt des Landes; Seeweg → vorerst ebenfalls Straße, und zwar zum
    /// nächsten Hafen außerhalb (Gateway Richtung Hafen des Spielerlandes), markiert „bis Wasserstraße/Hafen fertig“.
    /// </summary>
    public sealed partial class GatewaySystem
    {
        // Je Gut das günstigste Lieferland (Import) bzw. der beste Abnehmer (Export) am Weltmarkt – täglich neu
        int partnerDay = int.MinValue;
        readonly int[] spotImportPartner = new int[GoodsCatalog.Count];
        readonly int[] spotExportPartner = new int[GoodsCatalog.Count];

        /// <summary>
        /// Hook für <c>GoodsSystem.PreferredGateway</c>: Knoten des Gateways Richtung Partnerland (Import = Einfahrt,
        /// Export = Ausfahrt) im Netzteil der Firma; null = nächster Außenanschluss wie bisher.
        /// </summary>
        public RoadNode TradeNode(GoodType g, bool import, bool contract, int component)
        {
            if (g == null) return null;
            int partner = contract ? ContractPartner(g, import) : SpotPartner(g, import);
            var gw = partner >= 0 ? GatewayToward(partner) : null;
            if (gw == null) return null;
            var node = world.Roads.GetNode(import ? gw.EntryNodeId : gw.ExitNodeId);
            if (node == null || world.Traffic == null || !world.Traffic.IsExternalUsable(node, import)) return null;
            var comps = world.Citizens?.Components;
            if (comps != null && comps.OfNode(node.Id) != component) return null;
            return node;
        }

        /// <summary>Handelspartner eines laufenden Vertrags für dieses Gut (Lieferland bei Import, Käuferland bei Export); −1 = keiner.</summary>
        public int ContractPartner(GoodType g, bool import)
        {
            var trade = world.Trade;
            if (trade == null) return -1;
            int player = trade.PlayerId;
            TradeContract best = null;
            foreach (var c in trade.Contracts)
            {
                if (!c.IsActive || c.Kind != ContractKind.Supply || c.Good != g) continue;
                if (import ? c.Buyer != player : c.Seller != player) continue;
                if (best == null || c.Id < best.Id) best = c;
            }
            return best == null ? -1 : import ? best.Seller : best.Buyer;
        }

        /// <summary>Am Weltmarkt: günstigstes Lieferland (Preis + Fracht) bzw. bester Abnehmer (Preis − Fracht); −1 = keins.</summary>
        public int SpotPartner(GoodType g, bool import)
        {
            var trade = world.Trade;
            if (trade == null) return -1;
            int day = trade.Day;
            if (day != partnerDay)
            {
                partnerDay = day;
                int player = trade.PlayerId;
                foreach (var good in GoodsCatalog.All)
                {
                    int bi = -1, be = -1;
                    double bestImport = double.MaxValue, bestExport = double.MinValue;
                    for (int c = 0; c < trade.Nations.Count; c++)
                    {
                        if (c == player) continue;
                        var route = trade.Geography.Route(player, c);
                        if (route == null) continue;
                        double price = trade.CountryPrice(c, good), freight = route.UnitCost(good);
                        if (price + freight < bestImport) { bestImport = price + freight; bi = c; }
                        if (price - freight > bestExport) { bestExport = price - freight; be = c; }
                    }
                    spotImportPartner[good.Index] = bi;
                    spotExportPartner[good.Index] = be;
                }
            }
            return import ? spotImportPartner[g.Index] : spotExportPartner[g.Index];
        }

        /// <summary>
        /// Straßen-Gateway in Richtung eines Landes: Landweg → Richtung Handelsort des Landes, Seeweg → Richtung Hafen
        /// des Spielerlandes (Straße bis zum Hafen, bis Wasserstraße/Hafen fertig). null ohne Straßen-Gateway.
        /// </summary>
        public Gateway GatewayToward(int country) => GatewayToward(country, out _);

        public Gateway GatewayToward(int country, out bool viaPort)
        {
            viaPort = false;
            var dir = DirectionToward(country, out viaPort);
            Gateway best = null;
            float bestDot = float.MinValue;
            foreach (var g in gateways)
            {
                if (!g.IsRoad || !g.IsOperational) continue;
                float d = dir == Vector2.Zero ? -g.Id : Vector2.Dot(g.Direction, dir);
                if (d > bestDot + 1e-5f) { bestDot = d; best = g; }
            }
            return best;
        }

        /// <summary>Richtung (Ost/Nord) von der Stadt zum Partner bzw. zum Hafen (Seeweg).</summary>
        public Vector2 DirectionToward(int country, out bool viaPort)
        {
            viaPort = false;
            var trade = world.Trade;
            var planet = world.Planet;
            var start = planet?.StartRegion;
            if (trade == null || start == null || country < 0 || country >= planet.Countries.Count) return Vector2.Zero;
            int player = trade.PlayerId;
            int cell;
            var route = trade.Geography.Route(player, country);
            if (route != null && route.Mode == TransportMode.Sea)
            {
                viaPort = true;
                cell = trade.Geography.PortOf(player);
                if (cell == start.CenterCell || cell < 0)
                    return start.CoastDirection.HasValue && start.CoastDirection.Value.LengthSquared() > 1e-6f
                        ? Vector2.Normalize(start.CoastDirection.Value)
                        : Toward(start, trade.Geography.LocationOf(country));
            }
            else cell = trade.Geography.LocationOf(country);
            return Toward(start, cell);
        }

        Vector2 Toward(World.Region start, int cell)
        {
            var planet = world.Planet;
            if (cell < 0) return Vector2.Zero;
            return GatewayGenerator.Bearing(start.Latitude, start.Longitude, planet.LatitudeOf(cell), planet.Longitude(planet.XOf(cell)));
        }

        /// <summary>Text für Fenster/Inspektor: über welchen Anschluss der Handel mit einem Land läuft.</summary>
        public string TradeRouteText(int country)
        {
            var gw = GatewayToward(country, out bool viaPort);
            if (viaPort) { var harbor = HarborRouteText?.Invoke(country); if (harbor != null) return harbor; } // --- Wasserstraße: eigener Hafen
            if (gw == null) return "kein Straßen-Anschluss";
            var route = world.Trade?.Geography.Route(world.Trade.PlayerId, country);
            if (viaPort) return $"Seeweg: Lkw über {gw.Name} zum Hafen außerhalb (bis Wasserstraße/Hafen fertig)";
            return route != null ? $"Landweg über {gw.Name} ({route.LandKm:0} km)" : $"über {gw.Name}";
        }
    }
}
