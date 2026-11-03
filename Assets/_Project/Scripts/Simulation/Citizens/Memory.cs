using System;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>Art einer Erinnerung (Erlebnis, Lebensereignis, Eindruck vom Wohnort).</summary>
    public enum MemoryKind : byte
    {
        TrafficJam,
        LongCommute,
        PowerOutage,
        WaterOutage,
        Noise,
        Smog,
        NicePark,
        NiceShopping,
        NoShop,
        NewJob,
        JobLost,
        Retired,
        Birth,
        NewPartner,
        Wedding,
        Separation,
        PartnerDied,
        Illness,
        Recovered,
        MovedHome,
        OwnFlat,
        ChildMovedOut,
        FeelsUnsafe,
        Demolished,
        QuietEvening,
    }

    /// <summary>
    /// Eine Erinnerung: Art, Stärke (klingt ab), Tag, Bezüge (Straße, Gebäude/Firma, beteiligte Person) und die
    /// gewählte Textvorlage. Bezüge werden beim Entstehen festgehalten, damit der Text auch später noch stimmt.
    /// </summary>
    public struct Memory
    {
        public MemoryKind Kind;
        /// <summary>Index der Textvorlage (deterministisch beim Entstehen gewählt).</summary>
        public byte Template;
        /// <summary>Zone des <see cref="Building"/> (für den Firmennamen).</summary>
        public Zoning.ZoneType Zone;
        /// <summary>0..1; klingt täglich ab (<see cref="Memories.DailyDecay"/>), unter 0,05 vergessen.</summary>
        public float Strength;
        public int Day;
        /// <summary>Straßensegment (Stau, Lärm, Wohnstraße), 0 = keins.</summary>
        public int Street;
        /// <summary>Gebäude (Arbeitgeber, Wohnung), 0 = keins.</summary>
        public int Building;
        /// <summary>Dienstgebäude (G4, z. B. besuchter Park), 0 = keins.</summary>
        public int Facility;
        /// <summary>Beteiligte Person (Partner, Kind), 0 = keine.</summary>
        public int OtherId;
        /// <summary>Vorname der beteiligten Person als Schlüssel (Geschlecht × 10000 + Index + 1), 0 = keine.</summary>
        public int OtherName;

        /// <summary>Wirkung aufs Gemüt (Valenz × Stärke).</summary>
        public float MoodEffect => Memories.Valence(Kind) * Strength;
    }

    public static class Memories
    {
        /// <summary>Höchstens so viele Erinnerungen je Bürger (die schwächste wird ersetzt).</summary>
        public const int Capacity = 6;
        public const float ForgetBelow = 0.05f;

        /// <summary>Valenz −1..+1: wie gut/schlecht sich die Erinnerung anfühlt.</summary>
        public static float Valence(MemoryKind k)
        {
            switch (k)
            {
                case MemoryKind.TrafficJam: return -0.4f;
                case MemoryKind.LongCommute: return -0.3f;
                case MemoryKind.PowerOutage: return -0.6f;
                case MemoryKind.WaterOutage: return -0.6f;
                case MemoryKind.Noise: return -0.5f;
                case MemoryKind.Smog: return -0.5f;
                case MemoryKind.NicePark: return 0.5f;
                case MemoryKind.NiceShopping: return 0.3f;
                case MemoryKind.NoShop: return -0.3f;
                case MemoryKind.NewJob: return 0.6f;
                case MemoryKind.JobLost: return -0.8f;
                case MemoryKind.Retired: return 0.4f;
                case MemoryKind.Birth: return 0.9f;
                case MemoryKind.NewPartner: return 0.8f;
                case MemoryKind.Wedding: return 0.9f;
                case MemoryKind.Separation: return -0.8f;
                case MemoryKind.PartnerDied: return -1f;
                case MemoryKind.Illness: return -0.6f;
                case MemoryKind.Recovered: return 0.5f;
                case MemoryKind.MovedHome: return 0.4f;
                case MemoryKind.OwnFlat: return 0.6f;
                case MemoryKind.ChildMovedOut: return -0.1f;
                case MemoryKind.FeelsUnsafe: return -0.5f;
                case MemoryKind.Demolished: return -0.7f;
                default: return 0.3f; // QuietEvening
            }
        }

        /// <summary>Faktor je Tag (Halbwertszeit: Stau ~2 Tage, Lebensereignisse Wochen bis Monate).</summary>
        public static float DailyDecay(MemoryKind k)
        {
            switch (k)
            {
                case MemoryKind.TrafficJam: return 0.72f;
                case MemoryKind.LongCommute: return 0.8f;
                case MemoryKind.NoShop: return 0.7f;
                case MemoryKind.NiceShopping: return 0.7f;
                case MemoryKind.QuietEvening: return 0.65f;
                case MemoryKind.PowerOutage:
                case MemoryKind.WaterOutage: return 0.85f;
                case MemoryKind.Noise:
                case MemoryKind.Smog:
                case MemoryKind.FeelsUnsafe:
                case MemoryKind.NicePark: return 0.88f;
                case MemoryKind.PartnerDied: return 0.985f;
                case MemoryKind.Birth:
                case MemoryKind.Wedding:
                case MemoryKind.Separation: return 0.975f;
                default: return 0.94f;
            }
        }

        public static string DisplayName(MemoryKind k)
        {
            switch (k)
            {
                case MemoryKind.TrafficJam: return "Stau";
                case MemoryKind.LongCommute: return "langer Arbeitsweg";
                case MemoryKind.PowerOutage: return "Stromausfall";
                case MemoryKind.WaterOutage: return "Wasserausfall";
                case MemoryKind.Noise: return "Lärm";
                case MemoryKind.Smog: return "schlechte Luft";
                case MemoryKind.NicePark: return "schöner Park";
                case MemoryKind.NiceShopping: return "Einkaufsbummel";
                case MemoryKind.NoShop: return "kein Laden erreichbar";
                case MemoryKind.NewJob: return "neuer Job";
                case MemoryKind.JobLost: return "Job verloren";
                case MemoryKind.Retired: return "Rente";
                case MemoryKind.Birth: return "Geburt";
                case MemoryKind.NewPartner: return "neue Liebe";
                case MemoryKind.Wedding: return "Hochzeit";
                case MemoryKind.Separation: return "Trennung";
                case MemoryKind.PartnerDied: return "Partner verstorben";
                case MemoryKind.Illness: return "Krankheit";
                case MemoryKind.Recovered: return "Genesung";
                case MemoryKind.MovedHome: return "Umzug";
                case MemoryKind.OwnFlat: return "eigene Wohnung";
                case MemoryKind.ChildMovedOut: return "Kind ausgezogen";
                case MemoryKind.FeelsUnsafe: return "fühlt sich unsicher";
                case MemoryKind.Demolished: return "Wohnung abgerissen";
                default: return "ruhiger Abend";
            }
        }
    }

    /// <summary>Kurze Erinnerungsliste je Bürger (fest <see cref="Memories.Capacity"/>, wird erst bei Bedarf angelegt).</summary>
    public sealed class MemoryList
    {
        readonly Memory[] items = new Memory[Memories.Capacity];
        public int Count { get; private set; }
        public Memory this[int i] => items[i];

        /// <summary>
        /// Fügt hinzu bzw. frischt eine gleichartige Erinnerung mit gleichen Bezügen auf (keine Dubletten).
        /// Liefert true, wenn eine neue Erinnerung entstand.
        /// </summary>
        internal bool Add(in Memory m)
        {
            for (int i = 0; i < Count; i++)
            {
                if (items[i].Kind != m.Kind || items[i].Street != m.Street || items[i].Building != m.Building) continue;
                items[i].Strength = MathF.Min(1f, MathF.Max(items[i].Strength, m.Strength) + m.Strength * 0.25f);
                items[i].Day = m.Day;
                if (m.OtherId != 0) { items[i].OtherId = m.OtherId; items[i].OtherName = m.OtherName; }
                return false;
            }
            var add = m;
            add.Strength = MathF.Min(1f, m.Strength);
            if (Count < items.Length) { items[Count++] = add; return true; }
            int weakest = 0;
            for (int i = 1; i < Count; i++) if (items[i].Strength < items[weakest].Strength) weakest = i;
            if (items[weakest].Strength > add.Strength) return false;
            items[weakest] = add;
            return true;
        }

        /// <summary>Tägliches Abklingen; ängstliche Bürger vergessen Schlechtes langsamer.</summary>
        internal void Decay(float anxious)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                float f = Memories.DailyDecay(items[i].Kind);
                if (Memories.Valence(items[i].Kind) < 0f) f += (1f - f) * 0.35f * (anxious - 0.5f);
                items[i].Strength *= f;
                if (items[i].Strength < Memories.ForgetBelow)
                {
                    items[i] = items[Count - 1];
                    Count--;
                }
            }
        }

        /// <summary>Summe der Stärken einer Art (Einfluss auf Entscheidungen).</summary>
        public float Strength(MemoryKind kind)
        {
            float s = 0f;
            for (int i = 0; i < Count; i++) if (items[i].Kind == kind) s += items[i].Strength;
            return s;
        }

        /// <summary>Summe der Gemütswirkungen.</summary>
        public float MoodSum()
        {
            float s = 0f;
            for (int i = 0; i < Count; i++) s += items[i].MoodEffect;
            return s;
        }

        internal void Clear() => Count = 0;
    }
}
