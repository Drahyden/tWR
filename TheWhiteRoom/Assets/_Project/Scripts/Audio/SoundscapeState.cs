using System;
using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// Shared "is the world silent right now?" switch (D-168, D-082). A silent room requests silence while it is
    /// loaded; the ambient bed (D-077 fluorescent hum) and, later, the adaptive music (D-075) listen and fade out.
    /// Player, gun and enemy sounds are NOT affected (D-168), and gameplay noise events never are.
    /// The project has no AudioMixer yet, so this replaces the planned mixer snapshot: when a mixer exists, its
    /// listener can switch a snapshot here instead. Requests are counted so two sources can't cancel each other.
    /// </summary>
    public static class SoundscapeState
    {
        static int requests;

        /// <summary>(silenced, fadeSeconds). Raised only when the state flips.</summary>
        public static event Action<bool, float> Changed;

        public static bool IsSilenced => requests > 0;

        public static void RequestSilence(float fadeSeconds)
        {
            requests++;
            if (requests == 1) Changed?.Invoke(true, fadeSeconds);
        }

        public static void ReleaseSilence(float fadeSeconds)
        {
            if (requests == 0) return;
            requests--;
            if (requests == 0) Changed?.Invoke(false, fadeSeconds);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            requests = 0;
            Changed = null;
        }
    }
}
