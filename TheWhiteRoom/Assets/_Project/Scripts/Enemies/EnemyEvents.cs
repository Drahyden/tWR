using System;
using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>How alert an enemy is, in words music and the inner voice can use (D-075, D-080).</summary>
    public enum EnemyAwareness
    {
        /// <summary>Patrolling, returning, or dead.</summary>
        Unaware,
        /// <summary>Heard something small, checking.</summary>
        Suspicious,
        /// <summary>Investigating a noise or searching for a lost player.</summary>
        Alerted,
        /// <summary>Chasing or attacking.</summary>
        Hostile
    }

    /// <summary>
    /// Shared static enemy events (D-133, D-137). Spawned lets RunStats count the run's enemies
    /// (D-029, D-052: an avoided enemy is registered but never killed). AwarenessChanged is the hook for the
    /// adaptive music state (D-075) and inner-voice lines (D-080). Kills use CombatEvents.Killed.
    /// No scene references needed; listeners get the enemy GameObject.
    /// </summary>
    public static class EnemyEvents
    {
        /// <summary>(enemy). An enemy entered the scene (spawn point or placed).</summary>
        public static event Action<GameObject> Spawned;

        /// <summary>(enemy, from, to). Raised only when the awareness level actually changes.</summary>
        public static event Action<GameObject, EnemyAwareness, EnemyAwareness> AwarenessChanged;

        public static void RaiseSpawned(GameObject enemy) => Spawned?.Invoke(enemy);

        public static void RaiseAwarenessChanged(GameObject enemy, EnemyAwareness from, EnemyAwareness to) =>
            AwarenessChanged?.Invoke(enemy, from, to);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Spawned = null;
            AwarenessChanged = null;
        }
    }
}
