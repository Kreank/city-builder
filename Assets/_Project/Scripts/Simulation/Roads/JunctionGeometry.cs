using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Wie wird ein Knoten dargestellt?</summary>
    public enum NodeShape
    {
        /// <summary>Straßenende, Sackgasse oder gerade Fortsetzung – keine eigene Fläche.</summary>
        None,
        /// <summary>Kreuzung (ab 3 Straßen) oder Übergang zwischen zwei Straßentypen: eigene Fläche, Segmente enden davor.</summary>
        Junction,
        /// <summary>Knick zwischen zwei Straßen gleichen Typs: Eckstück mit vollem Querschnitt (Bogen).</summary>
        Bend,
    }

    /// <summary>Eine an einem Knoten angeschlossene Straße, reduziert auf das, was die Eckgeometrie braucht.</summary>
    public struct JunctionArmSpec
    {
        /// <summary>Richtung vom Knoten weg (normalisiert).</summary>
        public Vector2 Dir;
        /// <summary>Halbe Fahrbahnbreite (ohne Gehwege).</summary>
        public float Carriageway;
        /// <summary>Halbe Gesamtbreite.</summary>
        public float Full;
        public bool HasSidewalk;
        /// <summary>Größter erlaubter Trimm-Abstand (Segmentlänge).</summary>
        public float MaxTrim;
    }

    /// <summary>Ein Arm einer fertig berechneten Kreuzung.</summary>
    public struct JunctionArm
    {
        public RoadSegment Segment;
        public JunctionArmSpec Spec;
        /// <summary>Bogenlänge vom Knoten bis zum Straßenende.</summary>
        public float Trim;
        /// <summary>Mittellinie am Straßenende.</summary>
        public Vector2 Point;
        /// <summary>Richtung am Straßenende, vom Knoten weg.</summary>
        public Vector2 Dir;
        /// <summary>Rechts, vom Knoten weg gesehen.</summary>
        public Vector2 Right;
        public float Height;
    }

    /// <summary>Gehweg-/Bordsteinecke zwischen Arm A und dem nächsten Arm B (gegen den Uhrzeigersinn).</summary>
    public sealed class JunctionCorner
    {
        public int ArmA, ArmB;
        /// <summary>Bordsteinkante (= Fahrbahnrand) von der linken Kante von A zur rechten von B.</summary>
        public Vector2[] Curb;
        /// <summary>
        /// Gehweg-Außenkante, gleich viele Punkte wie <see cref="Curb"/>. Auch gesetzt, wenn nur einer der beiden Arme
        /// einen Gehweg hat (<see cref="TaperedSidewalk"/>): dann läuft der Gehweg bis zum anderen Arm auf null Breite aus.
        /// </summary>
        public Vector2[] Outer;
        /// <summary>Beide Arme haben Gehwege → Gehwegecke wird gebaut (und ist begehbar, s. Gehwegnetz).</summary>
        public bool HasSidewalk;
        /// <summary>
        /// Nur ein Arm hat einen Gehweg (Stadtstraße ↔ Rampe, z. B. am Zubringer der Anschlussstelle): Gehweg läuft keilförmig
        /// aus, damit er nicht offen am Straßenende abbricht. Nur Darstellung – kein Fußweg.
        /// </summary>
        public bool TaperedSidewalk;
        /// <summary>Wird eine Gehweg-Fläche gezeichnet (voll oder auslaufend)?</summary>
        public bool DrawsSidewalk => HasSidewalk || TaperedSidewalk;
        /// <summary>Äußerste Kante der Ecke (Gehweg-Außenkante, sonst Fahrbahnrand) – für Brüstung/Schürze.</summary>
        public Vector2[] Boundary => Outer ?? Curb;
    }

    /// <summary>Fertige Kreuzungsfläche: Arme mit Trimm, Fahrbahn-Umriss und Ecken.</summary>
    public sealed class JunctionLayout
    {
        public readonly List<JunctionArm> Arms = new List<JunctionArm>();
        public readonly List<JunctionCorner> Corners = new List<JunctionCorner>();
        /// <summary>Fahrbahn-Umriss gegen den Uhrzeigersinn (ohne Gehwege).</summary>
        public readonly List<Vector2> Ring = new List<Vector2>();
        public readonly List<float> RingHeights = new List<float>();
    }

    /// <summary>Eckstück an einem Knick: quadratische Bézierkurve zwischen den gekürzten Segmentenden.</summary>
    public struct BendLayout
    {
        public RoadType Type;
        /// <summary>Segment, dessen Richtung das Eckstück übernimmt (Spuren links→rechts wie dort).</summary>
        public RoadSegment From, To;
        public Vector2 P0, Control, P2;
        public float H0, H2;
        /// <summary>
        /// Höhe am Kontrollpunkt: Die Höhe läuft als quadratische Bézierkurve H0 → HControl → H2 und erreicht in der Mitte
        /// (t = 0,5) genau die Knotenhöhe. Früher linear H0 → H2: auf einer Kuppe hing das Eckstück unter dem Knoten durch,
        /// dort liegt das Gelände aber knapp unter der Knotenhöhe und schaute durch die Fahrbahn.
        /// </summary>
        public float HControl;
        public float Trim;

        /// <summary>Höhe des Eckstücks beim Kurvenparameter t (0 = P0, 1 = P2).</summary>
        public float HeightAt(float t)
        {
            float u = 1f - t;
            return u * u * H0 + 2f * u * t * HControl + t * t * H2;
        }
    }

    /// <summary>
    /// Reine Geometrie der Straßenknoten (ohne Unity): Wie weit enden Segmente vor dem Knoten, wie sehen
    /// Fahrbahnfläche und Gehwegecken (gerundeter Bordstein) aus, und welches Eckstück bekommt ein Knick?
    /// Wird von <c>RoadMeshBuilder</c> benutzt und hier getestet.
    /// </summary>
    public static class JunctionGeometry
    {
        /// <summary>Bordsteinradius an Stadtstraßen (Fahrbahnrand).</summary>
        public const float CurbRadius = 4f;
        /// <summary>Eckradius zwischen Straßen ohne Gehweg (Autobahn, Rampe).</summary>
        public const float EdgeRadius = 1.5f;
        /// <summary>Zuschlag auf den berechneten Trimm-Abstand.</summary>
        public const float Margin = 0.5f;
        /// <summary>Höchster Trimm-Abstand.</summary>
        public const float MaxTrimDistance = 60f;
        /// <summary>Segmente werden höchstens um diesen Anteil ihrer Länge gekürzt (je Ende).</summary>
        public const float MaxTrimShare = 0.45f;
        /// <summary>Punkte je Eckbogen.</summary>
        public const int ArcSteps = 8;
        /// <summary>Ab dieser Abweichung von der Geraden (Grad) bekommt ein Knoten gleichen Typs ein Eckstück.</summary>
        public const float BendThreshold = 0.5f;
        /// <summary>Mindest-Innenradius im Knick-Eckstück (Rundung der Innenkante).</summary>
        public const float BendInnerRadius = 1f;

        // ------------------------------------------------------------ Einstufung

        public static NodeShape Classify(RoadNetwork network, RoadNode node)
        {
            int n = node.SegmentIds.Count;
            if (n >= 3) return NodeShape.Junction;
            if (n != 2) return NodeShape.None;
            var a = network.GetSegment(node.SegmentIds[0]);
            var b = network.GetSegment(node.SegmentIds[1]);
            if (a == null || b == null || a == b) return NodeShape.None;
            if (a.Type != b.Type) return NodeShape.Junction;
            float angle = RoadGeometry.Angle(RoadGeometry.OutgoingDirection(a, node), RoadGeometry.OutgoingDirection(b, node));
            return 180f - angle > BendThreshold ? NodeShape.Bend : NodeShape.None;
        }

        /// <summary>Wie weit endet <paramref name="segment"/> vor <paramref name="node"/> (Bogenlänge)?</summary>
        public static float Trim(RoadNetwork network, RoadNode node, RoadSegment segment)
        {
            switch (Classify(network, node))
            {
                case NodeShape.Junction:
                {
                    var arms = SortedArms(network, node, out var segments);
                    int i = segments.IndexOf(segment);
                    if (i < 0) return 0f;
                    return ComputeTrims(arms, arms.Count == 2)[i];
                }
                case NodeShape.Bend:
                    return Bend(network, node).Trim;
                default:
                    return 0f;
            }
        }

        // ------------------------------------------------------------ Arme

        static bool HasSidewalk(RoadType t) => t.Lanes.Count > 0 && t.Lanes[0].Kind == LaneKind.Sidewalk;

        public static JunctionArmSpec SpecOf(RoadSegment s, RoadNode node) => new JunctionArmSpec
        {
            Dir = RoadGeometry.OutgoingDirection(s, node),
            Carriageway = s.Type.CarriagewayHalfWidth,
            Full = s.Type.HalfWidth,
            HasSidewalk = HasSidewalk(s.Type),
            MaxTrim = MathF.Min(s.Length * MaxTrimShare, MaxTrimDistance),
        };

        static List<JunctionArmSpec> SortedArms(RoadNetwork network, RoadNode node, out List<RoadSegment> segments)
        {
            var list = new List<(float angle, RoadSegment s, JunctionArmSpec spec)>();
            foreach (int id in node.SegmentIds)
            {
                var s = network.GetSegment(id);
                var spec = SpecOf(s, node);
                list.Add((MathF.Atan2(spec.Dir.Y, spec.Dir.X), s, spec));
            }
            list.Sort((a, b) => a.angle.CompareTo(b.angle)); // gegen den Uhrzeigersinn
            segments = new List<RoadSegment>(list.Count);
            var arms = new List<JunctionArmSpec>(list.Count);
            foreach (var e in list) { segments.Add(e.s); arms.Add(e.spec); }
            return arms;
        }

        /// <summary>Öffnungswinkel von a nach b gegen den Uhrzeigersinn (0..360°).</summary>
        public static float Span(Vector2 a, Vector2 b)
        {
            float s = RoadGeometry.SignedAngle(a, b);
            if (s < 0f) s += 360f;
            if (s < 1e-3f) s = 360f;
            return s;
        }

        /// <summary>Eckradius zwischen zwei Armen (Bordstein bei Stadtstraßen).</summary>
        static float CornerRadius(JunctionArmSpec a, JunctionArmSpec b) => a.HasSidewalk && b.HasSidewalk ? CurbRadius : EdgeRadius;

        static float SidewalkWidth(JunctionArmSpec a) => a.Full - a.Carriageway;

        /// <summary>Radius der Gehweg-Außenkante – konzentrisch zum Bordstein, wenn möglich.</summary>
        static float OuterRadius(JunctionArmSpec a, JunctionArmSpec b) =>
            MathF.Max(0f, CornerRadius(a, b) - MathF.Max(SidewalkWidth(a), SidewalkWidth(b)));

        /// <summary>
        /// Trimm-Abstände je Arm (Arme gegen den Uhrzeigersinn sortiert). Ein Arm endet erst dort, wo
        /// (1) der gerundete Bordstein zu beiden Nachbarn vollständig vor dem Straßenende liegt und
        /// (2) sich die Außenkanten (Gehweg) mit den Nachbarn nicht mehr überdecken.
        /// Spitze Winkel ergeben so lange Trimm-Abstände (gedeckelt durch Segmentlänge).
        /// Zwei Arme (Typwechsel) zusätzlich mit Verjüngung ~1:4.
        /// </summary>
        public static float[] ComputeTrims(IReadOnlyList<JunctionArmSpec> arms, bool transition)
        {
            int n = arms.Count;
            var need = new float[n];
            for (int i = 0; i < n; i++)
            {
                var me = arms[i];
                need[i] = transition ? MathF.Max(6f, MathF.Abs(me.Full - arms[(i + 1) % n].Full) * 4f + 4f) : me.Full * 0.5f + 1f;
            }
            if (n < 2) return need;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (j == i) continue;
                var a = arms[i];
                var b = arms[j];
                float span = Span(a.Dir, b.Dir);
                if (span >= 175f) continue; // gerade Durchfahrt oder überstumpf: Kanten laufen auseinander

                // Ohne Gehwege (Autobahn-Gabel) wie bisher mit Mindestwinkel 20°, sonst würden spitze Gabeln sehr lang
                float effSpan = a.HasSidewalk || b.HasSidewalk ? span : MathF.Max(span, 20f);
                var db = a.HasSidewalk || b.HasSidewalk ? b.Dir : RoadGeometry.Rotate(a.Dir, effSpan);
                float tanHalf = MathF.Tan(effSpan * 0.5f * MathF.PI / 180f);

                // Fahrbahnrand (Bordstein) mit Radius
                if (EdgeDistances(a.Dir, a.Carriageway, db, b.Carriageway, out float sa, out float sb))
                {
                    float tangent = CornerRadius(a, b) / tanHalf;
                    need[i] = MathF.Max(need[i], sa + tangent + Margin);
                    need[j] = MathF.Max(need[j], sb + tangent + Margin);
                }
                // Außenkanten
                if (EdgeDistances(a.Dir, a.Full, db, b.Full, out float oa, out float ob))
                {
                    float tangent = OuterRadius(a, b) / tanHalf;
                    need[i] = MathF.Max(need[i], oa + tangent + Margin);
                    need[j] = MathF.Max(need[j], ob + tangent + Margin);
                }
            }
            for (int i = 0; i < n; i++) need[i] = MathF.Min(need[i], arms[i].MaxTrim);
            return need;
        }

        /// <summary>
        /// Schnitt der linken Kante von a (Abstand wa) mit der rechten Kante von b (wb), beide als Geraden
        /// durch den Knoten: Abstände entlang a bzw. b.
        /// </summary>
        static bool EdgeDistances(Vector2 da, float wa, Vector2 db, float wb, out float sa, out float sb)
        {
            var ea = Geometry2D.Left(da) * wa;
            var eb = Geometry2D.Right(db) * wb;
            return Geometry2D.LineIntersection(ea, da, eb, db, out sa, out sb);
        }

        // ------------------------------------------------------------ Kreuzungsfläche

        /// <summary>Abtastung eines Segments – identisch mit der, die die Straßen-Meshes benutzen.</summary>
        public static SampledCurve Sample(RoadSegment s) => new SampledCurve(s.Start.Position, s.Control, s.End.Position, s.HeightAt);

        /// <summary>
        /// Straßenende <paramref name="distance"/> (Bogenlänge) vor dem Knoten: Mittelpunkt, Richtung vom Knoten weg, Höhe.
        /// Gleiche Abtastung wie das Segment-Mesh → Kanten passen exakt aneinander.
        /// </summary>
        public static void EndFrame(RoadSegment s, RoadNode node, float distance, out Vector2 point, out Vector2 dirAway, out float height)
        {
            var curve = Sample(s);
            float d = node == s.Start ? distance : curve.Length - distance;
            curve.At(d, out point, out var right, out height);
            var tan = new Vector2(-right.Y, right.X);
            dirAway = node == s.Start ? tan : -tan;
        }

        /// <summary>Kreuzungsfläche (auch Typwechsel mit zwei Armen) für einen Knoten.</summary>
        public static JunctionLayout Layout(RoadNetwork network, RoadNode node)
        {
            var specs = SortedArms(network, node, out var segments);
            var trims = ComputeTrims(specs, specs.Count == 2);
            var layout = new JunctionLayout();
            for (int i = 0; i < specs.Count; i++)
            {
                var s = segments[i];
                EndFrame(s, node, trims[i], out var point, out var dir, out float height);
                layout.Arms.Add(new JunctionArm
                {
                    Segment = s,
                    Spec = specs[i],
                    Trim = trims[i],
                    Point = point,
                    Dir = dir,
                    Right = Geometry2D.Right(dir),
                    Height = height,
                });
            }
            BuildLayout(node.Position, layout, specs.Count == 2);
            return layout;
        }

        /// <summary>Fahrbahn-Umriss und Ecken aus den (bereits getrimmten) Armen.</summary>
        public static void BuildLayout(Vector2 node, JunctionLayout layout, bool transition)
        {
            var arms = layout.Arms;
            int n = arms.Count;
            for (int i = 0; i < n; i++)
            {
                var a = arms[i];
                var b = arms[(i + 1) % n];
                float radius = CornerRadius(a.Spec, b.Spec);
                var curb = CornerPath(node, a, b, a.Spec.Carriageway, b.Spec.Carriageway, radius, transition);
                bool sidewalk = a.Spec.HasSidewalk && b.Spec.HasSidewalk;
                bool tapered = !sidewalk && (a.Spec.HasSidewalk || b.Spec.HasSidewalk);
                Vector2[] outer = null;
                if (sidewalk) outer = CornerPath(node, a, b, a.Spec.Full, b.Spec.Full, OuterRadius(a.Spec, b.Spec), transition);
                else if (tapered)
                    // Gehweg nur an einem Arm: Außenkante von dessen Gehweg bis zum Fahrbahnrand des anderen (Keil, scharfe Ecke)
                    outer = CornerPath(node, a, b, a.Spec.HasSidewalk ? a.Spec.Full : a.Spec.Carriageway,
                        b.Spec.HasSidewalk ? b.Spec.Full : b.Spec.Carriageway, 0f, transition);
                layout.Corners.Add(new JunctionCorner
                {
                    ArmA = i, ArmB = (i + 1) % n, Curb = curb, Outer = outer, HasSidewalk = sidewalk, TaperedSidewalk = tapered,
                });

                layout.Ring.Add(a.Point + a.Right * a.Spec.Carriageway);
                layout.RingHeights.Add(a.Height);
                for (int k = 0; k < curb.Length - 1; k++)
                {
                    layout.Ring.Add(curb[k]);
                    layout.RingHeights.Add(a.Height + (b.Height - a.Height) * k / (curb.Length - 1));
                }
            }
        }

        /// <summary>Anzahl der Punkte jedes Eckverlaufs.</summary>
        public const int CornerPoints = ArcSteps + 3;

        /// <summary>
        /// Eckverlauf von der linken Kante von Arm a (Abstand wa) zur rechten Kante von Arm b (wb), gegen den
        /// Uhrzeigersinn, immer <see cref="CornerPoints"/> Punkte (Bordstein und Außenkante passen so zusammen):
        /// [Kante a am Straßenende, Bogen (ArcSteps+1 Punkte), Kante b am Straßenende].
        ///   Spitze/rechte Ecke: Kanten bis zum Bogen mit <paramref name="radius"/> (Bordstein), Radius 0 = scharfe Ecke.
        ///   Gerade Durchfahrt (≈ 180°) oder Typwechsel mit kleinem Knick: gerade Linie (Verjüngung).
        ///   Überstumpf: Bogen um den Knoten herum (Außenseite eines Knicks).
        /// </summary>
        public static Vector2[] CornerPath(Vector2 node, JunctionArm a, JunctionArm b, float wa, float wb, float radius, bool transition)
        {
            var pts = new Vector2[CornerPoints];
            var ea = a.Point - a.Right * wa;     // linke Kante von a
            var eb = b.Point + b.Right * wb;     // rechte Kante von b
            pts[0] = ea;
            pts[CornerPoints - 1] = eb;
            float span = Span(a.Dir, b.Dir);

            if (span < 175f && Geometry2D.LineIntersection(ea, -a.Dir, eb, -b.Dir, out float ua, out float ub) && ua >= -0.01f && ub >= -0.01f)
            {
                // Schnitt X der Kanten (vor beiden Straßenenden) → Bogen tangential an beide Kanten
                var x = ea - a.Dir * ua;
                float half = span * 0.5f * MathF.PI / 180f;
                float r = radius;
                float tangent = r / MathF.Tan(half);
                float room = MathF.Max(0f, MathF.Min(ua, ub));
                if (tangent > room) { tangent = room; r = tangent * MathF.Tan(half); }
                var ta = x + a.Dir * tangent;
                var tb = x + b.Dir * tangent;
                if (r < 1e-3f)
                {
                    for (int k = 1; k <= ArcSteps + 1; k++) pts[k] = x;
                    return pts;
                }
                var bis = Vector2.Normalize(a.Dir + b.Dir);
                var c = x + bis * (r / MathF.Sin(half));
                float from = MathF.Atan2(ta.Y - c.Y, ta.X - c.X);
                float to = MathF.Atan2(tb.Y - c.Y, tb.X - c.X);
                float sweep = to - from;
                while (sweep > MathF.PI) sweep -= 2f * MathF.PI;
                while (sweep < -MathF.PI) sweep += 2f * MathF.PI;
                for (int k = 0; k <= ArcSteps; k++)
                {
                    float ang = from + sweep * k / ArcSteps;
                    pts[k + 1] = c + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
                }
                pts[1] = ta;
                pts[ArcSteps + 1] = tb;
                return pts;
            }

            bool reflex = span > (transition ? 190f : 185f);
            if (!reflex)
            {
                // gerade Durchfahrt / Verjüngung / Kanten schneiden sich erst hinter den Straßenenden
                for (int k = 1; k < CornerPoints - 1; k++) pts[k] = Vector2.Lerp(ea, eb, k / (float)(CornerPoints - 1));
                return pts;
            }

            // Überstumpf: entlang der Kante von a bis neben den Knoten, Bogen um den Knoten, entlang b hinaus
            var na = Geometry2D.Left(a.Dir);
            var rb = Geometry2D.Right(b.Dir);
            float angA = MathF.Atan2(na.Y, na.X);
            float sweepR = (span - 180f) * MathF.PI / 180f;
            float angB = MathF.Atan2(rb.Y, rb.X);
            // Sweep an den tatsächlichen Richtungen am Straßenende ausrichten
            float actual = angB - angA;
            while (actual < 0f) actual += 2f * MathF.PI;
            while (actual >= 2f * MathF.PI) actual -= 2f * MathF.PI;
            if (MathF.Abs(actual - sweepR) < 0.5f) sweepR = actual;
            for (int k = 0; k <= ArcSteps; k++)
            {
                float u = k / (float)ArcSteps;
                float ang = angA + sweepR * u;
                float w = wa + (wb - wa) * u;
                pts[k + 1] = node + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * w;
            }
            return pts;
        }

        // ------------------------------------------------------------ Knick (Grad 2, gleicher Typ)

        /// <summary>
        /// Trimm-Abstand L für ein symmetrisches Bézier-Eckstück mit Innenwinkel φ (Grad, zwischen den Armen):
        /// Kleinster Radius einer quadratischen Bézierkurve mit Schenkeln L ist L·sin²(φ/2)/cos(φ/2) (im Scheitel).
        /// Er soll mindestens halbe Breite + <see cref="BendInnerRadius"/> sein → Innenkante ohne Selbstschnitt.
        /// </summary>
        public static float BendTrim(float halfWidth, float interiorAngle)
        {
            float half = interiorAngle * 0.5f * MathF.PI / 180f;
            float s = MathF.Sin(half), c = MathF.Cos(half);
            if (c < 1e-4f) return 0f;
            if (s < 1e-3f) return float.PositiveInfinity;
            return (halfWidth + BendInnerRadius) * c / (s * s);
        }

        /// <summary>Eckstück für einen Knick zwischen zwei Segmenten gleichen Typs.</summary>
        public static BendLayout Bend(RoadNetwork network, RoadNode node)
        {
            var s0 = network.GetSegment(node.SegmentIds[0]);
            var s1 = network.GetSegment(node.SegmentIds[1]);
            // Fahrtrichtung übernehmen: von dem Segment, das im Knoten endet, zu dem, das dort beginnt
            RoadSegment from = s0, to = s1;
            if (s0.Start == node && s1.End == node) { from = s1; to = s0; }

            var dFrom = RoadGeometry.OutgoingDirection(from, node);
            var dTo = RoadGeometry.OutgoingDirection(to, node);
            float interior = RoadGeometry.Angle(dFrom, dTo);
            float trim = BendTrim(from.Type.HalfWidth, interior);
            trim = MathF.Min(trim, MathF.Min(MathF.Min(from.Length, to.Length) * MaxTrimShare, MaxTrimDistance));

            EndFrame(from, node, trim, out var p0, out var awayFrom, out float h0);
            EndFrame(to, node, trim, out var p2, out var awayTo, out float h2);
            // Kontrollpunkt = Schnitt der Tangenten an den Straßenenden (≈ Knoten) → Eckstück tangential an beide
            var control = RoadBuilder.RayIntersection(p0, -awayFrom, p2, -awayTo) ?? node.Position;
            return new BendLayout
            {
                Type = from.Type,
                From = from,
                To = to,
                P0 = p0,
                Control = control,
                P2 = p2,
                H0 = h0,
                H2 = h2,
                // Scheitel (t = 0,5) genau auf Knotenhöhe; bei gleichmäßiger Steigung ergibt das wieder die Gerade H0 → H2
                HControl = 2f * node.Height - 0.5f * (h0 + h2),
                Trim = trim,
            };
        }
    }
}
