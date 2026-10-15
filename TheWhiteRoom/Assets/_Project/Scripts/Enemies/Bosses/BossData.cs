using UnityEngine;
using UnityEngine.Localization;

namespace TWR.Enemies
{
    /// <summary>
    /// Tunables shared by both bosses (D-185): id, localized HUD name (D-084, D-193), HP, the phase threshold, how
    /// often the boss "thinks" (5 Hz candidate / sight checks, D-193), the death fade-out, the arena ammo respawn time
    /// (D-188), what counts as a wall for sight, charges and projectiles, and the boss's sound set (D-078).
    /// The Bull and the Preacher each have their own subclass (BullData, PreacherData) with the rest of their numbers.
    /// Abstract: create a BullData or PreacherData asset instead.
    /// </summary>
    public abstract class BossData : ScriptableObject
    {
        [Header("Identity - D-185, D-193")]
        [Tooltip("Stable id for logs, events and voice triggers, e.g. 'boss.bull'.")]
        public string bossId = "boss";
        [Tooltip("Name on the HUD boss bar (table 'UI', e.g. boss.bull.name). Placeholder names for now (GDD §14).")]
        public LocalizedString displayName = new LocalizedString();

        [Header("Health - D-185, D-192")]
        [Min(1f)] public float maxHealth = 1000f;
        [Tooltip("Phase 2 starts when HP drops to this fraction of max HP (D-184: 0.5).")]
        [Range(0.05f, 0.95f)] public float phaseTwoThreshold = 0.5f;
        [Tooltip("Seconds the defeated boss takes to sink and shrink away (placeholder for D-051's brief death + dissolve).")]
        [Min(0.1f)] public float vanishSeconds = 1.5f;
        [Tooltip("Seconds between two hurt sounds at most.")]
        [Min(0f)] public float hurtSoundInterval = 0.35f;

        [Header("Thinking - D-100, D-193")]
        [Tooltip("How often per second the boss re-checks sight, distances and teleport candidates (D-193: about 5).")]
        [Range(1f, 20f)] public float decisionsPerSecond = 5f;
        [Tooltip("What blocks sight, charges and projectiles: walls, columns, platforms, cover. Never Player/Enemy (removed at runtime).")]
        public LayerMask environmentMask = 1;

        [Header("Arena ammo - D-188")]
        [Tooltip("While the boss is alive, a collected ammo pickup in its arena comes back after this many seconds.")]
        [Min(1f)] public float ammoRespawnSeconds = 20f;

        [Header("Sound - D-078, D-138")]
        public BossSoundSet sounds;
    }
}
