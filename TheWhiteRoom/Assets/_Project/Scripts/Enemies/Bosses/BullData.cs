using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Every number of the Bull, Boss 2 of the Escape path (D-048, D-181, D-182, D-189, D-190, D-192): walking, the
    /// telegraphed charge and the faster charge, stun and the bonus damage it takes while stunned, the stomp, column
    /// breaking, knockback and the fast-charge count (2-5 attacks, rolled from the boss stream; seed 0 uses the authored
    /// sequence below). Placeholder values from D-192. Create via: Create > TWR > Bosses > Bull Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Boss_Bull", menuName = "TWR/Bosses/Bull Data")]
    public class BullData : BossData
    {
        [Header("Walking (NavMesh) - D-181")]
        [Tooltip("Speed while walking into position between attacks (m/s). Player walk 3.5, sprint 6.")]
        [Min(0.5f)] public float walkSpeed = 4.5f;
        [Tooltip("NavMeshAgent turn speed while walking (deg/s).")]
        [Min(10f)] public float angularSpeed = 240f;
        [Tooltip("Turn speed while aiming during a telegraph (deg/s).")]
        [Min(10f)] public float aimTurnSpeed = 180f;
        [Tooltip("Repaths per second while walking towards the player.")]
        [Range(0.5f, 10f)] public float repathsPerSecond = 3f;

        [Header("Charge - D-181, D-190")]
        [Tooltip("Telegraph length before a normal charge (paw, roar, head down), seconds.")]
        [Min(0.2f)] public float chargeTelegraphSeconds = 1f;
        [Tooltip("The aim locks at this fraction of the telegraph; after that the player can sidestep.")]
        [Range(0.3f, 1f)] public float aimLockFraction = 0.75f;
        [Min(1f)] public float chargeSpeed = 16f;
        [Tooltip("Damage of a normal charge that hits the player (D-192: 30).")]
        [Min(0f)] public float chargeDamage = 30f;
        [Tooltip("A charge that hits nothing ends after this distance (then skids).")]
        [Min(5f)] public float maxChargeDistance = 60f;
        [Tooltip("Only charges when the player is at least this far away (closer = stomp).")]
        [Min(1f)] public float minChargeDistance = 8f;
        [Tooltip("Only starts a charge when the player is closer than this and in sight.")]
        [Min(5f)] public float maxChargeStartDistance = 45f;
        [Tooltip("Skid after hitting the player or reaching max distance (D-190: about 3 m, no stun).")]
        [Min(0f)] public float skidDistance = 3f;
        [Tooltip("Pause after a charge or stomp before the next decision, seconds.")]
        [Min(0f)] public float recoverSeconds = 1f;
        [Tooltip("Half size of the body box swept forward each frame while charging (x = half width, y = half height, z = half length).")]
        public Vector3 chargeHalfExtents = new Vector3(1.2f, 1.0f, 2.2f);
        [Tooltip("Height of the sweep box centre above the feet (keeps it off the floor).")]
        [Min(0.5f)] public float chargeSweepHeight = 1.4f;

        [Header("Fast charge - D-182, D-189")]
        [Tooltip("Telegraph length before a FAST charge (red flash + higher roar), seconds.")]
        [Min(0.2f)] public float fastTelegraphSeconds = 1f;
        [Min(1f)] public float fastChargeSpeed = 26f;
        [Tooltip("Damage of a fast charge (D-192: 45; must stay below the player's max HP, D-190).")]
        [Min(0f)] public float fastChargeDamage = 45f;
        [Tooltip("Knockback distance of a fast charge that hits the player (D-182: up to 20 m, stops at walls).")]
        [Min(0f)] public float knockbackDistance = 20f;
        [Tooltip("Attacks (charges + stomps) before the next charge is fast: rolled between min and max, inclusive (D-189).")]
        [Range(1, 10)] public int fastChargeCountMin = 2;
        [Range(1, 10)] public int fastChargeCountMax = 5;
        [Tooltip("Seed 0 (curated) and the sandbox use these counts in order, then repeat (D-189).")]
        public int[] curatedFastChargeCounts = { 3, 4, 2, 5 };

        [Header("Stun - D-181, D-190")]
        [Tooltip("Stun after charging into a wall or column, seconds (D-192: 3).")]
        [Min(0f)] public float stunSeconds = 3f;
        [Tooltip("Damage taken while stunned is multiplied by this (D-192: x2).")]
        [Min(1f)] public float stunnedDamageMultiplier = 2f;

        [Header("Columns - D-190")]
        [Tooltip("Charge hits a breakable column takes before it breaks (D-190: 2).")]
        [Min(1)] public int columnHitsToBreak = 2;

        [Header("Stomp - D-181, D-192")]
        [Tooltip("Starts a stomp when the player is this close (m).")]
        [Min(1f)] public float stompTriggerDistance = 5f;
        [Tooltip("Players within this flat radius at the impact are hit (D-192: 4 m).")]
        [Min(0.5f)] public float stompRadius = 4f;
        [Tooltip("Players more than this far above/below the feet are not hit.")]
        [Min(0.5f)] public float stompMaxHeightDifference = 2.5f;
        [Min(0f)] public float stompDamage = 20f;
        [Tooltip("Rear-up telegraph before the stomp lands, seconds.")]
        [Min(0.1f)] public float stompWindUpSeconds = 0.7f;
        [Min(0f)] public float stompCooldown = 4f;

        void Reset()
        {
            // Defaults for a new asset (D-192; editor Reset). The scene builder sets them too.
            bossId = "boss.bull";
            maxHealth = 1000f;
        }

        void OnValidate()
        {
            if (fastChargeCountMax < fastChargeCountMin) fastChargeCountMax = fastChargeCountMin;
        }
    }
}
