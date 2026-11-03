using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Simulation.Core;
using UnityEngine;

namespace CityBuilder.Game.UI.Panels
{
    /// <summary>Wo ein Fenster sitzt: großes Panel am rechten Rand oder mittiges Fenster.</summary>
    public enum WindowSlot { Right, Center }

    /// <summary>
    /// Basisklasse für Fenster der Spiel-UI (UI Toolkit, Phase 2). Ein Fenster sagt nur, <b>ob</b> es offen sein will
    /// (<see cref="OpenScore"/>) und <b>was</b> drinsteht (<see cref="DrawHead"/>/<see cref="DrawBody"/> mit dem
    /// <see cref="Ui"/>-Baukasten); Rahmen, Scrollen, Platzierung, Esc, Mausblockade und Aktualisierung (5×/s und nach
    /// jedem Klick) übernimmt der <see cref="WindowHost"/>.
    /// <example><code>
    /// sealed class LagerWindow : GameWindow
    /// {
    ///     bool open;
    ///     public LagerWindow() : base(WindowSlot.Center) { }
    ///     public void Toggle() => open = !open;
    ///     public override float OpenScore => open ? 1f : 0f;
    ///     public override void Close() => open = false;
    ///     public override void DrawHead(Ui ui) => Head(ui, "Wirtschaft", Tone.Info, "Lager", "alle Güter der Stadt");
    ///     public override void DrawBody(Ui ui)
    ///     {
    ///         using (ui.Grid(2)) { ui.Stat("Mehl", "120", "t"); ui.Stat("Stahl", "40", "t"); }
    ///         ui.Section("Bestand");
    ///         using (ui.Fields()) ui.Field("Lager voll", "64 %", Tone.Warn);
    ///     }
    /// }
    /// // anmelden (z. B. in GameHud.Init oder später von außen): hud.Windows.Add(new LagerWindow());
    /// </code></example>
    /// </summary>
    public abstract class GameWindow
    {
        protected GameWindow(WindowSlot slot) { Slot = slot; }

        public WindowSlot Slot { get; }

        /// <summary>Zusätzliche USS-Klasse des Rahmens, z. B. <c>cb-window--wide</c> für die Weltkarte.</summary>
        public virtual string FrameClass => null;

        /// <summary>Host, an dem das Fenster hängt (gesetzt beim Anmelden).</summary>
        public WindowHost Host { get; internal set; }

        protected RoadTool Tool => Host.Tool;
        protected SimulationRunner Runner => Host.Tool.Runner;
        protected SimulationWorld World => Host.Tool.Runner.World;

        /// <summary>
        /// 0 = geschlossen. Sonst Vorrang im Platz: Es gewinnt der höchste Wert, bei Gleichstand das zuletzt
        /// geöffnete. Faustregel: 100 = vom Werkzeug-Modus verlangt, 50 = von einer Info-Ansicht, 1 = vom Spieler geöffnet.
        /// </summary>
        public abstract float OpenScore { get; }

        /// <summary>Schließen-Knopf (×). Standard: bis zum nächsten Öffnen ausblenden.</summary>
        public virtual void Close() => Dismissed = true;

        /// <summary>Esc gedrückt, während dieses Fenster vorne ist. true = verbraucht.</summary>
        public virtual bool HandleEscape()
        {
            Close();
            return true;
        }

        /// <summary>Vom Spieler mit × ausgeblendet; wird zurückgesetzt, sobald <see cref="OpenScore"/> auf 0 fällt.</summary>
        public bool Dismissed { get; set; }

        /// <summary>Wird beim Wechsel des Inhalts (z. B. neue Auswahl) erhöht → Scrollposition zurück nach oben.</summary>
        public int ContentVersion { get; protected set; }

        /// <summary>Kopf des Fensters (bleibt beim Scrollen stehen): Chip, Titel, Reiter …</summary>
        public abstract void DrawHead(Ui ui);

        /// <summary>Inhalt (scrollt).</summary>
        public abstract void DrawBody(Ui ui);

        /// <summary>Jedes Frame, solange das Fenster sichtbar ist (z. B. Welt-Beschriftungen, Tasten).</summary>
        public virtual void Tick() { }

        /// <summary>Jedes Frame, auch wenn das Fenster nicht sichtbar ist (z. B. Tastenkürzel zum Öffnen).</summary>
        public virtual void Poll() { }

        /// <summary>
        /// Standard-Kopfzeile wie im Entwurf: links Chip (optional mit Farbpunkt), rechts Zurück/Zusatzknöpfe/Folgen/
        /// Schließen, darunter Titel und Untertitel (optional mit Avatar). <paramref name="back"/>/<paramref name="toggleFollow"/>
        /// = null → Knopf fehlt; <paramref name="extraButtons"/> zeichnet weitere <see cref="Ui.IconButton"/>s.
        /// </summary>
        protected void Head(Ui ui, string chip, Tone chipTone, string title, string subtitle,
            System.Action back = null, bool backEnabled = true, bool following = false, System.Action toggleFollow = null,
            Color? chipDot = null, bool closable = true, string avatar = null, Color? avatarColor = null,
            System.Action<Ui> extraButtons = null)
        {
            using (ui.Row("cb-head-bar"))
            {
                if (!string.IsNullOrEmpty(chip)) ui.Chip(chip, chipTone, chipDot);
                ui.Spacer();
                if (back != null) ui.IconButton(HudIconKind.Back, back, false, backEnabled, "Zurück zur vorigen Auswahl");
                extraButtons?.Invoke(ui);
                if (toggleFollow != null) ui.IconButton(HudIconKind.Follow, toggleFollow, following, true, "Kamera folgen (F)");
                if (closable) ui.IconButton(HudIconKind.Close, Close, false, true, "Schließen (Esc)");
            }
            ui.Title(title, subtitle, avatar, avatarColor);
        }
    }
}
