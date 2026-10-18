using System;
using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// Shared "is the world silent right now?" switch (D-168, D-082), and still the single authority for silence:
    /// a silent room requests silence while it is loaded. Requests are counted so two sources can't cancel each other.
    /// A1 (D-220): when the state flips it tells AudioState, which switches the mixer to the Silent snapshot (music and
    /// ambience state groups fade to silence, hum included). Player, gun and enemy sounds are NOT affected (D-168), and
    /// gameplay noise events never are. The Changed event stays for other listeners (e.g. the future music system).
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
            if (requests != 1) return;
            AudioState.HandleSilenceChanged(fadeSeconds);
            Changed?.Invoke(true, fadeSeconds);
        }

        public static void ReleaseSilence(float fadeSeconds)
        {
            if (requests == 0) return;
            requests--;
            if (requests != 0) return;
            AudioState.HandleSilenceChanged(fadeSeconds);
            Changed?.Invoke(false, fadeSeconds);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            requests = 0;
            Changed = null;
        }
    }
}
