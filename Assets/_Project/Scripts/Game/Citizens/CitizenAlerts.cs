using System.Collections.Generic;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Citizens.Content;
using UnityEngine;

namespace CityBuilder.Game.Citizens
{
    /// <summary>
    /// Lebensereignisse als HUD-Meldung (Begleiter am RoadTool), sparsam: höchstens eine gesammelte Meldung je Spieltag
    /// mit Geburten und Hochzeiten des Vortags plus einem Beispielsatz aus dem Ereignis-Ticker
    /// (<see cref="CitizenSystem.RecentEvents"/>). Liest nur.
    /// </summary>
    public sealed class CitizenAlerts : MonoBehaviour
    {
        SimulationRunner runner;
        int lastDay = -1;
        int lastBorn, lastWeddings;

        public void Init(SimulationRunner r)
        {
            runner = r;
            r.WorldReplaced += () => lastDay = -1; // Speichern & Laden: Zähler der neuen Welt als Ausgangswert nehmen
        }

        void Update()
        {
            var citizens = runner != null ? runner.World?.Citizens : null;
            if (citizens == null) return;
            int day = runner.World.Clock.Day;
            var s = citizens.Stats;
            if (lastDay < 0) { lastDay = day; lastBorn = s.Born; lastWeddings = s.Weddings; return; }
            if (day == lastDay) return;
            lastDay = day;

            int born = s.Born - lastBorn, weddings = s.Weddings - lastWeddings;
            lastBorn = s.Born;
            lastWeddings = s.Weddings;
            if (born <= 0 && weddings <= 0) return;

            var parts = new List<string>();
            if (born > 0) parts.Add(born == 1 ? "1 Geburt" : $"{born} Geburten");
            if (weddings > 0) parts.Add(weddings == 1 ? "1 Hochzeit" : $"{weddings} Hochzeiten");
            string example = null;
            foreach (var e in citizens.RecentEvents)
                if (e.Kind == LifeEventKind.Birth || e.Kind == LifeEventKind.Wedding) { example = e.Text; break; }
            string text = string.Join(", ", parts) + (example != null ? $" – {example}" : "");
            HudMessages.Info("Neues aus der Stadt", text, HudIconKind.People, "citizens-daily");
        }
    }
}
