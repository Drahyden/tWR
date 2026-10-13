using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Tunable wall muffling for the shared noise system (D-131, implements D-116).
    /// A wall between a noise and a listener does not block the noise, it SHRINKS its radius:
    /// effective radius = radius x (factor of wall 1) x (factor of wall 2) ... (at most maxWallsCounted walls,
    /// never below minimumFactor). A NoiseOccluder component on a collider picks a different factor
    /// (thin wall, thick wall, door panel).
    /// HOW IT IS FOUND: there is exactly one, at Assets/_Project/Data/Resources/NoiseSettings.asset.
    /// NoiseSystem loads it once with Resources.Load, so no scene needs a reference to it (it works in the
    /// test scene, the future hub and every room). TWR > Setup Project / Build Test Scene create it.
    /// </summary>
    [CreateAssetMenu(fileName = "NoiseSettings", menuName = "TWR/Noise Settings")]
    public class NoiseSettings : ScriptableObject
    {
        /// <summary>Resources.Load path (file must be called NoiseSettings and sit in a Resources folder).</summary>
        public const string ResourcesPath = "NoiseSettings";

        [Header("Wall muffling - D-131 (D-116)")]
        [Tooltip("Off = old radius-only behaviour (walls ignored).")]
        public bool muffleThroughWalls = true;
        [Tooltip("What counts as a wall: Default + Interactable. Player, Enemy, Weapon and Ignore Raycast are ALWAYS " +
                 "removed at runtime, so bodies and trigger volumes never muffle.")]
        public LayerMask occluderMask = 1; // Default; the builder adds Interactable (layer numbers aren't known here)
        [Tooltip("The check ray starts this far above the noise position (footsteps are emitted at floor level).")]
        public float sourceHeightOffset = 0.3f;

        [Header("Factors (radius is multiplied by these per wall)")]
        [Tooltip("Any wall without a NoiseOccluder component.")]
        [Range(0.1f, 1f)] public float defaultWallFactor = 0.5f;
        [Range(0.1f, 1f)] public float thinWallFactor = 0.6f;
        [Range(0.1f, 1f)] public float thickWallFactor = 0.3f;
        [Range(0.1f, 1f)] public float doorPanelFactor = 0.6f;
        [Tooltip("The total multiplier never goes below this: walls muffle, they don't fully block (D-116).")]
        [Range(0.01f, 1f)] public float minimumFactor = 0.1f;
        [Tooltip("Walls after this many are ignored.")]
        [Min(1)] public int maxWallsCounted = 4;

        /// <summary>The factor of one wall, from its optional NoiseOccluder.</summary>
        public float FactorFor(NoiseOccluder occluder)
        {
            float factor = defaultWallFactor;
            if (occluder != null)
            {
                switch (occluder.Kind)
                {
                    case NoiseOccluderKind.Thin:      factor = thinWallFactor; break;
                    case NoiseOccluderKind.Thick:     factor = thickWallFactor; break;
                    case NoiseOccluderKind.DoorPanel: factor = doorPanelFactor; break;
                    case NoiseOccluderKind.Custom:    factor = occluder.CustomFactor; break;
                }
            }
            return Mathf.Clamp(factor, minimumFactor, 1f);
        }
    }
}
