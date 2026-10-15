using UnityEngine;
using TWR.Combat;
using TWR.Core;
using TWR.Player;
using TWR.Run;

namespace TWR.Enemies
{
    /// <summary>
    /// The shared frame around a boss fight (D-185), used by both the Bull (D-048) and the Preacher (D-047):
    /// - Start: registers the boss as spawned (RunStats leaves it out because of BossMarker, D-158), finds the player once,
    ///   and creates the boss's random stream: D-172 category 3 for this arena's plan stop (D-189). Seed 0 and the
    ///   sandbox get the authored defaults (the brains decide what that means).
    /// - Starts the fight when the player walks into the fight area (checked 5 times a second, D-193) or shoots the boss:
    ///   BossEvents.Started, the inner-voice cue, and EnemyEvents awareness Hostile, so music uses the combat state (D-193).
    /// - Phase 2 (BossHealth): BossEvents.PhaseChanged + voice cue, forwarded to the brain (D-184).
    /// - Death: brain stops, CombatEvents.Killed (bosses are not counted, D-158), BossEvents.Defeated (the exit gate
    ///   opens, D-165), voice cue, then BossDeath plays the placeholder sink-and-shrink (D-051 limits).
    /// Active = the boss in the loaded arena, for the F1 debug overlay only.
    /// </summary>
    [DefaultExecutionOrder(-10)] // Start before the brains, so the boss stream exists when they read it
    [RequireComponent(typeof(BossMarker), typeof(BossHealth))]
    [DisallowMultipleComponent]
    public class BossEncounter : MonoBehaviour
    {
        [SerializeField] BossData data;
        [SerializeField] BossHealth health;
        [SerializeField] BossDeath death;
        [SerializeField] BossAudio sounds;
        [Tooltip("World-space box of the arena floor space. The fight starts when the player's feet are inside it.")]
        [SerializeField] Bounds fightArea = new Bounds(Vector3.zero, Vector3.one * 10f);

        IBossBrain brain;
        PlayerController player;
        IDamageable playerHealth;
        Transform playerCamera;
        float nextCheck;
        float nextHurtSound;

        /// <summary>The boss in the currently loaded arena (debug overlay only). Null outside boss arenas.</summary>
        public static BossEncounter Active { get; private set; }

        public BossData Data => data;
        public BossHealth Health => health;
        public IBossBrain Brain => brain;
        public BossAudio Sounds => sounds;
        public string BossId => data != null ? data.bossId : name;
        public bool IsStarted { get; private set; }
        public bool IsDefeated { get; private set; }
        public Bounds FightArea => fightArea;

        /// <summary>The persistent player (Core scene, D-161). Null if none exists.</summary>
        public PlayerController Player => player;
        public Transform PlayerTransform => player != null ? player.transform : null;
        public Transform PlayerCamera => playerCamera;
        public IDamageable PlayerHealth => playerHealth;
        public bool PlayerAlive => player != null && playerHealth != null && playerHealth.IsAlive;

        /// <summary>The D-189 boss stream for this arena (D-172 category 3). Never null after Awake.</summary>
        public SeededRandom Stream { get; private set; }
        /// <summary>Where the stream came from: Seeded run, Curated (seed 0) or Sandbox.</summary>
        public SectionRollMode StreamMode { get; private set; } = SectionRollMode.Sandbox;
        public uint StreamSeed { get; private set; }

        void Awake()
        {
            if (health == null) health = GetComponent<BossHealth>();
            if (death == null) death = GetComponent<BossDeath>();
            if (sounds == null) sounds = GetComponent<BossAudio>();
            brain = GetComponent<IBossBrain>();
            if (data == null) Debug.LogError($"[Boss] '{name}' has no BossData. Run TWR > Run > Build Run Scenes.", this);
            if (brain == null) Debug.LogError($"[Boss] '{name}' has no boss brain (BullBrain / PreacherBrain).", this);
            // A stream always exists; Start replaces it with the run's stream once the RunManager has rolled this section.
            Stream = new SeededRandom(0u, SectionRollContext.StreamFor(0, 0, RollCategory.Boss));
        }

        void OnEnable()
        {
            Active = this;
            if (health == null) return;
            health.Damaged += HandleDamaged;
            health.PhaseChanged += HandlePhaseChanged;
            health.Died += HandleDied;
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
            if (health != null)
            {
                health.Damaged -= HandleDamaged;
                health.PhaseChanged -= HandlePhaseChanged;
                health.Died -= HandleDied;
            }
            BossEvents.RaiseDespawned(gameObject); // HUD bar hides (death finished or arena unloaded)
        }

        void Start()
        {
            // Once, never in Update (D-100). The run has exactly one player, in the Core scene.
            player = FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                playerHealth = player.GetComponent<IDamageable>();
                playerCamera = player.PlayerCamera != null ? player.PlayerCamera.transform : player.transform;
            }
            else Debug.LogWarning($"[Boss] '{name}' found no PlayerController: the fight can't start.", this);

            CreateStream();
            EnemyEvents.RaiseSpawned(gameObject); // RunStats sees the BossMarker and leaves it out (D-158)
            nextCheck = Time.time + 0.2f;
        }

        /// <summary>D-189: boss stream = SeededRandom(seed, 0x100 + branch x 0x40 + stop x 4 + 3).</summary>
        void CreateStream()
        {
            RunManager manager = RunManager.Instance;
            if (manager != null && manager.TryGetRollContext(gameObject.scene, out SectionRollContext context))
            {
                Stream = context.CreateRandom(RollCategory.Boss);
                StreamMode = context.Mode;
                StreamSeed = context.Seed;
                Debug.Log($"[Boss] '{BossId}': boss stream from seed {RunSeed.Format(context.Seed)}, stop {context.SectionIndex}, branch {context.Branch} " +
                          $"(stream 0x{SectionRollContext.StreamFor(context.Branch, context.SectionIndex, RollCategory.Boss):X}, {context.Mode}, D-189).", this);
            }
            else
            {
                StreamMode = SectionRollMode.Sandbox;
                Debug.Log($"[Boss] '{BossId}': no run context (sandbox or editor test): authored defaults (D-189).", this);
            }
        }

        void Update()
        {
            if (IsStarted || IsDefeated || player == null || Time.time < nextCheck) return;
            nextCheck = Time.time + 1f / (data != null ? data.decisionsPerSecond : 5f);
            if (PlayerAlive && fightArea.Contains(player.transform.position)) BeginFight("player entered the arena");
        }

        /// <summary>Start the fight once (entering the arena, first shot, or debug).</summary>
        public void BeginFight(string reason)
        {
            if (IsStarted || IsDefeated) return;
            IsStarted = true;
            Debug.Log($"[Boss] '{BossId}' fight started ({reason}).", this);
            BossEvents.RaiseStarted(gameObject, BossId);
            VoiceEvents.RaiseBossCue(BossId, "started");
            EnemyEvents.RaiseAwarenessChanged(gameObject, EnemyAwareness.Unaware, EnemyAwareness.Hostile); // combat music (D-193)
            brain?.BeginFight();
        }

        void HandleDamaged(DamageInfo damage)
        {
            if (!IsStarted) BeginFight("shot by the player");
            if (sounds != null && Time.time >= nextHurtSound)
            {
                nextHurtSound = Time.time + (data != null ? data.hurtSoundInterval : 0.35f);
                sounds.Play(BossSound.Hurt);
            }
        }

        void HandlePhaseChanged(int phase)
        {
            Debug.Log($"[Boss] '{BossId}' phase {phase} (D-184).", this);
            BossEvents.RaisePhaseChanged(gameObject, BossId, phase);
            VoiceEvents.RaiseBossCue(BossId, "phase" + phase);
            brain?.HandlePhaseChanged(phase);
        }

        void HandleDied(DamageInfo killingBlow)
        {
            if (IsDefeated) return;
            if (!IsStarted) BeginFight("killed before the fight started (debug)");
            IsDefeated = true;
            brain?.HandleDefeated();

            CombatEvents.RaiseKilled(gameObject, killingBlow);          // RunStats ignores bosses (D-158)
            BossEvents.RaiseDefeated(gameObject, BossId);               // exit gate opens (D-165)
            VoiceEvents.RaiseBossCue(BossId, "defeated");
            EnemyEvents.RaiseAwarenessChanged(gameObject, EnemyAwareness.Hostile, EnemyAwareness.Unaware);
            Debug.Log($"[Boss] '{BossId}' defeated: the arena exit opens (D-165, D-185).", this);

            if (sounds != null)
            {
                sounds.StopIdle();
                sounds.Play(BossSound.Death);
            }
            if (death != null) death.Begin();
            else gameObject.SetActive(false);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(fightArea.center, fightArea.size);
        }
    }
}
