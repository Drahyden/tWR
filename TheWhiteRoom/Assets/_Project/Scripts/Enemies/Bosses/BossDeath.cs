using UnityEngine;
using UnityEngine.AI;

namespace TWR.Enemies
{
    /// <summary>
    /// Placeholder boss death (stands in for D-051's brief gory death + dissolve; no gore here). BossEncounter calls Begin
    /// once: every collider goes off at once (bullets pass through, nothing can bump it), the NavMeshAgent stops, and
    /// the boss sinks a little while shrinking to nothing over BossData.vanishSeconds, then the object is switched off.
    /// </summary>
    public class BossDeath : MonoBehaviour
    {
        [SerializeField] BossData data;
        [Tooltip("Behaviours switched off at death (the brain, visuals that animate).")]
        [SerializeField] Behaviour[] disableOnDeath = new Behaviour[0];

        Collider[] colliders;
        NavMeshAgent agent;
        Vector3 startScale;
        Vector3 startPosition;
        bool dying;
        float timer;

        public bool IsDying => dying;

        void Awake()
        {
            colliders = GetComponentsInChildren<Collider>(true);
            agent = GetComponent<NavMeshAgent>();
        }

        public void Begin()
        {
            if (dying) return;
            dying = true;
            timer = 0f;
            startScale = transform.localScale;
            startPosition = transform.position;
            foreach (Behaviour b in disableOnDeath)
                if (b != null) b.enabled = false;
            if (agent != null) agent.enabled = false;
            foreach (Collider c in colliders)
                if (c != null) c.enabled = false;
        }

        void Update()
        {
            if (!dying) return;
            timer += Time.deltaTime;
            float seconds = data != null ? data.vanishSeconds : 1.5f;
            float t = Mathf.Clamp01(timer / seconds);
            float eased = t * t;
            transform.localScale = startScale * (1f - eased);
            transform.position = startPosition + Vector3.down * eased * 0.8f;
            if (t >= 1f) gameObject.SetActive(false);
        }
    }
}
