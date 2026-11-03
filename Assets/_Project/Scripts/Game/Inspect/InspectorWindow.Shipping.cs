using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Shipping;

namespace CityBuilder.Game.Inspect
{
    /// <summary>Wasserstraße im Inspektor: Schiff (Klasse, Tiefgang/Masthöhe, Zustand, Ladung), Hafen (Betrieb, Rinnen, Umschlag), Klappbrücke (Zustand, Öffnungen).</summary>
    public sealed partial class InspectorWindow
    {
        bool ShippingHead(Ui ui, SimulationWorld world, Selection s)
        {
            var sh = world.Shipping;
            if (sh == null) return false;
            switch (s.Kind)
            {
                case SelectionKind.Ship:
                {
                    var ship = sh.GetShip(s.Id);
                    if (ship == null) return false;
                    StandardHead(ui, "Schiff", Tone.Info, $"{ship.Class.DisplayName} #{ship.Id}", ShipStateText(sh, ship), true);
                    return true;
                }
                case SelectionKind.Harbor:
                {
                    var h = sh.GetHarbor(s.Id);
                    if (h == null) return false;
                    StandardHead(ui, "Hafen", h.InService ? Tone.Good : Tone.Warn, h.Name,
                        $"{h.Type.DisplayName} · {h.ShipClass.DisplayName} · {(h.InService ? "in Betrieb" : HarborProblem(h))}", false);
                    return true;
                }
                case SelectionKind.Drawbridge:
                {
                    var d = sh.DrawbridgeOf(s.Id);
                    if (d == null) return false;
                    StandardHead(ui, "Klappbrücke", d.RoadClosed ? Tone.Warn : Tone.Good, "Klappbrücke", ShippingSystem.StateText(d), false);
                    return true;
                }
            }
            return false;
        }

        static string HarborProblem(Harbor h) =>
            !h.HasAccess ? "keine Straße vor der Landseite" : h.BlockedReason ?? "keine Fahrrinne";

        static string ShipStateText(ShippingSystem sh, Ship s)
        {
            var h = sh.GetHarbor(s.HarborId);
            switch (s.State)
            {
                case ShipState.Waiting: return "wartet vor der Karte auf freie Einfahrt";
                case ShipState.WaitingBridge: return "wartet vor einer Brücke";
                case ShipState.WaitingBerth: return $"wartet auf einen Liegeplatz{(h != null ? " in " + h.Name : "")}";
                case ShipState.Moored: return $"liegt am Kai{(h != null ? " in " + h.Name : "")} (Umschlag)";
                default:
                    string where = s.Purpose == ShipPurpose.Inbound ? $" · Ziel {h?.Name ?? "Hafen"}" : s.Purpose == ShipPurpose.Outbound ? " · fährt hinaus" : " · Durchfahrt";
                    return $"{s.Speed * 3.6f:0.0} km/h" + where;
            }
        }

        bool ShippingBody(Ui ui, SimulationWorld world, Selection s)
        {
            var sh = world.Shipping;
            if (sh == null) return false;
            switch (s.Kind)
            {
                case SelectionKind.Ship:
                {
                    var ship = sh.GetShip(s.Id);
                    if (ship == null) return false;
                    var c = ship.Class;
                    using (ui.Fields())
                    {
                        ui.Field("Klasse", $"{c.DisplayName} · {c.Length:0} × {c.Beam:0.#} m · max. {c.Speed * 3.6f:0} km/h");
                        ui.Field("Tiefgang · Masthöhe", $"{c.Draft:0.0} m · {c.AirDraft:0.0} m (braucht {c.RequiredDepth:0.0} m Wasser, {c.RequiredClearance:0.0} m unter Brücken)");
                        ui.Field("Zustand", ShipStateText(sh, ship), ship.State == ShipState.WaitingBridge ? Tone.Warn : Tone.None);
                        string to = ship.Purpose == ShipPurpose.Inbound ? sh.GetHarbor(ship.HarborId)?.Name ?? "Hafen" : world.Gateways?.Get(ship.ToGatewayId)?.Name ?? "–";
                        ui.Field("Von → nach", $"{world.Gateways?.Get(ship.FromGatewayId)?.Name ?? "–"} → {to}");
                        float total = sh.RouteLength(ship);
                        ui.Field("Fahrt", $"{ship.S / System.Math.Max(1f, total) * 100f:0} % von {total:0} m");
                        if (c.Role == ShipRole.Cargo) ui.Field("Ladung", $"{ship.Loads} Lkw-Ladungen (Laderaum {c.Capacity})");
                        else ui.Field("An Bord", $"{ship.Passengers} Personen");
                        if (ship.WaitedSeconds > 1) ui.Field("Gewartet", $"{TimeModel.GameMinutes(ship.WaitedSeconds):0} Spielminuten vor Brücken/Liegeplatz",
                            ship.WaitedSeconds > ShippingRules.LongWait ? Tone.Warn : Tone.None);
                    }
                    using (ui.Actions())
                        ui.ActionButton(panel.Following ? "Folgen aus" : "Folgen", () => panel.Following = !panel.Following,
                            ActionStyle.Secondary, panel.Following, true, "Kamera folgt dem Schiff (F)");
                    return true;
                }
                case SelectionKind.Harbor:
                {
                    var h = sh.GetHarbor(s.Id);
                    if (h == null) return false;
                    using (ui.Grid(2))
                    {
                        ui.Stat("Umschlag", h.LoadsYesterday.ToString(), $"Ladungen gestern (heute {h.LoadsToday}/{sh.CapacityPerDay(h)})");
                        ui.Stat("Anläufe", h.CallsYesterday.ToString(), $"gestern · {h.PendingLoads} Ladungen warten");
                    }
                    using (ui.Fields())
                    {
                        ui.Field("Betrieb", h.InService ? "in Betrieb" : HarborProblem(h), h.InService ? Tone.Good : Tone.Bad);
                        ui.Field("Straßenzufahrt", h.HasAccess ? $"Straße {h.AccessSegmentId}" : "keine (Straße vor die Landseite bauen)", h.HasAccess ? Tone.None : Tone.Bad);
                        ui.Field("Schiffe", $"{h.ShipClass.DisplayName} · {h.Type.Berths} Liegeplätze · Anlauf alle {ShippingRules.HarborCallEveryHours} h");
                        foreach (var w in sh.WaterwaysOf(h))
                        {
                            var g = world.Gateways?.Get(w.GatewayA);
                            var x = w.FirstBlocking(h.ShipClass);
                            ui.Field($"Rinne → {g?.Name ?? "Kartenrand"}", x == null ? $"frei · {w.Length:0} m · Tiefe ≥ {w.MinDepth:0.0} m · {w.Crossings.Count} Brücken"
                                : $"gesperrt: {sh.CrossingName(x)} {System.Math.Max(0f, x.Clearance):0.0} m", x == null ? Tone.Good : Tone.Bad);
                        }
                        double share = h.ShipClass == ShipCatalog.Coaster ? ShippingRules.SeaFreightShare : ShippingRules.InlandFreightShare;
                        ui.Field("Fracht", $"{share * 100f:0.#} % des Warenwerts statt {Simulation.Goods.GoodsRules.WorldFreightShare * 100f:0} % per Lkw");
                        ui.Field("Baukosten", BuildLabels.Money((float)h.Type.Cost));
                        ui.Field("Unterhalt", $"{BuildLabels.Money((float)h.Type.UpkeepPerWeek)}/Woche");
                        double fee = h.ShipClass == ShipCatalog.Coaster ? ShippingRules.CoasterCallFee : ShippingRules.BargeCallFee;
                        ui.Field("Hafengeld", $"{fee:0} € je Anlauf + {ShippingRules.LoadFee:0} € je Ladung");
                    }
                    return true;
                }
                case SelectionKind.Drawbridge:
                {
                    var d = sh.DrawbridgeOf(s.Id);
                    if (d == null) return false;
                    using (ui.Grid(2))
                    {
                        ui.Stat("Öffnungen", d.OpeningsYesterday.ToString(), $"gestern (heute {d.OpeningsToday})");
                        ui.Stat("Gesperrt", $"{TimeModel.GameMinutes(d.ClosedSecondsToday):0}", "Spielminuten heute");
                    }
                    using (ui.Fields())
                    {
                        ui.Field("Zustand", ShippingSystem.StateText(d), d.RoadClosed ? Tone.Warn : Tone.Good);
                        ui.Field("Lichte Höhe geschlossen", $"{System.Math.Max(0f, d.ClosedClearance):0.0} m – größere Schiffe lassen öffnen");
                        ui.Field("Schiffe", $"{d.ShipsRequesting} angemeldet · {d.ShipsWaiting} warten");
                        ui.Field("Ablauf", $"Warnen {ShippingRules.DrawbridgeWarn:0} s · Heben {ShippingRules.DrawbridgeRaise:0} s · offen ≤ {ShippingRules.DrawbridgeMaxOpen:0} s · " +
                                           $"Senken {ShippingRules.DrawbridgeLower:0} s · Straße danach ≥ {ShippingRules.DrawbridgeMinRoadOpen:0} s frei (Verkehrszeit)");
                        ui.Field("Unterhalt", $"{BuildLabels.Money((float)ShippingRules.DrawbridgeUpkeep)}/Woche (Brückenwärter, Antrieb)");
                    }
                    return true;
                }
            }
            return false;
        }
    }
}
