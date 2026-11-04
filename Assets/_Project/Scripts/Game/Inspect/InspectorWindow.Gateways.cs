using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Gateways;
using UnityEngine;

namespace CityBuilder.Game.Inspect
{
    /// <summary>Außenanbindungen im Inspektor: Gateway (Art, Ziel, Zustand, Verkehr/Handel heute) und Liste in der Stadtübersicht.</summary>
    public sealed partial class InspectorWindow
    {
        static Tone GatewayTone(Gateway g) => g.IsOperational ? Tone.Good : g.Kind == GatewayKind.Water && !g.Navigable ? Tone.Muted : Tone.Info;

        bool GatewayHead(Ui ui, SimulationWorld world, Selection s)
        {
            if (s.Kind != SelectionKind.Gateway) return false;
            var g = world.Gateways?.Get(s.Id);
            if (g == null) return false;
            StandardHead(ui, $"Außenanbindung · {g.KindName}", GatewayTone(g), g.Name,
                $"{g.StatusText} · nach {g.CompassText}", false);
            return true;
        }

        bool GatewayBody(Ui ui, SimulationWorld world, Selection s)
        {
            if (s.Kind != SelectionKind.Gateway) return false;
            var gw = world.Gateways;
            var g = gw?.Get(s.Id);
            if (g == null) return false;

            if (g.IsRoad)
                using (ui.Grid(2))
                {
                    ui.Stat("Fahrzeuge rein", g.Today.VehiclesIn.ToString(), $"heute (gestern {g.Yesterday.VehiclesIn})");
                    ui.Stat("Fahrzeuge raus", g.Today.VehiclesOut.ToString(), $"heute (gestern {g.Yesterday.VehiclesOut})");
                    ui.Stat("Zuzug", g.Today.MoveIns.ToString(), $"Haushalte heute (gesamt {g.TotalMoveIns})", g.Today.MoveIns > 0 ? Tone.Good : Tone.None);
                    ui.Stat("Handel", $"{g.Today.Imports}/{g.Today.Exports}", "Import/Export-Ladungen heute");
                }
            using (ui.Fields())
            {
                ui.Field("Art", g.KindName);
                ui.Field("Zustand", g.StatusText, g.IsOperational ? Tone.Good : Tone.Warn);
                ui.Field("Ziel", TargetText(world, g));
                ui.Field("Richtung", g.CompassText);
                if (g.IsRoad)
                {
                    ui.Field("Kapazität", $"{g.Capacity:0} Fz/h je Richtung");
                    ui.Field("Wichtigkeit", $"{g.Importance:0.0} (Durchgangsverkehr)");
                    ui.Field("Handel heute", $"Import {BuildLabels.Money((float)g.Today.ImportValue)} · Export {BuildLabels.Money((float)g.Today.ExportValue)}");
                    ui.Field("Handel gestern", $"{g.Yesterday.Imports} Import · {g.Yesterday.Exports} Export · {g.Yesterday.MoveIns} Haushalte");
                }
                if (g.Kind == GatewayKind.Water)
                {
                    ui.Field("Breite/Tiefe", $"{g.WaterWidth:0} m / {g.WaterDepth:0.0} m");
                    ui.Field("Schiffbar", g.Navigable ? "ja" : "nein (zu schmal/flach)", g.Navigable ? Tone.Good : Tone.Muted);
                }
                if (g.PartnerId != 0)
                {
                    var partner = gw.Get(g.PartnerId);
                    if (partner != null) ui.FieldLink("Gegenstück", partner.Name, () => Go(SelectionKind.Gateway, partner.Id));
                }
            }
            if (g.IsRoad && world.Settings.RequireGatewayForMoveIn && gw.ConnectionHint != null)
                ui.Message("Noch nicht angeschlossen", gw.ConnectionHint, Tone.Warn);
            if (!g.IsOperational)
                ui.Note(g.Kind == GatewayKind.Rail ? "Die Trasse ist freigehalten – Bahnhöfe und Züge kommen in einer späteren Stufe."
                    : g.Kind == GatewayKind.Water ? "Wasserstraße vorbereitet – Hafen und Schiffe kommen später; Seehandel fährt bis dahin per Lkw über die Autobahn."
                    : "Luftweg: einen Flughafen bauen (Bauleiste „Flughafen“, 9) und eine Straße vor das Terminal legen – Einflugschneisen frei halten."); // Flughafen
            if (g.IsRoad) ui.Note("Gehört der Welt: nicht abreißbar, kein Unterhalt. Anschluss über den Zubringer an der Anschlussstelle.");
            return true;
        }

        /// <summary>Ort außerhalb als Zeile: Gateway anklickbar („A 7 Richtung …“), sonst Außenanschluss/Umland.</summary>
        void GatewayField(Ui ui, SimulationWorld world, string label, int nodeId)
        {
            var g = world.Gateways?.OfNode(nodeId);
            if (g != null) ui.FieldLink(label, g.Name, () => Go(SelectionKind.Gateway, g.Id));
            else ui.Field(label, nodeId != 0 ? $"außerhalb (Außenanschluss {nodeId})" : "Umland (ohne Anschluss)");
        }

        static string TargetText(SimulationWorld world, Gateway g)
        {
            switch (g.TargetKind)
            {
                case GatewayTargetKind.Country:
                    return g.TargetCountryId >= 0 && world.Trade != null && world.Gateways != null
                        ? $"{g.TargetName} (Land) · {world.Gateways.TradeRouteText(g.TargetCountryId)}"
                        : $"{g.TargetName} (Land)";
                case GatewayTargetKind.Region: return $"{g.TargetName} (Region im eigenen Land)";
                case GatewayTargetKind.Sea: return g.TargetName;
                default: return g.TargetName;
            }
        }

        /// <summary>Stadtübersicht: alle Außenanbindungen (anklickbar, wenn sie eine Lage haben).</summary>
        void GatewayOverview(Ui ui, SimulationWorld world)
        {
            var gw = world.Gateways;
            if (gw == null || gw.All.Count == 0) return;
            ui.Section("Außenanbindungen");
            if (gw.ConnectionHint != null) ui.Message("Nicht angeschlossen", gw.ConnectionHint, Tone.Warn);
            foreach (var g in gw.All)
            {
                var gate = g;
                string sub = g.IsRoad ? $"{g.StatusText} · heute {g.Today.VehiclesIn} rein / {g.Today.VehiclesOut} raus · {g.Today.MoveIns} Zuzüge"
                                      : g.StatusText;
                ui.ListItem(Initials(g), KindColor(g.Kind), g.Name, sub, g.KindName, GatewayTone(g),
                    g.HasLocation ? () => Go(SelectionKind.Gateway, gate.Id) : (System.Action)null);
            }
        }

        static string Initials(Gateway g)
        {
            switch (g.Kind)
            {
                case GatewayKind.Highway: return "A";
                case GatewayKind.Road: return "L";
                case GatewayKind.Rail: return "B";
                case GatewayKind.Water: return "W";
                default: return "F";
            }
        }

        static Color KindColor(GatewayKind k)
        {
            switch (k)
            {
                case GatewayKind.Highway: return new Color(0.16f, 0.36f, 0.75f);
                case GatewayKind.Road: return new Color(0.95f, 0.75f, 0.2f);
                case GatewayKind.Rail: return new Color(0.55f, 0.45f, 0.38f);
                case GatewayKind.Water: return new Color(0.2f, 0.6f, 0.8f);
                default: return new Color(0.6f, 0.6f, 0.7f);
            }
        }
    }
}
