using System.Collections.Generic;
using UnityEngine;

namespace CityBuilder.Game.UI
{
    /// <summary>
    /// Merkt sich, wo gerade (IMGUI-)Oberflächen liegen, damit Klicks darauf nicht in der Welt
    /// landen. UI-Komponenten melden ihre Flächen jedes Frame an.
    /// </summary>
    public static class UiBlocker
    {
        static readonly List<Rect> current = new List<Rect>();
        static readonly List<Rect> previous = new List<Rect>();
        static int frame = -1;

        /// <summary>Fläche in GUI-Koordinaten (Ursprung oben links) melden.</summary>
        public static void Register(Rect guiRect)
        {
            if (frame != Time.frameCount)
            {
                previous.Clear();
                previous.AddRange(current);
                current.Clear();
                frame = Time.frameCount;
            }
            if (!current.Contains(guiRect)) current.Add(guiRect);
        }

        /// <summary>Mausposition aus dem Input System (Ursprung unten links).</summary>
        public static bool IsPointerOverUi(Vector2 screenPos)
        {
            var gui = new Vector2(screenPos.x, Screen.height - screenPos.y);
            // OnGUI läuft nach Update – deshalb die Flächen des letzten Frames mitprüfen.
            foreach (var r in current) if (r.Contains(gui)) return true;
            foreach (var r in previous) if (r.Contains(gui)) return true;
            return false;
        }
    }
}
