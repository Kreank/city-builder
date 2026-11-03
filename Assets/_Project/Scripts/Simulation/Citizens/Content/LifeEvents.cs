namespace CityBuilder.Simulation.Citizens.Content
{
    /// <summary>Lebensereignisse, die das Bürgersystem würfelt bzw. auslöst.</summary>
    public enum LifeEventKind : byte
    {
        FindPartner,
        Wedding,
        Birth,
        MoveOut,
        Separation,
        Illness,
        Recovery,
        JobLoss,
        Retirement,
        Death,
        MoveHome,
        LeaveCity,
        JobChange,
    }

    /// <summary>
    /// Vorlage eines Lebensereignisses (mitgelieferte Daten): Grundwahrscheinlichkeit je Tag, Altersbereich,
    /// Einfluss eines Charakterzugs, Erinnerung für Betroffene und Angehörige sowie Meldungstexte für den
    /// Ereignis-Ticker. Platzhalter in Meldungen: {Name} (voller Name), {Partner}, {Kind}, {Straße}, {Firma}.
    /// Ereignisse mit Grundwahrscheinlichkeit 0 werden nicht gewürfelt, sondern von der Simulation ausgelöst.
    /// </summary>
    public sealed class LifeEventTemplate
    {
        public readonly LifeEventKind Kind;
        public readonly float ChancePerDay;
        public readonly int MinAge, MaxAge;
        public readonly Trait Trait;
        /// <summary>Multiplikator bei Zug = 1 (bei 0 der Kehrwert-Anteil): Chance × (1 + TraitEffect × (Zug − 0,5) × 2).</summary>
        public readonly float TraitEffect;
        public readonly MemoryKind SubjectMemory;
        public readonly float SubjectStrength;
        /// <summary>Erinnerung für Partner/Eltern (oder <see cref="SubjectMemory"/> mit Stärke 0 = keine).</summary>
        public readonly MemoryKind RelativeMemory;
        public readonly float RelativeStrength;
        public readonly string[] News;

        public LifeEventTemplate(LifeEventKind kind, float chancePerDay, int minAge, int maxAge, Trait trait, float traitEffect,
            MemoryKind subject, float subjectStrength, MemoryKind relative, float relativeStrength, params string[] news)
        {
            Kind = kind;
            ChancePerDay = chancePerDay;
            MinAge = minAge;
            MaxAge = maxAge;
            Trait = trait;
            TraitEffect = traitEffect;
            SubjectMemory = subject;
            SubjectStrength = subjectStrength;
            RelativeMemory = relative;
            RelativeStrength = relativeStrength;
            News = news;
        }

        /// <summary>Tageschance für einen Bürger mit dieser Persönlichkeit (ohne weitere Bedingungen).</summary>
        public float ChanceFor(in Personality p) => ChancePerDay * System.MathF.Max(0f, 1f + TraitEffect * (p[Trait] - 0.5f) * 2f);
    }

    public static class LifeEvents
    {
        public static readonly LifeEventTemplate[] All =
        {
            new LifeEventTemplate(LifeEventKind.FindPartner, 0.004f, 18, 70, Trait.Sociable, 0.8f,
                MemoryKind.NewPartner, 0.9f, MemoryKind.NewPartner, 0f,
                "{Name} und {Partner} sind jetzt ein Paar.",
                "Frisch verliebt: {Name} zieht mit {Partner} zusammen.",
                "In der {Straße} hat es gefunkt – {Name} und {Partner}."),
            new LifeEventTemplate(LifeEventKind.Wedding, 0.0015f, 20, 75, Trait.FamilyOriented, 0.8f,
                MemoryKind.Wedding, 1f, MemoryKind.Wedding, 1f,
                "{Name} und {Partner} haben geheiratet.",
                "Hochzeitsglocken in der {Straße}: {Name} heiratet {Partner}.",
                "Ja-Wort: {Name} und {Partner} sind jetzt verheiratet."),
            new LifeEventTemplate(LifeEventKind.Birth, 0.0035f, 20, 44, Trait.FamilyOriented, 0.9f,
                MemoryKind.Birth, 1f, MemoryKind.Birth, 1f,
                "Willkommen, {Kind}! {Name} und {Partner} sind Eltern geworden.",
                "Nachwuchs in der {Straße}: {Kind} ist geboren.",
                "{Name} freut sich über die Geburt von {Kind}."),
            new LifeEventTemplate(LifeEventKind.MoveOut, 0.006f, 18, 35, Trait.Adventurous, 0.8f,
                MemoryKind.OwnFlat, 0.8f, MemoryKind.ChildMovedOut, 0.6f,
                "{Name} ist bei den Eltern ausgezogen – in die {Straße}.",
                "Erste eigene Wohnung: {Name} zieht in die {Straße}.",
                "{Name} steht jetzt auf eigenen Beinen."),
            new LifeEventTemplate(LifeEventKind.Separation, 0.0006f, 18, 90, Trait.Adventurous, 0.6f,
                MemoryKind.Separation, 0.9f, MemoryKind.Separation, 0.9f,
                "{Name} und {Partner} gehen getrennte Wege.",
                "Aus und vorbei: {Name} hat sich von {Partner} getrennt.",
                "Trennung in der {Straße}: {Name} zieht aus."),
            new LifeEventTemplate(LifeEventKind.Illness, 0.0025f, 0, 120, Trait.Anxious, 0.2f,
                MemoryKind.Illness, 0.7f, MemoryKind.Illness, 0.3f,
                "{Name} ist krank geworden.",
                "{Name} liegt mit Fieber im Bett.",
                "Grippe in der {Straße}: auch {Name} hat es erwischt."),
            new LifeEventTemplate(LifeEventKind.Recovery, 0.12f, 0, 120, Trait.Anxious, -0.2f,
                MemoryKind.Recovered, 0.6f, MemoryKind.Recovered, 0f,
                "{Name} ist wieder gesund.",
                "Gute Besserung hat geholfen: {Name} ist genesen.",
                "{Name} ist wieder auf den Beinen."),
            new LifeEventTemplate(LifeEventKind.JobLoss, 0.0006f, 18, 64, Trait.Ambitious, -0.4f,
                MemoryKind.JobLost, 0.9f, MemoryKind.JobLost, 0.4f,
                "{Firma} hat {Name} entlassen.",
                "{Name} ist seit heute arbeitslos.",
                "Stellenabbau bei {Firma} – {Name} muss gehen."),
            new LifeEventTemplate(LifeEventKind.Retirement, 0f, 65, 65, Trait.Ambitious, 0f,
                MemoryKind.Retired, 0.8f, MemoryKind.Retired, 0f,
                "{Name} geht in Rente.",
                "Abschied bei {Firma}: {Name} ist jetzt Rentner.",
                "Feierabend für immer – {Name} ist im Ruhestand."),
            new LifeEventTemplate(LifeEventKind.Death, 0f, 0, 120, Trait.Anxious, 0f,
                MemoryKind.PartnerDied, 0f, MemoryKind.PartnerDied, 1f,
                "{Name} ist verstorben.",
                "Trauer in der {Straße}: {Name} ist gestorben.",
                "Die Stadt nimmt Abschied von {Name}."),
            new LifeEventTemplate(LifeEventKind.MoveHome, 0f, 0, 120, Trait.Adventurous, 0f,
                MemoryKind.MovedHome, 0.7f, MemoryKind.MovedHome, 0f,
                "Familie {Nachname} ist in die {Straße} umgezogen.",
                "Umzug: {Name} wohnt jetzt in der {Straße}.",
                "Neue Adresse für {Name}: {Straße}."),
            new LifeEventTemplate(LifeEventKind.LeaveCity, 0f, 0, 120, Trait.Adventurous, 0f,
                MemoryKind.MovedHome, 0f, MemoryKind.MovedHome, 0f,
                "Familie {Nachname} hat der Stadt den Rücken gekehrt.",
                "{Name} zieht weg – „hier hält mich nichts mehr“.",
                "Wegzug aus der {Straße}: {Name} sucht sein Glück woanders."),
            new LifeEventTemplate(LifeEventKind.JobChange, 0f, 18, 64, Trait.Ambitious, 0f,
                MemoryKind.NewJob, 0.7f, MemoryKind.NewJob, 0f,
                "{Name} arbeitet jetzt bei {Firma}.",
                "Jobwechsel: {Name} fängt bei {Firma} an.",
                "{Firma} hat {Name} eingestellt."),
        };

        public static LifeEventTemplate Get(LifeEventKind k) => All[(int)k];

        public static string DisplayName(LifeEventKind k)
        {
            switch (k)
            {
                case LifeEventKind.FindPartner: return "neue Partnerschaft";
                case LifeEventKind.Wedding: return "Hochzeit";
                case LifeEventKind.Birth: return "Geburt";
                case LifeEventKind.MoveOut: return "Auszug";
                case LifeEventKind.Separation: return "Trennung";
                case LifeEventKind.Illness: return "Krankheit";
                case LifeEventKind.Recovery: return "Genesung";
                case LifeEventKind.JobLoss: return "Jobverlust";
                case LifeEventKind.Retirement: return "Rente";
                case LifeEventKind.Death: return "Tod";
                case LifeEventKind.MoveHome: return "Umzug";
                case LifeEventKind.LeaveCity: return "Wegzug";
                default: return "Jobwechsel";
            }
        }
    }
}
