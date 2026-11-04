using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    public enum HudIconKind
    {
        Logo, Pause, Play, Fast, Faster, People, Briefcase, Info, Car, Noise, Air, Power, Water, Underground,
        Road, Highway, Zones, Terrain, TrafficLight, Bulldoze, Warning, Check, Pipe, Brush, Coins,
        /// <summary>G4 Dienste: Wappen/Schild mit Kreuz.</summary>
        Service,
        /// <summary>Fenster (Phase 2): Schließen, Kamera folgen, Zurück, Hilfe.</summary>
        Close, Follow, Back, Help,
        /// <summary>G6 Bodenwert: Haus mit Pfeil nach oben.</summary>
        LandValue,
        /// <summary>Welt: Globus (Weltkarte), Edelstein (Rohstoffe).</summary>
        Globe, Resource,
        /// <summary>UX: Mauszeiger (Auswählen-Modus).</summary>
        Cursor,
        /// <summary>ÖPNV: Bus von der Seite.</summary>
        Bus,
        /// <summary>Bahn: Zug von vorn auf dem Gleis.</summary>
        Train,
        /// <summary>Sicherheit: Flamme (Brände/Brandgefahr), Schild mit Stern (Kriminalität/Polizei).</summary>
        Flame, Shield,
        /// <summary>Wasserstraße &amp; Hafen: Frachtschiff von der Seite auf Wellen.</summary>
        Ship,
        /// <summary>Umwelt (Etappe 6): Laubbaum (Bäume pflanzen, Umweltfenster), Tropfen über Boden (Grundwasser).</summary>
        Tree, Groundwater,
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
                case HudIconKind.Flame: // Sicherheit: Flamme mit innerer Zunge
                    p.BeginPath(); p.MoveTo(P(12, 3)); p.BezierCurveTo(P(16, 8), P(19, 11), P(18, 15.5f));
                    p.BezierCurveTo(P(17, 19.5f), P(14.5f, 21), P(12, 21)); p.BezierCurveTo(P(9.5f, 21), P(7, 19.5f), P(6, 15.5f));
                    p.BezierCurveTo(P(5.5f, 12.5f), P(7.5f, 10), P(9, 8.5f)); p.BezierCurveTo(P(9.5f, 11), P(10.5f, 12), P(11.5f, 12.5f));
                    p.BezierCurveTo(P(11, 9), P(11, 6), P(12, 3)); p.ClosePath(); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(12, 21)); p.BezierCurveTo(P(10, 20), P(9.5f, 17.5f), P(12, 14.5f)); p.BezierCurveTo(P(14.5f, 17.5f), P(14, 20), P(12, 21)); p.Stroke();
                    break;
                case HudIconKind.Shield: // Sicherheit: Polizeischild mit Stern
                    Poly(true, 12, 3, 20, 6, 19, 14, 12, 21, 5, 14, 4, 6);
                    Poly(true, 12, 8, 13.2f, 10.8f, 16, 11, 13.8f, 12.9f, 14.5f, 15.6f, 12, 14.1f, 9.5f, 15.6f, 10.2f, 12.9f, 8, 11, 10.8f, 10.8f);
                    break;
                case HudIconKind.Tree: // Umwelt: Laubbaum (Krone aus drei Kreisen, Stamm)
                    Circle(12, 8, 4.5f); Circle(8, 12, 3.5f); Circle(16, 12, 3.5f);
                    Line(12, 14, 12, 21); Line(9, 21, 15, 21);
                    break;
                case HudIconKind.Groundwater: // Umwelt: Tropfen über Bodenschichten
                    p.BeginPath(); p.MoveTo(P(12, 3)); p.BezierCurveTo(P(15, 7), P(16.5f, 9.5f), P(16.5f, 11.5f));
                    p.BezierCurveTo(P(16.5f, 14), P(14.5f, 16), P(12, 16)); p.BezierCurveTo(P(9.5f, 16), P(7.5f, 14), P(7.5f, 11.5f));
                    p.BezierCurveTo(P(7.5f, 9.5f), P(9, 7), P(12, 3)); p.ClosePath(); p.Stroke();
                    Line(3, 19, 21, 19); Line(5, 22, 19, 22);
                    break;
                case HudIconKind.Bus: // ÖPNV
                    Poly(true, 3, 17, 3, 7, 21, 7, 21, 17);
                    Line(3, 12, 21, 12); Line(9, 7, 9, 12); Line(15, 7, 15, 12);
                    Circle(7.5f, 18, 1.8f); Circle(16.5f, 18, 1.8f);
                    break;
                case HudIconKind.Train: // Bahn
                    Poly(true, 6, 17, 6, 5, 18, 5, 18, 17);
                    Line(6, 11, 18, 11); Circle(9, 14, 1.2f); Circle(15, 14, 1.2f);
                    Line(8, 17, 5, 21); Line(16, 17, 19, 21); Line(4, 21, 20, 21);
                    break;
                case HudIconKind.Ship: // Wasserstraße
                    Poly(true, 3, 13, 21, 13, 18, 18, 6, 18);
                    Poly(true, 13, 13, 13, 8, 17, 8, 17, 13);
                    Line(15, 8, 15, 5); Line(6, 13, 6, 10); Line(9, 13, 9, 10); Line(6, 10, 11, 10); Line(11, 10, 11, 13);
                    Line(3, 21, 7, 20); Line(9, 21, 13, 20); Line(15, 21, 19, 20);
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
                case HudIconKind.Coins: // Münzstapel (Stadtkasse)
                    Poly(true, 5, 6, 19, 6, 19, 9, 5, 9); Poly(true, 5, 11, 19, 11, 19, 14, 5, 14); Poly(true, 5, 16, 19, 16, 19, 19, 5, 19);
                    break;
                case HudIconKind.Service: // Schild mit Kreuz (Dienste G4)
                    Poly(true, 12, 3, 20, 6, 19, 14, 12, 21, 5, 14, 4, 6); Line(12, 8, 12, 16); Line(8, 12, 16, 12);
                    break;
                case HudIconKind.Close:
                    Line(6, 6, 18, 18); Line(18, 6, 6, 18);
                    break;
                case HudIconKind.Follow: // Fadenkreuz
                    Circle(12, 12, 3); Line(12, 2, 12, 5); Line(12, 19, 12, 22); Line(2, 12, 5, 12); Line(19, 12, 22, 12);
                    break;
                case HudIconKind.Back:
                    Line(19, 12, 5, 12); Poly(false, 11, 6, 5, 12, 11, 18);
                    break;
                case HudIconKind.LandValue:
                    Poly(true, 3, 11, 12, 4, 21, 11, 21, 20, 3, 20); Line(12, 17.5f, 12, 11.5f); Poly(false, 9.5f, 14, 12, 11.5f, 14.5f, 14);
                    break;
                case HudIconKind.Globe:
                    Circle(12, 12, 9);
                    p.BeginPath(); p.MoveTo(P(12, 3)); p.BezierCurveTo(P(7.5f, 7), P(7.5f, 17), P(12, 21)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(12, 3)); p.BezierCurveTo(P(16.5f, 7), P(16.5f, 17), P(12, 21)); p.Stroke();
                    Line(3, 12, 21, 12);
                    break;
                case HudIconKind.Resource:
                    Poly(true, 7, 4, 17, 4, 21, 9, 12, 20, 3, 9); Line(3, 9, 21, 9); Poly(false, 9.5f, 4, 8, 9, 12, 20, 16, 9, 14.5f, 4);
                    break;
                case HudIconKind.Cursor: // Pfeil-Mauszeiger (Auswählen)
                    Poly(true, 6, 3, 6, 19, 10.5f, 15, 13.5f, 21.5f, 16.5f, 20.2f, 13.5f, 13.8f, 19, 13.5f);
                    break;
                case HudIconKind.Help:
                    Circle(12, 12, 9);
                    p.BeginPath(); p.MoveTo(P(9.3f, 9.5f)); p.BezierCurveTo(P(9.6f, 6.6f), P(14.8f, 6.6f), P(14.8f, 9.6f));
                    p.BezierCurveTo(P(14.8f, 11.6f), P(12, 11.8f), P(12, 14)); p.Stroke();
                    Line(12, 17, 12, 17.1f);
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
