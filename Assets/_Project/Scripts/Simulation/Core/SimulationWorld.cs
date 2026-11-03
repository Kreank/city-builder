using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Core
{
    /// <summary>
    /// Der komplette Zustand einer Stadt bzw. später eines Reiches.
    /// Kennt kein Unity – kann in Tests, auf einem Server oder headless laufen.
    /// Koordinaten: Vector2(x, y) entspricht in Unity (x, 0, z).
    /// </summary>
    public sealed class SimulationWorld
    {
        readonly List<ISimulationSystem> systems = new List<ISimulationSystem>();

        public WorldSettings Settings { get; }
        public SimulationClock Clock { get; } = new SimulationClock();
        public RoadNetwork Roads { get; } = new RoadNetwork();
        public EnvironmentLayers Environment { get; }

        public Vector2 Min => new Vector2(-Settings.SizeMeters * 0.5f);
        public Vector2 Max => new Vector2(Settings.SizeMeters * 0.5f);

        /// <summary>Höhenkarte. Ebenerdige Straßen passen das Gelände an (Damm/Einschnitt).</summary>
        public TerrainMap Terrain { get; }

        public SimulationWorld(WorldSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Environment = new EnvironmentLayers(Min, settings.SizeMeters, settings.EnvironmentCellSize);
            Terrain = settings.GenerateTerrain
                ? TerrainMap.Generate(Min, settings.SizeMeters, settings.TerrainCellSize, settings.TerrainSeed)
                : TerrainMap.Flat(Min, settings.SizeMeters, settings.TerrainCellSize);
            Roads.Ground = Terrain;
            Terrain.RoadRule = RoadRule;
            Roads.SegmentAdded += Terrain.ConformTo;
            // Terraforming: Gelände unter Straßen, Gebäuden und Versorgungsbauwerken ist gesperrt (weich überblendet)
            Terrain.LockQuery = (min, max) =>
            {
                var locks = TerrainLocks.Query(Roads, City?.Buildings, Terrain, min, max);
                if (Utilities == null) return locks;
                var facilities = new List<UtilityFacility>();
                foreach (var f in Utilities.Facilities)
                    if (f.Contains(Vector2.Clamp(f.Position, min, max), TerrainLocks.Band + Terrain.CellSize + MathF.Max(f.Type.Width, f.Type.Depth)))
                        facilities.Add(f);
                if (facilities.Count == 0) return locks;
                return p =>
                {
                    foreach (var f in facilities)
                        if (f.Contains(p, Terrain.CellSize)) return 1f;
                    return locks?.Invoke(p) ?? 0f;
                };
            };
        }

        /// <summary>
        /// Straßen haben Vorrang vor dem Gelände: unter einer ebenerdigen Fahrbahn liegt es exakt knapp darunter,
        /// im Streifen daneben nie höher. Brücken und Tunnel (weit über/unter dem natürlichen Gelände) zählen nicht.
        /// </summary>
        float RoadRule(Vector2 p, float proposed)
        {
            float result = proposed;
            float margin = Terrain.RoadMargin;
            foreach (var s in Roads.Segments)
            {
                float reach = s.Type.HalfWidth + margin;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var min, out var max);
                if (p.X < min.X - reach || p.Y < min.Y - reach || p.X > max.X + reach || p.Y > max.Y + reach) continue;

                float t = s.ClosestT(p);
                var q = s.Evaluate(t);
                float d = Vector2.Distance(p, q);
                if (d > reach) continue;

                float road = s.HeightAt(t);
                // Nur ebenerdige Abschnitte (bezogen aufs natürliche Gelände – stabil gegen spätere Änderungen)
                float natural = road - Terrain.NaturalHeightAt(q);
                float limit = s.FollowsTerrain ? TerrainMap.MaxCutFill + 0.5f : TerrainMap.ConformRange + 0.5f;
                if (natural > limit || natural < -limit) continue;

                float surface = road - TerrainMap.UnderRoad;
                result = d <= s.Type.HalfWidth ? surface : MathF.Min(result, surface);
            }
            return result;
        }

        /// <summary>Verkehr (Spurgraph, Regeln, Fahrzeuge). Gesetzt von <see cref="CreateDefault"/>.</summary>
        public TrafficSystem Traffic { get; private set; }

        /// <summary>Welt mit den Standard-Systemen.</summary>
        public static SimulationWorld CreateDefault(WorldSettings settings = null, int seed = 12345)
        {
            var world = new SimulationWorld(settings ?? new WorldSettings());
            world.AddSystem(new NoiseSystem());
            world.Traffic = new TrafficSystem(world.Roads, world.Min, world.Max, seed);
            world.AddSystem(world.Traffic);
            world.City = new CitySystem(world.Roads, world.Terrain, world.Min, world.Settings.SizeMeters, seed);
            world.AddSystem(world.City);
            // G2 Bürger (nach Verkehr und Stadt: Ankünfte und neue Gebäude sind im selben Tick schon bekannt)
            world.Citizens = new CitizenSystem(world.City, world.Traffic, world.Roads, world.Clock, seed);
            world.AddSystem(world.Citizens);

            // --- G3 Strom & Wasser (nach Lärm und Stadt: stempelt nach dem Neuzeichnen des Lärms, liest fertige Gebäude)
            world.Utilities = new UtilitySystem(world.Roads, world.Terrain, world.City, world.Environment, world.Min, world.Max);
            world.AddSystem(world.Utilities);
            // --- Ende G3

            // --- G4 Dienste (nach der Versorgung: hängt sich an deren Grundstücks-/Bau-Sperren an, stempelt Lärm nach dem NoiseSystem)
            world.Services = new ServiceSystem(world.Roads, world.Terrain, world.City, world.Utilities, world.Environment, world.Min, world.Max);
            world.AddSystem(world.Services);
            // Bedürfnismodell ↔ Dienste: Abdeckung, Müll, Parks als Freizeitziel, Schülerzahlen
            CitizenServiceBinding.Connect(world.Citizens, world.Services);
            // --- Ende G4

            // --- Stadtkasse (zuletzt: rechnet täglich mit fertigem Stand ab; Baukosten über die Hooks)
            world.Economy = new Economy.EconomySystem(world.Settings, world.Roads, world.City, world.Utilities);
            world.Roads.Budget = world.Economy;
            world.Terrain.Budget = world.Economy;
            world.Utilities.Budget = world.Economy;
            world.City.DemandModifier = world.Economy.DemandFactor;
            world.Services.Budget = world.Economy;          // G4: Baukosten Dienste
            world.Economy.RegisterUpkeep(world.Services);   // G4: Unterhalt Dienste
            world.AddSystem(world.Economy);
            // --- Ende Stadtkasse
            return world;
        }

        /// <summary>Zonen und Gebäude. Gesetzt von <see cref="CreateDefault"/>.</summary>
        public CitySystem City { get; private set; }

        /// <summary>Bürger, Haushalte, Jobs, Pendeln (G2). Gesetzt von <see cref="CreateDefault"/>.</summary>
        public CitizenSystem Citizens { get; private set; }

        /// <summary>Strom &amp; Wasser (G3). Gesetzt von <see cref="CreateDefault"/>.</summary>
        public UtilitySystem Utilities { get; private set; }

        /// <summary>Dienste (G4): Polizei, Feuerwehr, Gesundheit, Schulen, Bibliothek, Parks, Müll. Gesetzt von <see cref="CreateDefault"/>.</summary>
        public ServiceSystem Services { get; private set; }

        /// <summary>Stadtkasse (Kontostand, Steuern, Bilanz). Gesetzt von <see cref="CreateDefault"/>.</summary>
        public Economy.EconomySystem Economy { get; private set; }

        public void AddSystem(ISimulationSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (system.TickInterval < 1) throw new ArgumentException("TickInterval muss >= 1 sein.", nameof(system));
            systems.Add(system);
        }

        public T GetSystem<T>() where T : class, ISimulationSystem
        {
            foreach (var s in systems)
                if (s is T typed) return typed;
            return null;
        }

        public bool Contains(Vector2 p) =>
            p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

        /// <summary>Simuliert genau einen Tick.</summary>
        public void Step()
        {
            Clock.Advance();
            foreach (var system in systems)
            {
                if (Clock.Tick % system.TickInterval == 0)
                    system.Tick(this);
            }
        }
    }
}
