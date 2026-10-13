using System;
using UnityEngine;

namespace TWR.Items
{
    /// <summary>
    /// Shared static event raised when someone picks up an item (D-121). Future inner-voice
    /// triggers (D-080) can react to pickups without references to the pickup or the player.
    /// </summary>
    public static class ItemEvents
    {
        /// <summary>(collector, item, amount taken).</summary>
        public static event Action<GameObject, ItemData, int> PickedUp;

        public static void RaisePickedUp(GameObject collector, ItemData item, int amount)
        {
            PickedUp?.Invoke(collector, item, amount);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => PickedUp = null;
    }
}
