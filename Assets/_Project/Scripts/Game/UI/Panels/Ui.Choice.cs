using System;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Panels
{
    /// <summary>Auswahl-Chips für den Baukasten (Spielstart: Vorlieben im Neues-Spiel-Fenster).</summary>
    public sealed partial class Ui
    {
        /// <summary>
        /// Anklickbarer Chip (Umschalter) in einer <see cref="Chips"/>-Reihe: <paramref name="on"/> = gewählt,
        /// <paramref name="enabled"/> = false → ausgegraut (Tooltip bleibt), <paramref name="rare"/> = Akzentrand („selten“),
        /// <paramref name="badge"/> = kleine Zahl/Notiz rechts (z. B. Treffer).
        /// </summary>
        public void ChoiceChip(string text, bool on, Action onClick, bool enabled = true, bool rare = false, string badge = null, string tip = null)
        {
            var e = Take("choice", () =>
            {
                var c = MakeClickable(El("cb-pill", "cb-choice"));
                c.Add(Lbl("cb-pill__text"));
                c.Add(Lbl("cb-choice__badge"));
                return c;
            });
            SetText((Label)e[0], text);
            var b = (Label)e[1];
            SetText(b, badge);
            Show(b, !string.IsNullOrEmpty(badge));
            e.EnableInClassList("cb-on", on);
            e.EnableInClassList("cb-choice--rare", rare && !on);
            Bind(e, onClick, enabled);
            SetTip(e, tip);
        }
    }
}
