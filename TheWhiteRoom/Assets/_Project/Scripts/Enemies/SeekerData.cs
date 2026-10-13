using System;
using UnityEngine;
using TWR.Core;

namespace TWR.Enemies
{
    /// <summary>What a heard noise makes the Seeker do (D-133).</summary>
    public enum NoiseReaction
    {
        Ignore,
        /// <summary>Stop and turn toward it for a moment (footsteps, jumps, landings, interactions, pickups).</summary>
        Suspicious,
        /// <summary>Walk over and look around (gunshots, impacts, mechanisms).</summary>
        Investigate
    }

    /// <summary>Per-noise-type hearing rule: how sensitive and what reaction.</summary>
    [Serializable]
    public struct NoiseRule
    {
        public NoiseType type;
        [Tooltip("Multiplies the (muffled) radius before the distance check. 1 = normal, 0.5 = must be twice as close. " +
                 "Values above 1 have no effect beyond the noise's raw radius.")]
        [Range(0f, 1f)] public float sensitivity;
        public NoiseReaction reaction;

        public NoiseRule(NoiseType type, float sensitivity, NoiseReaction reaction)
        {
            this.type = type;
            this.sensitivity = sensitivity;
            this.reaction = reaction;
        }
    }

    /// <summary>
    /// Every tunable number of the Seeker (D-043) in one asset: health and melee (D-134), senses
    /// (D-132, confirmed: hearing via the muffled noise system of D-131, motion-gated sight), state timings and speeds
    /// (D-133), death timings (D-136) and footstep audio spacing (D-138).
    /// The Seeker scripts only read it; nothing in here changes during play.
    /// Create via: Project window > right-click > Create > TWR > Seeker Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Seeker_", menuName = "TWR/Seeker Data")]
    public class SeekerData : ScriptableObject
    {
        [Header("Health - D-134")]
        [Tooltip("100 = 4 pistol hits (25 each, D-119).")]
        [Min(1f)] public float maxHealth = 100f;

        [Header("Body + navigation - D-135")]
        public float agentRadius = 0.4f;
        public float agentHeight = 2f;
        [Tooltip("Eye/ear height above the feet (sight origin and hearing position).")]
        public float eyeHeight = 1.6f;
        [Tooltip("NavMeshAgent turn speed while walking (deg/s).")]
        public float angularSpeed = 540f;
        public float acceleration = 16f;
        [Tooltip("Turn speed when standing and turning toward something (deg/s).")]
        public float turnSpeed = 300f;
        [Tooltip("How close counts as 'arrived' at a waypoint or noise (m).")]
        public float arriveDistance = 0.5f;

        [Header("Patrol - D-133")]
        public float patrolSpeed = 1.6f;
        public float waypointPauseMin = 1f;
        public float waypointPauseMax = 2f;

        [Header("Suspicious - D-133")]
        [Tooltip("Stop and turn toward the noise for this long, then resume.")]
        public float suspiciousSeconds = 1f;

        [Header("Investigate - D-133")]
        public float investigateSpeed = 2.5f;
        [Tooltip("Look around this long at the noise position.")]
        public float lookAroundSeconds = 4f;
        [Tooltip("How far it turns left/right while looking around (deg).")]
        public float lookAroundAngle = 70f;

        [Header("Chase - D-133")]
        public float chaseSpeed = 4.5f;
        [Tooltip("Re-path toward the player this many times per second (4-5).")]
        public float chaseRepathsPerSecond = 4.5f;
        [Tooltip("Stop this far from the player while chasing (m).")]
        public float chaseStopDistance = 1.1f;
        [Tooltip("A noise heard closer than this starts a chase immediately (m).")]
        public float closeNoiseChaseDistance = 3f;
        [Tooltip("Seconds without seeing the player before giving up and searching.")]
        public float loseSightSeconds = 5f;
        [Tooltip("After losing sight it still 'knows' where you are for this long (rounding a corner).")]
        public float chaseMemorySeconds = 0.75f;

        [Header("Search - D-133")]
        public float searchSpeed = 2.5f;
        [Tooltip("Random NavMesh points are picked within this radius of the last known position (m).")]
        public float searchRadius = 6f;
        [Tooltip("How long it searches once it reached the last known position.")]
        public float searchSeconds = 8f;
        [Tooltip("Pause at each search point.")]
        public float searchPointPause = 0.8f;

        [Header("Return - D-133")]
        public float returnSpeed = 1.6f;

        [Header("Hearing - D-132 (muffling: D-131)")]
        [Tooltip("Per noise type: sensitivity and reaction. Types missing here use sensitivity 1 + Suspicious.")]
        public NoiseRule[] noiseRules =
        {
            new NoiseRule(NoiseType.Footstep,  1f, NoiseReaction.Suspicious),
            new NoiseRule(NoiseType.Jump,      1f, NoiseReaction.Suspicious),
            new NoiseRule(NoiseType.Land,      1f, NoiseReaction.Suspicious),
            new NoiseRule(NoiseType.Interact,  1f, NoiseReaction.Suspicious),
            new NoiseRule(NoiseType.Pickup,    1f, NoiseReaction.Suspicious),
            new NoiseRule(NoiseType.Gunshot,   1f, NoiseReaction.Investigate),
            new NoiseRule(NoiseType.Impact,    1f, NoiseReaction.Investigate),
            new NoiseRule(NoiseType.Mechanism, 1f, NoiseReaction.Investigate),
        };

        [Header("Sight - D-132 (confirmed: D-043 'movement' = sight)")]
        [Tooltip("Off = the Seeker is blind and reacts to sound and damage only.")]
        public bool useSight = true;
        [Tooltip("Full cone angle (deg).")]
        public float sightConeDegrees = 110f;
        public float sightChecksPerSecond = 5f;
        [Tooltip("A walking/sprinting/jumping player is noticed up to this far (m).")]
        public float seeMovingDistance = 15f;
        [Tooltip("A crouch-walking player is noticed up to this far (m).")]
        public float seeCrouchMovingDistance = 6f;
        [Tooltip("A player standing still is noticed only this close (m).")]
        public float seeStillDistance = 2.5f;
        [Tooltip("Player speed above this counts as moving (m/s).")]
        public float movingSpeedThreshold = 0.4f;
        [Tooltip("Seconds of continuous sight before it notices you.")]
        public float awarenessBuildSeconds = 0.5f;
        [Tooltip("Seconds for full awareness to fade when you are out of sight.")]
        public float awarenessDecaySeconds = 1.5f;
        [Tooltip("What blocks sight: Default + Interactable. Player/Enemy/Weapon/Ignore Raycast are always removed at runtime.")]
        public LayerMask sightBlockers = 1;

        [Header("Melee - D-134")]
        public float meleeDamage = 20f;
        [Tooltip("The hit lands only if the player is still this close when the wind-up ends (m, flat distance).")]
        public float meleeRange = 1.8f;
        [Tooltip("Starts the wind-up when the player is this close (m). Keep below meleeRange.")]
        public float attackStartDistance = 1.5f;
        [Tooltip("Telegraph: leans back for this long before striking (s).")]
        public float windUpSeconds = 0.4f;
        [Tooltip("Time after a strike before the next wind-up may start (s).")]
        public float attackCooldown = 1.2f;
        [Tooltip("'In front' = within this angle of its facing, either side (deg).")]
        public float meleeHalfAngle = 60f;
        [Tooltip("Max height difference for a hit (m).")]
        public float meleeMaxHeightDifference = 1.5f;

        [Header("Death - D-136 (placeholder for D-051)")]
        [Tooltip("Tips over with a darker tint.")]
        public float collapseSeconds = 0.6f;
        [Tooltip("Then shrinks away and is destroyed.")]
        public float fadeSeconds = 1f;

        [Header("Audio - D-138")]
        [Tooltip("Metres walked per footstep sound (3D audio only, never a noise event).")]
        public float footstepStride = 0.9f;

        /// <summary>Hearing rule for a noise type (allocation-free lookup).</summary>
        public NoiseRule GetNoiseRule(NoiseType type)
        {
            if (noiseRules != null)
                for (int i = 0; i < noiseRules.Length; i++)
                    if (noiseRules[i].type == type) return noiseRules[i];
            return new NoiseRule(type, 1f, NoiseReaction.Suspicious);
        }
    }
}
