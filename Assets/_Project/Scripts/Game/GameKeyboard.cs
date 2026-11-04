using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game
{
    /// <summary>
    /// Tastatur für Tastenkürzel: wie <see cref="Keyboard.current"/>, aber <b>null, solange ein Textfeld den Fokus hat</b>
    /// (z. B. Name eines Spielstands) – sonst würde Tippen „S“ die Kamera bewegen oder „B“ den Abriss wählen. Alle
    /// Kürzel lesen <see cref="Current"/> statt <c>Keyboard.current</c>.
    /// <para>Esc: wer Esc verbraucht (Fenster schließen, Bauvorgang abbrechen), ruft <see cref="ConsumeEscape"/>; das
    /// Spielmenü öffnet sich nur, wenn in diesem Frame niemand Esc verbraucht hat.</para>
    /// </summary>
    public static class GameKeyboard
    {
        /// <summary>Gesetzt vom Texteingabe-Widget, solange es fokussiert ist.</summary>
        public static bool TextInputActive { get; set; }

        public static Keyboard Current => TextInputActive ? null : Keyboard.current;

        /// <summary>Strg (links oder rechts) gedrückt? Kürzel mit Strg sollen keine Einzeltasten-Kürzel auslösen.</summary>
        public static bool Ctrl => Keyboard.current != null && Keyboard.current.ctrlKey.isPressed;

        /// <summary>
        /// Drehen beim Setzen (Spieler, 2026-11-04): <b>Strg links + Mausrad</b>. Solange Strg links gehalten wird, zoomt die Kamera
        /// nicht (<see cref="CameraController"/>); Werkzeuge mit drehbarer Vorschau lesen <see cref="WheelRotateSteps"/>.
        /// </summary>
        public static bool WheelRotateHeld => !TextInputActive && Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed;

        /// <summary>Mausrad-Rasten dieses Frames bei gehaltenem Strg links (+1 = vom Benutzer weg gedreht → im Uhrzeigersinn), sonst 0.
        /// Über der UI 0 (dort scrollt das Rad).</summary>
        public static int WheelRotateSteps
        {
            get
            {
                if (!WheelRotateHeld) return 0;
                var mouse = Mouse.current;
                if (mouse == null) return 0;
                float y = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(y) < 0.01f) return 0;
                if (CityBuilder.Game.UI.UiBlocker.IsPointerOverUi(mouse.position.ReadValue())) return 0;
                return y > 0f ? 1 : -1;
            }
        }

        /// <summary>Drehwinkel aus Komma/Punkt (gedrückt) und Strg + Mausrad dieses Frames, in Grad.</summary>
        public static float RotateInput(Keyboard kb, float step)
        {
            float d = 0f;
            if (kb != null && kb.commaKey.wasPressedThisFrame) d -= step;
            if (kb != null && kb.periodKey.wasPressedThisFrame) d += step;
            return d + WheelRotateSteps * step;
        }

        static int escapeConsumedFrame = -1;

        /// <summary>Esc wurde in diesem Frame für etwas anderes verwendet (Fenster/Abbrechen).</summary>
        public static void ConsumeEscape() => escapeConsumedFrame = Time.frameCount;

        public static bool EscapeConsumedThisFrame => escapeConsumedFrame == Time.frameCount;
    }
}
