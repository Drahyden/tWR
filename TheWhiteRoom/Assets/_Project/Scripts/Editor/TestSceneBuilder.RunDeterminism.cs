using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TWR.Run;

namespace TWR.EditorTools
{
    /// <summary>
    /// "TWR/Run/Check Determinism" (R3, D-172). The project has no test assemblies, so this is an editor menu with
    /// [TWR] logs instead of an EditMode test. It checks:
    /// 1. plans: RunPlanGenerator gives the same plan twice for several seeds (and seed 0 = the curated plan);
    /// 2. streams: every section-roll stream (0x100 + branch x 0x40 + stop x 4 + category) is unique and never one of
    ///    the plan streams 0-2, so R3 cannot change existing seeds' plans;
    /// 3. pure rolls on made-up candidate lists: the same seed + stop gives the same result twice AND whatever order the
    ///    candidates come in (they are sorted by stable id first), without repeats and with the right counts;
    /// 4. real scenes: for every built section scene, the candidates read from the scene roll identically twice and in
    ///    reversed hierarchy order. Scenes are opened additively and closed again.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        static readonly uint[] DeterminismSeeds = { 1u, 2u, 0x2Au, 0x1234ABCDu, 0xDEADBEEFu, 0xFFFFFFFFu };

        [MenuItem("TWR/Run/Check Determinism")]
        public static void CheckDeterminismMenu()
        {
            Debug.Log("[TWR] Determinism check (D-172) ...");
            bool ok = true;
            ok &= CheckPlanDeterminism();
            ok &= CheckStreamLayout();
            ok &= CheckSyntheticRolls();
            ok &= CheckSceneRolls();
            if (ok) Debug.Log("[TWR] Determinism check PASSED: same seed -> same plan and same section rolls, independent of hierarchy order.");
            else Debug.LogError("[TWR] Determinism check FAILED (see errors above).");
        }

        static bool CheckPlanDeterminism()
        {
            var config = AssetDatabase.LoadAssetAtPath<RunConfig>(RunConfigPath);
            if (config == null)
            {
                Debug.LogError("[TWR] Determinism: no RunConfig at " + RunConfigPath + ". Run TWR > Run > Create Run Assets.");
                return false;
            }
            bool ok = true;
            foreach (uint seed in DeterminismSeeds.Concat(new[] { RunSeed.Curated }))
            {
                RunPlan a = RunPlanGenerator.Create(seed, config);
                RunPlan b = RunPlanGenerator.Create(seed, config);
                string da = a != null ? a.Describe() : "(null)", db = b != null ? b.Describe() : "(null)";
                bool same = da == db && a != null;
                ok &= same;
                if (same) Debug.Log($"[TWR] Determinism plan {RunSeed.Format(seed)}: OK | {da}");
                else Debug.LogError($"[TWR] Determinism plan {RunSeed.Format(seed)}: DIFFERENT or missing\n  1st: {da}\n  2nd: {db}");
            }
            return ok;
        }

        static bool CheckStreamLayout()
        {
            var seen = new HashSet<uint>();
            bool ok = true;
            for (int branch = 0; branch <= 2; branch++)
                for (int stop = 0; stop <= SectionRollContext.MaxSectionIndex; stop++)
                    for (int c = 0; c <= (int)RollCategory.Puzzle; c++)
                    {
                        uint s = SectionRollContext.StreamFor(branch, stop, (RollCategory)c);
                        if (s <= 2 || !seen.Add(s)) ok = false;
                    }
            if (ok) Debug.Log($"[TWR] Determinism streams: OK ({seen.Count} section-roll streams, all unique, none overlaps plan streams 0-2).");
            else Debug.LogError("[TWR] Determinism streams: a section-roll stream repeats or overlaps the plan streams 0-2.");
            return ok;
        }

        /// Made-up sections, each rolled from three different candidate orders and twice per order.
        static bool CheckSyntheticRolls()
        {
            bool ok = true;
            var fingerprints = new HashSet<string>();
            foreach (uint seed in DeterminismSeeds)
                foreach ((int branch, int stop) in new[] { (0, 0), (0, 1), (0, 2), (0, 3), (1, 4), (2, 4), (1, 5), (2, 5) })
                {
                    var context = new SectionRollContext(seed, branch, stop, SectionRollMode.Seeded, null);
                    string reference = null;
                    for (int order = 0; order < 3; order++)
                        for (int repeat = 0; repeat < 2; repeat++)
                        {
                            SectionRollResult r = SectionRollPlanner.Roll(context, MakeSyntheticCandidates(order));
                            string fp = r.Fingerprint();
                            if (reference == null) reference = fp;
                            if (fp != reference || r.Problems.Count > 0 || !SyntheticCountsOk(r))
                            {
                                Debug.LogError($"[TWR] Determinism synthetic roll seed {RunSeed.Format(seed)} branch {branch} stop {stop} order {order}: " +
                                               $"{(fp != reference ? "DIFFERENT" : "bad counts/problems")}\n  ref: {reference}\n  got: {fp}\n  {string.Join("; ", r.Problems)}");
                                ok = false;
                            }
                        }
                    fingerprints.Add(reference);
                }
            if (fingerprints.Count < 4)
            {
                Debug.LogError($"[TWR] Determinism synthetic rolls: only {fingerprints.Count} distinct results over many seeds/stops; the rolls look stuck.");
                ok = false;
            }
            if (ok) Debug.Log($"[TWR] Determinism synthetic rolls: OK ({fingerprints.Count} distinct results, identical across 3 candidate orders x 2 runs each).");
            return ok;
        }

        static readonly string[] SyntheticEnemyIds = { "e.alpha", "e.bravo", "e.charlie", "e.delta", "e.echo", "e.r2" };
        static readonly string[] SyntheticSpotIds = { "p.1", "p.2", "p.3", "p.4", "p.5" };
        static readonly string[] SyntheticSlotIds = { "s.a", "s.b", "s.c", "s.d" };

        /// The same candidates in a different list order per 'order' (0 = as written, 1 = reversed, 2 = rotated).
        static SectionCandidates MakeSyntheticCandidates(int order)
        {
            var c = new SectionCandidates { EnemyCount = 3 };
            foreach (string id in Reordered(SyntheticEnemyIds, order)) c.EnemySpots.Add(new RollCandidate(id, id == "e.r2", id != "e.r2"));
            foreach (string groupId in Reordered(new[] { "g.ammo", "g.health" }, order))
            {
                var g = new PickupGroupCandidates { GroupId = groupId, PickupCount = groupId == "g.ammo" ? 2 : 1 };
                foreach (string id in Reordered(SyntheticSpotIds, order)) g.Spots.Add(new RollCandidate(groupId + "." + id, id == "p.1", true));
                c.PickupGroups.Add(g);
            }
            var puzzle = new PuzzleCandidates { PuzzleId = "z.main", Rollable = true, VariantCount = 3, ActiveCount = 3 };
            foreach (string id in Reordered(SyntheticSlotIds, order)) puzzle.Slots.Add(new RollCandidate(id, id != "s.d", true));
            puzzle.DefaultActiveIds.AddRange(new[] { "s.a", "s.b", "s.c" });
            c.Puzzles.Add(puzzle);
            return c;
        }

        static IEnumerable<string> Reordered(string[] ids, int order)
        {
            if (order == 1) return ids.Reverse();
            if (order == 2) return ids.Skip(2).Concat(ids.Take(2));
            return ids;
        }

        static bool SyntheticCountsOk(SectionRollResult r)
        {
            if (r.EnemySpotIds.Count != 3 || r.EnemySpotIds.Distinct().Count() != 3 || r.EnemySpotIds.Contains("e.r2")) return false;
            foreach (PickupGroupRoll g in r.Pickups)
                if (g.SpotIds.Distinct().Count() != g.SpotIds.Count || g.SpotIds.Count != (g.GroupId == "g.ammo" ? 2 : 1)) return false;
            foreach (PuzzleRoll p in r.Puzzles)
                if (p.ActiveSlotIds.Count != 3 || p.ActiveSlotIds.Distinct().Count() != 3 || p.VariantIndex < 0 || p.VariantIndex > 2) return false;
            return true;
        }

        /// Real candidates from every built section scene: identical rolls twice and with reversed hierarchy order.
        static bool CheckSceneRolls()
        {
            if (!File.Exists(RunScenePath(CoreSceneName)))
            {
                Debug.LogWarning("[TWR] Determinism scenes: run scenes not built yet (TWR > Run > Build Run Scenes); scene check skipped.");
                return true;
            }
            bool ok = true;
            foreach (var (asset, sceneName) in SectionScenes)
            {
                var section = AssetDatabase.LoadAssetAtPath<SectionData>(RunSectionsFolder + "/" + asset + ".asset");
                string path = RunScenePath(sceneName);
                if (section == null || !File.Exists(path)) continue;

                Scene scene = SceneManager.GetSceneByPath(path);
                bool openedHere = false;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    openedHere = true;
                }
                try
                {
                    SceneRollTargets targets = SectionRoller.Collect(scene);
                    var lines = new List<string>();
                    bool sceneOk = true;
                    foreach (uint seed in DeterminismSeeds)
                    {
                        int branch = section.type == SectionType.BossArena || (section.path != RunPath.None) ? (section.path == RunPath.Discover ? 2 : 1) : 0;
                        var context = new SectionRollContext(seed, branch, branch == 0 ? 1 : 4, SectionRollMode.Seeded, section);
                        string first = SectionRollPlanner.Roll(context, SectionRoller.BuildCandidates(targets, section)).Fingerprint();
                        string second = SectionRollPlanner.Roll(context, SectionRoller.BuildCandidates(targets, section)).Fingerprint();
                        SceneRollTargets reversed = ReversedTargets(targets);
                        string third = SectionRollPlanner.Roll(context, SectionRoller.BuildCandidates(reversed, section)).Fingerprint();
                        if (first != second || first != third)
                        {
                            Debug.LogError($"[TWR] Determinism scene '{sceneName}' seed {RunSeed.Format(seed)}: DIFFERENT\n  1: {first}\n  2: {second}\n  reversed: {third}");
                            sceneOk = false;
                            ok = false;
                        }
                        lines.Add($"{RunSeed.Format(seed)} -> {first}");
                    }
                    Debug.Log($"[TWR] Determinism scene '{sceneName}' ({section.DisplayId}): {(sceneOk ? "OK" : "see errors")}\n  " + string.Join("\n  ", lines));
                }
                finally
                {
                    if (openedHere) EditorSceneManager.CloseScene(scene, true);
                }
            }
            return ok;
        }

        static SceneRollTargets ReversedTargets(SceneRollTargets t)
        {
            var r = new SceneRollTargets();
            r.Spawns.AddRange(Enumerable.Reverse(t.Spawns));
            r.Groups.AddRange(Enumerable.Reverse(t.Groups));
            r.Puzzles.AddRange(Enumerable.Reverse(t.Puzzles));
            r.Pickups.AddRange(Enumerable.Reverse(t.Pickups));
            return r;
        }
    }
}
