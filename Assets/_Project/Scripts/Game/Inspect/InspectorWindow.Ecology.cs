using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.Inspect
{
    /// <summary>Umwelt (Etappe 6) im Inspektor: Gebäude-Umfeld (chronische Luft, Boden, Grundwasser), Firma (Ausstoß, Auflagen-Kosten), Kraftwerk (Auslastung, Filter), Brunnen.</summary>
    public sealed partial class InspectorWindow
    {
        /// <summary>Gebäude, Reiter Umfeld: unter „Umwelt am Gebäude“.</summary>
        void EcologySurroundings(Ui ui, SimulationWorld world, Building b)
        {
            var e = world.Ecology;
            if (e == null || !e.Enabled) return;
            using (ui.Card())
            {
                float exp = e.ExposureAt(b.Position);
                ui.Meter("Luft (Mittel ~10 Tage)", $"{exp:0.00} – {(exp >= EcologyRules.SmogThreshold ? "Smog, Atemwege leiden" : exp >= 0.15f ? "belastet" : "gut")}",
                    Mathf.Clamp01(exp / 0.5f), exp >= EcologyRules.SmogThreshold ? Tone.Bad : exp >= 0.15f ? Tone.Warn : Tone.Good);
                float soil = e.SoilAt(b.Position);
                ui.Meter("Boden (Altlasten)", $"{soil * 100f:0} %", Mathf.Clamp01(soil), soil >= 0.3f ? Tone.Bad : soil >= 0.1f ? Tone.Warn : Tone.Good);
                float gw = e.GroundwaterAt(b.Position);
                ui.Meter("Grundwasser", $"{gw * 100f:0} % belastet", Mathf.Clamp01(gw), gw >= 0.3f ? Tone.Bad : gw >= 0.1f ? Tone.Warn : Tone.Good);
                if (e.InZone(b.Position)) ui.Field("Umweltzone", "liegt in der Umweltzone (nur saubere Fahrzeuge)", Tone.Good);
            }
        }

        /// <summary>Firma: Ausstoß nach Rezept und Kosten der Umweltauflagen (unter der Gewinnrechnung).</summary>
        void EcologyFirm(Ui ui, SimulationWorld world, Firm f)
        {
            var e = world.Ecology;
            if (e == null || !e.Enabled || f?.Recipe == null) return;
            float perJob = EcologyRules.AirPerJob(f.Recipe.Id);
            EcologyRules.GroundPerJob(f.Recipe.Id, out float soil, out float gw);
            double cost = e.FirmCost(f);
            if (perJob <= 0f && soil <= 0f && cost <= 0) return;
            ui.Section("Umwelt", $"Auflagen: {EcologyRules.RegulationName(e.Regulation)}");
            using (ui.Fields())
            {
                if (perJob > 0f) ui.Field("Abgas", $"{e.FirmEmission(f):0.000} je 30 min am Schornstein (Filter × {EcologyRules.FilterFactor(e.Regulation):0.00})",
                    perJob >= 0.0008f ? Tone.Warn : Tone.None);
                if (soil > 0f || gw > 0f) ui.Field("Boden/Grundwasser", soil >= 0.00003f || gw >= 0.00002f ? "belastet den Boden am Standort (Altlasten)" : "gering");
                ui.Field("Umweltkosten", cost > 0 ? $"−{cost:N0} € je Tag (gestern {f.Yesterday.Environment:N0} €)" : "keine", cost > 0 ? Tone.Warn : Tone.Muted);
            }
        }

        /// <summary>Versorgungsbauwerk: Auslastung/Filter (Kraftwerke), Grundwasser am Brunnen.</summary>
        void EcologyFacility(Ui ui, SimulationWorld world, UtilityFacility f)
        {
            var e = world.Ecology;
            if (e == null || !e.Enabled) return;
            var t = f.Type;
            if (t.Pollution > 0f)
            {
                float load = e.PlantLoad(f);
                ui.Field("Ausstoß", $"Auslastung {load * 100f:0} % · Filter × {EcologyRules.FilterFactor(e.Regulation):0.00} ({EcologyRules.RegulationName(e.Regulation)})",
                    load > 0.5f && e.Regulation == 0 ? Tone.Warn : Tone.None);
            }
            if (t.Kind == UtilityKind.Water && !t.NeedsWater && world.WaterQuality != null)
            {
                float q = world.WaterQuality.PumpQuality(f);
                ui.Field("Brunnenwasser", $"{q * 100f:0} % (Grundwasser {e.GroundwaterAt(f.Position) * 100f:0} % belastet)",
                    q < WaterQualitySystem.BadDrinkingWater ? Tone.Warn : Tone.Good);
            }
        }
    }
}
