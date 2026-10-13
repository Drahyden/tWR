using UnityEngine;
using TWR.Combat;
using TWR.Core;
using TWR.Core.Perception;
using TWR.Player;

namespace TWR.Enemies
{
    /// <summary>
    /// The Seeker's ears and eyes (D-132, confirmed; D-043 "reacts to sound and movement": movement = sight).
    /// HEARING: an INoiseListener on the shared NoiseSystem, so wall muffling (D-116, D-131) is already
    /// applied. Noises made by enemies are ignored (D-138); each noise type has a sensitivity and a
    /// reaction from SeekerData. SIGHT (can be switched off with SeekerData.useSight): 5 times a second it checks
    /// the player's head and chest with the shared ViewCone (110 deg cone + line of sight). Being seen depends
    /// on MOVEMENT: moving 15 m, crouch-moving 6 m, standing still 2.5 m; awareness builds over ~0.5 s before it
    /// "spots" you. While the brain is hunting (Tracking = true) it keeps seeing you out to the moving range.
    /// This script only reports; the Seeker brain decides what to do.
    /// </summary>
    public class SeekerSenses : MonoBehaviour, INoiseListener
    {
        [SerializeField] SeekerData data;
        [Tooltip("Eye/ear point (child at eye height, facing forward).")]
        [SerializeField] Transform eye;

        /// <summary>A noise passed every filter. reaction comes from SeekerData's rule for that noise type.</summary>
        public delegate void NoiseHeardHandler(in HeardNoise heard, NoiseReaction reaction);
        public event NoiseHeardHandler NoiseHeard;

        /// <summary>Sight awareness reached full: the Seeker has noticed the player.</summary>
        public event System.Action PlayerSpotted;

        PlayerController player;
        PlayerLocomotion playerLocomotion;
        IDamageable playerHealth;
        Transform playerCamera;
        int sightMask;
        float nextSightCheck;
        float awareness;
        bool canSeePlayer;
        float lastSeenTime = -1000f;
        Vector3 lastSeenPosition;

        /// <summary>Set by the brain while chasing/attacking: sight ignores the movement gate (it's watching you).</summary>
        public bool Tracking { get; set; }

        public Vector3 HearingPosition => eye != null ? eye.position : transform.position + Vector3.up * 1.6f;
        public Transform Eye => eye != null ? eye : transform;
        public bool CanSeePlayer => canSeePlayer;
        public float Awareness01 => awareness;
        public float TimeSinceSeen => Time.time - lastSeenTime;
        public Vector3 LastSeenPosition => lastSeenPosition;
        public Transform PlayerTransform => player != null ? player.transform : null;
        public IDamageable PlayerHealth => playerHealth;
        public bool PlayerAlive => player != null && playerHealth != null && playerHealth.IsAlive;

        void Awake()
        {
            sightMask = GameLayers.WithoutActors(data != null ? (int)data.sightBlockers : 1);
        }

        void Start()
        {
            // Once, not in Update (D-100). The slice has exactly one player.
            player = FindFirstObjectByType<PlayerController>();
            if (player == null)
            {
                Debug.LogWarning($"[SeekerSenses] '{name}' found no PlayerController: sight is off.", this);
                return;
            }
            playerLocomotion = player.Locomotion != null ? player.Locomotion : player.GetComponent<PlayerLocomotion>();
            playerHealth = player.GetComponent<IDamageable>();
            playerCamera = player.PlayerCamera != null ? player.PlayerCamera.transform : null;
            nextSightCheck = Time.time + Random.value / Mathf.Max(1f, data.sightChecksPerSecond); // spread Seekers over frames
        }

        void OnEnable() => NoiseSystem.Register(this);

        void OnDisable()
        {
            NoiseSystem.Unregister(this);
            canSeePlayer = false;
        }

        // ---------- hearing ----------

        public void OnNoiseHeard(in HeardNoise heard)
        {
            if (data == null) return;
            GameObject source = heard.Noise.Source;
            if (source != null && source.layer == GameLayers.EnemyLayer) return; // enemies ignore enemy noise (D-138)

            NoiseRule rule = data.GetNoiseRule(heard.Noise.Type);
            if (rule.reaction == NoiseReaction.Ignore) return;
            if (heard.Distance > heard.EffectiveRadius * rule.sensitivity) return;

            NoiseHeard?.Invoke(heard, rule.reaction);
        }

        // ---------- sight ----------

        void Update()
        {
            if (data == null || !data.useSight || player == null) return;
            if (Time.time < nextSightCheck) return;

            float interval = 1f / Mathf.Max(0.5f, data.sightChecksPerSecond);
            nextSightCheck = Time.time + interval;
            CheckSight(interval);
        }

        void CheckSight(float interval)
        {
            bool visible = PlayerAlive && IsPlayerInView();
            if (visible)
            {
                lastSeenPosition = player.transform.position;
                float build = Mathf.Max(0.01f, data.awarenessBuildSeconds);
                bool wasFull = awareness >= 1f;
                awareness = Tracking ? 1f : Mathf.Min(1f, awareness + interval / build);
                canSeePlayer = awareness >= 1f;
                if (canSeePlayer) lastSeenTime = Time.time;
                if (canSeePlayer && !wasFull && !Tracking) PlayerSpotted?.Invoke();
            }
            else
            {
                canSeePlayer = false;
                awareness = Mathf.Max(0f, awareness - interval / Mathf.Max(0.01f, data.awarenessDecaySeconds));
            }
        }

        /// Head or chest inside the cone, within the range allowed by how the player is moving, and not behind a wall.
        bool IsPlayerInView()
        {
            float range = Tracking ? data.seeMovingDistance : AllowedRange();
            Vector3 eyePosition = Eye.position;
            Vector3 forward = Eye.forward;

            Vector3 chest = player.transform.position + Vector3.up * (ChestHeight());
            if (ViewCone.CanSee(eyePosition, forward, chest, data.sightConeDegrees, range, sightMask)) return true;
            return playerCamera != null && ViewCone.CanSee(eyePosition, forward, playerCamera.position, data.sightConeDegrees, range, sightMask);
        }

        float ChestHeight()
        {
            float height = playerLocomotion != null && playerLocomotion.Motor != null ? playerLocomotion.Motor.Height : 1.8f;
            return height * 0.6f;
        }

        /// Motion-gated sight distance (D-132).
        float AllowedRange()
        {
            if (playerLocomotion == null) return data.seeMovingDistance;
            bool moving = playerLocomotion.HorizontalSpeed > data.movingSpeedThreshold || !playerLocomotion.IsGrounded;
            if (!moving) return data.seeStillDistance;
            return playerLocomotion.IsCrouching ? data.seeCrouchMovingDistance : data.seeMovingDistance;
        }

        /// <summary>Forget the sight build-up (e.g. after returning to patrol).</summary>
        public void ResetAwareness()
        {
            awareness = 0f;
            canSeePlayer = false;
        }

        void OnDrawGizmosSelected()
        {
            if (data == null) return;
            Transform e = Eye;
            float half = data.sightConeDegrees * 0.5f;
            Vector3 left = Quaternion.AngleAxis(-half, Vector3.up) * e.forward;
            Vector3 right = Quaternion.AngleAxis(half, Vector3.up) * e.forward;
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
            Gizmos.DrawLine(e.position, e.position + left * data.seeMovingDistance);
            Gizmos.DrawLine(e.position, e.position + right * data.seeMovingDistance);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawLine(e.position + left * data.seeCrouchMovingDistance, e.position + right * data.seeCrouchMovingDistance);
            Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.8f);
            Gizmos.DrawLine(e.position + left * data.seeStillDistance, e.position + right * data.seeStillDistance);
        }
    }
}
