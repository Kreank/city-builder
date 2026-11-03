using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Game.WorldMap;
using CityBuilder.Simulation.World;
using UnityEngine;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Weltkarte (mittiges, breites Fenster; F4 oder Knopf oben): Kartentextur aus dem <see cref="WorldMapWindow"/>
    /// (dort bleiben Zustand offen/zu, Taste F4, Textur und IMGUI-Rückfall), Umschalter Biome/Relief/Länder/Klima/Rohstoffe
    /// (Rohstoffe mit eigener Zeile für die 7 Vorkommen). Maus über der Karte zeigt die Zelle, Klick auf ein Land wählt es:
    /// Name, Hauptstadt, Stärkenprofil (Primär/Sekundär), Bevölkerung, BIP und Überschuss/Defizit je Güterkategorie.
    /// Ohne Auswahl: Spieler-Land. Liest nur.
    /// </summary>
    public sealed class WorldMapUiWindow : GameWindow
    {
        const float Scale = 2f;

        static readonly string[] MainModes = { "Biome", "Relief", "Länder", "Temperatur", "Niederschlag", "Rohstoffe" };
        static readonly WorldMapWindow.Mode[] ResourceModes =
        {
            WorldMapWindow.Mode.Fertility, WorldMapWindow.Mode.Forest, WorldMapWindow.Mode.Ore, WorldMapWindow.Mode.Coal,
            WorldMapWindow.Mode.Oil, WorldMapWindow.Mode.Sand, WorldMapWindow.Mode.Groundwater,
        };
        static readonly string[] ResourceNames = { "Boden", "Wald", "Erz", "Kohle", "Öl", "Sand/Kies", "Grundwasser" };

        readonly WorldMapWindow map;
        WorldMapWindow.Mode mode = WorldMapWindow.Mode.Biomes;
        int selectedCountry = -1;
        int hoverCell = -1;

        public WorldMapUiWindow(WorldMapWindow map) : base(WindowSlot.Center) { this.map = map; }

        public override string FrameClass => "cb-window--wide";

        public override float OpenScore => map != null && map.Visible ? 1f : 0f;
        public override void Close() => map.Visible = false;

        public void Toggle() => map.Visible = !map.Visible;
        public bool IsOpen => map != null && map.Visible;

        int MainIndex => mode <= WorldMapWindow.Mode.Precipitation ? (int)mode : 5;

        public override void DrawHead(Ui ui)
        {
            var p = map.Planet;
            var player = p?.PlayerCountry;
            Head(ui, "Weltkarte (F4)", Tone.Info, player != null ? player.Name : "Welt",
                p == null ? "keine Welt erzeugt" : $"{p.Countries.Count} Länder · {p.Regions.Count} Regionen · Startregion {p.StartRegion?.Name ?? "–"}");
            if (p == null) return;
            ui.Tabs(MainModes, MainIndex, i =>
            {
                if (i < 5) mode = (WorldMapWindow.Mode)i;
                else if (MainIndex != 5) mode = WorldMapWindow.Mode.Fertility;
            }, small: true);
            if (MainIndex == 5)
                ui.Tabs(ResourceNames, System.Array.IndexOf(ResourceModes, mode), i => mode = ResourceModes[i], small: true);
        }

        public override void DrawBody(Ui ui)
        {
            var p = map.Planet;
            if (p == null)
            {
                ui.Message("Keine Weltkarte", "Im SimulationRunner „Generate World“ einschalten.", Tone.Accent);
                return;
            }
            var tex = map.TextureFor(mode);
            ui.Image(tex, p.Width * Scale, p.Height * Scale,
                uv => { int i = Cell(p, uv); var c = p.CountryOfCell(i); selectedCountry = c != null ? c.Id : -1; },
                uv => hoverCell = uv.HasValue ? Cell(p, uv.Value) : -1);
            ui.Note(hoverCell >= 0 ? WorldMapWindow.CellText(p, hoverCell) : "Maus über die Karte: Zelleninfo · Klick auf ein Land: Details · weiß = Startregion, gelber Punkt = deine Hauptstadt");

            var country = selectedCountry >= 0 && selectedCountry < p.Countries.Count ? p.Countries[selectedCountry] : p.PlayerCountry;
            if (country != null) CountryBody(ui, p, country);
        }

        static int Cell(Planet p, Vector2 uv)
        {
            int x = Mathf.Clamp((int)(uv.x * p.Width), 0, p.Width - 1);
            int y = Mathf.Clamp((int)(uv.y * p.Height), 0, p.Height - 1);
            return p.Index(x, y);
        }

        void CountryBody(Ui ui, Planet p, Country c)
        {
            ui.Section(c.Name + (c.IsPlayer ? " (dein Land)" : ""), $"Hauptstadt {c.CapitalName}");
            using (ui.Chips())
            {
                ui.Chip("★ " + CountryProfile.DisplayName(c.Profile.Primary), Tone.Good);
                ui.Chip("☆ " + CountryProfile.DisplayName(c.Profile.Secondary), Tone.Info);
                if (c.IsPlayer) ui.Chip("Spieler", Tone.Accent);
                if (selectedCountry >= 0 && !c.IsPlayer) ui.Chip("Auswahl", Tone.Muted);
            }
            // --- Etappe 8 Handel: Land im Handelsfenster öffnen
            if (!c.IsPlayer && World?.Trade != null && TradeWindow.Current != null)
            {
                int id = c.Id;
                using (ui.Actions())
                    ui.ActionButton("Handel", () => { TradeWindow.Current.OpenFor(id); Close(); }, ActionStyle.Primary, false, true,
                        $"Beziehung {World.Trade.Relation(p.PlayerCountryId, id):+0;−0} · Angebote, Verträge, Zölle (F11)");
            }
            // --- Ende Etappe 8
            var e = c.Economy;
            using (ui.Grid(3))
            {
                ui.Stat("Bevölkerung", e != null ? $"{e.Population / 1e6:0.0}" : "–", "Mio.");
                ui.Stat("BIP", e != null ? $"{e.Gdp / 1e6:0.0}" : "–", "Mio. €/Tag");
                ui.Stat("Fläche", $"{c.AreaKm2 / 1e6f:0.00}", "Mio. km²");
            }
            using (ui.Fields())
            {
                ui.Field("Regionen", c.Regions.Count.ToString());
                ui.Field("Klima", $"Ø {c.MeanTemperature:0.0} °C · {c.Precipitation:0} mm/Jahr");
                if (c.IsPlayer && p.StartRegion != null)
                {
                    var r = p.StartRegion;
                    ui.Field("Startregion", $"{r.Name}: {Biomes.DisplayName(r.DominantBiome)}, Relief {r.Ruggedness:0} m, " +
                                            $"{(r.HasRiver ? "Fluss" : "kein Fluss")}, {(r.IsCoastal ? "Küste" : "Binnenland")}");
                }
            }
            // --- Außenanbindungen: Richtungen der Anschlüsse (auf der Karte als Strahlen ab der Startregion)
            var gateways = World?.Gateways;
            if (c.IsPlayer && gateways != null && gateways.All.Count > 0)
            {
                ui.Section("Anschlüsse der Stadt", "rot = Autobahn · braun = Bahn (folgt) · hellblau = Wasser (folgt)");
                using (ui.Fields())
                    foreach (var g in gateways.All)
                        ui.Field($"{g.KindName} → {(g.HasLocation ? g.CompassText : "–")}", $"{g.Name} · {g.StatusText}",
                            g.IsOperational ? Tone.Good : Tone.Muted);
            }
            // --- Ende Außenanbindungen

            ui.Section("Stärken", "Rang unter allen Ländern");
            using (ui.Card())
                for (int k = 0; k < CountryProfile.Count; k++)
                {
                    var s = (Strength)k;
                    string mark = s == c.Profile.Primary ? " · Primär" : s == c.Profile.Secondary ? " · Sekundär" : "";
                    ui.Meter(CountryProfile.DisplayName(s), $"{c.Profile[s] * 100f:0}{mark}", c.Profile[s],
                        s == c.Profile.Primary ? Tone.Good : s == c.Profile.Secondary ? Tone.Info : Tone.Muted);
                }

            if (e == null) return;
            ui.Section("Volkswirtschaft", e.LastDay >= 0 ? $"aggregiert · Tag {e.LastDay + 1}" : "noch kein Tag gerechnet");
            using (ui.Card())
            {
                ui.Columns("Güter", "Saldo/Tag", "Lager · Preis", header: true);
                for (int k = 0; k < GoodsCategories.Count; k++)
                {
                    var g = (GoodsCategory)k;
                    double bal = e.Balance(g);
                    double days = e.Demand[k] > 0 ? e.Stock[k] / e.Demand[k] : 0;
                    ui.Columns(GoodsCategories.DisplayName(g), $"{(bal >= 0 ? "+" : "−")}{System.Math.Abs(bal) / 1e6:0.00} M",
                        $"{days:0} T. · {e.Price[k]:0.00} €", bal >= 0 ? Tone.Good : Tone.Warn, Tone.Muted);
                }
            }
            ui.Note("Saldo = Produktion − Bedarf je Tag (Überschuss = Exportpotenzial, Defizit = Importbedarf).");
        }
    }
}
