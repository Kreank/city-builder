using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.People
{
    /// <summary>Start oder Ziel eines Fußwegs: Tür (bzw. Parktor) und der zugehörige Gehweg-Punkt.</summary>
    public struct PedestrianEndpoint
    {
        /// <summary>Schlüssel für den Routen-Cache (Art + Id).</summary>
        public long Key;
        public Vector2 Door;
        public float DoorHeight;
        /// <summary>Blickrichtung aus der Tür heraus (zur Straße).</summary>
        public Vector2 Facing;
        public SidewalkAccess Access;
        /// <summary>Park: Punkt in der Fläche (Luftlinie vom Tor), sonst ungenutzt.</summary>
        public bool HasInner;
        public Vector2 Inner;
        public float InnerHeight;
        public bool IsValid;
    }

    /// <summary>
    /// Fußgänger-Planung (rein, ohne Unity): baut den <see cref="SidewalkGraph"/> bei Netzänderung neu, findet und
    /// speichert Gehwegrouten je Start/Ziel und berechnet den sichtbaren Zustand (<see cref="PedestrianPose"/>) eines
    /// Bürgers aus den lesenden Abfragen des Bürgersystems:
    /// <list type="bullet">
    /// <item>Platzhalter-Fahrt (<see cref="Activity.Travelling"/>, kein Auto): entlang der Route zwischen
    /// <see cref="Citizen.TripStartTick"/> und <see cref="Citizen.PlannedArrivalTick"/>; die Geschwindigkeit folgt aus
    /// Routenlänge / Fahrzeit (die Simulation rechnet 15 km/h Luftlinie) – Ankunft und Ankunftszeit passen so immer.</item>
    /// <item>Park (<see cref="Citizen.LeisureParkId"/>): Hinweg (wenn der Beginn bekannt ist), Aufenthalt auf der Parkfläche,
    /// Rückweg in den letzten Ticks vor <see cref="Citizen.BusyUntilTick"/> (Fußweg-Ticks wie im Bürgersystem).</item>
    /// <item>Spaziergang im Viertel: hin und her auf dem eigenen Gehweg. Einkauf: die letzten Ticks vor dem Laden.
    /// Ankunft zu Hause: kurz vor der Haustür.</item>
    /// </list>
    /// ÖPNV-Erweiterungsstelle: <see cref="Transit"/> (s. <see cref="TransitPose"/>).
    /// </summary>
    public sealed class PedestrianPlanner
    {
        /// <summary>Gehtempo, mit dem das Bürgersystem die Fußwege zum Park einrechnet (= Platzhalter-Fahrt 15 km/h je 6 min).</summary>
        public const float ParkWalkMetersPerTick = 1500f;
        /// <summary>So viele Ticks vor Ende des Einkaufs steht der Bürger vor dem Laden.</summary>
        public const int ShopLingerTicks = 3;
        /// <summary>So lange (Ticks) nach der Ankunft zu Hause steht der Bürger noch vor der Tür.</summary>
        public const int DoorTicks = 2;
        /// <summary>Spaziergang im Viertel: Länge des Gehwegstücks (m) und Dauer eines Hin-und-zurück (Ticks).</summary>
        public const float StrollLength = 40f;
        public const float StrollPeriodTicks = 8f;
        /// <summary>Höchstzahl gespeicherter Routen (danach wird der Cache geleert).</summary>
        public int MaxCachedRoutes = 30000;

        readonly RoadNetwork roads;
        readonly CitySystem city;
        readonly ServiceSystem services;
        SidewalkGraph graph;
        readonly Dictionary<(long, long), PedestrianRoute> cache = new Dictionary<(long, long), PedestrianRoute>();
        readonly PedestrianRouteBuilder builder = new PedestrianRouteBuilder();

        /// <summary>
        /// ÖPNV-Erweiterungsstelle: liefert für einen Bürger zur Zeit t eine Vorgabe (wartet an Haltestelle, im Bus,
        /// Fußweg zur Haltestelle). <see cref="TransitPoseKind.None"/> = normale Logik. Wird beim Zusammenführen mit dem
        /// ÖPNV verbunden; ohne Verbindung null.
        /// </summary>
        public Func<Citizen, double, TransitPose> Transit { get; set; }

        /// <summary>Neue Routenberechnungen, die noch erlaubt sind (Darstellung: je Frame begrenzen). Unbegrenzt in Tests.</summary>
        public int RouteBudget { get; set; } = int.MaxValue;
        /// <summary>Berechnete Routen seit dem Start (Statistik).</summary>
        public int RoutesComputed { get; private set; }
        public int CachedRoutes => cache.Count;

        public PedestrianPlanner(RoadNetwork roads, CitySystem city, ServiceSystem services)
        {
            this.roads = roads;
            this.city = city;
            this.services = services;
        }

        public PedestrianPlanner(Core.SimulationWorld world) : this(world.Roads, world.City, world.Services) { }

        /// <summary>Aktuelles Fußwegenetz (bei Netzänderung neu gebaut, Cache wird dann geleert).</summary>
        public SidewalkGraph Graph
        {
            get
            {
                if (graph == null || graph.Version != roads.Version)
                {
                    graph = new SidewalkGraph(roads);
                    cache.Clear();
                }
                return graph;
            }
        }

        // ------------------------------------------------------------ Start/Ziel

        const long KindBuilding = 1L << 56, KindPark = 2L << 56, KindOutside = 3L << 56, KindPoint = 4L << 56, KindStroll = 5L << 56;

        public bool TryEndpoint(Place p, out PedestrianEndpoint e)
        {
            e = default;
            if (p.IsBuilding) return TryBuildingEndpoint(city?.GetBuilding(p.BuildingId), out e);
            if (p.IsOutside)
            {
                var node = roads.GetNode(p.ConnectionNodeId);
                if (node == null || !Graph.TryAccessAtNode(node, out var a)) return false;
                e = new PedestrianEndpoint
                {
                    Key = KindOutside | (uint)node.Id, Door = a.Point, DoorHeight = a.Height, Facing = Vector2.UnitY, Access = a, IsValid = true,
                };
                return true;
            }
            return false;
        }

        public bool TryBuildingEndpoint(Building b, out PedestrianEndpoint e)
        {
            e = default;
            if (b == null || !Graph.TryAccessAtT(b.FrontSegmentId, b.FrontT, b.Entrance, out var a)) return false;
            e = new PedestrianEndpoint
            {
                Key = KindBuilding | (uint)b.Id,
                Door = b.Entrance - b.Front * 1f, // 1 m im Grundstück: Haustür
                DoorHeight = b.Height,
                Facing = b.Front,
                Access = a,
                IsValid = true,
            };
            return true;
        }

        /// <summary>Park als Ziel: Tor an der Straßenseite, dann Luftlinie zur Parkmitte.</summary>
        public bool TryParkEndpoint(ServiceFacility f, out PedestrianEndpoint e)
        {
            e = default;
            if (f == null) return false;
            var gate = f.Position + f.Front * (f.Type.Depth * 0.5f);
            bool ok = f.HasAccess ? Graph.TryAccessAtT(f.AccessSegmentId, f.AccessT, gate, out var a) : Graph.TryNearestAccess(gate, 60f, out a);
            if (!ok) return false;
            e = new PedestrianEndpoint
            {
                Key = KindPark | (uint)f.Id, Door = gate, DoorHeight = f.Height, Facing = f.Front, Access = a,
                HasInner = true, Inner = f.Position, InnerHeight = f.Height, IsValid = true,
            };
            return true;
        }

        /// <summary>Freier Punkt (z. B. Haltestelle): nächster Gehweg im Umkreis 80 m.</summary>
        public bool TryPointEndpoint(Vector2 p, float height, out PedestrianEndpoint e)
        {
            e = default;
            if (!Graph.TryNearestAccess(p, 80f, out var a)) return false;
            long qx = (long)MathF.Round(p.X * 4f) & 0xFFFFFF, qy = (long)MathF.Round(p.Y * 4f) & 0xFFFFFF;
            e = new PedestrianEndpoint { Key = KindPoint | (qx << 24) | qy, Door = p, DoorHeight = height, Facing = Vector2.UnitY, Access = a, IsValid = true };
            return true;
        }

        // ------------------------------------------------------------ Routen

        /// <summary>
        /// Fußweg von a nach b über die Gehwege (zwischengespeichert je Start/Ziel). null = nicht verbunden oder
        /// (bei erschöpftem <see cref="RouteBudget"/>) noch nicht berechnet.
        /// </summary>
        public PedestrianRoute Route(in PedestrianEndpoint a, in PedestrianEndpoint b)
        {
            var g = Graph;
            var key = (a.Key, b.Key);
            if (cache.TryGetValue(key, out var cached)) return cached;
            if (RouteBudget <= 0) return null;
            RouteBudget--;
            RoutesComputed++;

            builder.Clear();
            if (a.HasInner) builder.Add(a.Inner, a.InnerHeight, PedestrianRoute.SurfacePark);
            builder.Add(a.Door, a.DoorHeight, PedestrianRoute.SurfaceGround);
            PedestrianRoute route = null;
            if (g.AppendPath(a.Access, b.Access, builder))
            {
                builder.Add(b.Door, b.DoorHeight, PedestrianRoute.SurfaceGround);
                if (b.HasInner) builder.Add(b.Inner, b.InnerHeight, PedestrianRoute.SurfacePark);
                route = builder.Build();
            }
            if (cache.Count >= MaxCachedRoutes) cache.Clear();
            cache[key] = route;
            return route;
        }

        public PedestrianRoute Route(Place from, Place to) =>
            TryEndpoint(from, out var a) && TryEndpoint(to, out var b) ? Route(a, b) : null;

        // ------------------------------------------------------------ Zustand eines Bürgers

        /// <summary>
        /// Sichtbarer Zustand von <paramref name="c"/> zur Zeit <paramref name="time"/> (Ticks inkl. Bruchteil zwischen zwei
        /// Ticks). <paramref name="activitySince"/>: Tick, seit dem die aktuelle Tätigkeit läuft (von der Darstellung
        /// beobachtet), <see cref="long.MinValue"/> = unbekannt.
        /// </summary>
        public PedestrianPose Pose(Citizen c, double time, long activitySince = long.MinValue)
        {
            if (c == null) return PedestrianPose.Hidden;
            var transit = Transit != null ? Transit(c, time) : TransitPose.None;
            switch (transit.Kind)
            {
                case TransitPoseKind.Hidden: return PedestrianPose.Hidden;
                case TransitPoseKind.Waiting:
                    return Standing(transit.Position, transit.Height, transit.Facing, PedestrianSpot.Stop, PedestrianActivity.Waiting);
                case TransitPoseKind.Walking:
                {
                    if (!Endpoint(transit.WalkFrom, transit.WalkFromPoint, out var a) || !Endpoint(transit.WalkTo, transit.WalkToPoint, out var b))
                        return PedestrianPose.Hidden;
                    return Walk(Route(a, b), transit.WalkStart, transit.WalkEnd, time, PedestrianSpot.Trip);
                }
            }

            switch (c.Activity)
            {
                case Activity.Travelling:
                {
                    if (c.HasCarTrip || c.Mode == TravelMode.Car) return PedestrianPose.Hidden; // sichtbar als Auto
                    if (!TryEndpoint(c.Location, out var a) || !TryEndpoint(c.Destination, out var b)) return PedestrianPose.Hidden;
                    return Walk(Route(a, b), c.TripStartTick, c.PlannedArrivalTick, time, PedestrianSpot.Trip);
                }
                case Activity.Shopping:
                {
                    if (c.BusyUntilTick - time > ShopLingerTicks || !c.Location.IsBuilding) return PedestrianPose.Hidden;
                    var shop = city?.GetBuilding(c.Location.BuildingId);
                    if (shop == null || !TryBuildingEndpoint(shop, out var e)) return PedestrianPose.Hidden;
                    // Vor dem Schaufenster, über die Ladenfront verteilt
                    float spread = MathF.Max(0f, MathF.Min(shop.Width * 0.5f - 1f, 6f));
                    float u = Unit(c.Id, 11) * 2f - 1f;
                    var p = shop.Entrance + shop.Front * 0.7f + shop.Side * (u * spread);
                    return Standing(p, e.Access.Height, -shop.Front, PedestrianSpot.Shop, PedestrianActivity.Standing);
                }
                case Activity.Leisure:
                    return c.LeisureParkId > 0 ? ParkPose(c, time, activitySince) : StrollPose(c, time, activitySince);
                case Activity.Home:
                {
                    // Gerade angekommen (Bürgersystem: BusyUntil = Ankunft + 2) → kurz vor der Haustür
                    if (!c.Location.IsBuilding || c.BusyUntilTick <= time || c.BusyUntilTick - time > DoorTicks) return PedestrianPose.Hidden;
                    var home = city?.GetBuilding(c.Location.BuildingId);
                    if (home == null || !TryBuildingEndpoint(home, out var e)) return PedestrianPose.Hidden;
                    float u = Unit(c.Id, 5) * 2f - 1f;
                    var p = e.Door + home.Front * 0.8f + home.Side * (u * 0.8f);
                    return Standing(p, e.DoorHeight, home.Front, PedestrianSpot.Door, PedestrianActivity.Standing);
                }
                default:
                    return PedestrianPose.Hidden;
            }
        }

        bool Endpoint(Place place, Vector2 point, out PedestrianEndpoint e) =>
            place.IsNone ? TryPointEndpoint(point, 0f, out e) : TryEndpoint(place, out e);

        /// <summary>Gehen entlang der Route zwischen start und end (Ticks); vor dem Start an der Tür, danach am Ziel.</summary>
        public static PedestrianPose Walk(PedestrianRoute route, double start, double end, double time, PedestrianSpot spot)
        {
            if (route == null) return PedestrianPose.Hidden;
            double duration = end - start;
            float u = duration <= 1e-6 ? 1f : (float)Math.Clamp((time - start) / duration, 0.0, 1.0);
            float d = u * route.Length;
            route.Sample(d, out var p, out float h, out var dir);
            return new PedestrianPose
            {
                Kind = PedestrianActivity.Walking,
                Spot = spot,
                Position = p,
                Height = h,
                Direction = dir,
                MetersPerTick = duration > 1e-6 ? (float)(route.Length / duration) : 0f,
                Progress = u,
                OnCrossing = OnCrossing(route, d),
            };
        }

        static bool OnCrossing(PedestrianRoute r, float d)
        {
            // Abschnitt [i, i+1] liegt auf der Fahrbahn, wenn beide Enden Querungspunkte sind
            for (int i = 0; i < r.Count - 1; i++)
            {
                if (r.Distance(i + 1) < d) continue;
                return r.Surface(i) == PedestrianRoute.SurfaceCrossing && r.Surface(i + 1) == PedestrianRoute.SurfaceCrossing;
            }
            return false;
        }

        static PedestrianPose Standing(Vector2 p, float h, Vector2 facing, PedestrianSpot spot, PedestrianActivity kind) =>
            new PedestrianPose
            {
                Kind = kind, Spot = spot, Position = p, Height = h,
                Direction = facing.LengthSquared() > 1e-8f ? Vector2.Normalize(facing) : Vector2.UnitY,
            };

        /// <summary>Fußweg-Ticks je Richtung, wie sie das Bürgersystem beim Parkbesuch in die Dauer einrechnet.</summary>
        public static int ParkWalkTicks(Vector2 homeEntrance, Vector2 parkPosition) =>
            Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(homeEntrance, parkPosition) / ParkWalkMetersPerTick));

        PedestrianPose ParkPose(Citizen c, double time, long since)
        {
            var park = services?.GetFacility(c.LeisureParkId);
            var home = c.Household != null ? city?.GetBuilding(c.Household.HomeId) : null;
            if (park == null || home == null) return PedestrianPose.Hidden;
            if (!TryBuildingEndpoint(home, out var he) || !TryParkEndpoint(park, out var pe)) return PedestrianPose.Hidden;
            int walk = ParkWalkTicks(home.Entrance, park.Position);
            double end = c.BusyUntilTick;
            double back = end - walk;
            // Platz des Bürgers auf der Parkfläche (fest je Bürger) + kleine Runde
            var spot = ParkSpot(park, c.Id, time);
            if (time >= back) return Walk(Route(pe, he), back, end, time, PedestrianSpot.Park);
            if (since != long.MinValue && time < since + walk) return Walk(Route(he, pe), since, since + walk, time, PedestrianSpot.Park);
            // Vom Ende des Hinwegs (Parkmitte) zum eigenen Platz schlendern bzw. vor dem Rückweg zurück zur Mitte
            float blend = 1f;
            if (since != long.MinValue) blend = (float)Math.Clamp(time - (since + walk), 0.0, 1.0);
            blend = MathF.Min(blend, (float)Math.Clamp(back - time, 0.0, 1.0));
            var p = Vector2.Lerp(park.Position, spot.pos, blend);
            return new PedestrianPose
            {
                Kind = PedestrianActivity.Standing, Spot = PedestrianSpot.Park, Position = p, Height = park.Height,
                Direction = spot.dir,
            };
        }

        /// <summary>Fester Platz eines Bürgers im Park (innerhalb der Fläche) mit langsamer kleiner Runde.</summary>
        static (Vector2 pos, Vector2 dir) ParkSpot(ServiceFacility f, int citizenId, double time)
        {
            float u = Unit(citizenId, 21) - 0.5f, v = Unit(citizenId, 22) - 0.5f;
            var basePos = f.Position + f.Side * (u * f.Type.Width * 0.76f) + f.Front * (v * f.Type.Depth * 0.76f);
            float r = MathF.Min(1.5f, MathF.Min(f.Type.Width, f.Type.Depth) * 0.1f);
            double phase = time / 12.0 * 2.0 * Math.PI + Unit(citizenId, 23) * 6.2832;
            var off = new Vector2((float)Math.Cos(phase), (float)Math.Sin(phase));
            var dir = new Vector2(-off.Y, off.X);
            return (basePos + off * r, dir);
        }

        PedestrianPose StrollPose(Citizen c, double time, long since)
        {
            var home = c.Household != null ? city?.GetBuilding(c.Household.HomeId) : null;
            if (home == null || !TryBuildingEndpoint(home, out var he)) return PedestrianPose.Hidden;
            var route = StrollRoute(he, home.Id, (c.Id & 1) == 0 ? 1 : -1);
            if (route == null) return PedestrianPose.Hidden;
            double phase0 = since != long.MinValue ? since : Unit(c.Id, 31) * StrollPeriodTicks;
            double u = (time - phase0) / StrollPeriodTicks;
            u -= Math.Floor(u);
            float tri = (float)(1.0 - Math.Abs(2.0 * u - 1.0)); // 0 → 1 → 0
            float d = tri * route.Length;
            route.Sample(d, out var p, out float h, out var dir);
            if (u > 0.5) dir = -dir;
            return new PedestrianPose
            {
                Kind = PedestrianActivity.Walking, Spot = PedestrianSpot.Stroll, Position = p, Height = h, Direction = dir,
                MetersPerTick = 2f * route.Length / StrollPeriodTicks, Progress = tri,
            };
        }

        /// <summary>Haustür → eigener Gehweg → <see cref="StrollLength"/> m entlang (Richtung nach Bürger, sonst die andere).</summary>
        PedestrianRoute StrollRoute(in PedestrianEndpoint home, int homeId, int dir)
        {
            var g = Graph;
            var a = home.Access;
            for (int attempt = 0; attempt < 2; attempt++, dir = -dir)
            {
                if (!g.TryAccess(a.SegmentId, a.Side, a.D + dir * StrollLength, out var b) || MathF.Abs(b.D - a.D) < 10f) continue;
                var e = new PedestrianEndpoint
                {
                    Key = KindStroll | ((long)(uint)homeId << 1) | (dir > 0 ? 1L : 0L), Door = b.Point, DoorHeight = b.Height, Access = b, IsValid = true,
                };
                var r = Route(home, e);
                if (r != null) return r;
            }
            return null;
        }

        // ------------------------------------------------------------ Hilfen

        /// <summary>Deterministischer Wert 0..1 je Bürger und Zweck (Streuung, Platz, Phase).</summary>
        public static float Unit(int id, uint salt)
        {
            uint x = (uint)id * 0x9E3779B1u ^ salt * 0x85EBCA77u;
            x ^= x >> 15; x *= 0x2C1B3C6Du; x ^= x >> 12; x *= 0x297A2D39u; x ^= x >> 15;
            return (x & 0xFFFFFF) / 16777216f;
        }
    }
}
