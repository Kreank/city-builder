using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Eigene Assets 2 (Energie erweitert): Rohstoffbasis der <b>Biogasanlagen</b> = Bauernhöfe (Rezept „farm“) mit ihren
    /// Beschäftigten. Bewusst eine <b>Bedarfs-Anbindung</b> statt Warenfluss: Mais-Silage/Gülle sind noch keine Güter im
    /// <see cref="GoodsCatalog"/>, die Höfe verlieren nichts (Reststoffe, Zwischenfrucht). Offen: eigenes Gut „Biomasse“ mit
    /// Lieferungen per Traktor/Lkw und Erlös für die Höfe.
    /// </summary>
    public static class FarmFeedstock
    {
        /// <summary>Rezept-Id der Bauernhöfe.</summary>
        public const string FarmRecipe = "farm";

        /// <summary>Alle laufenden Bauernhöfe mit Lage (Gebäudemitte) und Beschäftigten, in Firmen-Reihenfolge.</summary>
        public static IEnumerable<(Vector2 Position, float Workers)> Sources(GoodsSystem goods)
        {
            if (goods == null) yield break;
            foreach (var f in goods.Firms)
            {
                if (f.Recipe == null || f.Recipe.Id != FarmRecipe || f.Building == null) continue;
                yield return (f.Building.Position, f.Workers);
            }
        }
    }
}
