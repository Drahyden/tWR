using System;
using UnityEngine;
using TWR.Combat;

namespace TWR.Enemies
{
    /// <summary>
    /// A boss's HP (D-185, D-192). Implements IDamageable, so the hitscan gun damages it and shows hit markers (the boss
    /// lives on the Enemy layer, D-134). DamageMultiplier is the hook for "takes more damage while stunned" (Bull x2,
    /// D-181). Raises Changed (HUD bar), Damaged, PhaseChanged (phase 2 at BossData.phaseTwoThreshold, D-184) and
    /// Died exactly once; at 0 HP IsAlive turns false BEFORE any event, so the gun shows the kill marker (D-122).
    /// BossEncounter turns those into BossEvents, CombatEvents.Killed and the death fade.
    /// </summary>
    public class BossHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] BossData data;

        float health;
        bool dead;

        /// <summary>(current, max).</summary>
        public event Action<float, float> Changed;
        /// <summary>A hit that did not kill.</summary>
        public event Action<DamageInfo> Damaged;
        /// <summary>(new phase). 2 when HP falls to the threshold.</summary>
        public event Action<int> PhaseChanged;
        /// <summary>The killing blow. Raised once.</summary>
        public event Action<DamageInfo> Died;

        public bool IsAlive => !dead;
        public float Health => health;
        public float MaxHealth => data != null ? data.maxHealth : 1000f;
        public float Normalized => health / MaxHealth;
        public int Phase { get; private set; } = 1;
        public BossData Data => data;

        /// <summary>Damage is multiplied by this (1 normally; the Bull sets 2 while stunned, D-181).</summary>
        public float DamageMultiplier { get; set; } = 1f;

        void Awake()
        {
            health = MaxHealth;
        }

        public void TakeDamage(in DamageInfo damage)
        {
            if (dead || damage.Amount <= 0f) return;

            float amount = damage.Amount * Mathf.Max(0f, DamageMultiplier);
            health = Mathf.Max(0f, health - amount);
            if (health <= 0f)
            {
                Kill(damage);
                return;
            }
            Changed?.Invoke(health, MaxHealth);
            UpdatePhase();
            Damaged?.Invoke(damage);
        }

        /// <summary>
        /// Debug only (F1 overlay, D-185): set HP directly. 0 or less kills the boss (no attacker, so it is never a
        /// "player kill"; bosses don't count anyway, D-158).
        /// </summary>
        public void DebugSetHealth(float value)
        {
            if (dead) return;
            if (value <= 0f)
            {
                health = 0f;
                Kill(new DamageInfo(0f, null, transform.position, Vector3.down, null));
                return;
            }
            health = Mathf.Min(value, MaxHealth);
            Changed?.Invoke(health, MaxHealth);
            UpdatePhase();
        }

        void UpdatePhase()
        {
            float threshold = data != null ? data.phaseTwoThreshold : 0.5f;
            if (Phase == 1 && health <= MaxHealth * threshold)
            {
                Phase = 2;
                PhaseChanged?.Invoke(Phase);
            }
        }

        void Kill(in DamageInfo killingBlow)
        {
            dead = true; // before any event: the gun reads IsAlive for the kill marker
            Changed?.Invoke(0f, MaxHealth);
            Died?.Invoke(killingBlow);
        }
    }
}
