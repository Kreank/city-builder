using System.Collections.Generic;
using CityBuilder.Simulation.Trade;
using UnityEngine;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Handel 2: HUD-Meldungen zum Handel (Begleiter am RoadTool, liest nur): Gegenangebot an uns, Einladung in ein Bündnis,
    /// Beitritt/Austritt, Sanktionen. Die Simulation meldet über <see cref="TradeSystem.News"/>; gesammelt und im nächsten Frame
    /// gezeigt (Handler laufen mitten im Simulationsschritt). Gleicher Schlüssel → die Meldung wird ersetzt.
    /// </summary>
    public sealed class TradeAlerts : MonoBehaviour
    {
        SimulationRunner runner;
        TradeSystem subscribed;
        readonly List<TradeNews> pending = new List<TradeNews>();

        public void Init(SimulationRunner r)
        {
            runner = r;
            r.WorldReplaced += Resubscribe; // Speichern & Laden / Stadtwechsel: neue Welt, neuer Handel
            Resubscribe();
        }

        void Resubscribe()
        {
            if (subscribed != null) subscribed.News -= OnNews;
            subscribed = runner?.World?.Trade;
            if (subscribed != null) subscribed.News += OnNews;
            pending.Clear();
        }

        void OnDestroy()
        {
            if (subscribed != null) subscribed.News -= OnNews;
            if (runner != null) runner.WorldReplaced -= Resubscribe;
        }

        void OnNews(TradeNews n) => pending.Add(n);

        void Update()
        {
            if (pending.Count == 0) return;
            foreach (var n in pending)
            {
                switch (n.Kind)
                {
                    case TradeNewsKind.CounterOffer:
                        HudMessages.Info("Gegenangebot", $"{n.CountryName} macht ein Gegenangebot über {n.Detail} (Runde {(int)n.Value}) – Handel (F11) → Verhandlung.",
                            HudIconKind.Coins, $"trade-counter-{n.Country}");
                        break;
                    case TradeNewsKind.BlocInvitation:
                        HudMessages.Info("Einladung ins Bündnis", $"{n.CountryName} lädt uns in {n.Detail} ein – Handel (F11) → Bündnisse.", HudIconKind.Coins, "trade-bloc-invite");
                        break;
                    case TradeNewsKind.BlocJoined:
                        HudMessages.Info("Bündnis", $"{n.Detail}: {n.CountryName} · jetzt {(int)n.Value} Mitglieder.", HudIconKind.Coins, "trade-bloc");
                        break;
                    case TradeNewsKind.BlocLeft:
                        HudMessages.Info("Bündnis", $"{n.Detail}: Austritt bzw. Auflösung.", HudIconKind.Coins, "trade-bloc");
                        break;
                    case TradeNewsKind.Sanction:
                        HudMessages.Warning("Sanktionen", n.Detail ?? $"Sanktionen gegen {n.CountryName}", HudIconKind.Warning, $"trade-sanction-{n.Country}");
                        break;
                }
            }
            pending.Clear();
        }
    }
}
