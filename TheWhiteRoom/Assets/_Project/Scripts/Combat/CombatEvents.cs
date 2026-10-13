using System;
using UnityEngine;

namespace TWR.Combat
{
    /// <summary>
    /// Shared static combat events. Killed carries the attacker (inside DamageInfo) so a future
    /// performance tracker can count kills by the player (D-029) and voice triggers (D-080) can react,
    /// without references to every enemy.
    /// </summary>
    public static class CombatEvents
    {
        /// <summary>(victim, killing blow). killingBlow.Attacker is who gets the credit.</summary>
        public static event Action<GameObject, DamageInfo> Killed;

        public static void RaiseKilled(GameObject victim, in DamageInfo killingBlow)
        {
            Killed?.Invoke(victim, killingBlow);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Killed = null;
    }
}
