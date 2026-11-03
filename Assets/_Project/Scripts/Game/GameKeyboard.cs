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

        static int escapeConsumedFrame = -1;

        /// <summary>Esc wurde in diesem Frame für etwas anderes verwendet (Fenster/Abbrechen).</summary>
        public static void ConsumeEscape() => escapeConsumedFrame = Time.frameCount;

        public static bool EscapeConsumedThisFrame => escapeConsumedFrame == Time.frameCount;
    }
}
