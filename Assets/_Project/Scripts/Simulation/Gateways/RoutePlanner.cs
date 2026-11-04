using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Gateways
{
    /// <summary>
    /// Grobes Raster für die Trassensuche der Welt-Infrastruktur (Autobahn, Fernbahn): geglättete Geländehöhe, vorzeichen-
    /// behafteter Abstand zum Ufer (+ Land, − Wasser; sein Gefälle zeigt quer zum Ufer) und Zusatzkosten je Meter (z. B. Nähe
    /// zur Autobahn, Anschlussstelle). Nur beim Erzeugen der Welt, nicht gespeichert.
    /// </summary>
    internal sealed class RouteField
    {
        public readonly float Half, Cell;
        public readonly int N;
        readonly Vector2 origin;
        readonly float[] height, shore, extra, wet;

        RouteField(float half, float cell)
        {
            Half = half;
            Cell = cell;
            N = Math.Max(4, (int)MathF.Ceiling(2f * half / cell) + 1);
            origin = new Vector2(-half, -half);
            height = new float[N * N];
            wet = new float[N * N];
            shore = new float[N * N];
            extra = new float[N * N];
        }

        public static RouteField Build(IGround ground, float half, float cell = RouteRules.FieldCell)
        {
            var f = new RouteField(half, cell);
            int n = f.N;
            var water = new bool[n * n];
            var raw = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = f.origin + new Vector2(x * cell, y * cell);
                    int i = y * n + x;
                    water[i] = ground.IsWater(p);
                    raw[i] = water[i] ? ground.WaterLevel + Profiles.WaterClearance : ground.HeightAt(p);
                }
            // Höhe geglättet (≈ 100 m): Trassen folgen dem großen Relief, nicht jeder Bodenwelle
            f.Blur(raw, f.height, 3);
            // Wassermaske, zweimal geglättet (≈ 40 m) – für die Ufernormale
            var mask = new float[n * n];
            for (int i = 0; i < n * n; i++) mask[i] = water[i] ? 1f : 0f;
            var tmp = new float[n * n];
            f.Blur(mask, tmp, 2);
            f.Blur(tmp, f.wet, 2);
            // Abstand zum Ufer: Land → nächstes Wasser, Wasser → nächstes Land (Chamfer 3-4)
            var dLand = Chamfer(water, n, true);
            var dWater = Chamfer(water, n, false);
            for (int i = 0; i < n * n; i++)
                f.shore[i] = water[i] ? -(dWater[i] - 0.5f) * cell : (dLand[i] - 0.5f) * cell;
            return f;
        }

        /// <summary>Chamfer-Abstand (in Zellen) jeder Zelle zur nächsten Zelle mit <c>water == targetWater</c>.</summary>
        static float[] Chamfer(bool[] water, int n, bool targetWater)
        {
            const float big = 1e6f, d1 = 1f, d2 = 1.41421356f;
            var d = new float[n * n];
            for (int i = 0; i < d.Length; i++) d[i] = water[i] == targetWater ? 0f : big;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float v = d[i];
                    if (x > 0) v = MathF.Min(v, d[i - 1] + d1);
                    if (y > 0)
                    {
                        v = MathF.Min(v, d[i - n] + d1);
                        if (x > 0) v = MathF.Min(v, d[i - n - 1] + d2);
                        if (x < n - 1) v = MathF.Min(v, d[i - n + 1] + d2);
                    }
                    d[i] = v;
                }
            for (int y = n - 1; y >= 0; y--)
                for (int x = n - 1; x >= 0; x--)
                {
                    int i = y * n + x;
                    float v = d[i];
                    if (x < n - 1) v = MathF.Min(v, d[i + 1] + d1);
                    if (y < n - 1)
                    {
                        v = MathF.Min(v, d[i + n] + d1);
                        if (x < n - 1) v = MathF.Min(v, d[i + n + 1] + d2);
                        if (x > 0) v = MathF.Min(v, d[i + n - 1] + d2);
                    }
                    d[i] = v;
                }
            return d;
        }

        void Blur(float[] src, float[] dst, int r)
        {
            int n = N;
            var tmp = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f; int c = 0;
                    for (int k = Math.Max(0, x - r); k <= Math.Min(n - 1, x + r); k++) { s += src[y * n + k]; c++; }
                    tmp[y * n + x] = s / c;
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f; int c = 0;
                    for (int k = Math.Max(0, y - r); k <= Math.Min(n - 1, y + r); k++) { s += tmp[k * n + x]; c++; }
                    dst[y * n + x] = s / c;
                }
        }

        float Sample(float[] data, Vector2 p)
        {
            float fx = (p.X - origin.X) / Cell, fy = (p.Y - origin.Y) / Cell;
            int x0 = Math.Clamp((int)MathF.Floor(fx), 0, N - 2), y0 = Math.Clamp((int)MathF.Floor(fy), 0, N - 2);
            float u = Math.Clamp(fx - x0, 0f, 1f), v = Math.Clamp(fy - y0, 0f, 1f);
            int i = y0 * N + x0;
            float a = data[i] + (data[i + 1] - data[i]) * u;
            float b = data[i + N] + (data[i + N + 1] - data[i + N]) * u;
            return a + (b - a) * v;
        }

        public float Height(Vector2 p) => Sample(height, p);
        /// <summary>Vorzeichenbehafteter Abstand zum Ufer (m): positiv an Land, negativ im Wasser.</summary>
        public float Shore(Vector2 p) => Sample(shore, p);
        /// <summary>Zusatzkosten je Meter an dieser Stelle.</summary>
        public float Extra(Vector2 p) => Sample(extra, p);

        /// <summary>
        /// Richtung quer zum Ufer (Einheitsvektor) – Zero, wo es keins gibt. Aus dem Gefälle der geglätteten Wassermaske: eine
        /// Faltung hängt an einer geraden Uferlinie nur vom Abstand zu ihr ab, das Gefälle steht also senkrecht zum Ufer (der
        /// Chamfer-Abstand dagegen rastet auf 8 Richtungen ein).
        /// </summary>
        public Vector2 ShoreNormal(Vector2 p)
        {
            float h = Cell;
            var g = new Vector2(Sample(wet, p + new Vector2(h, 0)) - Sample(wet, p - new Vector2(h, 0)),
                                Sample(wet, p + new Vector2(0, h)) - Sample(wet, p - new Vector2(0, h)));
            float len = g.Length();
            return len < 0.02f ? Vector2.Zero : g / len;
        }

        /// <summary>Zusatzkosten (je Meter) im Umkreis eines Linienzugs, linear zum Rand hin auslaufend.</summary>
        public void AddNearLine(IReadOnlyList<Vector2> line, float radius, float costPerMeter)
        {
            if (line == null || line.Count < 2) return;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = origin + new Vector2(x * Cell, y * Cell);
                    float d = DistanceToLine(line, p);
                    if (d < radius) extra[y * N + x] += costPerMeter * MathF.Min(1f, 1.5f * (1f - d / radius));
                }
        }

        public void AddNearPoint(Vector2 c, float radius, float costPerMeter)
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = origin + new Vector2(x * Cell, y * Cell);
                    float d = Vector2.Distance(p, c);
                    if (d < radius) extra[y * N + x] += costPerMeter * MathF.Min(1f, 1.5f * (1f - d / radius));
                }
        }

        public static float DistanceToLine(IReadOnlyList<Vector2> line, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
            {
                var a = line[i]; var ab = line[i + 1] - a;
                float l2 = ab.LengthSquared();
                float t = l2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f);
                best = MathF.Min(best, Vector2.DistanceSquared(p, a + ab * t));
            }
            return MathF.Sqrt(best);
        }
    }

    /// <summary>Alle Zahlen der Trassensuche (Platzhalter fürs Balancing).</summary>
    public static class RouteRules
    {
        /// <summary>Zellgröße des Suchrasters (m) und Raster der Zustände (Lage) in der Suche.</summary>
        public const float FieldCell = 16f, StateCell = 24f;
        /// <summary>Richtungsschritt der Bögen (Grad): ein Bogen-Baustein dreht genau um so viel.</summary>
        public const float StepDegrees = 5f;
        /// <summary>Kleinste Bogenhalbmesser der vorgebauten Trassen (m): Autobahn (Entwurf ~120 km/h), Fernbahn (160 km/h).</summary>
        public const float HighwayRadius = 750f, RailRadius = 1000f;
        /// <summary>Wasser quert man höchstens so schräg (Grad Abweichung von der Ufernormalen).</summary>
        public const float MaxCrossingDegrees = 22f;
        /// <summary>So nah am Ufer keine Bögen (Brücke und Rampen gerade): Autobahn, Bahn (m).</summary>
        public const float HighwayStraightNearWater = 80f, RailStraightNearWater = 60f;
        /// <summary>Gerade Autobahnstücke werden nur so weit vom Wasser geteilt (Brücke + Rampen in einem Stück, m).</summary>
        public const float HighwayCutClearance = 150f;
        /// <summary>Zusatzkosten je Meter über Wasser (Brücke): Autobahn, Bahn.</summary>
        public const float HighwayWaterCost = 14f, RailWaterCost = 18f;
        /// <summary>Höchstens so lang am Stück über Wasser (m) – kein Meer queren.</summary>
        public const float MaxWaterRun = 420f;
        /// <summary>Kosten je Meter Höhenunterschied über der erlaubten Steigung (Dämme, Einschnitte, Tunnel).</summary>
        public const float HeightCost = 10f;
        /// <summary>Aufschlag je Meter Bogen (lieber gerade, wenn nichts dagegen spricht).</summary>
        public const float TurnPenalty = 0.08f;
        /// <summary>Kosten je Meter, um den das Ende vom geraden Weg (Richtung der Ziele) abweicht.</summary>
        public const float DeviationCost = 1f;
        /// <summary>Das Ende trifft den Kartenrand höchstens so schräg (Grad gegen die Randnormale).</summary>
        public const float EdgeMaxDegrees = 50f;
        /// <summary>Fernbahn: um die Autobahn-Kreuzung herum gerade (je Seite, m), Abstand zur Autobahn sonst, Anschlussstelle frei.</summary>
        public const float RailStraightAtCrossing = 260f, RailHighwayClearance = 160f, RailHighwayCost = 40f;
        public const float RailInterchangeClearance = 480f, RailInterchangeCost = 40f;
        /// <summary>Fernbahn kreuzt die Autobahn höchstens so schräg (Grad gegen die Senkrechte).</summary>
        public const float RailCrossingMaxSkew = 30f;
        /// <summary>Höchstzahl ausgebauter Zustände je Suche (Abbruch → nächster Kandidat).</summary>
        public const int MaxExpansions = 250000;
        /// <summary>Gewicht der Schätzung (A*): > 1 sucht deutlich schneller, Trasse höchstens so viel teurer als die beste.</summary>
        public const float HeuristicWeight = 1.3f;
    }

    /// <summary>Ein Baustein der Trasse: gerade oder Bogen mit kleinstem Halbmesser, ab Punkt <see cref="P"/> in Richtung <see cref="Heading"/>.</summary>
    internal struct RouteStep
    {
        public Vector2 P;
        public float Heading; // Bogenmaß, 0 = Ost, gegen den Uhrzeigersinn
        public int Turn;      // −1 rechts, 0 gerade, +1 links
        public float Length;
        public float Radius;
    }

    /// <summary>Ein Stück der fertigen Mittellinie (quadratische Bézier, Kontrollpunkt im Tangentenschnitt).</summary>
    internal struct RoutePiece
    {
        public Vector2 P0, C, P2;
        public float H0, H1; // Richtung am Anfang/Ende (Bogenmaß)
        public int Turn;
        public float Length;
    }

    /// <summary>Was die Suche beachten soll.</summary>
    internal sealed class RouteSpec
    {
        public float Radius, MaxGrade, WaterCost, StraightNearWater, Edge, StraightStart;
        public float MaxCrossingDegrees = RouteRules.MaxCrossingDegrees;
        public Vector2 IdealExit;
        public int MaxExpansions = RouteRules.MaxExpansions;
    }

    /// <summary>
    /// Trassensuche (hybrides A*): Zustände = Lage (Raster) × Richtung (5°-Schritte); Bausteine gerade bzw. Bogen mit dem
    /// kleinsten Halbmesser, der genau einen Richtungsschritt dreht → die Trasse hält die Radien von selbst ein und ist
    /// tangentenstetig. Kosten: Länge + Wasser (Brücke) + Höhenunterschied über der Steigung + Zusatzkosten des Rasters
    /// + kleiner Bogenaufschlag; Wasser nur gerade und fast rechtwinklig zum Ufer (≤ 20°), in Ufernähe keine Bögen; Ende am
    /// Kartenrand auf Land, nicht zu schräg, Abweichung vom geraden Weg kostet. Deterministisch (Heap mit Einfügefolge).
    /// </summary>
    internal static class RoutePlanner
    {
        public static float Step => RouteRules.StepDegrees * MathF.PI / 180f;
        static readonly int[] Turns = { 0, 1, -1 };

        public static Vector2 Dir(float heading) => new Vector2(MathF.Cos(heading), MathF.Sin(heading));

        /// <summary>Endpunkt eines Bausteins.</summary>
        public static Vector2 EndOf(in RouteStep s, out float endHeading)
        {
            if (s.Turn == 0) { endHeading = s.Heading; return s.P + Dir(s.Heading) * s.Length; }
            float d = s.Turn * Step;
            endHeading = s.Heading + d;
            float chord = 2f * s.Radius * MathF.Sin(Step * 0.5f);
            return s.P + Dir(s.Heading + d * 0.5f) * chord;
        }

        /// <summary>Punkt bei Anteil f des Bausteins (und Richtung dort).</summary>
        public static Vector2 PointOf(in RouteStep s, float f, out float heading)
        {
            if (s.Turn == 0) { heading = s.Heading; return s.P + Dir(s.Heading) * (s.Length * f); }
            float d = s.Turn * Step * f;
            heading = s.Heading + d;
            float chord = 2f * s.Radius * MathF.Sin(MathF.Abs(d) * 0.5f);
            return s.P + Dir(s.Heading + d * 0.5f) * chord;
        }

        sealed class Node
        {
            public RouteStep Step;
            public Vector2 End;
            public int K; // Richtungsindex relativ zur Startrichtung
            public float G;
            public Node Parent;
            public bool Goal;
            public float WaterRun; // Länge der laufenden Wasserstrecke am Ende
        }

        /// <summary>Kosten eines Bausteins (float.PositiveInfinity = unzulässig); <paramref name="waterRun"/> wird fortgeschrieben.</summary>
        static float Cost(RouteField field, RouteSpec spec, in RouteStep s, ref float waterRun)
        {
            int n = Math.Max(2, (int)MathF.Ceiling(s.Length / 8f));
            float seg = s.Length / n;
            float cost = 0f;
            float cosMax = MathF.Cos(spec.MaxCrossingDegrees * MathF.PI / 180f);
            float limit = spec.Edge + 1f;
            var prevP = s.P;
            bool prevWet = field.Shore(prevP) < 0f;
            for (int i = 1; i <= n; i++)
            {
                var q = PointOf(s, i / (float)n, out float hd);
                if (MathF.Abs(q.X) > limit || MathF.Abs(q.Y) > limit) return float.PositiveInfinity;
                float sd = field.Shore(q);
                if (s.Turn != 0 && sd < spec.StraightNearWater) return float.PositiveInfinity;
                bool wet = sd < 0f;
                if (wet != prevWet)
                {
                    // Uferlinie gekreuzt: fast rechtwinklig?
                    var mid = (q + prevP) * 0.5f;
                    var nrm = field.ShoreNormal(mid);
                    if (nrm != Vector2.Zero && MathF.Abs(Vector2.Dot(nrm, Dir(hd))) < cosMax) return float.PositiveInfinity;
                }
                if (wet)
                {
                    waterRun += seg;
                    if (waterRun > RouteRules.MaxWaterRun) return float.PositiveInfinity;
                }
                else waterRun = 0f;
                cost += seg * (1f + (wet ? spec.WaterCost : 0f) + field.Extra(q));
                prevP = q;
                prevWet = wet;
            }
            var end = PointOf(s, 1f, out _);
            float dh = MathF.Abs(field.Height(end) - field.Height(s.P));
            cost += RouteRules.HeightCost * MathF.Max(0f, dh - s.Length * spec.MaxGrade);
            if (s.Turn != 0) cost += RouteRules.TurnPenalty * s.Length;
            return cost;
        }

        /// <summary>Gerade bis zum Rand: Länge (≥ 0) und Randnormale; false, wenn der Rand nicht vorn liegt.</summary>
        static bool ToEdge(Vector2 p, float heading, float edge, out float length, out Vector2 outward)
        {
            var d = Dir(heading);
            length = GatewayGenerator.ExitDistance(p, d, edge);
            outward = default;
            if (length == float.MaxValue || length < 0f) return false;
            var e = p + d * length;
            outward = MathF.Abs(e.X) >= MathF.Abs(e.Y) ? new Vector2(MathF.Sign(e.X), 0f) : new Vector2(0f, MathF.Sign(e.Y));
            return true;
        }

        /// <summary>
        /// Sucht die Trasse von <paramref name="start"/> (Richtung <paramref name="heading"/>) bis an den Kartenrand.
        /// Liefert die Bausteine (letzter = Gerade bis genau an den Rand) oder null.
        /// </summary>
        public static List<RouteStep> Plan(RouteField field, RouteSpec spec, Vector2 start, float heading, out int expansions)
        {
            expansions = 0;
            float step = Step;
            float len = spec.Radius * step; // Bogenlänge eines Bausteins = Länge der Geraden
            int bins = (int)MathF.Round(2f * MathF.PI / step);
            float sc = RouteRules.StateCell;
            int cells = (int)MathF.Ceiling(2f * (spec.Edge + 10f) / sc) + 2;

            var heap = new Heap();
            var best = new Dictionary<long, float>();
            long Key(Vector2 p, int k)
            {
                int cx = Math.Clamp((int)MathF.Floor((p.X + spec.Edge + 10f) / sc), 0, cells - 1);
                int cy = Math.Clamp((int)MathF.Floor((p.Y + spec.Edge + 10f) / sc), 0, cells - 1);
                int b = ((k % bins) + bins) % bins;
                return ((long)cy * cells + cx) * bins + b;
            }

            var root = new Node { End = start, K = 0, G = 0f, Step = new RouteStep { P = start, Heading = heading, Length = 0f } };
            // erzwungene Gerade am Anfang (z. B. Brücke über die Autobahn)
            if (spec.StraightStart > 0f)
            {
                var s0 = new RouteStep { P = start, Heading = heading, Turn = 0, Length = spec.StraightStart, Radius = spec.Radius };
                float wr = 0f;
                float c0 = Cost(field, spec, s0, ref wr);
                if (float.IsPositiveInfinity(c0)) return null;
                root = new Node { Step = s0, End = EndOf(s0, out _), K = 0, G = c0, Parent = root, WaterRun = wr };
            }
            heap.Push(root, root.G + RouteRules.HeuristicWeight * Vector2.Distance(root.End, spec.IdealExit));
            float cosEdge = MathF.Cos(RouteRules.EdgeMaxDegrees * MathF.PI / 180f);

            while (heap.Count > 0)
            {
                var cur = heap.Pop();
                if (cur.Goal) return Reconstruct(cur);
                if (++expansions > spec.MaxExpansions) return null;
                float h0 = heading + cur.K * step;

                // Ziel: Gerade bis an den Rand, wenn er nah vorn liegt
                if (ToEdge(cur.End, h0, spec.Edge, out float toEdge, out var outward) && toEdge <= 2.5f * len && toEdge >= 20f
                    && Vector2.Dot(Dir(h0), outward) >= cosEdge)
                {
                    var last = new RouteStep { P = cur.End, Heading = h0, Turn = 0, Length = toEdge, Radius = spec.Radius };
                    float wr = cur.WaterRun;
                    float c = Cost(field, spec, last, ref wr);
                    var e = EndOf(last, out _);
                    if (!float.IsPositiveInfinity(c) && field.Shore(e) > 8f && field.Shore(e - Dir(h0) * 60f) > 8f)
                    {
                        float g = cur.G + c + RouteRules.DeviationCost * Vector2.Distance(e, spec.IdealExit);
                        heap.Push(new Node { Step = last, End = e, K = cur.K, G = g, Parent = cur, Goal = true }, g);
                    }
                }

                foreach (int turn in Turns)
                {
                    var s = new RouteStep { P = cur.End, Heading = h0, Turn = turn, Length = len, Radius = spec.Radius };
                    float wr = cur.WaterRun;
                    float c = Cost(field, spec, s, ref wr);
                    if (float.IsPositiveInfinity(c)) continue;
                    var e = EndOf(s, out _);
                    int k = cur.K + turn;
                    if (MathF.Abs(k * step) > MathF.PI * 0.75f) continue; // keine Schleifen
                    float g = cur.G + c;
                    long key = Key(e, k);
                    if (best.TryGetValue(key, out float old) && old <= g) continue;
                    best[key] = g;
                    heap.Push(new Node { Step = s, End = e, K = k, G = g, Parent = cur, WaterRun = wr }, g + RouteRules.HeuristicWeight * Vector2.Distance(e, spec.IdealExit));
                }
            }
            return null;
        }

        static List<RouteStep> Reconstruct(Node goal)
        {
            var list = new List<RouteStep>();
            for (var n = goal; n != null; n = n.Parent)
                if (n.Step.Length > 0f) list.Add(n.Step);
            list.Reverse();
            return list;
        }

        /// <summary>
        /// Bausteine zu Stücken zusammenfassen: Geraden bis <paramref name="chunk"/> (über Stellen, an denen nicht geteilt werden
        /// darf, bis <paramref name="maxChunk"/>), Bögen gleicher Richtung bis <paramref name="maxTurnDegrees"/>.
        /// </summary>
        public static List<RoutePiece> ToPieces(List<RouteStep> steps, float chunk, float maxChunk, float maxTurnDegrees, Func<Vector2, bool> canCut)
        {
            var pieces = new List<RoutePiece>();
            int i = 0;
            int maxArcs = Math.Max(1, (int)MathF.Floor(maxTurnDegrees / RouteRules.StepDegrees + 1e-3f));
            while (i < steps.Count)
            {
                var first = steps[i];
                if (first.Turn != 0)
                {
                    int j = i;
                    while (j + 1 < steps.Count && steps[j + 1].Turn == first.Turn && j + 1 - i < maxArcs) j++;
                    var end = EndOf(steps[j], out float h1);
                    float total = (j - i + 1) * Step;
                    float tan = first.Radius * MathF.Tan(total * 0.5f);
                    pieces.Add(new RoutePiece
                    {
                        P0 = first.P, P2 = end, C = first.P + Dir(first.Heading) * tan, H0 = first.Heading, H1 = h1,
                        Turn = first.Turn, Length = first.Radius * total,
                    });
                    i = j + 1;
                    continue;
                }
                // Geraden: Gesamtlänge der Folge
                int last = i;
                while (last + 1 < steps.Count && steps[last + 1].Turn == 0) last++;
                var a = first.P;
                var b = EndOf(steps[last], out _);
                float length = Vector2.Distance(a, b);
                var d = Dir(first.Heading);
                // in Stücke ≈ chunk teilen, nur an erlaubten Stellen
                float pos = 0f;
                while (length - pos > chunk * 1.5f)
                {
                    float cut = -1f;
                    for (float s = pos + chunk; s <= MathF.Min(pos + maxChunk, length - 60f); s += 8f)
                        if (canCut(a + d * s)) { cut = s; break; }
                    if (cut < 0f)
                        for (float s = pos + chunk - 8f; s > pos + 60f; s -= 8f)
                            if (canCut(a + d * s)) { cut = s; break; }
                    if (cut < 0f) break;
                    pieces.Add(Straight(a + d * pos, a + d * cut, first.Heading));
                    pos = cut;
                }
                pieces.Add(Straight(a + d * pos, b, first.Heading));
                i = last + 1;
            }
            return pieces;
        }

        static RoutePiece Straight(Vector2 a, Vector2 b, float heading) => new RoutePiece
        {
            P0 = a, P2 = b, C = (a + b) * 0.5f, H0 = heading, H1 = heading, Turn = 0, Length = Vector2.Distance(a, b),
        };

        /// <summary>Stück umdrehen (Fahrtrichtung).</summary>
        public static RoutePiece Reverse(RoutePiece p) => new RoutePiece
        {
            P0 = p.P2, P2 = p.P0, C = p.C, H0 = p.H1 + MathF.PI, H1 = p.H0 + MathF.PI, Turn = -p.Turn, Length = p.Length,
        };

        /// <summary>
        /// Parallele im Abstand <paramref name="offset"/> (positiv = links der Richtung): Enden entlang der Normalen versetzt,
        /// Kontrollpunkt im Schnitt der Tangenten (Bögen bleiben Bögen mit R ∓ offset).
        /// </summary>
        public static RoutePiece Offset(RoutePiece p, float offset)
        {
            var d0 = Dir(p.H0); var d1 = Dir(p.H1);
            var n0 = new Vector2(-d0.Y, d0.X); var n1 = new Vector2(-d1.Y, d1.X);
            var a = p.P0 + n0 * offset; var b = p.P2 + n1 * offset;
            var c = (a + b) * 0.5f;
            if (p.Turn != 0 && LineIntersect(a, d0, b, d1, out var x)) c = x;
            float len = p.Turn == 0 ? Vector2.Distance(a, b) : Bezier.ApproximateLength(a, c, b);
            return new RoutePiece { P0 = a, P2 = b, C = c, H0 = p.H0, H1 = p.H1, Turn = p.Turn, Length = len };
        }

        static bool LineIntersect(Vector2 p, Vector2 r, Vector2 q, Vector2 s, out Vector2 x)
        {
            float den = r.X * s.Y - r.Y * s.X;
            x = default;
            if (MathF.Abs(den) < 1e-6f) return false;
            var qp = q - p;
            float t = (qp.X * s.Y - qp.Y * s.X) / den;
            x = p + r * t;
            return true;
        }

        /// <summary>Endstück (gerade) so verlängern/kürzen, dass sein Ende genau auf dem Rand liegt.</summary>
        public static void ClampEndToEdge(List<RoutePiece> pieces, float edge, bool atEnd)
        {
            if (pieces.Count == 0) return;
            int i = atEnd ? pieces.Count - 1 : 0;
            var p = pieces[i];
            if (p.Turn != 0) return;
            var e = atEnd ? p.P2 : p.P0;
            var o = atEnd ? p.P0 : p.P2;
            var d = Vector2.Normalize(e - o);
            var axis = MathF.Abs(e.X) >= MathF.Abs(e.Y) ? new Vector2(MathF.Sign(e.X), 0f) : new Vector2(0f, MathF.Sign(e.Y));
            float dd = Vector2.Dot(d, axis);
            if (dd < 0.2f) return;
            float s = (edge - Vector2.Dot(e, axis)) / dd;
            e += d * s;
            if (atEnd) p.P2 = e; else p.P0 = e;
            p.C = (p.P0 + p.P2) * 0.5f;
            p.Length = Vector2.Distance(p.P0, p.P2);
            pieces[i] = p;
        }

        /// <summary>Dichte Abtastung einer Stückfolge (für Gradiente/Prüfungen): Punkte etwa alle <paramref name="ds"/> m.</summary>
        public static List<Vector2> Sample(IReadOnlyList<RoutePiece> pieces, float ds, List<int> pieceStart = null)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                int n = Math.Max(1, (int)MathF.Ceiling(p.Length / ds));
                pieceStart?.Add(pts.Count);
                for (int k = 0; k < n; k++) pts.Add(Bezier.Evaluate(p.P0, p.C, p.P2, k / (float)n));
            }
            pieceStart?.Add(pts.Count);
            if (pieces.Count > 0) pts.Add(pieces[pieces.Count - 1].P2);
            return pts;
        }

        sealed class Heap
        {
            readonly List<(float f, long seq, Node n)> items = new List<(float, long, Node)>();
            long seq;
            public int Count => items.Count;

            static bool Less((float f, long seq, Node n) a, (float f, long seq, Node n) b) => a.f < b.f || (a.f == b.f && a.seq < b.seq);

            public void Push(Node n, float f)
            {
                items.Add((f, seq++, n));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (!Less(items[i], items[p])) break;
                    (items[i], items[p]) = (items[p], items[i]);
                    i = p;
                }
            }

            public Node Pop()
            {
                var top = items[0].n;
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
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
}
