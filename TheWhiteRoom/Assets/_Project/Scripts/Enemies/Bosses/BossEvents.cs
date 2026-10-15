using System;
using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Shared static boss events (D-148, D-165, D-185, D-193). Raised by BossEncounter for the Bull (D-048) and the
    /// Preacher (D-047), so music (D-075), the inner voice (D-080) and the HUD boss bar can react without scene
    /// references (D-161):
    /// - Started: the fight begins (the player entered the arena, or shot the boss first).
    /// - PhaseChanged: the boss reached a new phase (phase 2 at 50% HP, D-184).
    /// - Defeated: the boss of this arena is beaten; SectionExitGate (BossDefeated) opens the exit (D-165).
    /// - Despawned: the boss object went away (death animation finished or the arena unloaded); the HUD hides its bar.
    /// </summary>
    public static class BossEvents
    {
        /// <summary>(boss, bossId). The fight began.</summary>
        public static event Action<GameObject, string> Started;
        /// <summary>(boss, bossId, new phase number: 1, 2, ...).</summary>
        public static event Action<GameObject, string, int> PhaseChanged;
        /// <summary>(boss, bossId). The boss of the current arena is beaten.</summary>
        public static event Action<GameObject, string> Defeated;
        /// <summary>(boss). The boss object was disabled or destroyed.</summary>
        public static event Action<GameObject> Despawned;

        public static void RaiseStarted(GameObject boss, string bossId) => Started?.Invoke(boss, bossId);
        public static void RaisePhaseChanged(GameObject boss, string bossId, int phase) => PhaseChanged?.Invoke(boss, bossId, phase);
        public static void RaiseDefeated(GameObject boss, string bossId) => Defeated?.Invoke(boss, bossId);
        public static void RaiseDespawned(GameObject boss) => Despawned?.Invoke(boss);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Started = null;
            PhaseChanged = null;
            Defeated = null;
            Despawned = null;
        }
    }
}
