using System.Numerics;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>Pinsel-Operationen des Terraforming-Werkzeugs.</summary>
    public enum TerraformMode
    {
        /// <summary>Gelände anheben.</summary>
        Raise,
        /// <summary>Gelände absenken (unter den Wasserspiegel → Wasser).</summary>
        Lower,
        /// <summary>Zum Mittel der Nachbarpunkte hin glätten.</summary>
        Smooth,
        /// <summary>Auf <see cref="TerraformBrush.TargetHeight"/> einebnen.</summary>
        Level,
    }

    /// <summary>Eine einzelne Pinsel-Anwendung (das Werkzeug ruft sie mit fester Rate auf).</summary>
    public struct TerraformBrush
    {
        public TerraformMode Mode;
        public Vector2 Center;
        /// <summary>Radius in Metern.</summary>
        public float Radius;
        /// <summary>Höchste Höhenänderung je Anwendung in Metern (in der Pinselmitte).</summary>
        public float Strength;
        /// <summary>Anteil des Radius mit weicher Kante (0 = harte Kante, 1 = ganz weich).</summary>
        public float Softness;
        /// <summary>Zielhöhe für <see cref="TerraformMode.Level"/> (absolut, Welt-y).</summary>
        public float TargetHeight;

        public TerraformBrush(TerraformMode mode, Vector2 center, float radius, float strength, float softness = 0.5f, float targetHeight = 0f)
        {
            Mode = mode;
            Center = center;
            Radius = radius;
            Strength = strength;
            Softness = softness;
            TargetHeight = targetHeight;
        }
    }

    /// <summary>Ergebnis einer Pinsel-Anwendung.</summary>
    public struct TerraformResult
    {
        /// <summary>Anzahl geänderter Gitterpunkte.</summary>
        public int ChangedPoints;
        /// <summary>Gitterpunkte im Pinsel, die ganz gesperrt waren (unter Straßen/Gebäuden).</summary>
        public int LockedPoints;
        /// <summary>Bewegte Erdmenge in m³ (Summe |Δh| · Zellfläche).</summary>
        public float Volume;
        /// <summary>Gebuchte Kosten (Volumen × <c>EconomyRules.TerraformCostPerCubicMeter</c>).</summary>
        public double Cost;
        /// <summary>Nicht ausgeführt: zu wenig Geld in der Stadtkasse.</summary>
        public bool InsufficientFunds;
        /// <summary>Geändertes Rechteck (Weltkoordinaten, inkl. einer Zelle Rand). Nur gültig, wenn <see cref="Changed"/>.</summary>
        public Vector2 Min, Max;

        public bool Changed => ChangedPoints > 0;
    }
}
