using System.Collections.Generic;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Bauhilfen fürs Straßen-Werkzeug: Verlängerungs- und Lotlinien der angeschlossenen Straße,
    /// Winkelbögen, Kurvengriffe sowie Beschriftungen für Länge und Winkel.
    /// Reine Anzeige – liest nur <see cref="RoadTool.Guide"/>.
    /// </summary>
    public sealed class RoadGuidesView : MonoBehaviour
    {
        const float LineHeight = 0.3f;
        const int ArcSegments = 24;

        static readonly Color GuideColor = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color ArcColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        static readonly Color HandleColor = new Color(0.4f, 0.9f, 1f, 0.8f);

        RoadTool tool;
        Material lineMaterial;
        readonly List<LineRenderer> pool = new List<LineRenderer>();
        readonly List<(Vector3 world, string text)> labels = new List<(Vector3, string)>();
        int used;
        GUIStyle labelStyle;

        public void Init(RoadTool roadTool, Material baseMaterial)
        {
            tool = roadTool;
            // Weiße Kopie, Farbe kommt pro Linie über die Vertexfarben.
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

            if (tool != null && tool.Guide.Active && tool.Camera != null)
                Draw(tool.Guide);

            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        void Draw(RoadTool.GuideState g)
        {
            float camDist = Vector3.Distance(tool.Camera.transform.position, SimConvert.ToWorld(g.End));
            float width = Mathf.Max(0.15f, camDist * 0.0025f);
            float guideLength = Mathf.Max(80f, g.Length * 1.5f);
            float arcRadius = Mathf.Clamp(g.Length * 0.3f, g.Width * 1.2f, camDist * 0.08f + g.Width);

            // Verlängerung + Lot der Straße am Start (oder Nord/Ost-Achsen, wenn frei begonnen)
            if (g.HasStartReference)
            {
                Line(g.Start, g.Start - g.StartReference * guideLength, GuideColor, width);
                var perp = new SimVector2(-g.StartReference.Y, g.StartReference.X);
                Line(g.Start - perp * guideLength * 0.5f, g.Start + perp * guideLength * 0.5f, GuideColor, width);

                Arc(g.Start, g.StartReference, g.StartDirection, arcRadius, width * 1.4f);
                labels.Add((ArcLabelPos(g.Start, g.StartReference, g.StartDirection, arcRadius), $"{g.StartAngle:0}°"));
            }
            else
            {
                Line(g.Start - SimVector2.UnitY * guideLength * 0.5f, g.Start + SimVector2.UnitY * guideLength * 0.5f, GuideColor, width);
                Line(g.Start - SimVector2.UnitX * guideLength * 0.5f, g.Start + SimVector2.UnitX * guideLength * 0.5f, GuideColor, width);

                Arc(g.Start, SimVector2.UnitY, g.StartDirection, arcRadius, width * 1.4f);
                labels.Add((ArcLabelPos(g.Start, SimVector2.UnitY, g.StartDirection, arcRadius), $"{g.Bearing:0}° {Compass(g.Bearing)}"));
            }

            // Anschluss am Ende
            if (g.HasEndReference)
            {
                Line(g.End, g.End - g.EndReference * guideLength * 0.5f, GuideColor, width);
                Arc(g.End, g.EndReference, g.EndDirection, arcRadius, width * 1.4f);
                labels.Add((ArcLabelPos(g.End, g.EndReference, g.EndDirection, arcRadius), $"{g.EndAngle:0}°"));
            }

            // Kurvengriffe
            if (g.IsCurve && !g.PlacingControl)
            {
                Line(g.Start, g.Control, HandleColor, width);
                Line(g.Control, g.End, HandleColor, width);
            }

            // Länge an der Straßenmitte
            if (g.Length > 0.5f)
            {
                var mid = Bezier.Evaluate(g.Start, g.Control, g.End, 0.5f);
                string text = g.PlacingControl ? $"{g.Length:0} m (Richtung)" : $"{g.Length:0} m";
                labels.Add((SimConvert.ToWorld(mid, LineHeight), text));
            }
        }

        static string Compass(float bearing)
        {
            string[] names = { "N", "NO", "O", "SO", "S", "SW", "W", "NW" };
            return names[Mathf.RoundToInt(bearing / 45f) % 8];
        }

        static Vector3 ArcLabelPos(SimVector2 center, SimVector2 from, SimVector2 to, float radius)
        {
            float half = RoadGeometry.SignedAngle(from, to) * 0.5f;
            var dir = RoadGeometry.Rotate(from, half);
            return SimConvert.ToWorld(center + dir * radius * 1.35f, LineHeight);
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
                pool.Add(created);
            }
            var lr = pool[used++];
            lr.enabled = true;
            lr.startColor = lr.endColor = color;
            lr.widthMultiplier = width;
            return lr;
        }

        void OnGUI()
        {
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
            }

            foreach (var (world, text) in labels)
            {
                Vector3 sp = tool.Camera.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;
                var size = labelStyle.CalcSize(new GUIContent(text));
                GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y * 0.5f, size.x, size.y), text, labelStyle);
            }
        }
    }
}
