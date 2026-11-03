using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Goods;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Produktion lenken – Reiter „Lenkung“ im Fenster Wirtschaft (F5): Kosten der Förderung, Empfehlungen aus den Daten,
    /// je Branche Förderstufe (−/+), Betriebe, Förderung je Woche, Produktion/Import/Export gestern und gemalte Fläche
    /// (anklickbar → Schwerpunkt-Pinsel im Zonen-Werkzeug). Ändert nur über <see cref="ProductionPolicy.ChangeSubsidy"/>.
    /// </summary>
    public sealed partial class GoodsWindow
    {
        void DrawPolicy(Ui ui, GoodsSystem g)
        {
            var p = World?.Policy;
            if (p == null) { ui.Note("Lenkung nicht verfügbar."); return; }

            double perDay = p.SubsidyPerDay();
            using (ui.Grid(3))
            {
                ui.Stat("Förderung je Woche", Money(perDay * 7), "Prognose", perDay > 0 ? Tone.Accent : Tone.None);
                ui.Stat("Förderung gestern", Money(g.Stats.SubsidiesYesterday), "an Firmen gezahlt");
                ui.Stat("Betriebswechsel", $"{p.SwitchesYesterday} / {p.SwitchesTotal}", "gestern / gesamt");
            }
            if (p.SubsidiesSuspended)
                ui.Message("Förderung ruht", "Baustopp: die Stadtkasse ist zu lange überzogen – erst wieder ins Plus kommen.", Tone.Warn);
            if (p.BlockedYesterday > 0)
                ui.Note($"Gestern wollten {p.BlockedYesterday} fremde Betriebe in Schwerpunkt-Gebieten umstellen, fanden aber keinen passenden Betrieb " +
                        "(meist: Vorkommen unter der Mindestgüte).");

            var recs = p.Recommendations();
            ui.Section("Empfehlungen", recs.Count == 0 ? "nichts Auffälliges" : "aus Vorkommen, Importen und Förderung");
            foreach (var r in recs) ui.Text("• " + r, Tone.Info);

            ui.Section("Branchen", "Förderung je Beschäftigtem und Tag: gering 5 € · mittel 12 € · stark 25 €");
            foreach (var branch in IndustryBranches.Branches)
            {
                var b = branch;
                var rep = p.Report(b);
                int lvl = p.SubsidyLevel(b);
                ui.Stepper(IndustryBranches.ShortName(b), IndustryBranches.LevelName(lvl),
                    lvl > 0 ? () => { p.ChangeSubsidy(b, -1); ContentVersion++; } : (System.Action)null,
                    lvl < PolicyRules.MaxLevel ? () => { p.ChangeSubsidy(b, +1); ContentVersion++; } : (System.Action)null,
                    $"{rep.Firms} Betriebe · {rep.Workers}/{rep.Jobs} Jobs" + (rep.SubsidyPerDay > 0 ? $" · {Money(rep.SubsidyPerDay * 7)}/Woche" : ""),
                    lvl > 0 ? Tone.Accent : Tone.Muted);
                ui.Columns("   Produktion · Import/Export gestern", Money(rep.ProducedValue),
                    $"{Money(rep.ImportValue)} / {Money(rep.ExportValue)}", Tone.Muted,
                    rep.ImportValue > rep.ProducedValue && rep.ImportValue > 0 ? Tone.Warn : Tone.Muted);
                ui.FieldLink("   Schwerpunkt-Fläche", rep.FocusHectares > 0.05f ? $"{rep.FocusHectares:0.#} ha · malen" : "malen",
                    () => { Close(); Tool.SelectFocus(b); }, Tone.Muted);
            }
            ui.Note("Schwerpunkt-Gebiete malst du im Zonen-Werkzeug (Z, Kacheln „Schwerpunkt Industrie“): Kernbetriebe bekommen bei der " +
                    "Standortwahl einen starken Bonus, fremde einen Abschlag; fremde Betriebe stellen mit der Zeit um (Verlustbetriebe eher). " +
                    "Rohstoffe brauchen ein Vorkommen über der Mindestgüte. Förderung zahlt die Stadtkasse („Wirtschaftsförderung“) an die " +
                    "Firmen der Branche und lockt neue Betriebe an; ein Teil kommt über die Gewerbesteuer zurück. Bei Baustopp ruht sie.");
        }
    }
}
