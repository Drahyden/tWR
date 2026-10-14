using UnityEngine;
using TWR.Core;

namespace TWR.Items
{
    /// <summary>
    /// Walk-over item pickup, e.g. an ammo box (D-121, D-050). When something with an Inventory
    /// touches the trigger, as much as fits is added (D-120: ammo +12, capped at 99 per D-037);
    /// any leftover stays in the pickup, and a full stack means nothing happens. Collecting emits a quiet
    /// Pickup noise (D-079) and ItemEvents.PickedUp (D-080).
    /// Setup (done by TWR > Build Test Scene): trigger collider on the "Ignore Raycast" layer so it never
    /// blocks movement, bullets or the interaction ray, plus a kinematic Rigidbody so trigger events fire.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ItemPickup : MonoBehaviour
    {
        [SerializeField] ItemData item;
        [Tooltip("0 = use the item's pickupAmount (Ammo: 12).")]
        [SerializeField, Min(0)] int amountOverride;

        int remaining;
        Collider lastOther;          // cache: GetComponentInParent only runs for a NEW collider
        Inventory lastInventory;

        public ItemData Item => item;
        public int Remaining => remaining;

        void Awake()
        {
            remaining = amountOverride > 0 ? amountOverride : (item != null ? item.pickupAmount : 0);
            var trigger = GetComponent<Collider>();
            if (!trigger.isTrigger)
            {
                Debug.LogWarning($"[ItemPickup] '{name}': collider was not a trigger; fixing it so it doesn't block movement.", this);
                trigger.isTrigger = true;
            }
        }

        // Stay as well as Enter: if your stack was full when you stepped on it, reloading while still
        // standing on it lets you take the rest without stepping off and on again.
        void OnTriggerEnter(Collider other) => TryCollect(other);
        void OnTriggerStay(Collider other) => TryCollect(other);

        void TryCollect(Collider other)
        {
            if (item == null || remaining <= 0) return;

            if (other != lastOther)
            {
                lastOther = other;
                lastInventory = other.GetComponentInParent<Inventory>();
            }
            if (lastInventory == null) return;

            // The item decides what "collect" means: ammo goes into the Inventory, health heals (D-171).
            int taken = item.Collect(lastInventory.gameObject, lastInventory, remaining);
            if (taken <= 0) return; // stack full / HP full: stay here, do nothing

            remaining -= taken;
            GameObject collector = lastInventory.gameObject;
            NoiseSystem.Emit(transform.position, item.pickupNoiseRadius, NoiseType.Pickup, collector);
            ItemEvents.RaisePickedUp(collector, item, taken);

            if (remaining <= 0) Destroy(gameObject);
        }
    }
}
