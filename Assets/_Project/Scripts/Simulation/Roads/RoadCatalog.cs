using System.Collections.Generic;
using static CityBuilder.Simulation.Roads.LaneDirection;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Alle verfügbaren Straßentypen. Neue Typen = neuer Eintrag hier (später aus Datendateien).
    /// Querschnitte von links nach rechts in Bau-/Fahrtrichtung, Rechtsverkehr.
    /// </summary>
    public static class RoadCatalog
    {
        // ---- Stadt ----
        public static readonly RoadType CityTwoLane = RoadType.Create("city_2", "Stadtstraße 2-spurig", RoadCategory.City)
            .Sidewalk().Car(Backward).Car(Forward).Sidewalk()
            .Speed(50).Cost(12).Noise(0.35f, 40f).Build();

        public static readonly RoadType CityFourLane = RoadType.Create("city_4", "Stadtstraße 4-spurig", RoadCategory.City)
            .Sidewalk().Car(Backward).Car(Backward).Car(Forward).Car(Forward).Sidewalk()
            .Speed(50).Cost(22).Noise(0.55f, 60f).Build();

        public static readonly RoadType CityOneWay1 = RoadType.Create("city_oneway_1", "Einbahnstraße 1-spurig", RoadCategory.City)
            .Sidewalk().Car(Forward, 4f).Sidewalk()
            .Speed(30).Cost(8).Noise(0.25f, 30f).Build();

        public static readonly RoadType CityOneWay2 = RoadType.Create("city_oneway_2", "Einbahnstraße 2-spurig", RoadCategory.City)
            .Sidewalk().Car(Forward).Car(Forward).Sidewalk()
            .Speed(50).Cost(12).Noise(0.35f, 40f).Build();

        public static readonly RoadType CityBusLanes = RoadType.Create("city_2_bus", "Stadtstraße mit Busspuren", RoadCategory.City)
            .Sidewalk().Bus(Backward).Car(Backward).Car(Forward).Bus(Forward).Sidewalk()
            .Speed(50).Cost(24).Noise(0.5f, 55f).Build();

        public static readonly RoadType CityTram = RoadType.Create("city_2_tram", "Stadtstraße mit Tram", RoadCategory.City)
            .Sidewalk().Car(Backward).Tram(Backward).Tram(Forward).Car(Forward).Sidewalk()
            .Speed(50).Cost(32).Noise(0.5f, 55f).Build();

        public static readonly RoadType CityOneWayBus = RoadType.Create("city_oneway_bus", "Einbahnstraße mit Busspur", RoadCategory.City)
            .Sidewalk().Car(Forward).Car(Forward).Bus(Forward).Sidewalk()
            .Speed(50).Cost(18).Noise(0.4f, 45f).Build();

        public static readonly RoadType CityOneWayTram = RoadType.Create("city_oneway_tram", "Einbahnstraße mit Tram", RoadCategory.City)
            .Sidewalk().Car(Forward).Car(Forward).Tram(Forward).Sidewalk()
            .Speed(50).Cost(26).Noise(0.4f, 45f).Build();

        // ---- Autobahn (Richtungsfahrbahnen – je Richtung eine eigene Straße, wie in Cities: Skylines) ----
        public static readonly RoadType Highway2 = RoadType.Create("highway_2", "Autobahn 2-spurig (eine Richtung)", RoadCategory.Highway)
            .Shoulder(1f).Car(Forward, 3.75f).Car(Forward, 3.75f).Shoulder(2.5f)
            .Speed(130).Cost(45).Noise(0.85f, 100f).Build();

        public static readonly RoadType Highway3 = RoadType.Create("highway_3", "Autobahn 3-spurig (eine Richtung)", RoadCategory.Highway)
            .Shoulder(1f).Car(Forward, 3.75f).Car(Forward, 3.75f).Car(Forward, 3.75f).Shoulder(2.5f)
            .Speed(130).Cost(60).Noise(1f, 120f).Build();

        public static readonly RoadType Ramp = RoadType.Create("ramp_1", "Auf-/Abfahrt", RoadCategory.Ramp)
            .Shoulder(1f).Car(Forward, 4f).Shoulder(1.5f)
            .Speed(80).Cost(30).Noise(0.6f, 70f).Build();

        public static readonly RoadType[] All =
        {
            CityTwoLane, CityFourLane, CityOneWay1, CityOneWay2,
            CityBusLanes, CityOneWayBus, CityTram, CityOneWayTram,
            Highway2, Highway3, Ramp,
        };

        /// <summary>Standard-Fahrbahn für Kreisverkehre.</summary>
        public static RoadType DefaultRoundabout => CityOneWay1;

        static Dictionary<string, RoadType> byId;

        public static RoadType Get(string id)
        {
            if (byId == null)
            {
                byId = new Dictionary<string, RoadType>();
                foreach (var t in All) byId[t.Id] = t;
            }
            return byId.TryGetValue(id, out var type) ? type : null;
        }
    }
}
