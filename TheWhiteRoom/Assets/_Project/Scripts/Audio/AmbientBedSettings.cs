using UnityEngine;

namespace TWR.Audio
{
    /// <summary>
    /// Tunables of the ambient bed (D-077, D-223): the 2D fluorescent hum volume and the sparse, randomized distant office
    /// one-shots (phone, printer, keyboard): how often, how far from the player (15-40 m), how loud, how 3D
    /// (spatialBlend about 0.6) and their max distance (60 m or less, so they stay under the D-144 portal distance).
    /// Ranges are Vector2: x = minimum, y = maximum. The clips themselves live in the AudioLibrary (D-227).
    /// Create via: Create > TWR > Audio > Ambient Bed Settings (the A1 builder makes Data/Audio/AmbientBedSettings).
    /// </summary>
    [CreateAssetMenu(fileName = "AmbientBedSettings", menuName = "TWR/Audio/Ambient Bed Settings")]
    public class AmbientBedSettings : ScriptableObject
    {
        [Header("Fluorescent hum (2D loop) - D-077")]
        [Range(0f, 1f)] public float humVolume = 0.12f;
        [Tooltip("Overtime after the button (D-221): seconds for the hum to fade out.")]
        [Min(0f)] public float humFadeOutSeconds = 2f;

        [Header("Distant office one-shots - D-223")]
        [Tooltip("Seconds between two one-shots (random in x..y). Sparse on purpose.")]
        public Vector2 intervalSeconds = new Vector2(14f, 38f);
        [Tooltip("Delay before the first one-shot after they are switched on (random 0.5x..1x of the interval).")]
        [Min(0f)] public float firstDelayScale = 0.5f;
        [Tooltip("Distance from the player in metres (random in x..y), in a random direction.")]
        public Vector2 distanceMetres = new Vector2(15f, 40f);
        [Tooltip("Height above the player's feet in metres (random in x..y).")]
        public Vector2 heightMetres = new Vector2(0.5f, 2.5f);
        [Tooltip("Volume multiplier (random in x..y), on top of the AudioLibrary entry's own volume.")]
        public Vector2 volume = new Vector2(0.35f, 0.6f);
        [Tooltip("0 = flat 2D, 1 = fully 3D. About 0.6 = 'somewhere over there' without hard panning.")]
        [Range(0f, 1f)] public float spatialBlend = 0.6f;
        [Tooltip("Full volume up to this distance (linear roll-off).")]
        [Min(0.5f)] public float minDistance = 5f;
        [Tooltip("Silent beyond this distance. Keep at 60 m or less (D-144, D-227).")]
        [Range(5f, 60f)] public float maxDistance = 60f;
        [Tooltip("How many one-shots can overlap (small pool of AudioSources, created once).")]
        [Range(1, 4)] public int poolSize = 3;

        void OnValidate()
        {
            intervalSeconds = Ordered(intervalSeconds, 1f);
            distanceMetres = Ordered(distanceMetres, 0f);
            heightMetres = Ordered(heightMetres, -10f);
            volume = new Vector2(Mathf.Clamp01(volume.x), Mathf.Clamp01(volume.y));
            volume = Ordered(volume, 0f);
            if (distanceMetres.y > maxDistance) distanceMetres.y = maxDistance;
            if (distanceMetres.x > distanceMetres.y) distanceMetres.x = distanceMetres.y;
        }

        static Vector2 Ordered(Vector2 range, float minimum)
        {
            float a = Mathf.Max(minimum, range.x), b = Mathf.Max(minimum, range.y);
            return a <= b ? new Vector2(a, b) : new Vector2(b, a);
        }
    }
}
