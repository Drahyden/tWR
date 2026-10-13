using UnityEngine;

namespace TWR.Core
{
    /// <summary>How much a wall muffles noise (D-131). Values live in NoiseSettings.</summary>
    public enum NoiseOccluderKind
    {
        /// <summary>Same as having no NoiseOccluder: NoiseSettings.defaultWallFactor (0.5).</summary>
        Default,
        /// <summary>Thin partition (0.6).</summary>
        Thin,
        /// <summary>Thick / outer wall (0.3).</summary>
        Thick,
        /// <summary>A door panel (0.6). Opening the door moves the panel out of the way, so the noise gets through.</summary>
        DoorPanel,
        /// <summary>Use CustomFactor below.</summary>
        Custom
    }

    /// <summary>
    /// Optional tag for a collider that muffles noise differently from a normal wall (D-131, D-116).
    /// Put it on the SAME GameObject as the collider. Walls without it use the default factor (0.5).
    /// It only stores a choice; NoiseSystem does the work.
    /// </summary>
    public class NoiseOccluder : MonoBehaviour
    {
        [SerializeField] NoiseOccluderKind kind = NoiseOccluderKind.Default;
        [Tooltip("Only used when Kind = Custom. 1 = no muffling, 0.1 = very thick.")]
        [Range(0.1f, 1f)] [SerializeField] float customFactor = 0.5f;

        public NoiseOccluderKind Kind { get => kind; set => kind = value; }
        public float CustomFactor => customFactor;
    }
}
