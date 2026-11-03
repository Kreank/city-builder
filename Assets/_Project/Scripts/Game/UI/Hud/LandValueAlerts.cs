using System.Collections.Generic;
using CityBuilder.Simulation.LandValue;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// G6 als HUD-Meldung, sparsam: sammelt Aufstiege (<see cref="LandValueSystem.LevelChanged"/> mit höherer Stufe) und
    /// verfallene Gebäude (<see cref="LandValueSystem.BuildingDecayed"/>) und meldet sie höchstens einmal je Spieltag
    /// (Aufstiege als Info, Verfall als Warnung, je eigener Schlüssel). Abstiege bleiben still (sieht man im Inspektor).
    /// Begleiter der <see cref="GameHud"/>; liest nur.
    /// </summary>
    public sealed class LandValueAlerts : MonoBehaviour
    {
        SimulationRunner runner;
        LandValueSystem subscribed;
        int lastDay = -1;
        int upgrades;
        int topLevel;
        string upgradeExample;
        int decayed;
        string decayExample;

        public void Init(SimulationRunner r) => runner = r;

        void OnDestroy() => Unsubscribe();

        void Unsubscribe()
        {
            if (subscribed == null) return;
            subscribed.LevelChanged -= OnLevelChanged;
            subscribed.BuildingDecayed -= OnDecayed;
            subscribed = null;
        }

        void OnLevelChanged(Building b, int oldLevel)
        {
            if (b == null || b.Level <= oldLevel) return;
            upgrades++;
            if (b.Level >= topLevel)
            {
                topLevel = b.Level;
                upgradeExample = $"{Zones.DisplayName(b.Zone)} #{b.Id} jetzt Stufe {b.Level}";
            }
        }

        void OnDecayed(Building b)
        {
            decayed++;
            if (b != null && decayExample == null) decayExample = $"{Zones.DisplayName(b.Zone)} #{b.Id}";
        }

        void Update()
        {
            var world = runner != null ? runner.World : null;
            var lv = world?.LandValue;
            if (lv != subscribed)
            {
                Unsubscribe();
                if (lv != null)
                {
                    lv.LevelChanged += OnLevelChanged;
                    lv.BuildingDecayed += OnDecayed;
                    subscribed = lv;
                }
            }
            if (lv == null) return;

            int day = world.Clock.Day;
            if (lastDay < 0) { lastDay = day; return; }
            if (day == lastDay) return;
            lastDay = day;

            if (upgrades > 0)
                HudMessages.Info(upgrades == 1 ? "Ein Gebäude ist aufgestiegen" : $"{upgrades} Gebäude aufgestiegen",
                    upgradeExample + " · Bodenwert und Zufriedenheit tragen (F2: Bodenwert-Ansicht)", HudIconKind.Zones, "landvalue-upgrades");
            if (decayed > 0)
                HudMessages.Warning(decayed == 1 ? "Ein Gebäude ist verfallen" : $"{decayed} Gebäude verfallen",
                    (decayExample != null ? decayExample + " abgerissen · " : "") + "Zustand sinkt ohne Versorgung, mit Müll und bei niedrigem Bodenwert",
                    HudIconKind.Warning, "landvalue-decay");
            upgrades = 0;
            topLevel = 0;
            upgradeExample = null;
            decayed = 0;
            decayExample = null;
        }
    }
}
