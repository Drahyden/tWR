using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using TWR.Combat;
using TWR.Core;
using TWR.Enemies;
using TWR.Items;
using TWR.Portals;
using TWR.Puzzles;
using TWR.Run;

namespace TWR.EditorTools
{
    /// <summary>
    /// R3 checks for one run content scene (D-172 to D-177, D-130), called from the R2 scene validation
    /// ("TWR/Run/Validate Run Setup" and after "TWR/Run/Build Run Scenes"). Checks:
    /// - stable ids: every spawn point, pickup spot, pickup group and switch slot has an authored id, unique in the scene;
    /// - enemies (D-176): at least enemyCount + 1 rollable spawn points, each at least 15 m from the entry spawn, outside
    ///   the entry vestibule, not visible from it (rays from 18 points inside the vestibule to the Seeker's chest and
    ///   head; only this scene's solid colliders block), on the NavMesh, reachable, with its own patrol route
    ///   (stealth: at least 2 waypoints, D-169); the curated default spots match enemyCount;
    /// - pickups (D-177): every group has at least as many spots as pickups, its default spots are where the pickups
    ///   stand, every spot is on the NavMesh, reachable from the entry and not within 1.5 m of a portal (D-143);
    ///   puzzle-area groups lie inside the puzzle's area; boss arenas have exactly 4 ammo + 1 health pickup (D-188, D-194);
    ///   in arenas with ArenaPlatforms (Preacher) "reachable" means on foot with jumps (D-186 graph), not the NavMesh;
    /// - puzzles (D-173, D-130): main puzzles in a run have a variant pool without Sequence+Toggle, at least 4 slots,
    ///   3 default slots, PuzzleData = AllActive+Latch (seed 0), gun requirements limited to run-start guns, slot roots and
    ///   tally dots assigned, a puzzle area, and at least one ammo group inside that area.
    /// Opens nothing itself: runs on the scene the R2 validation already opened.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const float MinSpawnDistanceFromEntry = 15f; // D-176
        const float MinPickupDistanceFromPortal = 1.5f; // D-143
        static readonly float[] VestibuleSampleX = { -1.5f, 0f, 1.5f };
        static readonly float[] VestibuleSampleZ = { 0.6f, 2f, 3.6f };
        static readonly float[] VestibuleEyeHeights = { 1.0f, 1.65f }; // crouched, standing
        static readonly float[] SeekerSightHeights = { 1.0f, 1.7f };   // chest, head

        static bool ValidateSectionRolls(StringBuilder report, Scene scene, SectionData section, SectionEntry entry)
        {
            bool ok = true;
            report.AppendLine("  -- R3 seeded rolls (D-172 to D-177) --");
            SceneRollTargets t = SectionRoller.Collect(scene);
            ok &= CheckStableIds(report, scene, t);

            Physics.SyncTransforms(); // edit mode: make sure collider positions are current
            List<Collider> blockers = SolidColliders(scene);
            Vector3 entryPos = entry != null ? entry.transform.position : Vector3.zero;
            Transform vestibule = entry != null ? entry.transform.parent : null;
            List<GunData> runStartGuns = EditorRunStartGuns();

            ok &= CheckEnemySpots(report, t, section, entry, entryPos, vestibule, blockers);
            // R4 (D-183, D-186): arenas with platforms check pickup reachability on foot (jumps), not on the NavMesh.
            PlatformGraph platforms = InScene<ArenaPlatform>(scene).Count > 0 ? BuildPlatformGraph(scene, ReachabilityTuning()) : null;
            ok &= CheckPickupGroups(report, scene, t, section, entry, entryPos, runStartGuns, platforms);
            ok &= CheckPuzzleRolls(report, t, section, runStartGuns);
            return ok;
        }

        // ---------- ids (D-172) ----------

        static bool CheckStableIds(StringBuilder report, Scene scene, SceneRollTargets t)
        {
            var ids = new List<(string kind, string id, bool authored)>();
            foreach (SeekerSpawnPoint s in t.Spawns) ids.Add(("spawn", s.SpotId, Authored(s, "spotId")));
            foreach (PickupGroup g in t.Groups)
            {
                ids.Add(("pickup group", g.GroupId, Authored(g, "groupId")));
                foreach (PickupSpot s in g.Spots)
                    if (s != null) ids.Add(("pickup spot", s.SpotId, Authored(s, "spotId")));
            }
            foreach (PuzzleController p in t.Puzzles)
                foreach (ShootableSwitch sw in p.Slots)
                    if (sw != null) ids.Add(("switch slot", sw.SwitchId, Authored(sw, "switchId")));

            var duplicates = ids.GroupBy(i => i.kind + ":" + i.id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var unauthored = ids.Where(i => !i.authored).Select(i => i.kind + " '" + i.id + "'").ToList();
            bool ok = Check(report, $"stable ids unique ({ids.Count} ids)", duplicates.Count == 0,
                            duplicates.Count > 0 ? "duplicates: " + string.Join(", ", duplicates) : "");
            ok &= Check(report, "every rollable thing has an authored id (not just its object name)", unauthored.Count == 0,
                        unauthored.Count > 0 ? string.Join(", ", unauthored.Take(8)) : "");
            return ok;
        }

        static bool Authored(Object component, string field)
        {
            SerializedProperty p = new SerializedObject(component).FindProperty(field);
            return p != null && !string.IsNullOrEmpty(p.stringValue);
        }

        // ---------- enemies (D-176) ----------

        static bool CheckEnemySpots(StringBuilder report, SceneRollTargets t, SectionData section, SectionEntry entry,
                                    Vector3 entryPos, Transform vestibule, List<Collider> blockers)
        {
            int count = section != null ? section.enemyCount : 0;
            bool isBoss = section != null && section.type == SectionType.BossArena;
            if (t.Spawns.Count == 0 && count == 0)
            {
                report.AppendLine("  enemies: none here (enemyCount 0)");
                return true;
            }
            bool ok = true;
            if (isBoss) ok &= Check(report, "boss arena: no Seeker spawn points (the boss is not a rolled enemy)", t.Spawns.Count == 0, t.Spawns.Count.ToString());
            if (section != null && count != SectionData.DefaultEnemyCount(section.type))
                report.AppendLine($"  WARNING: enemyCount {count} differs from the template default {SectionData.DefaultEnemyCount(section.type)} (D-176).");

            List<SeekerSpawnPoint> rollable = t.Spawns.Where(s => s.AllowInRolls).ToList();
            List<SeekerSpawnPoint> defaults = t.Spawns.Where(s => s.IsDefault).ToList();
            ok &= Check(report, $"enemies: >= enemyCount + 1 = {count + 1} rollable spawn points (D-176)", count == 0 || rollable.Count >= count + 1,
                        $"{rollable.Count} rollable: {string.Join(", ", rollable.Select(s => s.SpotId))}");
            ok &= Check(report, $"enemies: curated default spawn points = enemyCount {count} (seed 0 layout)", defaults.Count == count,
                        $"{defaults.Count} default: {string.Join(", ", defaults.Select(s => s.SpotId))}");
            if (entry == null) return Check(report, "enemies: entry spawn known for the distance/sight checks", false, "no single SectionEntry");

            var usedWaypoints = new Dictionary<Transform, string>();
            foreach (SeekerSpawnPoint s in t.Spawns)
            {
                bool shared = false;
                foreach (Transform w in s.PatrolRoute)
                {
                    if (w == null) continue;
                    if (usedWaypoints.TryGetValue(w, out string other) && other != s.SpotId) shared = true;
                    else usedWaypoints[w] = s.SpotId;
                }
                ok &= Check(report, $"spawn '{s.SpotId}': its own patrol route", s.PatrolRoute.Length >= 1 && !shared,
                            $"{s.PatrolRoute.Length} waypoints{(shared ? ", SHARES waypoints with another spot" : "")}");
            }

            foreach (SeekerSpawnPoint s in rollable)
            {
                Vector3 p = s.transform.position;
                Vector3 flat = p - entryPos;
                flat.y = 0f;
                float distance = flat.magnitude;
                bool inVestibule = vestibule != null && InsideVestibule(vestibule, p);
                string seenFrom = null;
                bool visible = vestibule != null && VisibleFromVestibule(vestibule, p, blockers, out seenFrom);
                bool reachable = Reachable(entryPos, p, out string path);
                int minWaypoints = section != null && section.type == SectionType.Stealth ? 2 : 1;
                bool ruleOk = distance >= MinSpawnDistanceFromEntry && !inVestibule && !visible && reachable && s.PatrolRoute.Length >= minWaypoints;
                ok &= Check(report, $"roll spawn '{s.SpotId}': >= {MinSpawnDistanceFromEntry:0} m from entry, outside + hidden from the vestibule, reachable", ruleOk,
                            $"{distance:0.0} m, in vestibule {(inVestibule ? "YES" : "no")}, visible {(visible ? "YES (" + seenFrom + ")" : "no")}, " +
                            $"path {path}, {s.PatrolRoute.Length} waypoints");
            }
            foreach (SeekerSpawnPoint s in t.Spawns.Where(s => !s.AllowInRolls))
                report.AppendLine($"  spawn '{s.SpotId}': curated only (R2 layout, not rolled; exempt from the D-176 sight/distance rules)");
            if (section != null && section.type == SectionType.Stealth)
                report.AppendLine("  stealth: every roll spot has its own patrol through the main room, so any pair covers it (D-169; routes authored by the builder, not measurable here)");
            return ok;
        }

        static bool InsideVestibule(Transform vestibule, Vector3 world)
        {
            Vector3 local = vestibule.InverseTransformPoint(world);
            return Mathf.Abs(local.x) <= VestibuleWidth * 0.5f && local.z >= 0f && local.z <= VestibuleDepth;
        }

        /// True if any ray from a point inside the vestibule reaches the Seeker's chest or head unblocked.
        static bool VisibleFromVestibule(Transform vestibule, Vector3 feet, List<Collider> blockers, out string seenFrom)
        {
            foreach (float x in VestibuleSampleX)
                foreach (float z in VestibuleSampleZ)
                    foreach (float y in VestibuleEyeHeights)
                    {
                        Vector3 eye = vestibule.TransformPoint(new Vector3(x, y, z));
                        foreach (float h in SeekerSightHeights)
                        {
                            Vector3 target = feet + Vector3.up * h;
                            if (Blocked(eye, target, blockers)) continue;
                            seenFrom = $"eye {eye}";
                            return true;
                        }
                    }
            seenFrom = null;
            return false;
        }

        static bool Blocked(Vector3 from, Vector3 to, List<Collider> blockers)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return false;
            var ray = new Ray(from, delta / distance);
            foreach (Collider c in blockers)
                if (c.Raycast(ray, out _, distance - 0.05f)) return true;
            return false;
        }

        /// Solid colliders of this scene only (other open scenes never interfere): no triggers, no Player/Enemy/
        /// Ignore Raycast/Weapon/Portal layers.
        static List<Collider> SolidColliders(Scene scene)
        {
            var skip = new HashSet<int>();
            foreach (string layer in new[] { GameLayers.Player, GameLayers.Enemy, GameLayers.IgnoreRaycast, GameLayers.Weapon, GameLayers.Portal, GameLayers.PortalFallback })
            {
                int index = LayerMask.NameToLayer(layer);
                if (index >= 0) skip.Add(index);
            }
            var list = new List<Collider>();
            foreach (Collider c in InScene<Collider>(scene))
                if (c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && !skip.Contains(c.gameObject.layer)) list.Add(c);
            return list;
        }

        /// Walkable path on this scene's NavMesh from the entry to the point (edit-mode NavMesh query).
        static bool Reachable(Vector3 from, Vector3 to, out string detail)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 1.5f, NavMesh.AllAreas)) { detail = "entry not on NavMesh"; return false; }
            if (!NavMesh.SamplePosition(to, out NavMeshHit end, 1f, NavMesh.AllAreas)) { detail = "spot not on NavMesh"; return false; }
            var path = new NavMeshPath();
            bool found = NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path);
            bool complete = found && path.status == NavMeshPathStatus.PathComplete;
            detail = complete ? "ok" : "NO (" + path.status + ")";
            return complete;
        }

        // ---------- pickups (D-177, D-130, D-143) ----------

        /// The player's movement numbers for the D-186 rules (a default instance if the asset is missing).
        static TWR.Player.PlayerTuning ReachabilityTuning()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<TWR.Player.PlayerTuning>(TuningPath);
            return tuning != null ? tuning : ScriptableObject.CreateInstance<TWR.Player.PlayerTuning>();
        }

        static bool CheckPickupGroups(StringBuilder report, Scene scene, SceneRollTargets t, SectionData section, SectionEntry entry,
                                      Vector3 entryPos, List<GunData> runStartGuns, PlatformGraph platforms)
        {
            bool ok = true;
            List<Portal> portals = InScene<Portal>(scene);
            var grouped = new HashSet<ItemPickup>();
            foreach (PickupGroup g in t.Groups)
            {
                var pickups = g.Pickups.Where(p => p != null).ToList();
                grouped.UnionWith(pickups);
                var spots = g.Spots.Where(s => s != null).ToList();
                var defaults = spots.Where(s => s.IsDefault).ToList();
                ok &= Check(report, $"pickup group '{g.GroupId}': {pickups.Count} pickup(s) <= {spots.Count} spot(s), no empty slots",
                            pickups.Count >= 1 && pickups.Count <= spots.Count && pickups.Count == g.Pickups.Count && spots.Count == g.Spots.Count, "");
                bool defaultsMatch = defaults.Count == pickups.Count &&
                                     pickups.All(p => defaults.Any(s => Vector3.Distance(Flat(s.transform.position), Flat(p.transform.position)) <= 0.1f));
                ok &= Check(report, $"pickup group '{g.GroupId}': default spots = the authored pickup positions (seed 0)", defaultsMatch,
                            $"{defaults.Count} default spot(s)");

                PuzzleController areaPuzzle = string.IsNullOrEmpty(g.PuzzleAreaId) ? null : t.Puzzles.FirstOrDefault(p => p.StableId == g.PuzzleAreaId);
                if (!string.IsNullOrEmpty(g.PuzzleAreaId))
                    ok &= Check(report, $"pickup group '{g.GroupId}': puzzle '{g.PuzzleAreaId}' with an area exists", areaPuzzle != null && areaPuzzle.HasArea, "");

                foreach (PickupSpot s in spots)
                {
                    Vector3 p = s.transform.position;
                    // Platform arenas: on foot with jumps (D-186); everywhere else: the NavMesh path from the entry.
                    bool reachable = entry == null || (platforms != null ? platforms.IsReachable(p, out _) : Reachable(entryPos, p, out _));
                    float portalDistance = portals.Count > 0 ? portals.Min(x => Vector3.Distance(x.transform.position, p)) : float.MaxValue;
                    bool inArea = areaPuzzle == null || !areaPuzzle.HasArea || areaPuzzle.Area.Contains(p + Vector3.up * 0.1f);
                    ok &= Check(report, $"  spot '{s.SpotId}'{(s.IsDefault ? " (default)" : "")}: reachable, away from portals{(areaPuzzle != null ? ", inside the puzzle area (D-130)" : "")}",
                                reachable && portalDistance >= MinPickupDistanceFromPortal && inArea,
                                $"reachable {(reachable ? "yes" : "NO")}{(portals.Count > 0 ? $", portal {portalDistance:0.0} m" : "")}{(areaPuzzle != null ? ", in area " + (inArea ? "yes" : "NO") : "")}");
                }
            }

            foreach (ItemPickup loose in t.Pickups.Where(p => !grouped.Contains(p)))
                report.AppendLine($"  WARNING: pickup '{loose.name}' is in no PickupGroup, so it is never rolled (D-177).");

            if (section != null && section.type == SectionType.BossArena)
            {
                int ammo = t.Pickups.Count(p => p.Item != null && IsAmmoItem(p.Item, runStartGuns));
                int health = t.Pickups.Count(p => p.Item is HealthItemData);
                ok &= Check(report, "boss arena: exactly 4 ammo pickups (D-188, supersedes D-177's 2) and 1 health pickup (D-194)",
                            ammo == 4 && health == 1, $"ammo {ammo}, health {health}");
            }
            return ok;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static bool IsAmmoItem(ItemData item, List<GunData> runStartGuns)
        {
            if (item is HealthItemData) return false;
            return runStartGuns.Count == 0 || runStartGuns.Any(g => g != null && g.ammoItem == item);
        }

        // ---------- puzzles (D-173, D-130) ----------

        static bool CheckPuzzleRolls(StringBuilder report, SceneRollTargets t, SectionData section, List<GunData> runStartGuns)
        {
            bool ok = true;
            foreach (PuzzleController p in t.Puzzles)
            {
                if (p.Data == null) continue;
                if (p.Data.isOptional)
                {
                    report.AppendLine($"  puzzle '{p.StableId}': optional, never rolled (D-173)");
                    continue;
                }
                PuzzleVariantPool pool = p.VariantPool;
                ok &= Check(report, $"puzzle '{p.StableId}': variant pool assigned (D-173)", pool != null, pool != null ? pool.name : "MISSING");
                if (pool != null)
                {
                    var problems = new List<string>();
                    ok &= Check(report, $"puzzle '{p.StableId}': pool = {pool.Count} variants, no Sequence+Toggle, hints set, 3 active (D-173, D-178)",
                                pool.Validate(problems), string.Join("; ", problems));
                }
                ok &= Check(report, $"puzzle '{p.StableId}': seed 0 rules AllActive+Latch (D-173)",
                            p.Data.solveMode == PuzzleSolveMode.AllActive && p.Data.switchBehaviour == SwitchBehaviour.Latch,
                            $"{p.Data.solveMode}+{p.Data.switchBehaviour}");

                var slots = p.Slots.Where(s => s != null).ToList();
                int needed = pool != null ? pool.activeSwitchCount : 3;
                ok &= Check(report, $"puzzle '{p.StableId}': >= 4 switch slots", slots.Count >= 4 && slots.Count == p.Slots.Count,
                            $"{slots.Count}: {string.Join(", ", slots.Select(s => s.SwitchId + (s.DefaultActive ? "*" : "")))} (* = default)");
                ok &= Check(report, $"puzzle '{p.StableId}': {needed} default slots (curated layout)", slots.Count(s => s.DefaultActive) == needed,
                            slots.Count(s => s.DefaultActive).ToString());
                foreach (ShootableSwitch sw in slots)
                {
                    bool gunOk = !sw.Requirement.requireSpecificGun || runStartGuns.Count == 0 || runStartGuns.Any(g => sw.Requirement.Accepts(g));
                    bool rootSet = new SerializedObject(sw).FindProperty("slotRoot").objectReferenceValue != null;
                    bool inArea = !p.HasArea || p.Area.Contains(sw.transform.position);
                    ok &= Check(report, $"  slot '{sw.SwitchId}': run-start gun, slot root, {needed} tally dots, inside the area",
                                gunOk && rootSet && sw.OrderMarkCount >= needed && inArea,
                                $"gun {(sw.Requirement.requireSpecificGun ? sw.Requirement.requiredGun.ToString() : "any")} {(gunOk ? "ok" : "NOT a run-start gun")}, " +
                                $"root {(rootSet ? "ok" : "MISSING")}, dots {sw.OrderMarkCount}, in area {(inArea ? "yes" : "NO")}");
                }

                ok &= Check(report, $"puzzle '{p.StableId}': puzzle area set (D-130)", p.HasArea, p.HasArea ? p.Area.ToString() : "size 0");
                bool hasAmmoGroup = t.Groups.Any(g => g.PuzzleAreaId == p.StableId && g.Pickups.Any(x => x != null && x.Item != null && IsAmmoItem(x.Item, runStartGuns)));
                ok &= Check(report, $"puzzle '{p.StableId}': an ammo pickup group bound to its area (D-130, D-177)", hasAmmoGroup, "");
                ok &= Check(report, $"puzzle '{p.StableId}': PuzzleSequenceClue for Sequence variants (D-175)", p.GetComponent<PuzzleSequenceClue>() != null, "");
            }
            return ok;
        }

        /// The guns the player starts a run with (D-120), read from the Player prefab's WeaponInventory slots.
        static List<GunData> EditorRunStartGuns()
        {
            var list = new List<GunData>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            WeaponInventory weapons = prefab != null ? prefab.GetComponent<WeaponInventory>() : null;
            if (weapons != null) weapons.CollectRunStartGuns(list);
            if (list.Count == 0) Debug.LogWarning("[TWR] Could not read the run-start guns from " + PrefabPath + "; gun checks are skipped.");
            return list;
        }
    }
}
