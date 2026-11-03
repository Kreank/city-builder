using System.Collections.Generic;
using CityBuilder.Game.Economy;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Economy;
using UnityEngine;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Stadtkasse (mittiges Fenster, Taste M oder Klick auf den Kontostand): Kontostand, Kreditrahmen/Baustopp,
    /// Tagesprognose, Wochenbilanz je Kategorie (letzte/laufende Woche, Dienste je Name), Steuersätze ± mit
    /// Nachfragefaktor und Saldo-Balken der letzten 12 Wochen. Zustand (offen/zu, Taste M) bleibt im
    /// <see cref="BudgetPanel"/>; Steuersätze ändern über <see cref="EconomySystem.ChangeTaxRate"/>.
    /// </summary>
    public sealed class BudgetWindow : GameWindow
    {
        const int ChartWeeks = 12;

        readonly BudgetPanel panel;
        readonly List<double> nets = new List<double>(ChartWeeks);
        BudgetPeriod estimate;
        float estimateAt = -999f;

        public BudgetWindow(BudgetPanel panel) : base(WindowSlot.Center) { this.panel = panel; }

        EconomySystem Economy => World?.Economy;

        public override float OpenScore => panel != null && panel.Visible && Economy != null ? 1f : 0f;

        public override void Close() => panel.Visible = false;

        BudgetPeriod Estimate(EconomySystem e)
        {
            if (estimate == null || Time.unscaledTime - estimateAt > 1f)
            {
                estimate = e.EstimateDay();
                estimateAt = Time.unscaledTime;
            }
            return estimate;
        }

        public override void DrawHead(Ui ui)
        {
            var e = Economy;
            string state = e.UnlimitedFunds ? "Sandbox: unbegrenztes Geld" : e.ConstructionFrozen ? "Baustopp" : e.IsOverdrawn ? "überzogen" : "Konto im Plus";
            Tone tone = e.UnlimitedFunds ? Tone.Info : e.ConstructionFrozen ? Tone.Bad : e.IsOverdrawn ? Tone.Warn : Tone.Good;
            Head(ui, "Stadtkasse (M)", tone, Money(e.Balance), state);
        }

        public override void DrawBody(Ui ui)
        {
            var e = Economy;
            var est = Estimate(e);
            var last = e.LastWeek;

            using (ui.Grid(3))
            {
                ui.Stat("Prognose heute", Signed(est.Net), null, est.Net >= 0 ? Tone.Good : Tone.Warn);
                ui.Stat("Letzte Woche", last != null ? Signed(last.Net) : "–", null, last == null ? Tone.None : last.Net >= 0 ? Tone.Good : Tone.Warn);
                ui.Stat("Verfügbar", e.UnlimitedFunds ? "∞" : Money(e.AvailableFunds));
            }

            if (e.UnlimitedFunds)
                ui.Message("Sandbox", "Unbegrenztes Geld – Kosten werden nur gezählt.", Tone.Info);
            else
            {
                if (e.ConstructionFrozen)
                    ui.Message("Baustopp", "Zu lange im Minus. Keine Neubauten, bis der Kontostand wieder ≥ 0 ist.", Tone.Bad);
                else if (e.IsOverdrawn)
                    ui.Message($"Überzogen seit {e.DaysInDebt} Tagen",
                        $"{EconomyRules.DailyOverdraftInterest * 100:0.0#} % Zinsen je Tag · Baustopp in {e.DaysUntilFreeze} Tagen", Tone.Warn);
                using (ui.Fields())
                    ui.Field("Verfügbar inkl. Kreditrahmen", $"{Money(e.AvailableFunds)} (Rahmen {Money(e.CreditLimit)})");
            }

            ui.Section("Prognose je Tag");
            using (ui.Fields())
            {
                ui.Field("Einnahmen", "+" + Money(est.Income), Tone.Good);
                ui.Field("Ausgaben", "−" + Money(est.Expense), Tone.Warn);
                ui.TotalRow("Saldo", Signed(est.Net), est.Net >= 0 ? Tone.Good : Tone.Warn);
            }

            ui.Section("Wochenbilanz", last != null ? $"letzte = Woche {last.Index + 1}" : "noch keine Woche abgeschlossen");
            using (ui.Card())
            {
                ui.Columns("Kategorie", "letzte Woche", $"diese ({e.CurrentWeek.Days}/7 T.)", header: true);
                for (int i = 0; i < Budget.Count; i++)
                {
                    var c = (BudgetCategory)i;
                    double a = last?.Amount(c) ?? 0, b = e.CurrentWeek.Amount(c);
                    if (a == 0 && b == 0 && est.Amount(c) == 0) continue;
                    bool income = Budget.IsIncome(c);
                    ui.Columns(Budget.DisplayName(c), Colored(a, income), Colored(b, income), ToneOf(a, income), ToneOf(b, income));
                }
                if (last != null)
                    foreach (var kv in last.ServiceUpkeep)
                        ui.Columns("   " + kv.Key, "−" + Money(kv.Value), "", Tone.Muted);
                ui.Divider();
                ui.Columns("Saldo", last != null ? Signed(last.Net) : "–", Signed(e.CurrentWeek.Net),
                    last == null ? Tone.None : last.Net >= 0 ? Tone.Good : Tone.Warn, e.CurrentWeek.Net >= 0 ? Tone.Good : Tone.Warn, header: true);
            }

            ui.Section("Steuersätze", "± je Prozentpunkt");
            using (ui.Fields())
                for (int i = 0; i < Budget.TaxKinds; i++)
                {
                    var k = (TaxKind)i;
                    float rate = e.TaxRate(k);
                    float f = EconomySystem.DemandFactorForRate(rate);
                    ui.Stepper(Budget.DisplayName(k), $"{rate * 100f:0} %",
                        () => { e.ChangeTaxRate(k, -EconomyRules.TaxStep); estimate = null; },
                        () => { e.ChangeTaxRate(k, EconomyRules.TaxStep); estimate = null; },
                        $"Nachfrage ×{f:0.00}", f < 0.999f ? Tone.Warn : f > 1.001f ? Tone.Good : Tone.Muted);
                }
            ui.Note($"Standard {EconomyRules.DefaultTaxRate * 100f:0} % (Nachfrage ×1). Darüber −{EconomyRules.DemandPenaltyPerPoint * 100f:0} % " +
                    $"Nachfrage je Prozentpunkt, darunter +{EconomyRules.DemandBonusPerPoint * 100f:0} %.");

            ui.Section("Saldo je Woche", $"letzte {ChartWeeks} Wochen");
            var h = e.History;
            nets.Clear();
            double max = 1;
            for (int i = Mathf.Max(0, h.Count - ChartWeeks); i < h.Count; i++)
            {
                nets.Add(h[i].Net);
                max = System.Math.Max(max, System.Math.Abs(h[i].Net));
            }
            if (nets.Count == 0) ui.Note("Noch keine abgeschlossene Woche.");
            else ui.WeekBars(nets, $"±{Money(max)}", ChartWeeks);
        }

        static string Money(double v) => BuildLabels.Money(v);
        static string Signed(double v) => (v >= 0 ? "+" : "−") + Money(System.Math.Abs(v));

        static string Colored(double amount, bool income) =>
            amount == 0 ? "–" : (income ? "+" : "−") + Money(amount);

        static Tone ToneOf(double amount, bool income) => amount == 0 ? Tone.Muted : income ? Tone.Good : Tone.Warn;
    }
}
