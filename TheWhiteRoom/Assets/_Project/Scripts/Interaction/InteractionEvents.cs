using System;
using UnityEngine;

namespace TWR.Interaction
{
    /// <summary>
    /// Shared static event raised whenever the player interacts with something. Future inner-voice
    /// triggers (D-080) and puzzle logic can listen here without references to the player.
    /// </summary>
    public static class InteractionEvents
    {
        /// <summary>(interactor, target). Use target.InteractionId to decide what to react to.</summary>
        public static event Action<GameObject, IInteractable> OnInteracted;

        public static void RaiseInteracted(GameObject interactor, IInteractable target)
        {
            OnInteracted?.Invoke(interactor, target);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OnInteracted = null;
    }
}
