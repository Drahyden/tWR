using System;
using System.Collections.Generic;
using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Shared noise-event hub (D-043, D-052, D-079, D-114). Anything can call Emit(); every registered
    /// listener inside the radius is checked. Walls MUFFLE noise (D-116, D-131): one ray from the
    /// noise (+0.3 m) to the listener's ears counts the walls in between, and each wall multiplies the radius
    /// by its factor (0.5 by default, NoiseOccluder for thin/thick/door). The listener hears the noise only if
    /// it is within that smaller "effective radius". Factors and the wall mask live in NoiseSettings
    /// (loaded once from Resources). Static so gun, player, props and enemies share it without scene references.
    /// Allocation-free: one shared RaycastHit buffer, struct events (D-100).
    /// </summary>
    public static class NoiseSystem
    {
        const int MaxOccluderHits = 8;

        static readonly List<INoiseListener> listeners = new List<INoiseListener>(32);
        static readonly RaycastHit[] occluderHits = new RaycastHit[MaxOccluderHits];

        static NoiseSettings settings;
        static bool settingsResolved;
        static int occluderMask;

        /// <summary>Raised for EVERY noise (heard or not). Used by NoiseDebugDrawer; enemies should use INoiseListener.</summary>
        public static event Action<NoiseEvent> NoiseEmitted;

        /// <summary>Debug hook: one listener inside the raw radius was checked (heard or muffled out).</summary>
        public delegate void ListenerCheckedHandler(in HeardNoise result);

        /// <summary>Raised for every listener inside the RAW radius, heard or not. Used by debug tools only.</summary>
        public static event ListenerCheckedHandler ListenerChecked;

        public static int ListenerCount => listeners.Count;

        /// <summary>The active muffling settings (loaded from Resources on first use, or built-in defaults).</summary>
        public static NoiseSettings Settings
        {
            get
            {
                if (!settingsResolved) ResolveSettings();
                return settings;
            }
        }

        /// <summary>Swap the settings at runtime (tests, special rooms). Pass null to reload the Resources asset.</summary>
        public static void UseSettings(NoiseSettings newSettings)
        {
            settings = newSettings;
            settingsResolved = newSettings != null;
            if (settingsResolved) occluderMask = GameLayers.WithoutActors(settings.occluderMask);
        }

        public static void Register(INoiseListener listener)
        {
            if (listener != null && !listeners.Contains(listener))
                listeners.Add(listener);
        }

        public static void Unregister(INoiseListener listener)
        {
            listeners.Remove(listener);
        }

        /// <summary>Make a noise at a position. Listeners within radius metres (after wall muffling) hear it.</summary>
        public static void Emit(Vector3 position, float radius, NoiseType type, GameObject source)
        {
            if (radius <= 0f) return;

            var noise = new NoiseEvent(position, radius, type, source, UnityEngine.Time.time);
            NoiseEmitted?.Invoke(noise);

            NoiseSettings s = Settings;
            Vector3 rayStart = position + Vector3.up * s.sourceHeightOffset;
            float radiusSqr = radius * radius;

            // Iterate backwards so a listener may unregister itself while being notified.
            for (int i = listeners.Count - 1; i >= 0; i--)
            {
                if (i >= listeners.Count) continue;
                INoiseListener listener = listeners[i];
                Vector3 ears = listener.HearingPosition;
                float distanceSqr = (position - ears).sqrMagnitude;
                if (distanceSqr > radiusSqr) continue; // only listeners inside the raw radius are checked

                float factor = MuffleFactor(rayStart, ears, listener, source, s, out int walls);
                var result = new HeardNoise(noise, listener, rayStart, ears, Mathf.Sqrt(distanceSqr), radius * factor, walls);

                ListenerChecked?.Invoke(result);
                if (result.IsHeard) listener.OnNoiseHeard(result);
            }
        }

        /// One ray from the noise to the ears; every wall in between multiplies the radius by its factor.
        static float MuffleFactor(Vector3 from, Vector3 to, INoiseListener listener, GameObject source, NoiseSettings s, out int walls)
        {
            walls = 0;
            if (!s.muffleThroughWalls) return 1f;

            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f) return 1f;

            int count = Physics.RaycastNonAlloc(from, delta / length, occluderHits, length, occluderMask, QueryTriggerInteraction.Ignore);

            // Never let the listener's own body or the noise maker's own body count as a wall
            // (the layer mask already removes Player and Enemy; this also covers anything on Default).
            Transform listenerRoot = listener is Component c ? c.transform : null;
            Transform sourceRoot = source != null ? source.transform : null;

            float factor = 1f;
            for (int i = 0; i < count; i++)
            {
                Collider wall = occluderHits[i].collider;
                Transform t = wall.transform;
                if (listenerRoot != null && t.IsChildOf(listenerRoot)) continue;
                if (sourceRoot != null && t.IsChildOf(sourceRoot)) continue;
                if (walls >= s.maxWallsCounted) break;

                walls++; // a raycast reports each collider at most once, so these are distinct colliders
                wall.TryGetComponent(out NoiseOccluder occluder); // TryGetComponent: no editor garbage when missing
                factor *= s.FactorFor(occluder);
            }
            return Mathf.Max(s.minimumFactor, factor);
        }

        static void ResolveSettings()
        {
            settingsResolved = true;
            settings = Resources.Load<NoiseSettings>(NoiseSettings.ResourcesPath);
            if (settings == null)
            {
                Debug.LogWarning("[NoiseSystem] No Resources/NoiseSettings asset found: using built-in muffling defaults. " +
                                 "Run TWR > Setup Project to create it.");
                settings = ScriptableObject.CreateInstance<NoiseSettings>();
                settings.occluderMask = LayerMask.GetMask("Default", GameLayers.Interactable);
            }
            occluderMask = GameLayers.WithoutActors(settings.occluderMask);
        }

        // Clears static state when entering Play mode with "Enter Play Mode Options" (no domain reload).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            listeners.Clear();
            NoiseEmitted = null;
            ListenerChecked = null;
            settings = null;
            settingsResolved = false;
        }
    }
}
