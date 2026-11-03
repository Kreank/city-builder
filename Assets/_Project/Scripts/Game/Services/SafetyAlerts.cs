using CityBuilder.Game.UI.Hud;
using UnityEngine;

namespace CityBuilder.Game.Services
{
    /// <summary>
    /// Sicherheit: Ereignisse der Simulation (<c>world.Safety.Events</c> – Brand gemeldet/übergegriffen/gelöscht/ausgebrannt,
    /// Sanierung fertig, Einbruchserie) als HUD-Meldungen (Begleiter am RoadTool). Liest nur; gleicher Brand = gleicher
    /// Schlüssel, die Meldung wird ersetzt statt gestapelt.
    /// </summary>
    public sealed class SafetyAlerts : MonoBehaviour
    {
        SimulationRunner runner;
        int lastSerial;

        public void Init(SimulationRunner r)
        {
            runner = r;
            // Speichern & Laden: Meldungen der geladenen Welt nicht erneut zeigen
            r.WorldReplaced += () => { var s = r.World?.Safety; lastSerial = s != null && s.Events.Count > 0 ? s.Events[0].Serial : 0; };
        }

        void Update()
        {
            var safety = runner != null ? runner.World?.Safety : null;
            if (safety == null || safety.Events.Count == 0) return;
            var newest = safety.Events[0];
            if (newest.Serial <= lastSerial) return;
            int shown = 0;
            for (int i = safety.Events.Count - 1; i >= 0; i--) // älteste zuerst, die neueste liegt oben
            {
                var e = safety.Events[i];
                if (e.Serial <= lastSerial) continue;
                if (newest.Serial - e.Serial > 2) continue; // höchstens die drei neuesten je Bild
                var icon = e.Key != null && e.Key.StartsWith("crime") ? HudIconKind.Shield : HudIconKind.Flame;
                if (e.Warning) HudMessages.Warning(e.Title, e.Text, icon, e.Key);
                else HudMessages.Info(e.Title, e.Text, icon, e.Key);
                shown++;
            }
            lastSerial = newest.Serial;
        }
    }
}
