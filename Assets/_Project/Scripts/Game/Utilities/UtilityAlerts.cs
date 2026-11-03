using CityBuilder.Game.UI.Hud;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.Utilities
{
    /// <summary>
    /// Meldungen zur Versorgung (Begleiter am RoadTool): Netz unterdeckt bzw. wieder ausreichend,
    /// Gebäude verlassen (<see cref="UtilitySystem.BuildingAbandoned"/>), Leitung durch Bau abgerissen.
    /// Liest nur die Simulation und setzt Meldungen über <see cref="HudMessages"/> ab.
    /// </summary>
    public sealed class UtilityAlerts : MonoBehaviour
    {
        /// <summary>Verlassene Gebäude so lange sammeln (Echtzeit), dann eine Meldung.</summary>
        const float AbandonGather = 1.5f;

        SimulationRunner runner;
        UtilitySystem subscribed;
        readonly bool[] shortage = new bool[3]; // Strom, Wasser, Abwasser (G3 erw.)
        int shownStatus = -1;
        int removedLines;
        int abandonedPending;
        string abandonedZone;
        float abandonedSince;

        public void Init(SimulationRunner r)
        {
            runner = r;
            r.WorldReplaced += () => { shownStatus = -1; abandonedPending = 0; removedLines = 0; }; // Speichern & Laden
        }

        /// <summary>
        /// Ist irgendein Netz der Art unterdeckt (und wird die Art schon verlangt, d. h. gibt es einen Versorger)?
        /// Liefert den Deckungsgrad (geliefert/Bedarf über alle Netze) und die Zahl unversorgter Gebäude.
        /// </summary>
        public static bool HasShortage(UtilitySystem u, UtilityKind kind, out float coverage, out int unsupplied)
        {
            coverage = 1f;
            unsupplied = 0;
            if (u == null || !u.IsRequired(kind)) return false;
            float demand = 0f, delivered = 0f;
            bool any = false;
            foreach (var n in u.Networks(kind))
            {
                if (n.Level != GridLevel.Distribution) continue; // Hochspannung: Bedarf = weitergereichter Gebäudebedarf (nicht doppelt)
                demand += n.Demand;
                delivered += n.Delivered;
                // Netze selbst verlegt: Gebäude ganz ohne Anschluss (eigenes Netz ohne Quelle) sind keine Unterdeckung,
                // sondern fehlende Leitungen – die zeigt die Info-Ansicht (O) rot.
                if (n.Buildings > 0 && n.HasShortage && n.HasSource)
                {
                    any = true;
                    unsupplied += n.Buildings - n.SuppliedBuildings;
                }
            }
            coverage = demand > 0f ? Mathf.Clamp01(delivered / demand) : 1f;
            return any;
        }

        void OnDestroy()
        {
            if (subscribed != null) subscribed.BuildingAbandoned -= OnAbandoned;
        }

        void Update()
        {
            var world = runner != null ? runner.World : null;
            if (world == null) return;
            var u = world.Utilities;
            if (subscribed != u)
            {
                if (subscribed != null) subscribed.BuildingAbandoned -= OnAbandoned;
                subscribed = u;
                u.BuildingAbandoned += OnAbandoned;
                removedLines = u.LinesRemovedByConstruction;
            }

            if (u.StatusVersion != shownStatus)
            {
                shownStatus = u.StatusVersion;
                CheckShortage(u, UtilityKind.Power);
                CheckShortage(u, UtilityKind.Water);
                CheckShortage(u, UtilityKind.Sewage); // G3 erweitert
            }

            if (u.LinesRemovedByConstruction > removedLines)
            {
                int n = u.LinesRemovedByConstruction - removedLines;
                removedLines = u.LinesRemovedByConstruction;
                HudMessages.Warning(n == 1 ? "Leitung abgerissen" : $"{n} Leitungen abgerissen",
                    "Ein Bauvorhaben lag im Weg und die Leitung ließ sich nicht neu trassieren. Netz prüfen (O).",
                    HudIconKind.Power, "line-removed");
            }

            if (abandonedPending > 0 && Time.unscaledTime - abandonedSince >= AbandonGather)
            {
                string title = abandonedPending == 1 ? "Gebäude verlassen" : $"{abandonedPending} Gebäude verlassen";
                string text = abandonedPending == 1
                    ? $"{abandonedZone}: länger als {UtilitySystem.AbandonAfterTicks / Simulation.Core.SimulationClock.TicksPerDay} Tage ohne Strom oder Wasser."
                    : $"Länger als {UtilitySystem.AbandonAfterTicks / Simulation.Core.SimulationClock.TicksPerDay} Tage ohne Strom oder Wasser.";
                HudMessages.Warning(title, text, HudIconKind.Warning, "abandoned");
                abandonedPending = 0;
            }
        }

        void CheckShortage(UtilitySystem u, UtilityKind kind)
        {
            bool now = HasShortage(u, kind, out float coverage, out int unsupplied);
            int i = (int)kind;
            if (now == shortage[i]) return;
            shortage[i] = now;
            if (!now && !u.IsRequired(kind)) return; // letzter Versorger abgerissen: nicht „ausreichend“ melden
            string name = UtilityCatalog.KindName(kind);
            string unit = UtilityCatalog.Unit(kind);
            if (now)
                HudMessages.Warning($"{(kind == UtilityKind.Power ? "Stromnetz" : kind == UtilityKind.Sewage ? "Kanalnetz" : "Wassernetz")} unterdeckt",
                    $"Bedarf {BuildLabels.Number(u.TotalDemand(kind))} {unit}, Erzeugung {BuildLabels.Number(u.TotalProduction(kind))} {unit} · " +
                    $"{unsupplied} Gebäude ohne {name} ({coverage * 100f:0} % gedeckt).",
                    kind == UtilityKind.Power ? HudIconKind.Power : kind == UtilityKind.Sewage ? HudIconKind.Pipe : HudIconKind.Water, "shortage-" + i);
            else
                HudMessages.Info($"{name} wieder ausreichend", "Alle angeschlossenen Gebäude sind versorgt.",
                    kind == UtilityKind.Power ? HudIconKind.Power : kind == UtilityKind.Sewage ? HudIconKind.Pipe : HudIconKind.Water, "shortage-" + i);
        }

        // Kommt aus World.Step (Hauptthread) – nur zählen, nichts an der Simulation ändern.
        void OnAbandoned(Building b)
        {
            if (abandonedPending == 0) abandonedSince = Time.unscaledTime;
            abandonedPending++;
            abandonedZone = b != null ? Zones.DisplayName(b.Zone) : "Gebäude";
        }
    }
}
