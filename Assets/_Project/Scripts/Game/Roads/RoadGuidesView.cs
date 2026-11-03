using System.Collections.Generic;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Bauhilfen fürs Straßen-Werkzeug: Verlängerungs- und Lotlinien, Winkelbögen, Kurvengriffe,
    /// Fahrtrichtungspfeil, Einrast-Markierung sowie Beschriftungen (Länge, Winkel, Kosten, Fehler).
    /// Bauhilfe Parallelabstand: gestrichelte Parallelen im optimalen Abstand, Zonentiefe bestehender Straßen und der
    /// Vorschau, Blockraster (F10).
    /// Reine Anzeige – liest nur den Zustand des <see cref="RoadTool"/>.
    /// </summary>
    public sealed class RoadGuidesView : MonoBehaviour
    {
        const float LineOffset = 0.45f;
        const int ArcSegments = 24;
        /// <summary>Höhe der Hilfslinien – folgt der Bauhöhe (Brücken).</summary>
        float LineHeight = LineOffset;

        static readonly Color GuideColor = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color ArcColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        static readonly Color HandleColor = new Color(0.4f, 0.9f, 1f, 0.8f);
        static readonly Color SnapColor = new Color(0.3f, 1f, 0.5f, 0.95f);
        static readonly Color DirectionColor = new Color(1f, 1f, 1f, 0.9f);

        RoadTool tool;
        Material lineMaterial;
        readonly List<LineRenderer> pool = new List<LineRenderer>();
        readonly List<(Vector3 world, string text, bool error)> labels = new List<(Vector3, string, bool)>();
        int used;
        GUIStyle labelStyle, errorStyle;

        public void Init(RoadTool roadTool, Material baseMaterial)
        {
            tool = roadTool;
            lineMaterial = new Material(baseMaterial) { color = Color.white, name = "RoadGuides" };
        }

        void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }

        void LateUpdate()
        {
            used = 0;
            labels.Clear();

            if (tool != null && tool.Camera != null && tool.HasCursor)
            {
                float camDist = Vector3.Distance(tool.Camera.transform.position, SimConvert.ToWorld(tool.Cursor));
                float width = Mathf.Max(0.15f, camDist * 0.0025f);

                var g = tool.Guide;
                LineHeight = LineOffset + (g.Active ? Mathf.Max(g.StartHeight, g.EndHeight) : tool.CursorHeight);
                if (tool.CurrentMode == RoadTool.Mode.Zone)
                {
                    var c = CityBuilder.Game.Zoning.ZoneView.ZoneColor(tool.SelectedZone);
                    c.a = 0.95f;
                    LineHeight = LineOffset + tool.CursorHeight + 0.5f;
                    Circle(tool.Cursor, tool.BrushRadius, c, width * 1.5f, 40);
                }
                else DrawSnapMarker(tool.CursorAnchor, width);
                // --- Bauhilfe Parallelabstand
                if (tool.ShowParallelGuides)
                {
                    DrawParallelGuides(width);
                    if (g.Active && !g.LabelOnly && !g.IsRoundabout && g.Type != null) DrawOwnZones(g, width);
                }
                if (tool.BlockGrid && tool.CurrentMode == RoadTool.Mode.Straight && g.Active && !g.LabelOnly && !g.IsRoundabout && g.Type != null)
                    DrawBlockTicks(g, width);
                // --- Ende Bauhilfe
                if (g.Active)
                {
                    if (g.LabelOnly) DrawLabels(g);
                    else if (g.IsRoundabout) DrawRoundabout(g, width);
                    else DrawRoad(g, width, camDist);
                }
                else if (tool.IsBuildMode && Mathf.Abs(tool.BuildHeight) > 0.1f)
                {
                    labels.Add((SimConvert.ToWorld(tool.Cursor, LineHeight), $"Bauhöhe {tool.BuildHeight:0} m", false));
                }
            }

            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        // ------------------------------------------------------------ Inhalte

        void DrawSnapMarker(RoadAnchor anchor, float width)
        {
            if (anchor.Kind == AnchorKind.Free) return;
            float r = anchor.Kind == AnchorKind.Node ? 3f : 2f;
            float old = LineHeight;
            LineHeight = LineOffset + anchor.Height;
            Circle(anchor.Position, r + width * 4f, SnapColor, width * 1.2f, 20);
            LineHeight = old;
        }

        void DrawLabels(RoadTool.GuideState g)
        {
            if (!string.IsNullOrEmpty(g.Info))
                labels.Add((SimConvert.ToWorld(g.Start, LineHeight + 3f), g.Info, false));
            if (!string.IsNullOrEmpty(g.Error))
                labels.Add((SimConvert.ToWorld(g.Start, LineHeight + 3f), g.Error, true));
        }

        void DrawRoad(RoadTool.GuideState g, float width, float camDist)
        {
            float guideLength = Mathf.Max(80f, g.Length * 1.5f);
            float arcRadius = Mathf.Clamp(g.Length * 0.3f, g.Type.Width * 0.9f, camDist * 0.08f + g.Type.Width);

            if (g.HasStartReference)
            {
                Line(g.Start, g.Start - g.StartReference * guideLength, GuideColor, width);
                var perp = new SimVector2(-g.StartReference.Y, g.StartReference.X);
                Line(g.Start - perp * guideLength * 0.5f, g.Start + perp * guideLength * 0.5f, GuideColor, width);
                Arc(g.Start, g.StartReference, g.StartDirection, arcRadius, width * 1.4f);
                labels.Add((ArcLabelPos(g.Start, g.StartReference, g.StartDirection, arcRadius), $"{g.StartAngle:0}°", false));
            }
            else
            {
                Line(g.Start - SimVector2.UnitY * guideLength * 0.5f, g.Start + SimVector2.UnitY * guideLength * 0.5f, GuideColor, width);
                Line(g.Start - SimVector2.UnitX * guideLength * 0.5f, g.Start + SimVector2.UnitX * guideLength * 0.5f, GuideColor, width);
                Arc(g.Start, SimVector2.UnitY, g.StartDirection, arcRadius, width * 1.4f);
                labels.Add((ArcLabelPos(g.Start, SimVector2.UnitY, g.StartDirection, arcRadius), $"{g.Bearing:0}° {Compass(g.Bearing)}", false));
            }

            if (g.HasEndReference)
            {
                Line(g.End, g.End - g.EndReference * guideLength * 0.5f, GuideColor, width);
                Arc(g.End, g.EndReference, g.EndDirection, arcRadius, width * 1.4f);
                labels.Add((ArcLabelPos(g.End, g.EndReference, g.EndDirection, arcRadius), $"{g.EndAngle:0}°", false));
            }

            if (g.IsCurve && !g.PlacingControl)
            {
                Line(g.Start, g.Control, HandleColor, width);
                Line(g.Control, g.End, HandleColor, width);
            }

            // Fahrtrichtung bei Einbahnstraßen/Autobahnen
            if (g.Type.IsOneWay && g.Length > 6f)
                DirectionArrow(Bezier.Evaluate(g.Start, g.Control, g.End, 0.5f),
                    SafeDir(Bezier.Derivative(g.Start, g.Control, g.End, 0.5f)), g.Type.Width * 0.35f, width * 1.5f);

            if (g.Length > 0.5f)
            {
                var mid = Bezier.Evaluate(g.Start, g.Control, g.End, 0.5f);
                var side = SafeDir(Bezier.Derivative(g.Start, g.Control, g.End, 0.5f));
                var labelPos = mid + new SimVector2(side.Y, -side.X) * (g.Type.HalfWidth + 6f);
                string text = g.PlacingControl
                    ? $"{g.Length:0} m (Richtung)"
                    : $"{g.Length:0} m · {g.Cost:0} €";
                if (!string.IsNullOrEmpty(g.Info)) text += "\n" + g.Info;
                labels.Add((SimConvert.ToWorld(labelPos, LineHeight), text, false));
            }

            if (!string.IsNullOrEmpty(g.Error))
                labels.Add((SimConvert.ToWorld(g.End, LineHeight), g.Error, true));
        }

        void DrawRoundabout(RoadTool.GuideState g, float width)
        {
            Line(g.Center, g.Center + new SimVector2(g.Radius, 0f), GuideColor, width);
            Circle(g.Center, 1.5f, GuideColor, width, 12);
            // Fahrtrichtung gegen den Uhrzeigersinn andeuten
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.PI * 0.5f * i + Mathf.PI * 0.25f;
                var p = g.Center + g.Radius * new SimVector2(Mathf.Cos(a), Mathf.Sin(a));
                DirectionArrow(p, new SimVector2(-Mathf.Sin(a), Mathf.Cos(a)), g.Type.Width * 0.35f, width * 1.5f);
            }

            labels.Add((SimConvert.ToWorld(g.Center + new SimVector2(g.Radius * 0.5f, 4f), LineHeight),
                $"r = {g.Radius:0} m · {g.Cost:0} €", false));
            if (!string.IsNullOrEmpty(g.Error))
                labels.Add((SimConvert.ToWorld(g.Center, LineHeight), g.Error, true));
        }

        // ------------------------------------------------------------ Bauhilfe Parallelabstand

        const float GuideSearchRadius = 60f;
        const int MaxGuideRoads = 3;
        const float DashLength = 4f, DashGap = 3f;
        const float GuideRange = 150f;
        static readonly Color ParallelColor = new Color(0.45f, 0.85f, 1f, 0.75f);
        static readonly Color ParallelActiveColor = new Color(0.3f, 1f, 0.5f, 1f);
        static readonly Color ZoneBandColor = new Color(1f, 1f, 1f, 0.07f);
        static readonly Color ZoneRowColor = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color OwnZoneColor = new Color(0.35f, 1f, 0.55f, 0.16f);
        static readonly Color OwnZoneEdgeColor = new Color(0.35f, 1f, 0.55f, 0.55f);
        static readonly Color BlockTickColor = new Color(1f, 0.85f, 0.3f, 0.6f);

        readonly List<GuideHit> guideHits = new List<GuideHit>();
        readonly List<int> guideSeen = new List<int>();

        /// <summary>Gestrichelte Parallelen (optimaler Abstand für den gewählten Typ) neben den nächsten Stadtstraßen,
        /// dazu deren Zonentiefe als zarte Fläche mit Reihenlinien.</summary>
        void DrawParallelGuides(float width)
        {
            var roads = tool.Runner.World.Roads;
            ParallelGuides.FindGuides(roads, tool.Cursor, tool.SelectedType, tool.ParallelRows, GuideSearchRadius, guideHits);
            var snapped = tool.SnappedGuide;
            guideSeen.Clear();
            foreach (var h in guideHits)
            {
                if (guideSeen.Count >= MaxGuideRoads) break;
                var s = h.Segment;
                if (guideSeen.Contains(s.Id)) continue;
                guideSeen.Add(s.Id);

                for (int side = -1; side <= 1; side += 2)
                {
                    ZoneBand(s, side, ZoneGrid.Depth, ZoneBandColor, ZoneRowColor, width);
                    bool active = snapped.HasValue && snapped.Value.Segment == s && snapped.Value.Side == side;
                    GuideRangeOf(s, h, out float t0, out float t1);
                    DashedOffset(s, t0, t1, side * h.Offset, active ? ParallelActiveColor : ParallelColor, width * (active ? 1.8f : 1.1f));
                }
            }
        }

        /// <summary>Gerade Straßen: Linie ±150 m um den Cursor, auch über die Enden hinaus (Verlängerung). Kurven: ganz.</summary>
        static void GuideRangeOf(RoadSegment s, GuideHit h, out float t0, out float t1)
        {
            if (!h.Straight || s.Length < 1f) { t0 = 0f; t1 = 1f; return; }
            float ext = ParallelGuides.Extension / s.Length, range = GuideRange / s.Length;
            t0 = Mathf.Max(-ext, h.T - range);
            t1 = Mathf.Min(1f + ext, h.T + range);
        }

        void DashedOffset(RoadSegment s, float t0, float t1, float signedOffset, Color color, float width)
        {
            float length = Mathf.Abs(t1 - t0) * s.Length * (1f + Mathf.Abs(signedOffset) / Mathf.Max(50f, s.Length) * 0.5f);
            float step = DashLength + DashGap;
            int dashes = Mathf.Clamp(Mathf.CeilToInt(length / step), 1, 60);
            step = length / dashes;
            float dash = step * DashLength / (DashLength + DashGap);
            for (int i = 0; i < dashes; i++)
            {
                float a = t0 + (t1 - t0) * (i * step) / length;
                float b = t0 + (t1 - t0) * (i * step + dash) / length;
                var lr = Next(color, width);
                lr.positionCount = 2;
                lr.SetPosition(0, OnGround(ParallelGuides.OffsetPoint(s, a, signedOffset)));
                lr.SetPosition(1, OnGround(ParallelGuides.OffsetPoint(s, b, signedOffset)));
            }
        }

        /// <summary>Zonenstreifen einer Straßenseite: flache Fläche + Linien an den Reihengrenzen.</summary>
        void ZoneBand(RoadSegment s, int side, int rows, Color fill, Color rowLine, float width)
        {
            float depth = ParallelGuides.ZoneDepth(rows);
            const int n = 24;
            var band = NextFlat(fill, depth);
            band.positionCount = n + 1;
            for (int i = 0; i <= n; i++)
                band.SetPosition(i, OnGround(ParallelGuides.OffsetPoint(s, i / (float)n, side * (s.Type.HalfWidth + depth * 0.5f)), 0.05f));
            for (int r = 1; r <= rows; r++)
            {
                var lr = Next(rowLine, width * 0.7f);
                lr.positionCount = n + 1;
                for (int i = 0; i <= n; i++)
                    lr.SetPosition(i, OnGround(ParallelGuides.OffsetPoint(s, i / (float)n, side * (s.Type.HalfWidth + r * ZoneGrid.CellSize))));
            }
        }

        /// <summary>Vorschau: eigene Zonentiefe der gerade gezogenen Straße beidseitig (halbtransparent).</summary>
        void DrawOwnZones(RoadTool.GuideState g, float width)
        {
            if (g.Length < 1f) return;
            float depth = ParallelGuides.ZoneDepth(ZoneGrid.Depth);
            const int n = 24;
            for (int side = -1; side <= 1; side += 2)
            {
                var band = NextFlat(OwnZoneColor, depth);
                band.positionCount = n + 1;
                var edge = Next(OwnZoneEdgeColor, width);
                edge.positionCount = n + 1;
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    var p = Bezier.Evaluate(g.Start, g.Control, g.End, t);
                    var d = SafeDir(Bezier.Derivative(g.Start, g.Control, g.End, t));
                    var right = new SimVector2(d.Y, -d.X) * side;
                    band.SetPosition(i, OnGround(p + right * (g.Type.HalfWidth + depth * 0.5f), 0.08f));
                    edge.SetPosition(i, OnGround(p + right * (g.Type.HalfWidth + depth)));
                }
            }
        }

        /// <summary>Blockraster (F10): Querlinien dort, wo eine Querstraße gleichen Typs den Block ab dem Start ohne Rest
        /// füllt (8-m-Takt hinter der Kreuzungsfläche).</summary>
        void DrawBlockTicks(RoadTool.GuideState g, float width)
        {
            if (g.Length < 1f) return;
            const int n = 64;
            var cumulative = new float[n + 1];
            for (int i = 1; i <= n; i++)
                cumulative[i] = cumulative[i - 1] + SimVector2.Distance(Bezier.Evaluate(g.Start, g.Control, g.End, (i - 1) / (float)n),
                                                                        Bezier.Evaluate(g.Start, g.Control, g.End, i / (float)n));
            float reach = g.Type.HalfWidth + ParallelGuides.ZoneDepth(ZoneGrid.Depth);
            int drawn = 0;
            for (int k = 1; drawn < 60; k++)
            {
                float at = ParallelGuides.BlockLength(g.Type, g.Type, k);
                if (at > cumulative[n] + 0.5f) break;
                int i = 1;
                while (i < n && cumulative[i] < at) i++;
                float f = Mathf.InverseLerp(cumulative[i - 1], cumulative[i], at);
                float t = (i - 1 + f) / n;
                var p = Bezier.Evaluate(g.Start, g.Control, g.End, t);
                var d = SafeDir(Bezier.Derivative(g.Start, g.Control, g.End, t));
                var right = new SimVector2(d.Y, -d.X);
                bool atEnd = Mathf.Abs(at - g.Length) < 0.6f;
                Line3(OnGround(p - right * reach), OnGround(p + right * reach), atEnd ? ParallelActiveColor : BlockTickColor, width * (atEnd ? 1.6f : 0.9f));
                drawn++;
            }
        }

        Vector3 OnGround(SimVector2 p, float lift = 0.15f)
        {
            var ground = tool.Runner.World.Roads.Ground;
            return SimConvert.ToWorld(p, ground.HeightAt(p) + LineOffset + lift);
        }

        void Line3(Vector3 a, Vector3 b, Color color, float width)
        {
            var lr = Next(color, width);
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }

        /// <summary>Flach liegendes Band (Breite in Metern, nicht zur Kamera gedreht).</summary>
        LineRenderer NextFlat(Color color, float width)
        {
            var lr = Next(color, width);
            lr.alignment = LineAlignment.TransformZ;
            lr.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            lr.numCapVertices = 0;
            return lr;
        }

        // --- Ende Bauhilfe Parallelabstand

        // ------------------------------------------------------------ Primitive

        static string Compass(float bearing)
        {
            string[] names = { "N", "NO", "O", "SO", "S", "SW", "W", "NW" };
            return names[Mathf.RoundToInt(bearing / 45f) % 8];
        }

        static SimVector2 SafeDir(SimVector2 v) => v.LengthSquared() > 1e-6f ? SimVector2.Normalize(v) : SimVector2.UnitY;

        Vector3 ArcLabelPos(SimVector2 center, SimVector2 from, SimVector2 to, float radius)
        {
            float half = RoadGeometry.SignedAngle(from, to) * 0.5f;
            var dir = RoadGeometry.Rotate(from, half);
            return SimConvert.ToWorld(center + dir * radius * 1.35f, LineHeight);
        }

        void DirectionArrow(SimVector2 at, SimVector2 dir, float size, float width)
        {
            var right = new SimVector2(dir.Y, -dir.X);
            var tip = at + dir * size;
            var lr = Next(DirectionColor, width);
            lr.positionCount = 3;
            lr.SetPosition(0, SimConvert.ToWorld(at - dir * size * 0.3f - right * size * 0.8f, LineHeight));
            lr.SetPosition(1, SimConvert.ToWorld(tip, LineHeight));
            lr.SetPosition(2, SimConvert.ToWorld(at - dir * size * 0.3f + right * size * 0.8f, LineHeight));
        }

        void Arc(SimVector2 center, SimVector2 from, SimVector2 to, float radius, float width)
        {
            float angle = RoadGeometry.SignedAngle(from, to);
            var lr = Next(ArcColor, width);
            lr.positionCount = ArcSegments + 1;
            for (int i = 0; i <= ArcSegments; i++)
            {
                var dir = RoadGeometry.Rotate(from, angle * i / ArcSegments);
                lr.SetPosition(i, SimConvert.ToWorld(center + dir * radius, LineHeight));
            }
        }

        void Circle(SimVector2 center, float radius, Color color, float width, int segments)
        {
            var lr = Next(color, width);
            lr.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                lr.SetPosition(i, SimConvert.ToWorld(center + radius * new SimVector2(Mathf.Cos(a), Mathf.Sin(a)), LineHeight));
            }
        }

        void Line(SimVector2 a, SimVector2 b, Color color, float width)
        {
            var lr = Next(color, width);
            lr.positionCount = 2;
            lr.SetPosition(0, SimConvert.ToWorld(a, LineHeight));
            lr.SetPosition(1, SimConvert.ToWorld(b, LineHeight));
        }

        LineRenderer Next(Color color, float width)
        {
            if (used == pool.Count)
            {
                var go = new GameObject("Guide Line");
                go.transform.SetParent(transform, false);
                var created = go.AddComponent<LineRenderer>();
                created.useWorldSpace = true;
                created.sharedMaterial = lineMaterial;
                created.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                created.receiveShadows = false;
                created.numCapVertices = 2;
                created.numCornerVertices = 2;
                pool.Add(created);
            }
            var lr = pool[used++];
            lr.enabled = true;
            lr.loop = false;
            lr.alignment = LineAlignment.View;   // NextFlat stellt auf flach um
            lr.numCapVertices = 2;
            lr.startColor = lr.endColor = color;
            lr.widthMultiplier = width;
            return lr;
        }

        void OnGUI()
        {
            if (CityBuilder.Game.UI.Panels.WorldLabels.Enabled)
            {
                // Neue UI: als UI-Toolkit-Beschriftung melden (Ebene in der GameHud), nicht selbst zeichnen
                if (Event.current.type != EventType.Layout) return;
                CityBuilder.Game.UI.Panels.WorldLabels.Begin(this);
                foreach (var (world, text, error) in labels)
                    CityBuilder.Game.UI.Panels.WorldLabels.Add(this, world, text, error ? CityBuilder.Game.UI.Panels.WorldLabelStyle.Error : CityBuilder.Game.UI.Panels.WorldLabelStyle.Normal,
                        error ? 28f : 0f);
                return;
            }
            if (labels.Count == 0 || tool == null || tool.Camera == null) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(6, 6, 3, 3),
                };
                labelStyle.normal.textColor = Color.white;
                errorStyle = new GUIStyle(labelStyle);
                errorStyle.normal.textColor = new Color(1f, 0.45f, 0.4f);
            }

            foreach (var (world, text, error) in labels)
            {
                Vector3 sp = tool.Camera.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;
                var style = error ? errorStyle : labelStyle;
                var size = style.CalcSize(new GUIContent(text));
                float y = Screen.height - sp.y - size.y * 0.5f + (error ? 28f : 0f);
                GUI.Box(new Rect(sp.x - size.x * 0.5f, y, size.x, size.y), text, style);
            }
        }
    }
}
