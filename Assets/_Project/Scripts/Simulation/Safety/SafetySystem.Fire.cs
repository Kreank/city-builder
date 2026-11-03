using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Services.Content;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Safety
{
    /// <summary>Brände: Entstehung, Verlauf, Löschen, Übergreifen, Folgen, Sanierung/Wiederaufbau.</summary>
    public sealed partial class SafetySystem
    {
        const float Dt = 1f / TimeModel.TicksPerHour; // Stunden je Tick

        // ------------------------------------------------------------ Brandgefahr

        /// <summary>Brandgefahr eines Gebäudes mit allen Faktoren (Inspektor, Ansicht „Brandgefahr“).</summary>
        public FireRiskInfo FireRiskOf(Building b)
        {
            var info = new FireRiskInfo { Zone = 1f, Recipe = 1f, Condition = 1f, Garbage = 1f, Power = 1f, Size = 1f, Prevention = 1f };
            if (b == null) return info;
            info.Zone = SafetyRules.ZoneFactor(b.Zone);
            var firm = world.Goods?.FirmAt(b.Id);
            if (firm != null)
            {
                info.Recipe = SafetyRules.RecipeFactor(firm.Recipe.Id);
                info.RecipeName = firm.Recipe.Name;
            }
            info.Condition = 1f + SafetyRules.ConditionWeight * (1f - Math.Clamp(b.Condition, 0f, 1f));
            info.Garbage = 1f + SafetyRules.GarbageWeight * services.GarbageLevel(b);
            var u = world.Utilities;
            if (u != null && u.IsRequired(UtilityKind.Power) && !u.GetStatus(b).HasPower) info.Power = SafetyRules.PowerOutageFactor;
            info.Size = SafetyRules.SizeFactor(b);
            info.Prevention = 1f - SafetyRules.PreventionWeight * services.Coverage(ServiceKinds.Fire, b);
            info.Factor = info.Zone * info.Recipe * info.Condition * info.Garbage * info.Power * info.Size * info.Prevention;
            info.PerYear = info.Factor * SafetyRules.IgnitionsPerDay * SimulationClock.DaysPerYear;
            return info;
        }

        /// <summary>Löschwasser am Gebäude: Hydrant = Wassernetz liefert, sonst nur Tankwasser.</summary>
        public float WaterFactor(Building b)
        {
            var u = world.Utilities;
            if (u == null || b == null) return SafetyRules.TankWaterFactor;
            return u.GetStatus(b).HasWater ? 1f : SafetyRules.TankWaterFactor;
        }

        string RecipeOf(Building b) => world.Goods?.FirmAt(b.Id)?.Recipe?.Id;

        // ------------------------------------------------------------ Entstehung

        void RollIgnitions(long now)
        {
            int hour = (int)(now / TimeModel.TicksPerHour);
            var list = SortedBuildings();
            foreach (var b in list)
            {
                if (!b.IsConstructed || b.OutOfService || fireByBuilding.ContainsKey(b.Id)) continue;
                double p = FireRiskOf(b).Factor * SafetyRules.IgnitionsPerDay * IgnitionScale / 24.0;
                if (SafetyRules.Roll(b.Id, hour, 3) >= p) continue;
                Ignite(b, now, 0);
            }
        }

        /// <summary>Brand an einem Gebäude auslösen (Entstehung, Übergreifen, Tests). Liefert den Brand (oder den schon laufenden).</summary>
        public Fire Ignite(Building b, long now, int sourceFireId = 0, float intensity = SafetyRules.StartIntensity)
        {
            if (b == null || !b.IsConstructed || b.OutOfService) return null;
            if (fireByBuilding.TryGetValue(b.Id, out var running)) return running;
            var f = new Fire
            {
                Id = nextFireId++, BuildingId = b.Id, Position = b.Position, StartTick = now,
                Intensity = intensity, PeakIntensity = intensity, SourceFireId = sourceFireId, Water = WaterFactor(b),
            };
            fires.Add(f);
            fireByBuilding[b.Id] = f;
            Today.Fires++;
            if (sourceFireId != 0) Today.Spread++;
            Version++;
            return f;
        }

        // ------------------------------------------------------------ Verlauf

        void UpdateFires(long now)
        {
            if (fires.Count == 0) return;
            var snapshot = fires.ToArray(); // Übergreifen fügt neue Brände hinzu (die laufen erst ab dem nächsten Tick)
            foreach (var f in snapshot)
            {
                if (!fireByBuilding.ContainsKey(f.BuildingId)) continue;
                var b = city.GetBuilding(f.BuildingId);
                if (b == null) { EndFire(f, null, FireOutcome.Removed, notify: false); continue; }
                StepFire(f, b, now);
            }
        }

        void StepFire(Fire f, Building b, long now)
        {
            bool vehiclesOn = vehicles != null && vehicles.Enabled;
            int present = -1;
            // Notruf: mit Menschen im Haus sofort, sonst etwas später (Nachbarn, Rauchmelder)
            if (!f.IsReported)
            {
                present = citizens != null ? citizens.PresentIn(b.Id) : 0;
                int delay = present > 0 ? SafetyRules.DetectTicksOccupied : SafetyRules.DetectTicksEmpty;
                if (now - f.StartTick >= delay) Report(f, b, now);
            }

            // Löschfahrzeuge vor Ort / unterwegs
            int onSite = 0, enRoute = 0;
            if (vehiclesOn)
            {
                foreach (var u in vehicles.UnitsAt(b.Id, ServiceMission.FireCall))
                {
                    if (u.State == ServiceUnitState.Working) onSite++;
                    else enRoute++;
                }
            }
            else if (f.FallbackArrivalTick >= 0)
            {
                if (now >= f.FallbackArrivalTick) onSite = 1; else enRoute = 1;
            }
            if (onSite > 0 && f.FirstArrivalTick < 0) { f.FirstArrivalTick = now; f.FirstArrivalTime = world.Traffic.Time; }
            f.TrucksOnSite = onSite;
            f.TrucksEnRoute = enRoute;
            f.Water = WaterFactor(b);

            // Entstehungsbrand: Menschen im Haus löschen ihn manchmal selbst
            if (onSite == 0 && f.Intensity < SafetyRules.SelfExtinguishBelow)
            {
                if (present < 0) present = citizens != null ? citizens.PresentIn(b.Id) : 0;
                if (present > 0 && SafetyRules.Roll(f.Id, now, 11) < SafetyRules.SelfExtinguishPerTick)
                {
                    EndFire(f, b, FireOutcome.SelfExtinguished, notify: true);
                    return;
                }
            }

            // Wachsen, Löschen, Schaden
            float flamm = SafetyRules.Flammability(b.Zone, RecipeOf(b));
            float i = f.Intensity;
            float growth = (SafetyRules.GrowthPerHour * flamm * i + SafetyRules.BaseGrowthPerHour) * (1f - i);
            float suppression = onSite * SafetyRules.SuppressionPerTruckHour * f.Water;
            i = Math.Clamp(i + (growth - suppression) * Dt, 0f, 1f);
            f.Intensity = i;
            if (i > f.PeakIntensity) f.PeakIntensity = i;
            float dmg = SafetyRules.DamagePerHour * i * Dt;
            f.Damage = MathF.Min(1f, f.Damage + dmg);
            b.Condition = MathF.Max(SafetyRules.MinCondition, b.Condition - dmg * SafetyRules.ConditionLossPerDamage);

            if (f.Damage >= 1f) { EndFire(f, b, FireOutcome.TotalLoss, notify: true); return; }
            if (i <= SafetyRules.OutBelow) { EndFire(f, b, onSite > 0 ? FireOutcome.Extinguished : FireOutcome.SelfExtinguished, notify: true); return; }

            // Verletzte (zweite Runde, wenn das Feuer groß wird), Schlagzeile
            if (!f.SecondInjuryRoll && i >= 0.5f)
            {
                f.SecondInjuryRoll = true;
                RollInjuries(f, b, now);
            }
            if (!f.Headlined && f.IsReported && i >= 0.4f)
            {
                f.Headlined = true;
                Headline(HeadlineTopic.Fire, "fire" + b.Id, ("Straße", StreetName(b)), ("Name", BuildingName(b)),
                    ("Anzahl", Math.Max(1, onSite + enRoute).ToString()));
            }

            // Alarmierung: je nach Stärke 1–3 Löschfahrzeuge
            if (f.IsReported) Dispatch(f, b, now, onSite + enRoute);

            // Übergreifen auf Nachbarn
            if (i >= SafetyRules.SpreadFrom) Spread(f, b, now, onSite);
        }

        void Report(Fire f, Building b, long now)
        {
            f.ReportTick = now;
            f.ReportTime = world.Traffic.Time;
            Version++;
            if (vehicles == null || !vehicles.Enabled)
            {
                // Rückfall ohne Dienstfahrzeuge: geschätzte Fahrzeit der zuständigen (sonst schnellsten) Feuerwache
                float best = float.PositiveInfinity;
                foreach (var st in services.Facilities)
                {
                    if (st.Kind != ServiceKinds.Fire) continue;
                    float s = services.ResponseSeconds(st, b);
                    if (s <= st.Type.RangeSeconds * 1.5f && s < best) best = s;
                }
                if (!float.IsInfinity(best)) f.FallbackArrivalTick = now + SafetyRules.FallbackAlarmTicks + TimeModel.TicksFor(best);
            }
            RollInjuries(f, b, now);
            string name = BuildingName(b);
            Post(true, $"Feuer: {name} an der {StreetName(b)}", FireStatusText(f), b.Id, "fire-" + f.Id);
            // Wer drin ist, erlebt es (Evakuierung); keine Verbrauch von Zufall
            if (citizens != null)
                foreach (var c in citizens.PresentCitizens(b.Id)) citizens.RememberEvent(c, MemoryKind.Fire, 0.6f, b);
        }

        string FireStatusText(Fire f)
        {
            var b = city.GetBuilding(f.BuildingId);
            bool any = false;
            foreach (var st in services.Facilities) if (st.Kind == ServiceKinds.Fire) { any = true; break; }
            if (!any) return "Keine Feuerwache in der Stadt – das Feuer brennt, bis es ausgeht.";
            return b != null && WaterFactor(b) < 1f
                ? "Feuerwehr alarmiert. Kein Wasseranschluss am Gebäude – Löschen nur mit Tankwasser."
                : "Feuerwehr alarmiert.";
        }

        void Dispatch(Fire f, Building b, long now, int assigned)
        {
            if (vehicles == null || !vehicles.Enabled) return;
            int want = f.Intensity >= SafetyRules.ThirdTruckAt ? 3 : f.Intensity >= SafetyRules.SecondTruckAt ? 2 : 1;
            while (assigned < want)
            {
                var u = vehicles.Dispatch(ServiceMission.FireCall, b, now);
                if (u == null)
                {
                    f.MissedDispatches++;
                    if (assigned == 0 && f.MissedDispatches == 1) Today.MissedFireDispatches++;
                    return;
                }
                f.TrucksSent++;
                assigned++;
                Version++;
            }
        }

        void RollInjuries(Fire f, Building b, long now)
        {
            if (citizens == null || f.Injured >= SafetyRules.MaxInjuredPerFire) return;
            var present = citizens.PresentCitizens(b.Id);
            float p = SafetyRules.InjuryBase + SafetyRules.InjuryPerIntensity * f.Intensity;
            foreach (var c in present)
            {
                if (f.Injured >= SafetyRules.MaxInjuredPerFire) break;
                if (SafetyRules.Roll(c.Id, now, 17 + f.Id % 7) >= p) continue;
                citizens.Injure(c, SafetyRules.InjurySeverityBase + SafetyRules.InjurySeverityPerIntensity * f.Intensity, b);
                f.Injured++;
                Today.Injured++;
                if (vehicles != null && vehicles.Enabled) vehicles.Dispatch(ServiceMission.Rescue, b, now);
            }
        }

        void Spread(Fire f, Building b, long now, int onSite)
        {
            float strength = (f.Intensity - SafetyRules.SpreadFrom) / (1f - SafetyRules.SpreadFrom);
            if (strength <= 0f) return;
            float reach = SafetyRules.SpreadDistance + MathF.Max(b.Width, b.Depth) * 0.75f + 24f;
            foreach (var n in SortedBuildings())
            {
                if (n.Id == b.Id || !n.IsConstructed || n.OutOfService || fireByBuilding.ContainsKey(n.Id)) continue;
                if (MathF.Abs(n.Position.X - b.Position.X) > reach || MathF.Abs(n.Position.Y - b.Position.Y) > reach) continue;
                float d = EdgeDistance(b, n);
                if (d >= SafetyRules.SpreadDistance) continue;
                float p = SafetyRules.SpreadPerTick * strength * (1f - d / SafetyRules.SpreadDistance) * SafetyRules.Flammability(n.Zone, RecipeOf(n));
                if (onSite > 0) p *= SafetyRules.SpreadWithTrucks;
                if (SafetyRules.Roll(n.Id, now, 23 + f.Id % 5) >= p) continue;
                var spread = Ignite(n, now, f.Id);
                if (spread != null) Post(true, $"Feuer greift über: {BuildingName(n)}", $"Von {BuildingName(b)} an der {StreetName(b)}.", n.Id, "fire-" + spread.Id);
            }
        }

        /// <summary>Abstand zweier Grundflächen (Kante zu Kante, Näherung über die Ecken).</summary>
        static float EdgeDistance(Building a, Building b)
        {
            float best = float.MaxValue;
            foreach (var c in Corners(a)) best = MathF.Min(best, RectDistance(c, b));
            foreach (var c in Corners(b)) best = MathF.Min(best, RectDistance(c, a));
            return best;
        }

        static Vector2[] Corners(Building b)
        {
            var side = b.Side * (b.Width * 0.5f);
            var fr = b.Front * (b.Depth * 0.5f);
            return new[] { b.Position - side - fr, b.Position + side - fr, b.Position + side + fr, b.Position - side + fr };
        }

        static float RectDistance(Vector2 p, Building b)
        {
            var d = p - b.Position;
            float x = MathF.Max(0f, MathF.Abs(Vector2.Dot(d, b.Side)) - b.Width * 0.5f);
            float y = MathF.Max(0f, MathF.Abs(Vector2.Dot(d, b.Front)) - b.Depth * 0.5f);
            return MathF.Sqrt(x * x + y * y);
        }

        /// <summary>Löschfahrzeug bleibt am Brand, solange er brennt (Hook im <see cref="ServiceVehicleSystem"/>).</summary>
        bool HoldTruck(ServiceUnit u) => u.Mission == ServiceMission.FireCall && fireByBuilding.ContainsKey(u.TargetBuildingId);

        // ------------------------------------------------------------ Ende und Folgen

        void EndFire(Fire f, Building b, FireOutcome outcome, bool notify)
        {
            fires.Remove(f);
            fireByBuilding.Remove(f.BuildingId);
            Version++;
            var rec = new FireRecord
            {
                FireId = f.Id, BuildingId = f.BuildingId, Street = b?.FrontSegmentId ?? 0, Position = f.Position,
                StartTick = f.StartTick, EndTick = clock.Tick, Damage = f.Damage, PeakIntensity = f.PeakIntensity, Outcome = outcome,
                Injured = f.Injured, Spread = f.SourceFireId != 0, BuildingName = BuildingName(b),
                ResponseMinutes = f.ResponseMinutes,
            };
            fireHistory.Insert(0, rec);
            if (fireHistory.Count > MaxHistory) fireHistory.RemoveAt(fireHistory.Count - 1);
            if (rec.ResponseMinutes >= 0f) { Today.ResponseMinutesSum += rec.ResponseMinutes; Today.ResponseCount++; }
            switch (outcome)
            {
                case FireOutcome.SelfExtinguished: Today.SelfExtinguished++; break;
                case FireOutcome.Extinguished: Today.Extinguished++; break;
                case FireOutcome.TotalLoss: Today.TotalLosses++; break;
            }
            if (b == null || outcome == FireOutcome.Removed) return;

            // Lager der Firma brennt anteilig mit
            var firm = world.Goods?.FirmAt(b.Id);
            if (firm != null && f.Damage > 0f)
            {
                float keep = Math.Clamp(1f - f.Damage * SafetyRules.StockLossPerDamage, 0f, 1f);
                for (int g = 0; g < firm.OutputStock.Length; g++)
                {
                    firm.OutputStock[g] *= keep;
                    firm.InputStock[g] *= keep;
                    firm.InputValue[g] *= keep;
                }
            }

            string name = BuildingName(b), street = StreetName(b);
            if (citizens != null && f.PeakIntensity >= 0.3f)
                citizens.RememberAround(b.Position, SafetyRules.NeighborMemoryRadius, b.Id, MemoryKind.Fire, 0.25f, b);

            if (outcome == FireOutcome.TotalLoss)
            {
                Headline(HeadlineTopic.FireRuin, "ruin" + b.Id, ("Straße", street), ("Name", name));
                if (citizens != null)
                    foreach (var c in citizens.ResidentsOf(b.Id)) citizens.RememberEvent(c, MemoryKind.Fire, 1f, b);
                if (b.IsPlayerPlaced)
                {
                    // Entscheidung: gesetzte Gebäude bleiben als Brandruine stehen – Wiederaufbau gegen Geld oder Abriss durch den Spieler
                    var repair = new FireRepair { BuildingId = b.Id, UntilTick = -1, Ruin = true };
                    TakeOutOfService(b, repair);
                    b.Condition = SafetyRules.MinCondition;
                    Post(true, $"{name} ausgebrannt", $"Brandruine an der {street}. Wiederaufbau im Inspektor ({RebuildCost(b):N0} €) oder abreißen.", b.Id, "fire-" + f.Id);
                }
                else
                {
                    Post(true, $"{name} ausgebrannt", $"Das Gebäude an der {street} ist nicht mehr zu retten und wird abgerissen.", b.Id, "fire-" + f.Id);
                    city.RemoveBuilding(b); // Zonengebäude: Abriss, die Zone wird neu bebaut
                }
                return;
            }
            if (f.Damage >= SafetyRules.SevereDamage)
            {
                Today.SevereDamage++;
                var repair = new FireRepair { BuildingId = b.Id, UntilTick = clock.Tick + SafetyRules.TicksForDays(SafetyRules.RepairBaseDays + SafetyRules.RepairDaysPerDamage * f.Damage) };
                TakeOutOfService(b, repair);
                float days = (repair.UntilTick - clock.Tick) / (float)TimeModel.TicksPerDay;
                Post(true, $"Brand gelöscht – {name} unbewohnbar",
                    $"Schaden {f.Damage * 100f:0} %. Sanierung ≈ {days:0.#} Tage, {repair.Households.Count} Haushalte ziehen vorübergehend aus.", b.Id, "fire-" + f.Id);
                return;
            }
            if (notify)
                Post(false, outcome == FireOutcome.SelfExtinguished ? $"Kleiner Brand gelöscht: {name}" : $"Brand gelöscht: {name}",
                    outcome == FireOutcome.SelfExtinguished ? $"Die Bewohner an der {street} haben ihn selbst erstickt."
                        : $"Schaden {f.Damage * 100f:0} %" + (rec.ResponseMinutes >= 0f ? $", Feuerwehr nach {rec.ResponseMinutes:0} min vor Ort." : "."),
                    b.Id, "fire-" + f.Id);
        }

        void TakeOutOfService(Building b, FireRepair repair)
        {
            if (citizens != null) repair.Households.AddRange(citizens.Vacate(b));
            b.OutOfService = true;
            b.Construction = 0f;
            repairs[b.Id] = repair;
            city.NotifyBuildingChanged(b);
            Version++;
        }

        void UpdateRepairs(long now)
        {
            if (repairs.Count == 0) return;
            List<FireRepair> done = null;
            foreach (var r in repairs.Values)
                if (r.UntilTick >= 0 && now >= r.UntilTick) (done ??= new List<FireRepair>()).Add(r);
            if (done == null) return;
            foreach (var r in done)
            {
                repairs.Remove(r.BuildingId);
                var b = city.GetBuilding(r.BuildingId);
                if (b == null) continue;
                b.OutOfService = false;
                b.Construction = 1f;
                b.Condition = MathF.Max(b.Condition, r.Ruin ? 1f : 0.85f);
                city.NotifyBuildingChanged(b);
                int back = citizens != null ? citizens.ReturnHouseholds(b, r.Households) : 0;
                Post(false, r.Ruin ? $"Wiederaufgebaut: {BuildingName(b)}" : $"Saniert: {BuildingName(b)}",
                    back > 0 ? $"{back} Haushalte ziehen zurück an die {StreetName(b)}." : $"An der {StreetName(b)} kann wieder eingezogen werden.", b.Id, "repair-" + b.Id);
                Version++;
            }
        }

        // ------------------------------------------------------------ Wiederaufbau (Spieleraktion)

        /// <summary>Kosten für den Wiederaufbau einer Brandruine (Anteil der Baukosten der Bauart, Rest trägt die Versicherung).</summary>
        public float RebuildCost(Building b)
        {
            var type = HouseCatalog.Of(b);
            return type == null ? 0f : type.Cost * SafetyRules.RebuildCostShare;
        }

        /// <summary>Kann die Ruine wieder aufgebaut werden (Ruine, Geld reicht)?</summary>
        public bool CanRebuild(int buildingId)
        {
            var b = city.GetBuilding(buildingId);
            if (b == null || !repairs.TryGetValue(buildingId, out var r) || !r.IsRuin) return false;
            var budget = world.Economy;
            return budget == null || budget.CanAfford(RebuildCost(b));
        }

        /// <summary>Wiederaufbau einer Brandruine (gesetztes Gebäude): bucht die Kosten („Bau Gebäude (gesetzt)“), Bauzeit ≈ 3 Tage.</summary>
        public bool Rebuild(int buildingId)
        {
            if (!CanRebuild(buildingId)) return false;
            var b = city.GetBuilding(buildingId);
            var r = repairs[buildingId];
            world.Economy?.Spend(Economy.BudgetCategory.HouseConstruction, RebuildCost(b));
            r.UntilTick = clock.Tick + SafetyRules.TicksForDays(SafetyRules.RebuildDays);
            Version++;
            return true;
        }

        readonly List<Building> sortedBuildings = new List<Building>();
        int sortedCityVersion = -1;
        long sortedTick = -1;

        /// <summary>Alle Gebäude nach Id (je Tick bzw. Stadt-Version einmal sortiert; nicht speichern – abgeleitet).</summary>
        List<Building> SortedBuildings()
        {
            if (sortedCityVersion != city.Version || sortedTick != clock.Tick)
            {
                sortedBuildings.Clear();
                sortedBuildings.AddRange(city.Buildings);
                sortedBuildings.Sort((a, b) => a.Id.CompareTo(b.Id));
                sortedCityVersion = city.Version;
                sortedTick = clock.Tick;
            }
            return new List<Building>(sortedBuildings);
        }
    }
}
