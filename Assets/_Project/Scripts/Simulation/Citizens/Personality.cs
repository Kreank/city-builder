using System;
using CityBuilder.Simulation.Citizens.Content;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>Charakterzüge (je 0..1, 0,5 = durchschnittlich).</summary>
    public enum Trait : byte
    {
        /// <summary>Gesellig: Freizeit draußen/unter Leuten, Partnersuche.</summary>
        Sociable,
        /// <summary>Sparsam: eher zu Fuß/ÖPNV, zieht ungern um, kauft weniger.</summary>
        Thrifty,
        /// <summary>Ehrgeizig: Arbeit und Bildung wichtiger, wechselt für bessere Jobs.</summary>
        Ambitious,
        /// <summary>Ängstlich: Sicherheit und Versorgung wichtiger, stärkere negative Erinnerungen.</summary>
        Anxious,
        /// <summary>Naturverbunden: Park, saubere Luft, eher Rad/zu Fuß.</summary>
        NatureLoving,
        /// <summary>Lärmempfindlich: Ruhe viel wichtiger.</summary>
        NoiseSensitive,
        /// <summary>Familienorientiert: Platz, Schule, Kinderwunsch.</summary>
        FamilyOriented,
        /// <summary>Abenteuerlustig: weniger berechenbar (höhere Temperatur), zieht leichter weg.</summary>
        Adventurous,
    }

    /// <summary>
    /// Persönlichkeit eines Bürgers: acht Charakterzüge als Gewichte auf Bedürfnisse und Entscheidungen.
    /// Erwachsene beim Zuzug entstehen aus einem Archetyp (<see cref="Archetypes"/>) plus Streuung,
    /// Kinder aus der Mischung der Eltern plus Streuung.
    /// </summary>
    public struct Personality
    {
        public const int TraitCount = 8;

        float sociable, thrifty, ambitious, anxious, natureLoving, noiseSensitive, familyOriented, adventurous;

        /// <summary>Nächster Archetyp (Index in <see cref="Archetypes.All"/>) – für Anzeige und Gedankensätze.</summary>
        public int Archetype { get; internal set; }

        public float this[Trait t]
        {
            get
            {
                switch (t)
                {
                    case Trait.Sociable: return sociable;
                    case Trait.Thrifty: return thrifty;
                    case Trait.Ambitious: return ambitious;
                    case Trait.Anxious: return anxious;
                    case Trait.NatureLoving: return natureLoving;
                    case Trait.NoiseSensitive: return noiseSensitive;
                    case Trait.FamilyOriented: return familyOriented;
                    default: return adventurous;
                }
            }
            set
            {
                value = Math.Clamp(value, 0f, 1f);
                switch (t)
                {
                    case Trait.Sociable: sociable = value; break;
                    case Trait.Thrifty: thrifty = value; break;
                    case Trait.Ambitious: ambitious = value; break;
                    case Trait.Anxious: anxious = value; break;
                    case Trait.NatureLoving: natureLoving = value; break;
                    case Trait.NoiseSensitive: noiseSensitive = value; break;
                    case Trait.FamilyOriented: familyOriented = value; break;
                    default: adventurous = value; break;
                }
            }
        }

        public float Sociable => sociable;
        public float Thrifty => thrifty;
        public float Ambitious => ambitious;
        public float Anxious => anxious;
        public float NatureLoving => natureLoving;
        public float NoiseSensitive => noiseSensitive;
        public float FamilyOriented => familyOriented;
        public float Adventurous => adventurous;

        /// <summary>
        /// Temperatur des gewichteten Zufalls (Softmax): klein = fast immer die beste Option (berechenbar),
        /// groß = öfter Abweichungen. Abenteuerlustige streuen mehr, Ängstliche weniger.
        /// </summary>
        public float Temperature => 0.06f + 0.22f * adventurous + 0.06f * (1f - anxious);

        /// <summary>Durchschnittliche Persönlichkeit (alle Züge 0,5).</summary>
        public static Personality Neutral
        {
            get
            {
                var p = new Personality();
                foreach (Trait t in AllTraits) p[t] = 0.5f;
                p.Archetype = Archetypes.Nearest(p);
                return p;
            }
        }

        public static readonly Trait[] AllTraits =
        {
            Trait.Sociable, Trait.Thrifty, Trait.Ambitious, Trait.Anxious,
            Trait.NatureLoving, Trait.NoiseSensitive, Trait.FamilyOriented, Trait.Adventurous,
        };

        /// <summary>
        /// Multiplikator der Persönlichkeit auf das Gewicht eines Bedürfnisses (1 bei durchschnittlicher Persönlichkeit
        /// ungefähr; Bereich etwa 0,5..1,6).
        /// </summary>
        public float NeedWeight(NeedKind k)
        {
            switch (k)
            {
                case NeedKind.Housing: return 0.75f + 0.5f * familyOriented;
                case NeedKind.Work: return 0.45f + 0.8f * ambitious + 0.3f * thrifty;
                case NeedKind.Supply: return 0.85f + 0.3f * anxious;
                case NeedKind.Shopping: return 0.7f + 0.6f * (1f - thrifty);
                case NeedKind.Health: return 0.75f + 0.5f * anxious;
                case NeedKind.Safety: return 0.35f + 1.0f * anxious + 0.3f * familyOriented;
                case NeedKind.Education: return 0.4f + 0.6f * ambitious + 0.6f * familyOriented;
                case NeedKind.Leisure: return 0.5f + 0.7f * sociable + 0.3f * natureLoving;
                case NeedKind.Calm: return 0.2f + 1.2f * noiseSensitive + 0.4f * natureLoving;
                default: return 0.7f + 0.4f * ambitious + 0.2f * (1f - adventurous);
            }
        }

        /// <summary>Aus einem Archetyp mit Streuung ±<paramref name="spread"/>.</summary>
        public static Personality FromArchetype(int archetype, SimRandom rng, float spread = 0.15f)
        {
            var a = Archetypes.All[archetype];
            var p = new Personality();
            for (int i = 0; i < TraitCount; i++) p[(Trait)i] = a.Traits[i] + rng.NextSigned() * spread;
            p.Archetype = Archetypes.Nearest(p);
            return p;
        }

        /// <summary>Kind: Mittel der Eltern (zufällig gewichtet) plus Streuung. Ohne zweiten Elternteil: Archetyp-Zufall statt Partner.</summary>
        public static Personality Mix(in Personality a, in Personality b, SimRandom rng, float spread = 0.18f)
        {
            var p = new Personality();
            for (int i = 0; i < TraitCount; i++)
            {
                var t = (Trait)i;
                float w = 0.3f + 0.4f * rng.NextFloat();
                p[t] = a[t] * w + b[t] * (1f - w) + rng.NextSigned() * spread;
            }
            p.Archetype = Archetypes.Nearest(p);
            return p;
        }

        public static string DisplayName(Trait t)
        {
            switch (t)
            {
                case Trait.Sociable: return "gesellig";
                case Trait.Thrifty: return "sparsam";
                case Trait.Ambitious: return "ehrgeizig";
                case Trait.Anxious: return "ängstlich";
                case Trait.NatureLoving: return "naturverbunden";
                case Trait.NoiseSensitive: return "lärmempfindlich";
                case Trait.FamilyOriented: return "familienorientiert";
                default: return "abenteuerlustig";
            }
        }

        /// <summary>Gegenteil eines Zugs (für Beschreibungen bei Werten deutlich unter 0,5).</summary>
        public static string OppositeName(Trait t)
        {
            switch (t)
            {
                case Trait.Sociable: return "zurückhaltend";
                case Trait.Thrifty: return "großzügig";
                case Trait.Ambitious: return "genügsam";
                case Trait.Anxious: return "gelassen";
                case Trait.NatureLoving: return "städtisch";
                case Trait.NoiseSensitive: return "lärmunempfindlich";
                case Trait.FamilyOriented: return "unabhängig";
                default: return "bodenständig";
            }
        }

        /// <summary>Kurzbeschreibung: Archetyp und die zwei/drei ausgeprägtesten Züge, z. B. „Naturfreundin – naturverbunden, lärmempfindlich“.</summary>
        public string Describe(Gender gender)
        {
            Trait best1 = Trait.Sociable, best2 = Trait.Sociable;
            float d1 = -1f, d2 = -1f;
            foreach (var t in AllTraits)
            {
                float d = MathF.Abs(this[t] - 0.5f);
                if (d > d1) { d2 = d1; best2 = best1; d1 = d; best1 = t; }
                else if (d > d2) { d2 = d; best2 = t; }
            }
            string n1 = this[best1] >= 0.5f ? DisplayName(best1) : OppositeName(best1);
            string n2 = this[best2] >= 0.5f ? DisplayName(best2) : OppositeName(best2);
            var a = Archetypes.All[Archetype];
            return $"{(gender == Gender.Female ? a.NameFemale : a.NameMale)} – {n1}, {n2}";
        }
    }
}
