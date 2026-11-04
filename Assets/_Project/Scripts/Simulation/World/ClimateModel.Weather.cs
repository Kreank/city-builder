using System;
using System.Numerics;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Simulation.World
{
    /// <summary>Wind eines Tages (Umwelt, Etappe 6).</summary>
    public readonly struct WindState
    {
        /// <summary>Richtung, in die die Luft zieht (Einheitsvektor, x = Ost, y = Nord).</summary>
        public readonly Vector2 Toward;
        /// <summary>Herkunft in Grad (0 = Nord, 90 = Ost, im Uhrzeigersinn) – „Wind aus Westen“ = 270.</summary>
        public readonly float FromDegrees;
        /// <summary>Stärke 0..1 (≈ 0 … 12 m/s, Ø ≈ 0,45).</summary>
        public readonly float Strength;

        public WindState(Vector2 toward, float fromDegrees, float strength)
        {
            Toward = toward; FromDegrees = fromDegrees; Strength = strength;
        }

        /// <summary>Geschwindigkeit in m/s (Anzeige).</summary>
        public float MetersPerSecond => Strength * 12f;

        /// <summary>Himmelsrichtung der Herkunft („West“, „Nordost“ …).</summary>
        public string FromName => CompassName(FromDegrees);

        public static string CompassName(float degrees)
        {
            string[] names = { "Nord", "Nordost", "Ost", "Südost", "Süd", "Südwest", "West", "Nordwest" };
            float d = ((degrees % 360f) + 360f) % 360f;
            return names[(int)MathF.Round(d / 45f) % 8];
        }
    }

    /// <summary>
    /// Umwelt (Etappe 6): Wetter über die Temperatur/Niederschlag hinaus – Wind (Richtung/Stärke je Tag), Dürre, Hitze und
    /// Schneedecke. Wie das übrige Klima zustandslos und deterministisch aus Seed + Tag (beliebig abfragbar, kein Spielstand).
    /// </summary>
    public sealed partial class ClimateModel
    {
        /// <summary>
        /// Wind des Tages: Hauptwindrichtung nach Breite (Passat unter 30° aus Ost, Westwindzone 30–60° aus West, polar aus Ost),
        /// Wetterlagen drehen ihn über ~3 Tage um bis zu ±100°, dazu Tagesrauschen. Stärke aus geglättetem Rauschen (Ø ≈ 0,45).
        /// </summary>
        public WindState WindOn(int day)
        {
            float a = MathF.Abs(Parameters.Latitude);
            float prevailingFrom = a < 30f ? 90f : a < 60f ? 270f : 90f;
            float swing = (WorldNoise.Value1(day / 3f, seed + 101) - 0.5f) * 200f + (WorldNoise.Hash01(day, 13, 0, seed) - 0.5f) * 40f;
            float from = ((prevailingFrom + swing) % 360f + 360f) % 360f;
            float strength = 0.08f + 0.8f * WorldNoise.Value1(day / 2.5f, seed + 211) + (WorldNoise.Hash01(day, 17, 0, seed) - 0.5f) * 0.2f;
            strength = Math.Clamp(strength, 0.02f, 1f);
            float rad = from * MathF.PI / 180f;
            var toward = new Vector2(-MathF.Sin(rad), -MathF.Cos(rad)); // „aus West“ (270°) zieht nach Osten (+x)
            return new WindState(toward, from, strength);
        }

        /// <summary>Wind heute (braucht <see cref="Clock"/>).</summary>
        public WindState WindToday => WindOn(Clock != null ? Clock.Day : 0);

        /// <summary>
        /// Inversionswetterlage: kalt (unter 3 °C), windstill (Stärke &lt; 0,3) und trocken im Winterhalbjahr – die Luft
        /// über der Stadt tauscht sich kaum aus (Smog). 0..1 (weicher Übergang).
        /// </summary>
        public float InversionOn(int day)
        {
            var s = SeasonOn(day);
            if (s != Season.Winter && s != Season.Autumn) return 0f;
            float cold = 1f - WorldNoise.SmoothStep(-2f, 4f, TemperatureOn(day));
            float calm = 1f - WorldNoise.SmoothStep(0.15f, 0.35f, WindOn(day).Strength);
            float dry = PrecipitationOn(day) > 0.5f ? 0.3f : 1f;
            return Math.Clamp(cold * calm * dry, 0f, 1f);
        }

        /// <summary>
        /// Dürre 0..1: Niederschlag der letzten 20 Tage gegen das jahreszeitliche Mittel (unter 65 % → trocken), gewichtet mit
        /// der Verdunstung (warm). 0 = normal/feucht.
        /// </summary>
        public float DroughtOn(int day)
        {
            float rain = 0f, expected = 0f, warm = 0f;
            for (int d = day - 19; d <= day; d++)
            {
                rain += PrecipitationOn(d);
                expected += MeanDailyPrecipitation * RainSeasonFactor(d);
                warm += TemperatureOn(d);
            }
            if (expected <= 0.01f) return 1f;
            float ratio = rain / expected;
            float dry = Math.Clamp((0.65f - ratio) / 0.65f, 0f, 1f);
            return dry * WorldNoise.SmoothStep(8f, 20f, warm / 20f);
        }

        /// <summary>Hitzestress 0..1 an diesem Tag (Tageshöchstwert ≈ Mittel + 6 K; ab 30 °C, voll bei 40 °C).</summary>
        public float HeatStressOn(int day) => WorldNoise.SmoothStep(30f, 40f, TemperatureOn(day) + 6f);

        int snowDay = int.MinValue;
        float snowCover;

        /// <summary>
        /// Schneedecke 0..1: Grad-Tag-Modell über die letzten 40 Tage (Niederschlag unter 0,5 °C bleibt liegen, über 0 °C
        /// schmilzt 1,5 mm je K und Tag); 15 mm Wasserwert = geschlossene Decke. Nur in kalten Regionen/Wintern nennenswert.
        /// </summary>
        public float SnowCoverOn(int day)
        {
            if (day == snowDay) return snowCover; // reiner Zwischenspeicher (Ergebnis hängt nur vom Tag ab)
            float snow = 0f;
            for (int d = day - 40; d <= day; d++)
            {
                float t = TemperatureOn(d);
                if (t < 0.5f) snow += PrecipitationOn(d);
                else snow = MathF.Max(0f, snow - 1.5f * t - 0.5f * PrecipitationOn(d));
            }
            snowDay = day;
            snowCover = Math.Clamp(snow / 15f, 0f, 1f);
            return snowCover;
        }

        /// <summary>
        /// Trockenheit des Grases 0..1 für die Gelände-Farben: Dürre, Sommerhitze und trockenes Grundklima (unter ~450 mm im Jahr).
        /// </summary>
        public float GrassDrynessOn(int day)
        {
            float arid = 1f - WorldNoise.SmoothStep(250f, 650f, Parameters.AnnualPrecipitation);
            float summer = SeasonOn(day) == Season.Summer ? WorldNoise.SmoothStep(18f, 30f, TemperatureOn(day)) * 0.35f : 0f;
            return Math.Clamp(0.55f * arid + 0.75f * DroughtOn(day) + summer, 0f, 1f);
        }
    }
}
