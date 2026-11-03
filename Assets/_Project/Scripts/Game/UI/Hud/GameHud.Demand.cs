using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Setzen statt Zonen: Nachfrage-Anzeige in der oberen Leiste – vier senkrechte Balken W(ohnen) L(aden) B(üro) I(ndustrie)
    /// aus <see cref="CitySystem.Demand"/> + G5-Zuschlägen, Tooltip je Balken mit dem „warum“ (<see cref="DemandReport.Explain"/>).
    /// Klick öffnet die Kategorie „Bauen“ mit dem passenden Reiter. Eigener Block; <c>GameHud.cs</c> bekommt nur eine Zeile.
    /// </summary>
    public sealed partial class GameHud
    {
        VisualElement DemandChip()
        {
            var chip = El("cb-stat");
            var col = Container("cb-col");
            col.Add(Text("Nachfrage", "cb-stat__label"));
            var bars = Container("cb-demand");
            foreach (var kind in DemandReport.All)
            {
                var k = kind;
                var item = El("cb-demand__item");
                var bar = Container("cb-demand__bar");
                var fill = Container("cb-demand__fill");
                fill.style.backgroundColor = BuildLabels.ZoneColor(ZoneOf(k));
                bar.Add(fill);
                item.Add(bar);
                item.Add(Text(DemandReport.ShortName(k), "cb-demand__label"));
                slowRefresh.Add(() =>
                {
                    var w = runner.World;
                    float v = w == null ? 0f : Mathf.Clamp(DemandReport.ValueOf(w.City, k), 0f, 100f);
                    fill.style.height = Length.Percent(Mathf.Max(4f, v));
                });
                Tip(item, () => runner.World == null ? DemandReport.NameOf(k) : DemandReport.Explain(runner.World, k).Tooltip +
                                "\nKlick: Bauen → " + (k == DemandKind.Residential ? "Wohnen" : k == DemandKind.Industry ? "Industrie" : "Gewerbe"));
                OnClick(item, () => OpenBuildTab(k));
                bars.Add(item);
            }
            col.Add(bars);
            chip.Add(col);
            Tip(chip, () => "Nachfrage je Art (Balken) · Maus auf einen Balken: warum");
            return chip;
        }

        static ZoneType ZoneOf(DemandKind k) =>
            k == DemandKind.Residential ? ZoneType.ResidentialLow : k == DemandKind.Shop ? ZoneType.Retail
            : k == DemandKind.Office ? ZoneType.Office : ZoneType.Industry;

        void OpenBuildTab(DemandKind k)
        {
            var ht = Houses;
            if (ht == null) return;
            HouseType type = k == DemandKind.Residential ? HouseCatalog.Low : k == DemandKind.Shop ? HouseCatalog.Shop
                : k == DemandKind.Office ? HouseCatalog.OfficeBuilding : HouseCatalog.SmallHall;
            ht.SelectType(type);
        }
    }
}
