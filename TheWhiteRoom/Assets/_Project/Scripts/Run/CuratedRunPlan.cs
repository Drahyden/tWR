using UnityEngine;

namespace TWR.Run
{
    /// <summary>
    /// The hand-made first run (D-154, D-160), used for seed 0 (D-159) while no endings are saved.
    /// Not generated: the sections are listed here in order. Default per D-160: puzzle, combat,
    /// impossible geometry, fork room, then Escape = stealth + bull arena, Discover = silent + preacher arena.
    /// Create via: Project window > right-click > Create > TWR > Run > Curated Run Plan.
    /// </summary>
    [CreateAssetMenu(fileName = "CuratedRunPlan", menuName = "TWR/Run/Curated Run Plan")]
    public class CuratedRunPlan : ScriptableObject
    {
        [Tooltip("2-3 general sections before the fork (D-151, D-156).")]
        public SectionData[] preForkSections = new SectionData[0];
        public SectionData forkRoom;

        [Header("Escape branch")]
        public SectionData escapePathSection;
        public SectionData escapeBossArena;

        [Header("Discover branch")]
        public SectionData discoverPathSection;
        public SectionData discoverBossArena;

        /// <summary>Checks the plan against D-151/D-156. Returns false with a readable reason.</summary>
        public bool IsValid(out string problem)
        {
            if (preForkSections == null || preForkSections.Length < 2 || preForkSections.Length > 3)
            {
                problem = "needs 2-3 sections before the fork";
                return false;
            }
            for (int i = 0; i < preForkSections.Length; i++)
            {
                if (preForkSections[i] == null) { problem = $"pre-fork slot {i + 1} is empty"; return false; }
                if (!preForkSections[i].IsGeneral) { problem = $"pre-fork slot {i + 1} ('{preForkSections[i].DisplayId}') must be path None and not Fork/BossArena"; return false; }
            }
            if (forkRoom == null || forkRoom.type != SectionType.Fork) { problem = "fork room is empty or not of type Fork"; return false; }
            if (escapePathSection == null || !escapePathSection.CanBePathSectionFor(RunPath.Escape)) { problem = "Escape path section is empty or not usable on Escape"; return false; }
            if (discoverPathSection == null || !discoverPathSection.CanBePathSectionFor(RunPath.Discover)) { problem = "Discover path section is empty or not usable on Discover"; return false; }
            if (escapeBossArena == null || !escapeBossArena.IsBossArenaFor(RunPath.Escape)) { problem = "Escape boss arena is empty or not a BossArena tagged Escape"; return false; }
            if (discoverBossArena == null || !discoverBossArena.IsBossArenaFor(RunPath.Discover)) { problem = "Discover boss arena is empty or not a BossArena tagged Discover"; return false; }
            problem = null;
            return true;
        }
    }
}
