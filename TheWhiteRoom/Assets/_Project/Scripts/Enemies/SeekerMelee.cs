using System;
using UnityEngine;
using TWR.Combat;

namespace TWR.Enemies
{
    /// <summary>
    /// The Seeker's close-range attack (D-043, D-134): a visible and audible 0.4 s wind-up (leans back,
    /// band flashes, wind-up sound), then the strike. The strike only hurts if the target is STILL within 1.8 m
    /// and in front of the Seeker at that moment, so stepping or dodging away during the telegraph avoids it.
    /// 20 damage, then a 1.2 s cooldown. Damage goes through IDamageable with the Seeker as attacker (D-118).
    /// The brain (Seeker) decides when to start; this script only runs one attack at a time.
    /// </summary>
    public class SeekerMelee : MonoBehaviour
    {
        [SerializeField] SeekerData data;
        [SerializeField] SeekerVisuals visuals;
        [SerializeField] SeekerAudio sounds;

        Transform target;
        IDamageable targetHealth;
        bool windingUp;
        float windUpStart;
        float readyTime;

        /// <summary>(landed). Raised when a wind-up ends, true if it hit.</summary>
        public event Action<bool> AttackFinished;

        public bool IsWindingUp => windingUp;
        public bool IsReady => !windingUp && Time.time >= readyTime;

        /// <summary>Start the wind-up against target. Returns false if on cooldown or already attacking.</summary>
        public bool TryStartAttack(Transform targetTransform, IDamageable damageable)
        {
            if (!IsReady || data == null || targetTransform == null || damageable == null || !damageable.IsAlive) return false;

            target = targetTransform;
            targetHealth = damageable;
            windingUp = true;
            windUpStart = Time.time;
            if (sounds != null) sounds.PlayWindUp();
            if (visuals != null) visuals.SetWindUp(0f);
            return true;
        }

        /// <summary>Abort the wind-up without striking (death, target gone).</summary>
        public void Cancel()
        {
            if (!windingUp) return;
            windingUp = false;
            if (visuals != null) visuals.EndWindUp(false);
        }

        void OnDisable() => Cancel();

        void Update()
        {
            if (!windingUp) return;

            float t = data.windUpSeconds > 0f ? (Time.time - windUpStart) / data.windUpSeconds : 1f;
            if (visuals != null) visuals.SetWindUp(t);
            if (t >= 1f) Strike();
        }

        void Strike()
        {
            windingUp = false;
            readyTime = Time.time + data.attackCooldown;
            if (visuals != null) visuals.EndWindUp(true);

            bool landed = target != null && targetHealth != null && targetHealth.IsAlive && TargetInReach();
            if (landed)
            {
                Vector3 direction = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up).normalized;
                Vector3 point = target.position + Vector3.up * data.eyeHeight * 0.7f;
                targetHealth.TakeDamage(new DamageInfo(data.meleeDamage, gameObject, point, direction, null));
            }
            AttackFinished?.Invoke(landed);
        }

        /// Still close enough, roughly level, and inside the front arc.
        bool TargetInReach()
        {
            Vector3 offset = target.position - transform.position;
            if (Mathf.Abs(offset.y) > data.meleeMaxHeightDifference) return false;
            Vector3 flat = Vector3.ProjectOnPlane(offset, Vector3.up);
            if (flat.sqrMagnitude > data.meleeRange * data.meleeRange) return false;
            if (flat.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(transform.forward, flat) <= data.meleeHalfAngle;
        }
    }
}
