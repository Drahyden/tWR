using System;
using UnityEngine;
using UnityEngine.Audio;

namespace TWR.Audio
{
    /// <summary>
    /// The one controller that picks the mixer snapshot (D-220). Priority: Paused > Ending > Silent > Default.
    /// - Paused: SetPaused (API only; the pause menu D-087 will call it).
    /// - Ending (D-221): EnterEnding. Scripted endings call it when their scene starts; Overtime stays on Default (hum,
    ///   footsteps) until the button, then EnterEnding(ambienceOff: true): Ending snapshot AND the ambient bed fades its
    ///   hum out (AmbienceOff), because the Ending snapshot alone only lowers ambience by about 10 dB.
    /// - Silent (D-168): SoundscapeState stays the single authority for silence requests and tells us when it flips.
    /// - Default: ResetToDefault (main menu, new run, leaving an ending; D-220).
    /// Snapshots only move the STATE groups; the five exposed user volumes belong to the sound settings (D-225).
    /// Changes are rare events, never per frame. Mixer Update Mode must be Unscaled Time so fades also run while paused.
    /// </summary>
    public static class AudioState
    {
        static bool paused, ending, endingAmbienceOff;
        static AudioSnapshotKind current;
        static bool applied;

        /// <summary>Raised after any change (the snapshot or the ambience-off flag).</summary>
        public static event Action Changed;

        /// <summary>The snapshot that was last applied.</summary>
        public static AudioSnapshotKind Current => current;
        public static bool IsPaused => paused;
        public static bool IsEnding => ending;
        /// <summary>Overtime after the button (D-221): the ambient bed should be silent.</summary>
        public static bool AmbienceOff => ending && endingAmbienceOff;

        /// <summary>What the priority rule asks for right now.</summary>
        public static AudioSnapshotKind Wanted
        {
            get
            {
                if (paused) return AudioSnapshotKind.Paused;
                if (ending) return AudioSnapshotKind.Ending;
                if (SoundscapeState.IsSilenced) return AudioSnapshotKind.Silent;
                return AudioSnapshotKind.Default;
            }
        }

        static AudioRoutingSettings Settings => AudioRouting.Settings;

        /// <summary>Pause snapshot on/off (D-220). No caller yet; the pause menu (D-087) will use it.</summary>
        public static void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            Apply(Settings != null ? Settings.toPausedSeconds : 0.2f);
        }

        /// <summary>Switch to the Ending snapshot (D-221). ambienceOff = also fade the ambient bed out (Overtime button).</summary>
        public static void EnterEnding(bool ambienceOff)
        {
            if (ending && endingAmbienceOff == ambienceOff) return;
            ending = true;
            endingAmbienceOff = ambienceOff;
            Apply(Settings != null ? Settings.toEndingSeconds : 1.5f);
        }

        /// <summary>Back to normal play (D-220): not paused, no ending. A loaded silent room still keeps Silent.</summary>
        public static void ResetToDefault()
        {
            paused = false;
            ending = false;
            endingAmbienceOff = false;
            Apply(Settings != null ? Settings.toDefaultSeconds : 1f);
        }

        /// <summary>Called by SoundscapeState when silence starts or ends (D-168).</summary>
        internal static void HandleSilenceChanged(float fadeSeconds) => Apply(fadeSeconds);

        /// <summary>Apply the wanted snapshot again at once. The audio bootstrap calls it in Start (the mixer ignores calls in Awake).</summary>
        public static void Reapply()
        {
            applied = false;
            Apply(0f);
        }

        static void Apply(float seconds)
        {
            AudioSnapshotKind wanted = Wanted;
            if (!applied || wanted != current)
            {
                AudioRoutingSettings settings = Settings;
                AudioMixerSnapshot snapshot = settings != null ? settings.GetSnapshot(wanted) : null;
                if (snapshot != null)
                {
                    snapshot.TransitionTo(Mathf.Max(0f, seconds));
                    applied = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[Audio] Snapshot -> {wanted} ({seconds:0.##} s){(AmbienceOff ? ", ambience off" : "")} (D-220).");
#endif
                }
                current = wanted;
            }
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            paused = false;
            ending = false;
            endingAmbienceOff = false;
            current = AudioSnapshotKind.Default;
            applied = false;
            Changed = null;
        }
    }
}
