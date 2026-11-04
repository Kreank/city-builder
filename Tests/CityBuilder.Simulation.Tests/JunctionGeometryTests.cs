using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using Xunit;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Geometrie an Knoten: Trimm-Abstände, gerundete Bordsteinecken, Knick-Eckstücke und Innenkanten enger Kurven.
    /// Die Flächen werden genauso berechnet wie in RoadMeshBuilder (SampledCurve.OffsetAt, JunctionLayout).
    /// </summary>
    public class JunctionGeometryTests
    {
        static readonly RoadType City = RoadCatalog.CityTwoLane;

        // ------------------------------------------------------------ Helfer

        sealed class Piece
        {
            public string Name;
            public readonly List<(Vector2 a, Vector2 b, Vector2 c)> Tris = new List<(Vector2, Vector2, Vector2)>();
        }

        static Piece Strip(string name, SampledCurve c, float d0, float d1, float left, float right)
        {
            var piece = new Piece { Name = name };
            var st = new List<float>();
            c.Stations(d0, d1, st);
            for (int i = 0; i < st.Count - 1; i++)
            {
                var l0 = c.OffsetAt(st[i], left);
                var r0 = c.OffsetAt(st[i], right);
                var l1 = c.OffsetAt(st[i + 1], left);
                var r1 = c.OffsetAt(st[i + 1], right);
                piece.Tris.Add((l0, l1, r0));
                piece.Tris.Add((r0, l1, r1));
            }
            return piece;
        }

        /// <summary>Gehwege einzeln und die Fahrbahn als ein Streifen – wie das Segment-Mesh.</summary>
        static IEnumerable<Piece> RoadPieces(string name, SampledCurve c, RoadType type, float d0, float d1)
        {
            float cl = float.MaxValue, cr = float.MinValue;
            foreach (var lane in type.Lanes)
            {
                if (lane.Kind == LaneKind.Sidewalk)
                    yield return Strip($"{name}/Gehweg{(lane.Offset < 0 ? "L" : "R")}", c, d0, d1, lane.Left, lane.Right);
                else { cl = MathF.Min(cl, lane.Left); cr = MathF.Max(cr, lane.Right); }
            }
            yield return Strip($"{name}/Fahrbahn", c, d0, d1, cl, cr);
        }

        static IEnumerable<Piece> SegmentPieces(RoadNetwork net, RoadSegment s)
        {
            var c = JunctionGeometry.Sample(s);
            float d0 = JunctionGeometry.Trim(net, s.Start, s);
            float d1 = c.Length - JunctionGeometry.Trim(net, s.End, s);
            return RoadPieces($"Segment {s.Id}", c, s.Type, d0, d1);
        }

        static IEnumerable<Piece> JunctionPieces(JunctionLayout layout)
        {
            var asphalt = new Piece { Name = "Kreuzung/Fahrbahn" };
            var tri = Geometry2D.Triangulate(layout.Ring);
            for (int i = 0; i < tri.Count; i += 3)
                asphalt.Tris.Add((layout.Ring[tri[i]], layout.Ring[tri[i + 1]], layout.Ring[tri[i + 2]]));
            yield return asphalt;
            foreach (var corner in layout.Corners)
            {
                if (!corner.DrawsSidewalk) continue; // auch auslaufende Gehwege (nur ein Arm mit Gehweg)
                var p = new Piece { Name = $"Ecke {corner.ArmA}-{corner.ArmB}" };
                for (int k = 0; k < corner.Curb.Length - 1; k++)
                {
                    p.Tris.Add((corner.Curb[k], corner.Curb[k + 1], corner.Outer[k]));
                    p.Tris.Add((corner.Outer[k], corner.Curb[k + 1], corner.Outer[k + 1]));
                }
                yield return p;
            }
        }

        /// <summary>Erste flächige Überlappung zwischen zwei verschiedenen Teilen (oder null).</summary>
        static string FindOverlap(IList<Piece> pieces)
        {
            for (int i = 0; i < pieces.Count; i++)
                for (int j = i + 1; j < pieces.Count; j++)
                    foreach (var a in pieces[i].Tris)
                    {
                        if (MathF.Abs(Geometry2D.TriangleArea(a.a, a.b, a.c)) < 1e-4f) continue;
                        foreach (var b in pieces[j].Tris)
                        {
                            if (MathF.Abs(Geometry2D.TriangleArea(b.a, b.b, b.c)) < 1e-4f) continue;
                            if (Geometry2D.TrianglesOverlap(a.a, a.b, a.c, b.a, b.b, b.c, 0.02f))
                                return $"{pieces[i].Name} überlappt {pieces[j].Name} bei {a.a}";
                        }
                    }
            return null;
        }

        /// <summary>Überlappt ein Teil sich selbst (nicht benachbarte Dreiecke)?</summary>
        static bool SelfOverlaps(Piece p)
        {
            for (int i = 0; i < p.Tris.Count; i++)
                for (int j = i + 4; j < p.Tris.Count; j++)
                {
                    var a = p.Tris[i];
                    var b = p.Tris[j];
                    if (MathF.Abs(Geometry2D.TriangleArea(a.a, a.b, a.c)) < 1e-4f) continue;
                    if (MathF.Abs(Geometry2D.TriangleArea(b.a, b.b, b.c)) < 1e-4f) continue;
                    if (Geometry2D.TrianglesOverlap(a.a, a.b, a.c, b.a, b.b, b.c, 0.02f)) return true;
                }
            return false;
        }

        /// <summary>Stücke der Kante, die gegen die Kurvenrichtung laufen.</summary>
        static int BackwardSteps(SampledCurve c, Vector2[] line)
        {
            int n = 0;
            for (int i = 0; i < line.Length - 1; i++)
            {
                var r = c.RightAt(i);
                var tan = new Vector2(-r.Y, r.X);
                if (Vector2.Dot(line[i + 1] - line[i], tan) < -1e-3f) n++;
            }
            return n;
        }

        static RoadNode NodeAt(RoadNetwork net, Vector2 p) => net.FindNearestNode(p, 1f);

        static List<Piece> AllPieces(RoadNetwork net)
        {
            var pieces = new List<Piece>();
            foreach (var s in net.Segments) pieces.AddRange(SegmentPieces(net, s));
            foreach (var n in net.Nodes)
            {
                var shape = JunctionGeometry.Classify(net, n);
                if (shape == NodeShape.Junction) pieces.AddRange(JunctionPieces(JunctionGeometry.Layout(net, n)));
                else if (shape == NodeShape.Bend)
                {
                    var bend = JunctionGeometry.Bend(net, n);
                    var c = new SampledCurve(bend.P0, bend.Control, bend.P2, t => 0f);
                    pieces.AddRange(RoadPieces($"Knick {n.Id}", c, bend.Type, 0f, c.Length));
                }
            }
            return pieces;
        }

        // ------------------------------------------------------------ Kreuzungen

        [Fact]
        public void FourWay_TrimFollowsCurbRadius()
        {
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(-60, 0), new Vector2(60, 0));
            RoadNetworkTests.Build(net, new Vector2(0, -60), new Vector2(0, 60));
            var node = NodeAt(net, Vector2.Zero);
            Assert.Equal(4, node.SegmentIds.Count);
            Assert.Equal(NodeShape.Junction, JunctionGeometry.Classify(net, node));

            // 90°: Bordsteinbogen beginnt bei Fahrbahnhalbbreite + Radius/tan(45°) – plus Zuschlag
            float expected = City.CarriagewayHalfWidth + JunctionGeometry.CurbRadius + JunctionGeometry.Margin;
            foreach (int id in node.SegmentIds)
                Assert.Equal(expected, JunctionGeometry.Trim(net, node, net.GetSegment(id)), 2);

            // Bordstein-Ecke ist gerundet: Mittelpunkt des Bogens liegt um (√2 − 1)·R von der scharfen Ecke entfernt
            var layout = JunctionGeometry.Layout(net, node);
            var cw = City.CarriagewayHalfWidth;
            foreach (var corner in layout.Corners)
            {
                var mid = corner.Curb[corner.Curb.Length / 2];
                float fromSharpCorner = MathF.Min(
                    MathF.Min(Vector2.Distance(mid, new Vector2(cw, cw)), Vector2.Distance(mid, new Vector2(-cw, cw))),
                    MathF.Min(Vector2.Distance(mid, new Vector2(cw, -cw)), Vector2.Distance(mid, new Vector2(-cw, -cw))));
                Assert.Equal((MathF.Sqrt(2f) - 1f) * JunctionGeometry.CurbRadius, fromSharpCorner, 1);
            }
        }

        [Fact]
        public void FourWay_SidewalksAndCarriagewayDoNotOverlap()
        {
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(-60, 0), new Vector2(60, 0));
            RoadNetworkTests.Build(net, new Vector2(0, -60), new Vector2(0, 60));
            Assert.Null(FindOverlap(AllPieces(net)));
        }

        [Fact]
        public void TJunction_DoesNotOverlap()
        {
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(-60, 0), new Vector2(60, 0));
            RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(0, 60));
            var node = NodeAt(net, Vector2.Zero);
            Assert.Equal(3, node.SegmentIds.Count);
            Assert.Null(FindOverlap(AllPieces(net)));
        }

        [Theory]
        [InlineData(30f)]
        [InlineData(45f)]
        [InlineData(60f)]
        public void AcuteCrossing_DoesNotOverlap(float angle)
        {
            var net = new RoadNetwork();
            float r = angle * MathF.PI / 180f;
            var d = new Vector2(MathF.Cos(r), MathF.Sin(r)) * 100f;
            RoadNetworkTests.Build(net, new Vector2(-100, 0), new Vector2(100, 0));
            RoadNetworkTests.Build(net, -d, d);
            var node = NodeAt(net, Vector2.Zero);
            Assert.Equal(4, node.SegmentIds.Count);
            Assert.Null(FindOverlap(AllPieces(net)));
        }

        [Fact]
        public void MixedWidths_JunctionDoesNotOverlap()
        {
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(-80, 0), new Vector2(80, 0), RoadCatalog.CityFourLane);
            RoadNetworkTests.Build(net, new Vector2(-50, -60), new Vector2(50, 60));
            Assert.Null(FindOverlap(AllPieces(net)));
        }

        [Fact]
        public void JunctionRing_TriangulationCoversArea()
        {
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(-60, 0), new Vector2(60, 0));
            RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(40, 60));
            var layout = JunctionGeometry.Layout(net, NodeAt(net, Vector2.Zero));
            Assert.True(Geometry2D.SignedArea(layout.Ring) > 0f, "Umriss gegen den Uhrzeigersinn");
            var tri = Geometry2D.Triangulate(layout.Ring);
            float sum = 0f;
            for (int i = 0; i < tri.Count; i += 3)
            {
                float a = Geometry2D.TriangleArea(layout.Ring[tri[i]], layout.Ring[tri[i + 1]], layout.Ring[tri[i + 2]]);
                Assert.True(a >= -1e-4f);
                sum += a;
            }
            Assert.Equal(Geometry2D.SignedArea(layout.Ring), sum, 1);
        }

        [Fact]
        public void TypeTransition_StraightKeepsTaper()
        {
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(-100, 0), new Vector2(0, 0), RoadCatalog.CityFourLane);
            RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(100, 0));
            var node = NodeAt(net, Vector2.Zero);
            Assert.Equal(NodeShape.Junction, JunctionGeometry.Classify(net, node));
            float dw = RoadCatalog.CityFourLane.HalfWidth - City.HalfWidth;
            foreach (int id in node.SegmentIds)
                Assert.Equal(dw * 4f + 4f, JunctionGeometry.Trim(net, node, net.GetSegment(id)), 2);
            Assert.Null(FindOverlap(AllPieces(net)));
        }

        [Fact]
        public void TypeTransition_At90Degrees_DoesNotOverlap()
        {
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(-80, 0), new Vector2(0, 0), RoadCatalog.CityFourLane);
            RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(0, 80));
            Assert.Equal(NodeShape.Junction, JunctionGeometry.Classify(net, NodeAt(net, Vector2.Zero)));
            Assert.Null(FindOverlap(AllPieces(net)));
        }

        // ------------------------------------------------------------ Knick (Grad 2)

        [Fact]
        public void Classify_StraightContinuationNeedsNothing_KnickGetsBend()
        {
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(50, 0));
            RoadNetworkTests.Build(net, new Vector2(50, 0), new Vector2(100, 0));
            Assert.Equal(NodeShape.None, JunctionGeometry.Classify(net, NodeAt(net, new Vector2(50, 0))));

            RoadNetworkTests.Build(net, new Vector2(100, 0), new Vector2(100, 50));
            var knick = NodeAt(net, new Vector2(100, 0));
            Assert.Equal(NodeShape.Bend, JunctionGeometry.Classify(net, knick));
            Assert.True(JunctionGeometry.Trim(net, knick, net.GetSegment(knick.SegmentIds[0])) > City.HalfWidth);
        }

        [Fact]
        public void Bend90_SidewalksOfBothSegmentsDoNotOverlap()
        {
            var net = new RoadNetwork();
            var a = RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(50, 0))[0];
            var b = RoadNetworkTests.Build(net, new Vector2(50, 0), new Vector2(50, 50))[0];

            // Gegenprobe: ungekürzt (bisheriges Verhalten) überlappen sich die Gehwege im Knick
            var untrimmed = new List<Piece>();
            untrimmed.AddRange(RoadPieces("A", JunctionGeometry.Sample(a), City, 0f, a.Length));
            untrimmed.AddRange(RoadPieces("B", JunctionGeometry.Sample(b), City, 0f, b.Length));
            Assert.NotNull(FindOverlap(untrimmed));

            var pieces = AllPieces(net);
            Assert.Null(FindOverlap(pieces));
            foreach (var p in pieces) Assert.False(SelfOverlaps(p), p.Name);

            // Eckstück: Innenkante behält einen Radius (≥ BendInnerRadius), außen Bogen
            var bend = JunctionGeometry.Bend(net, NodeAt(net, new Vector2(50, 0)));
            var c = new SampledCurve(bend.P0, bend.Control, bend.P2, t => 0f);
            Assert.True(c.MinRadius >= City.HalfWidth + JunctionGeometry.BendInnerRadius - 0.1f, $"Radius {c.MinRadius}");
        }

        [Fact]
        public void SharpBend_ShortSegments_StillNoSelfIntersection()
        {
            // 135° Knick, kurze Segmente → Trimm wird gedeckelt, Innenkante muss zusammengezogen werden
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(30, 0), RoadCatalog.CityFourLane);
            RoadNetworkTests.Build(net, new Vector2(30, 0), new Vector2(30 - 21.2f, 21.2f), RoadCatalog.CityFourLane);
            var node = NodeAt(net, new Vector2(30, 0));
            Assert.Equal(NodeShape.Bend, JunctionGeometry.Classify(net, node));
            var bend = JunctionGeometry.Bend(net, node);
            var c = new SampledCurve(bend.P0, bend.Control, bend.P2, t => 0f);
            float hw = RoadCatalog.CityFourLane.HalfWidth;
            // Die Straßen überdecken sich hier selbst (Schenkel zu kurz) – geprüft wird, dass keine Kante sich schneidet
            foreach (var lane in RoadCatalog.CityFourLane.Lanes)
            {
                Assert.False(Geometry2D.HasSelfIntersection(c.OffsetLine(lane.Left)), $"Kante {lane.Left}");
                Assert.False(Geometry2D.HasSelfIntersection(c.OffsetLine(lane.Right)), $"Kante {lane.Right}");
            }
        }

        // ------------------------------------------------------------ Enge Kurven

        [Fact]
        public void TightCurve_InnerEdgeHasNoSelfIntersection()
        {
            // Enge Linkskurve (≈ 140°), Scheitelradius ≈ 4 m, Versatz innen 10 m (halbe Breite 4-spurig)
            var c = new SampledCurve(new Vector2(0, 0), new Vector2(25, 0), new Vector2(5, 20), t => 0f);
            Assert.True(c.MinRadius < 5f);
            const float offset = -10f; // links = innen

            // Gegenprobe: ungeklemmter Versatz läuft im Scheitel rückwärts (Schwalbenschwanz)
            var raw = new Vector2[c.Count];
            for (int i = 0; i < c.Count; i++) raw[i] = c.Point(i) + c.RightAt(i) * offset;
            Assert.True(BackwardSteps(c, raw) > 0, "Gegenprobe: ungeklemmter Versatz läuft rückwärts");

            var line = c.OffsetLine(offset);
            Assert.False(Geometry2D.HasSelfIntersection(line));
            Assert.Equal(0, BackwardSteps(c, line));

            // Gehweg innen (−10…−7) und Fahrbahn (−7…7) überlappen sich weder selbst noch gegenseitig
            var pieces = RoadPieces("Kurve", c, RoadCatalog.CityFourLane, 0f, c.Length).ToList();
            foreach (var p in pieces) Assert.False(SelfOverlaps(p), p.Name);
            Assert.Null(FindOverlap(pieces));
        }

        [Fact]
        public void GentleCurve_OffsetIsUnchanged()
        {
            var c = new SampledCurve(new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), t => 0f);
            for (int i = 0; i < c.Count; i++)
            {
                var expected = c.Point(i) + c.RightAt(i) * -6.5f;
                Assert.True(Vector2.Distance(expected, c.OffsetAt(c.Distance(i), -6.5f)) < 1e-3f);
            }
        }

        // ------------------------------------------------------------ Übergänge (Spieltest: Zubringer, Rampen, Kuppen)

        /// <summary>
        /// Stadtstraße ↔ Rampe (wie am Zubringer der Anschlussstelle): Der Gehweg der Stadtstraße bricht nicht offen ab,
        /// sondern läuft in der Ecke zum Fahrbahnrand der Rampe aus – ohne Überlappung mit Fahrbahnen.
        /// </summary>
        [Fact]
        public void CityMeetsRamp_SidewalkTapersInsteadOfEndingOpen()
        {
            var net = new RoadNetwork();
            RoadNetworkTests.Build(net, new Vector2(0, -80), new Vector2(0, 0));
            RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(0, 80));
            RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(80, 0), RoadCatalog.Ramp);
            var node = NodeAt(net, Vector2.Zero);
            Assert.Equal(NodeShape.Junction, JunctionGeometry.Classify(net, node));
            var layout = JunctionGeometry.Layout(net, node);

            var tapered = layout.Corners.Where(c => c.TaperedSidewalk).ToList();
            Assert.Equal(2, tapered.Count); // beide Ecken neben der Rampe
            Assert.Single(layout.Corners, c => c.HasSidewalk); // Ecke Stadt ↔ Stadt bleibt begehbar
            foreach (var c in tapered)
            {
                Assert.False(c.HasSidewalk); // kein Fußweg (Gehwegnetz unverändert)
                Assert.NotNull(c.Outer);
                Assert.Equal(c.Curb.Length, c.Outer.Length);
                var a = layout.Arms[c.ArmA];
                var b = layout.Arms[c.ArmB];
                // am Arm mit Gehweg beginnt die Außenkante auf seiner Gehweg-Außenkante, am Rampen-Arm endet sie auf dem Bordstein
                var sidewalkArm = a.Spec.HasSidewalk ? a : b;
                var sidewalkEnd = a.Spec.HasSidewalk ? c.Outer[0] : c.Outer[c.Outer.Length - 1];
                var rampEnd = a.Spec.HasSidewalk ? c.Outer[c.Outer.Length - 1] : c.Outer[0];
                var rampCurb = a.Spec.HasSidewalk ? c.Curb[c.Curb.Length - 1] : c.Curb[0];
                Assert.Equal(sidewalkArm.Spec.Full, Vector2.Distance(sidewalkEnd, sidewalkArm.Point), 2);
                Assert.True(Vector2.Distance(rampEnd, rampCurb) < 1e-3f);
                Assert.Equal(c.Outer, c.Boundary);
            }
            Assert.Null(FindOverlap(AllPieces(net)));
        }

        [Fact]
        public void JunctionRings_AreStarShapedAroundTheNode_FanCoversArea()
        {
            foreach (var setup in new Action<RoadNetwork>[]
            {
                n => { RoadNetworkTests.Build(n, new Vector2(-60, 0), new Vector2(60, 0)); RoadNetworkTests.Build(n, new Vector2(0, -60), new Vector2(0, 60)); },
                n => { RoadNetworkTests.Build(n, new Vector2(-60, 0), new Vector2(60, 0)); RoadNetworkTests.Build(n, new Vector2(0, 0), new Vector2(40, 60)); },
                n => { RoadNetworkTests.Build(n, new Vector2(-80, 0), new Vector2(0, 0), RoadCatalog.CityFourLane); RoadNetworkTests.Build(n, new Vector2(0, 0), new Vector2(80, 0)); },
                n => { RoadNetworkTests.Build(n, new Vector2(-100, 0), new Vector2(100, 0)); RoadNetworkTests.Build(n, new Vector2(-87, -50), new Vector2(87, 50)); },
            })
            {
                var net = new RoadNetwork();
                setup(net);
                var node = NodeAt(net, Vector2.Zero);
                var layout = JunctionGeometry.Layout(net, node);
                Assert.True(Geometry2D.IsStarShaped(layout.Ring, node.Position));
                // Fächer um den Knoten = Fläche des Umrisses (keine Rückseiten)
                float sum = 0f;
                for (int i = 0; i < layout.Ring.Count; i++)
                {
                    float a = Geometry2D.TriangleArea(node.Position, layout.Ring[i], layout.Ring[(i + 1) % layout.Ring.Count]);
                    Assert.True(a >= -1e-3f);
                    sum += a;
                }
                Assert.Equal(Geometry2D.SignedArea(layout.Ring), sum, 1);
            }
            // Gegenprobe: Punkt außerhalb bzw. hinter einer Einbuchtung
            var poly = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(5, 3), new Vector2(0, 10) };
            Assert.False(Geometry2D.IsStarShaped(poly, new Vector2(5, 8)));
            Assert.True(Geometry2D.IsStarShaped(poly, new Vector2(5, 1)));
            Assert.False(Geometry2D.IsStarShaped(poly, new Vector2(20, 5)));
        }

        /// <summary>Knick auf einer Kuppe: das Eckstück erreicht in der Mitte die Knotenhöhe (hing früher durch).</summary>
        [Fact]
        public void BendOnCrest_ReachesNodeHeight_StraightGradeStaysLinear()
        {
            var net = new RoadNetwork();
            Build3D(net, new Vector2(-60, 0), 0f, new Vector2(0, 0), 3f);
            Build3D(net, new Vector2(0, 0), 3f, new Vector2(0, 60), 0f); // 90°-Knick: Eckstück ~12 m je Seite
            var node = NodeAt(net, Vector2.Zero);
            Assert.Equal(NodeShape.Bend, JunctionGeometry.Classify(net, node));
            var bend = JunctionGeometry.Bend(net, node);
            // linear hätte die Mitte (H0 + H2)/2 gelegen – tiefer als das Gelände knapp unter der Knotenhöhe
            Assert.True((bend.H0 + bend.H2) * 0.5f < 3f - Environment.TerrainMap.UnderRoad - 0.1f, $"{bend.H0} {bend.H2}");
            Assert.Equal(3f, bend.HeightAt(0.5f), 3);
            Assert.Equal(bend.H0, bend.HeightAt(0f), 4);
            Assert.Equal(bend.H2, bend.HeightAt(1f), 4);

            // gleichmäßige Steigung durch den Knick: weiter linear
            var net2 = new RoadNetwork();
            Build3D(net2, new Vector2(-60, 0), 0f, new Vector2(0, 0), 3f);
            Build3D(net2, new Vector2(0, 0), 3f, new Vector2(0, 60), 6f);
            var b2 = JunctionGeometry.Bend(net2, NodeAt(net2, Vector2.Zero));
            for (int i = 0; i <= 4; i++)
                Assert.Equal(b2.H0 + (b2.H2 - b2.H0) * i / 4f, b2.HeightAt(i / 4f), 2);
        }

        static void Build3D(RoadNetwork net, Vector2 a, float ha, Vector2 b, float hb)
        {
            var plan = RoadBuilder.Plan(net, a, 8f, null, b, 8f, City, ha, hb);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(net, plan);
        }

        [Fact]
        public void Triangulate_ConcavePolygon()
        {
            var poly = new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(5, 3), new Vector2(0, 10) };
            var tri = Geometry2D.Triangulate(poly);
            Assert.Equal(9, tri.Count);
            float sum = 0f;
            for (int i = 0; i < tri.Count; i += 3) sum += Geometry2D.TriangleArea(poly[tri[i]], poly[tri[i + 1]], poly[tri[i + 2]]);
            Assert.Equal(Geometry2D.SignedArea(poly), sum, 3);
        }
    }
}
