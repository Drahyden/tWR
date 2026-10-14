using UnityEngine;

namespace TWR.Run
{
    /// <summary>
    /// One section template (D-155): an id, its type, free tags and which path it belongs to.
    /// General sections (path None, not Fork/Boss) can appear before the fork or as a path section;
    /// boss arenas must be tagged Escape or Discover (D-148, D-156). OnValidate warns about wrong setups.
    /// sceneName is reserved for step R2 (loading section scenes); R1 does not use it.
    /// Create via: Project window > right-click > Create > TWR > Run > Section Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Section_", menuName = "TWR/Run/Section Data")]
    public class SectionData : ScriptableObject
    {
        [Tooltip("Unique id used in logs and the debug overlay, e.g. 'section.puzzle'.")]
        public string id;
        public SectionType type = SectionType.Puzzle;
        [Tooltip("None = can appear on any path. Boss arenas must be Escape or Discover (D-156).")]
        public RunPath path = RunPath.None;
        [Tooltip("Free labels for later filtering (e.g. 'dark', 'portal').")]
        public string[] tags = new string[0];

        [Header("Step R2 (unused in R1)")]
        [Tooltip("Scene to load for this section. Empty for now.")]
        public string sceneName;

        /// <summary>Id, or the asset name if the id is empty.</summary>
        public string DisplayId => string.IsNullOrEmpty(id) ? name : id;

        /// <summary>Can be drawn before the fork: path None, not the fork room, not a boss arena (D-156).</summary>
        public bool IsGeneral => path == RunPath.None && type != SectionType.Fork && type != SectionType.BossArena;

        /// <summary>Can be the path section of this branch: general, or tagged with this path (D-156).</summary>
        public bool CanBePathSectionFor(RunPath branch) =>
            type != SectionType.Fork && type != SectionType.BossArena && (path == RunPath.None || path == branch);

        /// <summary>Is a boss arena tagged with this path (D-148).</summary>
        public bool IsBossArenaFor(RunPath branch) => type == SectionType.BossArena && path == branch && branch != RunPath.None;

        void OnValidate()
        {
            if (type == SectionType.BossArena && path == RunPath.None)
                Debug.LogWarning($"[TWR] Section '{name}' is a boss arena without a path. Set Path to Escape or Discover (D-148, D-156).", this);
            if (type == SectionType.Fork && path != RunPath.None)
                Debug.LogWarning($"[TWR] Section '{name}' is the fork room but has path {path}. The fork room belongs to no path; set Path to None.", this);
        }
    }
}
