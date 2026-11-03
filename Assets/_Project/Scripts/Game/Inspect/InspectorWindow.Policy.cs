using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Goods;

namespace CityBuilder.Game.Inspect
{
    /// <summary>
    /// Produktion lenken im Inspektor (Reiter „Firma“ eines Industriebetriebs): Schwerpunkt des Standorts und Passung des
    /// Betriebs (Kern/verwandt/fremd → stellt mit der Zeit um), Vorkommen dafür, Förderstufe der Branche und Betrag je Tag.
    /// </summary>
    public sealed partial class InspectorWindow
    {
        void PolicyFields(Ui ui, ProductionPolicy policy, Firm f, CityBuilder.Simulation.Zoning.Building b)
        {
            var pos = b.Position;
            var focus = policy.FocusAt(pos);
            var fit = IndustryBranches.Fit(focus, f.Recipe);
            var home = IndustryBranches.HomeOf(f.Recipe);
            int lvl = policy.SubsidyLevel(home);
            ui.Section("Lenkung", "Schwerpunkt des Standorts · Förderung der Branche");
            using (ui.Fields())
            {
                string fitText = fit == FocusFit.Primary ? "passt (Kernbetrieb)"
                    : fit == FocusFit.Related ? "verwandt – bleibt"
                    : fit == FocusFit.Foreign ? "fremd – stellt mit der Zeit um" : "–";
                ui.Field("Schwerpunkt", focus == IndustryFocus.Auto ? "automatisch (Simulation wählt)" : $"{IndustryBranches.DisplayName(focus)} · {fitText}",
                    fit == FocusFit.Foreign ? Tone.Warn : fit == FocusFit.Primary ? Tone.Good : Tone.None);
                if (focus != IndustryFocus.Auto && IndustryBranches.Deposit(focus) != null)
                {
                    var rating = policy.RateDeposit(focus, pos, out float q);
                    ui.Field("Vorkommen für den Schwerpunkt",
                        $"{ProductionPolicy.DepositLabel(IndustryBranches.Deposit(focus))}: " +
                        (rating == DepositRating.None ? "keins" : rating == DepositRating.Weak ? "schwach" : "gut") + $" ({q * 100f:0} %)",
                        rating == DepositRating.None ? Tone.Bad : rating == DepositRating.Weak ? Tone.Accent : Tone.Good);
                }
                ui.Field("Branche · Förderung", $"{IndustryBranches.DisplayName(home)} · {IndustryBranches.LevelName(lvl)}" +
                    (lvl > 0 ? $" ({Money(policy.SubsidyFor(f))}/Tag)" : ""), lvl > 0 ? Tone.Accent : Tone.None);
            }
        }
    }
}
