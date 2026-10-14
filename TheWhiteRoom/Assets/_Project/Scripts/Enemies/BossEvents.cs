using System;
using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Shared static boss events (D-148, D-165). Defeated opens the boss arena's exit (SectionExitGate with
    /// BossDefeated) and is a hook for music (D-075) and the inner voice (D-080). Raised by the boss placeholder now,
    /// by the real bull / preacher later.
    /// </summary>
    public static class BossEvents
    {
        /// <summary>(boss, bossId). The boss of the current arena is beaten.</summary>
        public static event Action<GameObject, string> Defeated;

        public static void RaiseDefeated(GameObject boss, string bossId) => Defeated?.Invoke(boss, bossId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Defeated = null;
    }
}
