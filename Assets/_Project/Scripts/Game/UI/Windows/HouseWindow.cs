using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Game.Zoning;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Wohnhäuser (rechtes Panel, offen im Haus-Werkzeug 6): Anno-Gefühl beim Platzieren – je Stufe die Anforderungen mit ✓/✗
    /// am Mauszeiger (aus <c>LandValueSystem.PreviewHouse</c>), erreichbare Stufe, Klick auf eine Einrichtung zeigt deren
    /// Reichweite (Abdeckungs-Ansicht bzw. Ring der Fußweite beim Laden). Dazu Bauart und Bestand der gesetzten Häuser.
    /// </summary>
    public sealed class HouseWindow : GameWindow
    {
        HouseTool houseTool;

        public HouseWindow() : base(WindowSlot.Right) { }

        HouseTool Ht => houseTool != null ? houseTool : houseTool = Tool.GetComponent<HouseTool>();

        public override float OpenScore => Tool.CurrentMode == RoadTool.Mode.House && Ht != null ? 90f : 0f;

        public override bool HandleEscape() => false; // Esc: zurück zum Auswählen (RoadTool)

        public override void DrawHead(Ui ui)
        {
            var t = Ht.SelectedType;
            Head(ui, "Wohnhäuser (6)", Tone.Info, Ht.Current == HouseTool.HouseAction.Remove ? "Abreißen" : t.DisplayName,
                $"{World.City.PlayerHouseCount} gesetzt · Stufen nach Einrichtungen in Reichweite");
        }

        public override void DrawBody(Ui ui)
        {
            var ht = Ht;
            if (ht.Current == HouseTool.HouseAction.Remove)
            {
                ui.Note("Gesetztes Wohnhaus anklicken: 25 % der Baukosten zurück, die Bewohner ziehen um oder weg.");
                Stock(ui);
                return;
            }

            var preview = ht.Preview;
            ui.Section("Am Mauszeiger", preview != null ? $"erreichbare Stufe {preview.ReachableLevel} von {HouseRules.MaxLevel}" : null);
            if (preview == null || !Tool.HasCursor) ui.Note("Maus an eine Stadtstraße bewegen – hier steht dann, was an dieser Stelle erfüllt wäre.");
            else
            {
                if (ht.Plan != null && !ht.Plan.IsValid) ui.Message("Hier nicht baubar", HouseTool.ErrorText(ht.Plan), Tone.Warn);
                foreach (var rule in HouseRules.Levels)
                {
                    bool reached = preview.ReachableLevel >= rule.Level;
                    ui.Text($"Stufe {rule.Level} – {rule.Name}", reached ? Tone.Good : Tone.None);
                    using (ui.Fields())
                        foreach (var s in preview.Needs)
                        {
                            if (System.Array.IndexOf(rule.Needs, s.Need) < 0) continue;
                            var need = s.Need;
                            bool shown = ht.ShownNeed == need;
                            bool hasReach = need.Kind == HouseNeedKind.Service || need.Kind == HouseNeedKind.Shop;
                            string label = $"{s.Mark} {need.Label}" + (shown ? " ◂" : "");
                            if (hasReach) ui.FieldLink(label, s.Detail, () => ht.FocusNeed = ht.FocusNeed == need ? null : need, s.Met ? Tone.Good : Tone.Warn);
                            else ui.Field(label, s.Detail, s.Met ? Tone.Good : Tone.Warn);
                        }
                }
                ui.Note($"Außerdem (weich, nach dem Einzug): Bewohner zufrieden (≥ {HouseRules.Levels[0].Satisfaction * 100f:0} % bzw. " +
                        $"{HouseRules.Levels[HouseRules.Levels.Length - 1].Satisfaction * 100f:0} %) und Wohnungen zu {HouseRules.Levels[0].Occupancy * 100f:0} % belegt. " +
                        "Fehlt eine Anforderung der eigenen Stufe, steigt das Haus nach einigen Tagen ab.");
                ui.Note("Klick auf eine Einrichtung zeigt ihre Reichweite (Abdeckung über den Häusern, Laden: Ring der Fußweite). " +
                        "Ohne Auswahl wird die erste fehlende gezeigt.");
            }

            var t = ht.SelectedType;
            ui.Section("Bauart", t.DisplayName);
            using (ui.Fields())
            {
                ui.Field("Zone (Modelle, Wohnkomfort)", Zones.DisplayName(t.Zone));
                ui.Field("Grundstück", $"{t.WidthCells}×{t.DepthCells} Zellen ({t.Width:0}×{t.Depth:0} m)");
                ui.Field("Wohnungen je Stufe", $"{t.HouseholdsAtLevel(1)} / {t.HouseholdsAtLevel(2)} / {t.HouseholdsAtLevel(3)}");
                ui.Field("Baukosten", BuildLabels.Money(t.Cost) + " (Abriss: 25 % zurück)");
            }
            Stock(ui);
        }

        void Stock(Ui ui)
        {
            int[] perLevel = new int[HouseRules.MaxLevel + 1];
            int residents = 0;
            foreach (var b in World.City.Buildings)
            {
                if (!b.IsPlayerPlaced) continue;
                perLevel[System.Math.Clamp(b.Level, 1, HouseRules.MaxLevel)]++;
                residents += b.Residents;
            }
            ui.Section("Deine Häuser", $"{World.City.PlayerHouseCount} gesetzt · {residents} Bewohner");
            using (ui.Fields())
                for (int l = 1; l <= HouseRules.MaxLevel; l++)
                    ui.Field($"Stufe {l} ({HouseRules.LevelName(l)})", perLevel[l].ToString());
        }
    }
}
