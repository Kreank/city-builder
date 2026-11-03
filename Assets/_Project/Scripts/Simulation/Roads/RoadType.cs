using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.Roads
{
    public enum RoadCategory
    {
        City,       // Stadtstraßen
        Highway,    // Autobahn (Richtungsfahrbahn, immer Einbahn)
        Ramp,       // Auf-/Abfahrt, Verbindung Autobahn ↔ Stadt
    }

    /// <summary>
    /// Definition eines Straßentyps über seinen Querschnitt (Spuren von links nach rechts,
    /// in Segmentrichtung gesehen). Rechtsverkehr: Gegenrichtung liegt links.
    /// Später datengetrieben (JSON/ScriptableObject); Katalog siehe <see cref="RoadCatalog"/>.
    /// </summary>
    public sealed class RoadType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public RoadCategory Category { get; }
        public IReadOnlyList<LaneDefinition> Lanes { get; }

        public float Width { get; }
        public float HalfWidth => Width * 0.5f;
        /// <summary>Halbe Breite der Fahrbahn ohne Gehwege.</summary>
        public float CarriagewayHalfWidth { get; }

        public float SpeedLimitKmh { get; }
        public float CostPerMeter { get; }
        /// <summary>Lärm direkt an der Straße (0..1).</summary>
        public float NoiseLevel { get; }
        /// <summary>Wie weit der Lärm reicht, in Metern.</summary>
        public float NoiseRadius { get; }

        public bool IsOneWay { get; }
        public bool HasTram { get; }
        public bool HasBusLanes { get; }
        public int CarLanesForward { get; }
        public int CarLanesBackward { get; }

        public bool IsHighway => Category == RoadCategory.Highway;

        RoadType(string id, string displayName, RoadCategory category, List<LaneDefinition> lanes,
            float speedLimitKmh, float costPerMeter, float noiseLevel, float noiseRadius)
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
            Lanes = lanes;
            SpeedLimitKmh = speedLimitKmh;
            CostPerMeter = costPerMeter;
            NoiseLevel = noiseLevel;
            NoiseRadius = noiseRadius;

            float width = 0f, carriageway = 0f;
            bool anyBackward = false;
            foreach (var lane in lanes)
            {
                width += lane.Width;
                if (lane.Kind != LaneKind.Sidewalk)
                    carriageway = Math.Max(carriageway, Math.Max(Math.Abs(lane.Left), Math.Abs(lane.Right)));
                if (lane.IsVehicleLane && lane.Direction == LaneDirection.Backward) anyBackward = true;
                if (lane.Kind == LaneKind.Tram) HasTram = true;
                if (lane.Kind == LaneKind.Bus) HasBusLanes = true;
                if (lane.Kind == LaneKind.Car && lane.Direction == LaneDirection.Forward) CarLanesForward++;
                if (lane.Kind == LaneKind.Car && lane.Direction == LaneDirection.Backward) CarLanesBackward++;
            }
            Width = width;
            CarriagewayHalfWidth = carriageway;
            IsOneWay = !anyBackward;
        }

        /// <summary>Darf eine Straße dieses Typs an einem Knoten mit <paramref name="other"/> verbunden werden?</summary>
        public bool CanConnectTo(RoadType other) =>
            !(IsHighway && other.Category == RoadCategory.City) && !(other.IsHighway && Category == RoadCategory.City);

        /// <summary>Ebenerdige Kreuzung erlaubt? Autobahnen kreuzen nie ebenerdig (Brücken kommen mit den Höhen).</summary>
        public bool CanCrossAtGrade(RoadType other) => !IsHighway && !other.IsHighway;

        public override string ToString() => DisplayName;

        public static Builder Create(string id, string displayName, RoadCategory category) =>
            new Builder(id, displayName, category);

        public sealed class Builder
        {
            readonly string id, name;
            readonly RoadCategory category;
            readonly List<(LaneKind kind, LaneDirection dir, float width)> lanes = new List<(LaneKind, LaneDirection, float)>();
            float speed = 50f, cost = 10f, noise = 0.3f, noiseRadius = 40f;

            internal Builder(string id, string name, RoadCategory category)
            {
                this.id = id;
                this.name = name;
                this.category = category;
            }

            public Builder Sidewalk(float width = 3f) => Add(LaneKind.Sidewalk, LaneDirection.None, width);
            public Builder Shoulder(float width = 1f) => Add(LaneKind.Shoulder, LaneDirection.None, width);
            public Builder Median(float width = 2f) => Add(LaneKind.Median, LaneDirection.None, width);
            public Builder Car(LaneDirection dir, float width = 3.5f) => Add(LaneKind.Car, dir, width);
            public Builder Bus(LaneDirection dir, float width = 3.5f) => Add(LaneKind.Bus, dir, width);
            public Builder Tram(LaneDirection dir, float width = 3.2f) => Add(LaneKind.Tram, dir, width);

            public Builder Speed(float kmh) { speed = kmh; return this; }
            public Builder Cost(float perMeter) { cost = perMeter; return this; }
            public Builder Noise(float level, float radius) { noise = level; noiseRadius = radius; return this; }

            Builder Add(LaneKind kind, LaneDirection dir, float width)
            {
                lanes.Add((kind, dir, width));
                return this;
            }

            public RoadType Build()
            {
                float total = 0f;
                foreach (var l in lanes) total += l.width;

                var result = new List<LaneDefinition>(lanes.Count);
                float x = -total * 0.5f;
                foreach (var l in lanes)
                {
                    result.Add(new LaneDefinition(l.kind, l.dir, l.width, x + l.width * 0.5f));
                    x += l.width;
                }
                return new RoadType(id, name, category, result, speed, cost, noise, noiseRadius);
            }
        }
    }
}
