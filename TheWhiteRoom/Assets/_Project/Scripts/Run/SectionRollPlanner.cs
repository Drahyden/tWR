using System.Collections.Generic;
using System.Text;

namespace TWR.Run
{
    /// <summary>One candidate (spawn point, pickup spot or switch slot) as plain data: its stable id and flags (D-172).</summary>
    public sealed class RollCandidate
    {
        public string Id;
        /// <summary>Used by seed 0 / the sandbox (the authored R2 layout).</summary>
        public bool IsDefault;
        /// <summary>May be picked by a seeded roll (D-176: only spots that pass the placement rules).</summary>
        public bool Rollable = true;

        public RollCandidate(string id, bool isDefault, bool rollable)
        {
            Id = id;
            IsDefault = isDefault;
            Rollable = rollable;
        }
    }

    /// <summary>A pickup group (D-177): a fixed number of pickups placed on some of its candidate spots.</summary>
    public sealed class PickupGroupCandidates
    {
        public string GroupId;
        public int PickupCount;
        public readonly List<RollCandidate> Spots = new List<RollCandidate>();
    }

    /// <summary>A puzzle that may get a variant (D-173): its switch slots, how many variants its pool has, how many switches are active.</summary>
    public sealed class PuzzleCandidates
    {
        public string PuzzleId;
        /// <summary>Main-path puzzle with a variant pool. Optional puzzles and puzzles without a pool keep their authored setup.</summary>
        public bool Rollable;
        public int VariantCount;
        public int ActiveCount = 3;
        public readonly List<RollCandidate> Slots = new List<RollCandidate>();
        /// <summary>The authored default switches in their authored (sequence) order.</summary>
        public readonly List<string> DefaultActiveIds = new List<string>();
    }

    /// <summary>
    /// Everything in one section that can be rolled, as plain ids (no Unity objects), so the same roll can be computed
    /// in Play mode and in the editor determinism check (D-172). SortAll orders every list by stable id (ordinal), never
    /// by hierarchy order.
    /// </summary>
    public sealed class SectionCandidates
    {
        public int EnemyCount;
        public readonly List<RollCandidate> EnemySpots = new List<RollCandidate>();
        public readonly List<PickupGroupCandidates> PickupGroups = new List<PickupGroupCandidates>();
        public readonly List<PuzzleCandidates> Puzzles = new List<PuzzleCandidates>();

        public void SortAll()
        {
            EnemySpots.Sort(CompareCandidates);
            PickupGroups.Sort((a, b) => string.CompareOrdinal(a.GroupId, b.GroupId));
            foreach (PickupGroupCandidates g in PickupGroups) g.Spots.Sort(CompareCandidates);
            Puzzles.Sort((a, b) => string.CompareOrdinal(a.PuzzleId, b.PuzzleId));
            foreach (PuzzleCandidates p in Puzzles) p.Slots.Sort(CompareCandidates);
        }

        static int CompareCandidates(RollCandidate a, RollCandidate b) => string.CompareOrdinal(a.Id, b.Id);
    }

    /// <summary>What was rolled for one pickup group.</summary>
    public sealed class PickupGroupRoll
    {
        public string GroupId;
        public bool FromDefaults;
        public readonly List<string> SpotIds = new List<string>();
    }

    /// <summary>What was rolled for one puzzle. VariantIndex -1 = the authored default (PuzzleData rules).</summary>
    public sealed class PuzzleRoll
    {
        public string PuzzleId;
        public bool FromDefaults;
        public int VariantIndex = -1;
        /// <summary>Active switches; in a Sequence variant this is the required order.</summary>
        public readonly List<string> ActiveSlotIds = new List<string>();
    }

    /// <summary>The ids chosen for one section (D-172). Pure data; SectionRoller applies it to the scene.</summary>
    public sealed class SectionRollResult
    {
        public bool EnemiesFromDefaults;
        public readonly List<string> EnemySpotIds = new List<string>();
        public readonly List<PickupGroupRoll> Pickups = new List<PickupGroupRoll>();
        public readonly List<PuzzleRoll> Puzzles = new List<PuzzleRoll>();
        /// <summary>Rules a roll could not satisfy (the category fell back to its defaults). Logged as [Run] errors.</summary>
        public readonly List<string> Problems = new List<string>();

        /// <summary>Compact text of every choice, used to compare two rolls in the determinism check.</summary>
        public string Fingerprint()
        {
            var sb = new StringBuilder();
            sb.Append("E:").Append(string.Join(",", EnemySpotIds));
            foreach (PickupGroupRoll g in Pickups) sb.Append("|P:").Append(g.GroupId).Append('=').Append(string.Join(",", g.SpotIds));
            foreach (PuzzleRoll p in Puzzles) sb.Append("|Z:").Append(p.PuzzleId).Append('=').Append(p.VariantIndex).Append(':').Append(string.Join(",", p.ActiveSlotIds));
            return sb.ToString();
        }
    }

    /// <summary>
    /// The pure section-roll rules (R3, D-172 to D-177). Given a context and the sorted candidate ids it decides:
    /// enemies = SectionData.enemyCount spawn points drawn without replacement from the rollable ones (D-176);
    /// pickups = each group's fixed count of spots drawn without replacement from its candidates (D-177);
    /// puzzles = one variant from the pool and its active switches drawn in order from the slots (D-173; the draw order
    /// is the Sequence order). Seed 0 and the sandbox take the authored defaults. If a rule can't be met (too few
    /// candidates), that category falls back to its defaults and a problem is recorded, so a roll never softlocks.
    /// No Unity calls: the editor determinism check runs exactly this code.
    /// </summary>
    public static class SectionRollPlanner
    {
        public static SectionRollResult Roll(SectionRollContext context, SectionCandidates candidates)
        {
            var result = new SectionRollResult();
            candidates.SortAll();
            RollEnemies(context, candidates, result);
            RollPickups(context, candidates, result);
            RollPuzzles(context, candidates, result);
            return result;
        }

        static void RollEnemies(SectionRollContext context, SectionCandidates c, SectionRollResult result)
        {
            if (c.EnemySpots.Count == 0) return;
            if (!context.UsesDefaults)
            {
                var rollable = new List<RollCandidate>();
                foreach (RollCandidate spot in c.EnemySpots)
                    if (spot.Rollable) rollable.Add(spot);

                if (c.EnemyCount <= 0)
                    result.Problems.Add($"enemyCount is 0 but the scene has {c.EnemySpots.Count} spawn point(s); using the authored defaults (set SectionData.enemyCount, D-176)");
                else if (rollable.Count < c.EnemyCount)
                    result.Problems.Add($"only {rollable.Count} rollable spawn point(s) for {c.EnemyCount} enemies; using the authored defaults (D-176 needs >= count+1)");
                else
                {
                    var picks = new List<int>(c.EnemyCount);
                    context.CreateRandom(RollCategory.Enemies).PickIndices(rollable.Count, c.EnemyCount, picks);
                    picks.Sort(); // order doesn't matter for enemies; sorted reads better in the overlay
                    foreach (int i in picks) result.EnemySpotIds.Add(rollable[i].Id);
                    return;
                }
            }

            result.EnemiesFromDefaults = true;
            foreach (RollCandidate spot in c.EnemySpots)
                if (spot.IsDefault) result.EnemySpotIds.Add(spot.Id);
        }

        static void RollPickups(SectionRollContext context, SectionCandidates c, SectionRollResult result)
        {
            if (c.PickupGroups.Count == 0) return;
            SeededRandom rng = context.UsesDefaults ? null : context.CreateRandom(RollCategory.Pickups);
            var picks = new List<int>(4);
            foreach (PickupGroupCandidates group in c.PickupGroups)
            {
                var roll = new PickupGroupRoll { GroupId = group.GroupId };
                result.Pickups.Add(roll);

                if (rng != null && group.Spots.Count >= group.PickupCount && group.PickupCount > 0)
                {
                    picks.Clear();
                    rng.PickIndices(group.Spots.Count, group.PickupCount, picks);
                    foreach (int i in picks) roll.SpotIds.Add(group.Spots[i].Id);
                    continue;
                }
                if (rng != null && group.PickupCount > 0)
                    result.Problems.Add($"pickup group '{group.GroupId}' has {group.PickupCount} pickup(s) but only {group.Spots.Count} spot(s); using the authored defaults (D-177)");

                roll.FromDefaults = true;
                foreach (RollCandidate spot in group.Spots)
                    if (spot.IsDefault) roll.SpotIds.Add(spot.Id);
            }
        }

        static void RollPuzzles(SectionRollContext context, SectionCandidates c, SectionRollResult result)
        {
            if (c.Puzzles.Count == 0) return;
            SeededRandom rng = context.UsesDefaults ? null : context.CreateRandom(RollCategory.Puzzle);
            var picks = new List<int>(4);
            foreach (PuzzleCandidates puzzle in c.Puzzles)
            {
                var roll = new PuzzleRoll { PuzzleId = puzzle.PuzzleId };
                result.Puzzles.Add(roll);

                if (rng != null && puzzle.Rollable)
                {
                    if (puzzle.VariantCount > 0 && puzzle.ActiveCount > 0 && puzzle.Slots.Count >= puzzle.ActiveCount)
                    {
                        roll.VariantIndex = rng.Range(0, puzzle.VariantCount);
                        picks.Clear();
                        rng.PickIndices(puzzle.Slots.Count, puzzle.ActiveCount, picks); // draw order = Sequence order (D-173)
                        foreach (int i in picks) roll.ActiveSlotIds.Add(puzzle.Slots[i].Id);
                        continue;
                    }
                    result.Problems.Add($"puzzle '{puzzle.PuzzleId}' has {puzzle.Slots.Count} slot(s), {puzzle.VariantCount} variant(s), " +
                                        $"needs {puzzle.ActiveCount} active; using the authored default (D-173)");
                }

                roll.FromDefaults = true;
                roll.ActiveSlotIds.AddRange(puzzle.DefaultActiveIds);
            }
        }
    }
}
