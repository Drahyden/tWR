namespace TWR.Run
{
    /// <summary>
    /// What a section roll decides (D-172). The number is part of the random stream, so never renumber.
    /// Boss (D-189): the boss fight's own stream (Bull fast-charge counts, Preacher teleport choices and volley sizes).
    /// </summary>
    public enum RollCategory
    {
        Enemies = 0,
        Pickups = 1,
        Puzzle = 2,
        Boss = 3
    }

    /// <summary>
    /// Where a section's content comes from (D-172). Sandbox = the Test_PlayerController scene (no rolls at all);
    /// Curated = seed 0, the authored first run (D-154): authored defaults everywhere; Seeded = rolled from the seed.
    /// </summary>
    public enum SectionRollMode
    {
        Sandbox,
        Curated,
        Seeded
    }

    /// <summary>
    /// Everything a loaded section needs to roll its content (R3, D-172): the run seed, which branch the stop is on
    /// (0 = pre-fork or fork room, 1 = Escape, 2 = Discover), the stop index in the plan and the mode. The RunManager
    /// builds it when a section scene finishes loading and hands it to SectionRoller before any Start runs.
    /// Each category gets its own SeededRandom stream: 0x100 + branch x 0x40 + sectionIndex x 4 + category. The plan
    /// itself uses streams 0-2 (RunPlanGenerator), so section rolls can never change an existing seed's plan, and the
    /// same seed + section always rolls the same content, however often the section is loaded.
    /// </summary>
    public readonly struct SectionRollContext
    {
        public const uint FirstSectionStream = 0x100;
        public const int BranchStride = 0x40;
        public const int SectionStride = 4;
        /// <summary>Highest stop index whose streams stay inside its branch block (0x40 / 4 = 16 stops).</summary>
        public const int MaxSectionIndex = BranchStride / SectionStride - 1;

        public readonly uint Seed;
        /// <summary>0 = pre-fork section or fork room, 1 = Escape branch, 2 = Discover branch.</summary>
        public readonly int Branch;
        /// <summary>The stop index in the RunPlan (0 = first section after the lobby).</summary>
        public readonly int SectionIndex;
        public readonly SectionRollMode Mode;
        /// <summary>The section template (null in the sandbox).</summary>
        public readonly SectionData Section;

        public SectionRollContext(uint seed, int branch, int sectionIndex, SectionRollMode mode, SectionData section)
        {
            Seed = seed;
            Branch = branch;
            SectionIndex = sectionIndex;
            Mode = mode;
            Section = section;
        }

        /// <summary>Curated and sandbox use the authored defaults (D-172: seed 0 never rolls).</summary>
        public bool UsesDefaults => Mode != SectionRollMode.Seeded;

        public string SectionId => Section != null ? Section.DisplayId : "(no section)";

        /// <summary>The D-172 stream number for one category of one stop.</summary>
        public static uint StreamFor(int branch, int sectionIndex, RollCategory category)
        {
            if (sectionIndex < 0) sectionIndex = 0;
            if (sectionIndex > MaxSectionIndex) sectionIndex = MaxSectionIndex;
            return FirstSectionStream + (uint)branch * BranchStride + (uint)sectionIndex * SectionStride + (uint)category;
        }

        /// <summary>A fresh generator for one category of this stop. Same context = same numbers (D-172).</summary>
        public SeededRandom CreateRandom(RollCategory category) => new SeededRandom(Seed, StreamFor(Branch, SectionIndex, category));

        /// <summary>Branch number for a stop: before or at the fork = 0, after it = the chosen path (1 Escape, 2 Discover).</summary>
        public static int BranchOf(RunPlan plan, int stopIndex, RunPath activePath)
        {
            if (plan == null || stopIndex <= plan.ForkIndex) return 0;
            return activePath == RunPath.Escape ? 1 : activePath == RunPath.Discover ? 2 : 0;
        }

        public static SectionRollContext Sandbox => new SectionRollContext(0u, 0, 0, SectionRollMode.Sandbox, null);
    }
}
