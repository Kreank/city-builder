using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Panels
{
    public enum WorldLabelStyle { Normal, Error, Letter }

    /// <summary>
    /// Beschriftungen an Weltpunkten (Länge/Winkel beim Straßenbau, Bauwerks-Hinweise, Zufahrt-Buchstaben …) als
    /// UI-Toolkit-Labels im Stil der Spiel-UI. Quellen melden ihre Beschriftungen je Frame an:
    /// <c>WorldLabels.Begin(this); WorldLabels.Add(this, pos, "120 m");</c> – <see cref="Begin"/> ersetzt alles, was die
    /// Quelle vorher gemeldet hat. Gemeldetes gilt für dieses und das nächste Frame (IMGUI-Quellen melden in OnGUI,
    /// also nach dem Zeichnen); danach verfällt es. Die Position wird beim Zeichnen mit der aktuellen Kamera umgerechnet.
    /// <para>Ist <see cref="Enabled"/> falsch (keine neue UI), zeichnen die Quellen wie bisher selbst per IMGUI.</para>
    /// </summary>
    public static class WorldLabels
    {
        public struct Entry
        {
            public Vector3 World;
            public string Text;
            public WorldLabelStyle Style;
            /// <summary>Verschiebung nach unten in Referenzpixeln (z. B. Fehlertext unter der Länge).</summary>
            public float OffsetY;
            /// <summary>Unterkante am Punkt (Beschriftung steht darüber) statt mittig.</summary>
            public bool Above;
        }

        sealed class Group
        {
            public int Frame;
            public readonly List<Entry> Entries = new List<Entry>();
        }

        static readonly Dictionary<object, Group> groups = new Dictionary<object, Group>();

        /// <summary>Wahr, sobald die UI-Toolkit-Ebene zeichnet (dann bitte nicht mehr selbst per IMGUI zeichnen).</summary>
        public static bool Enabled { get; internal set; }

        public static void Begin(object owner)
        {
            if (!groups.TryGetValue(owner, out var g)) groups[owner] = g = new Group();
            g.Entries.Clear();
            g.Frame = Time.frameCount;
        }

        public static void Add(object owner, Vector3 world, string text, WorldLabelStyle style = WorldLabelStyle.Normal, float offsetY = 0f,
            bool above = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!groups.TryGetValue(owner, out var g)) { Begin(owner); g = groups[owner]; }
            g.Frame = Time.frameCount;
            g.Entries.Add(new Entry { World = world, Text = text, Style = style, OffsetY = offsetY, Above = above });
        }

        /// <summary>Für die Zeichenebene: alle noch gültigen Beschriftungen.</summary>
        internal static void Collect(List<Entry> into)
        {
            into.Clear();
            int frame = Time.frameCount;
            foreach (var g in groups.Values)
                if (g.Frame >= frame - 1) into.AddRange(g.Entries);
        }
    }

    /// <summary>Zeichenebene für <see cref="WorldLabels"/> (Pool aus Labels, ohne Mausblockade).</summary>
    public sealed class WorldLabelLayer
    {
        readonly VisualElement layer;
        readonly List<Label> pool = new List<Label>();
        readonly List<WorldLabels.Entry> entries = new List<WorldLabels.Entry>();
        readonly Camera camera;

        public WorldLabelLayer(VisualElement hudRoot, Camera camera)
        {
            this.camera = camera;
            layer = Ui.Box("cb-world-labels");
            hudRoot.Insert(0, layer); // unter allen Leisten und Fenstern
            WorldLabels.Enabled = true;
        }

        public void Dispose() => WorldLabels.Enabled = false;

        public void Update()
        {
            WorldLabels.Collect(entries);
            var panel = layer.panel;
            int used = 0;
            if (panel != null && camera != null)
            {
                foreach (var e in entries)
                {
                    var vp = camera.WorldToViewportPoint(e.World);
                    if (vp.z <= 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) continue;
                    var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, e.World, camera);
                    var l = Next(ref used);
                    if (l.text != e.Text) l.text = e.Text;
                    l.EnableInClassList("cb-world-label--error", e.Style == WorldLabelStyle.Error);
                    l.EnableInClassList("cb-world-label--letter", e.Style == WorldLabelStyle.Letter);
                    // Mittig bzw. über dem Punkt: Verschiebung um die eigene Größe per Prozent-translate
                    l.style.left = p.x;
                    l.style.top = p.y + e.OffsetY;
                    l.style.translate = new Translate(Length.Percent(-50f), Length.Percent(e.Above ? -100f : -50f));
                }
            }
            for (int i = used; i < pool.Count; i++)
                if (!pool[i].ClassListContains("cb-hidden")) pool[i].AddToClassList("cb-hidden");
        }

        Label Next(ref int used)
        {
            if (used == pool.Count)
            {
                var l = Ui.Lbl("cb-world-label");
                layer.Add(l);
                pool.Add(l);
            }
            var r = pool[used++];
            r.RemoveFromClassList("cb-hidden");
            return r;
        }
    }
}
