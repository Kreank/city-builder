using System;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Realm;
using CityBuilder.Simulation.Trade;
using UnityEngine;

namespace CityBuilder.Game.Realm
{
    /// <summary>
    /// Etappe 7 Reich – Spieleraktionen der UI: Reichs-Commands sofort ausführen (wirkt auch bei Pause) und die detailliert
    /// laufende Stadt wechseln (<see cref="RealmSwitch"/> baut die Zielstadt wie beim Laden auf, der Runner ersetzt die Welt;
    /// alle Views bauen über <c>WorldReplaced</c> neu auf). Fehler kommen als Meldung, die alte Welt bleibt dann unverändert.
    /// </summary>
    public static class RealmActions
    {
        public static TradeResult Run(SimulationRunner runner, RealmCommand cmd)
        {
            var sys = runner?.World?.Realm?.System;
            if (sys == null) return TradeResult.Fail("kein Reich");
            var r = sys.Execute(cmd);
            if (r.Ok) HudMessages.Info("Reich", r.Message, HudIconKind.Flag, "realm");
            else HudMessages.Warning("Reich", r.Message, HudIconKind.Warning, "realm");
            return r;
        }

        /// <summary>Zur Stadt wechseln (aktuelle Stadt wird eingefroren und aggregiert, Zielstadt geladen bzw. neu erzeugt).</summary>
        public static bool SwitchTo(SimulationRunner runner, int cityId)
        {
            var world = runner?.World;
            if (world == null) return false;
            var check = RealmSwitch.Check(world, cityId);
            if (!check.Ok)
            {
                HudMessages.Warning("Stadtwechsel nicht möglich", check.FirstProblem, HudIconKind.Warning, "realm");
                return false;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var next = RealmSwitch.Switch(world, cityId);
                runner.ReplaceWorld(next);
                var c = next.Realm.ActiveCity;
                HudMessages.Info($"Willkommen in {c.Name}",
                    (c.Economy.Measured ? "Die Stadt läuft wieder detailliert." : "Neue Stadt – zuerst an die Autobahn anschließen.") +
                    $" ({watch.ElapsedMilliseconds} ms)", HudIconKind.Flag, "realm");
                return true;
            }
            catch (Exception e) when (e is SaveLoadException || e is InvalidOperationException)
            {
                Debug.LogException(e);
                HudMessages.Warning("Stadtwechsel fehlgeschlagen", e.Message, HudIconKind.Warning, "realm");
                return false;
            }
        }
    }
}
