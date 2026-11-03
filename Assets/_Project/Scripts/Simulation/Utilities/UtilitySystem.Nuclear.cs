using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Atomkraftwerk (G3 erweitert): Akzeptanz (Sorge im Umkreis) und seltener Störfall.
    /// <para>
    /// <b>Entscheidung Störfall:</b> eingebaut, aber bewusst harmlos und abschaltbar (<see cref="IncidentsEnabled"/>):
    /// je Spieltag und Kraftwerk Chance <see cref="IncidentChancePerDay"/> (≈ einmal in 1,4 Spieljahren), gewürfelt per
    /// stabilem Hash aus Kraftwerks-Id und Tag – verbraucht keinen Zufall anderer Systeme, bleibt deterministisch
    /// (Replays/Multiplayer) und ändert nichts, solange kein Atomkraftwerk steht. Folge: Schnellabschaltung für
    /// <see cref="IncidentOfflineDays"/> Tage (Leistung 0 → Stromnetz kann unterdeckt sein), Meldung in den Medien,
    /// stärkere Sorge der Anwohner. Keine Verstrahlung/Sperrzone – das wäre ein Spielende ohne Gegenmittel; ein
    /// Atomkraftwerk soll ein kalkulierbares Risiko sein (Ersatzleistung vorhalten), keine Falle.
    /// </para>
    /// </summary>
    public sealed partial class UtilitySystem
    {
        /// <summary>Seltener Störfall aktiv (Standard an; Tests/Sandbox können abschalten).</summary>
        public bool IncidentsEnabled { get; set; } = true;
        /// <summary>Chance je Spieltag und Atomkraftwerk.</summary>
        public float IncidentChancePerDay { get; set; } = 0.002f;
        /// <summary>So viele Spieltage bleibt das Kraftwerk nach einem Störfall abgeschaltet.</summary>
        public const int IncidentOfflineDays = 3;
        /// <summary>Bis zu diesem Anteil des <see cref="UtilityFacilityType.ConcernRadius"/> ist die Sorge voll, dann linear auf 0.</summary>
        public const float ConcernFullShare = 0.3f;

        readonly Dictionary<int, int> offlineUntilDay = new Dictionary<int, int>();
        int incidentCheckedDay = -1;

        /// <summary>Anzahl der Störfälle bisher.</summary>
        public int Incidents { get; private set; }
        /// <summary>Störfall in einem Kraftwerk (für Medien/HUD).</summary>
        public event Action<UtilityFacility> NuclearIncident;

        /// <summary>
        /// Sorge an einem Punkt 0..1 (1 = direkt neben einem Bauwerk mit <see cref="UtilityFacilityType.ConcernRadius"/>):
        /// voll bis <see cref="ConcernFullShare"/> des Radius, dann linear auf 0. Kurz nach einem Störfall (abgeschaltet) zählt
        /// der doppelte Radius. Lesende Abfrage fürs Bürgersystem.
        /// </summary>
        public float ConcernAt(Vector2 p)
        {
            float best = 0f;
            foreach (var f in facilities.Values)
            {
                float r = f.Type.ConcernRadius;
                if (r <= 0f) continue;
                if (IsOffline(f)) r *= 2f;
                float d = MathF.Max(0f, Vector2.Distance(p, f.Position) - f.Radius);
                float full = r * ConcernFullShare;
                float v = d <= full ? 1f : Math.Clamp((r - d) / (r - full), 0f, 1f);
                if (v > best) best = v;
            }
            return best;
        }

        /// <summary>Ist das Bauwerk nach einem Störfall abgeschaltet?</summary>
        public bool IsOffline(UtilityFacility f) => f != null && offlineUntilDay.TryGetValue(f.Id, out int until) && until > incidentCheckedDay;

        /// <summary>Verbleibende Tage der Abschaltung (0 = läuft).</summary>
        public int OfflineDaysLeft(UtilityFacility f) =>
            f != null && offlineUntilDay.TryGetValue(f.Id, out int until) ? Math.Max(0, until - incidentCheckedDay) : 0;

        float OutputFactor(UtilityFacility f) => IsOffline(f) ? 0f : 1f;

        /// <summary>Einmal je neuem Spieltag: Störfälle würfeln (stabiler Hash, kein gemeinsamer Zufall).</summary>
        void UpdateIncidents(SimulationWorld world)
        {
            if (world == null) return; // ForceTick ohne Uhr
            int day = world.Clock.Day;
            if (day == incidentCheckedDay) return;
            incidentCheckedDay = day;
            if (!IncidentsEnabled || IncidentChancePerDay <= 0f) return;
            List<UtilityFacility> hit = null;
            foreach (var f in facilities.Values)
            {
                if (f.Type.ConcernRadius <= 0f || IsOffline(f)) continue;
                double roll = IncidentHash(f.Id, day) / (double)uint.MaxValue;
                if (roll < IncidentChancePerDay) (hit ?? (hit = new List<UtilityFacility>())).Add(f);
            }
            if (hit == null) return;
            hit.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var f in hit) StartIncident(f, day);
        }

        /// <summary>Für Tests/Szenarien: Störfall sofort auslösen (ab dem aktuellen Tag).</summary>
        public void ForceIncident(int facilityId, int day)
        {
            if (!facilities.TryGetValue(facilityId, out var f)) return;
            incidentCheckedDay = day;
            StartIncident(f, day);
        }

        void StartIncident(UtilityFacility f, int day)
        {
            offlineUntilDay[f.Id] = day + IncidentOfflineDays;
            Incidents++;
            StatusVersion++;
            NuclearIncident?.Invoke(f);
        }

        static uint IncidentHash(int id, int day)
        {
            unchecked
            {
                uint h = (uint)id * 0x9E3779B1u ^ (uint)day * 0x85EBCA77u ^ 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du;
                h ^= h >> 12; h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }
    }
}
