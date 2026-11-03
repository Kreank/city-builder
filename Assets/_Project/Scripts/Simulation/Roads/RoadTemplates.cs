using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    public enum RoadTemplateKind
    {
        /// <summary>Anschlussstelle (Raute): Stadtstraße über die Autobahn, 4 Rampen.</summary>
        DiamondInterchange,
        /// <summary>Autobahnkreuz (Kleeblatt): 2 Autobahnen, 4 Schleifen + 4 direkte Rampen.</summary>
        Cloverleaf,
        /// <summary>Autobahndreieck: Autobahn trifft Autobahn (T-Form), 2 Schleifen + 2 direkte Rampen.</summary>
        Triangle,
    }

    /// <summary>Ein Teilstück einer Vorlage in lokalen Koordinaten.</summary>
    public struct TemplatePiece
    {
        public Vector2 P0, Control, P2;
        public float H0, H2;
        public RoadType Type;
        /// <summary>Ebenerdiges Teilstück: folgt dem Gelände.</summary>
        public bool FollowTerrain;
    }

    public sealed class TemplatePlan
    {
        public RoadTemplateKind Kind;
        public Vector2 Origin;
        public float RotationDegrees;
        /// <summary>Teilstücke in Weltkoordinaten, in Bau-Reihenfolge.</summary>
        public readonly List<TemplatePiece> Pieces = new List<TemplatePiece>();
        public RoadBuildError Error;
        public float Cost;
        /// <summary>Bauwerk, das ein Teilstück überschneiden würde (Bauwerks-Sperre).</summary>
        public StructureBlock BlockedBy;
        public bool IsValid => Error == RoadBuildError.None;
    }

    /// <summary>
    /// Fertige Autobahnknoten als Vorlagen (wie Assets in Cities: Skylines).
    /// Lokales System: Autobahn A entlang x (ebenerdig), B bzw. Querstraße entlang y (Brücke).
    /// Rechtsverkehr: Fahrbahn in +x liegt bei y = -s, in -x bei y = +s; +y bei x = +s, -y bei x = -s.
    /// Die Enden der Vorlage sind offene Knoten – dort schließt der Spieler seine Autobahnen an.
    /// </summary>
    public static class RoadTemplates
    {
        const float S = 12f;          // Abstand Fahrbahnachse ↔ Mitte
        const float BridgeHeight = 10f;
        static readonly float Sin15 = MathF.Sin(15f * MathF.PI / 180f);
        static readonly float Cos15 = MathF.Cos(15f * MathF.PI / 180f);

        public static string DisplayName(RoadTemplateKind kind)
        {
            switch (kind)
            {
                case RoadTemplateKind.DiamondInterchange: return "Anschlussstelle (Raute)";
                case RoadTemplateKind.Cloverleaf: return "Autobahnkreuz (Kleeblatt)";
                default: return "Autobahndreieck";
            }
        }

        /// <summary>Halbe Ausdehnung der Vorlage (für Vorschau/Platzierung).</summary>
        public static float HalfSize(RoadTemplateKind kind) => kind == RoadTemplateKind.DiamondInterchange ? 400f : 500f;

        // ------------------------------------------------------------ Planen / Bauen

        public static TemplatePlan Plan(RoadNetwork network, RoadTemplateKind kind, Vector2 origin, float rotationDegrees,
            RoadType highway = null, RoadType crossRoad = null)
        {
            highway ??= RoadCatalog.Highway2;
            crossRoad ??= RoadCatalog.CityTwoLane;
            var plan = new TemplatePlan { Kind = kind, Origin = origin, RotationDegrees = rotationDegrees };
            if (!highway.IsHighway || crossRoad.Category != RoadCategory.City) { plan.Error = RoadBuildError.InvalidType; return plan; }

            var local = new List<TemplatePiece>();
            switch (kind)
            {
                case RoadTemplateKind.DiamondInterchange: Diamond(local, highway, crossRoad); break;
                case RoadTemplateKind.Cloverleaf: Clover(local, highway, includeAll: true); break;
                default: Clover(local, highway, includeAll: false); break;
            }

            float rad = rotationDegrees * MathF.PI / 180f;
            float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            Vector2 Tf(Vector2 p) => origin + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);

            foreach (var p in local)
            {
                var w = p;
                w.P0 = Tf(p.P0); w.Control = Tf(p.Control); w.P2 = Tf(p.P2);
                // Vorlagenhöhen sind relativ zum Gelände; ebenerdige Stücke folgen ihm
                w.FollowTerrain = p.H0 < 0.01f && p.H2 < 0.01f;
                w.H0 = p.H0 + network.Ground.HeightAt(w.P0);
                w.H2 = p.H2 + network.Ground.HeightAt(w.P2);
                plan.Pieces.Add(w);
            }

            // Prüfen gegen den Bestand (die Teilstücke untereinander sind per Konstruktion verträglich).
            foreach (var piece in plan.Pieces)
            {
                var rp = RoadBuilder.Plan(network, piece.P0, 0f, piece.Control, piece.P2, 0f, piece.Type, piece.H0, piece.H2,
                    followTerrain: piece.FollowTerrain);
                plan.Cost += rp.Cost;
                if (!rp.IsValid && rp.Error != RoadBuildError.Duplicate && plan.Error == RoadBuildError.None) { plan.Error = rp.Error; plan.BlockedBy = rp.BlockedBy; }
            }
            if (plan.Error == RoadBuildError.None && !RoadBuilder.Affordable(network, plan.Cost)) plan.Error = RoadBuildError.InsufficientFunds;
            return plan;
        }

        public static List<RoadSegment> Build(RoadNetwork network, TemplatePlan plan)
        {
            if (plan == null || !plan.IsValid) return new List<RoadSegment>();
            // Stadtkasse: Gesamtpreis aus dem Plan einmal buchen
            return RoadBuilder.Charged(network, plan.Cost, () =>
            {
                var built = new List<RoadSegment>();
                foreach (var piece in plan.Pieces)
                {
                    var rp = RoadBuilder.Plan(network, piece.P0, 0.5f, piece.Control, piece.P2, 0.5f, piece.Type, piece.H0, piece.H2,
                        followTerrain: piece.FollowTerrain);
                    built.AddRange(RoadBuilder.Build(network, rp));
                }
                return built;
            });
        }

        // ------------------------------------------------------------ Anschlussstelle (Raute)

        static void Diamond(List<TemplatePiece> list, RoadType highway, RoadType crossRoad)
        {
            const float E = 400f, L = 250f, C = 52f, H = 8f;
            var ramp = RoadCatalog.Ramp;

            // Richtungsfahrbahnen
            Chain(list, highway, new[] { V(-E, -S), V(-L, -S), V(L, -S), V(E, -S) }, new[] { 0f, 0f, 0f, 0f });
            Chain(list, highway, new[] { V(E, S), V(L, S), V(-L, S), V(-E, S) }, new[] { 0f, 0f, 0f, 0f });
            // Querstraße als Brücke
            Chain(list, crossRoad, new[] { V(0, -E), V(0, -C), V(0, C), V(0, E) }, new[] { 0f, H, H, 0f });

            // Rampen: Ausfahrt mit 15° rechts weg, Einfahrt mit 15° von rechts hinein
            Add(list, ramp, V(-L, -S), Dir(-15f), V(0, -C), V(1, 0), 0f, H);       // Ausfahrt Richtung +x
            Add(list, ramp, V(0, -C), V(1, 0), V(L, -S), Dir(15f), H, 0f);          // Einfahrt Richtung +x
            Add(list, ramp, V(L, S), Dir(165f), V(0, C), V(-1, 0), 0f, H);          // Ausfahrt Richtung -x
            Add(list, ramp, V(0, C), V(-1, 0), V(-L, S), Dir(195f), H, 0f);         // Einfahrt Richtung -x
        }

        // ------------------------------------------------------------ Kleeblatt / Dreieck

        /// <summary>
        /// Quadrant 4 (x &gt; 0, y &lt; 0) wird einmal beschrieben und um 90°-Schritte gedreht.
        /// Bei Drehung tauschen A und B ihre Rollen; die Höhe ergibt sich daraus, an welcher Autobahn ein Ende hängt.
        /// </summary>
        static void Clover(List<TemplatePiece> list, RoadType highway, bool includeAll)
        {
            const float E = 500f, D = 409f, Wk = 30f;
            var ramp = RoadCatalog.Ramp;

            // Richtungsfahrbahnen
            if (includeAll)
            {
                Chain(list, highway, Xs(-S, new[] { -E, -D, -Wk, Wk, D, E }), HeightsA(6));
                Chain(list, highway, Xs(S, new[] { E, D, Wk, -Wk, -D, -E }), HeightsA(6));
                Chain(list, highway, Ys(S, new[] { -E, -D, -Wk, Wk, D, E }), HeightsB(new[] { -E, -D, -Wk, Wk, D, E }));
                Chain(list, highway, Ys(-S, new[] { E, D, Wk, -Wk, -D, -E }), HeightsB(new[] { E, D, Wk, -Wk, -D, -E }));
            }
            else
            {
                // Dreieck: B endet an A (nur Süden). B+y endet bei y = +Wk, B-y beginnt dort.
                Chain(list, highway, Xs(-S, new[] { -E, -D, D, E }), HeightsA(4));
                Chain(list, highway, Xs(S, new[] { E, Wk, -Wk, -E }), HeightsA(4));
                Chain(list, highway, Ys(S, new[] { -E, -D, Wk }), HeightsB(new[] { -E, -D, Wk }));
                Chain(list, highway, Ys(-S, new[] { Wk, -D, -E }), HeightsB(new[] { Wk, -D, -E }));
            }

            // Quadrant 4 lokal: Schleife A+x → B+y und direkte Rampe B+y → A+x
            const float G = 13f, R = 60f;
            float c = S + G + R; // Schleifenmittelpunkt (c, -c)
            var loop = new[]
            {
                (V(Wk, -S), Dir(-15f), 'A'),
                (V(c, -S - G), V(1, 0), ' '),
                (V(c + R, -c), V(0, -1), ' '),
                (V(c, -c - R), V(-1, 0), ' '),
                (V(S + G, -c), V(0, 1), ' '),
                (V(S, -Wk), Dir(105f), 'B'),
            };
            var direct = new[]
            {
                (V(S, -D), Dir(75f), 'B'),
                (V(175f, -175f), Dir(45f), ' '),
                (V(D, -S), Dir(15f), 'A'),
            };

            // Drehungen: 0 = Q4, 1 = Q1, 2 = Q2, 3 = Q3. Dreieck: direkte Rampen in Q4/Q3, Schleifen in Q1/Q2.
            for (int k = 0; k < 4; k++)
            {
                bool wantLoop = includeAll || k == 1 || k == 2;
                bool wantDirect = includeAll || k == 0 || k == 3;
                if (wantLoop) RotatedPath(list, ramp, loop, k);
                if (wantDirect) RotatedPath(list, ramp, direct, k);
            }
        }

        /// <summary>Höhe der Autobahn B (Brücke) an der Stelle y: in der Mitte oben, zu den Enden auf 0.</summary>
        static float HeightB(float y)
        {
            float a = MathF.Abs(y);
            if (a <= 120f) return BridgeHeight;
            return MathF.Max(0f, BridgeHeight * (500f - a) / 380f);
        }

        static float[] HeightsA(int count) => new float[count];

        static float[] HeightsB(float[] ys)
        {
            var h = new float[ys.Length];
            for (int i = 0; i < ys.Length; i++) h[i] = HeightB(ys[i]);
            return h;
        }

        static Vector2[] Xs(float y, float[] xs)
        {
            var r = new Vector2[xs.Length];
            for (int i = 0; i < xs.Length; i++) r[i] = V(xs[i], y);
            return r;
        }

        static Vector2[] Ys(float x, float[] ys)
        {
            var r = new Vector2[ys.Length];
            for (int i = 0; i < ys.Length; i++) r[i] = V(x, ys[i]);
            return r;
        }

        /// <summary>
        /// Legt einen Pfad (Punkte mit Fahrtrichtung) um k·90° gedreht an. Endpunkte mit 'A'/'B'
        /// hängen an Autobahn A bzw. B – nach ungerader Drehung vertauscht. Dazwischen gleichmäßige Steigung.
        /// </summary>
        static void RotatedPath(List<TemplatePiece> list, RoadType type, (Vector2 p, Vector2 dir, char attach)[] path, int k)
        {
            int n = path.Length;
            var pts = new Vector2[n];
            var dirs = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                pts[i] = Rot90(path[i].p, k);
                dirs[i] = Rot90(path[i].dir, k);
            }

            float EndHeight(int i)
            {
                char a = path[i].attach;
                bool onB = (a == 'B') ^ (k % 2 == 1);
                return onB ? HeightB(pts[i].Y) : 0f;
            }

            var controls = new Vector2[n - 1];
            var lengths = new float[n - 1];
            float total = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                controls[i] = Control(pts[i], dirs[i], pts[i + 1], dirs[i + 1]);
                lengths[i] = Bezier.ApproximateLength(pts[i], controls[i], pts[i + 1]);
                total += lengths[i];
            }

            float h0 = EndHeight(0), h1 = EndHeight(n - 1);
            float acc = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                float ha = h0 + (h1 - h0) * (acc / total);
                acc += lengths[i];
                float hb = h0 + (h1 - h0) * (acc / total);
                list.Add(new TemplatePiece { P0 = pts[i], Control = controls[i], P2 = pts[i + 1], H0 = ha, H2 = hb, Type = type });
            }
        }

        // ------------------------------------------------------------ Helfer

        static Vector2 V(float x, float y) => new Vector2(x, y);

        static Vector2 Dir(float degrees)
        {
            float r = degrees * MathF.PI / 180f;
            return new Vector2(MathF.Cos(r), MathF.Sin(r));
        }

        static Vector2 Rot90(Vector2 p, int k)
        {
            for (int i = 0; i < k; i++) p = new Vector2(-p.Y, p.X);
            return p;
        }

        /// <summary>Kontrollpunkt aus Start-/Endrichtung (Schnitt der Tangenten), sonst Mitte.</summary>
        static Vector2 Control(Vector2 p0, Vector2 d0, Vector2 p2, Vector2 d2) =>
            RoadBuilder.RayIntersection(p0, Vector2.Normalize(d0), p2, -Vector2.Normalize(d2)) ?? (p0 + p2) * 0.5f;

        static void Add(List<TemplatePiece> list, RoadType type, Vector2 p0, Vector2 d0, Vector2 p2, Vector2 d2, float h0, float h2) =>
            list.Add(new TemplatePiece { P0 = p0, Control = Control(p0, d0, p2, d2), P2 = p2, H0 = h0, H2 = h2, Type = type });

        static void Chain(List<TemplatePiece> list, RoadType type, Vector2[] pts, float[] heights)
        {
            for (int i = 0; i < pts.Length - 1; i++)
                list.Add(new TemplatePiece
                {
                    P0 = pts[i], Control = (pts[i] + pts[i + 1]) * 0.5f, P2 = pts[i + 1],
                    H0 = heights[i], H2 = heights[i + 1], Type = type,
                });
        }
    }
}
