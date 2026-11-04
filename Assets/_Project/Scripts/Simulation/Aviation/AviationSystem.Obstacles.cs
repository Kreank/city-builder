using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Aviation
{
    /// <summary>
    /// Geschätzte Bauhöhen für die Hindernisprüfung (die Simulation kennt nur die Bodenhöhe der Gebäude; die Werte folgen
    /// den Modellen/Platzhaltern: +30 % je Ausbaustufe wie <c>BuildingView</c>).
    /// </summary>
    public static class ObstacleHeights
    {
        public static float Zone(ZoneType zone, int widthCells, int level)
        {
            float h;
            switch (zone)
            {
                case ZoneType.ResidentialLow: h = 9f; break;
                case ZoneType.ResidentialMedium: h = 15f; break;
                case ZoneType.ResidentialHigh: h = 30f; break;
                case ZoneType.Mixed: h = 18f; break;
                case ZoneType.Retail: h = 8f; break;
                case ZoneType.Office: h = 24f; break;
                case ZoneType.Industry: h = 14f; break;
                default: h = 10f; break;
            }
            if (widthCells >= 4 && zone != ZoneType.Industry) h *= 1.3f;
            return h * (1f + 0.3f * (Math.Max(1, level) - 1));
        }

        public static float Building(Building b) => Zone(b.Zone, b.WidthCells, b.Level);

        public static float Service(string id)
        {
            switch (id)
            {
                case "media_tv": return 180f;
                case "media_radio": return 90f;
                case "landmark_church": return 45f;
                case "leisure_ferris_wheel": return 55f;
                case "leisure_stadium": return 35f;
                case "incinerator": return 60f;
                case "hospital": return 30f;
                case "landmark_castle": return 30f;
                case "landmark_obelisk": return 25f;
                case "school_high": return 16f;
                default: return 12f;
            }
        }

        public static float Utility(string id)
        {
            switch (id)
            {
                case "power_wind": return 140f;
                case "power_offshore_wind": return 160f;
                case "power_nuclear": return 150f;
                case "power_coal": return 110f;
                case "power_gas": return 55f;
                case "water_tower": return 32f;
                case "power_biogas": return 18f;
                default: return 12f;
            }
        }

        /// <summary>Masthöhe je Leitungsart (unterirdische Leitungen: 0).</summary>
        public static float Pole(string lineTypeId)
        {
            switch (lineTypeId)
            {
                case "power_hv": return 45f;
                case "power_overhead": return 12f;
                default: return 0f;
            }
        }
    }

    /// <summary>
    /// Hindernisfreiheit (vereinfachte Hindernisbegrenzungsflächen nach ICAO Annex 14): je Bahnende eine <b>Anflugfläche</b>
    /// (beginnt 60 m hinter dem Bahnende, Breite + 15 % Aufweitung je Seite, Neigung 4 % bzw. 2 %), seitlich der Bahn die
    /// <b>Übergangsfläche</b> (1 : 7 ab dem Streifenrand bis 45 m) und die <b>innere Horizontalfläche</b> (45 m über dem Platz).
    /// Bestehende Hindernisse (Gebäude, Dienste, Versorger, Strommasten, Gelände) senken die Kapazität; in der Kernzone
    /// (erste 1 200 m) sperren sie die Einflugschneise. Neue Hochbauten, die eine Anflug-/Übergangsfläche durchstoßen, sind
    /// ungültig (Hooks bei Diensten, Versorgern und gesetzten Gebäuden); Zonen wachsen nicht direkt vor der Schwelle.
    /// </summary>
    public sealed partial class AviationSystem
    {
        /// <summary>
        /// Erlaubte Höhe (über dem Flugplatz) am Punkt p: kleinster Wert der Flächen, die ihn überdecken; +∞ = keine Fläche.
        /// <paramref name="withHorizontal"/> = innere Horizontalfläche mitprüfen.
        /// </summary>
        static float AllowedAt(AirportType type, in AirportLayout l, Vector2 p, bool withHorizontal, out ObstacleSurface surface, out float distance)
        {
            surface = ObstacleSurface.InnerHorizontal;
            distance = 0f;
            float best = float.PositiveInfinity;
            var perp = l.Normal;
            for (int e = 0; e < 2; e++)
            {
                var outward = e == 0 ? -l.Dir : l.Dir;
                var start = (e == 0 ? l.EndA : l.EndB) + outward * AviationRules.ApproachStart;
                var rel = p - start;
                float d = Vector2.Dot(rel, outward);
                if (d < 0f || d > type.ApproachLength) continue;
                float lat = MathF.Abs(Vector2.Dot(rel, perp));
                if (lat > type.ApproachInnerHalfWidth + AviationRules.Divergence * d) continue;
                float allowed = type.ApproachSlope * d;
                if (allowed < best) { best = allowed; surface = e == 0 ? ObstacleSurface.ApproachA : ObstacleSurface.ApproachB; distance = d; }
            }
            // Übergangsfläche seitlich der Bahn (1 : 7 bis 45 m)
            float u = l.U(p), v = MathF.Abs(l.V(p));
            if (MathF.Abs(u) <= l.HalfLength + AviationRules.ApproachStart && v >= type.StripHalfWidth)
            {
                float allowed = (v - type.StripHalfWidth) / 7f;
                if (allowed < AviationRules.InnerHorizontalHeight && allowed < best) { best = allowed; surface = ObstacleSurface.Transitional; distance = 0f; }
            }
            if (withHorizontal && best > AviationRules.InnerHorizontalHeight)
            {
                float du = MathF.Max(0f, MathF.Abs(u) - l.HalfLength);
                float dv = MathF.Abs(l.V(p));
                if (du * du + dv * dv <= type.InnerHorizontalRadius * type.InnerHorizontalRadius)
                {
                    best = AviationRules.InnerHorizontalHeight;
                    surface = ObstacleSurface.InnerHorizontal;
                    distance = 0f;
                }
            }
            return best;
        }

        /// <summary>Kleinste erlaubte Höhe über dem Platz an der Mitte und vier Randpunkten eines Bauwerks.</summary>
        static float AllowedOver(AirportType type, in AirportLayout l, Vector2 c, float half, bool withHorizontal, out ObstacleSurface surface, out float distance)
        {
            float best = AllowedAt(type, l, c, withHorizontal, out surface, out distance);
            if (half <= 0.5f) return best;
            for (int i = 0; i < 4; i++)
            {
                var o = new Vector2(i < 2 ? (i == 0 ? half : -half) : 0f, i >= 2 ? (i == 2 ? half : -half) : 0f);
                float a = AllowedAt(type, l, c + o, withHorizontal, out var s, out var d);
                if (a < best) { best = a; surface = s; distance = d; }
            }
            return best;
        }

        // ------------------------------------------------------------ Bestand prüfen

        void ScanObstacles(AirportType type, Vector2 center, Vector2 dir, int side, float length, float elevation, int level, List<Obstacle> list)
        {
            list.Clear();
            var l = new AirportLayout(type, center, dir, side, length, Math.Max(1, level));
            var site = l.Site;
            void Test(ObstacleKind kind, string name, Vector2 p, float half, float ground, float height)
            {
                if (site.Contains(p, half)) return; // steht auf dem Gelände: weicht bzw. ist ein Platzfehler
                float allowed = AllowedOver(type, l, p, half, true, out var surface, out float dist);
                if (float.IsPositiveInfinity(allowed)) return;
                float top = ground + height - elevation;
                if (top <= allowed + 0.5f) return;
                list.Add(new Obstacle
                {
                    Kind = kind, Name = name, Position = p, Top = top, Allowed = allowed, Surface = surface, Distance = dist,
                    Core = (surface == ObstacleSurface.ApproachA || surface == ObstacleSurface.ApproachB) && dist <= AviationRules.CoreLength,
                });
            }
            var city = world.City;
            if (city != null)
                foreach (var b in city.Buildings)
                    Test(ObstacleKind.Building, HouseCatalog.NameOf(b), b.Position, MathF.Max(b.Width, b.Depth) * 0.5f, b.Height, ObstacleHeights.Building(b));
            if (world.Services != null)
                foreach (var f in world.Services.Facilities)
                    Test(ObstacleKind.Service, f.Type.DisplayName, f.Position, MathF.Max(f.Type.Width, f.Type.Depth) * 0.5f, f.Height, ObstacleHeights.Service(f.Type.Id));
            if (world.Utilities != null)
            {
                foreach (var f in world.Utilities.Facilities)
                    Test(ObstacleKind.Utility, f.Type.DisplayName, f.Position, MathF.Max(f.Type.Width, f.Type.Depth) * 0.5f, f.Height, ObstacleHeights.Utility(f.Type.Id));
                foreach (var line in world.Utilities.Lines)
                {
                    float h = ObstacleHeights.Pole(line.Type.Id);
                    if (h <= 0f) continue;
                    foreach (var p in line.Poles) Test(ObstacleKind.PowerLine, $"Mast ({line.Type.DisplayName})", p, 0f, terrain.HeightAt(p), h);
                }
            }
            // Gelände in den Anflugflächen (je 300-m-Abschnitt nur der höchste Punkt)
            for (int e = 0; e < 2; e++)
            {
                var outward = e == 0 ? -l.Dir : l.Dir;
                var start = (e == 0 ? l.EndA : l.EndB) + outward * AviationRules.ApproachStart;
                Obstacle bucket = null;
                int bucketIndex = -1;
                for (float d = 0f; d <= type.ApproachLength; d += AviationRules.TerrainProbeStep)
                {
                    float half = type.ApproachInnerHalfWidth + AviationRules.Divergence * d;
                    for (int k = 0; k < AviationRules.TerrainProbeAcross; k++)
                    {
                        float f = AviationRules.TerrainProbeAcross == 1 ? 0f : k / (float)(AviationRules.TerrainProbeAcross - 1) * 2f - 1f;
                        var p = start + outward * d + l.Normal * (half * f * 0.95f);
                        if (!world.Contains(p)) continue;
                        float top = terrain.HeightAt(p) - elevation;
                        float allowed = type.ApproachSlope * d;
                        if (top <= allowed + 0.5f) continue;
                        int bi = (int)(d / 300f);
                        if (bi != bucketIndex) { bucketIndex = bi; bucket = null; }
                        if (bucket != null && bucket.Penetration >= top - allowed) continue;
                        if (bucket == null)
                        {
                            bucket = new Obstacle { Kind = ObstacleKind.Terrain, Name = "Gelände (Anhöhe)", Surface = e == 0 ? ObstacleSurface.ApproachA : ObstacleSurface.ApproachB };
                            list.Add(bucket);
                        }
                        bucket.Position = p; bucket.Top = top; bucket.Allowed = allowed; bucket.Distance = d;
                        bucket.Core = d <= AviationRules.CoreLength;
                    }
                }
            }
            // stabil sortieren: Kernzone zuerst, dann nach Durchstoßung
            list.Sort((a, b) =>
            {
                int c = b.Core.CompareTo(a.Core);
                if (c != 0) return c;
                c = b.Penetration.CompareTo(a.Penetration);
                if (c != 0) return c;
                c = a.Position.X.CompareTo(b.Position.X);
                return c != 0 ? c : a.Position.Y.CompareTo(b.Position.Y);
            });
        }

        /// <summary>Zustand je Bahnende und Kapazitätsfaktor aus der Hindernisliste.</summary>
        static void Evaluate(List<Obstacle> list, out CorridorState a, out CorridorState b, out float factor)
        {
            a = b = CorridorState.Clear;
            factor = 1f;
            int limitedA = 0, limitedB = 0, side = 0;
            foreach (var o in list)
            {
                switch (o.Surface)
                {
                    case ObstacleSurface.ApproachA:
                        if (o.Core) a = CorridorState.Closed; else limitedA++;
                        break;
                    case ObstacleSurface.ApproachB:
                        if (o.Core) b = CorridorState.Closed; else limitedB++;
                        break;
                    default: side++; break;
                }
            }
            if (a == CorridorState.Clear && limitedA > 0) a = CorridorState.Limited;
            if (b == CorridorState.Clear && limitedB > 0) b = CorridorState.Limited;
            if (a == CorridorState.Limited) factor *= MathF.Pow(AviationRules.LimitedFactor, Math.Min(3, limitedA));
            if (b == CorridorState.Limited) factor *= MathF.Pow(AviationRules.LimitedFactor, Math.Min(3, limitedB));
            factor *= MathF.Max(0.85f, MathF.Pow(AviationRules.InnerHorizontalFactor, side));
            factor = MathF.Max(AviationRules.MinObstacleFactor, factor);
        }

        int ObstacleSignatureNow()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (world.City?.Version ?? 0);
                h = h * 31 + (world.City?.Stats.Buildings ?? 0);
                h = h * 31 + (world.Services?.StructureVersion ?? 0);
                h = h * 31 + (world.Utilities?.StructureVersion ?? 0);
                h = h * 31 + terrain.Version;
                return h;
            }
        }

        /// <summary>Hindernisse neu prüfen, wenn sich Gebäude, Bauwerke oder Gelände geändert haben (höchstens stündlich, außer erzwungen).</summary>
        void EnsureObstacles(bool force)
        {
            if (airports.Count == 0) return;
            if (!force && world.Clock.Tick % Core.TimeModel.TicksPerHour != 0) return;
            int sig = ObstacleSignatureNow();
            foreach (var a in airports)
            {
                if (!force && a.ObstacleSignature == sig) continue;
                a.ObstacleSignature = sig;
                var before = (a.CorridorA, a.CorridorB);
                ScanObstacles(a.Type, a.Center, a.Dir, a.Side, a.RunwayLength, a.Height, a.Level, a.Obstacles);
                Evaluate(a.Obstacles, out var ca, out var cb, out float factor);
                a.CorridorA = ca; a.CorridorB = cb; a.ObstacleFactor = factor;
                if (before != (ca, cb)) ChooseDirections(a, world.Clock.Day);
                Version++;
            }
        }

        // ------------------------------------------------------------ Neubau prüfen (Hooks)

        /// <summary>
        /// Hinweis, wenn ein neues Bauwerk (Mitte, halbe Größe, Höhe über Boden) eine Anflug- oder Übergangsfläche durchstößt bzw.
        /// auf einem Flughafengelände steht; null = erlaubt. Die innere Horizontalfläche sperrt keine Neubauten (nur Kapazität).
        /// </summary>
        public string ObstacleNote(Vector2 p, float half, float height, string name)
        {
            foreach (var a in airports)
            {
                var l = a.Layout;
                if (a.Site.Contains(p, half)) return $"Flughafengelände ({a.Name})";
                float allowed = AllowedOver(a.Type, l, p, half, false, out var surface, out _);
                if (float.IsPositiveInfinity(allowed)) continue;
                float ground = terrain.HeightAt(p);
                float maxHere = allowed + a.Height - ground;
                if (height <= maxHere + 0.5f) continue;
                string where = surface == ObstacleSurface.Transitional ? "Übergangsfläche" : "Einflugschneise";
                return $"{where} {a.Name}: hier höchstens {MathF.Max(0f, maxHere):0} m hoch – {name} ~{height:0} m";
            }
            return null;
        }

        /// <summary>Höchste erlaubte Bauhöhe über dem Boden an einem Punkt (+∞ = keine Einschränkung) – für Vorschau/Inspektor.</summary>
        public float HeightLimitAt(Vector2 p)
        {
            float best = float.PositiveInfinity;
            foreach (var a in airports)
            {
                float allowed = AllowedAt(a.Type, a.Layout, p, false, out _, out _);
                if (float.IsPositiveInfinity(allowed)) continue;
                best = MathF.Min(best, allowed + a.Height - terrain.HeightAt(p));
            }
            return best;
        }

        string HousePlacementNote(HousePlan plan)
        {
            if (airports.Count == 0 || plan?.Type == null) return null;
            float h = ObstacleHeights.Zone(plan.Type.Zone, plan.Type.WidthCells, 1);
            return ObstacleNote(plan.Position, MathF.Max(plan.Type.Width, plan.Type.Depth) * 0.5f, h, plan.Type.DisplayName);
        }

        static string ServiceName(string id) => Services.ServiceCatalog.Get(id)?.DisplayName ?? id;
        static float ServiceHeight(string id) => ObstacleHeights.Service(id);
        static string UtilityName(string id) => Utilities.UtilityCatalog.TypeById(id)?.DisplayName ?? id;

        /// <summary>
        /// Grundstücks-Sperre (Zonen, Masten, gesetzte Gebäude): nicht auf dem Flughafengelände. Höhen in der Einflugschneise prüft
        /// für gesetzte Gebäude der genauere Hook (<see cref="HousePlacementNote"/>); Zonen (Altbestand) wachsen dort weiter und
        /// werden dann zum Hindernis.
        /// </summary>
        bool BlocksLot(Vector2 p)
        {
            foreach (var a in airports)
                if (a.Contains(p, ZoneGrid.CellSize * 0.5f)) return true;
            return false;
        }
    }
}
