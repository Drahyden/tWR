using UnityEngine;
using UnityEngine.AI;
using TWR.Combat;

namespace TWR.Enemies
{
    /// <summary>
    /// Placeholder death sequence (D-136, stands in for D-051's brief gory animation + dissolve).
    /// The moment SeekerHealth reports death it turns everything off: AI brain, senses, melee, NavMeshAgent and
    /// all colliders (bullets pass through, nothing can bump it). Then 0.6 s collapse (tips over, darker tint),
    /// 1.0 s shrink, and the GameObject is destroyed. Nothing persists; a reload brings a fresh Seeker.
    /// </summary>
    public class SeekerDeath : MonoBehaviour
    {
        [SerializeField] SeekerData data;
        [SerializeField] SeekerHealth health;
        [SerializeField] SeekerVisuals visuals;
        [SerializeField] SeekerAudio sounds;
        [Tooltip("Behaviours switched off at death (brain, senses, melee).")]
        [SerializeField] Behaviour[] disableOnDeath = new Behaviour[0];

        Collider[] colliders;
        NavMeshAgent agent;
        bool dying;
        float timer;

        void Awake()
        {
            if (health == null) health = GetComponent<SeekerHealth>();
            colliders = GetComponentsInChildren<Collider>(true);
            agent = GetComponent<NavMeshAgent>();
        }

        void OnEnable()
        {
            if (health != null) health.DeathStarted += Begin;
        }

        void OnDisable()
        {
            if (health != null) health.DeathStarted -= Begin;
        }

        void Begin(DamageInfo killingBlow)
        {
            if (dying) return;
            dying = true;
            timer = 0f;

            foreach (Behaviour b in disableOnDeath)
                if (b != null) b.enabled = false;
            if (agent != null) agent.enabled = false;
            foreach (Collider c in colliders)
                if (c != null) c.enabled = false;

            if (visuals != null) visuals.BeginDeath(killingBlow.Direction);
            if (sounds != null) sounds.PlayDeath();
        }

        void Update()
        {
            if (!dying) return;

            timer += Time.deltaTime;
            float collapse = data != null ? data.collapseSeconds : 0.6f;
            float fade = data != null ? data.fadeSeconds : 1f;
            float collapse01 = collapse > 0f ? Mathf.Clamp01(timer / collapse) : 1f;
            float fade01 = fade > 0f ? Mathf.Clamp01((timer - collapse) / fade) : 1f;
            if (visuals != null) visuals.SetDeath(collapse01, fade01);

            if (timer >= collapse + fade) Destroy(gameObject);
        }
    }
}
