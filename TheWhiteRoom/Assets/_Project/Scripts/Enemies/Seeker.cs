using System;
using UnityEngine;
using UnityEngine.AI;
using TWR.Combat;
using TWR.Core;

namespace TWR.Enemies
{
    /// <summary>
    /// The Seeker's brain (D-043: patrols, reacts to sound and movement, melee at close range).
    /// A simple state machine (D-133): Patrol -> Suspicious (small noise: stop and turn, 1 s) ->
    /// Investigate (loud noise or a second small one: walk over, look around 4 s) -> Chase (seen, noise within 3 m,
    /// or shot) -> Attack (melee, SeekerMelee) -> Search (5 s without sight: last known position, then random
    /// NavMesh points within 6 m for 8 s) -> Return (nearest waypoint) -> Patrol.
    /// It moves with a NavMeshAgent (D-135; can't open doors), re-paths 4-5 times a second while chasing,
    /// and raises EnemyEvents.AwarenessChanged for music/voice. Senses, melee, health, visuals and audio are
    /// separate components; this one only decides. No allocations per frame (D-100).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(SeekerSenses), typeof(SeekerHealth))]
    public class Seeker : MonoBehaviour
    {
        [SerializeField] SeekerData data;
        [SerializeField] SeekerSenses senses;
        [SerializeField] SeekerHealth health;
        [SerializeField] SeekerMelee melee;
        [SerializeField] SeekerVisuals visuals;
        [SerializeField] SeekerAudio sounds;
        [Tooltip("Usually set by SeekerSpawnPoint. Empty = stand guard where it spawned.")]
        [SerializeField] Transform[] patrolRoute = new Transform[0];
        [Tooltip("Testing: write every state change to the Console.")]
        [SerializeField] bool logStateChanges = true;

        NavMeshAgent agent;
        NavMeshPath scratchPath;

        SeekerState state = SeekerState.Patrol;
        EnemyAwareness awareness = EnemyAwareness.Unaware;
        float stateTime;

        int waypointIndex;
        bool pausing;
        float pauseUntil;
        Vector3 guardPosition;
        Quaternion guardRotation;

        Vector3 noisePosition;      // Suspicious / Investigate target
        float noiseRadius;          // raw radius of that noise (a gunshot outranks its own bullet impact)
        Vector3 lastKnownPosition;  // Chase / Search target
        float lastContactTime;      // last time it saw (or was shot by) the player
        float nextRepathTime;

        bool lookingAround;         // Investigate: arrived and scanning
        float lookAroundStart;
        float lookAroundBaseYaw;

        bool searchArrived;         // Search: reached the last known position
        float searchStart;
        float searchWaitUntil;
        bool searchWaiting;

        SeekerSenses.NoiseHeardHandler onNoise;

        public SeekerState State => state;
        public SeekerData Data => data;
        public Vector3 LastKnownPosition => lastKnownPosition;

        /// <summary>(from, to). Every state change, for debug tools and future animation.</summary>
        public event Action<SeekerState, SeekerState> StateChanged;

        /// <summary>Give the Seeker its waypoints (SeekerSpawnPoint calls this right after spawning).</summary>
        public void SetPatrolRoute(Transform[] route)
        {
            patrolRoute = route ?? new Transform[0];
            waypointIndex = 0;
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (senses == null) senses = GetComponent<SeekerSenses>();
            if (health == null) health = GetComponent<SeekerHealth>();
            if (melee == null) melee = GetComponent<SeekerMelee>();
            if (visuals == null) visuals = GetComponent<SeekerVisuals>();
            if (sounds == null) sounds = GetComponent<SeekerAudio>();
            scratchPath = new NavMeshPath(); // reused for every search-point check
            onNoise = HandleNoise;

            if (data == null)
            {
                Debug.LogError($"[Seeker] '{name}' has no SeekerData. Run TWR > Build Test Scene.", this);
                enabled = false;
                return;
            }
            agent.radius = data.agentRadius;
            agent.height = data.agentHeight;
            agent.angularSpeed = data.angularSpeed;
            agent.acceleration = data.acceleration;
            agent.autoBraking = true;
        }

        void OnEnable()
        {
            if (senses != null)
            {
                senses.NoiseHeard += onNoise;
                senses.PlayerSpotted += HandlePlayerSpotted;
            }
            if (health != null)
            {
                health.Damaged += HandleDamaged;
                health.DeathStarted += HandleDeath;
            }
        }

        void OnDisable()
        {
            if (senses != null)
            {
                senses.NoiseHeard -= onNoise;
                senses.PlayerSpotted -= HandlePlayerSpotted;
            }
            if (health != null)
            {
                health.Damaged -= HandleDamaged;
                health.DeathStarted -= HandleDeath;
            }
        }

        void Start()
        {
            // Make sure we stand on the NavMesh (spawn points may be a little off the floor).
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            if (!agent.isOnNavMesh)
                Debug.LogError($"[Seeker] '{name}' is not on a NavMesh. Rebuild the test scene (TWR > Build Test Scene).", this);

            guardPosition = transform.position;
            guardRotation = transform.rotation;
            EnemyEvents.RaiseSpawned(gameObject);
            EnterState(SeekerState.Patrol, true);
        }

        void Update()
        {
            if (!agent.isOnNavMesh) return;
            stateTime += Time.deltaTime;

            switch (state)
            {
                case SeekerState.Patrol:      UpdatePatrol(); break;
                case SeekerState.Suspicious:  UpdateSuspicious(); break;
                case SeekerState.Investigate: UpdateInvestigate(); break;
                case SeekerState.Chase:       UpdateChase(); break;
                case SeekerState.Attack:      UpdateAttack(); break;
                case SeekerState.Search:      UpdateSearch(); break;
                case SeekerState.Return:      UpdateReturn(); break;
            }
        }

        // ---------- states ----------

        void UpdatePatrol()
        {
            if (!HasRoute)
            {
                // Guard post: go back to the spawn spot and face the original way.
                if (Arrived()) TurnTowards(transform.position + guardRotation * Vector3.forward);
                return;
            }

            if (pausing)
            {
                if (Time.time < pauseUntil) return;
                pausing = false;
                waypointIndex = (waypointIndex + 1) % patrolRoute.Length;
                MoveTo(CurrentWaypoint(), data.patrolSpeed);
                return;
            }

            if (Arrived())
            {
                pausing = true;
                pauseUntil = Time.time + UnityEngine.Random.Range(data.waypointPauseMin, data.waypointPauseMax);
                StopMoving();
            }
        }

        void UpdateSuspicious()
        {
            TurnTowards(noisePosition);
            if (stateTime >= data.suspiciousSeconds) EnterState(SeekerState.Patrol);
        }

        void UpdateInvestigate()
        {
            if (!lookingAround)
            {
                if (!Arrived()) return;
                lookingAround = true;
                lookAroundStart = Time.time;
                lookAroundBaseYaw = transform.eulerAngles.y;
                StopMoving();
            }

            float t = Time.time - lookAroundStart;
            float yaw = lookAroundBaseYaw + Mathf.Sin(t * 1.6f) * data.lookAroundAngle;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, yaw, 0f), data.turnSpeed * Time.deltaTime);
            if (t >= data.lookAroundSeconds) EnterState(SeekerState.Return);
        }

        void UpdateChase()
        {
            if (!senses.PlayerAlive)
            {
                EnterState(SeekerState.Return);
                return;
            }

            Transform player = senses.PlayerTransform;
            if (senses.CanSeePlayer) lastContactTime = Time.time;

            // Shortly after losing sight it still tracks the real position (rounding a corner), then the last known one.
            bool tracking = senses.CanSeePlayer || Time.time - lastContactTime <= data.chaseMemorySeconds;
            if (tracking && player != null) lastKnownPosition = player.position;

            if (Time.time >= nextRepathTime)
            {
                nextRepathTime = Time.time + 1f / Mathf.Max(0.5f, data.chaseRepathsPerSecond);
                agent.stoppingDistance = data.chaseStopDistance;
                MoveTo(lastKnownPosition, data.chaseSpeed);
            }

            if (player != null && senses.CanSeePlayer && melee != null && melee.IsReady
                && FlatDistance(player.position) <= data.attackStartDistance)
            {
                EnterState(SeekerState.Attack);
                return;
            }

            if (Time.time - lastContactTime >= data.loseSightSeconds)
                EnterState(SeekerState.Search);
        }

        void UpdateAttack()
        {
            Transform player = senses.PlayerTransform;
            if (player != null) TurnTowards(player.position);

            if (melee == null || !melee.IsWindingUp)
                EnterState(senses.PlayerAlive ? SeekerState.Chase : SeekerState.Return);
        }

        void UpdateSearch()
        {
            if (senses.CanSeePlayer)
            {
                BeginChase(senses.LastSeenPosition);
                return;
            }

            if (!searchArrived)
            {
                // Safety: if the last known position can't be reached, start searching after a while anyway.
                if (!Arrived() && stateTime < 10f) return;
                searchArrived = true;
                searchStart = Time.time;
                BeginSearchPause();
            }

            if (Time.time - searchStart >= data.searchSeconds)
            {
                EnterState(SeekerState.Return);
                return;
            }

            if (searchWaiting)
            {
                TurnTowards(transform.position + Quaternion.Euler(0f, 120f * Time.deltaTime, 0f) * transform.forward);
                if (Time.time >= searchWaitUntil)
                {
                    searchWaiting = false;
                    GoToRandomSearchPoint();
                }
            }
            else if (Arrived())
            {
                BeginSearchPause();
            }
        }

        void UpdateReturn()
        {
            if (Arrived()) EnterState(SeekerState.Patrol);
        }

        // ---------- events from senses / health ----------

        void HandleNoise(in HeardNoise heard, NoiseReaction reaction)
        {
            Vector3 position = heard.Noise.Position;
            switch (state)
            {
                case SeekerState.Dead:
                    return;
                case SeekerState.Chase:
                case SeekerState.Attack:
                    lastKnownPosition = position; // already hunting: just a better guess where you are
                    return;
            }

            if (heard.Distance <= data.closeNoiseChaseDistance)
            {
                BeginChase(position);
                return;
            }

            switch (state)
            {
                case SeekerState.Patrol:
                    if (reaction == NoiseReaction.Investigate) BeginInvestigate(position, heard.Noise.Radius);
                    else BeginSuspicious(position);
                    break;
                case SeekerState.Investigate:
                    // One trigger pull makes a Gunshot AND an Impact in the same frame: keep the louder one,
                    // so it investigates the shooter, not where the bullet landed.
                    if (stateTime < 0.5f && heard.Noise.Radius < noiseRadius) return;
                    BeginInvestigate(position, heard.Noise.Radius);
                    break;
                default:
                    // Suspicious + a second noise escalates; Search/Return re-target to the new noise.
                    BeginInvestigate(position, heard.Noise.Radius);
                    break;
            }
        }

        void HandlePlayerSpotted()
        {
            if (state == SeekerState.Chase || state == SeekerState.Attack || state == SeekerState.Dead) return;
            BeginChase(senses.LastSeenPosition);
        }

        void HandleDamaged(DamageInfo damage)
        {
            if (visuals != null) visuals.FlashHurt();
            if (sounds != null) sounds.PlayHurt();
            if (state == SeekerState.Dead) return;

            Vector3 position = damage.Attacker != null ? damage.Attacker.transform.position : damage.Point;
            lastContactTime = Time.time;
            if (state == SeekerState.Chase || state == SeekerState.Attack)
            {
                lastKnownPosition = position;
                return;
            }
            BeginChase(position); // shot: chase whoever did it
        }

        void HandleDeath(DamageInfo killingBlow)
        {
            EnterState(SeekerState.Dead);
            enabled = false; // SeekerDeath turns off the agent and colliders
        }

        // ---------- transitions ----------

        void BeginSuspicious(Vector3 position)
        {
            noisePosition = position;
            EnterState(SeekerState.Suspicious);
        }

        void BeginInvestigate(Vector3 position, float radius)
        {
            noisePosition = position;
            noiseRadius = radius;
            EnterState(SeekerState.Investigate, state == SeekerState.Investigate); // force restart on re-target
        }

        void BeginChase(Vector3 position)
        {
            lastKnownPosition = position;
            lastContactTime = Time.time;
            EnterState(SeekerState.Chase);
        }

        void EnterState(SeekerState next, bool force = false)
        {
            if (next == state && !force) return;
            SeekerState previous = state;
            state = next;
            stateTime = 0f;
            if (agent.enabled) agent.updateRotation = true;
            senses.Tracking = next == SeekerState.Chase || next == SeekerState.Attack;
            if (agent.isOnNavMesh) agent.stoppingDistance = next == SeekerState.Chase ? data.chaseStopDistance : 0.1f;

            switch (next)
            {
                case SeekerState.Patrol:
                    pausing = false;
                    senses.ResetAwareness();
                    if (HasRoute) MoveTo(CurrentWaypoint(), data.patrolSpeed);
                    else MoveTo(guardPosition, data.patrolSpeed);
                    break;
                case SeekerState.Suspicious:
                    StopMoving();
                    break;
                case SeekerState.Investigate:
                    lookingAround = false;
                    MoveTo(noisePosition, data.investigateSpeed);
                    break;
                case SeekerState.Chase:
                    nextRepathTime = 0f;
                    if (previous != SeekerState.Attack && sounds != null) sounds.PlayAlert();
                    break;
                case SeekerState.Attack:
                    StopMoving();
                    if (melee == null || !melee.TryStartAttack(senses.PlayerTransform, senses.PlayerHealth))
                        state = SeekerState.Chase; // couldn't attack after all (cooldown): keep chasing
                    break;
                case SeekerState.Search:
                    searchArrived = false;
                    searchWaiting = false;
                    MoveTo(lastKnownPosition, data.searchSpeed);
                    break;
                case SeekerState.Return:
                    if (HasRoute)
                    {
                        waypointIndex = NearestWaypoint();
                        MoveTo(CurrentWaypoint(), data.returnSpeed);
                    }
                    else MoveTo(guardPosition, data.returnSpeed);
                    break;
                case SeekerState.Dead:
                    if (melee != null) melee.Cancel();
                    break;
            }

            if (state == previous && !force) return; // Attack fell back to Chase
            if (visuals != null) visuals.ShowState(state);
            UpdateAwareness();
            if (logStateChanges) Debug.Log($"[Seeker] '{name}' {previous} -> {state}", this);
            StateChanged?.Invoke(previous, state);
        }

        void UpdateAwareness()
        {
            EnemyAwareness next;
            switch (state)
            {
                case SeekerState.Suspicious: next = EnemyAwareness.Suspicious; break;
                case SeekerState.Investigate:
                case SeekerState.Search:     next = EnemyAwareness.Alerted; break;
                case SeekerState.Chase:
                case SeekerState.Attack:     next = EnemyAwareness.Hostile; break;
                default:                     next = EnemyAwareness.Unaware; break;
            }
            if (next == awareness) return;
            EnemyAwareness previous = awareness;
            awareness = next;
            EnemyEvents.RaiseAwarenessChanged(gameObject, previous, next);
        }

        // ---------- movement helpers ----------

        bool HasRoute => patrolRoute != null && patrolRoute.Length > 0;

        Vector3 CurrentWaypoint()
        {
            Transform w = patrolRoute[Mathf.Clamp(waypointIndex, 0, patrolRoute.Length - 1)];
            return w != null ? w.position : guardPosition;
        }

        int NearestWaypoint()
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < patrolRoute.Length; i++)
            {
                if (patrolRoute[i] == null) continue;
                float d = (patrolRoute[i].position - transform.position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        void MoveTo(Vector3 destination, float speed)
        {
            if (!agent.isOnNavMesh) return;
            agent.speed = speed;
            agent.isStopped = false;
            agent.updateRotation = true;
            if (!agent.SetDestination(destination)
                && NavMesh.SamplePosition(destination, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
        }

        void StopMoving()
        {
            if (!agent.isOnNavMesh) return;
            agent.isStopped = true;
            agent.ResetPath();
            agent.updateRotation = false; // we turn by hand while standing
        }

        /// Reached the end of the current path (also true when there is no usable path, so states never hang).
        bool Arrived()
        {
            if (agent.pathPending) return false;
            if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid) return true;
            return agent.remainingDistance <= agent.stoppingDistance + data.arriveDistance;
        }

        void TurnTowards(Vector3 point)
        {
            Vector3 flat = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return;
            agent.updateRotation = false;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), data.turnSpeed * Time.deltaTime);
        }

        float FlatDistance(Vector3 point) => Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude;

        void BeginSearchPause()
        {
            StopMoving();
            searchWaiting = true;
            searchWaitUntil = Time.time + data.searchPointPause;
        }

        /// Random reachable NavMesh point within searchRadius of the last known position (a few tries).
        void GoToRandomSearchPoint()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * data.searchRadius;
                Vector3 candidate = lastKnownPosition + new Vector3(offset.x, 0f, offset.y);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                if (!agent.CalculatePath(hit.position, scratchPath) || scratchPath.status != NavMeshPathStatus.PathComplete) continue;

                agent.speed = data.searchSpeed;
                agent.isStopped = false;
                agent.updateRotation = true;
                agent.SetPath(scratchPath);
                return;
            }
            BeginSearchPause(); // nothing found this time: look around and try again
        }

        void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
            Color c;
            switch (state)
            {
                case SeekerState.Suspicious:  c = Color.yellow; break;
                case SeekerState.Investigate: c = new Color(1f, 0.5f, 0f); break;
                case SeekerState.Chase:
                case SeekerState.Attack:      c = Color.red; break;
                case SeekerState.Search:      c = new Color(0.6f, 0.2f, 1f); break;
                case SeekerState.Return:      c = new Color(0.55f, 0.75f, 1f); break;
                default:                      c = Color.white; break;
            }
            UnityEditor.Handles.color = c;
            string label = state.ToString();
            if (senses != null) label += $"  sight {senses.Awareness01:0.0}";
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.4f, label);

            if (state == SeekerState.Chase || state == SeekerState.Search)
            {
                Gizmos.color = c;
                Gizmos.DrawWireSphere(lastKnownPosition, 0.3f);
            }
            else if (state == SeekerState.Suspicious || state == SeekerState.Investigate)
            {
                Gizmos.color = c;
                Gizmos.DrawWireSphere(noisePosition, 0.3f);
            }
#endif
        }
    }
}
