using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Realm;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Das Gebiet des Spielers (Spielstart &amp; Startregion, <b>Etappe 7 Reich</b>): eigene Städte, Stadtname, Landesname und Flagge.
    /// Der Spieler beginnt als <b>Stadtstaat</b> ohne eigenen Staatsnamen – angezeigt als „Freie Stadt {Hauptstadt}“
    /// (<see cref="DisplayName"/>). Er gründet weitere Städte (<see cref="Cities"/>, <see cref="FoundCityCommand"/>) und ab
    /// <see cref="MinCitiesForNation"/> Städten ein <b>Land</b> (<see cref="NationName"/>, <see cref="FlagColor"/>, Hauptstadt).
    /// <para>
    /// Genau eine Stadt läuft detailliert (<see cref="ActiveCity"/> = die <c>SimulationWorld</c>), die anderen aggregiert
    /// (<see cref="RealmCity.Economy"/>) mit eingefrorenem Detailzustand (<see cref="RealmCity.Snapshot"/>). Die Logik (Tageslauf,
    /// Lieferungen, Gründen, NPC-Reaktion/Expansion) steht im <see cref="RealmSystem"/>, der Wechsel in <see cref="RealmSwitch"/>.
    /// </para>
    /// <para>
    /// Immer vorhanden (<c>SimulationWorld.Realm</c>), auch ohne Weltkarte (dann nur der Stadtname). Gespeichert im Abschnitt
    /// „realm“ (v2): Namen, Städte samt Detail-Schnappschüssen, Lieferungen, Landeskasse, Anerkennung, Expansion und der
    /// <b>Besitz aller Regionen</b> (Land je Region, −1 = frei) – der Planet entsteht beim Laden neu aus dem Seed.
    /// Der Abschnitt gehört zu den <b>reichsweiten</b> Abschnitten, die beim Stadtwechsel mitwandern (s. <see cref="RealmSwitch"/>).
    /// </para>
    /// </summary>
    public sealed partial class PlayerRealm : ISaveable
    {
        public const string DefaultCityName = "Neustadt";
        public const int MaxNameLength = 40;
        /// <summary>So viele eigene Städte braucht es, um ein Land zu gründen.</summary>
        public const int MinCitiesForNation = RealmRules.MinCitiesForNation;

        readonly Planet planet;
        readonly List<RealmCity> cities = new List<RealmCity>();
        internal readonly List<RealmDelivery> deliveries = new List<RealmDelivery>();
        internal readonly List<RealmShipment> shipments = new List<RealmShipment>();
        internal readonly List<RealmNews> news = new List<RealmNews>();
        internal readonly Queue<RealmCommand> queue = new Queue<RealmCommand>();
        internal readonly List<RealmLogEntry> log = new List<RealmLogEntry>();

        public PlayerRealm(Planet planet, string cityName = null)
        {
            this.planet = planet;
            var first = new RealmCity
            {
                Id = 0, Name = Clean(cityName) ?? DefaultCityName,
                RegionId = planet?.StartRegionId ?? -1,
                Cell = planet?.PlayerCountry != null && planet.PlayerCountry.IsCityState ? planet.PlayerCountry.CapitalCell
                     : planet?.StartRegion?.CenterCell ?? -1,
            };
            cities.Add(first);
            int n = planet?.Countries.Count ?? 0;
            Recognized = new bool[n];
            RecognitionReviewDay = new int[n];
            LastClaimDay = new int[n];
            for (int i = 0; i < n; i++) LastClaimDay[i] = -RealmRules.ExpansionCooldownDays;
            InitialFreeRegions = planet?.FreeRegionCount ?? 0;
            Rng = new SimRandom(unchecked((planet?.Seed ?? 1) * 7919 + 0x5EA1));
            RefreshRegions();
            Apply();
        }

        public Planet Planet => planet;

        // ------------------------------------------------------------ Städte

        /// <summary>Alle eigenen Städte (Gründungsreihenfolge, zuerst die Startstadt).</summary>
        public IReadOnlyList<RealmCity> Cities => cities;
        /// <summary>Id der detailliert laufenden Stadt (= diese <c>SimulationWorld</c>).</summary>
        public int ActiveCityId { get; internal set; }
        public RealmCity ActiveCity => City(ActiveCityId);
        public RealmCity City(int id)
        {
            foreach (var c in cities) if (c.Id == id) return c;
            return null;
        }
        internal int NextCityId { get; set; } = 1;
        internal void AddCity(RealmCity c) { cities.Add(c); RefreshRegions(); Version++; }

        /// <summary>Hauptstadt (vor der Landesgründung die Startstadt).</summary>
        public int CapitalCityId { get; internal set; }
        public RealmCity CapitalCity => City(CapitalCityId) ?? (cities.Count > 0 ? cities[0] : null);

        /// <summary>Name der detailliert laufenden Stadt (obere Leiste, Spielstand).</summary>
        public string CityName => ActiveCity?.Name ?? DefaultCityName;
        /// <summary>Regionen mit eigenen Städten (zuerst die der Startstadt, ohne Doppelte).</summary>
        public List<int> CityRegions { get; } = new List<int>();

        internal void RefreshRegions()
        {
            CityRegions.Clear();
            foreach (var c in cities) if (c.RegionId >= 0 && !CityRegions.Contains(c.RegionId)) CityRegions.Add(c.RegionId);
        }

        /// <summary>Wird bei jeder Änderung erhöht (Darstellung).</summary>
        public int Version { get; internal set; }
        /// <summary>Logik des Reichs (gesetzt von <c>SimulationWorld.CreateDefault</c>; null in Welten ohne Standardsysteme).</summary>
        public RealmSystem System { get; internal set; }

        // ------------------------------------------------------------ Land

        /// <summary>Name des gegründeten Landes; null = noch Stadtstaat.</summary>
        public string NationName { get; internal set; }
        /// <summary>Flaggen-/Kartenfarbe RGB (Standard Gold wie der Stadtstaat).</summary>
        public uint FlagColor { get; internal set; } = PlanetGenerator.PlayerColor;
        /// <summary>Tag der Landesgründung (−1 = noch keine).</summary>
        public int NationFoundedDay { get; internal set; } = -1;
        /// <summary>Landeskasse (Landesabgabe der Städte, Zuschüsse an Städte).</summary>
        public double NationTreasury { get; internal set; }
        public double NationIncomeTotal { get; internal set; }
        /// <summary>Anerkennung durch die Länder (Index = Land-Id) und nächster Prüftag.</summary>
        public bool[] Recognized { get; private set; }
        internal int[] RecognitionReviewDay { get; private set; }

        /// <summary>Hat der Spieler schon ein eigenes Land gegründet?</summary>
        public bool IsNation => NationName != null;
        /// <summary>Alle Bedingungen der Landesgründung erfüllt (s. <see cref="RealmSystem.CheckNation"/>).</summary>
        public bool CanFoundNation => !IsNation && System != null ? System.CheckNation(null, -1).Ok : !IsNation && cities.Count >= MinCitiesForNation;
        /// <summary>Anzeigename des Spieler-Gebiets: Landesname oder „Freie Stadt {Hauptstadt}“.</summary>
        public string DisplayName => NationName ?? CityStateName(CapitalCity?.Name);

        /// <summary>Platzhalter-Name eines Stadtstaats.</summary>
        public static string CityStateName(string cityName) => "Freie Stadt " + (Clean(cityName) ?? DefaultCityName);

        /// <summary>Bereinigt einen Namen (Leerraum, Länge); null bei leerer Eingabe.</summary>
        public static string Clean(string name)
        {
            if (name == null) return null;
            var s = name.Trim();
            if (s.Length == 0) return null;
            if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength).TrimEnd();
            return s;
        }

        /// <summary>Aktive Stadt umbenennen (leer = unverändert). Der Stadtstaat auf der Weltkarte/im Handel heißt mit.</summary>
        public bool RenameCity(string name) => RenameCity(ActiveCityId, name);

        /// <summary>Eigene Stadt umbenennen (leer oder schon vergeben = unverändert).</summary>
        public bool RenameCity(int cityId, string name)
        {
            var s = Clean(name);
            var c = City(cityId);
            if (s == null || c == null) return false;
            foreach (var o in cities) if (o != c && string.Equals(o.Name, s, StringComparison.OrdinalIgnoreCase)) return false;
            c.Name = s;
            Apply();
            Version++;
            return true;
        }

        /// <summary>Namen/Farbe/Hauptstadt auf das Spieler-Land des Planeten übertragen (nur Stadtstaat bzw. gegründetes Land, nie ein NPC-Land).</summary>
        internal void Apply()
        {
            var c = planet?.PlayerCityState;
            if (c == null) return;
            bool changed = c.Name != DisplayName || c.Color != FlagColor;
            c.Name = DisplayName;
            var cap = CapitalCity;
            c.CapitalName = cap?.Name ?? DefaultCityName;
            c.Color = FlagColor;
            if (cap != null && cap.Cell >= 0 && cap.RegionId >= 0 && planet.Regions[cap.RegionId].CountryId == c.Id)
            {
                changed |= c.CapitalCell != cap.Cell;
                c.CapitalCell = cap.Cell;
                c.CapitalRegionId = cap.RegionId;
            }
            if (changed) planet.PoliticalVersion++;
        }

        // ------------------------------------------------------------ NPC-Expansion

        /// <summary>NPC-Länder beanspruchen über die Zeit freies Land (Standard aus; das Spiel schaltet ein).</summary>
        public bool NpcExpansion { get; set; }
        /// <summary>Freie Regionen beim Spielstart (Obergrenze der Expansion).</summary>
        public int InitialFreeRegions { get; internal set; }
        internal int[] LastClaimDay { get; private set; }
        public int RegionsClaimedByNpc { get; internal set; }
        internal SimRandom Rng { get; private set; }

        // ------------------------------------------------------------ Lieferungen, Meldungen, Commands

        public IReadOnlyList<RealmDelivery> Deliveries => deliveries;
        public IReadOnlyList<RealmShipment> Shipments => shipments;
        internal int NextDeliveryId { get; set; } = 1;
        public RealmDelivery Delivery(int id)
        {
            foreach (var d in deliveries) if (d.Id == id) return d;
            return null;
        }

        /// <summary>Letzte Meldungen (neueste zuletzt).</summary>
        public IReadOnlyList<RealmNews> News => news;
        internal void Report(int day, string title, string text, bool important = false)
        {
            news.Add(new RealmNews(day, title, text, important));
            if (news.Count > RealmRules.NewsCapacity) news.RemoveRange(0, news.Count - RealmRules.NewsCapacity);
            Version++;
        }

        public IReadOnlyList<RealmLogEntry> CommandLog => log;

        /// <summary>Summe der Einwohner aller Städte (aktive aus der Detailsimulation, übrige aggregiert).</summary>
        public double TotalPopulation
        {
            get { double s = 0; foreach (var c in cities) s += c.Economy.Population; return s; }
        }
    }

    /// <summary>Ausgeführtes Reichs-Command (Tag, Command, Ergebnis) – Grundlage für Replays.</summary>
    public readonly struct RealmLogEntry
    {
        public readonly int Day;
        public readonly RealmCommand Command;
        public readonly CityBuilder.Simulation.Trade.TradeResult Result;
        public RealmLogEntry(int day, RealmCommand command, CityBuilder.Simulation.Trade.TradeResult result) { Day = day; Command = command; Result = result; }
    }
}
