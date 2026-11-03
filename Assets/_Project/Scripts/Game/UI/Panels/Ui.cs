using System;
using System.Collections.Generic;
using CityBuilder.Game.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Panels
{
    /// <summary>Farbton für Chips, Werte, Balken und Meldungen (Klassen <c>cb-tone--…</c> / <c>cb-fg--…</c> in CityHud.uss).</summary>
    public enum Tone { None, Good, Warn, Bad, Info, Accent, Muted }

    /// <summary>Aussehen eines Aktionsknopfs (<see cref="Ui.ActionButton"/>).</summary>
    public enum ActionStyle { Secondary, Primary, Danger }

    /// <summary>
    /// Baukasten für Fenster der Spiel-UI (UI Toolkit, Phase 2) im <b>Sofort-Modus</b>: Ein Fenster beschreibt seinen
    /// Inhalt bei jeder Aktualisierung neu (<c>ui.Section(…); ui.Field(…); using (ui.Card()) { … }</c>) – so knapp wie
    /// IMGUI. Unter der Haube werden die <see cref="VisualElement"/>s aber <b>wiederverwendet</b>: Jeder Aufruf nimmt das
    /// Element an der gleichen Stelle, wenn es dieselbe Art hat, und ändert nur Text/Klassen/Breiten. Dadurch bleiben
    /// Hover, gedrückte Knöpfe und die Scrollposition über Aktualisierungen erhalten, und es entsteht kaum Müll.
    /// <para>
    /// Ablauf: <see cref="Begin"/> → Aufrufe → <see cref="End"/> (entfernt übrig gebliebene Elemente). Behälter
    /// (<see cref="Card"/>, <see cref="Grid"/>, <see cref="Fields"/> …) liefern einen <see cref="Scope"/> für <c>using</c>.
    /// Klick-Aktionen werden bei jedem Durchlauf neu gesetzt; nach einem Klick ruft der Baukasten <see cref="Clicked"/>
    /// (der Fenster-Host zeichnet dann sofort neu). Die Widgets stehen in <c>Ui.Widgets.cs</c>.
    /// </para>
    /// </summary>
    public sealed partial class Ui
    {
        /// <summary>Verwaltungsdaten je Element (liegt in <c>userData</c>).</summary>
        sealed class Tag
        {
            public string Kind;
            public Action Click;
            public Action<float> Change;
            public bool Disabled;
            public string ExtraClass;
            public float Value = float.NaN;
            public Color? Color;
            public string Tip;
            public Action<Vector2> Point;
            public Action<Vector2?> Hover;
        }

        /// <summary>
        /// Anbindung an die Tooltips der HUD (gesetzt von <see cref="Hud.GameHud"/>): registriert ein Element mit einer
        /// Textquelle. Ohne HUD bleibt es null und Widgets haben einfach keinen Tooltip.
        /// </summary>
        public static Action<VisualElement, Func<string>> TooltipHook;

        /// <summary>Tooltip eines wiederverwendeten Elements; Text kommt je Durchlauf aus dem <see cref="Tag"/>.</summary>
        static void SetTip(VisualElement e, string tip)
        {
            var t = TagOf(e);
            if (t.Tip == null && tip != null && TooltipHook != null && !e.ClassListContains("cb-has-tip"))
            {
                e.AddToClassList("cb-has-tip");
                TooltipHook(e, () => (e.userData as Tag)?.Tip);
            }
            t.Tip = tip;
        }

        readonly List<(VisualElement parent, int index)> stack = new List<(VisualElement, int)>();
        VisualElement parent;
        int index;

        /// <summary>Nach jeder Klick-/Änderungsaktion (der Host zeichnet daraufhin neu).</summary>
        public System.Action Clicked;

        /// <summary>Startet einen Durchlauf in <paramref name="root"/>.</summary>
        public void Begin(VisualElement root)
        {
            stack.Clear();
            parent = root;
            index = 0;
        }

        /// <summary>Schließt offene Behälter und entfernt Elemente, die in diesem Durchlauf nicht mehr vorkamen.</summary>
        public void End()
        {
            while (stack.Count > 0) Pop();
            Trim();
            parent = null;
        }

        void Trim()
        {
            while (parent.childCount > index) parent.RemoveAt(parent.childCount - 1);
        }

        /// <summary>Behälter-Bereich: alle Aufrufe bis <see cref="Scope.Dispose"/> landen in diesem Element.</summary>
        public readonly struct Scope : IDisposable
        {
            readonly Ui ui;
            public Scope(Ui ui) { this.ui = ui; }
            public void Dispose() => ui?.Pop();
        }

        Scope Push(VisualElement container)
        {
            stack.Add((parent, index));
            parent = container;
            index = 0;
            return new Scope(this);
        }

        void Pop()
        {
            Trim();
            // Zeilen-Listen: letzte Zeile ohne Trennlinie (USS kennt kein :last-child)
            if (parent.ClassListContains("cb-fields"))
                for (int i = 0; i < parent.childCount; i++)
                    parent[i].EnableInClassList("cb-last", i == parent.childCount - 1);
            var top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            parent = top.parent;
            index = top.index;
        }

        static bool Matches(VisualElement e, string kind) => e.userData is Tag t && t.Kind == kind;

        /// <summary>
        /// Element der Art <paramref name="kind"/> an der aktuellen Stelle wiederverwenden oder neu anlegen.
        /// Fehlt gegenüber dem letzten Durchlauf ein Element, wird es übersprungen statt alles danach neu zu bauen.
        /// </summary>
        T Take<T>(string kind, Func<T> create) where T : VisualElement
        {
            if (index < parent.childCount)
            {
                var e = parent[index];
                if (Matches(e, kind) && e is T same) { index++; return same; }
                if (index + 1 < parent.childCount && Matches(parent[index + 1], kind) && parent[index + 1] is T next)
                {
                    parent.RemoveAt(index);
                    index++;
                    return next;
                }
            }
            var n = create();
            n.userData = new Tag { Kind = kind };
            parent.Insert(index++, n);
            return n;
        }

        static Tag TagOf(VisualElement e) => (Tag)e.userData;

        /// <summary>Zusätzliche, wechselnde Klasse (z. B. Farbton) setzen – die vorige wird entfernt.</summary>
        static void SetExtraClass(VisualElement e, string cls)
        {
            // auch für innere Elemente ohne eigenen Tag (z. B. das Wert-Label einer Zeile)
            if (!(e.userData is Tag t)) e.userData = t = new Tag { Kind = "inner" };
            if (t.ExtraClass == cls) return;
            if (!string.IsNullOrEmpty(t.ExtraClass)) e.RemoveFromClassList(t.ExtraClass);
            if (!string.IsNullOrEmpty(cls)) e.AddToClassList(cls);
            t.ExtraClass = cls;
        }

        // ------------------------------------------------------------ Grundelemente

        internal static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        /// <summary>Reiner Layout-Behälter ohne eigene Fläche (Klicks gehen an das Fenster dahinter).</summary>
        internal static VisualElement Box(params string[] classes)
        {
            var e = El(classes);
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        internal static Label Lbl(string cls, string text = "")
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            if (!string.IsNullOrEmpty(cls))
                foreach (var c in cls.Split(' '))
                    if (c.Length > 0) l.AddToClassList(c);
            return l;
        }

        internal static void SetText(Label l, string text)
        {
            text ??= "";
            if (l.text != text) l.text = text;
        }

        internal static void Show(VisualElement e, bool visible) => e.EnableInClassList("cb-hidden", !visible);

        /// <summary>Breite in Prozent nur bei Änderung setzen (Balken).</summary>
        static void SetWidth(VisualElement e, float fraction)
        {
            if (!(e.userData is Tag t)) e.userData = t = new Tag { Kind = "inner" };
            float v = Math.Max(0f, Math.Min(1f, float.IsNaN(fraction) ? 0f : fraction));
            if (Math.Abs(t.Value - v) < 0.0005f) return;
            t.Value = v;
            e.style.width = Length.Percent(v * 100f);
        }

        /// <summary>Hintergrundfarbe nur bei Änderung setzen (Element braucht einen <see cref="Tag"/>).</summary>
        static void SetBackground(VisualElement e, Color color)
        {
            if (!(e.userData is Tag t)) e.userData = t = new Tag { Kind = "bg" };
            if (t.Color.HasValue && t.Color.Value == color) return;
            t.Color = color;
            e.style.backgroundColor = color;
        }

        static void SetHeight(VisualElement e, float fraction)
        {
            if (!(e.userData is Tag t)) e.userData = t = new Tag { Kind = "inner" };
            float v = Math.Max(0f, Math.Min(1f, float.IsNaN(fraction) ? 0f : fraction));
            if (Math.Abs(t.Value - v) < 0.0005f) return;
            t.Value = v;
            e.style.height = Length.Percent(v * 100f);
        }

        /// <summary>Macht ein Element klickbar; die Aktion steht im <see cref="Tag"/> und wird je Durchlauf erneuert.</summary>
        VisualElement MakeClickable(VisualElement e)
        {
            e.AddManipulator(new Clickable(() =>
            {
                if (!(e.userData is Tag t) || t.Disabled || t.Click == null) return;
                t.Click();
                Clicked?.Invoke();
            }));
            return e;
        }

        /// <summary>Klickaktion + Zustand eines wiederverwendeten Elements setzen.</summary>
        static void Bind(VisualElement e, Action click, bool enabled = true)
        {
            var t = TagOf(e);
            t.Click = click;
            t.Disabled = !enabled;
            e.EnableInClassList("cb-disabled", !enabled);
        }

        internal static string ToneClass(Tone tone) => tone == Tone.None ? null : "cb-tone--" + ToneName(tone);
        internal static string FgClass(Tone tone) => tone == Tone.None ? null : "cb-fg--" + ToneName(tone);
        static string FillClass(Tone tone) => "cb-fill--" + (tone == Tone.None ? "good" : ToneName(tone));

        static string ToneName(Tone tone)
        {
            switch (tone)
            {
                case Tone.Good: return "good";
                case Tone.Warn: return "warn";
                case Tone.Bad: return "bad";
                case Tone.Info: return "info";
                case Tone.Accent: return "accent";
                case Tone.Muted: return "muted";
                default: return "none";
            }
        }

        internal static HudIcon NewIcon(HudIconKind kind, string cls = null)
        {
            var i = new HudIcon(kind);
            if (!string.IsNullOrEmpty(cls)) i.AddToClassList(cls);
            return i;
        }
    }
}
