using System;
using UnityEngine;
using TWR.Combat;

namespace TWR.Enemies
{
    /// <summary>
    /// The Seeker's HP (D-134: 100 HP = 4 pistol hits). Implements IDamageable so the existing
    /// hitscan gun (D-118) damages it and shows hit markers. At 0 HP it flips IsAlive to false at once (so the
    /// gun shows the KILL marker, D-122), raises DeathStarted (the OnDeathStarted hook for SeekerDeath and the
    /// later gory animation, D-051) and then CombatEvents.Killed exactly once, with the attacker (D-029).
    /// </summary>
    public class SeekerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] SeekerData data;

        float health;
        bool dead;

        /// <summary>Non-lethal hit (the brain chases the attacker, visuals flash).</summary>
        public event Action<DamageInfo> Damaged;

        /// <summary>
        /// OnDeathStarted hook (D-136): fired once, before CombatEvents.Killed. SeekerDeath shuts the
        /// Seeker down here; a future gory death animation (D-051) can listen too.
        /// </summary>
        public event Action<DamageInfo> DeathStarted;

        public bool IsAlive => !dead;
        public float Health => health;
        public float MaxHealth => data != null ? data.maxHealth : 100f;

        void Awake()
        {
            health = MaxHealth;
        }

        public void TakeDamage(in DamageInfo damage)
        {
            if (dead || damage.Amount <= 0f) return;

            health = Mathf.Max(0f, health - damage.Amount);
            if (health > 0f)
            {
                Damaged?.Invoke(damage);
                return;
            }

            dead = true; // before any event, so IsAlive is already false for the gun's kill marker
            DeathStarted?.Invoke(damage);
            CombatEvents.RaiseKilled(gameObject, damage);
        }
    }
}
