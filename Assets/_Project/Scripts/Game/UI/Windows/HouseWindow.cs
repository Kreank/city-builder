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
            Head(ui, "Bauen (6)", Tone.Info, Ht.Current == HouseTool.HouseAction.Remove ? "Abreißen" : t.DisplayName,
                $"{World.City.PlayerHouseCount} gesetzt · Stufen nach Einrichtungen in Reichweite · ziehen = Reihe");
        }

        public override void DrawBody(Ui ui)
        {
            var ht = Ht;
            if (ht.Current == HouseTool.HouseAction.Remove)
            {
                ui.Note("Gesetztes Gebäude anklicken: 25 % der Baukosten zurück, Bewohner ziehen um oder weg, Beschäftigte verlieren den Job.");
                Stock(ui);
                return;
            }

            var preview = ht.Preview;
            ui.Section("Am Mauszeiger", preview != null ? $"erreichbare Stufe {preview.ReachableLevel} von {preview.MaxLevel}" : null);
            if (preview == null || !Tool.HasCursor) ui.Note("Maus an eine Stadtstraße bewegen – hier steht dann, was an dieser Stelle erfüllt wäre.");
            else
            {
                if (ht.Plan != null && !ht.Plan.IsValid) ui.Message("Hier nicht baubar", HouseTool.ErrorText(ht.Plan), Tone.Warn);
                else if (ht.Plan != null && !ht.Plan.HasRoadAccess) ui.Message("Noch keine Straße", HouseTool.NoRoadText, Tone.Warn);
                var levels = HouseRules.LevelsFor(ht.SelectedType.Zone); // Setzen statt Zonen: Regeln je Bauart
                foreach (var rule in levels)
                {
                    bool reached = preview.ReachableLevel >= rule.Level;
                    ui.Text($"Stufe {rule.Level} – {rule.Name}", reached ? Tone.Good : Tone.None);
                    using (ui.Fields())
                        foreach (var s in preview.Needs)
                        {
                            if (System.Array.IndexOf(rule.Needs, s.Need) < 0) continue;
                            var need = s.Need;
                            bool shown = ht.ShownNeed == need;
                            bool hasReach = need.Kind == HouseNeedKind.Service || need.Kind == HouseNeedKind.Shop || need.Kind == HouseNeedKind.Customers;
                            string label = $"{s.Mark} {need.Label}" + (shown ? " ◂" : "");
                            if (hasReach) ui.FieldLink(label, s.Detail, () => ht.FocusNeed = ht.FocusNeed == need ? null : need, s.Met ? Tone.Good : Tone.Warn);
                            else ui.Field(label, s.Detail, s.Met ? Tone.Good : Tone.Warn);
                        }
                }
                bool homes = ht.SelectedType.HasHomes;
                ui.Note($"Außerdem (weich, nach dem Einzug): {(homes ? "Bewohner" : "Beschäftigte")} zufrieden (≥ {levels[0].Satisfaction * 100f:0} % bzw. " +
                        $"{levels[levels.Length - 1].Satisfaction * 100f:0} %) und {(homes ? "Wohnungen" : "Arbeitsplätze")} zu {levels[0].Occupancy * 100f:0} % belegt. " +
                        "Fehlt eine Anforderung der eigenen Stufe, steigt das Gebäude nach einigen Tagen ab.");
                if (ht.SelectedType.HasFirm)
                    ui.Note(ht.SelectedType.Zone == ZoneType.Industry
                        ? "Welcher Betrieb einzieht, entscheidet die Simulation (Vorkommen am Ort, Nachfrage, Schwerpunkt-Gebiet und Förderung – lenken mit Z)."
                        : "Ein Händler bzw. Dienstleister zieht ein; Ware und Gewinn laufen über die Warenketten (F5). Geht er pleite, kommt ein neuer.");
                ui.Note("Klick auf eine Einrichtung zeigt ihre Reichweite (Abdeckung über den Häusern, Laden/Kundschaft: Ring). " +
                        "Ohne Auswahl wird die erste fehlende gezeigt.");
            }

            var t = ht.SelectedType;
            int max = HouseRules.MaxLevelFor(t.Zone);
            ui.Section("Bauart", t.DisplayName);
            using (ui.Fields())
            {
                ui.Field("Art (Modelle, Steuern)", Zones.DisplayName(t.Zone));
                ui.Field("Grundstück", $"{t.WidthCells}×{t.DepthCells} Zellen ({t.Width:0}×{t.Depth:0} m)");
                if (t.HasHomes) ui.Field("Wohnungen je Stufe", $"{t.HouseholdsAtLevel(1)} / {t.HouseholdsAtLevel(2)} / {t.HouseholdsAtLevel(max)}");
                if (t.JobsAtLevel(1) > 0) ui.Field("Arbeitsplätze je Stufe", $"{t.JobsAtLevel(1)} / {t.JobsAtLevel(2)} / {t.JobsAtLevel(max)}");
                ui.Field("Baukosten", BuildLabels.Money(t.Cost) + " (Abriss: 25 % zurück)");
                var kind = DemandReport.KindOf(t.Zone);
                ui.Field($"Nachfrage {DemandReport.NameOf(kind)}", $"{DemandReport.ValueOf(World.City, kind):0} %");
            }
            Stock(ui);
        }

        void Stock(Ui ui)
        {
            const int levels = 3;
            int[] perLevel = new int[levels + 1];
            int residents = 0, workers = 0;
            foreach (var b in World.City.Buildings)
            {
                if (!b.IsPlayerPlaced) continue;
                perLevel[System.Math.Clamp(b.Level, 1, levels)]++;
                residents += b.Residents;
                workers += b.Workers;
            }
            ui.Section("Deine Gebäude", $"{World.City.PlayerHouseCount} gesetzt · {residents} Bewohner · {workers} Beschäftigte");
            using (ui.Fields())
                for (int l = 1; l <= levels; l++)
                    ui.Field($"Stufe {l}", perLevel[l].ToString());
        }
    }
}
