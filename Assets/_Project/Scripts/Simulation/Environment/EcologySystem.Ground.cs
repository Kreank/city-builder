using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>Fehler beim Bäume-Pflanzen.</summary>
    public enum TreePlantError { None, OutOfWorld, NothingFree, InsufficientFunds }

    /// <summary>Vorschau einer Aufforstung (Zellen im Pinsel, die frei sind und noch Platz für Bäume haben).</summary>
    public sealed class TreePlan
    {
        public Vector2 Center;
        public float Radius;
        public readonly List<int> Cells = new List<int>();
        public float Trees;
        public double Cost;
        public TreePlantError Error;
        public bool IsValid => Error == TreePlantError.None;
    }

    /// <summary>Umwelt: Boden, Grundwasser, chronische Belastung (täglich) und Bäume pflanzen.</summary>
    public sealed partial class EcologySystem
    {
        void Daily()
        {
            GrowPlanted();
            Array.Copy(emissionToday, emissionYesterday, emissionToday.Length);
            Array.Clear(emissionToday, 0, emissionToday.Length);
            if (!Enabled) { dayAirPasses = 0; Array.Clear(dayAirSum, 0, dayAirSum.Length); return; }
            UpdateExposureAndDeposition();
            EmitGround();
            SettleGround();
            DetectFishKill();
            Version++;
        }

        void UpdateExposureAndDeposition()
        {
            var exp = Exposure.RawValues;
            var soil = Soil.RawValues;
            float inv = dayAirPasses > 0 ? 1f / dayAirPasses : 0f;
            for (int i = 0; i < exp.Length; i++)
            {
                float avg = dayAirSum[i] * inv;
                exp[i] += (avg - exp[i]) * EcologyRules.ExposureSmoothing;
                if (exp[i] < 1e-6f) exp[i] = 0f;
                soil[i] += EcologyRules.SoilFromAir * avg;
                dayAirSum[i] = 0f;
            }
            dayAirPasses = 0;
            Exposure.MarkChanged();
        }

        void EmitGround()
        {
            float filter = EcologyRules.FilterFactor(Regulation);
            var goods = world.Goods;
            if (goods != null)
            {
                var firms = goods.Firms;
                for (int k = 0; k < firms.Count; k++)
                {
                    var f = firms[k];
                    if (f.Recipe == null || f.Building == null || !f.Building.IsConstructed) continue;
                    EcologyRules.GroundPerJob(f.Recipe.Id, out float s, out float g);
                    if (s <= 0f && g <= 0f) continue;
                    float act = Activity(f) * f.Jobs * filter;
                    var b = f.Building;
                    float r = MathF.Max(b.Width, b.Depth) * 0.5f + 16f;
                    if (s > 0f) Soil.AddRadial(b.Position, r, s * act);
                    if (g > 0f) Groundwater.AddRadial(b.Position, r, g * act);
                }
            }
            if (world.Services != null)
            {
                serviceBuffer.Clear();
                foreach (var f in world.Services.Facilities) if (f.Kind == ServiceKinds.Garbage) serviceBuffer.Add(f);
                serviceBuffer.Sort((a, b) => a.Id.CompareTo(b.Id));
                foreach (var f in serviceBuffer)
                {
                    float r = MathF.Max(f.Type.Width, f.Type.Depth) * 0.5f;
                    if (f.Type.Storage > 0f) // Müllhalde: Sickerwasser, Boden – mit dem Füllstand
                    {
                        float fill = 0.3f + 0.7f * Math.Clamp(f.Stored / f.Type.Storage, 0f, 1f);
                        Soil.AddRadial(f.Position, r + 30f, EcologyRules.LandfillSoil * fill);
                        Groundwater.AddRadial(f.Position, r + EcologyRules.LandfillGroundwaterRadius, EcologyRules.LandfillGroundwater * fill * filter);
                    }
                    else if (f.Type.Pollution > 0f) // Verbrennung: Asche/Schlacke
                        Soil.AddRadial(f.Position, r + 16f, EcologyRules.IncineratorSoil * filter);
                }
            }
            var wq = world.WaterQuality;
            if (wq != null && world.Utilities != null)
            {
                plantBuffer.Clear();
                foreach (var f in world.Utilities.Facilities) if (f.Type.Untreated) plantBuffer.Add(f);
                plantBuffer.Sort((a, b) => a.Id.CompareTo(b.Id));
                foreach (var f in plantBuffer)
                {
                    float d = wq.DischargeOf(f);
                    if (d > 0f) Soil.AddRadial(f.Position, EcologyRules.OutletRadius, EcologyRules.OutletSoil * MathF.Min(1f, d / 500f));
                }
            }
        }

        void SettleGround()
        {
            var soil = Soil.RawValues;
            var gw = Groundwater.RawValues;
            var wood = world.Resources?.Layer(ResourceIds.Wood);
            bool sameGrid = wood != null && wood.Width == width && wood.Height == height;
            for (int i = 0; i < soil.Length; i++) gw[i] += EcologyRules.Leaching * soil[i];
            Groundwater.Diffuse(EcologyRules.GroundwaterSpread);
            for (int i = 0; i < soil.Length; i++)
            {
                float rec = EcologyRules.SoilRecovery * (sameGrid && wood.RawValues[i] > 0.3f ? EcologyRules.ForestSoilBonus : 1f);
                float s = Math.Clamp(soil[i] * (1f - rec), 0f, 1f);
                soil[i] = s < 1e-6f ? 0f : s;
                float g = Math.Clamp(gw[i] * (1f - EcologyRules.GroundwaterRecovery), 0f, 1f);
                gw[i] = g < 1e-6f ? 0f : g;
            }
            Soil.MarkChanged();
            Groundwater.MarkChanged();
        }

        bool fishKillActive;

        void DetectFishKill()
        {
            var wq = world.WaterQuality;
            if (wq == null) return;
            float max = wq.Pollution.MaxValue();
            bool kill = max >= EcologyRules.FishKillThreshold;
            if (kill && !fishKillActive)
            {
                FishKills++;
                world.Media?.Report(Services.Content.HeadlineTopic.FishKill, "fish" + world.Clock.Day,
                    ("Prozent", ((int)MathF.Round(100f * Math.Min(1f, max))).ToString()));
            }
            fishKillActive = max >= EcologyRules.FishKillThreshold * 0.7f && (kill || fishKillActive);
        }

        // ------------------------------------------------------------ Bäume pflanzen

        /// <summary>Freie Zellen im Kreis (nicht Wasser, keine Straße/Gebäude/Bauwerke, noch nicht dicht bewaldet), Kosten.</summary>
        public TreePlan PlanTrees(Vector2 center, float radius)
        {
            radius = Math.Clamp(radius, 4f, EcologyRules.MaxPlantRadius);
            var plan = new TreePlan { Center = center, Radius = radius };
            if (!world.Contains(center)) { plan.Error = TreePlantError.OutOfWorld; return plan; }
            var wood = world.Resources?.Layer(ResourceIds.Wood);
            var min = center - new Vector2(radius + air.CellSize);
            var max = center + new Vector2(radius + air.CellSize);
            var locks = world.Terrain.LockQuery?.Invoke(min, max);
            float density = EcologyRules.PlantedDensity;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var c = air.CellCenter(x, y);
                if (c.X < min.X || c.Y < min.Y || c.X > max.X || c.Y > max.Y) continue;
                if (Vector2.Distance(c, center) > radius) continue;
                if (world.Terrain.IsWater(c)) continue;
                if (locks != null && locks(c) >= 0.5f) continue;
                int i = y * width + x;
                float have = MathF.Max(wood != null ? wood.RawValues[i] : 0f, Planted.RawValues[i]);
                if (have >= density * 0.9f) continue;
                plan.Cells.Add(i);
                plan.Trees += EcologyRules.TreesPerCell * (density - have) / density;
            }
            plan.Trees = MathF.Round(plan.Trees);
            plan.Cost = plan.Trees * EcologyRules.CostPerTree;
            if (plan.Cells.Count == 0) plan.Error = TreePlantError.NothingFree;
            else if (Budget != null && !Budget.CanAfford(plan.Cost)) plan.Error = TreePlantError.InsufficientFunds;
            return plan;
        }

        /// <summary>Pflanzt (Setzlinge, wachsen über ~2 Monate zum Wald) und bucht die Kosten („Umweltschutz“).</summary>
        public bool PlantTrees(TreePlan plan)
        {
            if (plan == null) return false;
            var fresh = PlanTrees(plan.Center, plan.Radius);
            if (!fresh.IsValid) { plan.Error = fresh.Error; return false; }
            Budget?.Spend(BudgetCategory.Environment, fresh.Cost);
            var wood = world.Resources?.Layer(ResourceIds.Wood);
            foreach (int i in fresh.Cells)
            {
                if (Planted.RawValues[i] <= 0f) PlantedCells++;
                Planted.RawValues[i] = EcologyRules.PlantedDensity;
                if (wood != null && wood.RawValues[i] < EcologyRules.SeedlingDensity) wood.RawValues[i] = EcologyRules.SeedlingDensity;
            }
            TreesPlanted += fresh.Trees;
            Planted.MarkChanged();
            wood?.MarkChanged();
            Version++;
            return true;
        }

        /// <summary>
        /// Täglich: Aufforstungen wachsen (Waldraster + <see cref="EcologyRules.PlantGrowthPerDay"/> bis zur Ziel-Dichte); wo
        /// inzwischen eine Straße, ein Gebäude oder Bauwerk steht, endet die Aufforstung (Bäume weg bis auf den Naturwald).
        /// </summary>
        void GrowPlanted()
        {
            if (PlantedCells <= 0) return;
            var res = world.Resources;
            var wood = res?.Layer(ResourceIds.Wood);
            if (wood == null || wood.Width != width) return;
            var planted = Planted.RawValues;
            Vector2 min = new Vector2(float.MaxValue), max = new Vector2(float.MinValue);
            for (int i = 0; i < planted.Length; i++)
            {
                if (planted[i] <= 0f) continue;
                var c = air.CellCenter(i % width, i / width);
                min = Vector2.Min(min, c); max = Vector2.Max(max, c);
            }
            var locks = world.Terrain.LockQuery?.Invoke(min - new Vector2(air.CellSize), max + new Vector2(air.CellSize));
            bool changed = false;
            int count = 0;
            for (int i = 0; i < planted.Length; i++)
            {
                float target = planted[i];
                if (target <= 0f) continue;
                var c = air.CellCenter(i % width, i / width);
                if ((locks != null && locks(c) >= 0.5f) || world.Terrain.IsWater(c))
                {
                    planted[i] = 0f;
                    wood.RawValues[i] = MathF.Min(wood.RawValues[i], res.NaturalAt(ResourceIds.Wood, c));
                    changed = true;
                    continue;
                }
                count++;
                if (wood.RawValues[i] < target)
                {
                    wood.RawValues[i] = MathF.Min(target, wood.RawValues[i] + EcologyRules.PlantGrowthPerDay);
                    changed = true;
                }
            }
            PlantedCells = count;
            if (changed) { wood.MarkChanged(); Planted.MarkChanged(); }
        }
    }
}
