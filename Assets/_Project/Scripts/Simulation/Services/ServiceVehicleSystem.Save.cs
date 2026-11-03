using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Services
{
    /// <summary>
    /// Spielstand der Dienstfahrzeuge (Abschnitt „servicevehicles“ v1): Schalter, Zähler, Zufall, alle Fahrzeuge mit Auftrag,
    /// Halten und laufender Fahrt-Id (das Fahrzeug selbst steht im Verkehrsabschnitt), Rettungs-Warteschlange, Tageszähler.
    /// Fehlt der Abschnitt (alte Stände) → keine Fahrzeuge, aus; das Spiel schaltet es nach dem Laden ein.
    /// </summary>
    public sealed partial class ServiceVehicleSystem : ISaveable
    {
        public string SaveId => "servicevehicles";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Bool(Enabled);
            w.Int(nextUnitId); w.Int(nextTripId); w.Int(statsDay);
            rng.WriteState(w);
            WriteStats(w, Today);
            WriteStats(w, Yesterday);
            w.Int(units.Count);
            foreach (var u in units)
            {
                w.Int(u.Id); w.Name(u.Kind); w.Int(u.DepotId); w.Name(u.State); w.Name(u.Mission);
                w.Int(u.TripId); w.Int(u.TargetBuildingId); w.Int(u.FromBuildingId);
                w.Int(u.Stops.Count);
                foreach (int s in u.Stops) w.Int(s);
                w.Double(u.WorkUntil); w.Long(u.ReadyTick);
                w.Bool(u.HasPark); w.Vector2(u.ParkPosition); w.Vector2(u.ParkDirection); w.Float(u.ParkHeight);
                w.Float(u.Load); w.Int(u.Visits); w.Int(u.Retries); w.Long(u.MissionStartTick);
            }
            w.Int(rescueQueue.Count);
            foreach (var (c, h) in rescueQueue) { w.Int(c); w.Int(h); }
            w.Float(services.GarbageBaseShare);
        }

        public void Read(SaveReader r, int version)
        {
            Enabled = r.Bool();
            nextUnitId = r.Int(); nextTripId = r.Int(); statsDay = r.Int();
            rng.ReadState(r);
            ReadStats(r, Today);
            ReadStats(r, Yesterday);
            units.Clear();
            byTrip.Clear();
            int n = r.Count(40);
            for (int i = 0; i < n; i++)
            {
                var u = new ServiceUnit
                {
                    Id = r.Int(), Kind = r.Name<ServiceVehicleKind>(), DepotId = r.Int(), State = r.Name<ServiceUnitState>(),
                    Mission = r.Name<ServiceMission>(), TripId = r.Int(), TargetBuildingId = r.Int(), FromBuildingId = r.Int(),
                };
                int stops = r.Count(4);
                for (int s = 0; s < stops; s++) u.Stops.Add(r.Int());
                u.WorkUntil = r.Double(); u.ReadyTick = r.Long();
                u.HasPark = r.Bool(); u.ParkPosition = r.Vector2(); u.ParkDirection = r.Vector2(); u.ParkHeight = r.Float();
                u.Load = r.Float(); u.Visits = r.Int(); u.Retries = r.Int(); u.MissionStartTick = r.Long();
                units.Add(u);
                if (u.TripId != 0) byTrip[u.TripId] = u;
            }
            rescueQueue.Clear();
            int q = r.Count(8);
            for (int i = 0; i < q; i++) rescueQueue.Add((r.Int(), r.Int()));
            services.GarbageBaseShare = r.Float();
        }

        static void WriteStats(SaveWriter w, ServiceVehicleStats s)
        {
            w.Int(s.Trips); w.Int(s.Stops); w.Int(s.PatrolStops); w.Int(s.FireCalls); w.Int(s.Rescues); w.Int(s.MissedCalls);
            w.Int(s.FailedStarts); w.Float(s.GarbageCollected);
        }

        static void ReadStats(SaveReader r, ServiceVehicleStats s)
        {
            s.Trips = r.Int(); s.Stops = r.Int(); s.PatrolStops = r.Int(); s.FireCalls = r.Int(); s.Rescues = r.Int(); s.MissedCalls = r.Int();
            s.FailedStarts = r.Int(); s.GarbageCollected = r.Float();
        }
    }
}
