using System;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// G5 Warenketten: Anschluss des Bürgersystems an das Warensystem (<c>Goods.GoodsSystem</c>), verbunden in
    /// <c>Goods.GoodsBinding.Connect</c>. Ohne Bindung (reine Tests) gilt der alte Ablauf: jeder Einkauf gelingt.
    /// </summary>
    public sealed partial class CitizenSystem
    {
        /// <summary>
        /// Einkauf im Laden: (Haushalt, Laden-Gebäude-Id) → Erfüllung 0..1. 1 = alles bekommen, 0 = Laden leer.
        /// null = jeder Einkauf gelingt (vor G5).
        /// </summary>
        public Func<Household, int, float> Purchase { get; set; }

        /// <summary>Hat der Laden (Gebäude-Id) Grundbedarf vorrätig? Leere Läden werden bei der Ladenwahl übersprungen. null = alle.</summary>
        public Func<int, bool> ShopHasStock { get; set; }

        /// <summary>Löhne je Tag aller Beschäftigten eines Gebäudes (brutto, wie <see cref="Household.Income"/>).</summary>
        public double WageBillOf(int buildingId)
        {
            double sum = 0;
            foreach (var c in WorkersAt(buildingId)) sum += DailyIncome(c);
            return sum;
        }

        /// <summary>Beschäftigte eines Gebäudes je Bildungsstufe: [0] ohne/Grundschule, [1] mittlerer Abschluss, [2] Studium.</summary>
        public void WorkforceOf(int buildingId, int[] counts)
        {
            if (counts == null || counts.Length < 3) throw new ArgumentException("Mindestens 3 Felder.", nameof(counts));
            counts[0] = counts[1] = counts[2] = 0;
            foreach (var c in WorkersAt(buildingId))
            {
                if (c.Education >= Education.Higher) counts[2]++;
                else if (c.Education >= Education.Secondary) counts[1]++;
                else counts[0]++;
            }
        }

        /// <summary>
        /// Neuer Einkaufsbedarf nach einem Einkauf mit Erfüllung <paramref name="fulfilment"/>: voll = 0, leer = knapp unter 1
        /// (kein sofortiger neuer Versuch, das Bedürfnis „Einkauf“ bleibt aber niedrig; am nächsten Tag geht es erneut los).
        /// </summary>
        internal static float ShoppingNeedAfter(float need, float fulfilment) =>
            MathF.Min(0.99f, need) * (1f - Math.Clamp(fulfilment, 0f, 1f));
    }
}
