using UnityEngine;

namespace TWR.Combat
{
    /// <summary>
    /// TEST ONLY: a shooting dummy with HP (D-118). Flashes red when hit, and at 0 HP raises
    /// CombatEvents.Killed (D-029), then plays a placeholder for the D-051 death: it shrinks and darkens,
    /// disappears, and respawns with full HP a few seconds later. Real enemies (Seeker, D-043) will
    /// implement IDamageable the same way. Values here are test-only, so they stay on the component.
    /// </summary>
    public class TestDamageDummy : MonoBehaviour, IDamageable
    {
        [SerializeField] float maxHealth = 100f;
        [SerializeField] float hitFlashSeconds = 0.12f;
        [Tooltip("Placeholder for D-051: shrink + darken this long, then vanish.")]
        [SerializeField] float dissolveSeconds = 0.6f;
        [SerializeField] float respawnSeconds = 4f;
        [SerializeField] bool logHits = true;
        [SerializeField] Renderer targetRenderer;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        enum State { Alive, Dissolving, Gone }

        State state;
        float health;
        float stateTime;
        float flashTimer;
        Vector3 startScale;
        Color idleColour = Color.white;
        Collider[] colliders;
        MaterialPropertyBlock block;

        public bool IsAlive => state == State.Alive;
        public float Health => health;

        void Awake()
        {
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
            colliders = GetComponentsInChildren<Collider>();
            block = new MaterialPropertyBlock();
            startScale = transform.localScale;
            if (targetRenderer != null && targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty(BaseColorId))
                idleColour = targetRenderer.sharedMaterial.GetColor(BaseColorId);
            Respawn();
        }

        public void TakeDamage(in DamageInfo damage)
        {
            if (!IsAlive) return;

            health = Mathf.Max(0f, health - damage.Amount);
            flashTimer = hitFlashSeconds;
            if (logHits) Debug.Log($"[TestDamageDummy] '{name}' took {damage.Amount:0} from {(damage.Attacker != null ? damage.Attacker.name : "?")}: {health:0}/{maxHealth:0} HP", this);

            if (health <= 0f)
            {
                state = State.Dissolving;
                stateTime = 0f;
                SetCollidersEnabled(false);
                CombatEvents.RaiseKilled(gameObject, damage);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (state)
            {
                case State.Alive:
                    if (flashTimer > 0f)
                    {
                        flashTimer -= dt;
                        SetColour(Color.Lerp(idleColour, Color.red, Mathf.Clamp01(flashTimer / hitFlashSeconds)));
                    }
                    break;

                case State.Dissolving:
                    stateTime += dt;
                    float t = Mathf.Clamp01(stateTime / dissolveSeconds);
                    transform.localScale = startScale * (1f - t);
                    SetColour(Color.Lerp(Color.red, Color.black, t));
                    if (t >= 1f)
                    {
                        state = State.Gone;
                        stateTime = 0f;
                        if (targetRenderer != null) targetRenderer.enabled = false;
                    }
                    break;

                case State.Gone:
                    stateTime += dt;
                    if (stateTime >= respawnSeconds) Respawn();
                    break;
            }
        }

        void Respawn()
        {
            state = State.Alive;
            health = maxHealth;
            flashTimer = 0f;
            transform.localScale = startScale;
            if (targetRenderer != null) targetRenderer.enabled = true;
            SetCollidersEnabled(true);
            SetColour(idleColour);
        }

        void SetCollidersEnabled(bool on)
        {
            foreach (Collider c in colliders) c.enabled = on;
        }

        void SetColour(Color c)
        {
            if (targetRenderer == null) return;
            targetRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            targetRenderer.SetPropertyBlock(block);
        }
    }
}
