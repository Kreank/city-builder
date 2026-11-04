using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Panels
{
    /// <summary>
    /// Bausteine für geführte Abläufe (Neues-Spiel-Fenster): Schrittanzeige, Begrüßung, Gruppen mit Erklärung,
    /// Auswahlkarten und ein großes Namensfeld. Stile in CityHud.uss (Abschnitt „Assistent“).
    /// </summary>
    public sealed partial class Ui
    {
        /// <summary>
        /// Schrittanzeige: nummerierte Punkte mit Titel und Kurztext, verbunden durch eine Linie. Erledigte Schritte sind
        /// gefüllt, der aktuelle hervorgehoben; <paramref name="reachable"/> = false → ausgegraut und nicht anklickbar.
        /// </summary>
        public void WizardSteps(IReadOnlyList<string> names, IReadOnlyList<string> hints, int current, Func<int, bool> reachable, Action<int> select)
        {
            var e = Take("wizard-steps", () => Box("cb-steps"));
            int want = names.Count * 2 - 1;
            if (e.childCount != want)
            {
                e.Clear();
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0) e.Add(Box("cb-steps__line"));
                    var s = MakeClickable(El("cb-step"));
                    s.userData = new Tag { Kind = "step" };
                    var dot = Box("cb-step__dot");
                    dot.Add(Lbl("cb-step__num cb-heading"));
                    s.Add(dot);
                    var text = Box("cb-step__text");
                    text.Add(Lbl("cb-step__title"));
                    text.Add(Lbl("cb-step__hint"));
                    s.Add(text);
                    e.Add(s);
                }
            }
            for (int i = 0; i < names.Count; i++)
            {
                int k = i;
                var s = e[i * 2];
                SetText((Label)s[0][0], (i + 1).ToString());
                SetText((Label)s[1][0], names[i]);
                string hint = hints != null && i < hints.Count ? hints[i] : null;
                SetText((Label)s[1][1], hint);
                Show(s[1][1], !string.IsNullOrEmpty(hint));
                bool can = i <= current || reachable == null || reachable(i);
                s.EnableInClassList("cb-on", i == current);
                s.EnableInClassList("cb-step--done", i < current);
                Bind(s, () => select(k), can);
                if (i > 0) e[i * 2 - 1].EnableInClassList("cb-steps__line--done", i <= current);
            }
        }

        /// <summary>Begrüßungsfläche oben im Fenster: kleine Überzeile, großer Titel, erklärender Text.</summary>
        public void Hero(string kicker, string title, string text)
        {
            var e = Take("hero", () =>
            {
                var h = Box("cb-hero");
                h.Add(Lbl("cb-hero__kicker"));
                h.Add(Lbl("cb-hero__title cb-heading"));
                h.Add(Lbl("cb-hero__text"));
                return h;
            });
            SetText((Label)e[0], kicker);
            Show(e[0], !string.IsNullOrEmpty(kicker));
            SetText((Label)e[1], title);
            SetText((Label)e[2], text);
            Show(e[2], !string.IsNullOrEmpty(text));
        }

        /// <summary>
        /// Gruppe mit Kopf (Titel, Hinweis rechts), Erklärsatz und Inhalt – der Inhalt landet im zurückgegebenen Bereich.
        /// </summary>
        public Scope Group(string title, string hint, string description)
        {
            var e = Take("group", () =>
            {
                var g = Box("cb-group");
                var head = Box("cb-group__head");
                head.Add(Lbl("cb-group__title cb-heading"));
                head.Add(Lbl("cb-group__hint"));
                g.Add(head);
                g.Add(Lbl("cb-group__desc"));
                g.Add(Box("cb-group__body"));
                return g;
            });
            SetText((Label)e[0][0], title);
            SetText((Label)e[0][1], hint);
            Show(e[0][1], !string.IsNullOrEmpty(hint));
            SetText((Label)e[1], description);
            Show(e[1], !string.IsNullOrEmpty(description));
            return Push(e[2]);
        }

        /// <summary>Raster für <see cref="OptionCard"/>s (drei Spalten, umbrechend).</summary>
        public Scope OptionGrid() => Push(Take("option-grid", () => Box("cb-option-grid")));

        /// <summary>
        /// Auswahlkarte (Umschalter) mit Titel, Beschreibung und kleiner Zahl rechts oben. <paramref name="on"/> = gewählt,
        /// <paramref name="enabled"/> = false → ausgegraut (Tooltip bleibt), <paramref name="rare"/> = Akzentrand.
        /// </summary>
        public void OptionCard(string title, string text, bool on, Action onClick, bool enabled = true, bool rare = false,
            string badge = null, string tip = null)
        {
            var cell = Take("option", () =>
            {
                var c = Box("cb-option-cell");
                var card = MakeClickable(El("cb-option"));
                card.userData = new Tag { Kind = "option-card" };
                var head = Box("cb-option__head");
                head.Add(Box("cb-option__check"));
                head.Add(Lbl("cb-option__title"));
                head.Add(Lbl("cb-option__badge"));
                card.Add(head);
                card.Add(Lbl("cb-option__text"));
                c.Add(card);
                return c;
            });
            var e = cell[0];
            SetText((Label)e[0][1], title);
            var b = (Label)e[0][2];
            SetText(b, badge);
            Show(b, !string.IsNullOrEmpty(badge));
            SetText((Label)e[1], text);
            Show(e[1], !string.IsNullOrEmpty(text));
            e.EnableInClassList("cb-on", on);
            e.EnableInClassList("cb-option--rare", rare && !on);
            Bind(e, onClick, enabled);
            SetTip(e, tip);
        }

        /// <summary>
        /// Großes Eingabefeld ohne Bezeichnungsspalte (z. B. Stadtname) mit Platzhaltertext. Sonst wie <see cref="TextInput"/>.
        /// </summary>
        public void BigTextInput(string value, Action<string> onChange, Action onSubmit = null, int maxLength = 60, string placeholder = null)
        {
            var e = Take("big-text-input", () =>
            {
                var r = Box("cb-big-input");
                r.Add(NewTextField(maxLength, "cb-text-input--large"));
                return r;
            });
            var field = (TextField)e[0];
            if (placeholder != null && field.textEdition.placeholder != placeholder)
            {
                field.textEdition.placeholder = placeholder;
                field.textEdition.hidePlaceholderOnFocus = false;
            }
            BindTextField(field, value, onChange, onSubmit);
        }
    }
}
