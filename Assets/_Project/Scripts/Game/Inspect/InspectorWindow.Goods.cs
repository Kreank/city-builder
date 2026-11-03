using System.Collections.Generic;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Game.Inspect
{
    /// <summary>
    /// G5 Warenketten im Inspektor: Reiter „Firma“ am Gebäude (Rolle, Rezept, Lager, Gewinn gestern/heute, Marge,
    /// Produktivität, Zustand, laufende Lieferungen mit anklickbaren Lkw) und Ladung eines Lkw (<c>ShipmentForTrip</c>).
    /// Liest nur die Abfrage-API des <see cref="GoodsSystem"/>.
    /// </summary>
    public sealed partial class InspectorWindow
    {
        readonly List<Shipment> firmShipments = new List<Shipment>();

        void FirmTab(Ui ui, SimulationWorld world, Building b)
        {
            var g = world.Goods;
            var f = g?.FirmAt(b.Id);
            if (f == null) { ui.Note("Keine Firma in diesem Gebäude."); return; }
            var r = f.Recipe;
            double today = f.Today.Profit, yesterday = f.Yesterday.Profit;
            using (ui.Grid(3))
            {
                ui.Stat("Gewinn gestern", Signed(yesterday), null, yesterday >= 0 ? Tone.Good : Tone.Bad);
                ui.Stat("Gewinn heute", Signed(today), "bisher", today >= 0 ? Tone.Good : Tone.Warn);
                ui.Stat("Marge", $"{f.Margin * 100:0} %", "geglättet", f.Margin >= 0 ? Tone.None : Tone.Warn);
            }
            using (ui.Card())
            {
                ui.Meter("Produktivität", $"{f.Productivity * 100f:0} %", System.Math.Min(1f, f.Productivity),
                    f.Productivity >= 0.8f ? Tone.Good : f.Productivity >= 0.4f ? Tone.Accent : Tone.Bad);
                ui.Note($"{Firm.StatusText(f.Status)} · Belegschaft {f.Workers}/{f.Jobs} · Fachkräfte {f.SkillCoverage * 100f:0} %" +
                        (r.Form == IndustryForm.Agriculture ? $" · Ernte ×{g.HarvestToday:0.00}" : ""));
            }

            ui.Section("Firma", RecipeCatalog.DisplayName(r.Role));
            using (ui.Fields())
            {
                ui.Field("Betrieb", r.Name + (r.Form != IndustryForm.None ? $" ({RecipeCatalog.DisplayName(r.Form)})" : ""));
                ui.Field("Zustand", Firm.StatusText(f.Status), f.Status == FirmStatus.Producing || f.Status == FirmStatus.Operating ? Tone.Good : Tone.Warn);
                if (r.Deposit != null)
                    ui.Field("Vorkommen", $"{ResourceIds.DisplayName(r.Deposit)} {f.DepositQuality * 100f:0} % (mind. {r.MinDeposit * 100f:0} %)",
                        f.DepositQuality < r.MinDeposit ? Tone.Bad : Tone.None);
                if (r.IsIndustry) ui.Field("Strom · Wasser (Rezept)", $"{r.PowerPerJob * f.Jobs:0} kW · {r.WaterPerJob * f.Jobs:0.#} m³/h");
                ui.Field("Kasse", Money(f.Cash), f.Cash < 0 ? Tone.Warn : Tone.None);
                if (f.LossDays > 0) ui.Field("Verlusttage am Stück", $"{f.LossDays} (Pleite ab {GoodsRules.BankruptAfterLossDays})", Tone.Warn);
            }
            if (r.IsIndustry && world.Policy != null) PolicyFields(ui, world.Policy, f, b); // Produktion lenken

            ui.Section("Gewinnrechnung", "gestern · heute bisher");
            using (ui.Fields())
            {
                ui.Columns("", "gestern", "heute", header: true);
                ui.Columns("Erlös", Money(f.Yesterday.Revenue), Money(f.Today.Revenue));
                ui.Columns("Wareneinsatz", "−" + Money(f.Yesterday.InputCost), "−" + Money(f.Today.InputCost));
                ui.Columns("Löhne", "−" + Money(f.Yesterday.Wages), "(abends)");
                ui.Columns("Transport", "−" + Money(f.Yesterday.Transport), "−" + Money(f.Today.Transport));
                if (f.Yesterday.Subsidy > 0 || f.Today.Subsidy > 0) // Produktion lenken
                    ui.Columns("Förderung", "+" + Money(f.Yesterday.Subsidy), "(abends)");
                ui.Columns("Gewinn", Signed(yesterday), Signed(today), yesterday >= 0 ? Tone.Good : Tone.Bad, today >= 0 ? Tone.Good : Tone.Warn);
            }

            if (r.Inputs.Length > 0)
            {
                ui.Section(f.Role == FirmRole.Retail ? "Regale" : "Eingangslager", "Bestand / Kapazität · unterwegs");
                foreach (var i in r.Inputs)
                {
                    float cap = f.InputCapacityOf(i.Good);
                    float have = f.InputOf(i.Good);
                    ui.Meter(i.Good.Name, $"{have:0.#}/{cap:0} · +{f.IncomingOf(i.Good):0.#}", cap > 0 ? have / cap : 0f,
                        have <= 0.01f ? Tone.Bad : have < cap * GoodsRules.ReorderLevel ? Tone.Accent : Tone.Good);
                }
            }
            if (r.Outputs.Length > 0)
            {
                ui.Section("Ausgangslager", "Bestand / Kapazität · lokaler Preis");
                foreach (var o in r.Outputs)
                {
                    float cap = f.OutputCapacityOf(o.Good);
                    float have = f.OutputOf(o.Good);
                    ui.Meter(o.Good.Name, $"{have:0.#}/{cap:0} · {g.Market(o.Good).LocalPrice:0} €", cap > 0 ? have / cap : 0f,
                        have >= cap * 0.99f ? Tone.Bad : have >= cap * GoodsRules.ExportFill ? Tone.Accent : Tone.Good);
                }
            }

            g.ShipmentsOf(b.Id, firmShipments);
            if (firmShipments.Count == 0) return;
            ui.Section($"Lieferungen ({firmShipments.Count})", "Lkw anklickbar");
            using (ui.Fields())
            {
                int n = 0;
                foreach (var s in firmShipments)
                {
                    if (++n > 12) { ui.Note($"… und {firmShipments.Count - 12} weitere"); break; }
                    string dir = s.FromBuildingId == b.Id ? "aus" : "ein";
                    string text = $"{s.Amount:0.#} {s.Good.Name} · {KindName(s)} · {(s.FromBuildingId == b.Id ? Other(s.ToBuildingId) : Other(s.FromBuildingId))}";
                    var v = s.IsTruck ? TruckOf(world, s) : null;
                    if (v != null) ui.FieldLink(dir, text + $" · Lkw #{v.Id}", () => Go(SelectionKind.Vehicle, v.Id));
                    else ui.Field(dir, text + (s.IsTruck ? " · Lkw" : " · ohne Fahrzeug"));
                }
            }
        }

        /// <summary>Ladung eines Lkw (Lieferfahrt des Warensystems) im Fahrzeug-Inspektor. false = kein Lkw des Warensystems.</summary>
        bool ShipmentSection(Ui ui, SimulationWorld world, Vehicle v)
        {
            var s = world.Goods?.ShipmentForTrip(v.TripId);
            if (s == null) return false;
            ui.Section("Ladung", "Lieferfahrt");
            using (ui.Fields())
            {
                ui.Field("Gut", $"{s.Amount:0.#} {s.Good.Name} ({GoodsCatalog.DisplayName(s.Good.Tier)})");
                ui.Field("Art", KindName(s));
                if (s.FromBuildingId != 0 && world.City.GetBuilding(s.FromBuildingId) != null)
                    ui.FieldLink("Von", FirmName(world, s.FromBuildingId), () => Go(SelectionKind.Building, s.FromBuildingId));
                else GatewayField(ui, world, "Von", s.ConnectionNodeId); // Außenanbindung: welcher Gateway
                if (s.ToBuildingId != 0 && world.City.GetBuilding(s.ToBuildingId) != null)
                    ui.FieldLink("Nach", FirmName(world, s.ToBuildingId), () => Go(SelectionKind.Building, s.ToBuildingId));
                else GatewayField(ui, world, "Nach", s.ConnectionNodeId);
                ui.Field("Warenwert", Money(s.Value));
                ui.Field("Transport (geschätzt)", Money(s.TransportCost) + " · wird mit der echten Fahrzeit abgerechnet");
            }
            return true;
        }

        static Vehicle TruckOf(SimulationWorld world, Shipment s)
        {
            foreach (var v in world.Traffic.Vehicles) if (v.TripId == s.TripId) return v;
            return null;
        }

        static string FirmName(SimulationWorld world, int buildingId)
        {
            var f = world.Goods?.FirmAt(buildingId);
            return f != null ? $"{f.Recipe.Name} #{buildingId}" : $"Gebäude #{buildingId}";
        }

        static string KindName(Shipment s) => s.Kind == ShipmentKind.Import ? "Import" : s.Kind == ShipmentKind.Export ? "Export" : "lokal";
        static string Other(int id) => id != 0 ? $"#{id}" : "Weltmarkt";
        static string Money(double v) => $"{v:N0} €";
        static string Signed(double v) => (v >= 0 ? "+" : "−") + Money(System.Math.Abs(v));
    }
}
