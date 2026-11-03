using System.Collections.Generic;

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
    }

    public enum TripPurpose : byte { None, ToWork, ToShop, ToHome }

    public enum TravelMode : byte
    {
        None,
        /// <summary>Sichtbares Auto im Verkehr.</summary>
        Car,
        /// <summary>Zu Fuß/Rad/ÖPNV – vorerst ein Platzhalter mit Fahrzeit aus der Luftlinie.</summary>
        Other,
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
        internal int LastWorkDay = -1;

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

        public override string ToString() => $"Haushalt #{Id} ({MemberList.Count} Personen)";
    }
}
