using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using TWR.Combat;
using TWR.Enemies;
using TWR.Items;
using TWR.Level;
using TWR.Player;
using TWR.Run;

namespace TWR.EditorTools
{
    /// <summary>
    /// R4 boss-arena checks (D-180 to D-194), run for both boss scenes by "TWR/Run/Validate Run Setup" and after
    /// "TWR/Run/Build Run Scenes". Logs [TWR] lines. Checks:
    /// - the boss: exactly one BossEncounter with BossMarker, BossHealth, the right BossData + brain for the path
    ///   (Escape = Bull, Discover = Preacher, D-148), Enemy layer, a localized name and a sound set; a baked NavMesh;
    /// - size: the room shell measures 72 x 200 x 12 m (Bull, D-180/D-192) or 48 x 40 x 160 m (Preacher, D-183);
    /// - the exit door is on the far side from the entry (and at the entry's height for the Preacher);
    /// - pickups: 4 ammo + 1 health, each group with at least count + 2 spots, the respawner lists exactly the ammo;
    /// - camera far clip >= 250 m (D-193);
    /// - Bull: >= 8 columns spread along the length, >= 50% and >= 4 unbreakable, breakable ones fully wired (D-190);
    /// - Preacher: >= 12 cavities with unique ids, each with a reachable standing spot within 35 m and a clear line of
    ///   sight (D-187), and the D-186 reachability graph: every platform reachable from the floor and able to get back
    ///   down, ramps <= 40 degrees, headroom >= 2 m. Unreachable platforms each get a [TWR] warning.
    /// The reachability numbers come from PlayerTuning (step-up 0.3 m, jump step-up 0.9 m, gap <= 0.75 x sprint x airtime).
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const float WalkStepUp = 0.3f;        // D-186
        const float JumpStepUp = 0.9f;        // D-186
        const float GapEfficiency = 0.75f;    // D-186: 0.75 x sprint x airtime
        const float MaxRampDegrees = 40f;     // D-186
        const float MinHeadroom = 2.0f;       // D-186
        const float CavitySightRange = 35f;   // D-187
        const int MinCavities = 12;           // D-187
        const float SizeTolerance = 0.5f;

        // ---------- reachability graph (D-186) ----------

        /// One walkable top surface (from an ArenaPlatform's BoxCollider).
        sealed class PlatformNode
        {
            public ArenaPlatform Source;
            public string Id;
            public ArenaPlatformKind Kind;
            public float MinX, MaxX, MinZ, MaxZ;
            public float EntryTop;  // height you step/jump onto (ramps: low end)
            public float ExitTop;   // height you leave from (ramps: high end)
            public float Bottom;
            public float Slope;
            public bool Reachable;   // from the floor
            public bool ReachesFloor;
            public MovingPlatform Mover;  // D-242: null = static
            public Vector3[] Offsets = { Vector3.zero }; // movers: sampled positions along the path (from point A)
            public readonly List<int> Out = new List<int>();
            public readonly List<int> In = new List<int>();
            public bool Moving => Mover != null;
            public Vector3 Centre => new Vector3((MinX + MaxX) * 0.5f, ExitTop, (MinZ + MaxZ) * 0.5f);

            /// This node moved by offset (a mover at one sample of its path).
            public PlatformNode At(Vector3 o) => o == Vector3.zero ? this : new PlatformNode
            {
                Source = Source, Id = Id, Kind = Kind, MinX = MinX + o.x, MaxX = MaxX + o.x, MinZ = MinZ + o.z, MaxZ = MaxZ + o.z,
                EntryTop = EntryTop + o.y, ExitTop = ExitTop + o.y, Bottom = Bottom + o.y, Slope = Slope,
            };
        }

        /// The D-186 graph of one arena scene. Built from ArenaPlatform markers (no physics needed, works in edit mode).
        sealed class PlatformGraph
        {
            public readonly List<PlatformNode> Nodes = new List<PlatformNode>();
            public int FloorCount => Nodes.Count(n => n.Kind == ArenaPlatformKind.Floor);

            /// Is this point (feet) standing on a platform that is reachable from the floor?
            public bool IsReachable(Vector3 feet, out string onWhat)
            {
                foreach (PlatformNode n in Nodes)
                {
                    if (n.Moving) continue; // nothing stands on a mover for good (pickups, spawns, cavities)
                    if (feet.x < n.MinX - 0.3f || feet.x > n.MaxX + 0.3f || feet.z < n.MinZ - 0.3f || feet.z > n.MaxZ + 0.3f) continue;
                    if (Mathf.Abs(feet.y - n.ExitTop) > 0.4f) continue;
                    onWhat = n.Id;
                    return n.Reachable;
                }
                onWhat = "no platform under it";
                return false;
            }
        }

        /// includeMovers = false: static platforms only (D-242: the combat exit must not need a mover).
        static PlatformGraph BuildPlatformGraph(Scene scene, PlayerTuning tuning, bool includeMovers = true)
        {
            var graph = new PlatformGraph();
            foreach (ArenaPlatform p in InScene<ArenaPlatform>(scene))
            {
                var box = p.GetComponent<BoxCollider>();
                if (box == null) continue;
                var mover = p.GetComponent<MovingPlatform>();
                if (mover != null && !includeMovers) continue;
                Transform t = p.transform;
                Vector3 half = box.size * 0.5f;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                float topLow = float.MaxValue, topHigh = float.MinValue, bottom = float.MaxValue;
                for (int i = 0; i < 8; i++)
                {
                    var local = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                    Vector3 w = t.TransformPoint(box.center + local);
                    minX = Mathf.Min(minX, w.x); maxX = Mathf.Max(maxX, w.x);
                    minZ = Mathf.Min(minZ, w.z); maxZ = Mathf.Max(maxZ, w.z);
                    bottom = Mathf.Min(bottom, w.y);
                    if (local.y > 0f)
                    {
                        topLow = Mathf.Min(topLow, w.y);
                        topHigh = Mathf.Max(topHigh, w.y);
                    }
                }
                float slope = Vector3.Angle(t.up, Vector3.up);
                bool tilted = slope > 1f;
                graph.Nodes.Add(new PlatformNode
                {
                    Source = p, Id = p.PlatformId, Kind = p.Kind, MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ,
                    EntryTop = tilted ? topLow : topHigh, ExitTop = topHigh, Bottom = bottom, Slope = slope,
                    Mover = mover, Offsets = mover != null ? MoverSamples(mover.Travel) : new[] { Vector3.zero },
                });
            }

            List<PlatformNode> n = graph.Nodes;
            for (int a = 0; a < n.Count; a++)
                for (int b = 0; b < n.Count; b++)
                    if (a != b && CanMoveAnySample(n[a], n[b], tuning))
                    {
                        n[a].Out.Add(b);
                        n[b].In.Add(a);
                    }

            // BFS from the floor (forward = reachable, backward = can get back down to the floor).
            var queue = new Queue<int>();
            for (int i = 0; i < n.Count; i++)
                if (n[i].Kind == ArenaPlatformKind.Floor) { n[i].Reachable = true; queue.Enqueue(i); }
            while (queue.Count > 0)
                foreach (int next in n[queue.Dequeue()].Out)
                    if (!n[next].Reachable) { n[next].Reachable = true; queue.Enqueue(next); }
            for (int i = 0; i < n.Count; i++)
                if (n[i].Kind == ArenaPlatformKind.Floor) { n[i].ReachesFloor = true; queue.Enqueue(i); }
            while (queue.Count > 0)
                foreach (int previous in n[queue.Dequeue()].In)
                    if (!n[previous].ReachesFloor) { n[previous].ReachesFloor = true; queue.Enqueue(previous); }
            return graph;
        }

        /// <summary>
        /// D-186: can the player get from the top of a to the top of b with base movement (walk, sprint, one jump)?
        /// Walk: rise <= 0.3 m and touching. Jump: rise <= 0.9 m and edge gap <= 0.75 x sprintSpeed x airtime(rise).
        /// Drops (negative rise) use the same formula with the longer airtime; dropping onto something that sticks out
        /// from under you (the floor always does) is always allowed.
        /// </summary>
        static bool CanMove(PlatformNode a, PlatformNode b, PlayerTuning tuning)
        {
            float rise = b.EntryTop - a.ExitTop;
            float dx = Mathf.Max(0f, Mathf.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            float dz = Mathf.Max(0f, Mathf.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
            float gap = Mathf.Sqrt(dx * dx + dz * dz);
            float overlapX = Mathf.Min(a.MaxX, b.MaxX) - Mathf.Max(a.MinX, b.MinX);
            float overlapZ = Mathf.Min(a.MaxZ, b.MaxZ) - Mathf.Max(a.MinZ, b.MinZ);
            bool overlapping = overlapX > 0.05f && overlapZ > 0.05f;

            if (overlapping)
            {
                if (rise < -0.05f) return !Covers(a, b); // walk off a's edge and land on b
                return rise <= JumpStepUp;               // b sits on a: step or jump onto it
            }
            if (rise <= WalkStepUp && gap <= 0.15f) return true;
            if (rise > JumpStepUp) return false;
            float maxGap = MaxJumpGap(rise, tuning);
            return maxGap >= 0f && gap <= maxGap;
        }

        /// D-242: a mover connects if the move works at ANY of its samples (it cycles forever and waits at both ends).
        static bool CanMoveAnySample(PlatformNode a, PlatformNode b, PlayerTuning tuning)
        {
            foreach (Vector3 oa in a.Offsets)
                foreach (Vector3 ob in b.Offsets)
                    if (CanMove(a.At(oa), b.At(ob), tuning)) return true;
            return false;
        }

        /// Point A, every 1 m of travel, and point B.
        static Vector3[] MoverSamples(Vector3 travel)
        {
            float length = travel.magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length));
            var samples = new Vector3[steps + 1];
            for (int i = 0; i <= steps; i++) samples[i] = travel * (i / (float)steps);
            return samples;
        }

        static bool Covers(PlatformNode outer, PlatformNode inner) =>
            outer.MinX <= inner.MinX && outer.MaxX >= inner.MaxX && outer.MinZ <= inner.MinZ && outer.MaxZ >= inner.MaxZ;

        /// D-186: 0.75 x sprintSpeed x airtime(rise); airtime = time until the jump arc comes back down to 'rise'.
        static float MaxJumpGap(float rise, PlayerTuning tuning)
        {
            float g = Mathf.Max(0.1f, -tuning.gravity);
            float v0 = Mathf.Sqrt(2f * g * tuning.jumpHeight);
            float disc = v0 * v0 - 2f * g * rise;
            if (disc < 0f) return -1f;
            float airtime = (v0 + Mathf.Sqrt(disc)) / g;
            return GapEfficiency * tuning.sprintSpeed * airtime;
        }

        // ---------- boss arena checks ----------

        static bool ValidateBossArena(StringBuilder report, Scene scene, SectionData section)
        {
            report.AppendLine("  -- R4 boss arena (D-180 to D-194) --");
            bool ok = true;
            bool isBull = section.path == RunPath.Escape;
            var tuning = AssetDatabase.LoadAssetAtPath<PlayerTuning>(TuningPath);
            if (tuning == null)
            {
                report.AppendLine("  WARNING: no PlayerTuning at " + TuningPath + "; using default values for the reachability rules.");
                tuning = ScriptableObject.CreateInstance<PlayerTuning>();
            }

            ok &= CheckBoss(report, scene, isBull);
            ok &= CheckArenaSize(report, scene, isBull);
            ok &= CheckArenaExit(report, scene, isBull);
            ok &= CheckBossPickups(report, scene);

            List<ArenaCameraFarClip> farClips = InScene<ArenaCameraFarClip>(scene);
            ok &= Check(report, "camera far clip >= 250 m while loaded (D-193)", farClips.Count == 1 && farClips[0].FarClip >= 250f,
                        farClips.Count == 1 ? $"{farClips[0].FarClip:0} m" : farClips.Count + " found");
            int shadowCasters = InScene<Renderer>(scene).Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
            if (shadowCasters > 0) report.AppendLine($"  WARNING: {shadowCasters} renderer(s) cast shadows (D-193: emissive light only).");

            if (isBull) ok &= CheckBullColumns(report, scene);
            else ok &= CheckPreacherArena(report, scene, tuning);
            return ok;
        }

        static bool CheckBoss(StringBuilder report, Scene scene, bool isBull)
        {
            List<BossEncounter> bosses = InScene<BossEncounter>(scene);
            bool ok = Check(report, "exactly one boss (BossEncounter)", bosses.Count == 1, bosses.Count.ToString());
            if (bosses.Count != 1) return false;
            BossEncounter boss = bosses[0];
            BossData data = boss.Data;
            bool rightData = isBull ? data is BullData : data is PreacherData;
            bool rightBrain = isBull ? boss.GetComponent<BullBrain>() != null : boss.GetComponent<PreacherBrain>() != null;
            var health = boss.GetComponent<BossHealth>();
            bool healthData = health != null && new SerializedObject(health).FindProperty("data").objectReferenceValue == data;
            ok &= Check(report, $"boss is the {(isBull ? "Bull (Escape, D-148)" : "Preacher (Discover, D-148)")} with its BossData + brain",
                        rightData && rightBrain && healthData,
                        $"data {(data != null ? data.name : "MISSING")}, brain {(rightBrain ? "ok" : "MISSING/WRONG")}, health data {(healthData ? "ok" : "MISMATCH")}");
            ok &= Check(report, "boss has BossMarker (not scored, D-158)", boss.GetComponent<BossMarker>() != null, "");
            ok &= Check(report, "boss on the Enemy layer (shootable, D-134)", boss.gameObject.layer == LayerMask.NameToLayer(EnemyLayerName),
                        LayerMask.LayerToName(boss.gameObject.layer));
            if (data != null)
            {
                bool named = data.displayName != null && !data.displayName.IsEmpty;
                ok &= Check(report, "BossData: localized name + sound set (D-084, D-185)", named && data.sounds != null,
                            $"name {(named ? data.displayName.TableEntryReference.Key : "EMPTY")}, sounds {(data.sounds != null ? data.sounds.name : "MISSING")}");
                if (data is BullData bull)
                    ok &= Check(report, "Bull: fast charge damage below the player's max HP; counts 2-5 (D-189, D-190)",
                                bull.fastChargeDamage < 100f && bull.fastChargeCountMin >= 1 && bull.fastChargeCountMax >= bull.fastChargeCountMin,
                                $"fast {bull.fastChargeDamage:0}, counts {bull.fastChargeCountMin}-{bull.fastChargeCountMax}, curated [{string.Join(",", bull.curatedFastChargeCounts)}]");
            }
            ok &= Check(report, "fight area set", boss.FightArea.size.sqrMagnitude > 1f, boss.FightArea.ToString());

            List<NavMeshSurface> surfaces = InScene<NavMeshSurface>(scene);
            bool baked = surfaces.Count > 0 && surfaces.All(s => s.navMeshData != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(s.navMeshData)));
            ok &= Check(report, "baked + saved NavMesh in the arena (D-161, D-193)", baked, surfaces.Count + " surface(s)");
            if (isBull)
            {
                bool onMesh = NavMesh.SamplePosition(boss.transform.position, out _, 2f, NavMesh.AllAreas);
                ok &= Check(report, "Bull stands on the NavMesh", onMesh, boss.transform.position.ToString());
            }
            return ok;
        }

        /// Inner size from the shell pieces built by BuildSectionShell (walls, floor, ceiling renderers).
        static bool CheckArenaSize(StringBuilder report, Scene scene, bool isBull)
        {
            Transform shell = InScene<Transform>(scene).FirstOrDefault(t => t.name == "Room Shell");
            if (shell == null) return Check(report, "room shell found", false, "no 'Room Shell'");
            float wallW = float.MinValue, wallE = float.MaxValue, wallS = float.MinValue, wallN = float.MaxValue, floorTop = float.MinValue, ceiling = float.MaxValue;
            foreach (Transform piece in shell)
            {
                var r = piece.GetComponent<Renderer>();
                if (r == null) continue;
                Bounds b = r.bounds;
                if (piece.name.StartsWith("Wall W")) wallW = Mathf.Max(wallW, b.max.x);
                else if (piece.name.StartsWith("Wall E")) wallE = Mathf.Min(wallE, b.min.x);
                else if (piece.name.StartsWith("Wall S")) wallS = Mathf.Max(wallS, b.max.z);
                else if (piece.name.StartsWith("Wall N")) wallN = Mathf.Min(wallN, b.min.z);
                else if (piece.name == "Floor") floorTop = Mathf.Max(floorTop, b.max.y);
                else if (piece.name == "Ceiling") ceiling = Mathf.Min(ceiling, b.min.y);
            }
            float width = wallE - wallW, length = wallN - wallS, height = ceiling - floorTop;
            Vector3 expected = isBull ? new Vector3(BullArenaWidth, BullArenaHeight, BullArenaLength)
                                      : new Vector3(PreacherArenaWidth, PreacherArenaHeight, PreacherArenaLength);
            bool ok = Mathf.Abs(width - expected.x) <= SizeTolerance && Mathf.Abs(length - expected.z) <= SizeTolerance &&
                      Mathf.Abs(height - expected.y) <= SizeTolerance;
            return Check(report, $"arena size {expected.x:0} wide x {expected.z:0} long x {expected.y:0} high ({(isBull ? "D-180, D-192" : "D-183")})", ok,
                         $"measured {width:0.0} x {length:0.0} x {height:0.0} m");
        }

        /// Exit door on the far side from the entry; for the Preacher at the entry's height (D-180, D-183).
        static bool CheckArenaExit(StringBuilder report, Scene scene, bool isBull)
        {
            List<SectionEntry> entries = InScene<SectionEntry>(scene);
            List<SectionExitGate> gates = InScene<SectionExitGate>(scene);
            if (entries.Count != 1 || gates.Count < 1) return Check(report, "entry + exit gate for the far-side check", false, "");
            Vector3 entry = entries[0].transform.position;
            Vector3 exit = gates[0].transform.position;
            float length = isBull ? BullArenaLength : PreacherArenaLength;
            bool far = exit.z - entry.z >= length * 0.9f;
            bool level = Mathf.Abs(exit.y - entry.y) <= 0.5f;
            bool condition = gates[0].FallbackCondition == SectionExitCondition.BossDefeated;
            return Check(report, "exit door on the far side, at the entry's height, BossDefeated (D-165, D-180, D-183)", far && level && condition,
                         $"entry z {entry.z:0.0} y {entry.y:0.0} -> exit z {exit.z:0.0} y {exit.y:0.0}, gate fallback {gates[0].FallbackCondition}");
        }

        /// D-188: 4 ammo with respawn; D-194: 1 health, no respawn; each group >= count + 2 spots.
        static bool CheckBossPickups(StringBuilder report, Scene scene)
        {
            List<GunData> runStartGuns = EditorRunStartGuns();
            List<ItemPickup> pickups = InScene<ItemPickup>(scene);
            var ammo = pickups.Where(p => p.Item != null && IsAmmoItem(p.Item, runStartGuns)).ToList();
            var health = pickups.Where(p => p.Item is HealthItemData).ToList();
            bool ok = Check(report, "4 ammo pickups (D-188) and 1 health pickup (D-194)", ammo.Count == 4 && health.Count == 1,
                            $"ammo {ammo.Count}, health {health.Count}");
            foreach (PickupGroup g in InScene<PickupGroup>(scene))
            {
                int count = g.PickupCount, spots = g.Spots.Count(s => s != null);
                ok &= Check(report, $"pickup group '{g.GroupId}': {count} pickup(s), >= {count + 2} spots to roll from", spots >= count + 2, spots + " spots");
            }
            List<BossPickupRespawner> respawners = InScene<BossPickupRespawner>(scene);
            if (respawners.Count == 1)
            {
                var so = new SerializedObject(respawners[0]);
                SerializedProperty list = so.FindProperty("pickups");
                var listed = new HashSet<Object>();
                for (int i = 0; i < list.arraySize; i++) listed.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
                bool exact = listed.Count == ammo.Count && ammo.All(p => listed.Contains(p)) && !health.Any(p => listed.Contains(p));
                bool bossSet = so.FindProperty("boss").objectReferenceValue != null;
                ok &= Check(report, "ammo respawner lists exactly the 4 ammo pickups, not the health (D-188, D-194)", exact && bossSet,
                            $"{listed.Count} listed, boss {(bossSet ? "set" : "MISSING")}");
            }
            else ok &= Check(report, "one ammo respawner (D-188)", false, respawners.Count + " found");
            return ok;
        }

        static bool CheckBullColumns(StringBuilder report, Scene scene)
        {
            List<BullColumn> columns = InScene<BullColumn>(scene);
            int unbreakable = columns.Count(c => !c.Breakable);
            int needed = Mathf.Max(4, Mathf.CeilToInt(columns.Count * 0.5f));
            bool ok = Check(report, ">= 8 columns, >= 50% and >= 4 unbreakable (D-180, D-190)", columns.Count >= 8 && unbreakable >= needed,
                            $"{columns.Count} columns, {unbreakable} unbreakable (need {needed})");
            if (columns.Count > 0)
            {
                float minZ = columns.Min(c => c.transform.position.z), maxZ = columns.Max(c => c.transform.position.z);
                ok &= Check(report, "columns spread along the length (D-180)", maxZ - minZ >= BullArenaLength * 0.5f, $"z {minZ:0} to {maxZ:0}");
            }
            foreach (BullColumn c in columns.Where(c => c.Breakable))
            {
                var so = new SerializedObject(c);
                bool wired = so.FindProperty("solid").objectReferenceValue != null && so.FindProperty("rubble").objectReferenceValue != null &&
                             so.FindProperty("obstacle").objectReferenceValue is NavMeshObstacle o && o.carving;
                var modifier = c.GetComponent<NavMeshModifier>();
                bool keptOutOfBake = modifier != null && modifier.ignoreFromBuild;
                bool rubbleHarmless = c.GetComponentsInChildren<Collider>(true).All(col => col.transform.IsChildOf(((GameObject)so.FindProperty("solid").objectReferenceValue).transform));
                ok &= Check(report, $"  breakable column '{c.ColumnId}': solid, rubble without colliders, carving obstacle, not baked",
                            wired && keptOutOfBake && rubbleHarmless, "");
            }
            int unique = columns.Select(c => c.ColumnId).Distinct().Count();
            ok &= Check(report, "column ids unique", unique == columns.Count, "");
            return ok;
        }

        /// <summary>
        /// The D-186 platform rules on one graph (Preacher arena, and the large sections per D-242): every platform
        /// reachable from the floor and with a way back down, headroom >= 2 m between overlapping static platforms, ramps
        /// <= 40 degrees. Each problem platform also gets its own [TWR] warning (label = where, decisions = the IDs to cite).
        /// </summary>
        static bool CheckPlatformGraph(StringBuilder report, PlatformGraph graph, PlayerTuning tuning, string label, string decisions)
        {
            bool ok = true;
            report.AppendLine($"  reachability (D-186): sprint {tuning.sprintSpeed} m/s, jump {tuning.jumpHeight} m, gravity {tuning.gravity}: " +
                              $"max gap flat {MaxJumpGap(0f, tuning):0.00} m, +0.9 m {MaxJumpGap(JumpStepUp, tuning):0.00} m");
            ok &= Check(report, "floor marked as the start of the reachability graph", graph.FloorCount >= 1, graph.FloorCount.ToString());

            int platforms = 0, unreachable = 0, stuck = 0, headroom = 0, steep = 0, tiny = 0;
            var tinyNames = new List<string>();
            foreach (PlatformNode n in graph.Nodes)
            {
                if (n.Kind == ArenaPlatformKind.Floor) continue;
                platforms++;
                if (!n.Reachable)
                {
                    unreachable++;
                    Debug.LogWarning($"[TWR] {label}: platform '{n.Id}' (top {n.ExitTop:0.0} m) can't be reached from the floor with " +
                                     $"walk / sprint / one jump ({decisions}).", n.Source);
                }
                if (!n.ReachesFloor)
                {
                    stuck++;
                    Debug.LogWarning($"[TWR] {label}: from platform '{n.Id}' there is no way back down to the floor (D-186).", n.Source);
                }
                if (n.Kind == ArenaPlatformKind.Ramp && n.Slope > MaxRampDegrees)
                {
                    steep++;
                    Debug.LogWarning($"[TWR] {label}: ramp '{n.Id}' is {n.Slope:0} degrees (max {MaxRampDegrees}, D-186).", n.Source);
                }
                if (n.MaxX - n.MinX < 0.8f || n.MaxZ - n.MinZ < 0.8f)
                {
                    tiny++;
                    tinyNames.Add($"{n.Id} ({n.MaxX - n.MinX:0.0} x {n.MaxZ - n.MinZ:0.0} m, {n.Kind})");
                }
                if (n.Moving) continue; // mover headroom is checked along its whole path (D-242)
                foreach (PlatformNode above in graph.Nodes)
                {
                    if (above == n || above.Kind == ArenaPlatformKind.Floor || above.Moving) continue;
                    float ox = Mathf.Min(n.MaxX, above.MaxX) - Mathf.Max(n.MinX, above.MinX);
                    float oz = Mathf.Min(n.MaxZ, above.MaxZ) - Mathf.Max(n.MinZ, above.MinZ);
                    float clearance = above.Bottom - n.ExitTop;
                    if (ox > 0.05f && oz > 0.05f && clearance > 0.01f && clearance < MinHeadroom)
                    {
                        headroom++;
                        Debug.LogWarning($"[TWR] {label}: only {clearance:0.00} m headroom above '{n.Id}' (under '{above.Id}', D-186 wants {MinHeadroom} m).", n.Source);
                        break;
                    }
                }
            }
            ok &= Check(report, $"every platform reachable from the floor with base movement ({decisions})", unreachable == 0, $"{platforms} platforms, {unreachable} unreachable");
            ok &= Check(report, "every platform has a way back down (drops allowed, D-186)", stuck == 0, stuck + " stuck");
            ok &= Check(report, $"headroom >= {MinHeadroom} m, ramps <= {MaxRampDegrees} deg (D-186)", headroom == 0 && steep == 0, $"{headroom} low ceilings, {steep} steep ramps");
            if (tiny > 0) report.AppendLine($"  WARNING: {tiny} platform(s) narrower than 0.8 m: {string.Join(", ", tinyNames)}");
            float highest = graph.Nodes.Where(n => n.Kind != ArenaPlatformKind.Floor).Select(n => n.ExitTop).DefaultIfEmpty(0f).Max();
            report.AppendLine($"  highest platform top: {highest:0.0} m");
            return ok;
        }

        static bool CheckPreacherArena(StringBuilder report, Scene scene, PlayerTuning tuning)
        {
            PlatformGraph graph = BuildPlatformGraph(scene, tuning);
            bool ok = CheckPlatformGraph(report, graph, tuning, "Preacher arena", "D-183, D-186");

            // Cavities (D-187).
            List<PreacherCavity> cavities = InScene<PreacherCavity>(scene);
            int uniqueIds = cavities.Select(c => c.CavityId).Distinct().Count();
            ok &= Check(report, $">= {MinCavities} dark cavities with unique ids (D-187)", cavities.Count >= MinCavities && uniqueIds == cavities.Count,
                        $"{cavities.Count} cavities, {uniqueIds} unique ids");
            var brain = InScene<PreacherBrain>(scene).FirstOrDefault();
            if (brain != null)
            {
                SerializedProperty list = new SerializedObject(brain).FindProperty("cavities");
                ok &= Check(report, "the Preacher knows every cavity", list.arraySize == cavities.Count, $"{list.arraySize} of {cavities.Count}");
            }

            Physics.SyncTransforms();
            List<Collider> blockers = SolidColliders(scene);
            var spots = new List<Vector3>(); // eye points of reachable standing spots
            foreach (PlatformNode n in graph.Nodes.Where(n => n.Reachable))
            {
                if (n.Kind == ArenaPlatformKind.Floor)
                {
                    for (float x = n.MinX + 2f; x < n.MaxX - 1f; x += 4f)
                        for (float z = n.MinZ + 2f; z < n.MaxZ - 1f; z += 4f)
                            spots.Add(new Vector3(x, n.ExitTop + 1.6f, z));
                }
                else spots.Add(n.Centre + Vector3.up * 1.6f);
            }
            int unfair = 0;
            foreach (PreacherCavity cavity in cavities)
            {
                Vector3 chest = cavity.Position + Vector3.up * 1.8f;
                bool seen = spots.Where(s => Vector3.Distance(s, chest) <= CavitySightRange)
                                 .OrderBy(s => (s - chest).sqrMagnitude)
                                 .Any(s => !Blocked(s, chest, blockers));
                bool onReachable = graph.IsReachable(cavity.Position, out string onWhat);
                if (!seen || !onReachable)
                {
                    unfair++;
                    Debug.LogWarning($"[TWR] Preacher arena: cavity '{cavity.CavityId}' {(onReachable ? "" : $"stands on {onWhat} (not reachable); ")}" +
                                     $"{(seen ? "" : "has no reachable standing spot within 35 m with line of sight")} (D-187).", cavity);
                }
            }
            ok &= Check(report, "every cavity: on a reachable surface, seen from a reachable spot within 35 m (D-187)", unfair == 0, unfair + " unfair");
            return ok;
        }
    }
}
