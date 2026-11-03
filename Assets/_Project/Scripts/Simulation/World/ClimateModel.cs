using System;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Simulation.World
{
    public enum Season { Spring, Summer, Autumn, Winter }

    /// <summary>Klimakennwerte eines Ortes (aus der Weltzelle/Region oder Standard „gemäßigt, Mitteleuropa“).</summary>
    public sealed class ClimateParams
    {
        /// <summary>Jahresmitteltemperatur °C.</summary>
        public float MeanTemperature = 9.5f;
        /// <summary>Halbe Jahresschwankung (K): kältester Tag ≈ Mittel − Amplitude.</summary>
        public float Amplitude = 9f;
        /// <summary>Jahresniederschlag mm.</summary>
        public float AnnualPrecipitation = 750f;
        /// <summary>Geografische Breite (Grad, negativ = Südhalbkugel → Jahreszeiten vertauscht).</summary>
        public float Latitude = 50f;

        public static ClimateParams Default => new ClimateParams();

        public static ClimateParams FromRegion(Region r) => new ClimateParams
        {
            MeanTemperature = r.MeanTemperature,
            Amplitude = r.SeasonalAmplitude,
            AnnualPrecipitation = r.Precipitation,
            Latitude = r.Latitude,
        };

        public static ClimateParams FromCell(Planet p, int cell) => new ClimateParams
        {
            MeanTemperature = p.Temperature[cell],
            Amplitude = p.Amplitude[cell],
            AnnualPrecipitation = p.Precipitation[cell],
            Latitude = p.LatitudeOf(cell),
        };
    }

    /// <summary>Wetter eines Tages.</summary>
    public readonly struct ClimateDay
    {
        public readonly int Day;
        public readonly Season Season;
        /// <summary>Tagesmitteltemperatur °C.</summary>
        public readonly float Temperature;
        /// <summary>Niederschlag mm.</summary>
        public readonly float Precipitation;
        /// <summary>0..1 Wachstumsbedingungen (Ernte).</summary>
        public readonly float Harvest;
        /// <summary>0..1,5 Heizbedarf (0 ab 15 °C).</summary>
        public readonly float Heating;
        /// <summary>0..1,5 Kühlbedarf (0 bis 22 °C).</summary>
        public readonly float Cooling;

        public ClimateDay(int day, Season season, float temperature, float precipitation, float harvest, float heating, float cooling)
        {
            Day = day; Season = season; Temperature = temperature; Precipitation = precipitation;
            Harvest = harvest; Heating = heating; Cooling = cooling;
        }
    }

    /// <summary>
    /// Klima eines Ortes über den Kalender (360 Tage, Tag 0 = 1. Januar): Jahresgang der Temperatur
    /// (Kosinus, kältester Tag Mitte Januar bzw. Mitte Juli auf der Südhalbkugel) + Wetterrauschen über einige
    /// Tage, Niederschlag als nasse/trockene Tage (Tropen mit Regenzeit im Sommer). Alles deterministisch
    /// aus Seed und Tag – keine Zustände, beliebig abfragbar. Faktoren für Ernte und Heiz-/Kühlbedarf.
    /// </summary>
    public sealed class ClimateModel
    {
        readonly int seed;
        readonly float annualHarvest;

        public ClimateParams Parameters { get; }
        /// <summary>Uhr für <see cref="Today"/> (kann null sein, dann nur die Abfragen je Tag).</summary>
        public SimulationClock Clock { get; }

        public ClimateModel(ClimateParams parameters, int seed, SimulationClock clock = null)
        {
            Parameters = parameters ?? ClimateParams.Default;
            this.seed = seed;
            Clock = clock;
            float sum = 0f;
            for (int d = 0; d < SimulationClock.DaysPerYear; d++) sum += HarvestFactor(d);
            annualHarvest = sum / SimulationClock.DaysPerYear;
        }

        public bool SouthernHemisphere => Parameters.Latitude < 0f;
        public float MeanTemperature => Parameters.MeanTemperature;
        public float AnnualPrecipitation => Parameters.AnnualPrecipitation;
        /// <summary>Mittlerer Erntefaktor über das Jahr (0..1).</summary>
        public float AnnualHarvestFactor => annualHarvest;

        static int DayOfYear(int day) => ((day % SimulationClock.DaysPerYear) + SimulationClock.DaysPerYear) % SimulationClock.DaysPerYear;

        public Season SeasonOn(int day)
        {
            int month = DayOfYear(day) / 30;           // 0 = Januar
            if (SouthernHemisphere) month = (month + 6) % 12;
            if (month == 11 || month <= 1) return Season.Winter;
            if (month <= 4) return Season.Spring;
            if (month <= 7) return Season.Summer;
            return Season.Autumn;
        }

        /// <summary>Jahresgang ohne Wetter: −1 am kältesten, +1 am wärmsten Tag.</summary>
        float SeasonalWave(int day)
        {
            float phase = (DayOfYear(day) - 15f) / SimulationClock.DaysPerYear * MathF.PI * 2f;
            float w = -MathF.Cos(phase);
            return SouthernHemisphere ? -w : w;
        }

        /// <summary>Temperatur des Jahresgangs ohne Wetter (°C).</summary>
        public float NormalTemperatureOn(int day) => Parameters.MeanTemperature + Parameters.Amplitude * SeasonalWave(day);

        /// <summary>Tagesmitteltemperatur °C (Jahresgang + Wetterlagen über ~4 Tage + Tagesrauschen).</summary>
        public float TemperatureOn(int day)
        {
            float weather = (WorldNoise.Value1(day / 4f, seed + 17) - 0.5f) * 8f + (WorldNoise.Hash01(day, 3, 0, seed) - 0.5f) * 2f;
            return NormalTemperatureOn(day) + weather;
        }

        /// <summary>Jahreszeitlicher Niederschlagsfaktor (Mittel ≈ 1): Tropen Regenzeit im Sommer, sonst schwach.</summary>
        float RainSeasonFactor(int day)
        {
            float a = MathF.Abs(Parameters.Latitude);
            float strength = a < 25f ? 0.85f * (1f - a / 40f) : 0.15f;
            return 1f + strength * SeasonalWave(day);
        }

        /// <summary>Niederschlag mm an diesem Tag (0 an trockenen Tagen).</summary>
        public float PrecipitationOn(int day)
        {
            float meanDaily = Parameters.AnnualPrecipitation / SimulationClock.DaysPerYear * RainSeasonFactor(day);
            float pWet = Math.Clamp(0.12f + Parameters.AnnualPrecipitation / 2500f, 0.06f, 0.7f);
            if (WorldNoise.Hash01(day, 7, 0, seed) >= pWet) return 0f;
            float u = WorldNoise.Hash01(day, 11, 0, seed);
            return meanDaily / pWet * -MathF.Log(1f - u * 0.999f);   // exponentiell verteilt, Mittel = meanDaily
        }

        /// <summary>Mittlerer Tagesniederschlag des Jahres (mm).</summary>
        public float MeanDailyPrecipitation => Parameters.AnnualPrecipitation / SimulationClock.DaysPerYear;

        /// <summary>Wachstumsbedingungen 0..1: Temperatur (ab 4 °C, Optimum ~14–30 °C) × Wasser (Niederschlag der Jahreszeit).</summary>
        public float HarvestFactor(int day)
        {
            float t = NormalTemperatureOn(day) + (TemperatureOn(day) - NormalTemperatureOn(day)) * 0.3f;
            float growth = WorldNoise.SmoothStep(4f, 14f, t) * (1f - WorldNoise.SmoothStep(30f, 40f, t));
            float water = WorldNoise.SmoothStep(200f, 700f, Parameters.AnnualPrecipitation * RainSeasonFactor(day));
            return growth * (0.3f + 0.7f * water);
        }

        public float HeatingFactor(int day) => Math.Clamp((15f - TemperatureOn(day)) / 20f, 0f, 1.5f);
        public float CoolingFactor(int day) => Math.Clamp((TemperatureOn(day) - 22f) / 10f, 0f, 1.5f);

        public ClimateDay On(int day) => new ClimateDay(day, SeasonOn(day), TemperatureOn(day), PrecipitationOn(day),
            HarvestFactor(day), HeatingFactor(day), CoolingFactor(day));

        /// <summary>Wetter heute (braucht <see cref="Clock"/>).</summary>
        public ClimateDay Today => On(Clock != null ? Clock.Day : 0);
        public Season CurrentSeason => SeasonOn(Clock != null ? Clock.Day : 0);

        public static string SeasonName(Season s)
        {
            switch (s)
            {
                case Season.Spring: return "Frühling";
                case Season.Summer: return "Sommer";
                case Season.Autumn: return "Herbst";
                default: return "Winter";
            }
        }
    }
}
