using System.Collections.Generic;
using System.Threading.Tasks;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Game.WorldMap;
using CityBuilder.Simulation.World;
using UnityEngine;

namespace CityBuilder.Game.Persistence
{
    /// <summary>
    /// Neues-Spiel-Fenster (Spielstart &amp; Startregion; mittig, breit, Baukasten). Vier Schritte:
    /// <list type="number">
    /// <item><b>Welt</b>: Seed eingeben oder „neu würfeln“; kleine Weltkarte – freies Land hell, Länder getönt.</item>
    /// <item><b>Vorlieben</b>: Chips je Gruppe (Klima, Lage, Gelände, Schwerpunkt, Schwierigkeit, Nachbarn). Was mit der Auswahl
    /// keine freie Region mehr trifft, ist ausgegraut (Tooltip mit Erklärung, z. B. Erze fast nur in Gebirgen), wenige Treffer = „selten“.</item>
    /// <item><b>Vorschläge</b>: die besten Regionen mit Begründung und Werten, auf der Karte markiert; Klick wählt – oder jede freie
    /// Region direkt auf der Karte.</item>
    /// <item><b>Stadtname</b> → „Stadt gründen“ (<see cref="SaveManager.StartNewGame"/> → <c>SimulationRunner.NewGame(settings)</c>).</item>
    /// </list>
    /// Die Vorschau-Welt entsteht im Hintergrund (reines C#, gleicher Seed/gleiche Einstellungen wie das Spiel → gleiche Regions-Ids).
    /// Beim ersten Programmstart ohne Spielstand öffnet der <see cref="SaveManager"/> das Fenster und es lässt sich nicht schließen.
    /// </summary>
    public sealed class NewGameWindow : GameWindow
    {
        enum Step { World, Preferences, Suggestions, Name }
        static readonly string[] StepNames = { "1 Welt", "2 Vorlieben", "3 Startregion", "4 Stadtname" };
        const float MapScale = 2f;

        readonly SaveManager manager;
        bool open, firstStart;
        Step step;
        int speedBeforeOpen = -1;

        // Welt
        string seedText = "";
        int seed;
        Task<(Planet planet, StartFinder finder)> pending;
        int pendingSeed;
        Planet planet;
        StartFinder finder;
        string error;

        // Vorlieben / Vorschläge
        readonly HashSet<string> selection = new HashSet<string>();
        List<StartOptionState> states;
        List<StartSuggestion> suggestions;
        int matches;
        int chosenRegion = -1;
        StartSuggestion chosen;
        int hoverCell = -1;

        // Name
        string cityName = "";

        Texture2D texture;
        bool textureDirty = true;
        int hoverRegionShown = -2;

        public NewGameWindow(SaveManager manager) : base(WindowSlot.Center) { this.manager = manager; }

        public override string FrameClass => "cb-window--wide";
        public override float OpenScore => open ? 2f : 0f; // vor dem Spielmenü
        public bool IsOpen => open;

        public void Open(bool firstStart)
        {
            open = true;
            this.firstStart = firstStart;
            step = Step.World;
            ContentVersion++;
            if (Runner != null && !Runner.IsPaused) { speedBeforeOpen = Runner.SpeedIndex; Runner.SetSpeed(0); }
            if (planet == null && pending == null)
            {
                var s = Runner != null ? Runner.CreateSettings() : null;
                Generate(s != null ? s.WorldSeed : 2024);
            }
        }

        public override void Close()
        {
            if (firstStart && Runner != null && Runner.AwaitingNewGame) return; // erst eine Stadt gründen
            open = false;
            GameKeyboard.TextInputActive = false;
            if (speedBeforeOpen > 0 && Runner != null && Runner.IsPaused) Runner.SetSpeed(speedBeforeOpen);
            speedBeforeOpen = -1;
        }

        public override bool HandleEscape()
        {
            if (step > Step.World) { step--; ContentVersion++; return true; }
            Close();
            return true;
        }

        // ------------------------------------------------------------ Welt erzeugen (Hintergrund)

        void Generate(int newSeed)
        {
            seed = newSeed;
            seedText = newSeed.ToString();
            pendingSeed = newSeed;
            error = null;
            var settings = Runner != null ? Runner.CreateSettings(newSeed) : new Simulation.Core.WorldSettings { WorldSeed = newSeed };
            var planetSettings = settings.Planet;
            pending = Task.Run(() =>
            {
                var p = PlanetGenerator.Generate(newSeed, planetSettings);
                return (p, new StartFinder(p));
            });
        }

        public override void Tick()
        {
            if (pending == null || !pending.IsCompleted) return;
            if (pending.IsFaulted) error = pending.Exception?.GetBaseException().Message ?? "Fehler beim Erzeugen";
            else if (pendingSeed == seed)
            {
                (planet, finder) = pending.Result;
                chosenRegion = -1;
                chosen = null;
                Recompute();
            }
            pending = null;
            Host?.MarkDirty();
        }

        void Recompute()
        {
            if (finder == null) return;
            // Auswahl-Optionen, die es in dieser Welt nicht gibt, bleiben gewählt, zählen aber 0 → Hinweis
            states = finder.Evaluate(selection);
            matches = finder.Count(selection);
            suggestions = finder.Suggest(selection, 5);
            if (chosenRegion >= 0 && !finder.IsSelectable(chosenRegion)) chosenRegion = -1;
            chosen = chosenRegion >= 0 ? finder.Describe(chosenRegion, selection) : null;
            textureDirty = true;
        }

        void Choose(int regionId)
        {
            if (finder == null || !finder.IsSelectable(regionId)) return;
            chosenRegion = regionId;
            chosen = finder.Describe(regionId, selection);
            textureDirty = true;
        }

        // ------------------------------------------------------------ Kopf

        public override void DrawHead(Ui ui)
        {
            string sub = planet == null ? "Welt wird erzeugt …"
                : $"Welt {seed} · {planet.FreeRegionCount} freie Regionen · {StartFinder.DescribeSelection(selection)}";
            Head(ui, "Neues Spiel", Tone.Accent, firstStart ? "Willkommen – gründe deine Stadt" : "Neues Spiel", sub,
                closable: !(firstStart && Runner != null && Runner.AwaitingNewGame));
            ui.Tabs(StepNames, (int)step, i =>
            {
                var target = (Step)i;
                if (target > step && !CanEnter(target)) return;
                step = target;
                ContentVersion++;
            }, small: true);
        }

        bool CanEnter(Step s)
        {
            if (finder == null) return false;
            if (s >= Step.Suggestions && matches == 0 && chosenRegion < 0) return false;
            if (s >= Step.Name && chosenRegion < 0) return false;
            return true;
        }

        void Go(Step s)
        {
            if (!CanEnter(s) && s > step) return;
            step = s;
            ContentVersion++;
            if (s == Step.Suggestions && chosenRegion < 0 && suggestions != null && suggestions.Count > 0) Choose(suggestions[0].Region.Id);
        }

        // ------------------------------------------------------------ Inhalt

        public override void DrawBody(Ui ui)
        {
            if (error != null) ui.Message("Welt konnte nicht erzeugt werden", error, Tone.Bad);
            switch (step)
            {
                case Step.World: DrawWorld(ui); break;
                case Step.Preferences: DrawPreferences(ui); break;
                case Step.Suggestions: DrawSuggestions(ui); break;
                default: DrawName(ui); break;
            }
        }

        void DrawWorld(Ui ui)
        {
            ui.Section("Welt", "fiktive Erde – Kontinente, Klima, Bodenschätze aus dem Seed");
            ui.TextInput("Welt-Seed", seedText, v => seedText = v, ApplySeed, 12);
            bool seedOk = int.TryParse(seedText.Trim(), out int typed);
            using (ui.Actions())
            {
                ui.ActionButton("Neu würfeln", () => Generate(Random.Range(1, 1000000)), ActionStyle.Secondary, enabled: pending == null,
                    tip: "Andere Welt mit zufälligem Seed");
                ui.ActionButton("Seed übernehmen", ApplySeed, ActionStyle.Secondary, enabled: pending == null && seedOk && typed != seed);
            }
            if (!seedOk) ui.Message("Eingabe", "Der Seed muss eine ganze Zahl sein.", Tone.Warn);
            if (planet == null || pending != null) { ui.Note("Welt wird erzeugt … (etwa eine halbe Sekunde)"); if (planet == null) return; }

            DrawMap(ui);
            using (ui.Grid(3))
            {
                ui.Stat("Länder", (planet.Countries.Count - (planet.PlayerCityState != null ? 1 : 0)).ToString());
                ui.Stat("Freie Regionen", finder.Candidates.Count.ToString(), $"von {planet.Regions.Count}");
                var zones = new HashSet<ClimateZone>();
                foreach (var f in finder.Candidates) zones.Add(f.Climate);
                ui.Stat("Klimazonen", zones.Count.ToString(), "mit freiem Land");
            }
            ui.Note("Hell = freies, unbesiedeltes Land – dort gründest du deine Stadt als freier Stadtstaat. Getönt = Länder (NPC). " +
                    "Mit weiteren Städten kannst du später ein eigenes Land gründen.");
            using (ui.Actions())
            {
                if (firstStart)
                    ui.ActionButton("Schnellstart", QuickStart, ActionStyle.Secondary, enabled: suggestions != null && suggestions.Count > 0,
                        tip: "Ohne Vorlieben: beste freie Region (mild, fruchtbar, Wasser), Stadtname „Neustadt“");
                ui.ActionButton("Weiter: Vorlieben", () => Go(Step.Preferences), ActionStyle.Primary, enabled: finder != null);
            }
        }

        void ApplySeed()
        {
            if (int.TryParse(seedText.Trim(), out int s) && s != seed && pending == null) Generate(s);
        }

        void DrawPreferences(Ui ui)
        {
            if (finder == null) { ui.Note("Welt wird erzeugt …"); return; }
            foreach (var g in StartCriteria.Groups)
            {
                ui.Section(g.Name, g.Exclusive ? "eine Wahl" : "mehrere möglich");
                using (ui.Chips())
                    foreach (var st in states)
                    {
                        if (st.Option.Group != g.Group) continue;
                        var opt = st.Option;
                        bool locked = !st.Selected && st.Availability == OptionAvailability.Unavailable;
                        bool rare = st.Availability == OptionAvailability.Rare;
                        string badge = locked ? "–" : rare ? $"selten · {st.Matches}" : st.Matches.ToString();
                        ui.ChoiceChip(opt.Name, st.Selected, () => { StartFinder.Toggle(selection, opt); Recompute(); },
                            enabled: !locked, rare: rare, badge: badge, tip: st.Tip);
                    }
            }
            ui.Space(6f);
            if (matches == 0)
                ui.Message("Keine freie Region passt", "Nimm eine Vorliebe heraus – oder wähle im nächsten Schritt frei auf der Karte.", Tone.Bad);
            else
                ui.Message($"{matches} freie Region{(matches == 1 ? "" : "en")} passen", "Zahlen an den Chips = Treffer, wenn du diese Vorliebe dazunimmst. " +
                           "Ausgegraut = keine Region auf dieser Welt (Maus darüber: warum).", matches <= StartFinder.RareThreshold ? Tone.Warn : Tone.Good);
            using (ui.Actions())
            {
                ui.ActionButton("Zurück", () => Go(Step.World));
                ui.ActionButton("Zurücksetzen", () => { selection.Clear(); Recompute(); }, ActionStyle.Secondary, enabled: selection.Count > 0);
                ui.ActionButton("Weiter: Startregion", () => Go(Step.Suggestions), ActionStyle.Primary);
            }
        }

        void DrawSuggestions(Ui ui)
        {
            if (finder == null) { ui.Note("Welt wird erzeugt …"); return; }
            DrawMap(ui);
            ui.Section("Vorschläge", matches > 0 ? $"die besten von {matches} passenden" : "keine passt – frei auf der Karte wählen");
            if (suggestions != null)
                for (int i = 0; i < suggestions.Count; i++)
                {
                    var s = suggestions[i];
                    int id = s.Region.Id;
                    bool on = id == chosenRegion;
                    ui.ListItem((i + 1).ToString(), on ? new Color(0.17f, 0.70f, 0.64f) : new Color(0.55f, 0.45f, 0.2f), s.Region.Name, s.Reason,
                        on ? "gewählt" : null, Tone.Good, () => Choose(id));
                }
            if (chosen != null)
            {
                bool own = suggestions == null || !suggestions.Exists(x => x.Region.Id == chosenRegion);
                ui.Section($"Gewählt: {chosen.Region.Name}", own ? "freie Wahl auf der Karte" : null);
                if (own) ui.Text(chosen.Reason);
                if (!finder.Matches(chosen.Facts, selection) && selection.Count > 0)
                    ui.Message("Passt nicht zu allen Vorlieben", "Frei gewählt – du kannst trotzdem hier beginnen.", Tone.Warn);
                using (ui.Card())
                    foreach (var v in chosen.Values) ui.Meter(v.Label, v.Text, v.Value, v.Value >= 0.6f ? Tone.Good : v.Value >= 0.3f ? Tone.Accent : Tone.Muted);
            }
            using (ui.Actions())
            {
                ui.ActionButton("Zurück", () => Go(Step.Preferences));
                ui.ActionButton("Weiter: Stadtname", () => Go(Step.Name), ActionStyle.Primary, enabled: chosenRegion >= 0);
            }
        }

        void DrawName(Ui ui)
        {
            if (chosen == null) { Go(Step.Suggestions); return; }
            var r = chosen.Region;
            ui.Section("Deine Stadt", r.Name);
            using (ui.Fields())
            {
                ui.Field("Region", $"{r.Name} · {Biomes.DisplayName(r.DominantBiome)}");
                ui.Field("Klima", $"{StartCriteria.ClimateWord(chosen.Facts.Climate)} · Ø {r.MeanTemperature:0.0} °C · {r.Precipitation:0} mm/Jahr");
                ui.Field("Lage", chosen.Reason);
                ui.Field("Welt-Seed", seed.ToString());
            }
            ui.TextInput("Stadtname", cityName, v => cityName = v, StartGame, PlayerRealm.MaxNameLength);
            string name = PlayerRealm.Clean(cityName) ?? PlayerRealm.DefaultCityName;
            ui.Note($"Dein Gebiet heißt „{PlayerRealm.CityStateName(name)}“ – ein freier Stadtstaat ohne eigenes Land. " +
                    "Mit weiteren Städten kannst du später ein Land gründen (eigener Name, eigene Flagge).");
            if (!(firstStart && Runner != null && Runner.AwaitingNewGame))
                ui.Message("Achtung", "Nicht gespeicherter Fortschritt geht verloren (Autosave bleibt erhalten).", Tone.Warn);
            using (ui.Actions())
            {
                ui.ActionButton("Zurück", () => Go(Step.Suggestions));
                ui.ActionButton("Stadt gründen", StartGame, ActionStyle.Primary, enabled: pending == null);
            }
        }

        void QuickStart()
        {
            if (suggestions == null || suggestions.Count == 0) return;
            Choose(suggestions[0].Region.Id);
            StartGame();
        }

        void StartGame()
        {
            if (Runner == null || chosenRegion < 0 || pending != null) return;
            var settings = Runner.CreateSettings(seed, chosenRegion);
            settings.CityName = PlayerRealm.Clean(cityName);
            GameKeyboard.TextInputActive = false;
            int resume = speedBeforeOpen > 0 ? speedBeforeOpen : 1;
            manager.StartNewGame(settings);
            open = false;
            speedBeforeOpen = -1;
            Runner.SetSpeed(resume);
            // Die Vorschau-Welt bleibt unverändert (das Spiel hat seinen eigenen Planeten) → nächste Öffnung zeigt dieselbe Welt
            step = Step.World;
        }

        // ------------------------------------------------------------ Karte

        void DrawMap(Ui ui)
        {
            if (planet == null) return;
            if (textureDirty) RebuildTexture();
            ui.Image(texture, planet.Width * MapScale, planet.Height * MapScale,
                uv =>
                {
                    int i = Cell(uv);
                    int r = planet.RegionOf[i];
                    if (r >= 0 && finder.IsSelectable(r))
                    {
                        Choose(r);
                        if (step < Step.Suggestions) Go(Step.Suggestions);
                    }
                },
                uv =>
                {
                    hoverCell = uv.HasValue ? Cell(uv.Value) : -1;
                    int hr = hoverCell >= 0 ? planet.RegionOf[hoverCell] : -1;
                    if (hr != hoverRegionShown) { hoverRegionShown = hr; Host?.MarkDirty(); }
                });
            ui.Note(HoverText());
        }

        int Cell(Vector2 uv)
        {
            int x = Mathf.Clamp((int)(uv.x * planet.Width), 0, planet.Width - 1);
            int y = Mathf.Clamp((int)(uv.y * planet.Height), 0, planet.Height - 1);
            return planet.Index(x, y);
        }

        string HoverText()
        {
            if (hoverCell < 0) return "Maus über die Karte: Region · Klick auf freies Land wählt die Startregion · gold = Vorschläge, weiß = gewählt";
            int r = planet.RegionOf[hoverCell];
            if (r < 0) return "Meer";
            var region = planet.Regions[r];
            var f = finder.FactsFor(r);
            if (f == null)
                return region.Country != null ? $"{region.Name} · gehört zu {region.Country.Name}" : $"{region.Name} · frei, aber zu klein für eine Stadt";
            bool fits = finder.Matches(f, selection);
            return $"{region.Name} · freies Land · {StartCriteria.ClimateWord(f.Climate)}, {f.TerrainWord}" +
                   $"{(f.CoastScore >= StartCriteria.Fit ? ", Küste" : "")}{(f.RiverScore >= StartCriteria.Fit ? ", großer Fluss" : region.HasRiver ? ", Fluss" : "")}" +
                   (selection.Count > 0 ? (fits ? " · passt zu deinen Vorlieben" : " · passt nicht zu allen Vorlieben") : "") + " – Klick wählt";
        }

        void RebuildTexture()
        {
            textureDirty = false;
            var p = planet;
            if (texture == null || texture.width != p.Width || texture.height != p.Height)
            {
                if (texture != null) Object.Destroy(texture);
                texture = new Texture2D(p.Width, p.Height, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "NewGameMap" };
            }
            // Regionsstatus: 0 = Land eines NPC, 1 = frei (nicht wählbar/passt nicht), 2 = frei und passt zur Auswahl
            var status = new byte[p.Regions.Count];
            foreach (var f in finder.Candidates) status[f.Region.Id] = (byte)(finder.Matches(f, selection) ? 2 : 1);
            foreach (var reg in p.Regions) if (reg.IsFree && status[reg.Id] == 0) status[reg.Id] = 1;
            var ocean = new Color32(22, 34, 58, 255);
            var px = new Color32[p.CellCount];
            for (int i = 0; i < p.CellCount; i++)
            {
                Color32 c;
                int r = p.RegionOf[i];
                if (r < 0) c = p.BiomeOf[i] == Biome.Shelf ? new Color32(30, 48, 78, 255) : ocean;
                else
                {
                    var biome = WorldMapWindow.BiomeColor(p.BiomeOf[i]);
                    if (status[r] == 0)
                    {
                        uint rgb = p.Regions[r].Country != null ? p.Regions[r].Country.Color : 0x808080u;
                        var tint = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
                        c = Color32.Lerp(Color32.Lerp(biome, new Color32(40, 42, 48, 255), 0.6f), tint, 0.25f);
                    }
                    else if (status[r] == 1) c = Color32.Lerp(biome, new Color32(120, 120, 120, 255), 0.35f);
                    else c = Color32.Lerp(biome, new Color32(255, 255, 255, 255), 0.18f);
                    // Grenzen zwischen Regionen verschiedenen Status/Besitzers
                    for (int k = 0; k < 2; k++)
                    {
                        int j = p.Neighbor(i, k, 1 - k);
                        if (j >= 0 && p.IsLand(j) && p.RegionOf[j] != r && (p.Regions[p.RegionOf[j]].CountryId != p.Regions[r].CountryId || status[r] == 2))
                        { c = Color32.Lerp(c, new Color32(0, 0, 0, 255), 0.35f); break; }
                    }
                }
                px[(p.Height - 1 - p.YOf(i)) * p.Width + p.XOf(i)] = c;
            }
            // Auswahl weiß umranden, Vorschläge als Goldpunkte
            if (chosenRegion >= 0)
                foreach (int i in p.Regions[chosenRegion].Cells)
                {
                    bool edge = false;
                    for (int k = 0; k < 4 && !edge; k++)
                    {
                        int dx = k == 0 ? 1 : k == 1 ? -1 : 0, dy = k == 2 ? 1 : k == 3 ? -1 : 0;
                        int j = p.Neighbor(i, dx, dy);
                        edge = j < 0 || p.RegionOf[j] != chosenRegion;
                    }
                    if (edge) px[(p.Height - 1 - p.YOf(i)) * p.Width + p.XOf(i)] = new Color32(255, 255, 255, 255);
                }
            if (suggestions != null)
                foreach (var s in suggestions) Dot(px, p, s.Region.CenterCell, s.Region.Id == chosenRegion ? new Color32(255, 255, 255, 255) : new Color32(242, 169, 59, 255));
            texture.SetPixels32(px);
            texture.Apply(false);
        }

        static void Dot(Color32[] px, Planet p, int cell, Color32 col)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int j = p.Neighbor(cell, dx, dy);
                    if (j >= 0) px[(p.Height - 1 - p.YOf(j)) * p.Width + p.XOf(j)] = col;
                }
        }
    }
}
