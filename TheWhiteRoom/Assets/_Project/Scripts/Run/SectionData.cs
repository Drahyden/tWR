using UnityEngine;

namespace TWR.Run
{
    /// <summary>
    /// One section template (D-155): an id, its type, free tags and which path it belongs to.
    /// General sections (path None, not Fork/Boss) can appear before the fork or as a path section;
    /// boss arenas must be tagged Escape or Discover (D-148, D-156). OnValidate warns about wrong setups.
    /// R2 (D-161, D-165): sceneName is the content scene loaded additively next to the Core scene, and
    /// exitCondition says what unlocks the section's exit door. "TWR/Run/Build Run Scenes" fills both.
    /// Create via: Project window > right-click > Create > TWR > Run > Section Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Section_", menuName = "TWR/Run/Section Data")]
    public class SectionData : ScriptableObject
    {
        [Tooltip("Unique id used in logs, voice triggers and the debug overlay, e.g. 'section.puzzle'.")]
        public string id;
        public SectionType type = SectionType.Puzzle;
        [Tooltip("None = can appear on any path. Boss arenas must be Escape or Discover (D-156).")]
        public RunPath path = RunPath.None;
        [Tooltip("Free labels for later filtering (e.g. 'dark', 'portal').")]
        public string[] tags = new string[0];

        [Header("Scene + exit - D-161, D-165")]
        [Tooltip("Content scene loaded additively next to Core for this section (must be in Build Settings).")]
        public string sceneName;
        [Tooltip("What unlocks the exit door. Defaults: puzzle = MainPuzzlesSolved; boss arena = BossDefeated; others = None. " +
                 "Optional puzzles never gate an exit (D-104).")]
        public SectionExitCondition exitCondition = SectionExitCondition.None;

        /// <summary>Id, or the asset name if the id is empty.</summary>
        public string DisplayId => string.IsNullOrEmpty(id) ? name : id;

        /// <summary>Can be drawn before the fork: path None, not the fork room, not a boss arena (D-156).</summary>
        public bool IsGeneral => path == RunPath.None && type != SectionType.Fork && type != SectionType.BossArena;

        /// <summary>Can be the path section of this branch: general, or tagged with this path (D-156).</summary>
        public bool CanBePathSectionFor(RunPath branch) =>
            type != SectionType.Fork && type != SectionType.BossArena && (path == RunPath.None || path == branch);

        /// <summary>Is a boss arena tagged with this path (D-148).</summary>
        public bool IsBossArenaFor(RunPath branch) => type == SectionType.BossArena && path == branch && branch != RunPath.None;

        /// <summary>The D-165 default exit condition for a section type.</summary>
        public static SectionExitCondition DefaultExitCondition(SectionType type)
        {
            switch (type)
            {
                case SectionType.Puzzle: return SectionExitCondition.MainPuzzlesSolved;
                case SectionType.BossArena: return SectionExitCondition.BossDefeated;
                default: return SectionExitCondition.None;
            }
        }

        void OnValidate()
        {
            if (type == SectionType.BossArena && path == RunPath.None)
                Debug.LogWarning($"[TWR] Section '{name}' is a boss arena without a path. Set Path to Escape or Discover (D-148, D-156).", this);
            if (type == SectionType.Fork && path != RunPath.None)
                Debug.LogWarning($"[TWR] Section '{name}' is the fork room but has path {path}. The fork room belongs to no path; set Path to None.", this);
            if (exitCondition == SectionExitCondition.BossDefeated && type != SectionType.BossArena)
                Debug.LogWarning($"[TWR] Section '{name}' waits for a boss but is not a boss arena: its exit would never open.", this);
        }
    }
}
