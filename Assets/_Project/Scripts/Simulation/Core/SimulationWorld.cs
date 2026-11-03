using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.World;
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
            // --- Welt (Etappe 5): Startregion bestimmt Gelände, Klima und Ressourcen der Stadtkarte
            TerrainParams terrainParams = null;
            var climate = ClimateParams.Default;
            var resources = RegionResources.Default;
            int terrainSeed = settings.TerrainSeed;
            if (settings.GenerateWorld)
            {
                Planet = PlanetGenerator.Generate(settings.WorldSeed, settings.Planet);
                int start = settings.StartRegion >= 0 && settings.StartRegion < Planet.Regions.Count
                    ? settings.StartRegion : Planet.SuggestStartRegion();
                Planet.SetStartRegion(start);
                var region = Planet.StartRegion;
                terrainParams = RegionMapping.TerrainFor(region, Planet);
                climate = RegionMapping.ClimateFor(region);
                resources = RegionMapping.ResourcesFor(region);
                terrainSeed = unchecked(settings.TerrainSeed + region.Id * 7919);
            }
            Terrain = settings.GenerateTerrain
                ? TerrainMap.Generate(Min, settings.SizeMeters, settings.TerrainCellSize, terrainSeed, terrainParams)
                : TerrainMap.Flat(Min, settings.SizeMeters, settings.TerrainCellSize);
            Climate = new ClimateModel(climate, unchecked(terrainSeed * 31 + settings.WorldSeed), Clock);
            Resources = ResourceMap.Generate(Min, settings.SizeMeters, settings.EnvironmentCellSize, resources, Terrain, terrainSeed);
            // --- Ende Welt
            Roads.Ground = Terrain;
            Terrain.RoadRule = RoadRule;
            Roads.SegmentAdded += Terrain.ConformTo;
            Roads.SegmentAdded += Resources.ClearUnderRoad; // Welt (Etappe 5): Straßen roden Wald, versiegeln Boden
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

        // --- Welt (Etappe 5)
        /// <summary>Fiktive Erde (nur mit <see cref="WorldSettings.GenerateWorld"/>, sonst null).</summary>
        public Planet Planet { get; }
        /// <summary>Region der Stadtkarte (null ohne Weltkarte).</summary>
        public Region StartRegion => Planet?.StartRegion;
        /// <summary>Klima der Stadt: Jahreszeit, Temperatur/Niederschlag je Tag, Ernte-/Heizfaktor (immer gesetzt, ohne Welt gemäßigt).</summary>
        public ClimateModel Climate { get; }
        /// <summary>Ressourcen-Raster der Stadtkarte (Boden, Wald, Erz, Kohle, Öl, Sand, Grundwasser) – immer gesetzt.</summary>
        public ResourceMap Resources { get; }
        // --- Ende Welt

        /// <summary>Verkehr (Spurgraph, Regeln, Fahrzeuge). Gesetzt von <see cref="CreateDefault"/>.</summary>
        public TrafficSystem Traffic { get; private set; }

        /// <summary>Welt mit den Standard-Systemen.</summary>
        public static SimulationWorld CreateDefault(WorldSettings settings = null, int seed = 12345)
        {
            var world = new SimulationWorld(settings ?? new WorldSettings());
            world.Seed = seed; // Speichern & Laden: CreateDefault(settings, seed) baut die Welt beim Laden neu auf
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

            // --- G6 Bodenwert & Ausbaustufen (nach Diensten: liest Abdeckung, Müll, Zufriedenheit; vor der Kasse: Grundsteuer)
            world.LandValue = new LandValue.LandValueSystem(world);
            world.AddSystem(world.LandValue);
            world.City.LotPreference = world.LandValue.GrowthPreference;
            // --- Ende G6

            // --- Welt (Etappe 5): täglich Erneuerung der Vorkommen und Länder-Volkswirtschaften
            world.AddSystem(new WorldSystem());
            // --- Ende Welt

            // --- G5 Warenketten (nach Diensten, vor der Stadtkasse: Tagesgewinne stehen fest, wenn die Kasse abrechnet)
            world.Goods = new GoodsSystem(world.City, world.Traffic, world.Roads, world.Clock, seed);
            world.AddSystem(world.Goods);
            GoodsBinding.Connect(world.Goods, world.City, world.Citizens, world.Utilities);
            GoodsBinding.ConnectWorld(world.Goods, world.Resources, world.Climate); // Vorkommen + Ernte aus der Welt
            // --- Ende G5

            // --- Stadtkasse (zuletzt: rechnet täglich mit fertigem Stand ab; Baukosten über die Hooks)
            world.Economy = new Economy.EconomySystem(world.Settings, world.Roads, world.City, world.Utilities);
            world.Roads.Budget = world.Economy;
            world.Terrain.Budget = world.Economy;
            world.Utilities.Budget = world.Economy;
            world.City.DemandModifier = world.Economy.DemandFactor;
            world.Services.Budget = world.Economy;          // G4: Baukosten Dienste
            world.Economy.RegisterUpkeep(world.Services);   // G4: Unterhalt Dienste
            world.Economy.PropertyTaxFactor = world.LandValue.PropertyTaxFactor; // G6: Grundsteuer nach Bodenwert
            world.AddSystem(world.Economy);
            // --- Ende Stadtkasse
            world.Economy.TaxableProfit = world.Goods.TaxableProfit; // G5: Gewerbesteuer aus dem Gewinn
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

        /// <summary>G6 Bodenwert, Ausbaustufen, Gebäudezustand. Gesetzt von <see cref="CreateDefault"/>.</summary>
        public LandValue.LandValueSystem LandValue { get; private set; }

        /// <summary>Warenketten (G5): Firmen, Märkte, Lieferverkehr, Import/Export. Gesetzt von <see cref="CreateDefault"/>.</summary>
        public GoodsSystem Goods { get; private set; }

        /// <summary>Stadtkasse (Kontostand, Steuern, Bilanz). Gesetzt von <see cref="CreateDefault"/>.</summary>
        public Economy.EconomySystem Economy { get; private set; }

        // --- Speichern & Laden
        /// <summary>Seed von <see cref="CreateDefault"/> (Systeme leiten ihre Zufallsgeneratoren davon ab).</summary>
        public int Seed { get; private set; } = 12345;
        /// <summary>Alle Systeme in Ausführungsreihenfolge (Spielstand: ISaveable-Systeme werden automatisch erfasst).</summary>
        public IReadOnlyList<ISimulationSystem> Systems => systems;
        // --- Ende Speichern & Laden

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
