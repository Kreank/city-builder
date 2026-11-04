using CityBuilder.Game.Realm;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Game.WorldMap;
using CityBuilder.Simulation.Realm;
using CityBuilder.Simulation.World;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Etappe 7 Reich in der Weltkarte: eigene Städte (Marker auf der Karte, Liste mit „Zur Stadt wechseln“), Klick auf eine Region
    /// zeigt, ob dort eine Stadt gegründet werden kann (alle Bedingungen mit ✓/✗, Weg zur Mutterstadt), Name eingeben, „Stadt gründen“.
    /// In der eigenen Region wählt der Klick die Weltzelle (freier Platz mit Abstand zu den eigenen Städten).
    /// </summary>
    public sealed partial class WorldMapUiWindow
    {
        /// <summary>Zuletzt angemeldete Weltkarte (für „Neue Stadt gründen …“ aus dem Reichsfenster).</summary>
        public static WorldMapUiWindow Current { get; private set; }

        int selectedRegion = -1;
        int selectedCell = -1;
        string foundName = "";
        bool foundingMode;

        /// <summary>Weltkarte öffnen und zum Gründen auffordern (Klick auf freies Land).</summary>
        public static void OpenForFounding()
        {
            var w = Current;
            if (w == null || w.map == null) return;
            w.map.Visible = true;
            w.foundingMode = true;
            w.mode = WorldMapWindow.Mode.Countries;
        }

        void SelectCell(Planet p, int cell)
        {
            selectedCell = cell;
            selectedRegion = cell >= 0 ? p.RegionOf[cell] : -1;
        }

        void RealmBody(Ui ui, Planet p)
        {
            var realm = World?.Realm;
            var sys = realm?.System;
            if (realm == null || sys == null) return;

            // --- Deine Städte
            ui.Section("Deine Städte", $"{realm.DisplayName} · gold = Stadt, weiß = detailliert angezeigt");
            foreach (var c in realm.Cities)
            {
                bool active = c.Id == realm.ActiveCityId;
                int id = c.Id;
                string sub = $"{(c.RegionId >= 0 ? p.Regions[c.RegionId].Name : "–")} · {c.Economy.Population:N0} Einwohner · " +
                             (active ? "detailliert" : c.HasSnapshot ? "aggregiert" : "neu");
                ui.ListItem(Ui.Initials(c.Name), active ? UnityEngine.Color.white : new UnityEngine.Color(0.91f, 0.77f, 0.28f), c.Name, sub,
                    active ? "hier" : "wechseln", active ? Tone.Accent : Tone.Info,
                    active ? (System.Action)null : () => { Close(); RealmActions.SwitchTo(Runner, id); });
            }
            using (ui.Actions())
            {
                ui.ActionButton(foundingMode ? "Gründen: Region anklicken …" : "Neue Stadt gründen", () => { foundingMode = !foundingMode; mode = WorldMapWindow.Mode.Countries; },
                    ActionStyle.Secondary, foundingMode, true, "Freie Nachbarregion (hell) oder freien Platz in der eigenen Region anklicken");
                if (RealmWindow.Current != null)
                    ui.ActionButton("Reich (Pos1)", () => { Close(); RealmWindow.Current.Open(); });
            }

            // --- Ausgewählte Region
            if (selectedRegion < 0 || selectedRegion >= p.Regions.Count) return;
            var region = p.Regions[selectedRegion];
            var me = p.PlayerCountry;
            bool own = me != null && region.CountryId == me.Id;
            if (!region.IsFree && !own && !foundingMode) return;
            string status = region.IsFree ? "freies Land" : own ? "dein Gebiet" : $"gehört {region.Country?.Name}";
            ui.Section($"Region {region.Name}", $"{status} · {Biomes.DisplayName(region.DominantBiome)} · {(region.HasRiver ? "Fluss" : "kein Fluss")} · {(region.IsCoastal ? "Küste" : "Binnenland")}");
            int cell = own ? selectedCell : -1; // freies Land: beste Zelle; eigene Region: angeklickte Zelle
            var check = sys.CheckFoundCity(region.Id, cell, foundName);
            using (ui.Card(check.Ok ? Tone.Good : Tone.None))
            {
                using (ui.Fields())
                {
                    foreach (var i in check.Items) ui.Field((i.Ok ? "✓ " : "✗ ") + i.Text, i.Detail ?? "", i.Ok ? Tone.Good : Tone.Warn);
                    if (check.Route != null) ui.Field($"Weg ab {realm.ActiveCity.Name}", check.Route.ToString());
                }
                ui.TextInput("Name der neuen Stadt", foundName, v => foundName = v, null, PlayerRealm.MaxNameLength);
                using (ui.Actions())
                    ui.ActionButton($"Stadt gründen ({RealmRules.FoundingCost:N0} €)", () =>
                    {
                        var r = RealmActions.Run(Runner, new FoundCityCommand(region.Id, foundName, cell));
                        if (r.Ok) { foundName = ""; foundingMode = false; }
                    }, ActionStyle.Primary, false, check.Ok);
            }
            ui.Note("Die neue Stadt läuft aggregiert (Kasse, Lieferungen), bis du hinwechselst – dann entsteht ihre Karte aus der Region.");
        }
    }
}
