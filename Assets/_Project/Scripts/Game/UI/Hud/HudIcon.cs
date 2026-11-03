using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    public enum HudIconKind
    {
        Logo, Pause, Play, Fast, Faster, People, Briefcase, Info, Car, Noise, Air, Power, Water, Underground,
        Road, Highway, Zones, Terrain, TrafficLight, Bulldoze, Warning, Check, Pipe, Brush,
    }

    /// <summary>
    /// Vektor-Icon (Painter2D, Strichzeichnungen im 24er-Raster wie im Entwurf) – keine Bilddateien nötig.
    /// Farbe = aufgelöste Textfarbe (<c>color</c> aus dem USS), Größe über die Klassen <c>cb-icon</c>/<c>--l</c>/<c>--s</c>.
    /// </summary>
    public sealed class HudIcon : VisualElement
    {
        HudIconKind kind;
        Color shownColor;
        Painter2D p;
        float scale;
        Vector2 offset;

        public HudIcon(HudIconKind kind)
        {
            this.kind = kind;
            AddToClassList("cb-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            // Farbwechsel (z. B. Knopf ausgewählt) neu zeichnen – generateVisualContent hängt nicht automatisch an color.
            schedule.Execute(() =>
            {
                if (resolvedStyle.color != shownColor) MarkDirtyRepaint();
            }).Every(100);
        }

        public HudIconKind Kind
        {
            get => kind;
            set { if (kind != value) { kind = value; MarkDirtyRepaint(); } }
        }

        Vector2 P(float x, float y) => offset + new Vector2(x, y) * scale;

        void Draw(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 1f || rect.height < 1f) return;
            shownColor = resolvedStyle.color;
            scale = Mathf.Min(rect.width, rect.height) / 24f;
            offset = new Vector2(rect.x + (rect.width - 24f * scale) * 0.5f, rect.y + (rect.height - 24f * scale) * 0.5f);

            p = mgc.painter2D;
            p.strokeColor = shownColor;
            p.fillColor = shownColor;
            p.lineWidth = Mathf.Max(1f, 2f * scale);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            switch (kind)
            {
                case HudIconKind.Logo:
                    Line(3, 21, 21, 21); Poly(false, 5, 21, 5, 10, 10, 7, 10, 21); Poly(false, 10, 21, 10, 4, 19, 8, 19, 21);
                    Line(14, 11, 16, 11); Line(14, 15, 16, 15);
                    break;
                case HudIconKind.Pause:
                    FillPoly(6, 5, 10, 5, 10, 19, 6, 19); FillPoly(14, 5, 18, 5, 18, 19, 14, 19);
                    break;
                case HudIconKind.Play:
                    FillPoly(8, 5, 19, 12, 8, 19);
                    break;
                case HudIconKind.Fast:
                    FillPoly(3, 5, 12, 12, 3, 19); FillPoly(12, 5, 21, 12, 12, 19);
                    break;
                case HudIconKind.Faster:
                    FillPoly(1, 6, 8.5f, 12, 1, 18); FillPoly(8.5f, 6, 16, 12, 8.5f, 18); FillPoly(16, 6, 23.5f, 12, 16, 18);
                    break;
                case HudIconKind.People:
                    Circle(9, 8, 3.5f);
                    p.BeginPath(); p.MoveTo(P(2.5f, 20)); p.BezierCurveTo(P(3.3f, 16.5f), P(5.9f, 14.5f), P(9, 14.5f));
                    p.BezierCurveTo(P(12.1f, 14.5f), P(14.7f, 16.5f), P(15.5f, 20)); p.Stroke();
                    Circle(17, 9, 2.5f);
                    p.BeginPath(); p.MoveTo(P(16, 14.5f)); p.BezierCurveTo(P(18.7f, 14.7f), P(20.7f, 16.5f), P(21.3f, 19.5f)); p.Stroke();
                    break;
                case HudIconKind.Briefcase:
                    Poly(true, 3, 8, 21, 8, 21, 20, 3, 20); Poly(false, 8, 8, 8, 5, 16, 5, 16, 8); Line(3, 13, 21, 13);
                    break;
                case HudIconKind.Info:
                    Circle(12, 12, 9); Line(12, 11, 12, 16.5f); Line(12, 7.5f, 12, 7.6f);
                    break;
                case HudIconKind.Car:
                    Poly(true, 5, 17, 6.5f, 10.5f, 8.4f, 9, 15.6f, 9, 17.5f, 10.5f, 19, 17);
                    Circle(8, 17, 2); Circle(16, 17, 2);
                    break;
                case HudIconKind.Noise:
                    Poly(true, 4, 10, 4, 14, 8, 14, 13, 18, 13, 6, 8, 10);
                    p.BeginPath(); p.MoveTo(P(16.5f, 9)); p.QuadraticCurveTo(P(19, 12), P(16.5f, 15)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(19, 6.5f)); p.BezierCurveTo(P(22.5f, 9.5f), P(22.5f, 14.5f), P(19, 17.5f)); p.Stroke();
                    break;
                case HudIconKind.Air:
                    p.BeginPath(); p.MoveTo(P(3, 9)); p.LineTo(P(14, 9)); p.Arc(P(14, 6), 3f * scale, 90f, 180f, ArcDirection.CounterClockwise); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(3, 15)); p.LineTo(P(18, 15)); p.Arc(P(18, 18), 3f * scale, -90f, 180f, ArcDirection.Clockwise); p.Stroke();
                    Line(3, 12, 10, 12);
                    break;
                case HudIconKind.Power:
                    Poly(true, 13, 2, 4, 14, 11, 14, 10, 22, 19, 10, 12, 10);
                    break;
                case HudIconKind.Water:
                    p.BeginPath(); p.MoveTo(P(12, 3)); p.BezierCurveTo(P(15.5f, 7.5f), P(18, 10.6f), P(18, 13.5f));
                    p.Arc(P(12, 13.5f), 6f * scale, 0f, 180f, ArcDirection.Clockwise);
                    p.BezierCurveTo(P(6, 10.6f), P(8.5f, 7.5f), P(12, 3)); p.ClosePath(); p.Stroke();
                    break;
                case HudIconKind.Underground:
                    Poly(true, 12, 3, 21, 8, 12, 13, 3, 8); Poly(false, 3, 12.5f, 12, 17.5f, 21, 12.5f); Poly(false, 3, 17, 12, 22, 21, 17);
                    break;
                case HudIconKind.Road:
                    Line(7, 21, 10, 3); Line(17, 21, 14, 3); Line(12, 6, 12, 8); Line(12, 11, 12, 13); Line(12, 16, 12, 18);
                    break;
                case HudIconKind.Highway:
                    Line(4, 21, 8, 3); Line(20, 21, 16, 3); Line(10.5f, 21, 11.3f, 3); Line(13.5f, 21, 12.7f, 3);
                    break;
                case HudIconKind.Zones:
                    Poly(true, 3, 3, 11, 3, 11, 11, 3, 11); Poly(true, 13, 3, 21, 3, 21, 11, 13, 11);
                    Poly(true, 3, 13, 11, 13, 11, 21, 3, 21); Poly(true, 13, 13, 21, 13, 21, 21, 13, 21);
                    break;
                case HudIconKind.Terrain:
                    Poly(true, 3, 20, 9, 10, 13, 16, 16, 12, 21, 20);
                    break;
                case HudIconKind.TrafficLight:
                    Poly(true, 8, 2, 16, 2, 16, 22, 8, 22); Circle(12, 7, 1.6f); Circle(12, 12, 1.6f); Circle(12, 17, 1.6f);
                    break;
                case HudIconKind.Bulldoze:
                    Line(4, 7, 20, 7); Line(10, 11, 10, 17); Line(14, 11, 14, 17);
                    Poly(false, 6, 7, 7, 20, 17, 20, 18, 7); Poly(false, 9, 7, 9, 4, 15, 4, 15, 7);
                    break;
                case HudIconKind.Warning:
                    Poly(true, 12, 3, 22, 20, 2, 20); Line(12, 10, 12, 14); Line(12, 17, 12, 17.1f);
                    break;
                case HudIconKind.Check:
                    Poly(false, 5, 12, 10, 17, 19, 7);
                    break;
                case HudIconKind.Pipe:
                    Poly(false, 3, 9, 15, 9, 15, 19); Poly(false, 3, 13, 11, 13, 11, 19); Line(3, 9, 3, 13); Line(11, 19, 15, 19);
                    break;
                case HudIconKind.Brush:
                    Circle(12, 12, 8); Circle(12, 12, 3);
                    break;
            }
        }

        void Line(float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(P(x0, y0));
            p.LineTo(P(x1, y1));
            p.Stroke();
        }

        void Poly(bool closed, params float[] xy)
        {
            p.BeginPath();
            p.MoveTo(P(xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
            if (closed) p.ClosePath();
            p.Stroke();
        }

        void FillPoly(params float[] xy)
        {
            p.BeginPath();
            p.MoveTo(P(xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
            p.ClosePath();
            p.Fill();
        }

        void Circle(float x, float y, float r)
        {
            p.BeginPath();
            p.Arc(P(x, y), r * scale, 0f, 360f);
            p.Stroke();
        }
    }
}
