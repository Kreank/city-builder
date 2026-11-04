using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;

namespace CityBuilder.Simulation.Aviation
{
    /// <summary>
    /// Flughafen planen, bauen, ausbauen, abreißen: Platzregeln (in der Karte, kein Wasser, Gelände eben genug – wird eingeebnet,
    /// Erdmenge kostet –, keine Straßen/Gleise/Bauwerke/Freileitungen auf dem Gelände, Zonengebäude weichen), Hindernisfreiheit
    /// der Einflugschneisen, Straßenzufahrt an der Landseite des Terminals.
    /// </summary>
    public sealed partial class AviationSystem
    {
        // ------------------------------------------------------------ Planen

        /// <summary>
        /// Flughafen planen: Bahnmitte <paramref name="center"/>, Bahnrichtung <paramref name="dir"/> (A → B), Terminal-Seite
        /// <paramref name="side"/> (+1 = links von A → B). Bahnlänge nach Bauart und Kartengröße.
        /// </summary>
        public AirportPlan PlanAirport(AirportType type, Vector2 center, Vector2 dir, int side)
        {
            if (dir.LengthSquared() < 1e-6f) dir = Vector2.UnitX;
            dir = Vector2.Normalize(dir);
            var plan = new AirportPlan
            {
                Type = type, Center = center, Dir = dir, Side = side >= 0 ? 1 : -1,
                RunwayLength = type.RunwayLengthFor(world.Settings.SizeMeters), Cost = type.Cost,
            };
            plan.Error = ValidateAirport(plan);
            return plan;
        }

        /// <summary>Bahnrichtung nach dem Wind: Bahn liegt in der Hauptwindrichtung (Start und Landung gegen den Wind).</summary>
        public Vector2 WindAlignedDirection => Vector2.Normalize(PrevailingWind);

        AirportBuildError ValidateAirport(AirportPlan plan)
        {
            var type = plan.Type;
            var layout = plan.Layout;
            var areas = layout.Areas;
            float m = AviationRules.EdgeMargin;
            foreach (var area in areas)
                foreach (var c in area.Corners())
                    if (c.X < world.Min.X + m || c.Y < world.Min.Y + m || c.X > world.Max.X - m || c.Y > world.Max.Y - m)
                        return AirportBuildError.OutOfWorld;
            // --- Kacheln: beide Flächen auf gekauftem Land (die Bahn eines Verkehrsflughafens braucht meist zwei Kacheln)
            foreach (var area in areas)
                if (!BuildArea.AllowsRect(world.Roads, area.Center, area.Front, area.Width, area.Depth)) return AirportBuildError.TileNotOwned;
            // --- Ende Kacheln

            // Gelände: Wasser, Ebenheit, Erdmenge (Raster über beide Flächen)
            float step = AviationRules.SiteSampleStep;
            double sum = 0, cellSum = 0;
            float lo = float.MaxValue, hi = float.MinValue;
            var heights = new List<(float h, float area)>();
            foreach (var site in areas)
            {
                int nu = Math.Max(2, (int)MathF.Ceiling(site.Width / step)), nv = Math.Max(2, (int)MathF.Ceiling(site.Depth / step));
                float cellArea = (site.Width / nu) * (site.Depth / nv);
                for (int i = 0; i <= nu; i++)
                    for (int j = 0; j <= nv; j++)
                    {
                        var p = site.Center + site.Side * (site.Width * (i / (float)nu - 0.5f)) + site.Front * (site.Depth * (j / (float)nv - 0.5f));
                        if (terrain.IsWater(p)) { plan.BlockerName = "Gewässer"; return AirportBuildError.OnWater; }
                        float h = terrain.HeightAt(p);
                        heights.Add((h, cellArea));
                        sum += h * cellArea;
                        cellSum += cellArea;
                        lo = MathF.Min(lo, h); hi = MathF.Max(hi, h);
                    }
            }
            float target = (float)(sum / Math.Max(1.0, cellSum));
            plan.Height = MathF.Max(target, terrain.WaterLevel + 1.5f);
            double volume = 0;
            foreach (var (h, area) in heights) volume += MathF.Abs(h - plan.Height) * area;
            plan.EarthVolume = (float)volume;
            plan.EarthworkCost = plan.EarthVolume * EconomyRules.TerraformCostPerCubicMeter;
            if (hi - lo > AviationRules.MaxLevelDifference) { plan.BlockerName = $"{hi - lo:0} m Höhenunterschied"; return AirportBuildError.TooHilly; }

            // Straßen, Gleise, Bauwerke, Freileitungen
            int displaced = 0;
            foreach (var site in areas)
            {
                foreach (var s in roads.Segments)
                    if (ServiceGeometry.RoadCrossesRect(s, site.Center, site.Front, site.Width, site.Depth, 0f))
                    {
                        plan.BlockerName = s.IsLocked ? $"{s.Type.DisplayName} (gehört der Welt)" : s.Type.DisplayName;
                        return s.IsLocked ? AirportBuildError.OverlapsHighway : AirportBuildError.OverlapsRoad;
                    }
                if (world.Rail != null)
                    foreach (var s in world.Rail.Tracks.Segments)
                        if (ServiceGeometry.RoadCrossesRect(s, site.Center, site.Front, site.Width, site.Depth, 0f))
                        {
                            plan.BlockerName = s.IsLocked ? "Fernbahn-Hauptstrecke (gehört der Welt)" : "Gleis";
                            return AirportBuildError.OverlapsTrack;
                        }
                foreach (var b in roads.Structures)
                {
                    var o = b.Footprint;
                    if (!ServiceGeometry.RectsOverlap(site.Center, site.Front, site.Width, site.Depth, o.Center, o.Front, o.Width, o.Depth)) continue;
                    plan.BlockerName = b.Name;
                    return b.Kind == AirportBlockKind ? AirportBuildError.OverlapsAirport : AirportBuildError.OverlapsStructure;
                }
                if (world.Utilities != null)
                    foreach (var l in world.Utilities.Lines)
                        foreach (var p in l.Poles)
                            if (site.Contains(p, l.PoleFootprintHalf)) { plan.BlockerName = l.Type.DisplayName; return AirportBuildError.OverlapsPowerLine; }
                if (world.City != null)
                    foreach (var b in world.City.Buildings)
                        if (!b.IsPlayerPlaced && ServiceGeometry.RectsOverlap(site.Center, site.Front, site.Width, site.Depth, b.Position, b.Front, b.Width, b.Depth)) displaced++;
            }
            plan.Displaced = displaced;

            // Hindernisfreiheit der Einflugschneisen (Gebäude auf dem Gelände zählen nicht, sie weichen)
            ScanObstacles(type, plan.Center, plan.Dir, plan.Side, plan.RunwayLength, plan.Height, 0, plan.Obstacles);
            Evaluate(plan.Obstacles, out var ca, out var cb, out float factor);
            plan.CorridorA = ca; plan.CorridorB = cb; plan.CapacityFactor = factor;
            if (ca == CorridorState.Closed && cb == CorridorState.Closed)
            {
                plan.BlockerName = plan.Obstacles.Count > 0 ? plan.Obstacles[0].Name : "Hindernis";
                return AirportBuildError.ApproachBlocked;
            }

            plan.HasRoadAccess = RoadFront.FindAccess(roads, layout.Terminal, AviationRules.RoadReach, out _, out _);
            if (!Affordable(plan.TotalCost)) return AirportBuildError.InsufficientFunds;
            return AirportBuildError.None;
        }

        // ------------------------------------------------------------ Bauen / Ausbau / Abriss

        public Airport BuildAirport(AirportPlan plan)
        {
            if (plan == null || !plan.IsValid || !Affordable(plan.TotalCost)) return null;
            Budget?.Spend(BudgetCategory.AirportConstruction, plan.TotalCost);
            var a = new Airport
            {
                Id = nextAirportId++, Type = plan.Type, Center = plan.Center, Dir = plan.Dir, Side = plan.Side,
                RunwayLength = plan.RunwayLength, Height = plan.Height, Level = 1, BuiltTick = world.Clock.Tick,
            };
            a.Name = AirportName(a);
            PrepareSite(a);
            airports.Add(a);
            UpdateDemand(); // Nachfrage sofort (sonst erst um Mitternacht)
            ChooseDirections(a, world.Clock.Day);
            accessRoadVersion = -1;
            EnsureAccess();
            EnsureObstacles(true);
            ChooseDirections(a, world.Clock.Day);
            UpdateGatewayStatus();
            LayoutVersion++;
            Version++;
            return a;
        }

        public Airport TryBuildAirport(AirportType type, Vector2 center, Vector2 dir, int side) => BuildAirport(PlanAirport(type, center, dir, side));

        /// <summary>Terminal ausbauen (nächste Stufe: mehr Module, Abstellpositionen und Fluggäste je Stunde).</summary>
        public bool CanExpand(Airport a) => a != null && a.Level < a.Type.MaxLevel && Affordable(a.Type.ExpandCost);

        public bool ExpandTerminal(int id)
        {
            var a = GetAirport(id);
            if (!CanExpand(a)) return false;
            Budget?.Spend(BudgetCategory.AirportConstruction, a.Type.ExpandCost);
            a.Level++;
            accessRoadVersion = -1;
            EnsureAccess();
            LayoutVersion++;
            Version++;
            return true;
        }

        public bool DemolishAirport(int id)
        {
            var a = GetAirport(id);
            if (a == null) return false;
            airports.Remove(a);
            aircraft.RemoveAll(x => x.AirportId == id);
            rotations.RemoveAll(r => r.AirportId == id);
            Budget?.Refund((a.Type.Cost + (a.Level - 1) * a.Type.ExpandCost) * EconomyRules.DemolitionRefundShare);
            UpdateGatewayStatus();
            LayoutVersion++;
            Version++;
            return true;
        }

        void PrepareSite(Airport a)
        {
            var areas = a.Layout.Areas;
            foreach (var site in areas) terrain.FlattenArea(site.Center, site.Front, site.Width, site.Depth, a.Height, 24f);
            var city = world.City;
            if (city == null) return;
            var remove = new List<Zoning.Building>();
            foreach (var b in city.Buildings)
            {
                if (b.IsPlayerPlaced) continue;
                foreach (var site in areas)
                    if (ServiceGeometry.RectsOverlap(site.Center, site.Front, site.Width, site.Depth, b.Position, b.Front, b.Width, b.Depth)) { remove.Add(b); break; }
            }
            foreach (var b in remove) city.RemoveBuilding(b);
        }

        string AirportName(Airport a)
        {
            string city = world.Realm?.CityName ?? "Neustadt";
            string kind = a.Type.Class == AirportClass.Airfield ? "Flugplatz" : "Flughafen";
            if (airports.Count == 0) return $"{kind} {city}";
            return $"{kind} {city} {GatewayRules.Compass(a.Center)}";
        }

        // ------------------------------------------------------------ Zufahrt (abgeleitet)

        int accessRoadVersion = -1;

        void EnsureAccess()
        {
            if (accessRoadVersion == roads.Version) return;
            accessRoadVersion = roads.Version;
            foreach (var a in airports)
            {
                if (RoadFront.FindAccess(roads, a.Layout.Terminal, AviationRules.RoadReach, out int seg, out _))
                {
                    var road = roads.GetSegment(seg);
                    var front = a.Layout.Terminal.FrontMid;
                    a.HasAccess = true;
                    a.AccessSegmentId = seg;
                    a.AccessNodeId = Vector2.Distance(road.Start.Position, front) <= Vector2.Distance(road.End.Position, front) ? road.Start.Id : road.End.Id;
                }
                else { a.HasAccess = false; a.AccessSegmentId = -1; a.AccessNodeId = 0; }
            }
        }

        // ------------------------------------------------------------ Betriebsrichtung

        /// <summary>
        /// Betriebsrichtung des Tages: gegen den Wind landen und starten. Ist eine Einflugschneise gesperrt, landen und starten die
        /// Flugzeuge nur über das freie Bahnende (Gegenrichtungsbetrieb, Kapazität sinkt).
        /// </summary>
        void ChooseDirections(Airport a, int day)
        {
            // Wind des Tages aus dem Klima (Umwelt, Etappe 6: Richtung nach Breite + Wetterlagen); ohne Klima eigene Annahme
            var wind = world.Climate != null ? -world.Climate.WindOn(day).Toward : AviationRules.WindOfDay(PrevailingWind, world.Settings.TerrainSeed + a.Id * 17, day);
            a.WindToday = wind;
            int dw = Vector2.Dot(a.Dir, wind) >= 0f ? 1 : -1;
            bool ArrOk(int d) => (d > 0 ? a.CorridorA : a.CorridorB) != CorridorState.Closed;
            bool DepOk(int d) => (d > 0 ? a.CorridorB : a.CorridorA) != CorridorState.Closed;
            a.ArrivalDirection = ArrOk(dw) ? dw : ArrOk(-dw) ? -dw : dw;
            a.DepartureDirection = DepOk(dw) ? dw : DepOk(-dw) ? -dw : dw;
        }

        // ------------------------------------------------------------ Texte

        public static string ErrorText(AirportPlan plan)
        {
            switch (plan.Error)
            {
                case AirportBuildError.OutOfWorld: return "Flughafen passt hier nicht in die Karte – Mitte verschieben oder drehen (, .)";
                case AirportBuildError.OnWater: return "Gewässer auf dem Flughafengelände";
                case AirportBuildError.TooHilly: return $"Gelände zu uneben ({plan.BlockerName}, höchstens {AviationRules.MaxLevelDifference:0} m) – erst einebnen (L)";
                case AirportBuildError.OverlapsRoad: return $"Straße auf dem Gelände ({plan.BlockerName}) – abreißen oder Flughafen verschieben";
                case AirportBuildError.OverlapsHighway: return $"{plan.BlockerName} quert das Gelände – Flughafen verschieben oder drehen";
                case AirportBuildError.OverlapsTrack: return $"{plan.BlockerName} quert das Gelände – Flughafen verschieben oder drehen";
                case AirportBuildError.OverlapsStructure: return $"{plan.BlockerName} steht auf dem Gelände";
                case AirportBuildError.OverlapsPowerLine: return $"{plan.BlockerName} (Masten) über dem Gelände – Leitung verlegen";
                case AirportBuildError.OverlapsAirport: return $"{plan.BlockerName} im Weg";
                case AirportBuildError.ApproachBlocked: return $"Beide Einflugschneisen gesperrt ({plan.BlockerName}) – kein Flugbetrieb möglich";
                case AirportBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                case AirportBuildError.TileNotOwned: return BuildArea.NotOwnedText + " – ein Verkehrsflughafen braucht meist zwei Kacheln"; // Kacheln
                default: return plan.Error.ToString();
            }
        }

        public static string CorridorText(CorridorState s)
        {
            switch (s)
            {
                case CorridorState.Clear: return "frei";
                case CorridorState.Limited: return "eingeschränkt (Hindernisse)";
                default: return "gesperrt (Hindernis in der Kernzone)";
            }
        }
    }
}
