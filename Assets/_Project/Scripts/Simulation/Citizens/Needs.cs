namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Bedürfnisse eines Bürgers. Werte sind <b>Erfüllung</b> 0..1 (1 = voll erfüllt, 0 = dringend unerfüllt).
    /// </summary>
    public enum NeedKind : byte
    {
        /// <summary>Wohnen: Platz in der Wohnung und Zustand des Gebäudes.</summary>
        Housing,
        /// <summary>Arbeit/Einkommen: Job, passend zur Bildung, Dauer der Arbeitslosigkeit.</summary>
        Work,
        /// <summary>Versorgung: Strom und Wasser an der Wohnung.</summary>
        Supply,
        /// <summary>Einkaufen (aus <see cref="Citizen.ShoppingNeed"/>).</summary>
        Shopping,
        /// <summary>Gesundheit: körperliche Verfassung und Gesundheitsdienst in Reichweite.</summary>
        Health,
        /// <summary>Sicherheit: Polizei und Feuerwehr.</summary>
        Safety,
        /// <summary>Bildung: Schule (Kinder/Schüler), Bibliothek (alle).</summary>
        Education,
        /// <summary>Freizeit: sinkt mit der Zeit, steigt durch Freizeitaktivitäten (Park, Bummeln, zu Hause).</summary>
        Leisure,
        /// <summary>Ruhe/Umwelt: Lärm und Abgas an der Wohnung, Müllabfuhr.</summary>
        Calm,
        /// <summary>Mobilität: Pendelweg und Erlebnisse unterwegs (Stau).</summary>
        Mobility,
    }

    public static class Needs
    {
        public const int Count = 10;

        public static readonly NeedKind[] All =
        {
            NeedKind.Housing, NeedKind.Work, NeedKind.Supply, NeedKind.Shopping, NeedKind.Health,
            NeedKind.Safety, NeedKind.Education, NeedKind.Leisure, NeedKind.Calm, NeedKind.Mobility,
        };

        public static string DisplayName(NeedKind k)
        {
            switch (k)
            {
                case NeedKind.Housing: return "Wohnen";
                case NeedKind.Work: return "Arbeit/Einkommen";
                case NeedKind.Supply: return "Versorgung";
                case NeedKind.Shopping: return "Einkaufen";
                case NeedKind.Health: return "Gesundheit";
                case NeedKind.Safety: return "Sicherheit";
                case NeedKind.Education: return "Bildung";
                case NeedKind.Leisure: return "Freizeit";
                case NeedKind.Calm: return "Ruhe/Umwelt";
                default: return "Mobilität";
            }
        }

        /// <summary>
        /// Grundgewichte je Lebensphase (Zeile = <see cref="LifeStage"/>, Spalte = <see cref="NeedKind"/>).
        /// Die Persönlichkeit multipliziert darauf (<see cref="Personality.NeedWeight"/>).
        /// </summary>
        static readonly float[,] StageWeights =
        {
            //            Wohnen Arbeit Versorg Einkauf Gesundh Sicherh Bildung Freizeit Ruhe Mobil
            /* Kind   */ { 1.0f, 0.0f, 1.0f, 0.2f, 1.2f, 1.0f, 0.8f, 1.2f, 0.8f, 0.1f },
            /* Schüler*/ { 0.8f, 0.0f, 1.0f, 0.4f, 0.8f, 0.8f, 1.5f, 1.3f, 0.6f, 0.5f },
            /* Erwachs*/ { 1.2f, 1.5f, 1.0f, 0.8f, 0.8f, 0.8f, 0.5f, 0.9f, 0.9f, 1.0f },
            /* Senior */ { 1.0f, 0.0f, 1.0f, 0.8f, 1.6f, 1.2f, 0.3f, 0.9f, 1.2f, 0.4f },
        };

        public static float StageWeight(LifeStage stage, NeedKind k) => StageWeights[(int)stage, (int)k];

        /// <summary>Beschreibung eines Erfüllungswerts für Anzeigen.</summary>
        public static string Describe(float value) =>
            value >= 0.8f ? "sehr gut" : value >= 0.6f ? "gut" : value >= 0.4f ? "mittel" : value >= 0.2f ? "schlecht" : "sehr schlecht";
    }

    /// <summary>
    /// Ids der Dienste (G4) für <see cref="CitizenSystem.ServiceCoverage"/>. Der Dienst-Agent liefert die Abdeckung
    /// eines Gebäudes je Dienst 0..1 (0,5 = neutral/unbekannt).
    /// </summary>
    public static class ServiceIds
    {
        public const string Police = "police";
        public const string Fire = "fire";
        public const string Health = "health";
        public const string Education = "education";
        public const string Library = "library";
        public const string Park = "park";
        public const string Garbage = "garbage";

        public static readonly string[] All = { Police, Fire, Health, Education, Library, Park, Garbage };
    }
}
