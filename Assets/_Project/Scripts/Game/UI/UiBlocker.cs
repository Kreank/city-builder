using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI
{
    /// <summary>
    /// Merkt sich, wo gerade Oberflächen liegen, damit Klicks darauf nicht in der Welt landen.
    /// IMGUI-Komponenten melden ihre Flächen jedes Frame an (<see cref="Register"/>);
    /// UI-Toolkit-Oberflächen melden einmal ihre Wurzel an (<see cref="RegisterToolkitRoot"/>) und werden
    /// live per <c>panel.Pick</c> geprüft. Alle Werkzeuge fragen nur <see cref="IsPointerOverUi"/>.
    /// </summary>
    public static class UiBlocker
    {
        static readonly List<Rect> current = new List<Rect>();
        static readonly List<Rect> previous = new List<Rect>();
        static readonly List<VisualElement> toolkitRoots = new List<VisualElement>();
        static int frame = -1;

        /// <summary>Fläche in GUI-Koordinaten (Ursprung oben links) melden.</summary>
        public static void Register(Rect guiRect)
        {
            NextFrame();
            if (!current.Contains(guiRect)) current.Add(guiRect);
        }

        static void NextFrame()
        {
            if (frame == Time.frameCount) return;
            previous.Clear();
            previous.AddRange(current);
            current.Clear();
            frame = Time.frameCount;
        }

        /// <summary>
        /// UI-Toolkit-Wurzel anmelden. Blockiert wird, wo ein Element darunter getroffen wird
        /// (Container ohne eigene Fläche müssen <see cref="PickingMode.Ignore"/> haben).
        /// </summary>
        public static void RegisterToolkitRoot(VisualElement root)
        {
            if (root != null && !toolkitRoots.Contains(root)) toolkitRoots.Add(root);
        }

        public static void UnregisterToolkitRoot(VisualElement root) => toolkitRoots.Remove(root);

        /// <summary>Mausposition aus dem Input System (Ursprung unten links).</summary>
        public static bool IsPointerOverUi(Vector2 screenPos)
        {
            var gui = new Vector2(screenPos.x, Screen.height - screenPos.y);
            // OnGUI läuft nach Update – deshalb die Flächen des letzten Frames mitprüfen.
            foreach (var r in current) if (r.Contains(gui)) return true;
            foreach (var r in previous) if (r.Contains(gui)) return true;
            return IsOverToolkit(gui);
        }

        static bool IsOverToolkit(Vector2 gui)
        {
            for (int i = toolkitRoots.Count - 1; i >= 0; i--)
            {
                var root = toolkitRoots[i];
                if (root == null) { toolkitRoots.RemoveAt(i); continue; }
                var panel = root.panel;
                if (panel == null || root.resolvedStyle.display == DisplayStyle.None) continue;
                // ScreenToPanel erwartet Bildschirmpixel mit Ursprung oben links und rechnet die Skalierung ein.
                var local = RuntimePanelUtils.ScreenToPanel(panel, gui);
                var picked = panel.Pick(local);
                if (picked != null && (picked == root || root.Contains(picked))) return true;
            }
            return false;
        }

        /// <summary>
        /// Breite, die IMGUI-Fenster am rechten Bildschirmrand gerade belegen (für die Toasts der neuen UI,
        /// die sonst darunter lägen). 0 = frei.
        /// </summary>
        public static float RightEdgeImguiWidth()
        {
            float width = 0f;
            void Check(List<Rect> list)
            {
                foreach (var r in list)
                    if (r.xMax >= Screen.width - 24f && r.yMin < Screen.height * 0.5f) width = Mathf.Max(width, Screen.width - r.xMin);
            }
            Check(current);
            Check(previous);
            return width;
        }
    }
}
