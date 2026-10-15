using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using TWR.Combat;
using TWR.Enemies;
using TWR.Items;
using TWR.Puzzles;

namespace TWR.Run
{
    /// <summary>The rollable things found in one loaded section scene (collected once per load, never per frame).</summary>
    public sealed class SceneRollTargets
    {
        public readonly List<SeekerSpawnPoint> Spawns = new List<SeekerSpawnPoint>();
        public readonly List<PickupGroup> Groups = new List<PickupGroup>();
        public readonly List<PuzzleController> Puzzles = new List<PuzzleController>();
        public readonly List<ItemPickup> Pickups = new List<ItemPickup>();
    }

    /// <summary>What was rolled for one plan stop (D-172), kept by the RunManager for the debug overlay and the log.</summary>
    public sealed class SectionRollReport
    {
        public int StopIndex;
        public string SectionId;
        public string SceneName;
        public SectionRollMode Mode;
        public uint Seed;
        public readonly List<string> Lines = new List<string>();
        public readonly List<string> Problems = new List<string>();

        /// <summary>"[Run] Rolls for ..." line. Allocates; once per section load.</summary>
        public string OneLine()
        {
            var sb = new StringBuilder();
            sb.Append("Rolls for ").Append(SectionId).Append(" (seed ").Append(RunSeed.Format(Seed)).Append(", ")
              .Append(Mode == SectionRollMode.Curated ? "curated, fixed" : Mode == SectionRollMode.Seeded ? "seeded" : "sandbox").Append("): ");
            sb.Append(Lines.Count > 0 ? string.Join(" | ", Lines) : "nothing to roll");
            if (Problems.Count > 0) sb.Append(" | ").Append(Problems.Count).Append(" fallback(s), see errors");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Applies the seeded section rolls to a freshly loaded section scene (R3, D-172 to D-177, D-130). The RunManager
    /// calls Apply from SceneManager.sceneLoaded: Unity raises that callback after every Awake/OnEnable of the new scene
    /// but BEFORE any Start, and Seekers spawn in SeekerSpawnPoint.Start, so the choice is always in place first (if
    /// a roll ever came too late, SetChosen refuses and a [Run] error is logged; without a roll every point falls back to
    /// its authored default, never a double spawn).
    /// Steps: collect spawn points, pickup groups and puzzles in the scene; turn them into id lists sorted by stable id
    /// (SectionCandidates); let SectionRollPlanner decide (pure, deterministic); apply: choose spawn points, move pickups,
    /// set puzzle variants; then the D-130 solvability checks: active switches only need run-start guns (D-173) and
    /// every gun-gated main puzzle keeps at least one ammo pickup inside its area (D-177). A failed check logs a [Run]
    /// error and falls back to the authored default for that category, so a roll can never softlock the run.
    /// </summary>
    public static class SectionRoller
    {
        public static SceneRollTargets Collect(Scene scene)
        {
            var t = new SceneRollTargets();
            if (!scene.IsValid() || !scene.isLoaded) return t;
            var spawns = new List<SeekerSpawnPoint>();
            var groups = new List<PickupGroup>();
            var puzzles = new List<PuzzleController>();
            var pickups = new List<ItemPickup>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, spawns);
                t.Spawns.AddRange(spawns);
                root.GetComponentsInChildren(true, groups);
                t.Groups.AddRange(groups);
                root.GetComponentsInChildren(true, puzzles);
                t.Puzzles.AddRange(puzzles);
                root.GetComponentsInChildren(true, pickups);
                t.Pickups.AddRange(pickups);
            }
            return t;
        }

        /// <summary>Plain id lists for the planner (also used by the editor determinism check).</summary>
        public static SectionCandidates BuildCandidates(SceneRollTargets t, SectionData section)
        {
            var c = new SectionCandidates { EnemyCount = section != null ? section.enemyCount : 0 };
            foreach (SeekerSpawnPoint s in t.Spawns)
                c.EnemySpots.Add(new RollCandidate(s.SpotId, s.IsDefault, s.AllowInRolls));

            foreach (PickupGroup g in t.Groups)
            {
                var group = new PickupGroupCandidates { GroupId = g.GroupId, PickupCount = g.PickupCount };
                foreach (PickupSpot spot in g.Spots)
                    if (spot != null) group.Spots.Add(new RollCandidate(spot.SpotId, spot.IsDefault, true));
                c.PickupGroups.Add(group);
            }

            foreach (PuzzleController p in t.Puzzles)
            {
                PuzzleVariantPool pool = p.VariantPool;
                var puzzle = new PuzzleCandidates
                {
                    PuzzleId = p.StableId,
                    // D-173: main-path puzzles only; isOptional is never rolled.
                    Rollable = pool != null && pool.Count > 0 && p.Data != null && !p.Data.isOptional,
                    VariantCount = pool != null ? pool.Count : 0,
                    ActiveCount = pool != null ? pool.activeSwitchCount : 3,
                };
                foreach (ShootableSwitch sw in p.Slots)
                {
                    if (sw == null) continue;
                    puzzle.Slots.Add(new RollCandidate(sw.SwitchId, sw.DefaultActive, true));
                    if (sw.DefaultActive) puzzle.DefaultActiveIds.Add(sw.SwitchId);
                }
                c.Puzzles.Add(puzzle);
            }
            return c;
        }

        /// <summary>Roll and apply everything for one loaded section. runStartGuns = the player's run-start guns (D-120).</summary>
        public static SectionRollReport Apply(Scene scene, SectionRollContext context, int stopIndex, List<GunData> runStartGuns)
        {
            var report = new SectionRollReport
            {
                StopIndex = stopIndex,
                SectionId = context.SectionId,
                SceneName = scene.name,
                Mode = context.Mode,
                Seed = context.Seed,
            };

            SceneRollTargets targets = Collect(scene);
            SectionCandidates candidates = BuildCandidates(targets, context.Section);
            SectionRollResult result = SectionRollPlanner.Roll(context, candidates);
            report.Problems.AddRange(result.Problems);

            ApplyEnemies(targets, result, report);
            ApplyPickups(targets, result, report);
            ApplyPuzzles(targets, result, report, runStartGuns);
            CheckPuzzleAmmo(targets, report, runStartGuns);
            return report;
        }

        // ---------- enemies (D-176) ----------

        static void ApplyEnemies(SceneRollTargets t, SectionRollResult result, SectionRollReport report)
        {
            if (t.Spawns.Count == 0) return;
            var chosen = new HashSet<string>(result.EnemySpotIds);
            foreach (SeekerSpawnPoint spawn in t.Spawns)
                if (!spawn.SetChosen(chosen.Contains(spawn.SpotId)))
                    report.Problems.Add($"spawn point '{spawn.SpotId}' already spawned before the roll arrived (ordering bug)");
            report.Lines.Add($"enemies ({result.EnemySpotIds.Count}{(result.EnemiesFromDefaults ? ", authored" : "")}): {Join(result.EnemySpotIds)}");
        }

        // ---------- pickups (D-177) ----------

        static void ApplyPickups(SceneRollTargets t, SectionRollResult result, SectionRollReport report)
        {
            var chosenSpots = new List<PickupSpot>(4);
            foreach (PickupGroupRoll roll in result.Pickups)
            {
                PickupGroup group = FindGroup(t, roll.GroupId);
                if (group == null) continue;
                group.CaptureAuthoredPositions();
                if (!roll.FromDefaults)
                {
                    chosenSpots.Clear();
                    foreach (string id in roll.SpotIds)
                    {
                        PickupSpot spot = FindSpot(group, id);
                        if (spot != null) chosenSpots.Add(spot);
                    }
                    group.PlaceAt(chosenSpots);
                }
                report.Lines.Add($"pickups '{roll.GroupId}'{(roll.FromDefaults ? " (authored)" : "")}: {Join(roll.SpotIds)}");
            }
        }

        static PickupGroup FindGroup(SceneRollTargets t, string id)
        {
            foreach (PickupGroup g in t.Groups) if (g.GroupId == id) return g;
            return null;
        }

        static PickupSpot FindSpot(PickupGroup group, string id)
        {
            foreach (PickupSpot s in group.Spots) if (s != null && s.SpotId == id) return s;
            return null;
        }

        // ---------- puzzles (D-173, D-130) ----------

        static void ApplyPuzzles(SceneRollTargets t, SectionRollResult result, SectionRollReport report, List<GunData> runStartGuns)
        {
            foreach (PuzzleRoll roll in result.Puzzles)
            {
                PuzzleController puzzle = FindPuzzle(t, roll.PuzzleId);
                if (puzzle == null) continue;

                if (!roll.FromDefaults && !TryApplyVariant(puzzle, roll, runStartGuns, out string problem))
                {
                    report.Problems.Add($"puzzle '{roll.PuzzleId}': {problem}; using the authored default (D-130)");
                    roll.FromDefaults = true;
                }
                if (roll.FromDefaults) puzzle.ApplyDefaultVariant();

                // Report what is really in use now (the controller's state, not just the roll).
                var ids = new List<string>(puzzle.ActiveSwitches.Count);
                foreach (ShootableSwitch sw in puzzle.ActiveSwitches) ids.Add(sw.SwitchId);
                string separator = puzzle.SolveMode == PuzzleSolveMode.Sequence ? " > " : ", ";
                report.Lines.Add($"puzzle '{roll.PuzzleId}' {puzzle.SolveMode}+{puzzle.Behaviour}{(roll.FromDefaults ? " (authored)" : "")}: " +
                                 $"{string.Join(separator, ids)}");
            }
        }

        static bool TryApplyVariant(PuzzleController puzzle, PuzzleRoll roll, List<GunData> runStartGuns, out string problem)
        {
            PuzzleVariant variant = puzzle.VariantPool != null ? puzzle.VariantPool.Get(roll.VariantIndex) : null;
            if (variant == null) { problem = $"variant {roll.VariantIndex} missing"; return false; }
            if (!PuzzleVariantPool.IsAllowed(variant.solveMode, variant.switchBehaviour)) { problem = $"{variant.Family} is excluded (D-173)"; return false; }

            var use = new List<ShootableSwitch>(roll.ActiveSlotIds.Count);
            foreach (string id in roll.ActiveSlotIds)
            {
                ShootableSwitch sw = FindSwitch(puzzle, id);
                if (sw == null) { problem = $"slot '{id}' not found"; return false; }
                if (!AcceptsARunStartGun(sw.Requirement, runStartGuns))
                {
                    problem = $"slot '{id}' needs {sw.Requirement.requiredGun}, which is not a run-start gun (D-173)";
                    return false;
                }
                use.Add(sw);
            }
            int needed = puzzle.VariantPool.activeSwitchCount;
            if (use.Count != needed) { problem = $"{use.Count} active switches instead of {needed}"; return false; }
            if (!puzzle.ApplyVariant(variant.solveMode, variant.switchBehaviour, use)) { problem = "the controller refused the variant"; return false; }
            problem = null;
            return true;
        }

        static bool AcceptsARunStartGun(GunRequirement requirement, List<GunData> runStartGuns)
        {
            if (!requirement.requireSpecificGun) return true;
            if (runStartGuns == null || runStartGuns.Count == 0) return true; // unknown loadout: nothing to check against
            foreach (GunData gun in runStartGuns)
                if (requirement.Accepts(gun)) return true;
            return false;
        }

        static PuzzleController FindPuzzle(SceneRollTargets t, string id)
        {
            foreach (PuzzleController p in t.Puzzles) if (p.StableId == id) return p;
            return null;
        }

        static ShootableSwitch FindSwitch(PuzzleController puzzle, string id)
        {
            foreach (ShootableSwitch sw in puzzle.Slots) if (sw != null && sw.SwitchId == id) return sw;
            return null;
        }

        /// <summary>
        /// D-130 / D-177: each main puzzle with a puzzle area must have at least one ammo pickup inside it after the
        /// roll. If not, every pickup group goes back to its authored spots; if that still fails, the scene is authored
        /// wrong (error, nothing else to do).
        /// </summary>
        static void CheckPuzzleAmmo(SceneRollTargets t, SectionRollReport report, List<GunData> runStartGuns)
        {
            foreach (PuzzleController puzzle in t.Puzzles)
            {
                if (puzzle.Data == null || puzzle.Data.isOptional || puzzle.ActiveSwitches.Count == 0) continue;
                if (!puzzle.HasArea)
                {
                    report.Lines.Add($"puzzle '{puzzle.StableId}': no puzzle area set, ammo check skipped");
                    continue;
                }
                if (CountAmmoInArea(t, puzzle.Area, runStartGuns) > 0) continue;

                report.Problems.Add($"no ammo pickup inside the area of puzzle '{puzzle.StableId}' after the roll; pickups back to their authored spots (D-130, D-177)");
                foreach (PickupGroup g in t.Groups) g.RestoreAuthoredPositions();
                if (CountAmmoInArea(t, puzzle.Area, runStartGuns) == 0)
                    report.Problems.Add($"puzzle '{puzzle.StableId}' has no ammo pickup in its area even in the authored layout. Run TWR > Run > Validate Run Setup");
            }
        }

        static int CountAmmoInArea(SceneRollTargets t, Bounds area, List<GunData> runStartGuns)
        {
            int count = 0;
            foreach (ItemPickup pickup in t.Pickups)
            {
                if (pickup == null || pickup.Item == null || !IsAmmo(pickup.Item, runStartGuns)) continue;
                if (area.Contains(pickup.transform.position + Vector3.up * 0.1f)) count++;
            }
            return count;
        }

        /// <summary>Ammo = the ammo item of a run-start gun; without a known loadout: any non-health item.</summary>
        static bool IsAmmo(ItemData item, List<GunData> runStartGuns)
        {
            if (item is HealthItemData) return false;
            if (runStartGuns == null || runStartGuns.Count == 0) return true;
            foreach (GunData gun in runStartGuns)
                if (gun != null && gun.ammoItem == item) return true;
            return false;
        }

        static string Join(List<string> ids) => ids.Count > 0 ? string.Join(", ", ids) : "(none)";
    }
}
