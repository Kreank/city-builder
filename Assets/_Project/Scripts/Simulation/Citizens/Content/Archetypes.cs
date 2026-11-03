namespace CityBuilder.Simulation.Citizens.Content
{
    /// <summary>Persönlichkeits-Archetyp: Mittelwerte der acht Charakterzüge, Häufigkeit und Anzeigename.</summary>
    public sealed class Archetype
    {
        public readonly string NameMale;
        public readonly string NameFemale;
        public readonly string Description;
        /// <summary>Relative Häufigkeit beim Zuzug.</summary>
        public readonly float Weight;
        /// <summary>Reihenfolge wie <see cref="Trait"/>: gesellig, sparsam, ehrgeizig, ängstlich, naturverbunden, lärmempfindlich, familienorientiert, abenteuerlustig.</summary>
        public readonly float[] Traits;

        public Archetype(string male, string female, float weight, string description, params float[] traits)
        {
            NameMale = male;
            NameFemale = female;
            Weight = weight;
            Description = description;
            Traits = traits;
        }
    }

    /// <summary>Mitgelieferte Archetypen (Inhalte als Daten).</summary>
    public static class Archetypes
    {
        //                                                                         ges   spar  ehrg  ängs  natur lärm  fam   abent
        public static readonly Archetype[] All =
        {
            new Archetype("Durchschnittsbürger", "Durchschnittsbürgerin", 3.0f,
                "Weder hü noch hott – will, dass einfach alles funktioniert.",          0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f),
            new Archetype("Familienmensch", "Familienmensch", 2.2f,
                "Platz, Schule, Sicherheit – die Kinder gehen vor.",                   0.55f, 0.6f, 0.4f, 0.6f, 0.55f, 0.6f, 0.95f, 0.25f),
            new Archetype("Karrierist", "Karrieristin", 1.5f,
                "Der Job ist alles; pendelt lieber im Auto als im Bus.",               0.45f, 0.2f, 0.95f, 0.35f, 0.3f, 0.35f, 0.3f, 0.55f),
            new Archetype("Sparfuchs", "Sparfüchsin", 1.6f,
                "Rechnet jede Fahrt nach, nimmt Rad oder Bus.",                        0.4f, 0.95f, 0.45f, 0.5f, 0.55f, 0.45f, 0.55f, 0.2f),
            new Archetype("Naturfreund", "Naturfreundin", 1.4f,
                "Braucht Grün vor der Tür und saubere Luft.",                          0.5f, 0.6f, 0.35f, 0.45f, 0.95f, 0.75f, 0.55f, 0.5f),
            new Archetype("Geselliger", "Gesellige", 1.4f,
                "Immer unterwegs, kennt jeden im Viertel.",                            0.95f, 0.3f, 0.5f, 0.3f, 0.5f, 0.2f, 0.5f, 0.7f),
            new Archetype("Ruhesuchender", "Ruhesuchende", 1.2f,
                "Schläft schlecht bei Verkehrslärm, zieht notfalls um.",               0.3f, 0.5f, 0.4f, 0.55f, 0.65f, 0.95f, 0.5f, 0.25f),
            new Archetype("Sicherheitsbedürftiger", "Sicherheitsbedürftige", 1.1f,
                "Achtet auf Polizei, Feuerwehr und Licht im Treppenhaus.",             0.4f, 0.6f, 0.4f, 0.95f, 0.45f, 0.6f, 0.65f, 0.1f),
            new Archetype("Abenteurer", "Abenteurerin", 0.9f,
                "Probiert alles aus – und ist auch schnell wieder weg.",               0.75f, 0.35f, 0.6f, 0.1f, 0.6f, 0.25f, 0.2f, 0.95f),
            new Archetype("Bücherwurm", "Bücherwurm", 0.9f,
                "Bibliothek und Bildung zählen mehr als Shopping.",                    0.3f, 0.65f, 0.75f, 0.45f, 0.5f, 0.75f, 0.45f, 0.3f),
            new Archetype("Bodenständiger", "Bodenständige", 1.6f,
                "Verwurzelt, verlässlich, zieht nicht gern um.",                       0.55f, 0.65f, 0.4f, 0.45f, 0.5f, 0.45f, 0.65f, 0.05f),
            new Archetype("Stadtmensch", "Stadtmensch", 1.1f,
                "Lärm stört nicht, Hauptsache es ist was los.",                        0.8f, 0.3f, 0.65f, 0.25f, 0.15f, 0.05f, 0.35f, 0.65f),
            new Archetype("Gemütlicher", "Gemütliche", 1.2f,
                "Am liebsten zu Hause auf dem Sofa.",                                  0.2f, 0.55f, 0.2f, 0.5f, 0.4f, 0.55f, 0.6f, 0.15f),
            new Archetype("Weltverbesserer", "Weltverbesserin", 0.8f,
                "Fährt Rad aus Prinzip, ärgert sich über jedes Kraftwerk.",            0.7f, 0.7f, 0.6f, 0.45f, 0.9f, 0.6f, 0.5f, 0.55f),
            new Archetype("Macher", "Macherin", 1.0f,
                "Ehrgeizig, aber mit Familie – will alles gleichzeitig.",              0.6f, 0.4f, 0.85f, 0.35f, 0.45f, 0.4f, 0.8f, 0.45f),
            new Archetype("Sorgenvoller", "Sorgenvolle", 0.9f,
                "Jeder Stromausfall bleibt lange im Gedächtnis.",                      0.35f, 0.7f, 0.35f, 0.9f, 0.5f, 0.7f, 0.6f, 0.15f),
        };

        static readonly float TotalWeight = SumWeights();

        static float SumWeights()
        {
            float t = 0f;
            foreach (var a in All) t += a.Weight;
            return t;
        }

        /// <summary>Archetyp nach Häufigkeit würfeln.</summary>
        public static int Roll(SimRandom rng)
        {
            float roll = rng.NextFloat() * TotalWeight;
            for (int i = 0; i < All.Length; i++)
            {
                roll -= All[i].Weight;
                if (roll <= 0f) return i;
            }
            return All.Length - 1;
        }

        /// <summary>Nächster Archetyp (euklidisch über die Züge) – für Kinder/gemischte Persönlichkeiten.</summary>
        public static int Nearest(in Personality p)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < All.Length; i++)
            {
                float d = 0f;
                for (int t = 0; t < Personality.TraitCount; t++)
                {
                    float x = p[(Trait)t] - All[i].Traits[t];
                    d += x * x;
                }
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
