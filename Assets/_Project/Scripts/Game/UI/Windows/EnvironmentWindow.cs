using CityBuilder.Game.Environment;
using CityBuilder.Game.Services;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.World;
using UnityEngine;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Umwelt (Etappe 6, rechtes Panel, Knopf „Umwelt“ in der Ansichtsleiste): Luft an den Wohnungen, Smog, Wetter (Wind,
    /// Inversion, Dürre), Quellen des Vortags, Boden/Grundwasser, Wirkungen (Ernte, Tourismus) und die Gegenmaßnahmen mit Kosten:
    /// Umweltauflagen-Stufe (Filter → weniger Emission, höhere Kosten für Firmen und Stadt), Umweltzone, Bäume pflanzen.
    /// Liest die Simulation; Aktionen über die Methoden des <see cref="EcologySystem"/>.
    /// </summary>
    public sealed class EnvironmentWindow : GameWindow
    {
        bool open;
        EnvironmentOverlayView overlay;

        public EnvironmentWindow() : base(WindowSlot.Right) { }

        public bool IsOpen => open && World?.Ecology != null;
        public void Toggle() => open = !open;
        public override float OpenScore => IsOpen ? 1f : 0f;
        public override void Close() => open = false;

        public override void DrawHead(Ui ui)
        {
            var e = World.Ecology;
            var tone = !e.Enabled ? Tone.Muted : e.SmogAlert ? Tone.Bad : e.HomeAirAverage >= 0.15f ? Tone.Warn : Tone.Good;
            string chip = !e.Enabled ? "vereinfacht" : e.SmogAlert ? "Smog-Alarm" : e.HomeAirAverage >= 0.15f ? "belastet" : "gut";
            Head(ui, chip, tone, "Umwelt", "Luft, Boden, Grundwasser und Gegenmaßnahmen");
        }

        public override void DrawBody(Ui ui)
        {
            var w = World;
            var e = w.Ecology;
            if (!e.Enabled) ui.Message("Vereinfachte Umwelt", "Nur Kraftwerke und Verbrennung machen Abgas (altes Modell). Das Spiel schaltet das volle Modell ein.", Tone.Info);

            // --- Luft
            ui.Section("Luft an den Wohnungen");
            using (ui.Grid(3))
            {
                ui.Stat("Durchschnitt", $"{e.HomeAirAverage:0.00}", null, AirTone(e.HomeAirAverage));
                ui.Stat("Höchstwert", $"{e.HomeAirMax:0.00}", null, AirTone(e.HomeAirMax));
                ui.Stat("Im Smog", $"{e.SmogBuildings}", $"von {e.HomeBuildings}", e.SmogAlert ? Tone.Bad : Tone.None);
            }
            ui.Note($"Rasterwert ohne Einheit · Smog ab {EcologyRules.SmogThreshold:0.0} · Smog-Episoden bisher {e.SmogEpisodes}");

            int day = w.Clock.Day;
            var wind = e.Wind;
            float inversion = e.Inversion;
            ui.Section("Wetter heute");
            using (ui.Fields())
            {
                ui.Field("Wind", $"aus {wind.FromName}, {wind.MetersPerSecond:0.0} m/s – Abgas zieht nach {WindState.CompassName(wind.FromDegrees + 180f)}");
                ui.Field("Inversionswetterlage", inversion > 0.5f ? "ja – kalte, stehende Luft hält das Abgas über der Stadt" : inversion > 0.05f ? "schwach" : "nein",
                    inversion > 0.5f ? Tone.Warn : Tone.None);
                ui.Field("Regen", $"{w.Climate.PrecipitationOn(day):0.#} mm (wäscht die Luft aus)");
                ui.Field("Heizbedarf", $"{w.Climate.HeatingFactor(day) * 100f:0} % (Heizungen stoßen aus)");
                float drought = w.Climate.DroughtOn(day), heat = w.Climate.HeatStressOn(day);
                ui.Field("Dürre / Hitze", $"{drought * 100f:0} % / {heat * 100f:0} %", drought > 0.4f || heat > 0.3f ? Tone.Warn : Tone.None);
                ui.Field("Schnee", $"{w.Climate.SnowCoverOn(day) * 100f:0} % Decke");
            }

            ui.Section("Quellen gestern", e.TotalEmissionYesterday > 0f ? $"gesamt {e.TotalEmissionYesterday:0}" : null);
            using (ui.Card())
            {
                Source(ui, e, EmissionSource.Traffic, "Verkehr (Lkw 3,5×, Stau 1,6×)", new Color(0.55f, 0.55f, 0.6f));
                Source(ui, e, EmissionSource.Power, "Kraftwerke (nach Auslastung)", new Color(0.35f, 0.3f, 0.3f));
                Source(ui, e, EmissionSource.Industry, "Industrie (nach Rezept)", new Color(0.6f, 0.4f, 0.25f));
                Source(ui, e, EmissionSource.Heating, "Heizen (Klima, Bauart)", new Color(0.85f, 0.55f, 0.3f));
                Source(ui, e, EmissionSource.Waste, "Müllverbrennung", new Color(0.5f, 0.45f, 0.35f));
            }

            // --- Boden & Wasser
            ui.Section("Boden und Wasser");
            using (ui.Fields())
            {
                ui.Field("Bodenbelastung (Höchstwert)", $"{e.Soil.MaxValue() * 100f:0} % – senkt Fruchtbarkeit (−{EcologyRules.SoilFertilityLoss * 100f:0} % bei 100 %) und Bodenwert");
                ui.Field("Grundwasser (Höchstwert)", $"{e.Groundwater.MaxValue() * 100f:0} % – Brunnen liefern schlechteres Trinkwasser");
                if (w.WaterQuality != null)
                {
                    float wp = w.WaterQuality.Pollution.MaxValue();
                    ui.Field("Fluss/Meer (Höchstwert)", $"{Mathf.Clamp01(wp) * 100f:0} %{(wp >= EcologyRules.FishKillThreshold ? " – Fischsterben" : "")}",
                        wp >= EcologyRules.FishKillThreshold ? Tone.Bad : Tone.None);
                }
                ui.Field("Ernte durch Hitze/Dürre", $"× {e.HarvestStress(day):0.00}");
                ui.Field("Tourismus (vorgemerkt)", $"× {e.TourismFactor:0.00} Anziehung");
            }

            // --- Gegenmaßnahmen
            ui.Section("Umweltauflagen", "Filterpflicht für Kraftwerke, Industrie, Verbrennung");
            using (ui.Card())
            {
                int level = e.Regulation;
                ui.Stepper("Stufe", $"{level} – {EcologyRules.RegulationName(level)}",
                    () => e.SetRegulation(level - 1), () => e.SetRegulation(level + 1),
                    $"Ausstoß × {EcologyRules.FilterFactor(level):0.00}", level > 0 ? Tone.Good : Tone.None);
                ui.Field("Kosten Firmen", $"{e.FirmCostPerDay():N0} € je Tag (mindert Gewinn und Gewerbesteuer)", level > 0 ? Tone.Warn : Tone.Muted);
                ui.Field("Kosten Stadt", $"{e.WeeklyUpkeep():N0} € je Woche (Filter an städtischen Anlagen, Umweltzone, Baumpflege)");
            }

            ui.Section("Umweltzone", "nur saubere Fahrzeuge (Lkw × 0,4, Pkw × 0,85)");
            using (ui.Card())
            {
                ui.Field("Zustand", e.LowEmissionZone ? $"aktiv · Radius {e.ZoneRadius:0} m um ({e.ZoneCenter.X:0}, {e.ZoneCenter.Y:0})" : "keine",
                    e.LowEmissionZone ? Tone.Good : Tone.Muted);
                if (e.LowEmissionZone)
                    ui.Stepper("Radius", $"{e.ZoneRadius:0} m",
                        () => e.SetLowEmissionZone(true, e.ZoneCenter, e.ZoneRadius - 100f),
                        () => e.SetLowEmissionZone(true, e.ZoneCenter, e.ZoneRadius + 100f));
                ui.Note($"Firmen in der Zone zahlen {EcologyRules.ZoneCostPerJobDay:0.#} € je Arbeitsplatz und Tag (neuere Lieferfahrzeuge), die Stadt {EcologyRules.ZoneUpkeepPerWeek:0} € je Woche.");
                using (ui.Actions())
                {
                    if (!e.LowEmissionZone)
                        ui.ActionButton("Zone um die Stadtmitte einrichten", () => e.SetLowEmissionZone(true, e.CityCenter(), e.ZoneRadius), ActionStyle.Primary);
                    else ui.ActionButton("Zone aufheben", () => e.SetLowEmissionZone(false, e.ZoneCenter, e.ZoneRadius), ActionStyle.Danger);
                }
            }

            ui.Section("Bäume pflanzen", "Wald filtert Abgas, Boden erholt sich schneller");
            using (ui.Card())
            {
                ui.Field("Aufgeforstet", $"{e.PlantedCells} Felder · {e.TreesPlanted:N0} Bäume");
                ui.Note($"{EcologyRules.CostPerTree:0} € je Baum, Setzlinge wachsen in etwa zwei Monaten zum Wald. Nur auf freier Fläche (keine Straßen, Gebäude, Bauwerke, Wasser).");
                var serviceTool = Tool.GetComponent<ServiceTool>();
                if (serviceTool != null)
                    using (ui.Actions())
                        ui.ActionButton("Pflanz-Pinsel wählen", () => serviceTool.Select(ServiceTool.ServiceAction.PlantTrees), ActionStyle.Primary,
                            serviceTool.Active && serviceTool.Current == ServiceTool.ServiceAction.PlantTrees);
            }

            if (overlay == null) overlay = Object.FindAnyObjectByType<EnvironmentOverlayView>();
            if (overlay != null)
            {
                ui.Section("Ansichten");
                using (ui.Actions())
                {
                    ui.ActionButton("Luft", () => overlay.Toggle(EnvironmentOverlayView.Overlay.AirPollution), ActionStyle.Secondary,
                        overlay.Current == EnvironmentOverlayView.Overlay.AirPollution);
                    ui.ActionButton("Boden", () => overlay.Toggle(EnvironmentOverlayView.Overlay.Soil), ActionStyle.Secondary,
                        overlay.Current == EnvironmentOverlayView.Overlay.Soil);
                    ui.ActionButton("Grundwasser", () => overlay.Toggle(EnvironmentOverlayView.Overlay.Groundwater), ActionStyle.Secondary,
                        overlay.Current == EnvironmentOverlayView.Overlay.Groundwater);
                }
            }
        }

        static void Source(Ui ui, EcologySystem e, EmissionSource s, string label, Color color)
        {
            float share = e.ShareYesterday(s);
            ui.ValueBar(label, share, 1f, color, $"{share * 100f:0} %");
        }

        static Tone AirTone(float v) => v >= EcologyRules.SmogThreshold ? Tone.Bad : v >= 0.15f ? Tone.Warn : Tone.Good;
    }
}
