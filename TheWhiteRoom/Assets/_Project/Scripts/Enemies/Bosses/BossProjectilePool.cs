using UnityEngine;
using TWR.Combat;
using TWR.Core;

namespace TWR.Enemies
{
    /// <summary>
    /// Pooled boss projectiles (D-184, D-193: at most 30 alive). All projectile objects are made once in Awake (plain
    /// sphere renderers, no colliders, no scripts each); this one component moves them all. Every frame each live
    /// projectile does ONE Physics.SphereCast over the distance it travels (no allocations, D-100): a hit on
    /// the environment (platforms, cover, walls) just removes it, so cover really blocks (D-184); a hit on the player
    /// deals its damage through IDamageable with the boss as attacker. A projectile that hits nothing disappears after
    /// its lifetime. Fire returns false when all 30 are in the air (that shot is skipped).
    /// Lives on its own object in the arena, so the boss's death shrink never moves it.
    /// </summary>
    public class BossProjectilePool : MonoBehaviour
    {
        [Tooltip("Pool size (D-193: at most 30 projectiles at once).")]
        [SerializeField, Range(1, 30)] int capacity = 30;
        [SerializeField] Material material;
        [Tooltip("Visual diameter of a projectile (m).")]
        [SerializeField, Min(0.05f)] float visualSize = 0.5f;

        struct Projectile
        {
            public bool Alive;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Radius;
            public float Damage;
            public float DieAt;
            public GameObject Attacker;
        }

        Projectile[] projectiles;
        Transform[] visuals;
        int environmentMask;
        int hitMask;
        int playerLayer;
        IDamageable player;

        public int AliveCount { get; private set; }
        public int Capacity => capacity;

        void Awake()
        {
            projectiles = new Projectile[capacity];
            visuals = new Transform[capacity];
            for (int i = 0; i < capacity; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Projectile " + i;
                Destroy(go.GetComponent<Collider>()); // swept by hand, never a physics body
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * visualSize;
                var renderer = go.GetComponent<MeshRenderer>();
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                visuals[i] = go.transform;
            }
            playerLayer = GameLayers.PlayerLayer;
        }

        /// <summary>Set by the boss: what blocks projectiles, and who gets damaged on a Player-layer hit.</summary>
        public void Configure(LayerMask environment, IDamageable playerHealth)
        {
            environmentMask = GameLayers.WithoutActors(environment);
            hitMask = environmentMask | (playerLayer >= 0 ? 1 << playerLayer : 0);
            player = playerHealth;
        }

        /// <summary>Launch one projectile. False if the pool is full.</summary>
        public bool Fire(Vector3 origin, Vector3 direction, float speed, float radius, float damage, float lifetime, GameObject attacker)
        {
            for (int i = 0; i < projectiles.Length; i++)
            {
                if (projectiles[i].Alive) continue;
                projectiles[i] = new Projectile
                {
                    Alive = true,
                    Position = origin,
                    Velocity = direction.normalized * speed,
                    Radius = radius,
                    Damage = damage,
                    DieAt = Time.time + lifetime,
                    Attacker = attacker,
                };
                visuals[i].position = origin;
                visuals[i].gameObject.SetActive(true);
                AliveCount++;
                return true;
            }
            return false;
        }

        /// <summary>Remove every projectile (boss defeated).</summary>
        public void ClearAll()
        {
            for (int i = 0; i < projectiles.Length; i++)
                if (projectiles[i].Alive) Kill(i);
        }

        void Update()
        {
            if (AliveCount == 0) return;
            float dt = Time.deltaTime;
            float now = Time.time;
            for (int i = 0; i < projectiles.Length; i++)
            {
                if (!projectiles[i].Alive) continue;
                ref Projectile p = ref projectiles[i];
                if (now >= p.DieAt)
                {
                    Kill(i);
                    continue;
                }
                Vector3 step = p.Velocity * dt;
                float distance = step.magnitude;
                if (distance > 0f && Physics.SphereCast(p.Position, p.Radius, step / distance, out RaycastHit hit, distance, hitMask,
                                                        QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.gameObject.layer == playerLayer && player != null && player.IsAlive)
                        player.TakeDamage(new DamageInfo(p.Damage, p.Attacker, hit.point, p.Velocity.normalized, null));
                    Kill(i); // blocked by a platform or cover, or it hit the player (D-184)
                    continue;
                }
                p.Position += step;
                visuals[i].position = p.Position;
            }
        }

        void Kill(int i)
        {
            projectiles[i].Alive = false;
            projectiles[i].Attacker = null;
            visuals[i].gameObject.SetActive(false);
            AliveCount--;
        }
    }
}
