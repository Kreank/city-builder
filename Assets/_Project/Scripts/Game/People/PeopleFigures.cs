using System.Collections.Generic;
using CityBuilder.Game.Assets;
using UnityEngine;

namespace CityBuilder.Game.People
{
    /// <summary>Altersgruppe für Größe, Proportionen, Kleidung und Gangart.</summary>
    public enum FigureAge : byte { Toddler, Child, Teen, Adult, Senior }

    /// <summary>
    /// Platzhalter-Figuren aus Grundkörpern (ohne Texturen): Quader mit Drehpunkt unten (Rumpf, Kopf, Haare) bzw. oben
    /// (Beine, Arme – schwingen um Hüfte/Schulter). Maße für 1,75 m, Kinder werden skaliert (Kopf relativ größer).
    /// Dazu Farbpaletten (Haut, Haare, Oberteil, Hose) – Auswahl deterministisch nach Bürger-Id.
    /// </summary>
    public static class PeopleFigures
    {
        public const float HipHeight = 0.86f;
        public const float TorsoHeight = 0.58f;
        public const float HeadHeight = 0.22f;
        /// <summary>Gesamthöhe der Platzhalter-Figur (Erwachsene) ≈ 1,75 m.</summary>
        public const float Height = HipHeight + TorsoHeight + 0.02f + HeadHeight + 0.07f;

        public static readonly Color[] Skin =
        {
            new Color(0.96f, 0.80f, 0.69f), new Color(0.89f, 0.70f, 0.56f), new Color(0.76f, 0.57f, 0.42f),
            new Color(0.58f, 0.40f, 0.28f), new Color(0.38f, 0.26f, 0.18f),
        };
        public static readonly Color[] Hair =
        {
            new Color(0.08f, 0.07f, 0.06f), new Color(0.25f, 0.16f, 0.09f), new Color(0.45f, 0.30f, 0.16f),
            new Color(0.82f, 0.68f, 0.40f), new Color(0.62f, 0.28f, 0.12f),
        };
        /// <summary>Haare der Senioren: grau bis weiß.</summary>
        public static readonly Color[] GreyHair = { new Color(0.62f, 0.62f, 0.62f), new Color(0.82f, 0.82f, 0.80f), new Color(0.93f, 0.93f, 0.92f) };
        public static readonly Color[] Shirt =
        {
            new Color(0.85f, 0.20f, 0.18f), new Color(0.20f, 0.42f, 0.78f), new Color(0.95f, 0.95f, 0.93f),
            new Color(0.16f, 0.16f, 0.18f), new Color(0.20f, 0.58f, 0.36f), new Color(0.95f, 0.75f, 0.20f),
            new Color(0.55f, 0.30f, 0.62f), new Color(0.95f, 0.55f, 0.20f), new Color(0.45f, 0.70f, 0.85f),
            new Color(0.72f, 0.72f, 0.70f), new Color(0.80f, 0.40f, 0.55f), new Color(0.30f, 0.35f, 0.45f),
        };
        /// <summary>Kinder: kräftige Farben.</summary>
        public static readonly Color[] KidShirt =
        {
            new Color(0.98f, 0.30f, 0.25f), new Color(0.20f, 0.65f, 0.95f), new Color(0.98f, 0.85f, 0.15f),
            new Color(0.35f, 0.80f, 0.35f), new Color(0.98f, 0.50f, 0.75f), new Color(0.65f, 0.40f, 0.95f),
        };
        /// <summary>Senioren: gedeckte Farben.</summary>
        public static readonly Color[] SeniorShirt =
        {
            new Color(0.62f, 0.55f, 0.45f), new Color(0.40f, 0.42f, 0.48f), new Color(0.22f, 0.26f, 0.40f),
            new Color(0.45f, 0.32f, 0.24f), new Color(0.45f, 0.48f, 0.34f), new Color(0.70f, 0.68f, 0.62f),
        };
        public static readonly Color[] Trousers =
        {
            new Color(0.18f, 0.26f, 0.45f), new Color(0.10f, 0.10f, 0.12f), new Color(0.40f, 0.40f, 0.42f),
            new Color(0.70f, 0.62f, 0.48f), new Color(0.35f, 0.24f, 0.16f), new Color(0.22f, 0.30f, 0.22f),
        };

        public static FigureAge AgeOf(int age) =>
            age < 6 ? FigureAge.Toddler : age < 12 ? FigureAge.Child : age < 18 ? FigureAge.Teen : age < 65 ? FigureAge.Adult : FigureAge.Senior;

        /// <summary>Gesamtskalierung je Altersgruppe (1 = 1,75 m).</summary>
        public static float ScaleOf(FigureAge a)
        {
            switch (a)
            {
                case FigureAge.Toddler: return 0.55f;
                case FigureAge.Child: return 0.74f;
                case FigureAge.Teen: return 0.93f;
                case FigureAge.Senior: return 0.96f;
                default: return 1f;
            }
        }

        /// <summary>Schrittfrequenz (Hz, Echtzeit) und Schwung-Amplitude (Grad) der Beine.</summary>
        public static void GaitOf(FigureAge a, out float stepHz, out float legSwing)
        {
            switch (a)
            {
                case FigureAge.Toddler: stepHz = 2.6f; legSwing = 22f; break;
                case FigureAge.Child: stepHz = 2.3f; legSwing = 28f; break;
                case FigureAge.Senior: stepHz = 1.4f; legSwing = 15f; break;
                default: stepHz = 1.8f; legSwing = 25f; break;
            }
        }

        // ------------------------------------------------------------ Meshes

        /// <summary>Einheitsquader x,z ∈ [−0,5; 0,5], y ∈ [0; 1] (Drehpunkt unten) bzw. y ∈ [−1; 0] (Drehpunkt oben).</summary>
        public static Mesh Box(bool pivotTop, string name)
        {
            float y0 = pivotTop ? -1f : 0f, y1 = pivotTop ? 0f : 1f;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            void Face(Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
            const float h = 0.5f;
            Face(Vector3.forward, new Vector3(h, y0, h), new Vector3(h, y1, h), new Vector3(-h, y1, h), new Vector3(-h, y0, h));
            Face(Vector3.back, new Vector3(-h, y0, -h), new Vector3(-h, y1, -h), new Vector3(h, y1, -h), new Vector3(h, y0, -h));
            Face(Vector3.right, new Vector3(h, y0, -h), new Vector3(h, y1, -h), new Vector3(h, y1, h), new Vector3(h, y0, h));
            Face(Vector3.left, new Vector3(-h, y0, h), new Vector3(-h, y1, h), new Vector3(-h, y1, -h), new Vector3(-h, y0, -h));
            Face(Vector3.up, new Vector3(-h, y1, -h), new Vector3(-h, y1, h), new Vector3(h, y1, h), new Vector3(h, y1, -h));
            Face(Vector3.down, new Vector3(-h, y0, h), new Vector3(-h, y0, -h), new Vector3(h, y0, -h), new Vector3(h, y0, h));
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>Rolle eines Teils eines eigenen Personen-Modells (Kindobjekt-Name).</summary>
    public enum PersonPartRole : byte { Body, LegL, LegR, ArmL, ArmR }

    /// <summary>
    /// Eigenes Blender-Modell einer Person (<see cref="OwnModelCatalog.People"/>), zerlegt in instanzierbare Teile.
    /// Konvention: Ursprung = Mitte zwischen den Füßen, Front +Z, 1 Einheit = 1 m; optionale Kindobjekte
    /// <c>LegL</c>, <c>LegR</c>, <c>ArmL</c>, <c>ArmR</c> schwingen um ihren eigenen Ursprung (Hüfte/Schulter).
    /// </summary>
    public sealed class PersonModel
    {
        public sealed class Part
        {
            public ModelEntry Entry;
            public Matrix4x4 Local;
            public PersonPartRole Role;
            public Vector3 Pivot;
        }

        public string Id;
        public FigureAge Group;
        public readonly List<Part> Parts = new List<Part>();
        /// <summary>Korrektur grober Einheitenfehler (Modell ≠ ~1,75 m).</summary>
        public float Scale = 1f;

        public static PersonModel From(OwnModelCatalog.Entry entry, Material palette)
        {
            if (entry == null || entry.Model == null) return null;
            var root = entry.Model.transform;
            var toRoot = root.worldToLocalMatrix;
            var model = new PersonModel { Id = entry.Id, Group = GroupOf(entry.Id) };
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var mf in entry.Model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                var mats = mr != null ? mr.sharedMaterials : new Material[0];
                if (palette != null) { mats = new Material[Mathf.Max(1, mats.Length)]; for (int i = 0; i < mats.Length; i++) mats[i] = palette; }
                var role = PersonPartRole.Body;
                Transform limb = null;
                for (var t = mf.transform; t != null && t != root.parent; t = t.parent)
                {
                    var r = RoleOf(t.name);
                    if (r != PersonPartRole.Body) { role = r; limb = t; break; }
                }
                var local = toRoot * mf.transform.localToWorldMatrix;
                var part = new Part
                {
                    Entry = new ModelEntry { Name = entry.Id + "/" + mf.name, Mesh = mf.sharedMesh, Materials = mats },
                    Local = local,
                    Role = role,
                    Pivot = limb != null ? (Vector3)(toRoot * limb.localToWorldMatrix).GetColumn(3) : Vector3.zero,
                };
                model.Parts.Add(part);
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float y = local.MultiplyPoint3x4(c).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }
            if (model.Parts.Count == 0) return null;
            float height = maxY - minY;
            float expected = PeopleFigures.Height * PeopleFigures.ScaleOf(model.Group);
            if (height > 1e-3f && (height < expected * 0.33f || height > expected * 3f)) model.Scale = expected / height;
            return model;
        }

        /// <summary>Gruppe nach Dateinamen: <c>Child*</c>/<c>Kid*</c>, <c>Toddler*</c>, <c>Teen*</c>, <c>Senior*</c>, sonst Erwachsene.</summary>
        public static FigureAge GroupOf(string id)
        {
            string s = (id ?? "").ToLowerInvariant();
            if (s.StartsWith("toddler")) return FigureAge.Toddler;
            if (s.StartsWith("child") || s.StartsWith("kid")) return FigureAge.Child;
            if (s.StartsWith("teen")) return FigureAge.Teen;
            if (s.StartsWith("senior")) return FigureAge.Senior;
            return FigureAge.Adult;
        }

        static PersonPartRole RoleOf(string name)
        {
            switch (name)
            {
                case "LegL": return PersonPartRole.LegL;
                case "LegR": return PersonPartRole.LegR;
                case "ArmL": return PersonPartRole.ArmL;
                case "ArmR": return PersonPartRole.ArmR;
                default: return PersonPartRole.Body;
            }
        }
    }
}
