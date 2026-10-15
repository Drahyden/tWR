using UnityEngine;

namespace TWR.Player
{
    /// <summary>
    /// All tunable numbers for the first-person player in one asset (D-032 moveset, D-079 noises).
    /// Named "PlayerTuning" to avoid clashing with UnityEditor.PlayerSettings.
    /// Values marked "OPEN" are placeholder defaults for GDD items that are not decided yet.
    /// R4 (D-182, D-191): fall-speed cap (no fall damage anywhere) and the knockback deceleration of a Bull fast charge.
    /// Create via: Project window > right-click > Create > TWR > Player Tuning.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerTuning", menuName = "TWR/Player Tuning")]
    public class PlayerTuning : ScriptableObject
    {
        [Header("Movement speeds (m/s) - D-032")]
        public float walkSpeed = 3.5f;
        public float sprintSpeed = 6f;
        public float crouchSpeed = 1.8f;
        [Tooltip("How fast you reach target speed on the ground (m/s per second).")]
        public float groundAcceleration = 40f;
        [Tooltip("Acceleration while airborne (lower = less air control).")]
        public float airAcceleration = 8f;

        [Header("Jump & gravity - D-032, D-033")]
        public float jumpHeight = 1.1f;
        [Tooltip("Total jumps before landing. 1 = normal. Double jump (D-033) = 2, or add PlayerLocomotion.BonusJumps at runtime.")]
        public int maxJumps = 1;
        [Tooltip("Negative number, m/s^2. Steeper than real gravity feels snappier.")]
        public float gravity = -20f;
        [Tooltip("Grace time after walking off a ledge where a jump still counts as a ground jump.")]
        public float coyoteTime = 0.1f;
        [Tooltip("How quickly impulses from AddImpulse (dash, launch pads) fade, m/s per second.")]
        public float impulseDrag = 12f;
        [Tooltip("Fastest fall speed (m/s, D-191: about 40). There is no fall damage anywhere (D-191); landing noise stays capped by landNoiseMax.")]
        [Min(5f)] public float maxFallSpeed = 40f;

        [Header("Knockback - D-182, D-191")]
        [Tooltip("How quickly a knockback slows down (m/s per second). Start speed = sqrt(2 x this x distance), so it slides exactly the distance.")]
        [Min(1f)] public float knockbackDeceleration = 20f;
        [Tooltip("A knockback stops at once when the motor moves less than this share of the intended push (blocked by a wall).")]
        [Range(0.05f, 0.9f)] public float knockbackBlockedRatio = 0.3f;

        [Header("Body heights (m)")]
        public float standingHeight = 1.8f;
        public float crouchHeight = 1.0f;
        public float standingEyeHeight = 1.65f;
        public float crouchEyeHeight = 0.85f;
        [Tooltip("Capsule radius. Applied to the motor when the Player prefab is built (TWR/Rebuild Player Prefab).")]
        public float controllerRadius = 0.35f;
        [Tooltip("How fast the body/eyes move between stand and crouch.")]
        public float crouchTransitionSpeed = 8f;
        [Tooltip("Layers that block standing up and leaning (everything solid).")]
        public LayerMask obstacleMask = ~0;

        [Header("Mouse look")]
        [Tooltip("Degrees per mouse pixel. OPEN: settings menu slider later (D-090).")]
        public float mouseSensitivity = 0.1f;
        public float minPitch = -85f;
        public float maxPitch = 85f;
        [Tooltip("OPEN: FOV toggle/slider in Settings is undecided (D-090). 80 for now.")]
        public float fieldOfView = 80f;

        [Header("Head bob - OPEN (D-090 toggle undecided), off by default")]
        public bool headBobEnabled = false;
        [Tooltip("Vertical bob amount in metres.")]
        public float headBobAmount = 0.035f;
        [Tooltip("Bobs per metre walked (so it matches the footsteps).")]
        public float headBobFrequency = 0.7f;

        [Header("Lean (Q/E) - D-032")]
        [Tooltip("Sideways camera offset at full lean, metres.")]
        public float leanDistance = 0.45f;
        [Tooltip("Camera roll at full lean, degrees.")]
        public float leanAngle = 12f;
        [Tooltip("How fast lean goes in/out (full lean per second).")]
        public float leanSpeed = 6f;
        [Tooltip("Radius of the spherecast that stops the camera clipping into walls.")]
        public float leanCollisionRadius = 0.2f;

        [Header("Interaction - D-091")]
        public float interactDistance = 2.5f;

        [Header("Footsteps (metres walked per footstep) - D-079")]
        public float crouchStride = 0.6f;
        public float walkStride = 0.8f;
        public float sprintStride = 1.1f;
        [Range(0f, 1f)] public float crouchStepVolume = 0.25f;
        [Range(0f, 1f)] public float walkStepVolume = 0.6f;
        [Range(0f, 1f)] public float sprintStepVolume = 1f;

        [Header("Noise radii (m) - D-043, D-052, D-079. OPEN: exact values not in GDD, tune with the Seeker")]
        public float crouchStepNoise = 1f;
        public float walkStepNoise = 4f;
        public float sprintStepNoise = 10f;
        public float jumpNoise = 6f;
        [Tooltip("Landing noise = landNoiseBase + fallSpeed * landNoisePerSpeed, capped at landNoiseMax.")]
        public float landNoiseBase = 2f;
        public float landNoisePerSpeed = 0.8f;
        public float landNoiseMax = 15f;
        [Tooltip("Landings slower than this (m/s downward) make no noise, e.g. stepping off a kerb.")]
        public float landMinSpeed = 3f;
        public float interactNoise = 3f;

        [Header("Health - D-034")]
        public float maxHealth = 100f;
        [Tooltip("Seconds between dying and the run ending (back to the hub lobby with a new run, D-157).")]
        public float deathRestartDelay = 1.5f;

        [Header("Starting ammo - D-120")]
        [Tooltip("Reserve rounds put in the Inventory at the start (on top of a full magazine).")]
        public int startingReserveAmmo = 24;
    }
}
