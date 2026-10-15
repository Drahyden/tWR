using System.Collections.Generic;
using UnityEngine;
using TWR.Core;
using TWR.Core.Perception;

namespace TWR.Enemies
{
    /// <summary>
    /// The Preacher's brain, Boss 1 of the Discover path (D-047, D-184, D-187, D-189). It never walks (no NavMesh use):
    /// - Teleports between dark cavities (PreacherCavity, D-065) every 6 s (phase 2: 3.5 s). First a ~1 s telegraph AT THE
    ///   DESTINATION: a flickering white silhouette and a 3D hum there (D-184); then it vanishes and appears there.
    ///   It never fires during the telegraph or for 1 s after arriving (D-187).
    /// - Fair destinations only (D-187), re-checked 5 times a second from a pre-sorted cavity list (D-193): within 40 m
    ///   horizontally and +-20 m vertically of the player and not on top of them. In phase 2 it may pick a cavity BEHIND
    ///   the player: outside the player's view cone (shared ViewCone helper, D-132/D-045/D-053), within +-5 m height and at
    ///   least 6 m away. If no fair cavity exists it stays put, so it never teleports out of reach.
    /// - Volleys of 3-5 slow projectiles (pooled, BossProjectilePool) aimed at the player's chest with a small fan; they
    ///   are dodgeable and stopped by platforms and cover. Only fires with line of sight.
    /// Teleport picks and volley sizes come from the D-189 boss stream (PreacherTeleportPlanner), so the same seed and the
    /// same player behaviour give the same choices.
    /// </summary>
    [RequireComponent(typeof(BossEncounter))]
    public class PreacherBrain : MonoBehaviour, IBossBrain
    {
        enum State { Idle, Present, Telegraph, Dead }

        [SerializeField] PreacherData data;
        [SerializeField] BossEncounter encounter;
        [SerializeField] BossHealth health;
        [SerializeField] BossAudio sounds;
        [SerializeField] PreacherVisuals visuals;
        [Tooltip("Where projectiles leave (between the raised hands).")]
        [SerializeField] Transform hands;
        [SerializeField] BossProjectilePool projectiles;
        [Tooltip("Flickering silhouette shown at the destination during the telegraph (not a child of the Preacher).")]
        [SerializeField] Transform telegraphGhost;
        [SerializeField] Renderer[] ghostRenderers = new Renderer[0];
        [Tooltip("3D source on the ghost: the telegraph sound comes from the destination.")]
        [SerializeField] AudioSource ghostAudio;
        [Tooltip("Every cavity of the arena (sorted by id at runtime).")]
        [SerializeField] PreacherCavity[] cavities = new PreacherCavity[0];

        readonly List<int> candidates = new List<int>(32);
        readonly List<int> behind = new List<int>(32);
        PreacherCavity[] sorted;
        int environmentMask;

        State state = State.Idle;
        float stateTime;
        int current = -1;
        int target = -1;
        float nextCheck;
        float nextTeleportTime;
        float fireAllowedTime;
        float nextVolleyTime;
        float nextShotTime;
        float windUpEnd;
        bool windingUp;
        int shotsLeft;
        int shotIndex;
        bool canSeePlayer;
        float playerDistance;
        int teleports;
        bool lastPickBehind;

        public int CavityCount => sorted != null ? sorted.Length : 0;

        void Awake()
        {
            if (encounter == null) encounter = GetComponent<BossEncounter>();
            if (health == null) health = GetComponent<BossHealth>();
            if (sounds == null) sounds = GetComponent<BossAudio>();
            if (visuals == null) visuals = GetComponent<PreacherVisuals>();
            if (data == null)
            {
                Debug.LogError($"[Boss] Preacher '{name}' has no PreacherData. Run TWR > Run > Build Run Scenes.", this);
                enabled = false;
                return;
            }
            environmentMask = GameLayers.WithoutActors(data.environmentMask);

            // Stable order for the boss stream (D-172, D-189): by cavity id, never by hierarchy. Once, at load.
            var list = new List<PreacherCavity>(cavities.Length);
            foreach (PreacherCavity c in cavities)
                if (c != null) list.Add(c);
            list.Sort((a, b) => string.CompareOrdinal(a.CavityId, b.CavityId));
            sorted = list.ToArray();
            current = NearestCavity(transform.position, 3f);
            ShowGhost(false);
        }

        void Start()
        {
            if (projectiles != null) projectiles.Configure(data.environmentMask, encounter.PlayerHealth);
            else Debug.LogError($"[Boss] Preacher '{name}' has no BossProjectilePool.", this);
            if (sorted.Length < 12) Debug.LogWarning($"[Boss] Preacher arena has only {sorted.Length} cavities (D-187 wants at least 12).", this);
        }

        int NearestCavity(Vector3 position, float maxDistance)
        {
            int best = -1;
            float bestSqr = maxDistance * maxDistance;
            for (int i = 0; i < sorted.Length; i++)
            {
                float sqr = (sorted[i].Position - position).sqrMagnitude;
                if (sqr <= bestSqr) { bestSqr = sqr; best = i; }
            }
            return best;
        }

        // ---------- IBossBrain ----------

        public void BeginFight()
        {
            float now = Time.time;
            nextTeleportTime = now + Interval;
            fireAllowedTime = now + 0.5f;
            nextVolleyTime = now + 1f;
            Enter(State.Present);
        }

        public void HandlePhaseChanged(int phase)
        {
            // Phase 2 (D-184): shorter intervals from now on.
            nextTeleportTime = Mathf.Min(nextTeleportTime, Time.time + data.teleportIntervalPhaseTwo);
        }

        public void HandleDefeated()
        {
            Enter(State.Dead);
            ShowGhost(false);
            shotsLeft = 0;
            windingUp = false;
            if (projectiles != null) projectiles.ClearAll();
        }

        public string DebugState
        {
            get
            {
                string here = current >= 0 ? sorted[current].CavityId : "(start spot)";
                string next = target >= 0 && state == State.Telegraph ? sorted[target].CavityId : "-";
                float inSeconds = Mathf.Max(0f, nextTeleportTime - Time.time);
                return $"Preacher: <b>{state}</b> at '{here}' | phase {health.Phase} | seen: {(canSeePlayer ? "yes" : "no")}, {playerDistance:0} m\n" +
                       $"teleport target: <b>{next}</b> | next teleport in {inSeconds:0.0} s | fair cavities {candidates.Count}, behind {behind.Count}\n" +
                       $"teleports {teleports}{(lastPickBehind ? " (last one: behind the player)" : "")} | volley shots left {shotsLeft} | " +
                       $"projectiles {(projectiles != null ? projectiles.AliveCount : 0)}/{(projectiles != null ? projectiles.Capacity : 0)} | cavities {sorted.Length}";
            }
        }

        float Interval => health.Phase >= 2 ? data.teleportIntervalPhaseTwo : data.teleportIntervalPhaseOne;

        // ---------- update ----------

        void Update()
        {
            if (state == State.Dead || state == State.Idle) return;
            float now = Time.time;
            stateTime += Time.deltaTime;

            if (now >= nextCheck)
            {
                nextCheck = now + 1f / data.decisionsPerSecond;
                Sense();
                RefreshCandidates();
            }

            if (state == State.Present)
            {
                FacePlayer();
                UpdateFiring(now);
                if (now >= nextTeleportTime && shotsLeft == 0 && !windingUp) TryStartTeleport(now);
            }
            else if (state == State.Telegraph)
            {
                // Flicker at the destination (D-184).
                bool on = ((int)(stateTime * 16f) & 1) == 0;
                for (int i = 0; i < ghostRenderers.Length; i++)
                    if (ghostRenderers[i] != null) ghostRenderers[i].enabled = on;
                if (stateTime >= data.teleportTelegraphSeconds) Arrive(now);
            }

            if (visuals != null)
            {
                float cast = windingUp ? 1f - Mathf.Clamp01((windUpEnd - now) / Mathf.Max(0.01f, data.volleyWindUpSeconds)) : shotsLeft > 0 ? 1f : 0f;
                visuals.Apply(cast);
            }
        }

        /// 5 Hz: distance + line of sight from the hands to the player's chest.
        void Sense()
        {
            Transform player = encounter.PlayerTransform;
            if (player == null || !encounter.PlayerAlive)
            {
                canSeePlayer = false;
                return;
            }
            Vector3 from = HandsPosition;
            Vector3 chest = PlayerChest();
            playerDistance = Vector3.Distance(from, chest);
            canSeePlayer = ViewCone.HasLineOfSight(from, chest, environmentMask);
        }

        /// <summary>
        /// 5 Hz (D-193): which cavities are fair destinations right now (D-187). Loops the pre-sorted array, no allocations.
        /// </summary>
        void RefreshCandidates()
        {
            candidates.Clear();
            behind.Clear();
            Transform player = encounter.PlayerTransform;
            if (player == null) return;

            Vector3 feet = player.position;
            Transform view = encounter.PlayerCamera;
            Vector3 eye = view != null ? view.position : feet + Vector3.up * 1.65f;
            Vector3 forward = view != null ? view.forward : player.forward;
            bool phaseTwo = health.Phase >= 2;

            for (int i = 0; i < sorted.Length; i++)
            {
                if (i == current) continue;
                Vector3 p = sorted[i].Position;
                Vector3 offset = p - feet;
                float dy = offset.y;
                float distance = offset.magnitude;
                offset.y = 0f;
                float horizontal = offset.magnitude;
                if (horizontal > data.maxHorizontalDistance || Mathf.Abs(dy) > data.maxVerticalDistance) continue;

                if (distance >= data.minTeleportDistance) candidates.Add(i);
                if (phaseTwo && Mathf.Abs(dy) <= data.behindHeightBand && distance >= data.behindMinDistance &&
                    !ViewCone.IsInCone(eye, forward, p + Vector3.up * 1.8f, data.playerViewConeAngle, 1000f))
                    behind.Add(i);
            }
        }

        // ---------- teleport (D-184, D-187) ----------

        void TryStartTeleport(float now)
        {
            int pick = PreacherTeleportPlanner.Choose(encounter.Stream, candidates, behind, health.Phase >= 2, data.behindChancePercent, out bool pickedBehind);
            if (pick < 0)
            {
                nextTeleportTime = now + data.retryTeleportSeconds; // nothing fair right now: stay (never out of reach)
                return;
            }
            target = pick;
            lastPickBehind = pickedBehind;
            Transform stand = sorted[target].StandPoint;
            if (telegraphGhost != null) telegraphGhost.SetPositionAndRotation(stand.position, Quaternion.Euler(0f, stand.eulerAngles.y, 0f));
            ShowGhost(true);
            if (sounds != null) sounds.PlayAt(ghostAudio, BossSound.TeleportTelegraph);
            Enter(State.Telegraph);
        }

        void Arrive(float now)
        {
            ShowGhost(false);
            Transform stand = sorted[target].StandPoint;
            transform.SetPositionAndRotation(stand.position, Quaternion.Euler(0f, stand.eulerAngles.y, 0f));
            current = target;
            target = -1;
            teleports++;
            FacePlayer(360f);
            if (sounds != null) sounds.Play(BossSound.TeleportArrive);
            if (visuals != null) visuals.Pop();

            fireAllowedTime = now + data.noFireAfterArrivalSeconds; // D-187: no firing for 1 s after arrival
            nextVolleyTime = Mathf.Max(nextVolleyTime, fireAllowedTime);
            nextTeleportTime = now + Interval;
            Enter(State.Present);
        }

        void ShowGhost(bool show)
        {
            if (telegraphGhost != null) telegraphGhost.gameObject.SetActive(show);
            for (int i = 0; i < ghostRenderers.Length; i++)
                if (ghostRenderers[i] != null) ghostRenderers[i].enabled = show;
        }

        // ---------- volleys (D-184, D-192) ----------

        void UpdateFiring(float now)
        {
            if (shotsLeft > 0)
            {
                if (now >= nextShotTime) FireOne(now);
                return;
            }
            if (windingUp)
            {
                if (now < windUpEnd) return;
                windingUp = false;
                shotsLeft = PreacherTeleportPlanner.VolleySize(encounter.Stream, data.volleyCountMin, data.volleyCountMax);
                shotIndex = 0;
                nextShotTime = now;
                return;
            }
            if (now >= nextVolleyTime && now >= fireAllowedTime && canSeePlayer && playerDistance <= data.maxFireDistance && encounter.PlayerAlive)
            {
                windingUp = true;
                windUpEnd = now + data.volleyWindUpSeconds;
            }
        }

        void FireOne(float now)
        {
            if (encounter.PlayerTransform == null || !encounter.PlayerAlive)
            {
                shotsLeft = 0;
                nextVolleyTime = now + data.volleyCooldown;
                return;
            }
            Vector3 origin = HandsPosition;
            Vector3 direction = PlayerChest() - origin;
            // Small fan: left, centre, right, left... (no randomness needed; still dodgeable by moving sideways).
            float yaw = data.spreadDegrees * ((shotIndex % 3) - 1);
            direction = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
            if (projectiles != null)
                projectiles.Fire(origin, direction, data.projectileSpeed, data.projectileRadius, data.projectileDamage, data.projectileLifetime, gameObject);
            if (sounds != null) sounds.Play(BossSound.Volley);

            shotIndex++;
            shotsLeft--;
            nextShotTime = now + data.shotInterval;
            if (shotsLeft == 0) nextVolleyTime = now + data.volleyCooldown;
        }

        // ---------- helpers ----------

        Vector3 HandsPosition => hands != null ? hands.position : transform.position + Vector3.up * 2.6f;

        Vector3 PlayerChest() => encounter.PlayerTransform.position + Vector3.up * 1.2f;

        void FacePlayer(float maxDegrees = -1f)
        {
            Transform player = encounter.PlayerTransform;
            if (player == null) return;
            Vector3 flat = player.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            float step = maxDegrees > 0f ? maxDegrees : 270f * Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), step);
        }

        void Enter(State next)
        {
            state = next;
            stateTime = 0f;
        }
    }
}
