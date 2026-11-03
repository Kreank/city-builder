using System;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Standortwahl der Industrie: eine Unterart (Rezept) je Grundstück nach Vorkommen am Ort und Nachfrage
    /// (geplanter Bedarf gegen geplante Kapazität aller Firmen, lokaler Preis, Weltmarkt).
    /// </summary>
    public sealed partial class GoodsSystem
    {
        /// <summary>Bewertung eines Industrie-Rezepts für ein Grundstück (höher = besser, −∞ = nicht möglich).</summary>
        public float SiteScore(Building b, Recipe r)
        {
            float depositFactor = 1f, depositBonus = 0f;
            if (r.Deposit != null)
            {
                float q = DepositAt?.Invoke(r.Deposit, b.Position) ?? 0f;
                if (q < r.MinDeposit) return float.NegativeInfinity;
                // Rohstoff vor Ort ist ein Standortvorteil (reiche Vorkommen werden fast immer genutzt)
                depositFactor = 0.6f + q;
                depositBonus = 0.2f * q;
            }
            float need = 0f, value = 0f;
            foreach (var o in r.Outputs)
            {
                float v = o.PerJob * o.Good.BasePrice;
                need += v * NeedOf(markets[o.Good.Index]);
                value += v;
            }
            if (value > 0f) need /= value;
            // Kleine, stabile Streuung je Grundstück und Rezept (kein Zufallsstrom → unabhängig von der Reihenfolge)
            float jitter = (SimRandom.Hash(b.Id * 131 + seed, r.Index) & 0xFFFF) / 65535f * 0.25f;
            return need * depositFactor + depositBonus + jitter;
        }

        /// <summary>Wie dringend die Stadt mehr von diesem Gut herstellen sollte.</summary>
        static float NeedOf(GoodMarket m)
        {
            float demand = m.PlannedDemandPerDay;
            float shortfall = demand > 0f ? Math.Clamp((demand - m.CapacityPerDay) / MathF.Max(demand, 1f), -1f, 1f) : m.CapacityPerDay > 0f ? -1f : 0f;
            float price = m.PriceIndex - 1f;
            float world = m.WorldPrice / m.Good.BasePrice - 1f;
            return 0.25f + MathF.Max(0f, shortfall) * 1.5f + MathF.Min(0f, shortfall) * 0.3f + 0.5f * price + 0.3f * world;
        }

        Recipe ChooseIndustry(Building b, Recipe exclude)
        {
            UpdatePlanned(); // aktuelle Firmenliste inkl. der heute schon gegründeten
            Recipe best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var r in RecipeCatalog.Industry)
            {
                if (r == exclude) continue;
                float s = SiteScore(b, r);
                if (s > bestScore) { bestScore = s; best = r; }
            }
            // Ohne jedes Vorkommen und ohne Daten: Verarbeitung/Fertigung geht immer
            return best ?? RecipeCatalog.Get("food_factory");
        }
    }
}
