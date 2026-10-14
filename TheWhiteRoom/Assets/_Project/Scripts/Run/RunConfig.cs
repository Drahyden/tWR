using System.Collections.Generic;
using UnityEngine;

namespace TWR.Run
{
    /// <summary>
    /// Everything the RunManager needs to build a run (D-151, D-156): the section pool, the fork room,
    /// the boss arenas (one per path, D-148), the curated first-run plan (D-154, D-160) and the lobby scene
    /// a run returns to when it ends (D-157). Validate() is shared by the RunManager and the editor menu
    /// "TWR/Run/Validate Run Setup". Create via: Create > TWR > Run > Run Config.
    /// </summary>
    [CreateAssetMenu(fileName = "RunConfig", menuName = "TWR/Run/Run Config")]
    public class RunConfig : ScriptableObject
    {
        [Header("Scenes - D-057, D-157")]
        [Tooltip("The hub lobby scene a run starts in and returns to. For the slice the whole test scene is the lobby.")]
        public string lobbySceneName = "Test_PlayerController";

        [Header("Run length - D-151, D-156")]
        [Range(1, 4)] public int minPreForkSections = 2;
        [Range(1, 4)] public int maxPreForkSections = 3;

        [Header("Sections - D-155")]
        [Tooltip("Templates for pre-fork and path sections. Fork and boss arenas in here are ignored.")]
        public SectionData[] sectionPool = new SectionData[0];
        public SectionData forkRoom;
        [Tooltip("Boss arenas, each tagged Escape or Discover (D-148).")]
        public SectionData[] bossArenas = new SectionData[0];

        [Header("First run - D-154, D-159, D-160")]
        [Tooltip("Used for seed 0.")]
        public CuratedRunPlan curatedPlan;

        /// <summary>Fills errors (run can't work) and warnings (works, but not as designed). True if no errors.</summary>
        public bool Validate(List<string> errors, List<string> warnings)
        {
            if (string.IsNullOrEmpty(lobbySceneName)) errors.Add("Lobby scene name is empty.");
            if (minPreForkSections < 1 || maxPreForkSections < minPreForkSections)
                errors.Add($"Pre-fork section count {minPreForkSections}-{maxPreForkSections} is invalid.");
            else if (minPreForkSections != 2 || maxPreForkSections != 3)
                warnings.Add($"Pre-fork section count is {minPreForkSections}-{maxPreForkSections}; D-151 says 2-3.");

            var unique = new HashSet<SectionData>();
            if (sectionPool != null)
                foreach (SectionData s in sectionPool)
                {
                    if (s == null) { warnings.Add("Section pool has an empty slot."); continue; }
                    if (s.IsGeneral) unique.Add(s);
                    else warnings.Add($"Pool entry '{s.DisplayId}' is not a general section (path None, not Fork/Boss); it is only used as a path section if path-tagged.");
                }
            if (unique.Count == 0) errors.Add("Section pool has no general sections (path None, not Fork/BossArena).");
            else if (unique.Count < 3) warnings.Add($"Section pool has only {unique.Count} unique general sections; fewer than 3 means repeats in a run.");

            if (forkRoom == null) errors.Add("Fork room is not assigned.");
            else if (forkRoom.type != SectionType.Fork) errors.Add($"Fork room '{forkRoom.DisplayId}' is not of type Fork.");

            bool escapeBoss = false, discoverBoss = false;
            if (bossArenas != null)
                foreach (SectionData b in bossArenas)
                {
                    if (b == null) { warnings.Add("Boss arena list has an empty slot."); continue; }
                    if (b.type != SectionType.BossArena) errors.Add($"Boss arena '{b.DisplayId}' is not of type BossArena.");
                    else if (b.path == RunPath.None) errors.Add($"Boss arena '{b.DisplayId}' has no path (D-148, D-156).");
                    escapeBoss |= b.IsBossArenaFor(RunPath.Escape);
                    discoverBoss |= b.IsBossArenaFor(RunPath.Discover);
                }
            if (!escapeBoss) errors.Add("No boss arena tagged Escape.");
            if (!discoverBoss) errors.Add("No boss arena tagged Discover.");

            if (curatedPlan == null) errors.Add("Curated run plan is not assigned (needed for the first run, D-154).");
            else if (!curatedPlan.IsValid(out string problem)) errors.Add("Curated run plan is invalid: " + problem + ".");

            return errors.Count == 0;
        }
    }
}
