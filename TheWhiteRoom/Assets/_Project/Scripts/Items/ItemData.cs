using UnityEngine;
using UnityEngine.Localization;

namespace TWR.Items
{
    /// <summary>
    /// One kind of item the player can carry (D-037), e.g. the shared Ammo item (D-050).
    /// The Inventory stores "how many" of each ItemData; this asset holds the fixed facts about it.
    /// Create via: Project window > right-click > Create > TWR > Item.
    /// </summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "TWR/Item")]
    public class ItemData : ScriptableObject
    {
        [Tooltip("Stable ID for saves, voice triggers (D-080) and logs, e.g. \"ammo\".")]
        public string itemId = "item";

        [Tooltip("Localized name for the pause-menu inventory grid later (D-084, D-087). Table 'UI'.")]
        public LocalizedString displayName = new LocalizedString();

        [Tooltip("Most you can carry of this item. D-037 caps every stack at 99.")]
        [Range(1, Inventory.MaxStack)] public int maxStack = Inventory.MaxStack;

        [Header("World pickups (D-120, D-121)")]
        [Tooltip("How many one pickup of this item gives (Ammo: 12).")]
        [Min(1)] public int pickupAmount = 12;

        [Tooltip("Noise radius (m) when walking over a pickup of this item (D-079). Small: picking things up is quiet.")]
        [Min(0f)] public float pickupNoiseRadius = 2f;

        /// <summary>
        /// What happens when a collector walks over a pickup of this item (ItemPickup calls this). Default: as much as
        /// fits goes into the Inventory. Returns how many units were taken; 0 leaves the pickup where it is.
        /// Special items override it (e.g. HealthItemData heals instead, D-171).
        /// </summary>
        public virtual int Collect(GameObject collector, Inventory inventory, int amount)
        {
            return inventory != null ? inventory.Add(this, amount) : 0;
        }
    }
}
