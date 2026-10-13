using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Shared noise-event hub (D-043, D-052, D-079). Anything can call Emit(); every registered
    /// listener inside the radius is told about it. Radius only, no wall occlusion (by design for now).
    /// Static so gun, player, props and enemies all use the same system without scene references.
    /// </summary>
    public static class NoiseSystem
    {
        static readonly List<INoiseListener> listeners = new List<INoiseListener>(32);

        /// <summary>Raised for EVERY noise (heard or not). Used by NoiseDebugDrawer; enemies should use INoiseListener.</summary>
        public static event Action<NoiseEvent> NoiseEmitted;

        public static int ListenerCount => listeners.Count;

        public static void Register(INoiseListener listener)
        {
            if (listener != null && !listeners.Contains(listener))
                listeners.Add(listener);
        }

        public static void Unregister(INoiseListener listener)
        {
            listeners.Remove(listener);
        }

        /// <summary>Make a noise at a position. Listeners within radius metres hear it.</summary>
        public static void Emit(Vector3 position, float radius, NoiseType type, GameObject source)
        {
            if (radius <= 0f) return;

            var noise = new NoiseEvent(position, radius, type, source, UnityEngine.Time.time);
            NoiseEmitted?.Invoke(noise);

            float radiusSqr = radius * radius;
            // Iterate backwards so a listener may unregister itself while being notified.
            for (int i = listeners.Count - 1; i >= 0; i--)
            {
                if (i >= listeners.Count) continue;
                INoiseListener listener = listeners[i];
                if ((position - listener.HearingPosition).sqrMagnitude <= radiusSqr)
                    listener.OnNoiseHeard(noise);
            }
        }

        // Clears static state when entering Play mode with "Enter Play Mode Options" (no domain reload).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            listeners.Clear();
            NoiseEmitted = null;
        }
    }
}
