using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.World;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Aviation
{
    /// <summary>
    /// Wirkungen des Flugverkehrs: Nachfrage (Einwohner, Geschäftsreisen nach Büro-Arbeitsplätzen, Touristen nach der
    /// Tourismus-Stärke des eigenen Landes und der Attraktivität der Stadt, Umland), Ziele weltweit (Hauptstädte mit
    /// Großkreis-Entfernung und Flugdauer), Besucher (bleiben 2–4 Tage, kaufen in Läden ein, zahlen Eintritt und Kurtaxe),
    /// Nachfrage-Zuschläge (Läden/Büros), seltener Zuzug per Flugzeug, Luftfracht für hochwertige Güter, Luft-Gateway.
    /// </summary>
    public sealed partial class AviationSystem
    {
        // ------------------------------------------------------------ Ziele

        readonly List<Destination> destinations = new List<Destination>();
        public IReadOnlyList<Destination> Destinations => destinations;

        void BuildDestinations()
        {
            destinations.Clear();
            var planet = world.Planet;
            var start = world.StartRegion;
            if (planet != null && start != null)
            {
                foreach (var c in planet.Countries)
                {
                    if (c.IsPlayer || c.CapitalCell < 0) continue;
                    float lat = planet.LatitudeOf(c.CapitalCell), lon = planet.Longitude(planet.XOf(c.CapitalCell));
                    float km = AviationRules.GreatCircleKm(start.Latitude, start.Longitude, lat, lon);
                    double pop = c.Economy?.Population ?? 1e6;
                    destinations.Add(new Destination
                    {
                        City = c.CapitalName ?? c.Name, Country = c.Name, CountryId = c.Id, DistanceKm = km, Hours = AviationRules.FlightHours(km),
                        Weight = (float)(Math.Sqrt(Math.Max(1.0, pop)) / (1.0 + km / 2500.0)), Tourism = c.Profile[Strength.Tourism],
                    });
                }
                destinations.Sort((a, b) => b.Weight != a.Weight ? b.Weight.CompareTo(a.Weight) : a.CountryId.CompareTo(b.CountryId));
                if (destinations.Count > 14) destinations.RemoveRange(14, destinations.Count - 14);
            }
            if (destinations.Count == 0)
            {
                // ohne Weltkarte: fiktive Ziele
                (string, float)[] list = { ("Hauptstadt", 420f), ("Küstenstadt", 680f), ("Bergland", 950f), ("Übersee", 5200f) };
                for (int i = 0; i < list.Length; i++)
                    destinations.Add(new Destination { City = list[i].Item1, DistanceKm = list[i].Item2, Hours = AviationRules.FlightHours(list[i].Item2), Weight = 4f - i, Tourism = 0.5f });
            }
        }

        int PickDestination(uint h)
        {
            float total = 0f;
            foreach (var d in destinations) total += d.Weight;
            float u = (h % 10000u) / 10000f * total, acc = 0f;
            for (int i = 0; i < destinations.Count; i++) { acc += destinations[i].Weight; if (u < acc) return i; }
            return Math.Max(0, destinations.Count - 1);
        }

        public Destination DestinationOf(Rotation r) => r != null && r.Destination >= 0 && r.Destination < destinations.Count ? destinations[r.Destination] : null;

        // ------------------------------------------------------------ Nachfrage (täglich, gespeichert)

        float demandResidents, demandBusiness, demandTourists;
        int officeJobs;

        /// <summary>Tourismus-Stärke des eigenen Landes (Profil, 0..1; ohne Welt 0,4).</summary>
        public float TourismStrength
        {
            get
            {
                var c = world.Planet?.PlayerCountry;
                return c != null ? c.Profile.Raw[(int)Strength.Tourism] : 0.4f;
            }
        }

        public float DemandResidents => demandResidents;
        public float DemandBusiness => demandBusiness;
        public float DemandTourists => demandTourists;

        void UpdateDemand()
        {
            var city = world.City;
            int pop = city?.Stats.Population ?? 0;
            int jobs = city?.Stats.Jobs ?? 0;
            int office = 0;
            if (city != null) foreach (var b in city.Buildings) if (b.Zone == ZoneType.Office) office += b.Jobs;
            officeJobs = office;
            demandResidents = pop * AviationRules.ResidentTripsPerDay;
            demandBusiness = office * AviationRules.BusinessTripsPerOfficeJob + Math.Max(0, jobs - office) * AviationRules.BusinessTripsPerJob;
            // Handel 2: mit Weltkarte kommt die Touristen-Nachfrage aus dem Tourismus-Handel der Länder (Anteil per Flugzeug)
            demandTourists = TouristDemandSource != null ? MathF.Max(0f, TouristDemandSource()) : BaseTouristPotential();
        }

        // --- Handel 2: Tourismus als Dienstleistungshandel (TradeSystem.Tourism)
        /// <summary>Touristen je Tag per Flugzeug aus dem Handel (null = eigene Formel <see cref="BaseTouristPotential"/>).</summary>
        public Func<float> TouristDemandSource { get; set; }

        /// <summary>Besucher-Potenzial der Stadt je Tag: Tourismus-Stärke × (1 + Attraktivität/60) × Stadtgröße (Grundstufe).</summary>
        public float BaseTouristPotential()
        {
            int pop = world.City?.Stats.Population ?? 0;
            float attract = world.Services?.Attractiveness ?? 0f;
            float size = MathF.Min(1f, MathF.Sqrt(pop / 5000f));
            return AviationRules.TouristBase * TourismStrength * (1f + attract / AviationRules.AttractivenessDoubling) * (0.4f + 0.6f * size);
        }

        /// <summary>Besucher, die per Straße/Bahn/Schiff ankommen (aus dem Tourismus-Handel) – gleiche Besucherliste wie Flugtouristen.</summary>
        public void AddGroundVisitors(int count, int salt)
        {
            if (count <= 0) return;
            AddVisitors(count, unchecked(salt * 7919 + 13));
            Version++;
        }
        // --- Ende Handel 2

        /// <summary>Fluggäste je Richtung und Tag für diesen Flughafen (Anteil an der Stadt-Nachfrage + Umland).</summary>
        public float DailyDemand(Airport a)
        {
            float share = ShareOf(a);
            return (demandResidents + demandBusiness + demandTourists) * share + a.Type.Catchment;
        }

        float ShareOf(Airport a)
        {
            float total = 0f, own = 0f;
            foreach (var x in airports)
            {
                if (!x.InService) continue;
                float w = x.Type.Class == AirportClass.Airport ? 0.8f : 0.2f;
                total += w;
                if (x == a) own = w;
            }
            return total > 0f ? own / total : 0f;
        }

        void Composition(Airport a, out float tourist, out float business)
        {
            float share = ShareOf(a);
            float t = demandTourists * share, b = demandBusiness * share, all = (demandResidents + demandBusiness + demandTourists) * share + a.Type.Catchment;
            tourist = all > 0f ? t / all : 0f;
            business = all > 0f ? b / all : 0f;
        }

        // ------------------------------------------------------------ Besucher (Tourismus-Grundstufe)

        /// <summary>Besucher in der Stadt je verbleibenden Tagen (Index = Tage bis zur Abreise).</summary>
        readonly float[] visitors = new float[AviationRules.MaxStay + 1];

        public float VisitorsPresent
        {
            get { float s = 0f; foreach (float v in visitors) s += v; return s; }
        }

        void AddVisitors(int n, int id)
        {
            if (n <= 0) return;
            int stay = AviationRules.MinStay + (int)(SimRandom.Hash(id, 4242) % (uint)(AviationRules.MaxStay - AviationRules.MinStay + 1));
            visitors[Math.Min(stay, visitors.Length - 1)] += n;
        }

        /// <summary>Täglich: Ausgaben der Besucher (Läden, Freizeit/Eintritt, Kurtaxe), Abreise, Nachfrage neu.</summary>
        void VisitorsDaily()
        {
            float present = VisitorsPresent;
            VisitorShopSpendYesterday = 0;
            VisitorLeisureSpendYesterday = 0;
            if (present > 0.5f)
            {
                double shop = present * AviationRules.VisitorShopSpendPerDay;
                double spent = world.Goods?.VisitorPurchase(shop) ?? 0.0;
                VisitorShopSpendYesterday = spent;
                float attract = world.Services?.Attractiveness ?? 0f;
                double leisure = present * AviationRules.VisitorLeisureSpendPerDay * Math.Min(1.0, attract / 20.0);
                VisitorLeisureSpendYesterday = leisure;
                pendingTourism += leisure + present * AviationRules.CityTaxPerNight;
            }
            for (int i = 0; i < visitors.Length - 1; i++) visitors[i] = visitors[i + 1];
            visitors[visitors.Length - 1] = 0f;
            UpdateDemand();
        }

        /// <summary>Nachfrage-Zuschlag: Besucher kaufen in Läden, Geschäftsreisende brauchen Büros (ohne Flughafen exakt 1).</summary>
        float DemandBoost(ZoneType z)
        {
            if (airports.Count == 0) return 1f;
            int pop = world.City?.Stats.Population ?? 0;
            if (z == ZoneType.Retail || z == ZoneType.Mixed)
                return 1f + MathF.Min(AviationRules.MaxRetailBoost, VisitorsPresent / MathF.Max(300f, pop) * 0.8f);
            if (z == ZoneType.Office)
            {
                int business = 0;
                foreach (var a in airports) business += a.BusinessYesterday;
                return 1f + MathF.Min(AviationRules.MaxOfficeBoost, business / MathF.Max(100f, officeJobs) * 2f);
            }
            return 1f;
        }

        public float RetailBoost => DemandBoost(ZoneType.Retail);
        public float OfficeBoost => DemandBoost(ZoneType.Office);

        // ------------------------------------------------------------ Zuzug per Flugzeug (selten)

        int pendingAirMoveIns;
        public int PendingAirMoveIns => pendingAirMoveIns;

        void ConnectMoveIn()
        {
            var c = world.Citizens;
            if (c?.MoveInGateway == null) return;
            var previous = c.MoveInGateway;
            c.MoveInGateway = (home, comp) => AirMoveIn(comp) ?? previous(home, comp);
        }

        RoadNode AirMoveIn(int component)
        {
            if (pendingAirMoveIns <= 0 || component < 0) return null;
            var comps = world.Citizens?.Components;
            if (comps == null) return null;
            foreach (var a in airports)
            {
                if (!a.InService || a.AccessNodeId == 0 || comps.OfNode(a.AccessNodeId) != component) continue;
                var node = roads.GetNode(a.AccessNodeId);
                if (node == null) continue;
                pendingAirMoveIns--;
                a.MoveInsTotal++;
                return node;
            }
            return null;
        }

        // ------------------------------------------------------------ Luftfracht (G5/Handel)

        int lastShipmentId;

        void ConnectGoods()
        {
            var goods = world.Goods;
            if (goods == null) return;
            var previous = goods.PreferredGateway;
            if (previous == null) return; // ohne Gateways wie bisher
            goods.PreferredGateway = (g, import, contract, comp) => AirNode(g, comp) ?? previous(g, import, contract, comp);
            var previousShare = goods.FreightShareFor;
            goods.FreightShareFor = node =>
            {
                if (AirportOfNode(node?.Id ?? 0) != null) return AviationRules.AirFreightShare;
                return previousShare?.Invoke(node) ?? -1.0;
            };
        }

        Airport AirportOfNode(int nodeId)
        {
            if (nodeId == 0) return null;
            foreach (var a in airports) if (a.InService && a.AccessNodeId == nodeId) return a;
            return null;
        }

        /// <summary>Hochwertige Güter (Elektronik) fliegen, wenn ein Verkehrsflughafen mit Frachtkapazität im Netzteil liegt.</summary>
        RoadNode AirNode(GoodType g, int component)
        {
            if (airports.Count == 0 || g == null || g.BasePrice < AviationRules.AirFreightMinValue || component < 0) return null;
            var comps = world.Citizens?.Components;
            if (comps == null) return null;
            EnsureAccess();
            CountFreight();
            foreach (var a in airports)
            {
                if (!a.InService || a.AccessNodeId == 0 || a.Type.CargoLoadsPerDay <= 0) continue;
                if (comps.OfNode(a.AccessNodeId) != component || a.LoadsToday >= a.Type.CargoLoadsPerDay) continue;
                return roads.GetNode(a.AccessNodeId);
            }
            return null;
        }

        void CountFreight()
        {
            var goods = world.Goods;
            if (goods == null || airports.Count == 0) return;
            var list = goods.Shipments;
            int i = list.Count - 1;
            while (i >= 0 && list[i].Id > lastShipmentId) i--;
            for (int k = i + 1; k < list.Count; k++)
            {
                var sh = list[k];
                lastShipmentId = Math.Max(lastShipmentId, sh.Id);
                if (sh.Kind != ShipmentKind.Import && sh.Kind != ShipmentKind.Export) continue;
                foreach (var a in airports)
                {
                    if (a.AccessNodeId == 0 || a.AccessNodeId != sh.ConnectionNodeId) continue;
                    a.LoadsToday++;
                    a.TotalLoads++;
                    a.PendingLoads++;
                    break;
                }
            }
        }

        // ------------------------------------------------------------ Luft-Gateway

        bool gatewayTouched;

        void ConnectGateways()
        {
            var gw = world.Gateways;
            if (gw == null) return;
            gw.AirGatewayOfNode = id =>
            {
                if (AirportOfNode(id) == null) return null;
                foreach (var g in gw.All) if (g.Kind == GatewayKind.Air) return g;
                return null;
            };
        }

        /// <summary>Luft-Gateway: in Betrieb mit Lage am Flughafen (Ziele weltweit), sonst „Flughafen fehlt“.</summary>
        void UpdateGatewayStatus()
        {
            var gw = world.Gateways;
            if (gw == null) return;
            if (airports.Count == 0 && !gatewayTouched) return;
            gatewayTouched = airports.Count > 0;
            Airport best = null;
            foreach (var a in airports) if (best == null || (a.InService && !best.InService) || (a.InService == best.InService && a.Type.Class > best.Type.Class)) best = a;
            foreach (var g in gw.All)
            {
                if (g.Kind != GatewayKind.Air) continue;
                if (best == null)
                {
                    g.Status = GatewayStatus.Planned; g.HasLocation = false; g.Name = "Luftweg (Flughafen folgt)"; g.TargetName = "weltweit";
                    g.AviationNote = null; g.Capacity = 0f;
                    continue;
                }
                g.HasLocation = true;
                g.Position = best.Layout.Terminal.FrontMid;
                g.Direction = best.Dir;
                g.Route = "Luftweg";
                g.Name = $"Luftweg · {best.Name}";
                g.TargetName = $"weltweit · {destinations.Count} Ziele";
                g.Capacity = MovementCapacity(best);
                g.Importance = MathF.Max(0.2f, destinations.Count / 4f);
                g.Status = best.InService ? GatewayStatus.Operational : GatewayStatus.Planned;
                g.AviationNote = best.InService ? null
                    : !best.HasAccess ? "noch nicht befahrbar (Straße vor dem Terminal fehlt)"
                    : "gesperrt (Hindernisse in beiden Einflugschneisen)";
            }
        }

        // ------------------------------------------------------------ Fluglärm-Beschwerden (täglich)

        void CountComplaints()
        {
            int complaints = 0;
            var cit = world.Citizens;
            if (cit != null && airports.Count > 0)
                foreach (var c in cit.Citizens)
                    if (c.Memories.Strength(MemoryKind.AircraftNoise) >= 0.2f) complaints++;
            ComplaintsYesterday = complaints;
        }

        /// <summary>Betroffene Einwohner (Fluglärm am Wohnort ≥ <see cref="AviationRules.AffectedNoise"/>), gezählt zur Abendspitze (18 Uhr).</summary>
        void CountAffected()
        {
            int affected = 0;
            var city = world.City;
            if (city != null && airports.Count > 0)
                foreach (var b in city.Buildings)
                    if (b.Residents > 0 && NoiseLayer.Sample(b.Position) >= AviationRules.AffectedNoise) affected += b.Residents;
            AffectedResidentsYesterday = affected;
        }
    }
}
