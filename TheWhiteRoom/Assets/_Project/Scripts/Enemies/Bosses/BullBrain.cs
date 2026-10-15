using UnityEngine;
using UnityEngine.AI;
using TWR.Combat;
using TWR.Core;
using TWR.Core.Perception;
using TWR.Player;
using TWR.Run;

namespace TWR.Enemies
{
    /// <summary>
    /// The Bull's brain, Boss 2 of the Escape path (D-048, D-181, D-182, D-189, D-190). A small state machine:
    /// Reposition (walks towards the player on the NavMesh) -> Telegraph (about 1 s: paws, roars, lowers its head; a FAST
    /// charge flashes red and roars higher, D-182) -> Charge (straight line; the NavMeshAgent is off and the body is moved
    /// by hand, one box sweep per frame against walls, columns and the player - no scene searches) ->
    /// - hits the player: damage ONCE (fast charges also knock the player back up to 20 m, D-182), then a ~3 m skid, no stun;
    /// - hits a wall or column: Stun (3 s, takes x2 damage, D-181); breakable columns count the hit and break on the 2nd;
    /// - reaches its max distance: skid -> Recover.
    /// Close to the player it Stomps instead (telegraphed rear-up, 4 m radius). Charges and stomps are attacks; after
    /// 2-5 of them (FastChargeSchedule, counts from the D-189 boss stream; seed 0 / sandbox = authored sequence) the next
    /// charge is fast. Sight checks run 5 times a second (D-193) with the shared ViewCone line-of-sight helper.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(BossEncounter))]
    public class BullBrain : MonoBehaviour, IBossBrain
    {
        enum State { Idle, Reposition, Telegraph, Charge, Skid, Stun, StompWindUp, Recover, Dead }

        const float SkinDistance = 0.05f;

        [SerializeField] BullData data;
        [SerializeField] BossEncounter encounter;
        [SerializeField] BossHealth health;
        [SerializeField] BullVisuals visuals;
        [SerializeField] BossAudio sounds;
        [Tooltip("Sight point (head height, facing forward).")]
        [SerializeField] Transform eye;

        NavMeshAgent agent;
        FastChargeSchedule schedule;
        PlayerLocomotion playerLocomotion;
        readonly RaycastHit[] hits = new RaycastHit[16];
        int environmentMask;
        int sweepMask;
        int playerLayer;

        State state = State.Idle;
        float stateTime;
        float nextDecision;
        float nextRepath;
        float stompReadyTime;
        bool canSeePlayer;
        float playerDistance;

        bool currentFast;
        Vector3 chargeDirection;
        float chargeTravelled;
        float moveSpeed;
        float skidDeceleration;
        float skidRemaining;
        bool hitPlayerThisCharge;

        int stuns;
        int playerHits;
        string lastEvent = "-";

        public FastChargeSchedule Schedule => schedule;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (encounter == null) encounter = GetComponent<BossEncounter>();
            if (health == null) health = GetComponent<BossHealth>();
            if (visuals == null) visuals = GetComponent<BullVisuals>();
            if (sounds == null) sounds = GetComponent<BossAudio>();
            if (data == null)
            {
                Debug.LogError($"[Boss] Bull '{name}' has no BullData. Run TWR > Run > Build Run Scenes.", this);
                enabled = false;
                return;
            }
            environmentMask = GameLayers.WithoutActors(data.environmentMask);
            playerLayer = GameLayers.PlayerLayer;
            sweepMask = environmentMask | (playerLayer >= 0 ? 1 << playerLayer : 0);

            agent.speed = data.walkSpeed;
            agent.angularSpeed = data.angularSpeed;
            agent.acceleration = 20f;
            agent.stoppingDistance = 2.5f;
            agent.autoBraking = true;
        }

        void Start()
        {
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            if (!agent.isOnNavMesh)
                Debug.LogError($"[Boss] Bull '{name}' is not on a NavMesh. Rebuild the run scenes (TWR > Run > Build Run Scenes).", this);
            EnsureSchedule();
        }

        /// <summary>BossEncounter (order -10) has made the boss stream by now. Seeded runs roll counts; seed 0 / sandbox use the authored list.</summary>
        void EnsureSchedule()
        {
            if (schedule != null) return;
            SeededRandom stream = encounter != null && encounter.StreamMode == SectionRollMode.Seeded ? encounter.Stream : null;
            schedule = new FastChargeSchedule(stream, data.fastChargeCountMin, data.fastChargeCountMax, data.curatedFastChargeCounts);
            Debug.Log($"[Boss] Bull fast-charge schedule: {(schedule.UsesStream ? "rolled from the seed" : "authored sequence")}, first target {schedule.Target} attacks (D-189).", this);
        }

        // ---------- IBossBrain ----------

        public void BeginFight()
        {
            EnsureSchedule();
            if (encounter.Player != null) playerLocomotion = encounter.Player.Locomotion;
            stompReadyTime = Time.time + 1f;
            Enter(State.Reposition);
        }

        public void HandlePhaseChanged(int phase)
        {
            lastEvent = "phase " + phase; // the Bull fights the same in both phases (D-181)
        }

        public void HandleDefeated()
        {
            health.DamageMultiplier = 1f;
            StopAgent();
            Enter(State.Dead);
        }

        /// <summary>F1 debug (D-185): the next charge is fast, without changing the seed's count sequence.</summary>
        public void ForceNextChargeFast()
        {
            EnsureSchedule();
            schedule.ForceNext = true;
            Debug.Log("[Boss] Debug: the Bull's next charge will be FAST (count and seed sequence unchanged).", this);
        }

        public string DebugState
        {
            get
            {
                if (schedule == null) return $"Bull: {state}";
                string next = schedule.NextChargeIsFast ? "FAST" : "normal";
                return $"Bull: <b>{state}</b>{(state == State.Stun ? $" (x{data.stunnedDamageMultiplier} damage)" : "")}   seen: {(canSeePlayer ? "yes" : "no")}, {playerDistance:0} m\n" +
                       $"attacks since fast {schedule.AttacksSinceFast} / target {schedule.Target} -> next charge {next}" +
                       $"{(schedule.ForceNext ? " (forced)" : "")} | counts: {(schedule.UsesStream ? "seed" : "authored")}\n" +
                       $"fast charges {schedule.FastCharges}, stuns {stuns}, player hits {playerHits}, last: {lastEvent}";
            }
        }

        // ---------- update ----------

        void Update()
        {
            if (state == State.Dead || state == State.Idle) return;
            float dt = Time.deltaTime;
            stateTime += dt;
            if (Time.time >= nextDecision)
            {
                nextDecision = Time.time + 1f / data.decisionsPerSecond;
                Sense();
            }

            switch (state)
            {
                case State.Reposition: UpdateReposition(); break;
                case State.Telegraph: UpdateTelegraph(dt); break;
                case State.Charge: UpdateCharge(dt); break;
                case State.Skid: UpdateSkid(dt); break;
                case State.Stun: UpdateStun(); break;
                case State.StompWindUp: UpdateStomp(dt); break;
                case State.Recover: UpdateRecover(); break;
            }
            Animate();
        }

        /// 5 Hz: distance and line of sight to the player's chest (D-193).
        void Sense()
        {
            Transform player = encounter.PlayerTransform;
            if (player == null || !encounter.PlayerAlive)
            {
                canSeePlayer = false;
                return;
            }
            Vector3 flat = player.position - transform.position;
            flat.y = 0f;
            playerDistance = flat.magnitude;
            Vector3 from = eye != null ? eye.position : transform.position + Vector3.up * 2f;
            canSeePlayer = ViewCone.HasLineOfSight(from, PlayerChest(), environmentMask);
        }

        Vector3 PlayerChest() => encounter.PlayerTransform.position + Vector3.up * 1.2f;

        void UpdateReposition()
        {
            if (!encounter.PlayerAlive)
            {
                StopAgent();
                return;
            }
            if (playerDistance <= data.stompTriggerDistance && Time.time >= stompReadyTime)
            {
                StartStomp();
                return;
            }
            if (canSeePlayer && playerDistance >= data.minChargeDistance && playerDistance <= data.maxChargeStartDistance)
            {
                StartTelegraph();
                return;
            }
            if (Time.time >= nextRepath && agent.enabled && agent.isOnNavMesh)
            {
                nextRepath = Time.time + 1f / data.repathsPerSecond;
                agent.isStopped = false;
                agent.SetDestination(encounter.PlayerTransform.position);
            }
        }

        // ---------- charge (D-181, D-182, D-190) ----------

        void StartTelegraph()
        {
            StopAgent();
            currentFast = schedule.BeginCharge(); // a charge starts now: counts as an attack (D-189)
            if (sounds != null) sounds.Play(currentFast ? BossSound.FastTelegraph : BossSound.Telegraph);
            lastEvent = currentFast ? "FAST charge telegraph" : "charge telegraph";
            Enter(State.Telegraph);
        }

        float TelegraphSeconds => currentFast ? data.fastTelegraphSeconds : data.chargeTelegraphSeconds;

        void UpdateTelegraph(float dt)
        {
            float t = stateTime / TelegraphSeconds;
            if (t < data.aimLockFraction && encounter.PlayerTransform != null)
                TurnTowards(encounter.PlayerTransform.position, data.aimTurnSpeed * dt);
            if (t >= 1f) StartCharge();
        }

        void StartCharge()
        {
            chargeDirection = transform.forward;
            chargeDirection.y = 0f;
            chargeDirection = chargeDirection.sqrMagnitude > 0.001f ? chargeDirection.normalized : Vector3.forward;
            chargeTravelled = 0f;
            hitPlayerThisCharge = false;
            moveSpeed = currentFast ? data.fastChargeSpeed : data.chargeSpeed;
            agent.enabled = false; // moved by hand while charging; back on at Recover
            if (sounds != null) sounds.Play(BossSound.Charge);
            Enter(State.Charge);
        }

        void UpdateCharge(float dt)
        {
            float step = moveSpeed * dt;
            if (Sweep(step, out RaycastHit hit, out bool isPlayer))
            {
                MoveForward(Mathf.Max(0f, hit.distance - SkinDistance));
                if (isPlayer)
                {
                    HitPlayer(hit);
                    StartSkid();
                }
                else HitObstacle(hit.collider);
                return;
            }
            MoveForward(step);
            chargeTravelled += step;
            if (chargeTravelled >= data.maxChargeDistance) StartSkid();
        }

        void HitPlayer(in RaycastHit hit)
        {
            if (hitPlayerThisCharge) return; // never a double hit (D-190)
            hitPlayerThisCharge = true;
            playerHits++;
            IDamageable target = encounter.PlayerHealth;
            if (target == null || !target.IsAlive) return;

            float damage = currentFast ? data.fastChargeDamage : data.chargeDamage;
            target.TakeDamage(new DamageInfo(damage, gameObject, PlayerChest(), chargeDirection, null));
            lastEvent = $"{(currentFast ? "FAST " : "")}charge hit the player ({damage:0})";
            // D-182: only a fast charge knocks back; through the motor, never a teleport (D-113, D-191). No extra damage.
            if (currentFast && target.IsAlive && playerLocomotion != null)
                playerLocomotion.ApplyKnockback(chargeDirection, data.knockbackDistance);
        }

        void HitObstacle(Collider obstacle)
        {
            BullColumn column = obstacle != null ? obstacle.GetComponentInParent<BullColumn>() : null; // once per impact
            if (column != null)
            {
                bool broke = column.RegisterChargeHit(data.columnHitsToBreak);
                lastEvent = $"hit column '{column.ColumnId}'{(column.Breakable ? $" ({column.Hits} hit(s){(broke ? ", BROKE" : "")})" : " (unbreakable)")}";
            }
            else lastEvent = "hit a wall";
            if (sounds != null) sounds.Play(BossSound.Impact);
            StartStun();
        }

        void StartSkid()
        {
            float distance = Mathf.Max(0.01f, data.skidDistance);
            skidRemaining = distance;
            skidDeceleration = moveSpeed * moveSpeed / (2f * distance); // stops after exactly skidDistance
            Enter(State.Skid);
        }

        void UpdateSkid(float dt)
        {
            moveSpeed = Mathf.Max(0f, moveSpeed - skidDeceleration * dt);
            float step = Mathf.Min(moveSpeed * dt, skidRemaining);
            if (step > 0f && Sweep(step, out RaycastHit hit, out _))
            {
                // Skids stop at anything (player included) without damage or stun (D-190).
                MoveForward(Mathf.Max(0f, hit.distance - SkinDistance));
                EnterRecover();
                return;
            }
            MoveForward(step);
            skidRemaining -= step;
            if (skidRemaining <= 0.001f || moveSpeed <= 0.05f) EnterRecover();
        }

        /// <summary>
        /// One box sweep along the charge direction against walls, columns and the player (NonAlloc, D-100). Returns the
        /// nearest hit. Walls already touching at the start (distance 0) are ignored so the Bull can't get stuck.
        /// </summary>
        bool Sweep(float distance, out RaycastHit nearest, out bool isPlayer)
        {
            nearest = default;
            isPlayer = false;
            Vector3 centre = transform.position + Vector3.up * data.chargeSweepHeight;
            Quaternion orientation = Quaternion.LookRotation(chargeDirection);
            int count = Physics.BoxCastNonAlloc(centre, data.chargeHalfExtents, chargeDirection, hits, orientation, distance,
                                                sweepMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null) continue;
                bool player = c.gameObject.layer == playerLayer;
                if (!player && hits[i].distance <= 0f) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    nearest = hits[i];
                    isPlayer = player;
                    found = true;
                }
            }
            return found;
        }

        void MoveForward(float distance)
        {
            if (distance > 0f) transform.position += chargeDirection * distance;
        }

        // ---------- stun (D-181, D-192) ----------

        void StartStun()
        {
            stuns++;
            health.DamageMultiplier = data.stunnedDamageMultiplier;
            Enter(State.Stun);
        }

        void UpdateStun()
        {
            if (stateTime < data.stunSeconds) return;
            health.DamageMultiplier = 1f;
            EnterRecover();
        }

        // ---------- stomp (D-181, D-192) ----------

        void StartStomp()
        {
            StopAgent();
            stompReadyTime = Time.time + data.stompCooldown;
            if (sounds != null) sounds.Play(BossSound.Telegraph);
            lastEvent = "stomp wind-up";
            Enter(State.StompWindUp);
        }

        void UpdateStomp(float dt)
        {
            if (encounter.PlayerTransform != null) TurnTowards(encounter.PlayerTransform.position, data.aimTurnSpeed * dt);
            if (stateTime < data.stompWindUpSeconds) return;

            schedule.RegisterStomp(); // stomps count as attacks (D-189)
            if (sounds != null) sounds.Play(BossSound.Stomp);
            Transform player = encounter.PlayerTransform;
            IDamageable target = encounter.PlayerHealth;
            if (player != null && target != null && target.IsAlive)
            {
                Vector3 offset = player.position - transform.position;
                float dy = Mathf.Abs(offset.y);
                offset.y = 0f;
                if (offset.magnitude <= data.stompRadius && dy <= data.stompMaxHeightDifference)
                {
                    target.TakeDamage(new DamageInfo(data.stompDamage, gameObject, PlayerChest(), offset.normalized, null));
                    playerHits++;
                    lastEvent = $"stomp hit the player ({data.stompDamage:0})";
                }
                else lastEvent = "stomp missed";
            }
            EnterRecover();
        }

        // ---------- helpers ----------

        void EnterRecover()
        {
            if (!agent.enabled)
            {
                agent.enabled = true;
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas)) agent.Warp(hit.position);
            }
            StopAgent();
            Enter(State.Recover);
        }

        void UpdateRecover()
        {
            if (stateTime >= data.recoverSeconds) Enter(State.Reposition);
        }

        void StopAgent()
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }

        void TurnTowards(Vector3 target, float maxDegrees)
        {
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), maxDegrees);
        }

        void Enter(State next)
        {
            state = next;
            stateTime = 0f;
        }

        void Animate()
        {
            if (visuals == null) return;
            switch (state)
            {
                case State.Reposition:
                    visuals.Apply(BullPose.Walk, 0f, false, agent.enabled ? agent.velocity.magnitude : 0f);
                    break;
                case State.Telegraph: visuals.Apply(BullPose.Telegraph, stateTime / TelegraphSeconds, currentFast, 0f); break;
                case State.Charge: visuals.Apply(BullPose.Charge, 0f, currentFast, moveSpeed); break;
                case State.Skid: visuals.Apply(BullPose.Skid, 0f, false, 0f); break;
                case State.Stun: visuals.Apply(BullPose.Stun, stateTime / Mathf.Max(0.01f, data.stunSeconds), false, 0f); break;
                case State.StompWindUp: visuals.Apply(BullPose.StompWindUp, stateTime / data.stompWindUpSeconds, false, 0f); break;
                case State.Recover: visuals.Apply(BullPose.Recover, stateTime / Mathf.Max(0.01f, data.recoverSeconds), false, 0f); break;
                default: visuals.Apply(BullPose.Idle, 0f, false, 0f); break;
            }
        }
    }
}
