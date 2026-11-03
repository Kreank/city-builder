using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.Zoning
{
    /// <summary>
    /// Eigene Assets: zeichnet Industriegrundstücke mit Landwirtschafts-Firma (G5, Rezept „farm“, <see cref="IndustryForm.Agriculture"/>)
    /// als Hof bzw. Felder aus <c>Models/Agriculture</c> statt als Industriegebäude. Je Grundstück fest aus der Gebäude-Id:
    /// Hofstelle, Windmühle, Weide, Grünland mit Kühen, Obstgarten, Weinberg, Gewächshaus oder Acker (Weizen, Mais, Raps,
    /// Kartoffeln, Gemüse). Äcker folgen der Jahreszeit aus <c>world.Climate</c> (gepflügt → Aufwuchs → reif → abgeerntet
    /// mit Heuballen), dazu Traktoren in der Feldarbeitszeit. Das Rezept kennt nur Getreide + Vieh – Obst/Wein/Gemüse sind
    /// optische Spielarten derselben Firma. Liest nur; <see cref="BuildingView"/> lässt solche Grundstücke aus (<see cref="Handles"/>).
    /// </summary>
    public sealed class FarmView : MonoBehaviour
    {
        /// <summary>Felder liegen auf der Grundstücksplatte des BuildingView (Oberkante 0,22 m).</summary>
        const float GroundLift = 0.23f;

        SimulationRunner runner;
        PartsRenderer parts, sails;
        readonly Dictionary<int, float> heights = new Dictionary<int, float>();
        readonly List<(ModelParts sails, Matrix4x4 at, float phase)> windmills = new List<(ModelParts, Matrix4x4, float)>();
        int builtCity = -1, builtMonth = -1, builtFirms = -1;
        Camera cam;

        public void Init(SimulationRunner r)
        {
            runner = r;
            r.WorldReplaced += () => { builtCity = builtMonth = builtFirms = -1; heights.Clear(); }; // Speichern & Laden
        }

        static OwnModelCatalog Catalog => OwnModelCatalog.Instance;

        /// <summary>Gibt es eigene Agrar-Modelle? Sonst bleibt alles beim Industriegebäude.</summary>
        public static bool Available
        {
            get
            {
                var c = Catalog;
                return c != null && c.Palette != null && c.Find(OwnModelRoles.Agriculture, "field") != null;
            }
        }

        /// <summary>Ist das Gebäude ein Bauernhof (Firma mit Landwirtschafts-Rezept) und zeichnet ihn diese View?</summary>
        public bool Handles(Building b)
        {
            if (b == null || b.Zone != ZoneType.Industry || runner == null || runner.World?.Goods == null || !Available) return false;
            var firm = runner.World.Goods.FirmAt(b.Id);
            return firm != null && firm.Form == IndustryForm.Agriculture;
        }

        public float VisualHeight(Building b) => b != null && heights.TryGetValue(b.Id, out float h) ? h : 0.5f;

        void LateUpdate()
        {
            if (runner == null || runner.World == null || !Available) return;
            if (parts == null)
            {
                parts = new PartsRenderer(Catalog.Palette, 256f, 1600f);
                sails = new PartsRenderer(Catalog.Palette) { Shadows = UnityEngine.Rendering.ShadowCastingMode.Off };
            }
            if (cam == null) cam = Camera.main;
            var w = runner.World;
            int month = FarmMonth(w);
            int firms = w.Goods != null ? w.Goods.Firms.Count : 0;
            if (w.City.Version != builtCity || month != builtMonth || firms != builtFirms)
            {
                builtCity = w.City.Version;
                builtMonth = month;
                builtFirms = firms;
                Rebuild(w, month);
            }
            parts.Render(cam);
            if (windmills.Count > 0)
            {
                sails.Clear();
                float t = Time.time * 0.6f * Mathf.Max(0.2f, runner.AnimationTimeScale);
                foreach (var (s, at, phase) in windmills)
                    sails.Add(s, s.PoseAt(at, Quaternion.Euler(0f, 0f, (t + phase) * Mathf.Rad2Deg)));
                sails.Render(cam);
            }
        }

        /// <summary>Monat 0–11 der Feldarbeit (Südhalbkugel um 6 Monate versetzt).</summary>
        static int FarmMonth(SimulationWorld w)
        {
            int month = w.Clock.DayOfYear / 30;
            if (w.Climate != null && w.Climate.SouthernHemisphere) month = (month + 6) % 12;
            return Mathf.Clamp(month, 0, 11);
        }

        // ------------------------------------------------------------ Aufbau

        enum Kind { Field, Farmstead, Windmill, Pasture, Meadow, Orchard, Vineyard, Greenhouse }

        void Rebuild(SimulationWorld w, int month)
        {
            parts.Clear();
            heights.Clear();
            windmills.Clear();
            foreach (var b in w.City.Buildings)
            {
                if (!Handles(b)) continue;
                heights[b.Id] = Place(b, month);
            }
        }

        static uint Hash(int id, int salt)
        {
            unchecked
            {
                uint h = (uint)(id * 73856093 ^ salt * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return h;
            }
        }

        static float Rand01(int id, int salt) => (Hash(id, salt) & 0xFFFF) / 65535f;

        static Kind KindOf(Building b)
        {
            float w = b.Width, d = b.Depth;
            if (Mathf.Min(w, d) < 14f) return Has("greenhouse") ? Kind.Greenhouse : Kind.Field;
            int r = (int)(Hash(b.Id, 1) % 100);
            if (r < 20 && Has("farmstead")) return Kind.Farmstead;
            if (r < 24 && Has("windmill")) return Kind.Windmill;
            if (r < 36 && Has("pasture")) return Kind.Pasture;
            if (r < 44) return Kind.Meadow;
            if (r < 48 && Has("orchard")) return Kind.Orchard;
            if (r < 51 && Has("vineyard")) return Kind.Vineyard;
            if (r < 54 && Has("greenhouse")) return Kind.Greenhouse;
            return Kind.Field;
        }

        static bool Has(string use) => Catalog.Find(OwnModelRoles.Agriculture, use) != null;

        static string CropOf(Building b)
        {
            int r = (int)(Hash(b.Id, 2) % 100);
            return r < 45 ? "wheat" : r < 62 ? "corn" : r < 76 ? "rapeseed" : r < 88 ? "potato" : "vegetables";
        }

        /// <summary>Feld-Modell der Feldfrucht im Monat (gepflügt/Aufwuchs/reif/abgeerntet).</summary>
        static OwnModelCatalog.Entry FieldFor(string crop, int month, out bool harvested, out bool work)
        {
            harvested = false;
            work = month >= 2 && month <= 9; // März–Oktober: Traktoren unterwegs
            string tag;
            switch (crop)
            {
                case "wheat":
                    tag = month == 2 ? "plowed" : month <= 4 && month >= 3 ? "meadow" : month <= 6 && month >= 5 ? "wheat" : month == 7 || month == 8 ? "harvested" : "plowed";
                    harvested = tag == "harvested";
                    break;
                case "corn": tag = month >= 4 && month <= 8 ? "corn" : "plowed"; break;
                case "rapeseed": tag = month >= 2 && month <= 5 ? "rapeseed" : month >= 8 || month <= 1 ? "meadow" : "plowed"; break;
                default: tag = month >= 4 && month <= 8 ? crop : "plowed"; break;
            }
            // „harvested“ gehört zum Weizen (Field_Wheat_Harvested trägt die Schlagwörter wheat + harvested)
            var e = tag == "harvested" ? Catalog.Find(OwnModelRoles.Agriculture, "field", "harvested")
                : tag == "wheat" ? FindExact("wheat") : Catalog.Find(OwnModelRoles.Agriculture, "field", tag);
            return e ?? Catalog.Find(OwnModelRoles.Agriculture, "field", "plowed") ?? Catalog.Find(OwnModelRoles.Agriculture, "field");
        }

        /// <summary>Feld mit genau diesem Schlagwort und ohne „harvested“ (Field_Wheat statt Field_Wheat_Harvested).</summary>
        static OwnModelCatalog.Entry FindExact(string tag)
        {
            foreach (var e in Catalog.WithRole(OwnModelRoles.Agriculture))
                if (e.Use == "field" && e.HasTag(tag) && !e.HasTag("harvested")) return e;
            return null;
        }

        float Place(Building b, int month)
        {
            float w = b.Width * 0.97f, d = b.Depth * 0.97f;
            var look = Quaternion.LookRotation(new Vector3(b.Front.X, 0f, b.Front.Y));
            var basePos = SimConvert.ToWorld(b.Position, b.Height + GroundLift);
            var side = look * Vector3.right;
            var fwd = look * Vector3.forward;
            if (!b.IsConstructed)
            {
                Tile(Catalog.Find(OwnModelRoles.Agriculture, "field", "plowed") ?? Catalog.Find(OwnModelRoles.Agriculture, "field"), basePos, look, w, d);
                return 0.5f;
            }

            var kind = KindOf(b);
            switch (kind)
            {
                case Kind.Farmstead:
                    return Fit(Catalog.Find(OwnModelRoles.Agriculture, "farmstead"), basePos, look, w, d, 1.2f);
                case Kind.Windmill:
                {
                    Tile(Catalog.Find(OwnModelRoles.Agriculture, "field", "meadow") ?? FieldFor("wheat", month, out _, out _), basePos, look, w, d);
                    var mill = Catalog.Find(OwnModelRoles.Agriculture, "windmill");
                    var body = ModelParts.Of(mill.Model, "Sails");
                    float s = Mathf.Min(1f, Mathf.Min(w, d) * 0.6f / Mathf.Max(1f, Mathf.Max(body.Size.x, body.Size.z)));
                    var at = Matrix4x4.TRS(basePos - side * (w * 0.15f), look, Vector3.one * s);
                    parts.Add(body, at);
                    var moving = ModelParts.Moving(mill.Model, "Sails");
                    if (moving != null) windmills.Add((moving, at, Rand01(b.Id, 9) * 6.28f));
                    return body.Size.y * s;
                }
                case Kind.Pasture:
                    Tile(Catalog.Find(OwnModelRoles.Agriculture, "pasture"), basePos, look, w, d);
                    return 2f;
                case Kind.Meadow:
                    Tile(Catalog.Find(OwnModelRoles.Agriculture, "field", "meadow") ?? Catalog.Find(OwnModelRoles.Agriculture, "pasture"), basePos, look, w, d);
                    if (month >= 3 && month <= 9) Scatter(b, Catalog.Find(OwnModelRoles.Agriculture, "animal"), basePos, side, fwd, w, d, 3, 20);
                    return 1.5f;
                case Kind.Orchard:
                    Tile(Catalog.Find(OwnModelRoles.Agriculture, "orchard"), basePos, look, w, d);
                    return 4f;
                case Kind.Vineyard:
                    Tile(Catalog.Find(OwnModelRoles.Agriculture, "vineyard"), basePos, look, w, d);
                    return 2f;
                case Kind.Greenhouse:
                    return Fit(Catalog.Find(OwnModelRoles.Agriculture, "greenhouse"), basePos, look, w, d, 1f, tile: true);
                default:
                {
                    var field = FieldFor(CropOf(b), month, out bool harvested, out bool work);
                    Tile(field, basePos, look, w, d);
                    if (harvested) Scatter(b, Catalog.Find(OwnModelRoles.Agriculture, "deco"), basePos, side, fwd, w, d, 2, 30);
                    if (work && Hash(b.Id, 3) % 4 == 0) Scatter(b, Catalog.Find(OwnModelRoles.Agriculture, "vehicle"), basePos, side, fwd, w, d, 1, 40);
                    return 1.2f;
                }
            }
        }

        /// <summary>Flache Kachel (32 × 32 m) auf das Grundstück gezogen; lange Grundstücke bekommen mehrere Kacheln.</summary>
        void Tile(OwnModelCatalog.Entry e, Vector3 basePos, Quaternion look, float w, float d)
        {
            if (e == null) return;
            var model = ModelParts.Of(e.Model);
            if (model.Parts.Count == 0) return;
            float sx = Mathf.Max(0.1f, model.Size.x), sz = Mathf.Max(0.1f, model.Size.z);
            int nw = Mathf.Max(1, Mathf.RoundToInt(w / Mathf.Max(d, 1f))), nd = Mathf.Max(1, Mathf.RoundToInt(d / Mathf.Max(w, 1f)));
            float tw = w / nw, td = d / nd;
            var side = look * Vector3.right;
            var fwd = look * Vector3.forward;
            for (int i = 0; i < nw; i++)
            for (int j = 0; j < nd; j++)
            {
                var p = basePos + side * (-w * 0.5f + tw * (i + 0.5f)) + fwd * (-d * 0.5f + td * (j + 0.5f));
                float x = tw / sx, z = td / sz;
                parts.Add(model, Matrix4x4.TRS(p, look, new Vector3(x, Mathf.Min(x, z), z)));
            }
        }

        /// <summary>Gebäude-Modell gleichmäßig eingepasst (um 90° gedreht, wenn das besser passt).</summary>
        float Fit(OwnModelCatalog.Entry e, Vector3 basePos, Quaternion look, float w, float d, float maxScale, bool tile = false)
        {
            if (e == null) return 0.5f;
            var model = ModelParts.Of(e.Model);
            if (model.Parts.Count == 0) return 0.5f;
            float s0 = Mathf.Min(w / model.Size.x, d / model.Size.z);
            float s90 = Mathf.Min(w / model.Size.z, d / model.Size.x);
            bool turn = s90 > s0 * 1.1f;
            float s = Mathf.Min(turn ? s90 : s0, maxScale);
            var rot = look * (turn ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
            parts.Add(model, Matrix4x4.TRS(basePos, rot, Vector3.one * s));
            // Gewächshäuser: zweites daneben, wenn Platz ist
            if (tile)
            {
                float used = (turn ? model.Size.z : model.Size.x) * s;
                if (w > used * 2.05f)
                {
                    var side = look * Vector3.right;
                    parts.Add(model, Matrix4x4.TRS(basePos + side * (used * 0.55f), rot, Vector3.one * s));
                    parts.Add(model, Matrix4x4.TRS(basePos - side * (used * 0.55f), rot, Vector3.one * s));
                }
            }
            return model.Size.y * s;
        }

        /// <summary>Einzelne Deko-Modelle (Kühe, Heuballen, Traktor) fest aus der Id verteilt.</summary>
        void Scatter(Building b, OwnModelCatalog.Entry e, Vector3 basePos, Vector3 side, Vector3 fwd, float w, float d, int count, int salt)
        {
            if (e == null) return;
            var model = ModelParts.Of(e.Model);
            for (int i = 0; i < count; i++)
            {
                float u = Rand01(b.Id, salt + i * 3) - 0.5f, v = Rand01(b.Id, salt + i * 3 + 1) - 0.5f;
                float yaw = Rand01(b.Id, salt + i * 3 + 2) * 360f;
                var p = basePos + side * (u * w * 0.7f) + fwd * (v * d * 0.7f) + Vector3.up * 0.1f;
                parts.Add(model, Matrix4x4.TRS(p, Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, yaw, 0f), Vector3.one));
            }
        }
    }
}
