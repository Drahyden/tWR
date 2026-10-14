using UnityEngine;
using TWR.Combat;

namespace TWR.Enemies
{
    /// <summary>
    /// Stand-in boss for the two arenas (D-148: Escape = bull, Discover = preacher) until the real bosses exist
    /// (D-047, D-048). It does not move or attack: shoot it until its HP (BossStubData) runs out. Then it raises
    /// CombatEvents.Killed and BossEvents.Defeated, which unlocks the arena exit (D-165 BossDefeated), and shrinks away.
    /// Needs a BossMarker on the same object, so RunStats never counts it as spawned or killed (D-158).
    /// </summary>
    [RequireComponent(typeof(BossMarker))]
    public class BossPlaceholder : MonoBehaviour, IDamageable
    {
        [SerializeField] BossStubData data;

        float health;
        bool defeated;
        float vanishTimer;
        Vector3 startScale;
        Collider[] colliders;

        public bool IsAlive => !defeated;
        public bool IsDefeated => defeated;
        public string BossId => data != null ? data.bossId : name;

        void Awake()
        {
            health = data != null ? data.maxHealth : 150f;
            colliders = GetComponentsInChildren<Collider>(true);
            startScale = transform.localScale;
        }

        void Start()
        {
            EnemyEvents.RaiseSpawned(gameObject); // RunStats sees the BossMarker and leaves it out (D-158)
        }

        public void TakeDamage(in DamageInfo damage)
        {
            if (defeated || damage.Amount <= 0f) return;
            health = Mathf.Max(0f, health - damage.Amount);
            if (health > 0f) return;

            defeated = true; // before the events, so the gun shows the kill marker
            foreach (Collider c in colliders)
                if (c != null) c.enabled = false;
            CombatEvents.RaiseKilled(gameObject, damage);
            BossEvents.RaiseDefeated(gameObject, BossId);
            Debug.Log($"[Run] Boss placeholder '{BossId}' defeated: the arena exit opens (D-165).", this);
        }

        void Update()
        {
            if (!defeated) return;
            float seconds = data != null ? data.vanishSeconds : 1f;
            vanishTimer += Time.deltaTime;
            float t = seconds > 0f ? Mathf.Clamp01(vanishTimer / seconds) : 1f;
            transform.localScale = startScale * (1f - t);
            if (t >= 1f) gameObject.SetActive(false);
        }
    }
}
