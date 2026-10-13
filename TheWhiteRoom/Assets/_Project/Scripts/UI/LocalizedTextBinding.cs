using System;
using UnityEngine;
using UnityEngine.Localization;

namespace TWR.UI
{
    /// <summary>
    /// Reusable helper for HUD text (D-084): "show this LocalizedString, and tell me whenever its text
    /// changes". Same safe pattern as InteractionPromptUI: it only touches the Localization package once
    /// LocalizationGuard says it works; otherwise it shows a fallback once instead of logging an error on
    /// every lookup. Event-driven: no work per frame. Plain C# class, owned by a UI MonoBehaviour.
    /// </summary>
    public sealed class LocalizedTextBinding
    {
        readonly Behaviour owner;
        readonly Action<string> onText;
        readonly string fallbackOverride;
        readonly LocalizedString.ChangeHandler onStringChanged;
        readonly Action<bool> onGuardResolved;

        LocalizedString current;
        bool subscribed;

        /// <param name="owner">The UI component; nothing is shown after it is destroyed or disabled.</param>
        /// <param name="onText">Receives the text every time it changes (language switch included).</param>
        /// <param name="fallbackOverride">Shown if localization is unavailable. Null = show "[key]".</param>
        public LocalizedTextBinding(Behaviour owner, Action<string> onText, string fallbackOverride = null)
        {
            this.owner = owner;
            this.onText = onText;
            this.fallbackOverride = fallbackOverride;
            onStringChanged = HandleStringChanged; // cached: no allocation per subscribe
            onGuardResolved = HandleGuardResolved;
        }

        public LocalizedString Current => current;

        /// <summary>Show this string (null or empty = show nothing). Safe to call with the same string again.</summary>
        public void Set(LocalizedString text)
        {
            LocalizedString next = (text != null && !text.IsEmpty) ? text : null;
            if (ReferenceEquals(next, current) && (subscribed || next == null)) return;

            Unsubscribe();
            current = next;
            if (current == null)
            {
                onText?.Invoke(string.Empty);
                return;
            }

            switch (LocalizationGuard.Current)
            {
                case LocalizationGuard.State.Ready:
                    current.StringChanged += onStringChanged; // fires right away with the translated text
                    subscribed = true;
                    break;
                case LocalizationGuard.State.Unavailable:
                    onText?.Invoke(fallbackOverride ?? LocalizationGuard.FallbackText(current));
                    break;
                default:
                    LocalizationGuard.WhenResolved(onGuardResolved); // re-applies once the check is done
                    break;
            }
        }

        /// <summary>Stop listening (call from OnDisable).</summary>
        public void Clear()
        {
            Unsubscribe();
            current = null;
        }

        void Unsubscribe()
        {
            if (current != null && subscribed) current.StringChanged -= onStringChanged;
            subscribed = false;
        }

        void HandleGuardResolved(bool ready)
        {
            if (owner == null || !owner.isActiveAndEnabled || current == null || subscribed) return;
            LocalizedString text = current;
            current = null; // force Set to re-apply
            Set(text);
        }

        void HandleStringChanged(string value)
        {
            if (owner != null) onText?.Invoke(value);
        }
    }
}
