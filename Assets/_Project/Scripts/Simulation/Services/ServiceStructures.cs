using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Services
{
    public enum ServiceBuildError
    {
        None,
        OutOfWorld,
        OnWater,
        TooSteep,
        OverlapsRoad,
        /// <summary>Anderes Dienst- oder Versorgungsbauwerk im Weg.</summary>
        OverlapsFacility,
        /// <summary>Strommast im Weg (Leitungen werden nicht automatisch verlegt).</summary>
        OverlapsPole,
        /// <summary>
        /// Keine Zufahrt an der Vorderseite: vor der Mitte der Vorderkante liegt keine ebenerdige Stadtstraße
        /// (Rand ≤ <see cref="ServiceSystem.RoadReach"/>) – Fahrzeuge kommen nicht raus.
        /// </summary>
        NoRoadAccess,
        /// <summary>Zu wenig Geld in der Stadtkasse (Kontostand + Kreditrahmen) oder Baustopp.</summary>
        InsufficientFunds,
    }

    /// <summary>Ein gebautes Dienstgebäude (frei platziert und gedreht wie die Versorger).</summary>
    public sealed class ServiceFacility
    {
        public int Id { get; internal set; }
        public ServiceType Type { get; internal set; }
        public Vector2 Position { get; internal set; }
        /// <summary>Richtung der Tiefe (Einheitsvektor), Vorderseite zeigt zur Zufahrt.</summary>
        public Vector2 Front { get; internal set; }
        public float Height { get; internal set; }
        /// <summary>Anschlusspunkt ans Straßennetz (Segment, -1 = keiner) und Parameter t darauf.</summary>
        public int AccessSegmentId { get; internal set; } = -1;
        public float AccessT { get; internal set; }
        /// <summary>Belastung je Tag (Personen, Schüler bzw. t Müll – gewichtet mit der Entfernungswirkung).</summary>
        public float Load { get; internal set; }
        /// <summary>Gebäude, die diesem Dienst zugeordnet sind (er ist für sie der nächste).</summary>
        public int ServedBuildings { get; internal set; }
        /// <summary>Gelagerter Müll in t (Müllhalde).</summary>
        public float Stored { get; internal set; }
        /// <summary>Heute (letzter Spieltag, gleitend) eingesammelter Müll in t.</summary>
        public float CollectedPerDay { get; internal set; }
        /// <summary>Straßen-Reichweite (null bei Parks oder ohne Anschluss).</summary>
        public ServiceReach Reach { get; internal set; }

        public string Kind => Type.Kind;
        public Vector2 Side => new Vector2(-Front.Y, Front.X);
        public bool HasAccess => AccessSegmentId >= 0;
        public bool IsFull => Type.Storage > 0f && Stored >= Type.Storage;
        /// <summary>Wirksame Kapazität (volle Müllhalde = 0).</summary>
        public float EffectiveCapacity => IsFull ? 0f : Type.Capacity;
        /// <summary>Auslastung (Belastung / Kapazität), &gt; 1 = überlastet.</summary>
        public float Utilization => EffectiveCapacity > 0f ? Load / EffectiveCapacity : (Load > 0f ? float.PositiveInfinity : 0f);
        /// <summary>Faktor 0..1, um den Überlastung die Wirkung senkt (min(1, Kapazität/Belastung)).</summary>
        public float LoadFactor => Load <= 1e-6f ? (EffectiveCapacity > 0f ? 1f : 0f) : MathF.Min(1f, EffectiveCapacity / Load);

        public bool Contains(Vector2 p, float margin = 0f) => ServiceGeometry.InRect(p, Position, Front, Type.Width, Type.Depth, margin);
        /// <summary>Grundfläche (gemeinsamer Baustein mit Straßensperre und Ausrichtung).</summary>
        public Footprint Footprint => new Footprint(Position, Front, Type.Width, Type.Depth);

        StructureBlock block;
        /// <summary>Eintrag für die Bauwerks-Sperre im Straßennetz (Straßen dürfen das Gebäude nicht überbauen).</summary>
        internal StructureBlock Block => block ??= new StructureBlock("service", Id, Type.DisplayName, Footprint, Height);
        internal void Relocate(Vector2 position, Vector2 front, float height)
        {
            Position = position;
            Front = front;
            Height = height;
            block = null;
        }
        public Vector2[] Corners() => ServiceGeometry.Corners(Position, Front, Type.Width, Type.Depth);
        /// <summary>Abstand eines Punktes zur Grundfläche (0 = darauf).</summary>
        public float DistanceTo(Vector2 p) => ServiceGeometry.RectDistance(p, Position, Front, Type.Width, Type.Depth);

        public override string ToString() => $"{Type.DisplayName} #{Id}";
    }

    /// <summary>Geplantes Dienstgebäude (Vorschau) – erst <see cref="ServiceSystem.Build"/> baut es.</summary>
    public sealed class ServicePlan
    {
        public ServiceType Type;
        public Vector2 Position;
        public Vector2 Front;
        public float Height;
        public float Cost;
        public ServiceBuildError Error;
        public int AccessSegmentId = -1;
        public float AccessT;
        /// <summary>Automatisch zur Straße ausgerichtet (Vorderseite zeigt zur Straße, Vorderkante am Gehweg).</summary>
        public bool Aligned;
        /// <summary>Verschieben: Id des bestehenden Dienstgebäudes (0 = Neubau). <see cref="Cost"/> ist dann der Umzugspreis.</summary>
        public int MovingId;
        /// <summary>Gebäude, die abgerissen würden.</summary>
        public readonly List<Building> Displaced = new List<Building>();
        public bool IsValid => Error == ServiceBuildError.None;
        /// <summary>Mitte der Vorderkante (Anschlusspunkt, Vorschau-Pfeil).</summary>
        public Vector2 FrontMid => Position + Front * (Type.Depth * 0.5f);
    }

    /// <summary>
    /// Ergebnis der Wegsuche (Dijkstra) vom Anschlusspunkt eines Dienstes über das Straßennetz:
    /// Fahrzeit in Sekunden zu jedem erreichten Knoten. Daraus ergibt sich die Zeit zu jedem Punkt
    /// eines Segments (Einbahnstraßen nur in Fahrtrichtung).
    /// </summary>
    public sealed class ServiceReach
    {
        readonly Dictionary<int, float> nodeSeconds;
        public int StartSegmentId { get; }
        public float StartT { get; }
        public float RangeSeconds { get; }

        internal ServiceReach(Dictionary<int, float> nodes, int startSegment, float startT, float range)
        {
            nodeSeconds = nodes;
            StartSegmentId = startSegment;
            StartT = startT;
            RangeSeconds = range;
        }

        /// <summary>Erreichte Knoten (Id → Sekunden).</summary>
        public IReadOnlyDictionary<int, float> Nodes => nodeSeconds;

        /// <summary>Fahrzeit in Sekunden bis zum Punkt t auf dem Segment (∞ = nicht erreichbar).</summary>
        public float SecondsTo(RoadSegment s, float t)
        {
            if (s == null) return float.PositiveInfinity;
            float secs = ServiceSystem.SegmentSeconds(s);
            bool fwd = ServiceSystem.CanDriveForward(s), bwd = ServiceSystem.CanDriveBackward(s);
            float best = float.PositiveInfinity;
            if (s.Id == StartSegmentId && ((t >= StartT && fwd) || (t <= StartT && bwd)))
                best = MathF.Abs(t - StartT) * secs;
            if (fwd && nodeSeconds.TryGetValue(s.Start.Id, out float a)) best = MathF.Min(best, a + t * secs);
            if (bwd && nodeSeconds.TryGetValue(s.End.Id, out float b)) best = MathF.Min(best, b + (1f - t) * secs);
            return best;
        }

        /// <summary>Entfernungswirkung 0..1 an der Stelle (1 bis <see cref="ServiceSystem.FullEffectShare"/> der Reichweite, dann linear auf 0).</summary>
        public float Factor(RoadSegment s, float t) => ServiceSystem.DistanceFactor(SecondsTo(s, t), RangeSeconds);
    }

    static class ServiceGeometry
    {
        public static bool InRect(Vector2 p, Vector2 center, Vector2 front, float width, float depth, float margin)
        {
            var d = p - center;
            var side = new Vector2(-front.Y, front.X);
            return MathF.Abs(Vector2.Dot(d, side)) <= width * 0.5f + margin && MathF.Abs(Vector2.Dot(d, front)) <= depth * 0.5f + margin;
        }

        public static float RectDistance(Vector2 p, Vector2 center, Vector2 front, float width, float depth)
        {
            var d = p - center;
            var side = new Vector2(-front.Y, front.X);
            float x = MathF.Max(0f, MathF.Abs(Vector2.Dot(d, side)) - width * 0.5f);
            float y = MathF.Max(0f, MathF.Abs(Vector2.Dot(d, front)) - depth * 0.5f);
            return MathF.Sqrt(x * x + y * y);
        }

        public static Vector2[] Corners(Vector2 center, Vector2 front, float width, float depth)
        {
            var side = new Vector2(-front.Y, front.X) * (width * 0.5f);
            var f = front * (depth * 0.5f);
            return new[] { center - side - f, center + side - f, center + side + f, center - side + f };
        }

        /// <summary>Überlappen sich zwei gedrehte Rechtecke? (Trennachsen-Test)</summary>
        public static bool RectsOverlap(Vector2 c1, Vector2 f1, float w1, float d1, Vector2 c2, Vector2 f2, float w2, float d2)
        {
            var a = Corners(c1, f1, w1, d1);
            var b = Corners(c2, f2, w2, d2);
            foreach (var axis in new[] { f1, new Vector2(-f1.Y, f1.X), f2, new Vector2(-f2.Y, f2.X) })
            {
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (var p in a) { float v = Vector2.Dot(p, axis); amin = MathF.Min(amin, v); amax = MathF.Max(amax, v); }
                foreach (var p in b) { float v = Vector2.Dot(p, axis); bmin = MathF.Min(bmin, v); bmax = MathF.Max(bmax, v); }
                if (amax <= bmin + 0.01f || bmax <= amin + 0.01f) return false;
            }
            return true;
        }

        /// <summary>Berührt die ebenerdige Fahrbahn (Brücken/Tunnel nicht) das Rechteck, erweitert um margin?</summary>
        public static bool RoadCrossesRect(RoadSegment s, Vector2 center, Vector2 front, float width, float depth, float margin)
        {
            float reach = MathF.Sqrt(width * width + depth * depth) * 0.5f + s.Type.HalfWidth + margin + 1f;
            Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
            if (center.X < lo.X - reach || center.Y < lo.Y - reach || center.X > hi.X + reach || center.Y > hi.Y + reach) return false;
            int samples = Math.Max(2, (int)MathF.Ceiling(s.Length / 2f));
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                if (MathF.Abs(s.ElevationAt(t)) > 3f) continue;
                if (InRect(s.Evaluate(t), center, front, width, depth, s.Type.HalfWidth + margin)) return true;
            }
            return false;
        }
    }

    /// <summary>Kleiner Min-Heap (Kosten, Id) für Dijkstra – Gleichstand nach Id, damit deterministisch.</summary>
    sealed class MinHeap
    {
        readonly List<(float cost, int id)> items = new List<(float, int)>();
        public int Count => items.Count;

        static bool Less((float cost, int id) a, (float cost, int id) b) => a.cost < b.cost || (a.cost == b.cost && a.id < b.id);

        public void Push(float cost, int id)
        {
            items.Add((cost, id));
            int i = items.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (!Less(items[i], items[p])) break;
                (items[i], items[p]) = (items[p], items[i]);
                i = p;
            }
        }

        public (float cost, int id) Pop()
        {
            var top = items[0];
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < items.Count && Less(items[l], items[m])) m = l;
                if (r < items.Count && Less(items[r], items[m])) m = r;
                if (m == i) break;
                (items[i], items[m]) = (items[m], items[i]);
                i = m;
            }
            return top;
        }
    }
}
