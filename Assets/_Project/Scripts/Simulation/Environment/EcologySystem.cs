using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.World;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Umwelt (Etappe 6): Luft, Boden und Grundwasser als eigenes System (<c>world.Ecology</c>).
    /// <para><b>Luft</b> (Raster <c>world.Environment.AirPollution</c>, Einheit wie bisher): Quellen Verkehr (echte Fahrzeuge je
    /// Zelle, jeden Tick gezählt, Lkw 3,5×, Stau 1,6×), Kraftwerke (Kohle/Gas/Biogas nach Auslastung), Industrie nach Rezept
    /// (× Produktivität), Heizen (Wohnungen/Arbeitsplätze × Heizbedarf des Klimas × Bauart/Stufe) und Müllverbrennung. Ausbreitung
    /// mit dem <b>Wind</b> aus dem Klimamodell (Richtung/Stärke je Tag), Diffusion, Abbau durch Lüftung, Regen, Wald (Waldraster)
    /// und Parks; Inversionswetterlagen halten die Luft fest. Je Durchlauf (30 min) einmal.</para>
    /// <para><b>Boden &amp; Grundwasser</b> (eigene Raster, täglich): Ablagerung aus der Luft, Industrie nach Rezept, Müllhalde
    /// (Sickerwasser), Verbrennung, Abwasser-Auslass; Auswaschung Boden → Grundwasser, langsame Erholung.</para>
    /// <para><b>Wirkungen</b> über Hooks (<see cref="Connect"/>): Fruchtbarkeit/Ernte, Brunnenqualität, chronische Belastung
    /// der Bürger (Gesundheit), Bodenwert (Altlasten), Firmenkosten der Umweltauflagen, Schlagzeilen.</para>
    /// <para><b>Standard aus</b> (<see cref="Enabled"/>): dann rechnet es die Luft exakt wie früher das UtilitySystem/ServiceSystem
    /// (nur Kraftwerke und Verbrennung, gleiche Reihenfolge – bestehende Tests/alte Welten bitgleich). Das Spiel schaltet ein.
    /// Bäume pflanzen geht immer.</para>
    /// </summary>
    public sealed partial class EcologySystem : ISimulationSystem, IUpkeepSource
    {
        /// <summary>Läuft jeden Tick (Verkehr zählen); Luft alle <see cref="PassTicks"/>, Boden täglich.</summary>
        public int TickInterval => 1;
        public const int PassTicks = 5;

        readonly SimulationWorld world;
        readonly GridLayer air;
        readonly int width, height;
        readonly float[] trafficAccum, shipAccum;
        int trafficSamples;
        readonly float[] dayAirSum;
        int dayAirPasses;
        float[] vegetation, scratch;
        int vegetationResourceVersion = int.MinValue, vegetationServiceVersion = int.MinValue;

        /// <summary>Volles Umweltmodell an (Spiel) bzw. alte Luftrechnung (Tests/alte Welten).</summary>
        public bool Enabled { get; set; }

        /// <summary>Bodenbelastung 0..1 (Altlasten).</summary>
        public GridLayer Soil { get; }
        /// <summary>Grundwasserbelastung 0..1.</summary>
        public GridLayer Groundwater { get; }
        /// <summary>Ziel-Dichte gepflanzter Bäume je Zelle (0 = nicht aufgeforstet).</summary>
        public GridLayer Planted { get; }
        /// <summary>Chronische Luftbelastung (Tagesmittel geglättet, ≈ 10 Tage).</summary>
        public GridLayer Exposure { get; }

        /// <summary>Stadtkasse für Bäume (null = kostenlos).</summary>
        public IConstructionBudget Budget { get; set; }

        // --- Gegenmaßnahmen
        /// <summary>Umweltauflagen 0..3 (Filterpflicht für Kraftwerke, Industrie, Verbrennung).</summary>
        public int Regulation { get; private set; }
        /// <summary>Umweltzone (Lkw/alte Pkw) als Kreis.</summary>
        public bool LowEmissionZone { get; private set; }
        public Vector2 ZoneCenter { get; private set; }
        public float ZoneRadius { get; private set; } = 500f;

        // --- Statistik
        const int Sources = (int)EmissionSource.Shipping + 1;
        readonly float[] emission = new float[Sources], emissionToday = new float[Sources], emissionYesterday = new float[Sources];
        static float SumOf(float[] a) { float s = 0f; for (int i = 0; i < a.Length; i++) s += a[i]; return s; }
        /// <summary>Ausstoß je Quelle im letzten Durchlauf (Summe über alle Zellen).</summary>
        public float EmissionOf(EmissionSource s) => emission[(int)s];
        public float TotalEmission => SumOf(emission);
        /// <summary>Ausstoß je Quelle am Vortag (Summe aller Durchläufe).</summary>
        public float EmissionYesterday(EmissionSource s) => emissionYesterday[(int)s];
        public float TotalEmissionYesterday => SumOf(emissionYesterday);
        /// <summary>Anteil einer Quelle am Ausstoß des Vortags 0..1.</summary>
        public float ShareYesterday(EmissionSource s) => TotalEmissionYesterday > 0f ? emissionYesterday[(int)s] / TotalEmissionYesterday : 0f;
        /// <summary>Mittel der Luft an bewohnten Gebäuden (nach Einwohnern gewichtet) und höchster Wert dort.</summary>
        public float HomeAirAverage { get; private set; }
        public float HomeAirMax { get; private set; }
        public int SmogBuildings { get; private set; }
        public int HomeBuildings { get; private set; }
        public bool SmogAlert { get; private set; }
        public int SmogEpisodes { get; private set; }
        public int FishKills { get; private set; }
        public int PlantedCells { get; private set; }
        public float TreesPlanted { get; private set; }
        public int Version { get; private set; }

        /// <summary>Smog-Alarm beginnt (true) bzw. endet (false).</summary>
        public event Action<bool> SmogAlertChanged;

        public EcologySystem(SimulationWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            air = world.Environment.AirPollution;
            width = air.Width; height = air.Height;
            Soil = new GridLayer("Bodenbelastung", air.Origin, width, height, air.CellSize);
            Groundwater = new GridLayer("Grundwasserbelastung", air.Origin, width, height, air.CellSize);
            Planted = new GridLayer("Aufforstung", air.Origin, width, height, air.CellSize);
            Exposure = new GridLayer("Chronische Luftbelastung", air.Origin, width, height, air.CellSize);
            trafficAccum = new float[width * height];
            shipAccum = new float[width * height];
            dayAirSum = new float[width * height];
            ZoneCenter = Vector2.Zero;
        }

        // ------------------------------------------------------------ Abfragen

        public WindState Wind => world.Climate.WindOn(world.Clock.Day);
        public float Inversion => world.Climate.InversionOn(world.Clock.Day);
        public float AirAt(Vector2 p) => air.Sample(p);
        public float SoilAt(Vector2 p) => Soil.Sample(p);
        public float GroundwaterAt(Vector2 p) => Groundwater.Sample(p);
        public float ExposureAt(Vector2 p) => Exposure.Sample(p);

        /// <summary>Fruchtbarkeit × (1 − Boden) × (1 − Luft): Wirkung der Umwelt auf den Ertrag des Bodens an p.</summary>
        public float FertilityFactorAt(Vector2 p) =>
            Math.Clamp(1f - EcologyRules.SoilFertilityLoss * Soil.Sample(p), 0f, 1f)
            * Math.Clamp(1f - EcologyRules.AirCropLoss * Math.Clamp(air.Sample(p), 0f, 1f), 0f, 1f);

        /// <summary>Ernte-Faktor aus Hitze und Dürre des Tages (Klima), 1 = kein Stress.</summary>
        public float HarvestStress(int day)
        {
            var c = world.Climate;
            return (1f - EcologyRules.HeatHarvestLoss * c.HeatStressOn(day)) * (1f - EcologyRules.DroughtHarvestLoss * c.DroughtOn(day));
        }

        /// <summary>Brunnen: 1 − Grundwasserbelastung (schlechteste Zelle im 3×3 um den Standort).</summary>
        public float WellQualityAt(Vector2 p)
        {
            float worst = 0f;
            float d = Groundwater.CellSize;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                worst = MathF.Max(worst, Groundwater.Sample(p + new Vector2(x * d, y * d)));
            return Math.Clamp(1f - EcologyRules.WellPollutionFactor * worst, 0f, 1f);
        }

        /// <summary>Tourismus (vorgemerkt): Anziehung 0,2..1,1 aus Luft an den Wohnungen, Wasserqualität und Wald.</summary>
        public float TourismFactor
        {
            get
            {
                float water = world.WaterQuality != null ? Math.Clamp(world.WaterQuality.Pollution.MaxValue(), 0f, 1f) : 0f;
                return Math.Clamp(1f - 1.2f * HomeAirAverage - 0.3f * water + 0.1f * Math.Min(1f, PlantedCells / 200f), 0.2f, 1.1f);
            }
        }

        // ------------------------------------------------------------ Gegenmaßnahmen

        /// <summary>Umweltauflagen setzen (0..3). Wirkt sofort auf Emission und Kosten.</summary>
        public void SetRegulation(int level)
        {
            level = Math.Clamp(level, 0, EcologyRules.MaxRegulation);
            if (level == Regulation) return;
            bool stricter = level > Regulation;
            Regulation = level;
            Version++;
            if (stricter && Enabled)
                world.Media?.Report(Services.Content.HeadlineTopic.EnvironmentRules, "rules" + level + "-" + world.Clock.Day,
                    ("Name", EcologyRules.RegulationName(level)));
        }

        public void SetLowEmissionZone(bool on, Vector2 center, float radius)
        {
            LowEmissionZone = on;
            ZoneCenter = center;
            ZoneRadius = Math.Clamp(radius, EcologyRules.ZoneMinRadius, EcologyRules.ZoneMaxRadius);
            Version++;
        }

        public bool InZone(Vector2 p) => LowEmissionZone && Vector2.DistanceSquared(p, ZoneCenter) <= ZoneRadius * ZoneRadius;

        /// <summary>Mittelpunkt der Bebauung (Vorschlag für die Umweltzone).</summary>
        public Vector2 CityCenter()
        {
            double x = 0, y = 0, n = 0;
            foreach (var b in world.City.Buildings)
            {
                double w = b.Households + b.Jobs + 1;
                x += b.Position.X * w; y += b.Position.Y * w; n += w;
            }
            return n > 0 ? new Vector2((float)(x / n), (float)(y / n)) : Vector2.Zero;
        }

        // ------------------------------------------------------------ Kosten (IUpkeepSource)

        public string Name => "Umweltschutz";

        /// <summary>Filter an städtischen Kraftwerken/Verbrennung (Auflagen), Umweltzone, Pflege der Aufforstung.</summary>
        public double WeeklyUpkeep()
        {
            double sum = PlantedCells * EcologyRules.CareUpkeepPerCellWeek;
            if (!Enabled) return sum;
            double share = EcologyRules.PlantUpkeepShare(Regulation);
            if (share > 0)
            {
                if (world.Utilities != null)
                    foreach (var f in world.Utilities.Facilities)
                        if (f.Type.Pollution > 0f) sum += f.Type.UpkeepPerDay * 7.0 * share;
                if (world.Services != null)
                    foreach (var f in world.Services.Facilities)
                        if (f.Type.Pollution > 0f) sum += f.Type.UpkeepPerWeek * share;
            }
            if (LowEmissionZone) sum += EcologyRules.ZoneUpkeepPerWeek;
            return sum;
        }

        /// <summary>Mehrkosten einer Firma je Tag (Auflagen nach Rezept + Umweltzone) – vom Warensystem täglich abgebucht.</summary>
        public double FirmCost(Goods.Firm f)
        {
            if (!Enabled || f == null || f.Building == null) return 0;
            double cost = 0;
            if (Regulation > 0 && f.Recipe != null)
                cost += f.Jobs * EcologyRules.FirmCostPerJobDay(Regulation) * EcologyRules.FirmWeight(f.Recipe.Id);
            if (LowEmissionZone && InZone(f.Building.Position) && (f.Role != Goods.FirmRole.Office))
                cost += f.Jobs * EcologyRules.ZoneCostPerJobDay;
            return cost;
        }

        /// <summary>Summe der Firmen-Mehrkosten je Tag (Anzeige).</summary>
        public double FirmCostPerDay()
        {
            if (world.Goods == null) return 0;
            double sum = 0;
            foreach (var f in world.Goods.Firms) sum += FirmCost(f);
            return sum;
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld w)
        {
            long tick = world.Clock.Tick;
            bool pass = tick % PassTicks == 0;
            if (!Enabled) { if (pass) LegacyAir(); }
            else
            {
                SampleTraffic();
                if (pass) AirPass();
            }
            if (tick % SimulationClock.TicksPerDay == 0) Daily();
        }

        /// <summary>
        /// Alte Luftrechnung (vor Etappe 6), unverändert aus UtilitySystem.Emit und ServiceSystem.Emit übernommen: Kraftwerke
        /// addieren, dann Ausbreitung/Abbau, falls Kraftwerke Abgas machen; danach die Verbrennungsanlagen (abklingen nur, wenn kein
        /// Kraftwerk Abgas macht). Die beiden „aktiv“-Merker liegen weiter in den Systemen (deren Spielstand).
        /// </summary>
        void LegacyAir()
        {
            var u = world.Utilities;
            bool utilityPolluting = false;
            if (u != null)
            {
                foreach (var f in u.Facilities)
                    if (f.Type.Pollution > 0f)
                    {
                        air.AddRadial(f.Position, f.Type.PollutionRadius, f.Type.Pollution);
                        utilityPolluting = true;
                    }
                if (utilityPolluting || u.pollutionActive)
                {
                    air.Diffuse(0.1f);
                    air.Decay(0.08f);
                    u.pollutionActive = utilityPolluting || air.MaxValue() > 0.001f;
                }
            }
            var s = world.Services;
            if (s != null)
            {
                bool polluting = false;
                foreach (var f in s.Facilities)
                    if (f.Type.Pollution > 0f) { air.AddRadial(f.Position, f.Type.PollutionRadius, f.Type.Pollution); polluting = true; }
                if (!utilityPolluting && (polluting || s.pollutionActive))
                {
                    air.Diffuse(0.1f);
                    air.Decay(0.08f);
                    s.pollutionActive = polluting || air.MaxValue() > 0.001f;
                }
            }
        }

        // ------------------------------------------------------------ Luft

        int CellIndex(Vector2 p)
        {
            int x = (int)MathF.Floor((p.X - air.Origin.X) / air.CellSize);
            int y = (int)MathF.Floor((p.Y - air.Origin.Y) / air.CellSize);
            return x >= 0 && y >= 0 && x < width && y < height ? y * width + x : -1;
        }

        /// <summary>Jeden Tick: echte Fahrzeuge je Zelle zählen (gewichtet nach Klasse, Stau, Umweltzone).</summary>
        void SampleTraffic()
        {
            var traffic = world.Traffic;
            if (traffic == null) return;
            trafficSamples++;
            var list = traffic.Vehicles;
            for (int k = 0; k < list.Count; k++)
            {
                var v = list[k];
                if (v.Lane == null && v.Connection == null) continue;
                float wgt = EcologyRules.VehicleWeight(v.Class);
                if (wgt <= 0f) continue;
                int i = CellIndex(v.Position);
                if (i < 0) continue;
                if (v.Speed < EcologyRules.SlowSpeed) wgt *= EcologyRules.SlowFactor;
                if (LowEmissionZone && InZone(v.Position))
                    wgt *= v.Class == Traffic.VehicleClass.Truck ? EcologyRules.ZoneTruckFactor : EcologyRules.ZoneCarFactor;
                trafficAccum[i] += wgt;
            }
            // Schiffe (Wasserstraße & Hafen): Schweröl-Diesel, nach Größe; am Liegeplatz laufen die Hilfsdiesel (kein Landstrom)
            var shipping = world.Shipping;
            if (shipping == null) return;
            var ships = shipping.Ships;
            for (int k = 0; k < ships.Count; k++)
            {
                var s = ships[k];
                if (!s.IsOnMap || s.Class == null) continue;
                shipping.Sample(s, s.S, out var pos, out _);
                int i = CellIndex(pos);
                if (i < 0) continue;
                float wgt = EcologyRules.ShipWeight(s.Class.Role, s.Class.Length);
                if (s.Speed < 0.3f) wgt *= EcologyRules.BerthedShipFactor;
                shipAccum[i] += wgt;
            }
        }

        void AirPass()
        {
            int day = world.Clock.Day;
            var climate = world.Climate;
            var wind = climate.WindOn(day);
            float inversion = climate.InversionOn(day);
            float rain = climate.PrecipitationOn(day);
            Array.Clear(emission, 0, emission.Length);
            var cells = air.RawValues;

            EmitTraffic(cells);
            EmitPower();
            EmitWaste();
            EmitIndustry();
            EmitHeating(cells, climate.HeatingFactor(day));

            for (int s = 0; s < emission.Length; s++) emissionToday[s] += emission[s];

            Advect(cells, wind, inversion);
            air.Diffuse(EcologyRules.Diffusion * (1f - (1f - EcologyRules.InversionDiffusionFactor) * inversion));
            RefreshVegetation();
            float ventilation = (EcologyRules.BaseDecay + EcologyRules.WindDecay * wind.Strength
                                 + EcologyRules.RainDecay * MathF.Min(1f, rain / EcologyRules.RainFullMillimeters))
                                * (1f - (1f - EcologyRules.InversionDecayFactor) * inversion);
            for (int i = 0; i < cells.Length; i++)
            {
                float c = cells[i];
                if (c <= 0f) { cells[i] = 0f; continue; }
                float k = Math.Clamp(ventilation + vegetation[i], 0f, 0.9f);
                c *= 1f - k;
                cells[i] = c < 1e-6f ? 0f : c;
                dayAirSum[i] += cells[i];
            }
            dayAirPasses++;
            air.MarkChanged();
            UpdateHomeStats();
            Version++;
        }

        void EmitTraffic(float[] cells)
        {
            if (trafficSamples <= 0) return;
            float k = EcologyRules.TrafficPerVehicle / trafficSamples;
            float sum = 0f;
            for (int i = 0; i < trafficAccum.Length; i++)
            {
                float a = trafficAccum[i];
                if (a <= 0f) continue;
                float e = a * k;
                cells[i] += e;
                sum += e;
                trafficAccum[i] = 0f;
            }
            emission[(int)EmissionSource.Traffic] = sum;
            float ships = 0f;
            for (int i = 0; i < shipAccum.Length; i++)
            {
                float a = shipAccum[i];
                if (a <= 0f) continue;
                float e = a * k;
                cells[i] += e;
                ships += e;
                shipAccum[i] = 0f;
            }
            emission[(int)EmissionSource.Shipping] = ships;
            trafficSamples = 0;
        }

        readonly List<UtilityFacility> plantBuffer = new List<UtilityFacility>();

        void EmitPower()
        {
            var u = world.Utilities;
            if (u == null) return;
            plantBuffer.Clear();
            foreach (var f in u.Facilities) if (f.Type.Pollution > 0f) plantBuffer.Add(f);
            plantBuffer.Sort((a, b) => a.Id.CompareTo(b.Id));
            float filter = EcologyRules.FilterFactor(Regulation);
            float sum = 0f;
            foreach (var f in plantBuffer)
            {
                float e = f.Type.Pollution * PlantLoad(f) * filter;
                if (e <= 0f) continue;
                sum += AddRadial(air, f.Position, f.Type.PollutionRadius, e);
            }
            emission[(int)EmissionSource.Power] = sum;
        }

        /// <summary>Auslastung eines Kraftwerks 0,15..1: gelieferte Leistung / Erzeugung seines Netzes × eigene Verfügbarkeit.</summary>
        public float PlantLoad(UtilityFacility f)
        {
            if (f.Rated > 0f && f.Output <= 0f) return 0f; // abgeschaltet (Störfall, kein Rohstoff)
            float avail = f.Rated > 0f ? Math.Clamp(f.Output / f.Rated, 0f, 1f) : 1f;
            var net = world.Utilities.NetworkOf(f);
            float use = net != null && net.Production > 0f ? Math.Clamp(net.Delivered / net.Production, 0f, 1f) : 0f;
            return avail * MathF.Max(EcologyRules.StandbyLoad, use);
        }

        readonly List<ServiceFacility> serviceBuffer = new List<ServiceFacility>();

        void EmitWaste()
        {
            var s = world.Services;
            if (s == null) return;
            serviceBuffer.Clear();
            foreach (var f in s.Facilities) if (f.Type.Pollution > 0f) serviceBuffer.Add(f);
            serviceBuffer.Sort((a, b) => a.Id.CompareTo(b.Id));
            float filter = EcologyRules.FilterFactor(Regulation);
            float sum = 0f;
            foreach (var f in serviceBuffer)
            {
                float use = f.EffectiveCapacity > 0f ? Math.Clamp(f.Load / f.EffectiveCapacity, 0f, 1f) : 0f;
                float e = f.Type.Pollution * (0.3f + 0.7f * use) * filter;
                sum += AddRadial(air, f.Position, f.Type.PollutionRadius, e);
            }
            emission[(int)EmissionSource.Waste] = sum;
        }

        /// <summary>Wirksamer Betrieb einer Firma 0..1 (Produktivität; fehlende Eingänge/volles Lager drosseln).</summary>
        static float Activity(Goods.Firm f)
        {
            float p = Math.Clamp(f.Productivity, 0f, 1.5f);
            if (f.Status == Goods.FirmStatus.MissingInput || f.Status == Goods.FirmStatus.OutputFull) p *= 0.3f;
            return p;
        }

        /// <summary>Ausstoß einer Firma je Durchlauf (für Inspektor und Emission).</summary>
        public float FirmEmission(Goods.Firm f)
        {
            if (f?.Recipe == null || f.Building == null || !f.Building.IsConstructed) return 0f;
            float perJob = EcologyRules.AirPerJob(f.Recipe.Id);
            if (perJob <= 0f) return 0f;
            return perJob * f.Jobs * Activity(f) * EcologyRules.FilterFactor(Regulation);
        }

        void EmitIndustry()
        {
            var goods = world.Goods;
            if (goods == null) return;
            float sum = 0f;
            var firms = goods.Firms;
            for (int k = 0; k < firms.Count; k++)
            {
                var f = firms[k];
                float e = FirmEmission(f);
                if (e <= 0f) continue;
                var b = f.Building;
                sum += AddRadial(air, b.Position, EcologyRules.IndustryRadius + MathF.Max(b.Width, b.Depth) * 0.5f, e);
            }
            emission[(int)EmissionSource.Industry] = sum;
        }

        void EmitHeating(float[] cells, float heating)
        {
            if (heating <= 0f) return;
            float sum = 0f;
            foreach (var b in world.City.Buildings)
            {
                if (!b.IsConstructed || b.OutOfService) continue;
                float e = (b.OccupiedHouseholds * EcologyRules.HeatingPerHousehold * EcologyRules.HeatingBuildingFactor(b)
                           + b.Workers * EcologyRules.HeatingPerJob) * heating;
                if (e <= 0f) continue;
                int i = CellIndex(b.Position);
                if (i < 0) continue;
                cells[i] += e;
                sum += e;
            }
            emission[(int)EmissionSource.Heating] = sum;
        }

        /// <summary>Wie <see cref="GridLayer.AddRadial"/> (Wert am Mittelpunkt, linearer Abfall), liefert die insgesamt addierte Menge.</summary>
        internal static float AddRadial(GridLayer layer, Vector2 center, float radius, float amount)
        {
            if (radius <= 0f || amount == 0f) return 0f;
            var cells = layer.RawValues;
            var origin = layer.Origin;
            float size = layer.CellSize;
            int minX = Math.Max(0, (int)MathF.Floor((center.X - radius - origin.X) / size));
            int maxX = Math.Min(layer.Width - 1, (int)MathF.Floor((center.X + radius - origin.X) / size));
            int minY = Math.Max(0, (int)MathF.Floor((center.Y - radius - origin.Y) / size));
            int maxY = Math.Min(layer.Height - 1, (int)MathF.Floor((center.Y + radius - origin.Y) / size));
            float sum = 0f;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float dist = Vector2.Distance(layer.CellCenter(x, y), center);
                if (dist > radius) continue;
                float v = amount * (1f - dist / radius);
                cells[y * layer.Width + x] += v;
                sum += v;
            }
            layer.MarkChanged();
            return sum;
        }

        /// <summary>Verschiebung mit dem Wind (Upwind-Verfahren, Bruchteile je Achse); was über den Rand zieht, verlässt die Karte.</summary>
        void Advect(float[] cells, WindState wind, float inversion)
        {
            float s = EcologyRules.MaxAdvection * wind.Strength * (1f - (1f - EcologyRules.InversionAdvectionFactor) * inversion);
            float ux = wind.Toward.X * s, uy = wind.Toward.Y * s;
            float ax = MathF.Abs(ux), ay = MathF.Abs(uy);
            if (ax + ay < 1e-4f) return;
            int dx = ux >= 0f ? 1 : -1, dy = uy >= 0f ? 1 : -1;
            if (scratch == null) scratch = new float[cells.Length];
            Array.Copy(cells, scratch, cells.Length);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float c = scratch[i];
                if (c <= 0f) continue;
                float mx = c * ax, my = c * ay;
                cells[i] -= mx + my;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < width) cells[i + dx] += mx;
                if (ny >= 0 && ny < height) cells[i + dy * width] += my;
            }
        }

        /// <summary>Zusätzlicher Abbau durch Wald (Waldraster) und Parkflächen – neu, wenn sich Wald oder Dienste ändern.</summary>
        void RefreshVegetation()
        {
            var res = world.Resources;
            int rv = res?.Version ?? 0;
            int sv = world.Services?.StructureVersion ?? 0;
            if (vegetation != null && rv == vegetationResourceVersion && sv == vegetationServiceVersion) return;
            vegetationResourceVersion = rv;
            vegetationServiceVersion = sv;
            if (vegetation == null) vegetation = new float[width * height];
            var wood = res?.Layer(ResourceIds.Wood);
            bool sameGrid = wood != null && wood.Width == width && wood.Height == height;
            for (int i = 0; i < vegetation.Length; i++)
                vegetation[i] = sameGrid ? EcologyRules.ForestDecay * Math.Clamp(wood.RawValues[i], 0f, 1f) : 0f;
            if (world.Services == null) return;
            foreach (var f in world.Services.Facilities)
            {
                if (f.Kind != ServiceKinds.Park) continue;
                float r = MathF.Max(f.Type.Width, f.Type.Depth) * 0.5f + 16f;
                int x0 = Math.Max(0, (int)MathF.Floor((f.Position.X - r - air.Origin.X) / air.CellSize));
                int x1 = Math.Min(width - 1, (int)MathF.Floor((f.Position.X + r - air.Origin.X) / air.CellSize));
                int y0 = Math.Max(0, (int)MathF.Floor((f.Position.Y - r - air.Origin.Y) / air.CellSize));
                int y1 = Math.Min(height - 1, (int)MathF.Floor((f.Position.Y + r - air.Origin.Y) / air.CellSize));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var c = air.CellCenter(x, y);
                    if (!f.Contains(c, 16f)) continue;
                    vegetation[y * width + x] += EcologyRules.ParkDecay;
                }
            }
        }

        void UpdateHomeStats()
        {
            double sum = 0, weight = 0;
            float max = 0f;
            int smog = 0, homes = 0;
            foreach (var b in world.City.Buildings)
            {
                if (b.Residents <= 0) continue;
                float a = air.Sample(b.Position);
                homes++;
                sum += a * b.Residents;
                weight += b.Residents;
                if (a > max) max = a;
                if (a >= EcologyRules.SmogThreshold) smog++;
            }
            HomeBuildings = homes;
            SmogBuildings = smog;
            HomeAirMax = max;
            HomeAirAverage = weight > 0 ? (float)(sum / weight) : 0f;
            bool alert = SmogAlert
                ? smog >= Math.Max(1, EcologyRules.SmogMinBuildings / 2)
                : smog >= EcologyRules.SmogMinBuildings && smog >= EcologyRules.SmogShare * homes;
            if (alert == SmogAlert) return;
            SmogAlert = alert;
            if (alert)
            {
                SmogEpisodes++;
                int share = homes > 0 ? (int)MathF.Round(100f * smog / homes) : 0;
                world.Media?.Report(Services.Content.HeadlineTopic.Smog, "smog" + world.Clock.Day,
                    ("Anzahl", smog.ToString()), ("Prozent", share.ToString()));
            }
            SmogAlertChanged?.Invoke(alert);
        }
    }
}
