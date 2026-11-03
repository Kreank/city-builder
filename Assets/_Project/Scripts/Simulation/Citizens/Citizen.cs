using System.Collections.Generic;
using CityBuilder.Simulation.Citizens.Content;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>Höchster Abschluss.</summary>
    public enum Education : byte
    {
        None = 0,
        /// <summary>Grund-/Hauptschule.</summary>
        Basic,
        /// <summary>Mittlerer Abschluss/Abitur, Ausbildung.</summary>
        Secondary,
        /// <summary>Studium.</summary>
        Higher,
    }

    public enum LifeStage : byte { Child, Student, Adult, Senior }

    /// <summary>Was der Bürger gerade tut.</summary>
    public enum Activity : byte
    {
        Home,
        Work,
        Shopping,
        Travelling,
        /// <summary>Außerhalb der Stadt (Einpendler zu Hause, Auspendler bei der Arbeit).</summary>
        Outside,
        /// <summary>Freizeit im Viertel (Spaziergang/Park), Ort bleibt die Wohnung – bis G4 echte Parks als Ziel liefert.</summary>
        Leisure,
    }

    public enum Gender : byte { Female, Male }

    /// <summary>Familienstand (Kinder unter 18 sind immer <see cref="Single"/>, Anzeige „ledig (Kind)“).</summary>
    public enum Relationship : byte
    {
        Single,
        /// <summary>In Partnerschaft (mit <see cref="Citizen.PartnerId"/>).</summary>
        Partnered,
        Married,
        Separated,
        Widowed,
    }

    public enum TripPurpose : byte { None, ToWork, ToShop, ToHome }

    public enum TravelMode : byte
    {
        None,
        /// <summary>Sichtbares Auto im Verkehr.</summary>
        Car,
        /// <summary>Zu Fuß/Rad – Platzhalter mit Fahrzeit aus der Luftlinie (ÖPNV seit Stufe 1 echt: <see cref="Transit"/>).</summary>
        Other,
        /// <summary>ÖPNV: wartet an der Haltestelle bzw. fährt in einem echten Bus (<c>TransitSystem</c>).</summary>
        Transit,
    }

    /// <summary>Ort eines Bürgers: Gebäude oder Außenanschluss (Kartenrand).</summary>
    public readonly struct Place
    {
        public readonly int BuildingId;
        /// <summary>Knoten des Außenanschlusses (wenn <see cref="BuildingId"/> 0 ist).</summary>
        public readonly int ConnectionNodeId;

        Place(int building, int node)
        {
            BuildingId = building;
            ConnectionNodeId = node;
        }

        public static Place Building(int id) => new Place(id, 0);
        public static Place Outside(int nodeId) => new Place(0, nodeId);
        public static readonly Place None = default;

        public bool IsBuilding => BuildingId != 0;
        public bool IsOutside => BuildingId == 0 && ConnectionNodeId != 0;
        public bool IsNone => BuildingId == 0 && ConnectionNodeId == 0;

        public override string ToString() => IsBuilding ? $"Gebäude #{BuildingId}" : IsOutside ? $"außerhalb (Anschluss {ConnectionNodeId})" : "-";
    }

    /// <summary>
    /// Ein einzelner Bürger (Agent). Einwohner gehören zu einem <see cref="Household"/>;
    /// Einpendler (<see cref="IsCommuterIn"/>) wohnen außerhalb und haben keinen Haushalt.
    /// Änderungen nur über das <see cref="CitizenSystem"/>.
    /// </summary>
    public sealed class Citizen
    {
        public const int AdultAge = 18;
        public const int RetirementAge = 65;

        public int Id { get; internal set; }
        public int Age { get; internal set; }
        /// <summary>Tag im Jahr, an dem der Bürger älter wird.</summary>
        public int BirthDayOfYear { get; internal set; }
        public Education Education { get; internal set; }

        public Household Household { get; internal set; }
        /// <summary>Wohnt außerhalb und pendelt zur Arbeit in die Stadt.</summary>
        public bool IsCommuterIn => Household == null;
        /// <summary>Zuhause: Wohngebäude bzw. Außenanschluss (Einpendler).</summary>
        public Place Home => Household != null ? Place.Building(Household.HomeId) : Place.Outside(CommuterConnectionId);
        internal int CommuterConnectionId;

        /// <summary>Arbeitsplatz in der Stadt (Gebäude-Id, 0 = keiner).</summary>
        public int WorkplaceId { get; internal set; }
        /// <summary>Arbeitet außerhalb (Auspendler) über diesen Außenanschluss.</summary>
        public bool WorksOutside => OutsideWorkConnectionId != 0;
        public int OutsideWorkConnectionId { get; internal set; }
        public bool IsEmployed => WorkplaceId != 0 || WorksOutside;
        public Place Workplace => WorkplaceId != 0 ? Place.Building(WorkplaceId) : WorksOutside ? Place.Outside(OutsideWorkConnectionId) : Place.None;

        /// <summary>Arbeitsbeginn als Tick im Tag (0..239).</summary>
        public int WorkStart { get; internal set; }
        public int UnemployedDays { get; internal set; }

        /// <summary>Bedürfnis Einkaufen (0 = gerade eingekauft, ab 1 dringend).</summary>
        public float ShoppingNeed { get; internal set; }

        public Activity Activity { get; internal set; }
        public Place Location { get; internal set; }

        // Laufende Fahrt
        public TripPurpose Purpose { get; internal set; }
        public TravelMode Mode { get; internal set; }
        public Place Destination { get; internal set; }
        internal int TripId;
        /// <summary>Tick, an dem die laufende Fahrt begonnen hat (nur gültig bei <see cref="Activity.Travelling"/>).</summary>
        public long TripStartTick { get; internal set; }
        /// <summary>Ankunft (Platzhalter-Fahrt) bzw. spätester Zeitpunkt (Auto, Sicherheitsnetz).</summary>
        internal long ArriveTick;
        /// <summary>Geplante Ankunft einer Platzhalter-Fahrt (zu Fuß/ÖPNV); bei Autofahrten nur das Sicherheitsnetz.</summary>
        public long PlannedArrivalTick => ArriveTick;
        /// <summary>Fährt gerade als sichtbares Auto (Fahrzeug über <see cref="CitizenSystem.VehicleOf"/>).</summary>
        public bool HasCarTrip => TripId != 0;
        /// <summary>Bis wann die aktuelle Tätigkeit dauert (Arbeit, Einkauf).</summary>
        internal long BusyUntil;
        /// <summary>Lesend: bis zu diesem Tick dauert die aktuelle Tätigkeit (Arbeit, Einkauf, Freizeit, kurz nach Ankunft) – Fußgänger-Darstellung.</summary>
        public long BusyUntilTick => BusyUntil;
        internal int LastWorkDay = -1;

        // ------------------------------------------------------------ Bedürfnismodell

        public Gender Gender { get; internal set; }
        internal int FirstNameIndex, LastNameIndex;
        public string FirstName => Names.First(Gender, FirstNameIndex);
        public string LastName => Names.LastName(LastNameIndex);
        public string FullName => FirstName + " " + LastName;

        /// <summary>Charakterzüge (Gewichte auf Bedürfnisse und Entscheidungen).</summary>
        public Personality Personality { get; internal set; }

        /// <summary>Erfüllung je Bedürfnis 0..1 (Index = <see cref="NeedKind"/>), gestaffelt aktualisiert.</summary>
        internal readonly float[] NeedValues = NewNeeds();
        public float Need(NeedKind k) => NeedValues[(int)k];

        /// <summary>Zufriedenheit 0..1: gewichteter Mittelwert der Bedürfnisse (Lebensphase × Persönlichkeit).</summary>
        public float Satisfaction { get; internal set; } = 0.6f;
        /// <summary>Gemüt 0..1: kurzfristige Stimmung aus Erinnerungen (0,5 = ausgeglichen).</summary>
        public float Mood { get; internal set; } = 0.5f;
        /// <summary>Körperliche Gesundheit 0..1 (sinkt bei Krankheit, Alter, Abgas).</summary>
        public float Health { get; internal set; } = 1f;
        public bool IsIll { get; internal set; }

        public Relationship Relationship { get; internal set; }
        /// <summary>Partner (Bürger-Id, 0 = keiner). Partner wohnen im selben Haushalt.</summary>
        public int PartnerId { get; internal set; }
        /// <summary>Eltern (Bürger-Ids, 0 = unbekannt/zugezogen ohne Eltern).</summary>
        public int ParentAId { get; internal set; }
        public int ParentBId { get; internal set; }

        internal MemoryList MemoryStore;
        static readonly MemoryList NoMemories = new MemoryList();
        /// <summary>Erinnerungen (kurze Liste mit Abklingen), Texte über <see cref="CitizenSystem.MemoryText"/>.</summary>
        public MemoryList Memories => MemoryStore ?? NoMemories;

        /// <summary>Luftlinie der laufenden Fahrt (m).</summary>
        internal float TripDistance;
        /// <summary>Luftlinie der laufenden Fahrt (m) – lesend, z. B. zu Fuß (≤ <see cref="Core.TimeModel.WalkMaxMeters"/>) oder Rad.</summary>
        public float TripAirDistance => TripDistance;
        /// <summary>Luftlinie Wohnung → Arbeit (m), 0 = kein Pendeln.</summary>
        public float CommuteMeters { get; internal set; }
        internal int LastLeisureDay = -1;
        /// <summary>Laufende Fahrt zum Laden ist ein Freizeitbummel.</summary>
        internal bool LeisureTrip;
        /// <summary>Besuchter Park (Dienstgebäude-Id aus G4) während <see cref="Activity.Leisure"/>, 0 = Spaziergang im Viertel.</summary>
        public int LeisureParkId { get; internal set; }
        /// <summary>Tag der Ankunft in der Stadt/Geburt.</summary>
        public int ArrivedDay { get; internal set; }

        static float[] NewNeeds()
        {
            var n = new float[Needs.Count];
            for (int i = 0; i < n.Length; i++) n[i] = 0.6f;
            return n;
        }

        public LifeStage Stage => Age < 6 ? LifeStage.Child : Age < AdultAge ? LifeStage.Student : Age < RetirementAge ? LifeStage.Adult : LifeStage.Senior;
        public bool IsWorkingAge => Age >= AdultAge && Age < RetirementAge;

        public override string ToString() => $"Bürger #{Id} ({Age} J., {Activity})";
    }

    /// <summary>
    /// Haushaltsart – abgeleitet aus den aktuellen Mitgliedern (Alter), nicht gespeichert. Ein eigener
    /// Familienstand je Bürger existiert noch nicht; bis dahin dient die Haushaltsart als Ersatz.
    /// </summary>
    public enum HouseholdKind : byte
    {
        Single,
        Couple,
        Family,
        SingleParent,
        Seniors,
        /// <summary>Sonstige Konstellation (z. B. drei Erwachsene, nur Kinder übrig).</summary>
        Other,
    }

    public static class HouseholdKinds
    {
        /// <summary>Art eines Haushalts aus Anzahl und Alter der Mitglieder.</summary>
        public static HouseholdKind Classify(IReadOnlyList<Citizen> members)
        {
            int adults = 0, children = 0, seniors = 0;
            foreach (var m in members)
            {
                if (m.Age < Citizen.AdultAge) children++;
                else { adults++; if (m.Age >= Citizen.RetirementAge) seniors++; }
            }
            if (children > 0) return adults >= 2 ? HouseholdKind.Family : adults == 1 ? HouseholdKind.SingleParent : HouseholdKind.Other;
            if (adults == 0) return HouseholdKind.Other;
            if (seniors == adults) return HouseholdKind.Seniors;
            if (adults == 1) return HouseholdKind.Single;
            return adults == 2 ? HouseholdKind.Couple : HouseholdKind.Other;
        }

        public static string DisplayName(HouseholdKind k)
        {
            switch (k)
            {
                case HouseholdKind.Single: return "Single";
                case HouseholdKind.Couple: return "Paar";
                case HouseholdKind.Family: return "Familie";
                case HouseholdKind.SingleParent: return "Alleinerziehend";
                case HouseholdKind.Seniors: return "Senioren";
                default: return "Wohngemeinschaft/sonstige";
            }
        }
    }

    /// <summary>Haushalt: wohnt zusammen in einer Wohnung eines Gebäudes.</summary>
    public sealed class Household
    {
        public int Id { get; internal set; }
        public int HomeId { get; internal set; }
        public int MovedInDay { get; internal set; }
        internal readonly List<Citizen> MemberList = new List<Citizen>();
        public IReadOnlyList<Citizen> Members => MemberList;
        /// <summary>Haushaltsart aus den aktuellen Mitgliedern (ändert sich mit Altern/Tod).</summary>
        public HouseholdKind Kind => HouseholdKinds.Classify(MemberList);
        /// <summary>
        /// Einkommen je Spieltag (Löhne/Renten der Mitglieder, brutto, ohne Steuern – die rechnet die Wirtschaft).
        /// Täglich vom Bürgersystem gesetzt.
        /// </summary>
        public float Income { get; internal set; }
        /// <summary>Mittlere Zufriedenheit der Mitglieder (gesetzt mit der Statistik).</summary>
        public float Satisfaction { get; internal set; } = 0.6f;

        public override string ToString() => $"Haushalt #{Id} ({MemberList.Count} Personen)";
    }
}
