using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Stromnetz mit Spannungsebenen (Netze selbst verlegt): Kraftwerk → <b>Hochspannung</b> → <b>Umspannwerk</b> →
    /// <b>Ortsnetz</b> (Freileitung oder Erdkabel) → Gebäude.
    /// <para>
    /// Je Durchlauf, nachdem der Gebäudebedarf je Netz feststeht:
    /// <list type="number">
    /// <item>Jedes Ortsnetz braucht <c>max(0, Bedarf − eigene Erzeugung)</c> (kleine Erzeuger dürfen direkt einspeisen).</item>
    /// <item>Der Bedarf wird auf die Umspannwerke des Ortsnetzes nach Kapazität verteilt, je Umspannwerk höchstens seine
    /// Kapazität – das Umspannwerk begrenzt die Last. Umspannwerke ohne erzeugende Hochspannung zählen nicht.</item>
    /// <item>Jedes Hochspannungsnetz liefert anteilig: reicht die Erzeugung nicht für alle Anforderungen, bekommt jedes
    /// Umspannwerk denselben Anteil (Unterdeckung anteilig).</item>
    /// <item>Im Ortsnetz verfügbar = eigene Erzeugung + geliefert; dort werden wie bisher die ältesten Gebäude zuerst versorgt.</item>
    /// </list>
    /// Ein Ortsnetz ohne Umspannwerk (und ohne eigenen Erzeuger) ist nicht versorgt. Überschuss im Ortsnetz fließt nicht
    /// zurück in die Hochspannung (Rückspeisung: später). Alles in fester Reihenfolge (Ids) → deterministisch.
    /// </para>
    /// </summary>
    public sealed partial class UtilitySystem
    {
        readonly List<UtilityFacility> substationScratch = new List<UtilityFacility>();

        /// <summary>Alle Umspannwerke (nach Id).</summary>
        public IEnumerable<UtilityFacility> Substations
        {
            get
            {
                var list = new List<UtilityFacility>();
                foreach (var f in facilities.Values) if (f.IsSubstation) list.Add(f);
                list.Sort((a, b) => a.Id.CompareTo(b.Id));
                return list;
            }
        }

        /// <summary>Netz eines Bauwerks (Umspannwerk: Ortsnetz), null = keins.</summary>
        public UtilityNetwork NetworkOf(UtilityFacility f)
        {
            if (f == null) return null;
            var list = networks[(int)f.Kind];
            return f.NetworkId >= 0 && f.NetworkId < list.Count ? list[f.NetworkId] : null;
        }

        /// <summary>Hochspannungsnetz an der Oberseite eines Umspannwerks (null = keins).</summary>
        public UtilityNetwork UpstreamNetworkOf(UtilityFacility f)
        {
            if (f == null || !f.IsSubstation) return null;
            var list = networks[(int)UtilityKind.Power];
            return f.UpstreamNetworkId >= 0 && f.UpstreamNetworkId < list.Count ? list[f.UpstreamNetworkId] : null;
        }

        /// <summary>Leistungsfluss Hochspannung → Umspannwerke → Ortsnetze (s. Klassenbeschreibung).</summary>
        void TransformPower(List<UtilityNetwork> list)
        {
            var subs = substationScratch;
            subs.Clear();
            foreach (var f in facilities.Values) if (f.IsSubstation) subs.Add(f);
            subs.Sort((a, b) => a.Id.CompareTo(b.Id));
            if (subs.Count == 0) return;

            bool Valid(int id) => id >= 0 && id < list.Count;
            // Kapazität je Ortsnetz: alle Umspannwerke (Anzeige) bzw. nur die mit erzeugender Hochspannung (Verteilung)
            var usableCapacity = new float[list.Count];
            foreach (var s in subs)
            {
                s.Load = 0f;
                if (!Valid(s.NetworkId)) continue;
                float cap = s.Type.Capacity;
                list[s.NetworkId].TransformerCapacity += cap;
                if (Valid(s.UpstreamNetworkId))
                {
                    list[s.UpstreamNetworkId].TransformerCapacity += cap;
                    if (list[s.UpstreamNetworkId].Generation > 0f) usableCapacity[s.NetworkId] += cap;
                }
            }

            // Anforderung je Umspannwerk: Fehlmenge des Ortsnetzes nach Kapazität verteilt, höchstens die eigene Kapazität
            var request = new float[subs.Count];
            for (int i = 0; i < subs.Count; i++)
            {
                var s = subs[i];
                if (!Valid(s.NetworkId) || !Valid(s.UpstreamNetworkId)) continue;
                var local = list[s.NetworkId];
                var hv = list[s.UpstreamNetworkId];
                if (hv.Generation <= 0f || usableCapacity[s.NetworkId] <= 0f) continue;
                float need = MathF.Max(0f, local.Demand - local.Generation);
                float cap = s.Type.Capacity;
                request[i] = MathF.Min(cap, need * cap / usableCapacity[s.NetworkId]);
                hv.Demand += request[i];
            }

            // Hochspannung liefert anteilig
            for (int i = 0; i < subs.Count; i++)
            {
                if (request[i] <= 0f) continue;
                var s = subs[i];
                var hv = list[s.UpstreamNetworkId];
                float share = hv.Demand > 0f ? MathF.Min(1f, hv.Generation / hv.Demand) : 0f;
                float delivered = request[i] * share;
                s.Load = delivered;
                hv.Delivered += delivered;
                hv.Imported += delivered; // Hochspannung: an Umspannwerke abgegeben
                list[s.NetworkId].Imported += delivered;
            }
            foreach (var n in list)
                if (n.Level == GridLevel.Distribution) n.Production = n.Generation + n.Imported;
        }
    }
}
