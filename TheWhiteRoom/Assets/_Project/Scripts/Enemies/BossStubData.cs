using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Tunables for a boss placeholder (D-148 bull / preacher stand-ins, D-165 "boss defeated" stub): how many HP
    /// it takes and how fast it shrinks away. The real bosses (D-047, D-048) get their own data later.
    /// Create via: Project window > right-click > Create > TWR > Boss Stub Data.
    /// </summary>
    [CreateAssetMenu(fileName = "BossStub_", menuName = "TWR/Boss Stub Data")]
    public class BossStubData : ScriptableObject
    {
        [Tooltip("Stable id for logs, events and voice triggers, e.g. 'boss.bull'.")]
        public string bossId = "boss";
        [Tooltip("HP. 150 = 6 pistol hits (25 dmg, D-119).")]
        [Min(1f)] public float maxHealth = 150f;
        [Tooltip("Seconds the defeated placeholder takes to shrink away.")]
        [Min(0f)] public float vanishSeconds = 1f;
    }
}
