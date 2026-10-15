using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>What kind of walkable surface an ArenaPlatform is (D-183, D-186).</summary>
    public enum ArenaPlatformKind
    {
        /// <summary>The arena floor: the start of the reachability check.</summary>
        Floor,
        /// <summary>A free-standing platform (reached by walking or jumping).</summary>
        Platform,
        /// <summary>A stair step (solid down to the floor, rises at most 0.3 m from its neighbour).</summary>
        Step,
        /// <summary>A tilted walkway; must be 40 degrees or less (D-186).</summary>
        Ramp
    }

    /// <summary>
    /// Marks a BoxCollider as a walkable surface of a boss arena, for the editor reachability check (D-183, D-186): the
    /// validator builds a graph of every platform top from these markers, using the step / jump-gap / headroom rules with
    /// the numbers in PlayerTuning, and warns about any platform that can't be reached from the floor with base movement
    /// (walk, sprint, one jump; D-061), or that has no way back down. No runtime logic, no per-frame cost.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public class ArenaPlatform : MonoBehaviour
    {
        [Tooltip("Stable id for validation messages, e.g. 'preacher.l1.step.03'.")]
        [SerializeField] string platformId = "";
        [SerializeField] ArenaPlatformKind kind = ArenaPlatformKind.Platform;

        public string PlatformId => string.IsNullOrEmpty(platformId) ? name : platformId;
        public ArenaPlatformKind Kind => kind;
    }
}
