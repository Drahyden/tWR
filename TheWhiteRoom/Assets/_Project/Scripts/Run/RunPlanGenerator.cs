using System.Collections.Generic;

namespace TWR.Run
{
    /// <summary>
    /// Builds a RunPlan from a seed (D-151, D-153, D-156, D-159). Pure C#, no UnityEngine.Random, so the
    /// same seed always gives the same plan. Seed 0 = the curated first-run plan asset (D-154, D-160).
    /// Otherwise: 2-3 general sections without repeats (stream 0), the fork room, then each branch from its own
    /// stream (Escape = 1, Discover = 2): a path section (general or tagged with that path, avoiding the
    /// pre-fork picks if possible) and a boss arena tagged with that path (D-148).
    /// </summary>
    public static class RunPlanGenerator
    {
        const uint MainStream = 0;
        const uint EscapeStream = 1;
        const uint DiscoverStream = 2;

        /// <summary>Plan for this seed using the config. Null if seed 0 is asked for but the curated plan is missing/invalid.</summary>
        public static RunPlan Create(uint seed, RunConfig config)
        {
            if (seed == RunSeed.Curated)
                return FromCurated(config.curatedPlan);
            return Generate(seed, config.sectionPool, config.forkRoom, config.bossArenas,
                            config.minPreForkSections, config.maxPreForkSections);
        }

        /// <summary>The authored first run (seed 0). Null if the asset is missing or invalid.</summary>
        public static RunPlan FromCurated(CuratedRunPlan curated)
        {
            if (curated == null || !curated.IsValid(out _)) return null;
            return new RunPlan(RunSeed.Curated, true, new List<SectionData>(curated.preForkSections), curated.forkRoom,
                curated.escapePathSection, curated.escapeBossArena, curated.discoverPathSection, curated.discoverBossArena);
        }

        /// <summary>Seeded plan from a pool. Missing pieces come back as null (the RunManager logs them).</summary>
        public static RunPlan Generate(uint seed, IReadOnlyList<SectionData> pool, SectionData forkRoom,
                                       IReadOnlyList<SectionData> bossArenas, int minPreFork, int maxPreFork)
        {
            // Unique general sections, in pool order (order matters for determinism).
            var general = new List<SectionData>();
            if (pool != null)
                foreach (SectionData s in pool)
                    if (s != null && s.IsGeneral && !general.Contains(s)) general.Add(s);

            // ---- Pre-fork: 2-3 sections, no repeats while the pool lasts (stream 0) ----
            var main = new SeededRandom(seed, MainStream);
            int count = main.Range(minPreFork, maxPreFork + 1);
            var preFork = new List<SectionData>(count);
            var bag = new List<SectionData>(general);
            for (int i = 0; i < count && general.Count > 0; i++)
            {
                if (bag.Count == 0) bag.AddRange(general); // pool too small: allow repeats
                int pick = main.Range(0, bag.Count);
                preFork.Add(bag[pick]);
                bag.RemoveAt(pick);
            }

            // ---- Both branches, each from its own stream ----
            var escapeRng = new SeededRandom(seed, EscapeStream);
            SectionData escapePath = PickPathSection(escapeRng, pool, RunPath.Escape, preFork);
            SectionData escapeBoss = PickBoss(escapeRng, bossArenas, RunPath.Escape);
            var discoverRng = new SeededRandom(seed, DiscoverStream);
            SectionData discoverPath = PickPathSection(discoverRng, pool, RunPath.Discover, preFork);
            SectionData discoverBoss = PickBoss(discoverRng, bossArenas, RunPath.Discover);

            return new RunPlan(seed, false, preFork, forkRoom, escapePath, escapeBoss, discoverPath, discoverBoss);
        }

        static SectionData PickPathSection(SeededRandom rng, IReadOnlyList<SectionData> pool, RunPath branch,
                                           List<SectionData> preFork)
        {
            var fresh = new List<SectionData>();
            var any = new List<SectionData>();
            if (pool != null)
                foreach (SectionData s in pool)
                {
                    if (s == null || !s.CanBePathSectionFor(branch) || any.Contains(s)) continue;
                    any.Add(s);
                    if (!preFork.Contains(s)) fresh.Add(s);
                }
            List<SectionData> from = fresh.Count > 0 ? fresh : any;
            return from.Count > 0 ? from[rng.Range(0, from.Count)] : null;
        }

        static SectionData PickBoss(SeededRandom rng, IReadOnlyList<SectionData> bossArenas, RunPath branch)
        {
            var candidates = new List<SectionData>();
            if (bossArenas != null)
                foreach (SectionData b in bossArenas)
                    if (b != null && b.IsBossArenaFor(branch) && !candidates.Contains(b)) candidates.Add(b);
            return candidates.Count > 0 ? candidates[rng.Range(0, candidates.Count)] : null;
        }
    }
}
