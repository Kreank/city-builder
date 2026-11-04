using System;
using System.Collections.Generic;
using System.Text;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Persönlichkeit eines Landes für die Handels-KI (je 0..1, deterministisch aus Seed + Land, leicht vom Profil geprägt).
    /// <list type="bullet">
    /// <item><see cref="Risk"/> risikofreudig: höhere Entscheidungs-Temperatur (mehr Zufall), kürzere Verträge, eher Weltpreis-Kopplung,
    /// verzeiht schlechtes Vertrauen eher.</item>
    /// <item><see cref="Protectionism"/>: eigener Grundzoll 0–20 %, ungern Import in Kategorien eigener Stärke, Vergeltungszölle,
    /// zögert bei Handelsabkommen.</item>
    /// <item><see cref="Reliability"/> verlässlich: Sicherheitsreserve im Lager klein (liefert auch bei knapper Ware), bricht selten bewusst.</item>
    /// <item><see cref="Thrift"/> geschäftstüchtig: verhandelt Preise näher am eigenen Vorteil, gewichtet den Preis stärker.</item>
    /// </list>
    /// </summary>
    public sealed class NationPersonality
    {
        public float Risk { get; internal set; }
        public float Protectionism { get; internal set; }
        public float Reliability { get; internal set; }
        public float Thrift { get; internal set; }

        /// <summary>Softmax-Temperatur der Entscheidungen.</summary>
        public float Temperature => 0.05f + 0.2f * Risk;
        /// <summary>Temperatur der Annahme (logistisch).</summary>
        public float AcceptTemperature => 0.08f + 0.15f * Risk;
        /// <summary>Lagerreserve in Tagen Bedarf, unter die das Land für Exportverträge nicht geht.</summary>
        public double ReserveDays => 3.0 + 12.0 * (1.0 - Reliability);

        internal static NationPersonality For(int seed, Country c, GovernmentForm government)
        {
            var rng = new SimRandom(unchecked(seed * 92821 + c.Id * 7907 + 17));
            var p = new NationPersonality
            {
                Risk = rng.NextFloat(),
                Protectionism = rng.NextFloat(),
                Reliability = 0.25f + 0.75f * rng.NextFloat(),
                Thrift = rng.NextFloat(),
            };
            // Profil prägt: Rohstoff-/Agrarländer leben vom Export → offener; Industrie schützt eher den eigenen Markt
            var pr = c.Profile;
            if (pr.Primary == Strength.RawMaterials || pr.Primary == Strength.Agriculture) p.Protectionism *= 0.75f;
            if (pr.Primary == Strength.Industry) p.Protectionism = MathF.Min(1f, p.Protectionism * 1.15f + 0.05f);
            if (pr.Primary == Strength.Technology) p.Thrift = MathF.Min(1f, p.Thrift + 0.1f);
            // Handel 2: Regierungsform prägt (kleine Verschiebung, s. Governments.Nudge)
            var (dr, dp, dl, dt) = Governments.Nudge(government);
            p.Risk = Math.Clamp(p.Risk + dr, 0f, 1f);
            p.Protectionism = Math.Clamp(p.Protectionism + dp, 0f, 1f);
            p.Reliability = Math.Clamp(p.Reliability + dl, 0.2f, 1f);
            p.Thrift = Math.Clamp(p.Thrift + dt, 0f, 1f);
            return p;
        }

        /// <summary>Handel 2: Geduld in Verhandlungen = Gegenangebote, die das Land höchstens macht (2–4; verlässlich/vorsichtig = geduldiger).</summary>
        public int Patience => 1 + (int)MathF.Round(3f * (0.3f + 0.4f * Reliability + 0.3f * (1f - Risk)));

        public string Describe()
        {
            var sb = new StringBuilder();
            void Add(string s) { if (sb.Length > 0) sb.Append(", "); sb.Append(s); }
            Add(Risk > 0.66f ? "risikofreudig" : Risk < 0.33f ? "vorsichtig" : "abwägend");
            if (Protectionism > 0.6f) Add("protektionistisch");
            else if (Protectionism < 0.3f) Add("freihändlerisch");
            Add(Reliability > 0.75f ? "verlässlich" : Reliability < 0.45f ? "unzuverlässig" : "meist verlässlich");
            if (Thrift > 0.66f) Add("geschäftstüchtig");
            return sb.ToString();
        }
    }

    /// <summary>Zollsätze eines Landes: Grundzoll + Sätze je Lieferland (NaN = Grundzoll). Handelsabkommen setzen effektiv 0.</summary>
    public sealed partial class TariffSchedule
    {
        readonly double[] overrides;
        public double General { get; internal set; }

        internal TariffSchedule(int countries, double general)
        {
            General = general;
            overrides = new double[countries];
            for (int i = 0; i < countries; i++) overrides[i] = double.NaN;
        }

        /// <summary>Eigener Satz für ein Lieferland (NaN = Grundzoll gilt).</summary>
        public double Override(int country) => country >= 0 && country < overrides.Length ? overrides[country] : double.NaN;
        public bool HasOverride(int country) => !double.IsNaN(Override(country));
        /// <summary>Satz für ein Lieferland ohne Abkommen.</summary>
        public double RateFor(int country) => HasOverride(country) ? overrides[country] : General;
        internal void Set(int country, double rate)
        {
            if (country < 0) General = rate;
            else overrides[country] = rate;
        }
    }

    /// <summary>
    /// Handelszustand eines Landes (Spieler und NPC gleich): Persönlichkeit, Zölle, Vertrauen in andere Länder, Gedächtnis,
    /// Handelsvolumen. Beziehungen (symmetrisch) liegen im <see cref="TradeSystem"/>.
    /// </summary>
    public sealed partial class NationTrade
    {
        public const int MemoryCapacity = 12;

        public int Id { get; }
        public Country Country { get; }
        public NationEconomy Economy => Country.Economy;
        public bool IsPlayer => Country.IsPlayer;
        public NationPersonality Personality { get; }
        /// <summary>Handel 2: Regierungsform (aus Seed + Profil, Spieler Republik).</summary>
        public GovernmentForm Government { get; }
        /// <summary>Handel 2: Tourismus gestern (Kategorie-Einheiten): eingereist (Export) bzw. ausgereist (Import), Wert seit Beginn.</summary>
        public double TourismInYesterday { get; internal set; }
        public double TourismOutYesterday { get; internal set; }
        public double TourismExportValue { get; internal set; }
        public double TourismImportValue { get; internal set; }
        /// <summary>Handel 2: verschenkt seit Beginn (€).</summary>
        public double GiftsGiven { get; internal set; }
        public TariffSchedule Tariffs { get; }
        /// <summary>Vertrauen in andere Länder (0..1).</summary>
        public readonly float[] Trust;
        /// <summary>Geglättetes Handelsvolumen je Kategorie (Kategorie-Einheiten/Tag, Ein- + Ausfuhr) – Gewicht im Weltpreis.</summary>
        public readonly double[] TradeVolume = new double[GoodsCategories.Count];
        /// <summary>Heute gehandelt (Kategorie-Einheiten), wird täglich in <see cref="TradeVolume"/> geglättet.</summary>
        internal readonly double[] TradedToday = new double[GoodsCategories.Count];
        /// <summary>Summe der Werte seit Beginn: Ausfuhr, Einfuhr, eingenommene Zölle, gezahlte/erhaltene Strafen.</summary>
        public double ExportValue { get; internal set; }
        public double ImportValue { get; internal set; }
        public double TariffIncome { get; internal set; }
        public double PenaltiesPaid { get; internal set; }
        public double PenaltiesReceived { get; internal set; }
        readonly List<TradeMemory> memory = new List<TradeMemory>();
        public IReadOnlyList<TradeMemory> Memory => memory;

        internal NationTrade(Country c, int countries, int seed)
        {
            Id = c.Id;
            Country = c;
            Government = Governments.For(seed, c); // Handel 2
            Personality = NationPersonality.For(seed, c, Government);
            double general = c.IsPlayer ? 0.0 : Math.Round(Math.Min(TradeRules.MaxTariff, Personality.Protectionism * 0.2 * Governments.TariffFactor(Government)), 2);
            Tariffs = new TariffSchedule(countries, general);
            Trust = new float[countries];
            for (int i = 0; i < countries; i++) Trust[i] = TradeRules.TrustStart;
        }

        internal void Remember(TradeMemory m)
        {
            // gleiche Art + Partner → auffrischen, sonst älteste ersetzen
            for (int i = 0; i < memory.Count; i++)
                if (memory[i].Kind == m.Kind && memory[i].Partner == m.Partner) { memory.RemoveAt(i); break; }
            if (memory.Count >= MemoryCapacity) memory.RemoveAt(0);
            memory.Add(m);
        }

        public override string ToString() => Country.Name;
    }
}
