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
                var tf = new TextField { maxLength = maxLength };
                tf.AddToClassList("cb-text-input");
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
                r.Add(tf);
                return r;
            });
            SetText((Label)e[0], label);
            var field = (TextField)e[1];
            var h = (TextHolder)field.userData;
            h.Change = onChange;
            h.Submit = onSubmit;
            bool focused = field.panel?.focusController?.focusedElement == field;
            if (!focused && field.value != (value ?? "")) field.SetValueWithoutNotify(value ?? "");
        }
    }
}
