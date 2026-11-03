using System.Collections.Generic;
using System.Text;
using CityBuilder.Simulation.Citizens;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Silbengenerator für fiktive Länder-, Regions- und Städtenamen (deterministisch aus dem Seed,
    /// eindeutig innerhalb einer Welt). Bewusst keine echten Ländernamen.
    /// </summary>
    public sealed class NameGenerator
    {
        static readonly string[] Onsets =
        {
            "", "b", "br", "d", "dr", "f", "g", "gr", "h", "k", "kr", "l", "m", "n", "p", "r", "s", "st", "sch", "t",
            "tr", "v", "w", "z", "th", "ch", "sk", "pl", "j", "q"
        };
        static readonly string[] Vowels = { "a", "e", "i", "o", "u", "a", "e", "o", "ae", "ei", "au", "ia", "y", "ou" };
        static readonly string[] Codas = { "", "", "", "n", "r", "l", "s", "m", "nd", "rt", "st", "k", "th", "x", "v" };

        static readonly string[] CountrySuffixes = { "ien", "ia", "an", "or", "estan", "land", "onien", "ara", "is", "ur", "ea", "avien" };
        static readonly string[] RegionSuffixes = { "mark", "tal", "au", "gau", "heim", "wald", "furt", "ach", "ing", "berg", "see", "ow", "ia", "ene" };
        static readonly string[] CitySuffixes = { "burg", "stadt", "hafen", "ingen", "dorf", "polis", "a", "ora", "grad", "ville", "ach", "brück" };

        readonly SimRandom rng;
        readonly HashSet<string> used = new HashSet<string>();

        public NameGenerator(int seed) { rng = new SimRandom(seed); }

        string Stem(int minSyllables, int maxSyllables)
        {
            int n = rng.Next(minSyllables, maxSyllables + 1);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                sb.Append(Onsets[rng.Next(Onsets.Length)]);
                sb.Append(Vowels[rng.Next(Vowels.Length)]);
                if (i == n - 1 || rng.Chance(0.3)) sb.Append(Codas[rng.Next(Codas.Length)]);
            }
            return sb.ToString();
        }

        string Make(string[] suffixes, double suffixChance, int minSyl, int maxSyl)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                string s = Stem(minSyl, maxSyl);
                if (rng.Chance(suffixChance)) s += suffixes[rng.Next(suffixes.Length)];
                if (s.Length < 4 || s.Length > 14) continue;
                s = char.ToUpperInvariant(s[0]) + s.Substring(1);
                if (used.Add(s)) return s;
            }
            // Rückfall: nummeriert (praktisch nie)
            string fallback = "Terra " + used.Count;
            used.Add(fallback);
            return fallback;
        }

        public string Country() => Make(CountrySuffixes, 0.8, 1, 2);
        public string Region() => Make(RegionSuffixes, 0.55, 1, 2);
        public string City() => Make(CitySuffixes, 0.6, 1, 2);
    }
}
