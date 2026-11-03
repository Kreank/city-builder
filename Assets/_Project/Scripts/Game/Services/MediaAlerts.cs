using CityBuilder.Game.UI.Hud;
using UnityEngine;

namespace CityBuilder.Game.Services
{
    /// <summary>
    /// G4 erweitert: Schlagzeilen der Medien (<c>world.Media.Recent</c>) als HUD-Meldung (Begleiter am RoadTool, kleine,
    /// markierte Anbindung an <see cref="HudMessages"/>). Ohne Medienhaus erzeugt die Simulation keine Schlagzeilen – dann
    /// kommt hier auch nichts. Liest nur; zeigt je Aktualisierung höchstens die zwei neuesten Meldungen.
    /// </summary>
    public sealed class MediaAlerts : MonoBehaviour
    {
        SimulationRunner runner;
        int lastId;

        public void Init(SimulationRunner r) => runner = r;

        void Update()
        {
            var media = runner != null ? runner.World?.Media : null;
            if (media == null || media.Recent.Count == 0) return;
            var newest = media.Recent[0];
            if (newest.Id <= lastId) return;
            for (int i = media.Recent.Count - 1; i >= 0; i--) // älteste zuerst, damit die neueste oben liegt
            {
                var h = media.Recent[i];
                if (h.Id <= lastId) continue;
                if (newest.Id - h.Id > 1) continue; // nur die zwei neuesten
                string title = $"{h.Source}: {h.Title}";
                if (h.Tone <= -0.6f) HudMessages.Warning(title, h.Text, HudIconKind.Warning, "media-" + h.Topic);
                else HudMessages.Info(title, h.Text, HudIconKind.Info, "media-" + h.Topic);
            }
            lastId = newest.Id;
        }
    }
}
