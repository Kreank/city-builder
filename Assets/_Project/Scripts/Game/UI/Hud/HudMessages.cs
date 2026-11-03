using System;

namespace CityBuilder.Game.UI.Hud
{
    public enum HudMessageKind { Info, Warning }

    /// <summary>Eine Meldung für die Toast-Liste oben rechts.</summary>
    public readonly struct HudMessage
    {
        public readonly HudMessageKind Kind;
        public readonly string Title;
        public readonly string Text;
        public readonly HudIconKind Icon;
        /// <summary>Gleicher Schlüssel ersetzt eine noch sichtbare Meldung (statt sie zu stapeln), z. B. „power-shortage“.</summary>
        public readonly string Key;

        public HudMessage(HudMessageKind kind, string title, string text, HudIconKind icon, string key)
        {
            Kind = kind;
            Title = title;
            Text = text;
            Icon = icon;
            Key = key;
        }
    }

    /// <summary>
    /// Meldungsdienst: Views setzen hier Meldungen ab, die Spiel-UI (<see cref="GameHud"/>) zeigt sie als Toasts.
    /// Ohne HUD (Fallback-IMGUI) verfallen Meldungen still. Liest/ändert nichts an der Simulation.
    /// </summary>
    public static class HudMessages
    {
        public static event Action<HudMessage> Posted;

        public static void Post(HudMessageKind kind, string title, string text = null, HudIconKind icon = HudIconKind.Info, string key = null) =>
            Posted?.Invoke(new HudMessage(kind, title, text, icon, key));

        public static void Info(string title, string text = null, HudIconKind icon = HudIconKind.Info, string key = null) =>
            Post(HudMessageKind.Info, title, text, icon, key);

        public static void Warning(string title, string text = null, HudIconKind icon = HudIconKind.Warning, string key = null) =>
            Post(HudMessageKind.Warning, title, text, icon, key);
    }
}
