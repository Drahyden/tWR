using UnityEngine;
using TWR.Combat;

namespace TWR.Items
{
    /// <summary>
    /// Placeholder health pickup item (D-171): the only way to get HP back during a run (D-162). It uses the normal
    /// walk-over ItemPickup flow (trigger, quiet Pickup noise, ItemEvents.PickedUp for voice triggers), but instead of
    /// going into the Inventory it heals the collector by healAmount, never above max HP. At full HP nothing happens
    /// and the pickup stays for later. Amount and placement get tuned after playtesting (§14).
    /// Create via: Project window > right-click > Create > TWR > Health Item.
    /// </summary>
    [CreateAssetMenu(fileName = "Item_Health", menuName = "TWR/Health Item")]
    public class HealthItemData : ItemData
    {
        [Header("Healing - D-171")]
        [Tooltip("HP restored by one pickup. Clamped to max HP.")]
        [Min(0f)] public float healAmount = 25f;

        public override int Collect(GameObject collector, Inventory inventory, int amount)
        {
            if (collector == null || amount <= 0) return 0;
            if (!collector.TryGetComponent(out IHealable target) || !target.NeedsHealing) return 0; // full HP: not wasted
            target.Heal(healAmount);
            return amount; // one pickup = one use, even if it healed less than healAmount
        }
    }
}
