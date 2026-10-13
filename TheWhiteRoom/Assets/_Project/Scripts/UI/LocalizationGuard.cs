using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace TWR.UI
{
    /// <summary>
    /// Checks once per session whether localization (D-084) can actually be used: settings exist and
    /// at least one locale loaded. If not, it logs ONE warning and reports "unavailable", so UI can skip
    /// LocalizedString lookups instead of making the Localization package log an error on every use.
    /// </summary>
    public static class LocalizationGuard
    {
        public enum State { Unknown, Pending, Ready, Unavailable }

        public static State Current { get; private set; } = State.Unknown;
        static event Action<bool> Resolved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode()
        {
            Current = State.Unknown;
            Resolved = null;
        }

        /// Calls callback(true/false) once the check is done (right away if it already is).
        public static void WhenResolved(Action<bool> callback)
        {
            if (Current == State.Ready || Current == State.Unavailable)
            {
                callback(Current == State.Ready);
                return;
            }
            Resolved += callback;
            if (Current == State.Unknown) Begin();
        }

        static void Begin()
        {
            Current = State.Pending;
            if (!LocalizationSettings.HasSettings)
            {
                Finish(false, "no Localization Settings asset");
                return;
            }
            if (LocalizationSettings.AvailableLocales is IPreloadRequired preload)
            {
                var handle = preload.PreloadOperation;
                if (handle.IsDone) CheckLocales();
                else handle.Completed += _ => CheckLocales();
            }
            else
            {
                CheckLocales();
            }
        }

        static void CheckLocales()
        {
            var locales = LocalizationSettings.AvailableLocales?.Locales;
            if (locales == null || locales.Count == 0)
                Finish(false, "no locales");
            else
                Finish(true, null);
        }

        static void Finish(bool ready, string reason)
        {
            Current = ready ? State.Ready : State.Unavailable;
            if (!ready)
                Debug.LogWarning($"[TWR] Localization not set up ({reason}); prompts will show their keys. Run TWR > Setup Project.");
            var callbacks = Resolved;
            Resolved = null;
            callbacks?.Invoke(ready);
        }

        /// Fallback shown when localization is unavailable: the entry key, so testing can continue.
        public static string FallbackText(LocalizedString text)
        {
            var key = text.TableEntryReference.Key;
            return string.IsNullOrEmpty(key) ? $"[{text.TableEntryReference.KeyId}]" : $"[{key}]";
        }
    }
}
