using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// The result of checking one noise against one listener (D-131, D-116): the noise itself plus
    /// how far away the listener's ears were, how many walls were in between and the effective (muffled)
    /// radius. IsHeard = Distance &lt;= EffectiveRadius. A struct, so checking noises makes no garbage (D-100).
    /// </summary>
    public readonly struct HeardNoise
    {
        public readonly NoiseEvent Noise;
        public readonly INoiseListener Listener;
        /// <summary>Where the wall-check ray started (noise position + a small height offset).</summary>
        public readonly Vector3 RayStart;
        /// <summary>The listener's HearingPosition at the time of the check.</summary>
        public readonly Vector3 ListenerPosition;
        public readonly float Distance;
        /// <summary>Noise radius after wall muffling. Equal to Noise.Radius when nothing was in the way.</summary>
        public readonly float EffectiveRadius;
        /// <summary>How many walls (colliders) were between the noise and the listener (max NoiseSettings.maxWallsCounted).</summary>
        public readonly int WallCount;
        public readonly bool IsHeard;

        public HeardNoise(in NoiseEvent noise, INoiseListener listener, Vector3 rayStart, Vector3 listenerPosition,
                          float distance, float effectiveRadius, int wallCount)
        {
            Noise = noise;
            Listener = listener;
            RayStart = rayStart;
            ListenerPosition = listenerPosition;
            Distance = distance;
            EffectiveRadius = effectiveRadius;
            WallCount = wallCount;
            IsHeard = distance <= effectiveRadius;
        }

        public bool IsMuffled => WallCount > 0;

        /// <summary>EffectiveRadius / Noise.Radius (1 = not muffled).</summary>
        public float MuffleFactor => Noise.Radius > 0f ? EffectiveRadius / Noise.Radius : 1f;
    }
}
