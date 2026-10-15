using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Every number of the Preacher, Boss 1 of the Discover path (D-047, D-184, D-187, D-192): teleport timing per
    /// phase and its telegraph, which dark cavities count as fair destinations (within 40 m and +-20 m of the player;
    /// in phase 2 also "behind the player": outside the view cone, +-5 m, at least 6 m away), the 1 s no-fire window
    /// after arriving, and the projectile volleys. Placeholder values from D-192.
    /// Create via: Create > TWR > Bosses > Preacher Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Boss_Preacher", menuName = "TWR/Bosses/Preacher Data")]
    public class PreacherData : BossData
    {
        [Header("Teleport - D-184, D-192")]
        [Tooltip("Seconds between teleports in phase 1 (D-192: 6).")]
        [Min(0.5f)] public float teleportIntervalPhaseOne = 6f;
        [Tooltip("Seconds between teleports in phase 2 (D-192: 3.5).")]
        [Min(0.5f)] public float teleportIntervalPhaseTwo = 3.5f;
        [Tooltip("Telegraph at the destination before it appears (flicker + sound, D-184: about 1 s).")]
        [Min(0.1f)] public float teleportTelegraphSeconds = 1f;
        [Tooltip("No firing for this long after arriving (D-187: 1 s).")]
        [Min(0f)] public float noFireAfterArrivalSeconds = 1f;
        [Tooltip("If no fair cavity exists right now, try again after this many seconds (it stays where it is).")]
        [Min(0.1f)] public float retryTeleportSeconds = 1f;

        [Header("Fair destinations - D-187")]
        [Tooltip("Cavities must be within this horizontal distance of the player (m).")]
        [Min(1f)] public float maxHorizontalDistance = 40f;
        [Tooltip("...and within this height difference of the player's feet (m).")]
        [Min(1f)] public float maxVerticalDistance = 20f;
        [Tooltip("...and at least this far from the player, so it never appears on top of them (m).")]
        [Min(0f)] public float minTeleportDistance = 8f;

        [Header("Phase 2: behind the player - D-184, D-187")]
        [Tooltip("Chance (percent, rolled from the boss stream) that a phase-2 teleport picks a cavity behind the player.")]
        [Range(0, 100)] public int behindChancePercent = 60;
        [Tooltip("Full angle of the player's view cone used for 'behind' (shared ViewCone helper).")]
        [Range(30f, 180f)] public float playerViewConeAngle = 100f;
        [Tooltip("'Behind' cavities stay within this height band of the player's feet (D-187: +-5 m).")]
        [Min(0.5f)] public float behindHeightBand = 5f;
        [Tooltip("'Behind' cavities are at least this far away (D-187: 6 m).")]
        [Min(0f)] public float behindMinDistance = 6f;

        [Header("Volleys - D-184, D-192")]
        [Range(1, 10)] public int volleyCountMin = 3;
        [Range(1, 10)] public int volleyCountMax = 5;
        [Tooltip("Damage per projectile (D-192: 10).")]
        [Min(0f)] public float projectileDamage = 10f;
        [Tooltip("Projectile speed, m/s (D-192: 15). Slow enough to sidestep.")]
        [Min(1f)] public float projectileSpeed = 15f;
        [Tooltip("Projectile radius for its sphere sweep (m).")]
        [Min(0.05f)] public float projectileRadius = 0.3f;
        [Tooltip("A projectile that hits nothing disappears after this many seconds.")]
        [Min(0.5f)] public float projectileLifetime = 6f;
        [Tooltip("Sideways fan between projectiles of a volley (degrees).")]
        [Min(0f)] public float spreadDegrees = 4f;
        [Tooltip("Seconds between two projectiles of one volley.")]
        [Min(0.02f)] public float shotInterval = 0.15f;
        [Tooltip("Seconds between the end of one volley and the start of the next.")]
        [Min(0.1f)] public float volleyCooldown = 2.2f;
        [Tooltip("Arms-raised wind-up before the first projectile, seconds.")]
        [Min(0f)] public float volleyWindUpSeconds = 0.35f;
        [Tooltip("Only fires when the player is closer than this (m).")]
        [Min(5f)] public float maxFireDistance = 70f;

        void Reset()
        {
            // Defaults for a new asset (D-192; editor Reset). The scene builder sets them too.
            bossId = "boss.preacher";
            maxHealth = 750f;
        }

        void OnValidate()
        {
            if (volleyCountMax < volleyCountMin) volleyCountMax = volleyCountMin;
        }
    }
}
