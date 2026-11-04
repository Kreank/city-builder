using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Panels
{
    /// <summary>Texteingabe für den Baukasten (Speichern unter, Seed …). Solange das Feld den Fokus hat, sind Tastenkürzel aus.</summary>
    public sealed partial class Ui
    {
        sealed class TextHolder
        {
            public Action<string> Change;
            public Action Submit;
        }

        /// <summary>
        /// Eingabefeld mit Bezeichnung. <paramref name="onChange"/> bei jeder Änderung, <paramref name="onSubmit"/> bei Enter.
        /// Während des Tippens liefert <see cref="GameKeyboard.Current"/> null (keine Kamerafahrt mit „S“, kein Abriss mit „B“).
        /// </summary>
        public void TextInput(string label, string value, Action<string> onChange, Action onSubmit = null, int maxLength = 60)
        {
            var e = Take("text-input", () =>
            {
                var r = Box("cb-field", "cb-text-row");
                r.Add(Lbl("cb-field__label"));
                r.Add(NewTextField(maxLength, null));
                return r;
            });
            SetText((Label)e[0], label);
            BindTextField((TextField)e[1], value, onChange, onSubmit);
        }

        /// <summary>Neues Textfeld mit dunklem Stil (<c>cb-text-input</c>), Enter/Esc und Tastatursperre.</summary>
        TextField NewTextField(int maxLength, string extraClass)
        {
            var tf = new TextField { maxLength = maxLength };
            tf.AddToClassList("cb-text-input");
            if (!string.IsNullOrEmpty(extraClass)) tf.AddToClassList(extraClass);
            var holder = new TextHolder();
            tf.userData = holder;
            tf.RegisterValueChangedCallback(ev => holder.Change?.Invoke(ev.newValue));
            tf.RegisterCallback<FocusInEvent>(_ => GameKeyboard.TextInputActive = true);
            tf.RegisterCallback<FocusOutEvent>(_ => GameKeyboard.TextInputActive = false);
            tf.RegisterCallback<DetachFromPanelEvent>(_ => GameKeyboard.TextInputActive = false);
            tf.RegisterCallback<KeyDownEvent>(ev =>
            {
                if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
                {
                    holder.Submit?.Invoke();
                    tf.Blur();
                    Clicked?.Invoke();
                }
                else if (ev.keyCode == KeyCode.Escape) tf.Blur();
            }, TrickleDown.TrickleDown);
            return tf;
        }

        static void BindTextField(TextField field, string value, Action<string> onChange, Action onSubmit)
        {
            var h = (TextHolder)field.userData;
            h.Change = onChange;
            h.Submit = onSubmit;
            bool focused = field.panel?.focusController?.focusedElement == field;
            if (!focused && field.value != (value ?? "")) field.SetValueWithoutNotify(value ?? "");
        }
    }
}
