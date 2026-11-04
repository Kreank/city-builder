using CityBuilder.Game.UI.Hud;
using UnityEngine;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Umwelt (Etappe 6): HUD-Warnung bei Smog-Alarm (Beginn/Ende) und Fischsterben (Begleiter am RoadTool, liest nur).
    /// Gleicher Schlüssel → die Meldung wird ersetzt.
    /// </summary>
    public sealed class EnvironmentAlerts : MonoBehaviour
    {
        SimulationRunner runner;
        bool shownSmog;
        int shownFishKills;

        public void Init(SimulationRunner r)
        {
            runner = r;
            r.WorldReplaced += Sync; // Speichern & Laden: Zustand der geladenen Welt übernehmen, nichts doppelt melden
            Sync();
        }

        void Sync()
        {
            var e = runner?.World?.Ecology;
            shownSmog = e != null && e.SmogAlert;
            shownFishKills = e?.FishKills ?? 0;
        }

        void Update()
        {
            var e = runner != null ? runner.World?.Ecology : null;
            if (e == null || !e.Enabled) return;
            if (e.SmogAlert != shownSmog)
            {
                shownSmog = e.SmogAlert;
                if (shownSmog)
                {
                    var wind = e.Wind;
                    string why = e.Inversion > 0.5f ? "Inversionswetterlage – die kalte Luft steht" : wind.Strength < 0.3f ? "kaum Wind" : $"Wind aus {wind.FromName}";
                    HudMessages.Warning("Smog-Alarm", $"{e.SmogBuildings} Wohngebäude im Smog ({why}). Gesundheit und Zufriedenheit leiden – " +
                                                      "Umweltauflagen, Bäume und weniger Verkehr helfen (Umweltfenster).", HudIconKind.Air, "smog");
                }
                else HudMessages.Info("Smog vorbei", "Die Luft über der Stadt ist wieder besser.", HudIconKind.Air, "smog");
            }
            if (e.FishKills > shownFishKills)
            {
                shownFishKills = e.FishKills;
                HudMessages.Warning("Fischsterben", "Der Fluss ist stark verschmutzt – ungeklärtes Abwasser? Eine Kläranlage hilft.", HudIconKind.Water, "fishkill");
            }
        }
    }
}
