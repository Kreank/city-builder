using System.Linq;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Safety;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.Inspect
{
    /// <summary>Sicherheit im Inspektor: Brand (Stärke, Feuerwehr vor Ort/unterwegs, Ankunft), Ruine/Sanierung, Kriminalität mit Faktoren und letzten Vorfällen, Stadtübersicht.</summary>
    public sealed partial class InspectorWindow
    {
        /// <summary>Gebäude-Übersicht: laufender Brand, Ruine/Sanierung (mit Wiederaufbau), Kriminalität.</summary>
        void SafetySection(Ui ui, SimulationWorld world, Building b)
        {
            var safety = world.Safety;
            if (safety == null || !safety.Enabled) return;
            var fire = safety.FireAt(b.Id);
            var repair = safety.RepairOf(b.Id);
            if (fire != null)
            {
                ui.Section("Brand", "Feuerwehr");
                using (ui.Card())
                {
                    ui.Meter($"Stärke · {SafetyRules.IntensityText(fire.Intensity)}", $"{fire.Intensity * 100f:0} %", fire.Intensity, Tone.Bad);
                    ui.Meter("Schaden", $"{fire.Damage * 100f:0} %", fire.Damage, fire.Damage >= SafetyRules.SevereDamage ? Tone.Bad : Tone.Warn);
                }
                using (ui.Fields())
                {
                    float minutes = (world.Clock.Tick - fire.StartTick) * TimeModel.GameMinutesPerTick;
                    ui.Field("Brennt seit", $"{minutes:0} min" + (fire.IsReported ? "" : " · noch nicht bemerkt"));
                    ui.Field("Löschfahrzeuge", $"{fire.TrucksOnSite} vor Ort, {fire.TrucksEnRoute} unterwegs",
                        fire.TrucksOnSite > 0 ? Tone.Good : Tone.Warn);
                    float eta = safety.EstimatedArrivalMinutes(fire);
                    if (fire.TrucksOnSite == 0 && eta >= 0f) ui.Field("Ankunft (Schätzung)", $"≈ {eta:0} min");
                    if (fire.IsReported && fire.TrucksOnSite == 0 && fire.TrucksEnRoute == 0)
                        ui.Field("Feuerwehr", fire.MissedDispatches > 0 ? "kein Fahrzeug frei" : "keine Wache erreichbar", Tone.Bad);
                    ui.Field("Löschwasser", fire.Water >= 1f ? "Hydrant (Wassernetz)" : "nur Tankwasser – kein Wasseranschluss", fire.Water >= 1f ? Tone.Good : Tone.Warn);
                    if (fire.Injured > 0) ui.Field("Verletzte", fire.Injured.ToString(), Tone.Warn);
                    if (fire.SourceFireId != 0) ui.Field("Ursache", "übergegriffen vom Nachbargebäude");
                }
            }
            else if (repair != null)
            {
                ui.Section(repair.IsRuin ? "Brandruine" : "Sanierung nach Brand");
                using (ui.Fields())
                {
                    if (repair.IsRuin) ui.Field("Zustand", "ausgebrannt – unbewohnbar", Tone.Bad);
                    else
                    {
                        float days = (repair.UntilTick - world.Clock.Tick) / (float)TimeModel.TicksPerDay;
                        ui.Field(repair.Ruin ? "Wiederaufbau" : "Sanierung", $"noch ≈ {Mathf.Max(0f, days):0.#} Tage", Tone.Accent);
                    }
                    if (repair.Households.Count > 0) ui.Field("Ausgezogen", $"{repair.Households.Count} Haushalte (kommen nach der Sanierung zurück)");
                }
                if (repair.IsRuin)
                {
                    float cost = safety.RebuildCost(b);
                    using (ui.Actions())
                        ui.ActionButton($"Wiederaufbauen ({BuildLabels.Money(cost)})", () => world.Safety.Rebuild(b.Id), ActionStyle.Primary, false,
                            safety.CanRebuild(b.Id), $"{SafetyRules.RebuildCostShare * 100f:0} % der Baukosten (Rest zahlt die Versicherung), Bauzeit ≈ {SafetyRules.RebuildDays:0} Tage");
                    ui.Note("Oder abreißen (unten) und neu setzen.");
                }
            }

            if (!b.IsConstructed) return;
            var f = safety.CrimeFactorsOf(b);
            var risk = safety.FireRiskOf(b);
            ui.Section("Sicherheit", "Ansichten links: Brandgefahr · Kriminalität");
            using (ui.Card())
            {
                ui.Meter($"Kriminalität · {SafetyRules.CrimeText(f.Rate)}", $"{f.Rate * 100f:0} %", Mathf.Clamp01(f.Rate / 0.45f),
                    f.Rate >= 0.3f ? Tone.Bad : f.Rate >= 0.18f ? Tone.Warn : Tone.Good);
                ui.Meter($"Brandgefahr · {SafetyRules.RiskText(risk.Factor)}", $"Faktor {risk.Factor:0.00} ≈ {risk.PerYear * 100f:0.#} % je Jahr",
                    Mathf.Clamp01(risk.Factor / 3f), risk.Factor >= 2f ? Tone.Bad : risk.Factor >= 1.2f ? Tone.Warn : Tone.Good);
            }
            var recent = safety.CrimesAt(b.Id).Take(3).ToList();
            if (recent.Count > 0)
                using (ui.Fields())
                    foreach (var c in recent)
                    {
                        float ago = (world.Clock.Tick - c.Tick) / (float)TimeModel.TicksPerHour;
                        string state = !c.Reported ? "nicht angezeigt" : !c.Closed ? "Polizei unterwegs" : c.Solved ? "aufgeklärt, Festnahme" : "ungeklärt";
                        ui.Field(CrimeKinds.DisplayName(c.Kind), $"vor {(ago < 24f ? $"{ago:0} h" : $"{ago / 24f:0} Tagen")} · {BuildLabels.Money(c.Loss)} · {state}",
                            c.Solved ? Tone.Good : Tone.Warn);
                    }
        }

        /// <summary>Gebäude-Reiter Umfeld: Faktoren der Kriminalität und der Brandgefahr.</summary>
        void SafetyFactors(Ui ui, SimulationWorld world, Building b)
        {
            var safety = world.Safety;
            if (safety == null || !safety.Enabled || !b.IsConstructed) return;
            var f = safety.CrimeFactorsOf(b);
            ui.Section("Kriminalität – Faktoren", "Viertel (≈ 300 m)");
            using (ui.Card())
            {
                Factor(ui, "Arbeitslosigkeit", f.Unemployment, SafetyRules.UnemploymentWeight);
                Factor(ui, "Unzufriedenheit", f.Dissatisfaction, SafetyRules.DissatisfactionWeight);
                Factor(ui, "wenig Bildung", f.LowEducation, SafetyRules.EducationWeight);
                Factor(ui, "Dichte", f.Density, SafetyRules.DensityWeight);
                Factor(ui, "niedriger Bodenwert", f.LowLandValue, SafetyRules.LowLandValueWeight);
                if (f.Shops > 0f) Factor(ui, "Laden (Diebstahl)", f.Shops, SafetyRules.ShopWeight);
                ui.Meter("Polizeiabdeckung", $"−{f.Police * SafetyRules.PoliceReduction * 100f:0} %", f.Police, Tone.Good);
                ui.Meter("Streifenpräsenz", $"−{f.Patrol * SafetyRules.PatrolReduction * 100f:0} %", f.Patrol, Tone.Good);
                ui.Note($"Druck {f.Pressure * 100f:0} % → Rate {f.Rate * 100f:0} % (nachts × {SafetyRules.NightWeight(2):0.#}, tagsüber × {SafetyRules.NightWeight(12):0.#})");
            }
            var r = safety.FireRiskOf(b);
            ui.Section("Brandgefahr – Faktoren", "× multipliziert");
            using (ui.Fields())
            {
                ui.Field("Bauart", $"× {r.Zone:0.00}");
                if (r.RecipeName != null) ui.Field("Betrieb", $"{r.RecipeName} × {r.Recipe:0.00}", r.Recipe > 1.2f ? Tone.Warn : Tone.None);
                ui.Field("Zustand/Alter", $"× {r.Condition:0.00}", r.Condition > 1.3f ? Tone.Warn : Tone.None);
                ui.Field("Müll", $"× {r.Garbage:0.00}", r.Garbage > 1.2f ? Tone.Warn : Tone.None);
                if (r.Power > 1f) ui.Field("Stromausfall (Kerzen)", $"× {r.Power:0.00}", Tone.Warn);
                ui.Field("Größe", $"× {r.Size:0.00}");
                ui.Field("Brandschutz (Feuerwehr)", $"× {r.Prevention:0.00}", Tone.Good);
                ui.Field("Löschwasser", safety.WaterFactor(b) >= 1f ? "Hydrant am Gebäude" : "kein Wasseranschluss – nur Tankwasser",
                    safety.WaterFactor(b) >= 1f ? Tone.Good : Tone.Warn);
            }
        }

        static void Factor(Ui ui, string label, float value, float weight) =>
            ui.Meter(label, $"{value * 100f:0} % · +{value * weight * 100f:0}", value, value >= 0.6f ? Tone.Warn : Tone.Accent);

        /// <summary>Stadtübersicht: Brände und Kriminalität (gestern, gesamt).</summary>
        void SafetyOverview(Ui ui, SimulationWorld world)
        {
            var safety = world.Safety;
            if (safety == null || !safety.Enabled) return;
            var y = safety.Yesterday;
            var total = new SafetyStats();
            safety.Total.AddTo(total);
            safety.Today.AddTo(total);
            ui.Section("Sicherheit", "gestern · gesamt");
            using (ui.Grid(2))
            {
                ui.Stat("Brände jetzt", safety.ActiveFires.Count.ToString(), null, safety.ActiveFires.Count > 0 ? Tone.Bad : Tone.None);
                ui.Stat("Kriminalität Ø", $"{safety.AverageCrimeRate * 100f:0}", "%", safety.AverageCrimeRate >= 0.3f ? Tone.Warn : Tone.None);
            }
            using (ui.Fields())
            {
                ui.Field("Brände", $"{y.Fires} · gesamt {total.Fires} (übergegriffen {total.Spread}, selbst gelöscht {total.SelfExtinguished})");
                ui.Field("Gelöscht / schwer / ausgebrannt", $"{total.Extinguished} / {total.SevereDamage} / {total.TotalLosses}", total.TotalLosses > 0 ? Tone.Warn : Tone.None);
                float resp = total.AverageResponseMinutes;
                ui.Field("Feuerwehr Ø Anfahrt", resp >= 0f ? $"{resp:0} min" : "–");
                if (total.Injured > 0) ui.Field("Verletzte", total.Injured.ToString(), Tone.Warn);
                int ruins = safety.Repairs.Count(r => r.IsRuin), repairing = safety.Repairs.Count(r => !r.IsRuin);
                if (ruins + repairing > 0) ui.Field("Ruinen / in Sanierung", $"{ruins} / {repairing}", ruins > 0 ? Tone.Warn : Tone.Accent);
                ui.Field("Straftaten", $"{y.Crimes} gestern · gesamt {total.Crimes} (Einbruch {total.Burglaries}, Laden {total.Shopliftings}, Vandalismus {total.Vandalism})");
                ui.Field("Aufklärungsquote", total.Crimes > 0 ? $"{total.ClearanceRate * 100f:0} % · Festnahmen {total.Arrests}" : "–");
                ui.Field("Zellen der Wachen", $"{safety.InCustody}/{safety.CustodyCapacity}" + (total.Released > 0 ? $" · {total.Released} mangels Platz frei" : "") + " (Gefängnis folgt)");
                ui.Field("Schaden durch Straftaten", BuildLabels.Money(total.CrimeLoss));
            }
        }
    }
}
