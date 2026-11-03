using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>Ein gebautes Versorger-Bauwerk (Kraftwerk, Pumpe ...), frei platziert und gedreht.</summary>
    public sealed class UtilityFacility
    {
        public int Id { get; internal set; }
        public UtilityFacilityType Type { get; internal set; }
        public Vector2 Position { get; internal set; }
        /// <summary>Richtung der Tiefe (Einheitsvektor).</summary>
        public Vector2 Front { get; internal set; }
        /// <summary>Bodenhöhe (eingeebnet).</summary>
        public float Height { get; internal set; }
        /// <summary>Aktuelle Leistung (Strom kW / Wasser m³/h).</summary>
        public float Output { get; internal set; }
        /// <summary>Netz, zu dem das Bauwerk gehört (-1 = keins). Umspannwerk: das Ortsnetz (Unterseite).</summary>
        public int NetworkId { get; internal set; } = -1;
        // --- Netze selbst verlegt: Umspannwerk (abgeleitete Werte, werden je Durchlauf neu berechnet – nicht speichern)
        /// <summary>Umspannwerk: Hochspannungsnetz der Oberseite (-1 = keins/kein Umspannwerk).</summary>
        public int UpstreamNetworkId { get; internal set; } = -1;
        /// <summary>Umspannwerk: aktuell ins Ortsnetz weitergegebene Leistung (kW, ≤ <see cref="UtilityFacilityType.Capacity"/>).</summary>
        public float Load { get; internal set; }
        public bool IsSubstation => Type.IsSubstation;
        // --- Ende

        // --- Energie erweitert (gespeichert ab utilities v3)
        /// <summary>
        /// Nennleistung dieses Bauwerks (kW): Katalogwert, bei Wasserkraft je Standort aus Abfluss und Fallhöhe berechnet
        /// (<see cref="UtilitySystem.EvaluateHydroSite"/>). Speicher: Lade-/Entladeleistung.
        /// </summary>
        public float Rated { get; internal set; }
        /// <summary>Batteriespeicher: gespeicherte Energie (kWh, 0 … <see cref="UtilityFacilityType.StorageKwh"/>).</summary>
        public float StoredKwh { get; internal set; }
        /// <summary>Batteriespeicher: im letzten Durchlauf geladen (kW, aus dem Netz) – Anzeige.</summary>
        public float Charging { get; internal set; }
        /// <summary>Wasserkraft: mittlerer Abfluss des Flusses am Standort (m³/s) und nutzbare Fallhöhe (m).</summary>
        public float SiteFlow { get; internal set; }
        public float SiteHead { get; internal set; }
        /// <summary>Anteil 0..1 der Nennleistung im letzten Durchlauf (Wind, Abfluss, Rohstoff; ohne Störfall).</summary>
        public float Availability { get; internal set; } = 1f;
        /// <summary>Biogas: genutzte Rohstoffbasis (Beschäftigte der Bauernhöfe im Umkreis, die diese Anlage beliefern).</summary>
        public float FeedWorkers { get; internal set; }
        public bool IsStorage => Type.IsStorage;
        public bool IsGenerator => Type.IsGenerator;
        /// <summary>Ladestand 0..1 eines Speichers.</summary>
        public float StateOfCharge => Type.StorageKwh > 0f ? StoredKwh / Type.StorageKwh : 0f;
        // --- Ende Energie erweitert

        public UtilityKind Kind => Type.Kind;
        public Vector2 Side => new Vector2(-Front.Y, Front.X);
        public float Radius => MathF.Sqrt(Type.Width * Type.Width + Type.Depth * Type.Depth) * 0.5f;

        public bool Contains(Vector2 p, float margin = 0f) => UtilityGeometry.InRect(p, Position, Front, Type.Width, Type.Depth, margin);
        /// <summary>Grundfläche (gemeinsamer Baustein mit Straßensperre und Ausrichtung).</summary>
        public Footprint Footprint => new Footprint(Position, Front, Type.Width, Type.Depth);

        StructureBlock block;
        /// <summary>Eintrag für die Bauwerks-Sperre im Straßennetz (Straßen dürfen das Bauwerk nicht überbauen).</summary>
        internal StructureBlock Block => block ??= new StructureBlock("utility", Id, Type.DisplayName, Footprint, Height);
        internal void Relocate(Vector2 position, Vector2 front, float height)
        {
            Position = position;
            Front = front;
            Height = height;
            block = null;
        }

        /// <summary>Die vier Ecken der Grundfläche.</summary>
        public Vector2[] Corners() => UtilityGeometry.Corners(Position, Front, Type.Width, Type.Depth);

        public override string ToString() => $"{Type.DisplayName} #{Id}";
    }

    /// <summary>
    /// Eine selbst verlegte Leitung, gerade von A nach B: Wasserrohr/Abwasserkanal/Erdkabel (unterirdisch) oder
    /// Hochspannungs-/Freileitung (oberirdisch mit Masten). Art und Spannungsebene stehen in <see cref="Type"/>.
    /// Zu speichern: Id, <see cref="Type"/>.Id, A, B, Poles, Revision (NetworkId wird neu berechnet).
    /// </summary>
    public sealed class UtilityLine
    {
        public int Id { get; internal set; }
        /// <summary>Leitungsart (Netze selbst verlegt): Kosten, Ebene, Masten oder unterirdisch.</summary>
        public UtilityLineType Type { get; internal set; }
        public UtilityKind Kind => Type.Kind;
        public GridLevel Level => Type.Level;
        /// <summary>Halbe Mast-Grundfläche + Rand dieser Leitungsart (für Platzprüfungen anderer Bauwerke).</summary>
        public float PoleFootprintHalf => Type.PoleFootprintHalf;
        public Vector2 A { get; internal set; }
        public Vector2 B { get; internal set; }
        public int NetworkId { get; internal set; } = -1;
        /// <summary>
        /// Maststandorte (nur Stromleitungen, Reihenfolge A → B; Wasserrohre: leer). Von der Simulation berechnet
        /// (nie auf Straße/Wasser/Bauwerk/Gebäude, Spannweite ≤ <see cref="UtilitySystem.MaxPoleSpan"/>).
        /// </summary>
        public IReadOnlyList<Vector2> Poles { get; internal set; } = Array.Empty<Vector2>();
        /// <summary>Wird erhöht, wenn die Leitung neu trassiert wurde (Masten verschoben) – für die Darstellung.</summary>
        public int Revision { get; internal set; }

        public float Length => Vector2.Distance(A, B);
        public float DistanceTo(Vector2 p) => UtilityGeometry.DistanceToSegment(p, A, B);

        public override string ToString() => $"{Type.DisplayName} #{Id}";
    }

    /// <summary>Geplanter Mast einer Stromleitung (Vorschau): Standort und ggf. warum er dort nicht stehen darf.</summary>
    public struct PolePlan
    {
        public Vector2 Position;
        public UtilityBuildError Error;
        public bool IsValid => Error == UtilityBuildError.None;
    }

    /// <summary>Versorgungszustand eines Gebäudes (je Art: versorgt? seit wie vielen Ticks nicht?).</summary>
    public struct UtilityStatus
    {
        public bool HasPower;
        public bool HasWater;
        public int TicksWithoutPower;
        public int TicksWithoutWater;
        public int PowerNetwork;
        public int WaterNetwork;
        // --- Abwasser (G3 erweitert): Abwasser wird abgenommen (Kläranlage/Auslass im selben Kanalnetz)
        public bool HasSewage;
        public int TicksWithoutSewage;
        public int SewageNetwork;
        // --- Ende Abwasser

        public bool Has(UtilityKind k) => k == UtilityKind.Power ? HasPower : k == UtilityKind.Sewage ? HasSewage : HasWater;
        public int TicksWithout(UtilityKind k) => k == UtilityKind.Power ? TicksWithoutPower : k == UtilityKind.Sewage ? TicksWithoutSewage : TicksWithoutWater;
    }

    /// <summary>Ein zusammenhängendes Netz einer Versorgungsart mit seiner Bilanz.</summary>
    public sealed class UtilityNetwork
    {
        public int Id { get; internal set; }
        public UtilityKind Kind { get; internal set; }
        /// <summary>Strom: Hochspannungsnetz oder Ortsnetz; Wasser/Abwasser immer <see cref="GridLevel.Distribution"/>.</summary>
        public GridLevel Level { get; internal set; }
        /// <summary>
        /// Verfügbare Leistung im Netz: eigene Erzeugung (<see cref="Generation"/>) + über Umspannwerke bezogen
        /// (<see cref="Imported"/>, nur Ortsnetz).
        /// </summary>
        public float Production { get; internal set; }
        /// <summary>Erzeugung der Bauwerke, die direkt in dieses Netz einspeisen.</summary>
        public float Generation { get; internal set; }
        /// <summary>Ortsnetz: über Umspannwerke aus der Hochspannung bezogen. Hochspannung: an Umspannwerke abgegeben.</summary>
        public float Imported { get; internal set; }
        /// <summary>Summe der Kapazitäten der angeschlossenen Umspannwerke (kW).</summary>
        public float TransformerCapacity { get; internal set; }
        /// <summary>Angeschlossene Umspannwerke (Ortsnetz: Unterseite, Hochspannung: Oberseite).</summary>
        public int Substations { get; internal set; }
        /// <summary>Bedarf: Ortsnetz/Rohrnetz = fertige Gebäude; Hochspannung = Anforderung der Umspannwerke.</summary>
        public float Demand { get; internal set; }
        /// <summary>Tatsächlich gelieferte Menge (≤ Erzeugung und ≤ Bedarf).</summary>
        public float Delivered { get; internal set; }
        /// <summary>Erzeuger im Netz (Umspannwerke zählen in <see cref="Substations"/>).</summary>
        public int Facilities { get; internal set; }
        public int Buildings { get; internal set; }
        public int SuppliedBuildings { get; internal set; }
        public int Lines { get; internal set; }
        // --- Energie erweitert: Batteriespeicher im Netz (kW im letzten Durchlauf; gespeichert ab utilities v3)
        /// <summary>Von Speichern in dieses Netz abgegeben (kW, in <see cref="Generation"/> enthalten).</summary>
        public float StorageDischarge { get; internal set; }
        /// <summary>Von Speichern aus diesem Netz aufgenommen (kW).</summary>
        public float StorageCharge { get; internal set; }
        /// <summary>Speicher im Netz.</summary>
        public int Storages { get; internal set; }
        // --- Ende Energie erweitert

        /// <summary>Gibt es eine Quelle (Erzeuger oder Umspannwerk mit Hochspannung)?</summary>
        public bool HasSource => Facilities > 0 || Imported > 0f || TransformerCapacity > 0f || StorageDischarge > 0f; // Energie erweitert: Speicher
        public bool HasShortage => Demand > Production + 1e-3f;
        /// <summary>Deckungsgrad 0..1 (1 = alles versorgt).</summary>
        public float Coverage => Demand <= 0f ? 1f : MathF.Min(1f, Production / Demand);
    }

    static class UtilityGeometry
    {
        public static bool InRect(Vector2 p, Vector2 center, Vector2 front, float width, float depth, float margin)
        {
            var d = p - center;
            var side = new Vector2(-front.Y, front.X);
            return MathF.Abs(Vector2.Dot(d, side)) <= width * 0.5f + margin && MathF.Abs(Vector2.Dot(d, front)) <= depth * 0.5f + margin;
        }

        public static Vector2[] Corners(Vector2 center, Vector2 front, float width, float depth)
        {
            var side = new Vector2(-front.Y, front.X) * (width * 0.5f);
            var f = front * (depth * 0.5f);
            return new[] { center - side - f, center + side - f, center + side + f, center - side + f };
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-9f ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }

    /// <summary>Union-Find (mit Pfadkompression) für den Netzzusammenhang.</summary>
    sealed class UnionFind
    {
        int[] parent = new int[64];
        int count;

        public int Count => count;

        public int Add()
        {
            if (count == parent.Length) Array.Resize(ref parent, parent.Length * 2);
            parent[count] = count;
            return count++;
        }

        public int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        public void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a == b) return;
            // kleinere Wurzel gewinnt → Netz-Ids hängen nicht von der Reihenfolge der Vereinigungen ab
            if (a < b) parent[b] = a; else parent[a] = b;
        }
    }
}
