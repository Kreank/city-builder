using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Aviation
{
    /// <summary>
    /// Spielstand „aviation“: Flughäfen (Lage, Richtung, Seite, Bahnlänge, Höhe, Ausbaustufe, Betriebsrichtung, Wind, Planungs-
    /// Überträge, Statistik, Lärm-Zähler, Zustand der Einflugschneisen), Umläufe (Flugplan), sichtbare Flugzeuge vollständig
    /// (Zustand, Weg, Tempo, Position, Zeiten), Nachtflugverbot, Besucher, Nachfrage des Tages, Zuzug-/Gebühren-Überträge,
    /// Fluglärm-Raster. Abgeleitet (nach dem Laden neu): Zufahrt, Hindernisliste (Anzeige), Ziele, Gateway-Zustand.
    /// </summary>
    public sealed partial class AviationSystem : ISaveable, IAfterLoad
    {
        public string SaveId => "aviation";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(nextAirportId); w.Int(nextAircraftId); w.Int(nextRotationId);
            w.Double(pendingFees); w.Double(pendingTourism); w.Bool(NightBan);
            w.Double(FeesTotal); w.Double(TourismTotal); w.Double(VisitorShopSpendYesterday); w.Double(VisitorLeisureSpendYesterday);
            w.Int(ComplaintsYesterday); w.Int(AffectedResidentsYesterday);
            w.Float(demandResidents); w.Float(demandBusiness); w.Float(demandTourists); w.Int(officeJobs);
            w.Floats(visitors);
            w.Int(pendingAirMoveIns); w.Int(lastShipmentId); w.Bool(gatewayTouched);
            w.List(airports, a =>
            {
                w.Int(a.Id); w.String(a.Type.Id); w.String(a.Name); w.Vector2(a.Center); w.Vector2(a.Dir); w.Int(a.Side);
                w.Float(a.RunwayLength); w.Float(a.Height); w.Int(a.Level); w.Long(a.BuiltTick);
                w.Int(a.ArrivalDirection); w.Int(a.DepartureDirection); w.Vector2(a.WindToday);
                w.Double(a.ScheduleCarry); w.Int(a.FlightCounter); w.Double(a.MoveInCarry);
                w.Ints(new[]
                {
                    a.ArrivalsToday, a.ArrivalsYesterday, a.DeparturesToday, a.DeparturesYesterday, a.PaxInToday, a.PaxInYesterday,
                    a.PaxOutToday, a.PaxOutYesterday, a.TouristsToday, a.TouristsYesterday, a.BusinessToday, a.BusinessYesterday,
                    a.LoadsToday, a.LoadsYesterday, a.PendingLoads, a.TotalLoads, a.MoveInsTotal, a.TotalMovements,
                    a.MovementsThisHour, a.MovementsLastHour,
                });
                w.Floats(new[] { a.NoiseA, a.NoiseB, a.NoiseHourA, a.NoiseHourB, a.ObstacleFactor });
                w.Name(a.CorridorA); w.Name(a.CorridorB); w.Int(a.ObstacleSignature);
            });
            w.List(rotations, r =>
            {
                w.Int(r.Id); w.Int(r.AirportId); w.String(r.Type.Id); w.String(r.FlightNumber); w.Int(r.Destination);
                w.Double(r.ArrivalTime); w.Double(r.DepartureTime);
                w.Int(r.ArrivingPax); w.Int(r.DepartingPax); w.Int(r.ArrivingLoads); w.Int(r.DepartingLoads);
                w.Float(r.TouristShare); w.Float(r.BusinessShare);
                w.Bool(r.Visible); w.Int(r.AircraftId); w.Bool(r.Arrived); w.Bool(r.Departed); w.Bool(r.Night);
            });
            w.List(aircraft, x =>
            {
                w.Int(x.Id); w.String(x.Type.Id); w.Int(x.AirportId); w.Name(x.State); w.Int(x.Direction); w.Int(x.Stand);
                w.Float(x.S); w.Float(x.PrevS); w.Float(x.Speed); w.Float(x.ExitU); w.Int(x.RotationId);
                w.Double(x.StateSince); w.Double(x.DepartAt); w.String(x.FlightNumber);
            });
            NoiseLayer.WriteState(w);
        }

        readonly List<(Airport a, int arr, int dep)> loadedDirections = new List<(Airport, int, int)>();

        public void Read(SaveReader r, int version)
        {
            nextAirportId = r.Int(); nextAircraftId = r.Int(); nextRotationId = r.Int();
            pendingFees = r.Double(); pendingTourism = r.Double(); NightBan = r.Bool();
            FeesTotal = r.Double(); TourismTotal = r.Double(); VisitorShopSpendYesterday = r.Double(); VisitorLeisureSpendYesterday = r.Double();
            ComplaintsYesterday = r.Int(); AffectedResidentsYesterday = r.Int();
            demandResidents = r.Float(); demandBusiness = r.Float(); demandTourists = r.Float(); officeJobs = r.Int();
            var v = r.Floats();
            Array.Clear(visitors, 0, visitors.Length);
            for (int i = 0; i < Math.Min(v.Length, visitors.Length); i++) visitors[i] = v[i];
            pendingAirMoveIns = r.Int(); lastShipmentId = r.Int(); gatewayTouched = r.Bool();
            airports.Clear();
            loadedDirections.Clear();
            airports.AddRange(r.List(() =>
            {
                var a = new Airport { Id = r.Int() };
                string type = r.String();
                a.Type = AirportCatalog.Get(type ?? "") ?? throw new SaveFormatException($"Unbekannter Flughafentyp „{type}“.");
                a.Name = r.String() ?? ""; a.Center = r.Vector2(); a.Dir = r.Vector2(); a.Side = r.Int();
                a.RunwayLength = r.Float(); a.Height = r.Float(); a.Level = r.Int(); a.BuiltTick = r.Long();
                a.ArrivalDirection = r.Int(); a.DepartureDirection = r.Int(); a.WindToday = r.Vector2();
                a.ScheduleCarry = r.Double(); a.FlightCounter = r.Int(); a.MoveInCarry = r.Double();
                var s = r.Ints();
                int k = 0;
                int Next() => k < s.Length ? s[k++] : 0;
                a.ArrivalsToday = Next(); a.ArrivalsYesterday = Next(); a.DeparturesToday = Next(); a.DeparturesYesterday = Next();
                a.PaxInToday = Next(); a.PaxInYesterday = Next(); a.PaxOutToday = Next(); a.PaxOutYesterday = Next();
                a.TouristsToday = Next(); a.TouristsYesterday = Next(); a.BusinessToday = Next(); a.BusinessYesterday = Next();
                a.LoadsToday = Next(); a.LoadsYesterday = Next(); a.PendingLoads = Next(); a.TotalLoads = Next();
                a.MoveInsTotal = Next(); a.TotalMovements = Next(); a.MovementsThisHour = Next(); a.MovementsLastHour = Next();
                var f = r.Floats();
                float F(int i) => i < f.Length ? f[i] : 0f;
                a.NoiseA = F(0); a.NoiseB = F(1); a.NoiseHourA = F(2); a.NoiseHourB = F(3); a.ObstacleFactor = f.Length > 4 ? f[4] : 1f;
                a.CorridorA = r.Name<CorridorState>(); a.CorridorB = r.Name<CorridorState>(); a.ObstacleSignature = r.Int();
                return a;
            }, 40));
            foreach (var a in airports) loadedDirections.Add((a, a.ArrivalDirection, a.DepartureDirection));
            rotations.Clear();
            rotations.AddRange(r.List(() =>
            {
                var x = new Rotation { Id = r.Int(), AirportId = r.Int() };
                string type = r.String();
                x.Type = AircraftCatalog.Get(type ?? "") ?? throw new SaveFormatException($"Unbekanntes Flugzeugmuster „{type}“.");
                x.FlightNumber = r.String() ?? ""; x.Destination = r.Int();
                x.ArrivalTime = r.Double(); x.DepartureTime = r.Double();
                x.ArrivingPax = r.Int(); x.DepartingPax = r.Int(); x.ArrivingLoads = r.Int(); x.DepartingLoads = r.Int();
                x.TouristShare = r.Float(); x.BusinessShare = r.Float();
                x.Visible = r.Bool(); x.AircraftId = r.Int(); x.Arrived = r.Bool(); x.Departed = r.Bool(); x.Night = r.Bool();
                return x;
            }, 30));
            aircraft.Clear();
            removedAircraft.Clear();
            aircraft.AddRange(r.List(() =>
            {
                var x = new Aircraft { Id = r.Int() };
                string type = r.String();
                x.Type = AircraftCatalog.Get(type ?? "") ?? throw new SaveFormatException($"Unbekanntes Flugzeugmuster „{type}“.");
                x.AirportId = r.Int(); x.State = r.Name<AircraftState>(); x.Direction = r.Int(); x.Stand = r.Int();
                x.S = r.Float(); x.PrevS = r.Float(); x.Speed = r.Float(); x.ExitU = r.Float(); x.RotationId = r.Int();
                x.StateSince = r.Double(); x.DepartAt = r.Double(); x.FlightNumber = r.String() ?? "";
                return x;
            }, 30));
            NoiseLayer.ReadState(r);
        }

        public void AfterLoad(SimulationWorld w)
        {
            accessRoadVersion = -1;
            EnsureAccess();
            // Hindernisliste nur für die Anzeige neu; Zustand der Schneisen/Kapazität kommt aus dem Stand (bitgleich weiter)
            foreach (var a in airports)
                ScanObstacles(a.Type, a.Center, a.Dir, a.Side, a.RunwayLength, a.Height, a.Level, a.Obstacles);
            foreach (var (a, arr, dep) in loadedDirections) { a.ArrivalDirection = arr; a.DepartureDirection = dep; }
            loadedDirections.Clear();
            BuildDestinations();
            UpdateGatewayStatus();
            LayoutVersion++;
            Version++;
        }
    }
}
