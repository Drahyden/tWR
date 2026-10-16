using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// One flag the future pause menu (Esc, D-087, D-110) must check before it opens: true while a scripted ending
    /// plays (D-199, D-207: all input frozen, including Esc) and while the end-of-run screen is up (D-209: Esc does
    /// nothing there). The Overtime ending keeps Esc working until its button is pressed (D-196, D-207).
    /// Lives in TWR.Core so UI/Player code can read it without referencing the run system.
    /// </summary>
    public static class PauseLock
    {
        /// <summary>True = Esc / pause must be ignored right now.</summary>
        public static bool IsBlocked { get; private set; }

        public static void SetBlocked(bool blocked) => IsBlocked = blocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => IsBlocked = false;
    }
}
