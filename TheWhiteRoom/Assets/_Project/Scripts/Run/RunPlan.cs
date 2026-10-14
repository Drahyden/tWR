using System.Collections.Generic;
using System.Text;

namespace TWR.Run
{
    /// <summary>
    /// One run's layout, fixed at run start (D-151, D-156): 2-3 pre-fork sections, the fork room, and BOTH
    /// branches (Escape and Discover: path section + boss arena). Which branch is played is chosen at the
    /// fork; the plan itself never changes. "Stops" are what the RunManager steps through:
    /// -1 = hub lobby, 0..ForkIndex-1 = pre-fork sections, ForkIndex = fork room, then path section, boss arena.
    /// Lobby and fork room are connectors, not sections (D-156), so SectionCount is 4-5.
    /// Plain C# (no MonoBehaviour), so it can be unit-tested.
    /// </summary>
    public class RunPlan
    {
        public const int LobbyIndex = -1;

        public uint Seed { get; }
        /// <summary>True = the authored first-run plan (seed 0, D-154, D-159).</summary>
        public bool IsCurated { get; }
        public IReadOnlyList<SectionData> PreForkSections { get; }
        public SectionData ForkRoom { get; }
        public SectionData EscapePathSection { get; }
        public SectionData EscapeBossArena { get; }
        public SectionData DiscoverPathSection { get; }
        public SectionData DiscoverBossArena { get; }

        public RunPlan(uint seed, bool isCurated, IReadOnlyList<SectionData> preFork, SectionData forkRoom,
                       SectionData escapePath, SectionData escapeBoss, SectionData discoverPath, SectionData discoverBoss)
        {
            Seed = seed;
            IsCurated = isCurated;
            PreForkSections = preFork;
            ForkRoom = forkRoom;
            EscapePathSection = escapePath;
            EscapeBossArena = escapeBoss;
            DiscoverPathSection = discoverPath;
            DiscoverBossArena = discoverBoss;
        }

        /// <summary>Index of the fork room stop.</summary>
        public int ForkIndex => PreForkSections.Count;
        /// <summary>Index of the path section stop (after the fork).</summary>
        public int PathSectionIndex => ForkIndex + 1;
        /// <summary>Index of the boss arena stop (the last one).</summary>
        public int BossIndex => ForkIndex + 2;
        /// <summary>Real sections only (no lobby, no fork room): 4-5 (D-156).</summary>
        public int SectionCount => PreForkSections.Count + 2;

        public SectionData GetPathSection(RunPath path) =>
            path == RunPath.Escape ? EscapePathSection : path == RunPath.Discover ? DiscoverPathSection : null;

        public SectionData GetBossArena(RunPath path) =>
            path == RunPath.Escape ? EscapeBossArena : path == RunPath.Discover ? DiscoverBossArena : null;

        /// <summary>The section at a stop index. Null for the lobby, and after the fork while no path is chosen.</summary>
        public SectionData GetStop(int index, RunPath activePath)
        {
            if (index < 0) return null;
            if (index < ForkIndex) return PreForkSections[index];
            if (index == ForkIndex) return ForkRoom;
            if (index == PathSectionIndex) return GetPathSection(activePath);
            if (index == BossIndex) return GetBossArena(activePath);
            return null;
        }

        /// <summary>1-based section number for logs ("section 2/5"), or 0 for the lobby and the fork room.</summary>
        public int SectionNumber(int index)
        {
            if (index < 0 || index == ForkIndex) return 0;
            return index < ForkIndex ? index + 1 : index; // after the fork, skip the fork room in the count
        }

        /// <summary>One-line description for the "[Run]" start log. Allocates; call only at run start.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("lobby");
            foreach (SectionData s in PreForkSections) sb.Append(" > ").Append(Name(s));
            sb.Append(" > [").Append(Name(ForkRoom)).Append(']');
            sb.Append(" > Escape: ").Append(Name(EscapePathSection)).Append(" + ").Append(Name(EscapeBossArena));
            sb.Append(" | Discover: ").Append(Name(DiscoverPathSection)).Append(" + ").Append(Name(DiscoverBossArena));
            return sb.ToString();
        }

        public static string Name(SectionData section) => section != null ? section.DisplayId : "(missing)";
    }
}
