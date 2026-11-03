using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Sicherheit (Brände, Kriminalität): Hooks und Aktionen, die das <c>Safety.SafetySystem</c> am Bürgersystem braucht.
    /// Ohne Sicherheitssystem (Hook null, keine Ereignisse) ändert sich nichts – bitgleich wie vorher.
    /// </summary>
    public sealed partial class CitizenSystem
    {
        /// <summary>Kriminalitätsrate am Wohngebäude 0..1 (null = 0). Senkt das Bedürfnis Sicherheit.</summary>
        public Func<Building, float> CrimeLevel { get; set; }

        /// <summary>Sicherheit sinkt um bis zu so viel bei Kriminalitätsrate 1.</summary>
        public const float CrimeSafetyWeight = 0.45f;
        /// <summary>… und um bis zu so viel nach einem Einbruch (Erinnerung, klingt ab) bzw. halb so viel nach einem Brand.</summary>
        public const float BurglarySafetyWeight = 0.25f;

        float SafetyAdjust(Citizen c, Building home, float safety)
        {
            var hook = CrimeLevel;
            if (hook == null) return safety;
            float crime = Math.Clamp(hook(home), 0f, 1f);
            var m = c.Memories;
            float shock = MathF.Min(1f, m.Strength(MemoryKind.Burglary) + 0.5f * m.Strength(MemoryKind.Fire));
            if (crime <= 0f && shock <= 0f) return safety;
            return Math.Clamp(safety - CrimeSafetyWeight * crime - BurglarySafetyWeight * shock, 0f, 1f);
        }

        /// <summary>Erinnerung von außen (Sicherheitssystem: Brand, Einbruch, Unsicherheit).</summary>
        public void RememberEvent(Citizen c, MemoryKind kind, float strength, Building at) =>
            Remember(c, kind, strength, street: at?.FrontSegmentId ?? 0, building: at?.Id ?? 0);

        /// <summary>Bewohner eines Gebäudes (alle Haushalte, nach Haushalts-Id).</summary>
        public IEnumerable<Citizen> ResidentsOf(int buildingId)
        {
            foreach (var h in HouseholdsIn(buildingId))
                foreach (var m in h.MemberList)
                    if (!gone.Contains(m)) yield return m;
        }

        /// <summary>Bürger, die gerade im Gebäude sind (zu Hause, bei der Arbeit, im Laden), nach Id.</summary>
        public List<Citizen> PresentCitizens(int buildingId)
        {
            var list = new List<Citizen>();
            foreach (var c in citizens)
                if (!gone.Contains(c) && c.Activity != Activity.Travelling && c.Location.BuildingId == buildingId) list.Add(c);
            return list;
        }

        /// <summary>
        /// Verletzung (Brand): Gesundheit sinkt um <paramref name="severity"/>, starke Brand-Erinnerung. Keine Todesfälle
        /// (bewusst: Brände sollen Folgen haben, aber kein Weltuntergang sein).
        /// </summary>
        public void Injure(Citizen c, float severity, Building at)
        {
            if (c == null || gone.Contains(c)) return;
            c.Health = Math.Clamp(c.Health - severity, 0.05f, 1f);
            Remember(c, MemoryKind.Fire, 0.95f, street: at?.FrontSegmentId ?? 0, building: at?.Id ?? 0);
        }

        /// <summary>
        /// Gebäude wird unbewohnbar (schwerer Brand, Ruine): Haushalte ziehen in eine freie Wohnung (sonst weg), Beschäftigte
        /// verlieren den Job (Einpendler gehen). Liefert die Ids der umgezogenen Haushalte (für die spätere Rückkehr).
        /// </summary>
        public List<int> Vacate(Building b)
        {
            var moved = new List<int>();
            if (b == null) return moved;
            if (homes.TryGetValue(b.Id, out var hs))
            {
                var order = new List<Household>(hs);
                order.Sort((x, y) => x.Id.CompareTo(y.Id)); // unabhängig von der Listenreihenfolge (Spielstand)
                foreach (var h in order)
                {
                    var target = FindFreeHome(b.Position, b.Id);
                    if (target == null || target.OutOfService)
                    {
                        RemoveHousehold(h); // keine freie Wohnung in der Stadt → zieht weg
                        continue;
                    }
                    RelocateHousehold(h, target);
                    moved.Add(h.Id);
                    foreach (var m in h.MemberList) Remember(m, MemoryKind.Fire, 0.8f, street: b.FrontSegmentId, building: b.Id);
                }
                if (homes.TryGetValue(b.Id, out var rest) && rest.Count == 0) homes.Remove(b.Id);
            }
            if (staff.TryGetValue(b.Id, out var ws))
            {
                staff.Remove(b.Id);
                var order = new List<Citizen>(ws);
                order.Sort((x, y) => x.Id.CompareTo(y.Id));
                foreach (var w in order)
                {
                    w.WorkplaceId = 0;
                    w.CommuteMeters = 0f;
                    if (w.IsCommuterIn) RemoveCitizen(w, false);
                    else Remember(w, MemoryKind.Fire, 0.6f, street: b.FrontSegmentId, building: b.Id);
                }
            }
            foreach (var c in citizens)
                if (!gone.Contains(c) && c.Activity != Activity.Travelling && c.Location.BuildingId == b.Id) TeleportHome(c);
            b.OccupiedHouseholds = 0;
            b.Residents = 0;
            b.Workers = 0;
            return moved;
        }

        /// <summary>
        /// Nach der Sanierung: umgezogene Haushalte kehren zurück, soweit sie noch in der Stadt wohnen und Platz ist.
        /// Liefert die Zahl der Rückkehrer.
        /// </summary>
        public int ReturnHouseholds(Building b, IReadOnlyList<int> householdIds)
        {
            if (b == null || householdIds == null || householdIds.Count == 0) return 0;
            var wanted = new HashSet<int>(householdIds);
            var found = new List<Household>();
            foreach (var kv in homes)
                foreach (var h in kv.Value)
                    if (wanted.Contains(h.Id) && h.HomeId != b.Id) found.Add(h);
            found.Sort((x, y) => x.Id.CompareTo(y.Id));
            int n = 0;
            foreach (var h in found)
            {
                if (HouseholdsIn(b.Id).Count >= b.Households) break;
                RelocateHousehold(h, b);
                foreach (var m in h.MemberList) Remember(m, MemoryKind.MovedHome, 0.5f, street: b.FrontSegmentId, building: b.Id);
                n++;
            }
            return n;
        }

        /// <summary>Wohngebäude im Umkreis (Mitte), nach Id – für Nachbarschafts-Erinnerungen.</summary>
        public void RememberAround(Vector2 center, float radius, int exceptBuilding, MemoryKind kind, float strength, Building at, int maxPeople = 60)
        {
            var near = new List<Building>();
            foreach (var b in city.Buildings)
                if (b.Id != exceptBuilding && b.Households > 0 && Vector2.DistanceSquared(b.Position, center) <= radius * radius) near.Add(b);
            near.Sort((x, y) => x.Id.CompareTo(y.Id));
            int n = 0;
            foreach (var b in near)
                foreach (var c in ResidentsOf(b.Id))
                {
                    if (n++ >= maxPeople) return;
                    Remember(c, kind, strength, street: at?.FrontSegmentId ?? 0, building: at?.Id ?? 0);
                }
        }
    }
}
