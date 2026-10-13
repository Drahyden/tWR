using UnityEngine;

namespace TWR.Combat
{
    /// <summary>
    /// TEST ONLY: a trigger volume that hurts any IDamageable standing in it, a few HP at a time.
    /// Lets you test the HP bar and death-restart (D-034, D-122) without a debug key, so the
    /// fixed key map (D-110) stays untouched. Needs a trigger collider (+ kinematic Rigidbody); the
    /// builder puts it on the Ignore Raycast layer so bullets and interaction pass through it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TestDamageZone : MonoBehaviour
    {
        [SerializeField] float damagePerTick = 10f;
        [SerializeField] float tickSeconds = 0.5f;

        Collider lastOther;
        IDamageable lastTarget;
        float nextTickTime;

        void OnTriggerStay(Collider other)
        {
            if (other != lastOther)
            {
                lastOther = other;
                lastTarget = other.GetComponentInParent<IDamageable>();
            }
            if (lastTarget == null || !lastTarget.IsAlive || Time.time < nextTickTime) return;

            nextTickTime = Time.time + tickSeconds;
            lastTarget.TakeDamage(new DamageInfo(damagePerTick, gameObject, other.transform.position, Vector3.down, null));
        }
    }
}
