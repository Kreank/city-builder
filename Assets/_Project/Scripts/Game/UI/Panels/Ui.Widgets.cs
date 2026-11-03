using System;
using System.Collections.Generic;
using CityBuilder.Game.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Panels
{
    /// <summary>
    /// Widgets des Fenster-Baukastens. Jede Methode beschreibt ein Element an der aktuellen Stelle (s. <see cref="Ui"/>).
    /// Aussehen in <c>Resources/UI/CityHud.uss</c>, Abschnitt „Fenster (Phase 2)“.
    /// </summary>
    public sealed partial class Ui
    {
        // ------------------------------------------------------------ Behälter

        /// <summary>Spalte (Elemente untereinander).</summary>
        public Scope Col(string cls = null)
        {
            var e = Take("col", () => Box("cb-col"));
            SetExtraClass(e, cls);
            return Push(e);
        }

        /// <summary>Zeile (Elemente nebeneinander, mittig ausgerichtet).</summary>
        public Scope Row(string cls = null)
        {
            var e = Take("row", () => Box("cb-row"));
            SetExtraClass(e, cls);
            return Push(e);
        }

        /// <summary>Karte (Fläche mit Rand) für zusammengehörige Inhalte.</summary>
        public Scope Card(Tone tone = Tone.None)
        {
            var e = Take("card", () => El("cb-card"));
            SetExtraClass(e, ToneClass(tone));
            return Push(e);
        }

        /// <summary>Karte mit Zeilen <see cref="Field"/>/<see cref="FieldLink"/>/<see cref="Stepper"/> und Trennlinien.</summary>
        public Scope Fields()
        {
            var e = Take("fields", () => El("cb-card", "cb-fields"));
            return Push(e);
        }

        /// <summary>Raster mit 2 oder 3 Spalten für <see cref="Stat"/> und <see cref="Need"/>.</summary>
        public Scope Grid(int columns = 2)
        {
            var e = Take("grid", () => Box("cb-grid"));
            SetExtraClass(e, columns >= 3 ? "cb-grid--3" : "cb-grid--2");
            return Push(e);
        }

        /// <summary>Reihe von Chips (bricht um).</summary>
        public Scope Chips()
        {
            var e = Take("chips", () => Box("cb-chips"));
            return Push(e);
        }

        /// <summary>Reihe gleich breiter Aktionsknöpfe (<see cref="ActionButton"/>).</summary>
        public Scope Actions()
        {
            var e = Take("actions", () => Box("cb-actions"));
            return Push(e);
        }

        // ------------------------------------------------------------ Abstand / Text

        public void Space(float px = 8f)
        {
            var e = Take("space", () => Box("cb-space"));
            var t = TagOf(e);
            if (Math.Abs(t.Value - px) > 0.01f) { t.Value = px; e.style.height = px; }
        }

        /// <summary>Füllt den Rest einer <see cref="Row"/>.</summary>
        public void Spacer() => Take("spacer", () => Box("cb-spacer"));

        public void Divider() => Take("divider", () => Box("cb-divider"));

        /// <summary>Abschnittsüberschrift, optional mit kurzem Hinweis rechts.</summary>
        public void Section(string title, string hint = null)
        {
            var e = Take("section", () =>
            {
                var s = Box("cb-section");
                s.Add(Lbl("cb-section__title cb-heading"));
                s.Add(Lbl("cb-section__hint"));
                return s;
            });
            SetText((Label)e[0], title);
            SetText((Label)e[1], hint);
            Show(e[1], !string.IsNullOrEmpty(hint));
        }

        /// <summary>Fließtext (bricht um, Rich Text erlaubt).</summary>
        public Label Text(string text, Tone tone = Tone.None)
        {
            var l = Take("text", () => Lbl("cb-text"));
            SetText(l, text);
            SetExtraClass(l, FgClass(tone));
            return l;
        }

        /// <summary>Gedämpfter Hinweistext.</summary>
        public void Note(string text)
        {
            var l = Take("note", () => Lbl("cb-note"));
            SetText(l, text);
        }

        /// <summary>Großer Titel mit Untertitel (Fensterkopf), optional mit Avatar (Initialen) links.</summary>
        public void Title(string title, string subtitle, string avatar = null, Color? avatarColor = null)
        {
            var e = Take("title", () =>
            {
                var r = Box("cb-title-row");
                var av = Box("cb-avatar", "cb-avatar--l");
                av.Add(Lbl("cb-avatar__text cb-heading"));
                r.Add(av);
                var c = Box("cb-title-block");
                c.Add(Lbl("cb-title cb-heading"));
                c.Add(Lbl("cb-subtitle"));
                r.Add(c);
                return r;
            });
            Show(e[0], !string.IsNullOrEmpty(avatar));
            if (!string.IsNullOrEmpty(avatar))
            {
                SetText((Label)e[0][0], avatar);
                SetBackground(e[0], avatarColor ?? AvatarColor(0));
            }
            SetText((Label)e[1][0], title);
            SetText((Label)e[1][1], subtitle);
            Show(e[1][1], !string.IsNullOrEmpty(subtitle));
        }

        // ------------------------------------------------------------ Zeilen Label/Wert

        /// <summary>Zeile „Bezeichnung … Wert“ (in <see cref="Fields"/> mit Trennlinie).</summary>
        public void Field(string label, string value, Tone tone = Tone.None)
        {
            var e = Take("field", () =>
            {
                var r = Box("cb-field");
                r.Add(Lbl("cb-field__label"));
                r.Add(Lbl("cb-field__value"));
                return r;
            });
            SetText((Label)e[0], label);
            SetText((Label)e[1], value);
            SetExtraClass(e[1], FgClass(tone));
        }

        /// <summary>Zeile mit anklickbarem Wert (Verweis auf ein anderes Objekt).</summary>
        public void FieldLink(string label, string value, System.Action onClick, Tone tone = Tone.None)
        {
            var e = Take("field-link", () =>
            {
                var r = MakeClickable(El("cb-field", "cb-field--link"));
                r.Add(Lbl("cb-field__label"));
                r.Add(Lbl("cb-field__value"));
                r.Add(Lbl("cb-field__chevron", "›"));
                return r;
            });
            SetText((Label)e[0], label);
            SetText((Label)e[1], value);
            SetExtraClass(e[1], FgClass(tone));
            Bind(e, onClick);
        }

        /// <summary>Summenzeile (fett, z. B. Saldo).</summary>
        public void TotalRow(string label, string value, Tone tone = Tone.None)
        {
            var e = Take("total", () =>
            {
                var r = Box("cb-field", "cb-field--total");
                r.Add(Lbl("cb-field__label"));
                r.Add(Lbl("cb-field__value"));
                return r;
            });
            SetText((Label)e[0], label);
            SetText((Label)e[1], value);
            SetExtraClass(e[1], FgClass(tone));
        }

        /// <summary>Tabellenzeile: Bezeichnung + zwei rechtsbündige Spalten (z. B. letzte/laufende Woche).</summary>
        public void Columns(string label, string a, string b, Tone toneA = Tone.None, Tone toneB = Tone.None, bool header = false)
        {
            var e = Take("cols", () =>
            {
                var r = Box("cb-cols");
                r.Add(Lbl("cb-cols__label"));
                r.Add(Lbl("cb-cols__cell"));
                r.Add(Lbl("cb-cols__cell"));
                return r;
            });
            e.EnableInClassList("cb-cols--header", header);
            SetText((Label)e[0], label);
            SetText((Label)e[1], a);
            SetText((Label)e[2], b);
            SetExtraClass(e[1], FgClass(toneA));
            SetExtraClass(e[2], FgClass(toneB));
        }

        // ------------------------------------------------------------ Kennzahlen und Balken

        /// <summary>Kennzahl-Kachel (in <see cref="Grid"/>): Bezeichnung, großer Wert, kleine Einheit.</summary>
        public void Stat(string label, string value, string unit = null, Tone tone = Tone.None)
        {
            var cell = Take("stat", () =>
            {
                var c = Box("cb-cell");
                var tile = Box("cb-stat-tile");
                tile.Add(Lbl("cb-stat-tile__label"));
                var row = Box("cb-stat-tile__row");
                row.Add(Lbl("cb-stat-tile__value"));
                row.Add(Lbl("cb-stat-tile__unit"));
                tile.Add(row);
                c.Add(tile);
                return c;
            });
            var t = cell[0];
            SetText((Label)t[0], label);
            SetText((Label)t[1][0], value);
            SetText((Label)t[1][1], unit);
            Show(t[1][1], !string.IsNullOrEmpty(unit));
            SetExtraClass(t[1][0], FgClass(tone));
        }

        /// <summary>Fortschrittsbalken 0..1, optional mit Beschriftung links/rechts darüber.</summary>
        public void Progress(float fraction, Tone tone = Tone.Good, string left = null, string right = null)
        {
            var e = Take("progress", () =>
            {
                var c = Box("cb-progress-block");
                var caps = Box("cb-progress__caps");
                caps.Add(Lbl("cb-progress__cap"));
                caps.Add(Lbl("cb-progress__cap"));
                c.Add(caps);
                var track = Box("cb-progress");
                var fill = Box("cb-progress__fill");
                fill.userData = new Tag { Kind = "fill" };
                track.Add(fill);
                c.Add(track);
                return c;
            });
            bool caps2 = !string.IsNullOrEmpty(left) || !string.IsNullOrEmpty(right);
            Show(e[0], caps2);
            SetText((Label)e[0][0], left);
            SetText((Label)e[0][1], right);
            var f = e[1][0];
            SetWidth(f, fraction);
            SetExtraClass(f, FillClass(tone));
        }

        /// <summary>Kennwert mit Balken: Bezeichnung und Wert in einer Zeile, darunter der Balken (z. B. Zufriedenheit).</summary>
        public void Meter(string label, string valueText, float fraction, Tone tone = Tone.Good)
        {
            var e = Take("meter", () =>
            {
                var c = Box("cb-meter");
                var head = Box("cb-meter__head");
                head.Add(Lbl("cb-meter__label"));
                head.Add(Lbl("cb-meter__value"));
                c.Add(head);
                var track = Box("cb-progress");
                var fill = Box("cb-progress__fill");
                fill.userData = new Tag { Kind = "fill" };
                track.Add(fill);
                c.Add(track);
                return c;
            });
            SetText((Label)e[0][0], label);
            SetText((Label)e[0][1], valueText);
            SetExtraClass(e[0][1], FgClass(tone));
            SetWidth(e[1][0], fraction);
            SetExtraClass(e[1][0], FillClass(tone));
        }

        /// <summary>Farbton für eine Erfüllung 0..1 (gut ≥ 0,6, knapp ≥ 0,35, sonst schlecht).</summary>
        public static Tone FulfilmentTone(float v) => v >= 0.6f ? Tone.Good : v >= 0.35f ? Tone.Accent : Tone.Bad;

        /// <summary>Bedürfnisbalken (für ein <see cref="Grid"/> mit 2 Spalten): Name, Wort, dünner Balken; Farbe aus der Erfüllung.</summary>
        public void Need(string label, string valueText, float fulfilment)
        {
            var cell = Take("need", () =>
            {
                var c = Box("cb-cell", "cb-cell--need");
                var head = Box("cb-need__head");
                head.Add(Lbl("cb-need__label"));
                head.Add(Lbl("cb-need__value"));
                c.Add(head);
                var track = Box("cb-progress", "cb-progress--thin");
                var fill = Box("cb-progress__fill");
                fill.userData = new Tag { Kind = "fill" };
                track.Add(fill);
                c.Add(track);
                return c;
            });
            var tone = FulfilmentTone(fulfilment);
            SetText((Label)cell[0][0], label);
            SetText((Label)cell[0][1], valueText);
            SetExtraClass(cell[0][1], tone == Tone.Good ? null : "cb-fg--warn");
            SetWidth(cell[1][0], fulfilment);
            SetExtraClass(cell[1][0], FillClass(tone));
        }

        /// <summary>Waagrechter Mengenbalken (z. B. Altersgruppen): Bezeichnung, Balken relativ zu <paramref name="max"/>, Zahl.</summary>
        public void ValueBar(string label, float value, float max, Color color, string valueText)
        {
            var e = Take("vbar", () =>
            {
                var r = Box("cb-vbar");
                r.Add(Lbl("cb-vbar__label"));
                var track = Box("cb-vbar__track");
                var fill = Box("cb-vbar__fill");
                fill.userData = new Tag { Kind = "fill" };
                track.Add(fill);
                r.Add(track);
                r.Add(Lbl("cb-vbar__value"));
                return r;
            });
            SetText((Label)e[0], label);
            var fillEl = e[1][0];
            SetWidth(fillEl, max > 0f ? value / max : 0f);
            SetBackground(fillEl, color);
            SetText((Label)e[2], valueText);
        }

        /// <summary>
        /// Mini-Balkendiagramm (z. B. Saldo je Woche): positive Werte grün nach oben, negative orange nach unten,
        /// skaliert auf den größten Betrag. <paramref name="caption"/> steht oben links (z. B. „±12.000 €“).
        /// </summary>
        public void WeekBars(IReadOnlyList<double> values, string caption, int slots = 12)
        {
            var e = Take("weekbars", () =>
            {
                var c = Box("cb-chart");
                c.Add(Lbl("cb-chart__caption"));
                c.Add(Box("cb-chart__bars"));
                return c;
            });
            SetText((Label)e[0], caption);
            var bars = e[1];
            slots = Math.Max(slots, values.Count);
            while (bars.childCount < slots)
            {
                var col = Box("cb-chart__col");
                var up = Box("cb-chart__half", "cb-chart__half--up");
                var upBar = Box("cb-chart__bar", "cb-chart__bar--pos");
                upBar.userData = new Tag { Kind = "bar" };
                up.Add(upBar);
                var down = Box("cb-chart__half", "cb-chart__half--down");
                var downBar = Box("cb-chart__bar", "cb-chart__bar--neg");
                downBar.userData = new Tag { Kind = "bar" };
                down.Add(downBar);
                col.Add(up);
                col.Add(down);
                bars.Add(col);
            }
            while (bars.childCount > slots) bars.RemoveAt(bars.childCount - 1);

            double max = 1;
            foreach (var v in values) max = Math.Max(max, Math.Abs(v));
            int offset = slots - values.Count; // rechtsbündig: neueste Woche rechts
            for (int i = 0; i < slots; i++)
            {
                double v = i >= offset ? values[i - offset] : 0;
                var col = bars[i];
                SetHeight(col[0][0], v > 0 ? (float)(v / max) : 0f);
                SetHeight(col[1][0], v < 0 ? (float)(-v / max) : 0f);
            }
        }

        // ------------------------------------------------------------ Chips, Listen, Hinweise

        /// <summary>Chip (Pille) in einer <see cref="Chips"/>-Reihe oder im Fensterkopf; optional mit Farbpunkt.</summary>
        public void Chip(string text, Tone tone = Tone.None, Color? dot = null)
        {
            var e = Take("chip", () =>
            {
                var c = Box("cb-pill");
                var d = Box("cb-pill__dot");
                c.Add(d);
                c.Add(Lbl("cb-pill__text"));
                return c;
            });
            SetExtraClass(e, ToneClass(tone == Tone.None ? Tone.Muted : tone));
            Show(e[0], dot.HasValue);
            if (dot.HasValue) SetBackground(e[0], dot.Value);
            SetText((Label)e[1], text);
        }

        /// <summary>
        /// Anklickbare Listenzeile mit Avatar (Initialen auf Farbfläche), Titel, Untertitel und rechts einem Chip.
        /// <paramref name="onClick"/> = null → nicht anklickbar.
        /// </summary>
        public void ListItem(string initials, Color avatar, string title, string sub, string badge = null, Tone badgeTone = Tone.None,
            System.Action onClick = null)
        {
            var e = Take("list-item", () =>
            {
                var r = MakeClickable(El("cb-list-item"));
                var av = Box("cb-avatar");
                av.Add(Lbl("cb-avatar__text cb-heading"));
                r.Add(av);
                var body = Box("cb-list-item__body");
                body.Add(Lbl("cb-list-item__title"));
                body.Add(Lbl("cb-list-item__sub"));
                r.Add(body);
                r.Add(Lbl("cb-pill cb-list-item__badge"));
                return r;
            });
            var avatarEl = e[0];
            SetBackground(avatarEl, avatar);
            SetText((Label)avatarEl[0], initials);
            SetText((Label)e[1][0], title);
            SetText((Label)e[1][1], sub);
            Show(e[1][1], !string.IsNullOrEmpty(sub));
            var b = (Label)e[2];
            SetText(b, badge);
            Show(b, !string.IsNullOrEmpty(badge));
            SetExtraClass(b, ToneClass(badgeTone == Tone.None ? Tone.Muted : badgeTone));
            Bind(e, onClick);
            e.EnableInClassList("cb-list-item--static", onClick == null);
        }

        /// <summary>Hervorgehobener Satz, z. B. eine Erinnerung („Erinnert sich“).</summary>
        public void Quote(string caption, string text)
        {
            var e = Take("quote", () =>
            {
                var c = Box("cb-quote");
                c.Add(Lbl("cb-quote__caption"));
                c.Add(Lbl("cb-quote__text"));
                return c;
            });
            SetText((Label)e[0], caption);
            Show(e[0], !string.IsNullOrEmpty(caption));
            SetText((Label)e[1], text);
        }

        /// <summary>Meldung mit Farbstreifen (wie im Entwurf „Meldungen“), z. B. Warnung im Fenster.</summary>
        public void Message(string title, string text, Tone tone = Tone.Info)
        {
            var e = Take("message", () =>
            {
                var c = Box("cb-message");
                c.Add(Box("cb-message__stripe"));
                var body = Box("cb-message__body");
                body.Add(Lbl("cb-message__title"));
                body.Add(Lbl("cb-message__text"));
                c.Add(body);
                return c;
            });
            SetExtraClass(e, ToneClass(tone));
            SetText((Label)e[1][0], title);
            SetText((Label)e[1][1], text);
            Show(e[1][1], !string.IsNullOrEmpty(text));
        }

        // ------------------------------------------------------------ Bedienelemente

        /// <summary>Reiter (Segment über die volle Breite). <paramref name="select"/> bekommt den angeklickten Index.</summary>
        public void Tabs(IReadOnlyList<string> names, int current, Action<int> select, bool small = false)
        {
            var e = Take("tabs", () => Box("cb-tabs"));
            e.EnableInClassList("cb-tabs--small", small);
            while (e.childCount < names.Count)
            {
                var tab = MakeClickable(El("cb-tab"));
                tab.userData = new Tag { Kind = "tab" };
                tab.Add(Lbl("cb-tab__text"));
                e.Add(tab);
            }
            while (e.childCount > names.Count) e.RemoveAt(e.childCount - 1);
            for (int i = 0; i < names.Count; i++)
            {
                int k = i;
                var tab = e[i];
                SetText((Label)tab[0], names[i]);
                tab.EnableInClassList("cb-on", i == current);
                Bind(tab, () => select(k));
            }
        }

        /// <summary>Aktionsknopf (in <see cref="Actions"/> gleich breit). <paramref name="on"/> = eingeschalteter Umschalter.</summary>
        public void ActionButton(string label, System.Action onClick, ActionStyle style = ActionStyle.Secondary, bool on = false,
            bool enabled = true, string tip = null)
        {
            var e = Take("action", () =>
            {
                var b = MakeClickable(El("cb-action"));
                b.Add(Lbl("cb-action__text"));
                return b;
            });
            SetText((Label)e[0], label);
            SetExtraClass(e, style == ActionStyle.Primary ? "cb-action--primary" : style == ActionStyle.Danger ? "cb-action--danger" : null);
            e.EnableInClassList("cb-on", on);
            Bind(e, onClick, enabled);
            SetTip(e, tip);
        }

        /// <summary>Kleiner quadratischer Knopf mit Icon (Fensterkopf: Zurück, Folgen, Schließen).</summary>
        public void IconButton(HudIconKind icon, System.Action onClick, bool on = false, bool enabled = true, string tip = null)
        {
            var e = Take("icon-btn", () =>
            {
                var b = MakeClickable(El("cb-head-btn"));
                b.Add(NewIcon(icon, "cb-icon--s"));
                return b;
            });
            ((HudIcon)e[0]).Kind = icon;
            e.EnableInClassList("cb-on", on);
            Bind(e, onClick, enabled);
            SetTip(e, tip);
        }

        /// <summary>Zeile mit −/+ (z. B. Steuersatz): Bezeichnung, −, Wert, +, rechts ein Zusatz (z. B. Wirkung).</summary>
        public void Stepper(string label, string value, System.Action minus, System.Action plus, string extra = null, Tone extraTone = Tone.None)
        {
            var e = Take("stepper", () =>
            {
                var r = Box("cb-field", "cb-stepper");
                r.Add(Lbl("cb-field__label"));
                var minusBtn = MakeClickable(El("cb-step-btn"));
                minusBtn.userData = new Tag { Kind = "step" };
                minusBtn.Add(Lbl("cb-step-btn__text", "−"));
                r.Add(minusBtn);
                r.Add(Lbl("cb-stepper__value"));
                var plusBtn = MakeClickable(El("cb-step-btn"));
                plusBtn.userData = new Tag { Kind = "step" };
                plusBtn.Add(Lbl("cb-step-btn__text", "+"));
                r.Add(plusBtn);
                r.Add(Lbl("cb-stepper__extra"));
                return r;
            });
            SetText((Label)e[0], label);
            Bind(e[1], minus, minus != null);
            SetText((Label)e[2], value);
            Bind(e[3], plus, plus != null);
            var x = (Label)e[4];
            SetText(x, extra);
            SetExtraClass(x, FgClass(extraTone));
        }

        /// <summary>Schieberegler mit Bezeichnung und Wertanzeige.</summary>
        public void Slider(string label, float value, float min, float max, Action<float> onChange, string valueText = null)
        {
            var e = Take("slider", () =>
            {
                var r = Box("cb-field", "cb-slider-row");
                r.Add(Lbl("cb-field__label"));
                var s = new Slider(min, max);
                s.AddToClassList("cb-slider");
                r.Add(s);
                r.Add(Lbl("cb-stepper__value"));
                return r;
            });
            var tag = TagOf(e);
            tag.Change = onChange;
            var slider = (Slider)e[1];
            if (slider.userData == null)
            {
                slider.userData = e; // Rückverweis für den Rückruf
                slider.RegisterValueChangedCallback(ev =>
                {
                    var owner = (VisualElement)slider.userData;
                    TagOf(owner).Change?.Invoke(ev.newValue);
                    Clicked?.Invoke();
                });
            }
            slider.lowValue = min;
            slider.highValue = max;
            if (!Mathf.Approximately(slider.value, value)) slider.SetValueWithoutNotify(value);
            SetText((Label)e[0], label);
            SetText((Label)e[2], valueText ?? $"{value:0.##}");
        }

        /// <summary>Blättern: ‹ Seite x/y ›. Ohne mehrere Seiten unsichtbar.</summary>
        public void Pager(int page, int pages, Action<int> setPage)
        {
            var e = Take("pager", () =>
            {
                var r = Box("cb-pager");
                var prev = MakeClickable(El("cb-step-btn"));
                prev.userData = new Tag { Kind = "step" };
                prev.Add(Lbl("cb-step-btn__text", "‹"));
                r.Add(prev);
                r.Add(Lbl("cb-pager__text"));
                var next = MakeClickable(El("cb-step-btn"));
                next.userData = new Tag { Kind = "step" };
                next.Add(Lbl("cb-step-btn__text", "›"));
                r.Add(next);
                return r;
            });
            Show(e, pages > 1);
            SetText((Label)e[1], $"Seite {page + 1} von {pages}");
            Bind(e[0], () => setPage(page - 1), page > 0);
            Bind(e[2], () => setPage(page + 1), page < pages - 1);
        }

        // ------------------------------------------------------------ Hilfen

        /// <summary>Initialen aus einem Namen („Anna Berger“ → „AB“).</summary>
        public static string Initials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        static readonly Color[] AvatarColors =
        {
            BuildLabels.Hex(0x5A4632), BuildLabels.Hex(0x2F4A63), BuildLabels.Hex(0x3C5536),
            BuildLabels.Hex(0x4D3F62), BuildLabels.Hex(0x5E3838), BuildLabels.Hex(0x35525A),
        };

        /// <summary>Stabile, gedämpfte Avatar-Farbe zu einer Id.</summary>
        public static Color AvatarColor(int id) => AvatarColors[(id % AvatarColors.Length + AvatarColors.Length) % AvatarColors.Length];
    }
}
