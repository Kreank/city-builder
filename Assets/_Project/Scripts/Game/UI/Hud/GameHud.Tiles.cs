using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Bauleiste, aufgeräumt (Spieltest-Feedback „zu viele Symbole“): Kacheln mit **Vorschaubild** des 3D-Modells
    /// (<see cref="ModelThumbnails"/>; ohne Modell ein schlichtes Icon in der Farbe der Gruppe) und **Gruppen-Reiter**
    /// (z. B. Versorgung: Strom erzeugen | Strom verteilen | Wasser | Abwasser | Werkzeuge) statt einer langen Kachelreihe.
    /// Kachel = Bild + Name + eine kurze Unterzeile (Kosten), alles Weitere im Tooltip.
    /// <para>Der gezeigte Reiter folgt dem Werkzeug: wechselt die aktive Kachel (Taste, Fenster, Klick) in eine andere Gruppe,
    /// springt der Reiter mit; ein Klick auf einen Reiter wählt nichts aus (man kann stöbern, ohne dass ein Geist erscheint).
    /// Der zuletzt gezeigte Reiter je Kategorie bleibt gemerkt.</para>
    /// </summary>
    public sealed partial class GameHud
    {
        ModelThumbnails thumbnails;
        ModelThumbnails Thumbs => thumbnails ??= new ModelThumbnails();

        /// <summary>Zuletzt gezeigter Reiter je Kategorie (Name der Gruppe).</summary>
        readonly Dictionary<Category, string> shownGroup = new Dictionary<Category, string>();

        // ------------------------------------------------------------ Kachel mit Vorschaubild

        /// <summary>
        /// Kachel mit Vorschaubild: <paramref name="thumbKey"/> + <paramref name="build"/> (null = kein Modell → Icon
        /// <paramref name="icon"/> in der Gruppenfarbe <paramref name="accent"/>).
        /// </summary>
        VisualElement ThumbTile(string name, string sub, Color accent, HudIconKind icon, string thumbKey, Func<GameObject> build,
            Action onClick, Func<bool> isOn, Func<string> tip)
        {
            var t = El("cb-tile", "cb-tile--thumb");
            var thumb = Container("cb-tile__thumb");
            thumb.style.borderBottomColor = accent;
            var fallback = Icon(icon, "cb-tile__thumb-icon");
            fallback.style.color = accent;
            thumb.Add(fallback);
            t.Add(thumb);
            if (build != null && thumbKey != null) Thumbs.Bind(thumb, fallback, thumbKey, build);
            t.Add(Text(name, "cb-tile__name"));
            if (sub != null) t.Add(Text(sub, "cb-tile__sub"));
            OnClick(t, onClick);
            if (isOn != null) panelFastRefresh.Add(() => t.EnableInClassList("cb-on", isOn()));
            Tip(t, tip);
            return t;
        }

        VisualElement ThumbTile(string name, string sub, Color accent, HudIconKind icon, string thumbKey, Func<GameObject> build,
            Action onClick, Func<bool> isOn, string tip) =>
            ThumbTile(name, sub, accent, icon, thumbKey, build, onClick, isOn, tip == null ? null : (Func<string>)(() => tip));

        // ------------------------------------------------------------ Gruppen-Reiter

        sealed class TileGroup
        {
            public string Name;
            public VisualElement Tab, Tiles;
            public Label Count;
            public readonly List<Func<bool>> Active = new List<Func<bool>>();

            public bool AnyActive()
            {
                foreach (var a in Active) if (a()) return true;
                return false;
            }
        }

        sealed class TileGroups
        {
            public Category Category;
            public readonly List<TileGroup> Groups = new List<TileGroup>();
            public VisualElement Tabs, Body;
            public TileGroup Shown, LastActive;
        }

        TileGroups BeginGroups(Category c)
        {
            var g = new TileGroups { Category = c };
            g.Tabs = Container("cb-group-tabs");
            g.Body = Container("cb-group-body");
            return g;
        }

        /// <summary>Neue Gruppe (Reiter + eigene Kachelreihe).</summary>
        TileGroup AddGroup(TileGroups groups, string name, HudIconKind icon, Color accent, string tip)
        {
            var grp = new TileGroup { Name = name };
            var tab = El("cb-group-tab");
            var dot = Icon(icon, "cb-icon--s");
            dot.style.color = accent;
            tab.Add(dot);
            tab.Add(Text(name, "cb-group-tab__label"));
            var count = Text("", "cb-group-tab__count");
            tab.Add(count);
            grp.Count = count;
            OnClick(tab, () => ShowGroup(groups, grp));
            Tip(tab, tip);
            grp.Tab = tab;
            grp.Tiles = Container("cb-tiles");
            groups.Tabs.Add(tab);
            groups.Body.Add(grp.Tiles);
            groups.Groups.Add(grp);
            return grp;
        }

        /// <summary>Kachel in die Gruppe; <paramref name="isOn"/> lässt den Reiter dem Werkzeug folgen.</summary>
        void AddToGroup(TileGroup grp, VisualElement tile, Func<bool> isOn)
        {
            grp.Tiles.Add(tile);
            if (isOn != null) grp.Active.Add(isOn);
        }

        void ShowGroup(TileGroups groups, TileGroup grp)
        {
            groups.Shown = grp;
            shownGroup[groups.Category] = grp.Name;
            foreach (var g in groups.Groups)
            {
                bool on = g == grp;
                g.Tab.EnableInClassList("cb-on", on);
                g.Tiles.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Reiter + Kachelbereich ins Panel hängen; Startreiter = aktive Kachel, sonst gemerkter, sonst erster.</summary>
        void FinishGroups(TileGroups groups)
        {
            groups.Groups.RemoveAll(g =>
            {
                if (g.Tiles.childCount > 0) return false;
                g.Tab.RemoveFromHierarchy();
                g.Tiles.RemoveFromHierarchy();
                return true;
            });
            if (groups.Groups.Count == 0) return;
            foreach (var g in groups.Groups) g.Count.text = g.Tiles.childCount.ToString();
            panelBody.Add(groups.Tabs);
            panelBody.Add(groups.Body);

            TileGroup start = null;
            if (shownGroup.TryGetValue(groups.Category, out var remembered))
                start = groups.Groups.Find(g => g.Name == remembered);
            ShowGroup(groups, start ?? groups.Groups[0]);

            // Reiter folgt dem Werkzeug, wenn die aktive Kachel in eine andere Gruppe wechselt
            panelFastRefresh.Add(() =>
            {
                TileGroup active = null;
                foreach (var g in groups.Groups)
                    if (g.AnyActive()) { active = g; break; }
                if (active != null && active != groups.LastActive && active != groups.Shown) ShowGroup(groups, active);
                groups.LastActive = active;
                foreach (var g in groups.Groups) g.Tab.EnableInClassList("cb-group-tab--active", g == active);
            });
        }
    }
}
