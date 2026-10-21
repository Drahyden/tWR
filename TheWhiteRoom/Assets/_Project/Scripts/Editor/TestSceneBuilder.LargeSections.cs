using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using TWR.Core;
using TWR.Enemies;
using TWR.Level;
using TWR.Player;
using TWR.Portals;
using TWR.Puzzles;
using TWR.Run;

namespace TWR.EditorTools
{
    /// <summary>
    /// D-240 to D-247 (large section rooms with platforms) for "TWR/Run/Build Run Scenes":
    /// - the moving-platform data (Data/Level: MovingPlatform_Horizontal, MovingPlatform_Lift, D-241);
    /// - the large puzzle room (48 x 24 x 12 m, exit at 6 m, switches per D-244), the combat room platforms (64 x 40 x 10 m,
    ///   exit at 4.5 m) and the impossible hall behind the IG portal (120 x 40 x 14 m, exit at 7.5 m);
    /// - every walkable piece is an ArenaPlatform for the D-186/D-242 reachability check; movers are left out of the NavMesh;
    /// - Seeker jump links (NavMeshLink, D-243) between static platforms, made from the same reachability graph.
    /// Geometry is greybox (D-100). Layout numbers are in the code comments next to each piece.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string LevelDataFolder = Root + "/Data/Level";
        const string MoverHorizontalPath = LevelDataFolder + "/MovingPlatform_Horizontal.asset";
        const string MoverLiftPath = LevelDataFolder + "/MovingPlatform_Lift.asset";

        // D-240 puzzle room: x 10.2..58.2 (48 m long, entry west -> exit east), z -12..12 (24 m wide), 12 m high.
        const float LpMinX = 10.2f, LpMaxX = 58.2f, LpMinZ = -12f, LpMaxZ = 12f, LpHeight = 12f;
        const float LpExitSill = 6f, LpExitZ = 0f;
        // D-240 combat room (BuildSectionShell): x -20..20, z 4..68, 10 m high, exit sill 4.5 m.
        const float LcWidth = 40f, LcLength = 64f, LcHeight = 10f, LcExitSill = 4.5f;
        // D-240 impossible hall of the IG section: x 500..620, z -36..4, 14 m high, exit in the east wall at z -16, 7.5 m.
        // (The lobby's curiosity hall keeps HallMinX.. = 30 x 20 x 8 m, D-146.)
        const float IgHallMinX = 500f, IgHallMaxX = 620f, IgHallMinZ = -36f, IgHallMaxZ = 4f, IgHallHeight = 14f;
        const float IgHallExitSill = 7.5f, IgHallExitZ = -16f;

        const float SlabThickness = 0.4f;
        const float MoverThickness = 0.25f;
        const float RampDegrees = 25f;
        // D-243 Seeker links: jump up <= 0.9 m (both ways), drop <= 3.5 m (one way), gap <= 2 m, cost x3.
        const float SeekerLinkUp = 0.9f, SeekerLinkDrop = 3.5f, SeekerLinkGap = 2f, SeekerLinkCost = 3f;
        const int MaxSeekerLinks = 40; // D-247
        // NavMesh bake: walkable patches are trimmed by the agent radius on every side, and patches smaller than the
        // surface's minRegionArea (default 2 m²) are dropped; a link can't end on a platform with no NavMesh (D-243).
        const float SeekerAgentRadius = 0.4f, MinNavMeshRegionArea = 2f;

        struct LevelAssets
        {
            public MovingPlatformData horizontal, lift;
            public Material mover;
        }

        // ---------- assets (D-241) ----------

        static bool PrepareLevelAssets(out LevelAssets lv)
        {
            lv = default;
            EnsureFolder(LevelDataFolder);
            int blockers = LayerMask.GetMask(GameLayers.Player, GameLayers.Enemy);
            lv.horizontal = LoadOrCreate<MovingPlatformData>(MoverHorizontalPath, d =>
            {
                d.speed = 2f;
                d.acceleration = 2f;
                d.pauseAtA = 1.5f;
                d.pauseAtB = 1.5f;
                d.blockers = blockers;
            });
            lv.lift = LoadOrCreate<MovingPlatformData>(MoverLiftPath, d =>
            {
                d.speed = 1.5f;
                d.acceleration = 2f;
                d.pauseAtA = 2f;   // longer wait at the bottom (D-241)
                d.pauseAtB = 1.5f;
                d.blockers = blockers;
            });
            if (lv.horizontal == null || lv.lift == null)
            {
                Debug.LogError("[TWR] Could not create the moving platform data in " + LevelDataFolder + " (D-241).");
                return false;
            }
            foreach (MovingPlatformData d in new[] { lv.horizontal, lv.lift })
            {
                if (d.blockers.value != 0) continue;
                d.blockers = blockers; // repair: an empty mask would let it push people
                EditorUtility.SetDirty(d);
            }
            AssetDatabase.SaveAssets();
            lv.mover = GetOrCreateMaterial("M_MovingPlatform", new Color(0.8f, 0.85f, 0.9f), new Color(0.12f, 0.16f, 0.22f));
            return true;
        }

        // ---------- the large puzzle room (D-240, D-244) ----------

        /// <summary>
        /// The run's puzzle room. Static route: stairs along the north wall (0.25 m rise, 1 m treads) to a 3 m gallery, more
        /// stairs to the exit ledge (x 52.2..58.2, z -5..12, 6 m). Jump route: solid towers 0.9 / 1.8 / 2.7 / 3.6 / 4.5 m (gaps 1.2 m),
        /// a horizontal mover at 5.4 m over to the ledge, and a lift from the floor to 4.5 m beside the last tower.
        /// Switch slots: A chest height (entry wall), B 10 m up the south wall above a shelf that hides it from the floor,
        /// C behind bars in a roofed cage, D (spare) hanging under the mover. Ammo inside, the exit door locked until solved.
        /// The rolls (enemies, slot setup, ammo spots) are added by AuthorPuzzleSectionRolls.
        /// </summary>
        static void BuildLargePuzzleRoom(Transform root, RunSceneAssets a, LevelAssets lv)
        {
            Material switchMat = GetOrCreateMaterial("M_PuzzleSwitch", new Color(0.55f, 0.05f, 0.04f), new Color(0.3f, 0.03f, 0.02f));
            Material barMat = GetOrCreateMaterial("M_Bars", new Color(0.3f, 0.3f, 0.32f), Color.black);
            Transform group = new GameObject("Puzzle Room (large, D-240, D-244)").transform;
            group.SetParent(root, false);

            // Shell. West wall (x 10.1) keeps the 2.4 x 3 m doorway from the vestibule; east wall has the raised exit.
            Transform shell = new GameObject("Room Shell").transform;
            shell.SetParent(group, false);
            float cx = (LpMinX + LpMaxX) * 0.5f, sx = LpMaxX - LpMinX, sz = LpMaxZ - LpMinZ, h = LpHeight;
            GameObject floor = Block("Floor", new Vector3(cx, -0.1f, 0f), new Vector3(sx + 0.4f, 0.2f, sz + 0.4f), a.white, shell);
            AddPlatformMarker(floor, "puzzle.floor", ArenaPlatformKind.Floor);
            Block("Ceiling", new Vector3(cx, h + 0.1f, 0f), new Vector3(sx + 0.4f, 0.2f, sz + 0.4f), a.whiteEmissive, shell);
            Block("Wall N", new Vector3(cx, h * 0.5f, LpMaxZ + 0.1f), new Vector3(sx + 0.4f, h + 0.2f, 0.2f), a.whiteEmissive, shell);
            Block("Wall S", new Vector3(cx, h * 0.5f, LpMinZ - 0.1f), new Vector3(sx + 0.4f, h + 0.2f, 0.2f), a.whiteEmissive, shell);
            WallAlongZ(shell, "Wall W", LpMinX - 0.1f, LpMinZ - 0.2f, LpMaxZ + 0.2f, h, a.whiteEmissive,
                       MainDoorwayHalfWidth * 2f, MainDoorwayHeight, 0f);
            RaisedOpeningWall(shell, "Wall E", false, LpMaxX + 0.1f, LpMinZ - 0.2f, LpMaxZ + 0.2f, h, LpExitZ, LpExitSill, a.whiteEmissive);

            Transform plat = new GameObject("Platforms (D-240, D-242)").transform;
            plat.SetParent(group, false);
            // Static route (Seekers walk it too, D-243): 12 steps x 1.0 m (>= 1 m to stand on) from the west wall -> gallery
            // 3 m (x 22.2..40.2) -> 12 steps -> exit ledge 6 m.
            Stairs("puzzle.stairs.a", true, LpMinX, 1, 9f, 12f, 12, 1f, 0f, 0.25f, 0f, a.offWhite, plat);
            WalkRect("puzzle.gallery", 22.2f, 40.2f, 9f, 12f, 3f, SlabThickness, ArenaPlatformKind.Platform, a.offWhite, plat);
            Stairs("puzzle.stairs.b", true, 40.2f, 1, 9f, 12f, 12, 1f, 3f, 0.25f, 0f, a.offWhite, plat);
            WalkRect("puzzle.exit_ledge", 52.2f, LpMaxX, -5f, LpMaxZ, LpExitSill, 0.5f, ArenaPlatformKind.Platform, a.offWhite, plat);

            // Parapets on the ledge (D-245), open where the mover (z -3.5..-0.5) and the stairs (z 9..12) arrive.
            Transform rails = new GameObject("Ledge Parapets (D-245)").transform;
            rails.SetParent(group, false);
            Parapet("Parapet S", 52.2f, LpMaxX, -5f, -4.8f, LpExitSill, a.offWhite, rails);
            Parapet("Parapet W (south)", 52.2f, 52.4f, -4.8f, -3.8f, LpExitSill, a.offWhite, rails);
            Parapet("Parapet W (middle)", 52.2f, 52.4f, -0.2f, 9f, LpExitSill, a.offWhite, rails);

            // Jump chain: solid towers, each 0.9 m higher, 1.2 m gaps (D-186 allows about 2.1 m at +0.9 m).
            WalkRect("puzzle.chain.1", 22f, 25f, -3.5f, -0.5f, 0.9f, 0.9f, ArenaPlatformKind.Platform, a.white, plat);
            WalkRect("puzzle.chain.2", 26.2f, 29.2f, -3.5f, -0.5f, 1.8f, 1.8f, ArenaPlatformKind.Platform, a.white, plat);
            WalkRect("puzzle.chain.3", 27.7f, 30.7f, -7.7f, -4.7f, 2.7f, 2.7f, ArenaPlatformKind.Platform, a.white, plat);
            WalkRect("puzzle.chain.4", 31.9f, 34.9f, -7.7f, -4.7f, 3.6f, 3.6f, ArenaPlatformKind.Platform, a.white, plat);
            WalkRect("puzzle.chain.5", 33.4f, 36.4f, -3.5f, -0.5f, 4.5f, 4.5f, ArenaPlatformKind.Platform, a.white, plat);

            // Movers (D-241): the bridge from the last tower to the ledge (0.6 m gaps at both ends, +0.9 / +0.6 m),
            // and the lift beside the last tower (floor -> 4.5 m).
            MovingPlatform bridge = Mover("puzzle.mover.bridge", new Vector3(38.5f, 5.4f, -2f), new Vector2(3f, 3f),
                                          new Vector3(11.6f, 0f, 0f), lv.horizontal, lv.mover, plat, false);
            Mover("puzzle.mover.lift", new Vector3(34.9f, MoverThickness, 1.6f), new Vector2(3f, 3f),
                  new Vector3(0f, 4.5f - MoverThickness, 0f), lv.lift, lv.mover, plat, true);

            // Switch B's shelf: 3.2 m deep, 1 m below the switch, so no floor spot can see it (D-244).
            Block("Shelf Under Switch B (hides it from the floor, D-244)", new Vector3(30f, 8.9f, LpMinZ + 1.6f),
                  new Vector3(8f, 0.2f, 3.2f), a.offWhite, group);

            var switches = new ShootableSwitch[4]; // list order = Sequence order (D-124)
            // A on the entry (west) wall, south of the doorway: the north wall near the entry is taken by the stairs.
            switches[0] = BuildSwitch("Switch A (chest height, entry wall)", "switch.easy", new Vector3(LpMinX, 1.3f, -6f), Vector3.right,
                                      a.offWhite, switchMat, group);
            switches[1] = BuildSwitch("Switch B (10 m up the south wall)", "switch.high", new Vector3(30f, 10f, LpMinZ), Vector3.forward,
                                      a.offWhite, switchMat, group);
            switches[2] = BuildSwitch("Switch C (behind bars)", "switch.caged", new Vector3(17f, 1.2f, LpMinZ), Vector3.forward,
                                      a.offWhite, switchMat, group);
            // D hangs under the bridge's south edge, facing south: a moving target (seeded runs only, D-244).
            LocalBlock("Switch Hanger", bridge.transform, new Vector3(0f, -0.6f, -1.45f), new Vector3(0.8f, 0.7f, 0.1f), a.offWhite, false, true);
            switches[3] = BuildSwitch("Switch D (under the moving bridge, spare slot)", "switch.south",
                                      bridge.transform.position + new Vector3(0f, -0.6f, -1.5f), Vector3.back, a.offWhite, switchMat, bridge.transform);
            BuildRoofedCage(group, a.offWhite, barMat);

            // Ammo on the way in (D-130); its other roll spots are set by AuthorPuzzleSectionRolls (D-177).
            AmmoPickup("Ammo Pickup (puzzle)", new Vector3(12.5f, 0f, -4.5f), a.combat, a.ammo, group);

            // Puzzle-area trigger just inside the doorway (Started, D-126).
            GameObject area = TriggerVolume("Puzzle Area Trigger", new Vector3(11.3f, 0f, 0f), new Vector3(1.8f, 3f, 3f), a.combat, group);
            var areaTrigger = area.AddComponent<PuzzleAreaTrigger>();

            // The raised exit: locked until solved (D-127), SectionExitGate MainPuzzlesSolved (D-165).
            ExitPassage exit = BuildExitPassage(root, a, "Exit (raised 6 m, D-240)", "door.puzzle_exit", new Vector3(LpMaxX + 0.1f, LpExitSill, LpExitZ),
                                                Vector3.right, RunPath.None, "puzzle.exit", true, true, SectionExitCondition.MainPuzzlesSolved, a.door);

            var controllerGO = new GameObject("Puzzle Controller (test.switches)");
            controllerGO.transform.SetParent(group, false);
            controllerGO.transform.position = new Vector3(cx, 0f, 0f);
            var controller = controllerGO.AddComponent<PuzzleController>();
            Assign(controller, "data", a.puzzle.data);
            Assign(controller, "startArea", areaTrigger);
            AssignArray(controller, "switches", switches);
            AssignArray(controller, "outputs", new Object[] { exit.door.doorLock });
            new GameObject("Puzzle Event Logger").AddComponent<PuzzleEventLogger>().transform.SetParent(group, false);
        }

        /// Switch C's alcove on the south wall (x 15.5..18.5): solid sides, thin bars 2.7 m in front of the switch (more
        /// than the 2.5 m reach, D-125) and a roof at 4 m, so nobody climbs in over the bars. No glass (D-105).
        static void BuildRoofedCage(Transform group, Material sideMat, Material barMat)
        {
            Transform cage = new GameObject("Cage (bars with gaps, roofed)").transform;
            cage.SetParent(group, false);
            const float left = 15.5f, right = 18.5f, h = 4f;
            float front = LpMinZ + 2.7f, mid = (LpMinZ + front) * 0.5f, depth = front - LpMinZ;
            Block("Cage Side W", new Vector3(left - 0.1f, h * 0.5f, mid), new Vector3(0.2f, h, depth), sideMat, cage);
            Block("Cage Side E", new Vector3(right + 0.1f, h * 0.5f, mid), new Vector3(0.2f, h, depth), sideMat, cage);
            Block("Cage Roof", new Vector3((left + right) * 0.5f, h + 0.1f, mid), new Vector3(right - left + 0.4f, 0.2f, depth + 0.2f), sideMat, cage);
            float pitch = (right - left) / BarCount;
            for (int i = 0; i < BarCount; i++)
                Block("Bar " + i, new Vector3(left + pitch * (i + 0.5f), h * 0.5f, front), new Vector3(BarThickness, h, BarThickness), barMat, cage);
        }

        // ---------- the large combat room (D-240) ----------

        /// <summary>
        /// Solid 3 m galleries along both long walls (x -20..-16 / 16..20, z 14..64) joined by north walkways, each reached
        /// by 25-degree ramps at the south end and mid-length; six 0.25 m steps on each walkway up to the exit ledge
        /// (x -5..5, z 63..68, 4.5 m), so the exit never needs a mover (D-242). Low islands 0.9 / 1.8 m, a central 3 m pillar
        /// and two horizontal movers between the galleries and the pillar at 3 m (shortcuts). Room shell built by the caller.
        /// </summary>
        static void BuildCombatPlatforms(Transform root, RunSceneAssets a, LevelAssets lv)
        {
            Transform plat = new GameObject("Platforms (D-240, D-242)").transform;
            plat.SetParent(root, false);
            WalkRect("combat.gallery.w", -20f, -16f, 14f, 64f, 3f, 3f, ArenaPlatformKind.Platform, a.offWhite, plat);
            WalkRect("combat.gallery.e", 16f, 20f, 14f, 64f, 3f, 3f, ArenaPlatformKind.Platform, a.offWhite, plat);
            WalkRect("combat.walkway.nw", -20f, -5f, 64f, 68f, 3f, 3f, ArenaPlatformKind.Platform, a.offWhite, plat);
            WalkRect("combat.walkway.ne", 5f, 20f, 64f, 68f, 3f, 3f, ArenaPlatformKind.Platform, a.offWhite, plat);
            WalkRect("combat.exit_ledge", -5f, 5f, 63f, 68f, LcExitSill, SlabThickness, ArenaPlatformKind.Platform, a.offWhite, plat);
            // 1.0 m treads (>= 1 m to stand on): walkway x -11..-5 / 5..11 up to the ledge.
            Stairs("combat.steps.w", true, -11f, 1, 64f, 68f, 6, 1f, 3f, 0.25f, 3f, a.offWhite, plat);
            Stairs("combat.steps.e", true, 11f, -1, 64f, 68f, 6, 1f, 3f, 0.25f, 3f, a.offWhite, plat);

            float run = 3f / Mathf.Tan(RampDegrees * Mathf.Deg2Rad); // 6.43 m
            Ramp("combat.ramp.w.south", new Vector3(-18f, 0f, 14f - run), new Vector3(-18f, 3f, 14f), 4f, a.white, plat);
            Ramp("combat.ramp.e.south", new Vector3(18f, 0f, 14f - run), new Vector3(18f, 3f, 14f), 4f, a.white, plat);
            Ramp("combat.ramp.w.mid", new Vector3(-16f + run, 0f, 39.5f), new Vector3(-16f, 3f, 39.5f), 3f, a.white, plat);
            Ramp("combat.ramp.e.mid", new Vector3(16f - run, 0f, 39.5f), new Vector3(16f, 3f, 39.5f), 3f, a.white, plat);

            WalkRect("combat.island.w", -9f, -5f, 26f, 30f, 0.9f, 0.9f, ArenaPlatformKind.Platform, a.white, plat);
            WalkRect("combat.island.e", 5f, 9f, 26f, 30f, 0.9f, 0.9f, ArenaPlatformKind.Platform, a.white, plat);
            WalkRect("combat.island.n", -2.5f, 2.5f, 37.5f, 42.5f, 1.8f, 1.8f, ArenaPlatformKind.Platform, a.white, plat);
            // 3 x 3 m: after the 0.4 m agent-radius trim it keeps 2.2 x 2.2 m of NavMesh (the bake drops patches < 2 m²).
            WalkRect("combat.island.n.step", 2.5f, 5.5f, 38.5f, 41.5f, 0.9f, 0.9f, ArenaPlatformKind.Platform, a.white, plat);
            WalkRect("combat.pillar", -1.5f, 1.5f, 48f, 51f, 3f, 3f, ArenaPlatformKind.Platform, a.offWhite, plat);

            // Movers at 3 m: gallery edge (x -16) + 0.6 m gap -> pillar edge (x -1.5) - 0.6 m gap, 10.3 m travel.
            Mover("combat.mover.w", new Vector3(-13.9f, 3f, 49.5f), new Vector2(3f, 3f), new Vector3(10.3f, 0f, 0f), lv.horizontal, lv.mover, plat, false);
            Mover("combat.mover.e", new Vector3(13.9f, 3f, 49.5f), new Vector2(3f, 3f), new Vector3(-10.3f, 0f, 0f), lv.horizontal, lv.mover, plat, false);
        }

        // ---------- the large impossible hall (D-240, D-245) ----------

        /// <summary>
        /// 120 x 40 x 14 m hall 500 m east (D-144) with the return portal in its west wall (same alcove + portal as D-146).
        /// Main route, oversized office furniture: a paper-ream stair beside the portal (so you look back at the "Behind you"
        /// spot, D-245), four giant desks (2.75 -> 4.5 m), a mover over the gap to the fifth desk, a lift 4.5 -> 7.5 m,
        /// five giant bookshelf tops and the exit ledge (x 612..620, z -20..-12). Static fallback: a 25-degree ramp along
        /// the north wall and walkways to the ledge. The exit passage itself is added by the caller.
        /// </summary>
        static Portal BuildLargeHall(Transform hall, RunSceneAssets a, LevelAssets lv)
        {
            float h = IgHallHeight;
            float cx = (IgHallMinX + IgHallMaxX) * 0.5f, cz = (IgHallMinZ + IgHallMaxZ) * 0.5f;
            float sx = IgHallMaxX - IgHallMinX, sz = IgHallMaxZ - IgHallMinZ;
            Material wallMat = a.whiteEmissive;

            Transform shell = new GameObject("Hall Shell").transform;
            shell.SetParent(hall, false);
            GameObject floor = Block("Floor", new Vector3(cx, -0.1f, cz), new Vector3(sx + 0.4f, 0.2f, sz + 0.4f), a.white, shell);
            AddPlatformMarker(floor, "ig.hall.floor", ArenaPlatformKind.Floor);
            Block("Ceiling", new Vector3(cx, h + 0.1f, cz), new Vector3(sx + 0.4f, 0.2f, sz + 0.4f), wallMat, shell);
            Block("Wall N", new Vector3(cx, h * 0.5f, IgHallMaxZ + 0.1f), new Vector3(sx + 0.4f, h + 0.2f, 0.2f), wallMat, shell);
            Block("Wall S", new Vector3(cx, h * 0.5f, IgHallMinZ - 0.1f), new Vector3(sx + 0.4f, h + 0.2f, 0.2f), wallMat, shell);
            RaisedOpeningWall(shell, "Wall E", false, IgHallMaxX + 0.1f, IgHallMinZ - 0.2f, IgHallMaxZ + 0.2f, h, IgHallExitZ, IgHallExitSill, wallMat);
            float zPortal = HallPortalPosition.z, half = OpeningWidth * 0.5f;
            float southLength = (zPortal - half) - IgHallMinZ, northLength = IgHallMaxZ - (zPortal + half);
            Block("Wall W (south)", new Vector3(IgHallMinX - 0.1f, h * 0.5f, IgHallMinZ + southLength * 0.5f), new Vector3(0.2f, h + 0.2f, southLength), wallMat, shell);
            Block("Wall W (north)", new Vector3(IgHallMinX - 0.1f, h * 0.5f, IgHallMaxZ - northLength * 0.5f), new Vector3(0.2f, h + 0.2f, northLength), wallMat, shell);
            Block("Wall W (above portal)", new Vector3(IgHallMinX - 0.1f, (OpeningHeight + h + 0.2f) * 0.5f, zPortal),
                  new Vector3(0.2f, h + 0.2f - OpeningHeight, OpeningWidth), wallMat, shell);

            Portal hallPortal = BuildReturnPortal(a.portal, hall, a.white, a.offWhite, "ig.hall.return", out Transform anchorT);

            Transform route = new GameObject("Route: oversized office furniture (D-054, D-062, D-240)").transform;
            route.SetParent(hall, false);
            // Paper-ream stair along the west wall, starting 3 m north of the portal (its 1 m vestibule stays clear, D-142).
            // 1 m treads (z -13..-2), then the first desk against the north wall (z -2..4).
            Stairs("ig.reams", false, -13f, 1, IgHallMinX, IgHallMinX + 3f, 11, 1f, 0f, 0.25f, 0f, a.white, route);
            GiantDesk("ig.desk.1", 500f, 508f, -2f, IgHallMaxZ, 2.75f, a.wood, a.offWhite, route);
            GiantDesk("ig.desk.2", 510f, 518f, -4f, 2f, 3.35f, a.wood, a.offWhite, route);
            GiantDesk("ig.desk.3", 520f, 528f, -6f, 0f, 3.95f, a.wood, a.offWhite, route);
            GiantDesk("ig.desk.4", 530f, 538f, -4f, 2f, 4.5f, a.wood, a.offWhite, route);
            Mover("ig.mover.desk_gap", new Vector3(540.1f, 4.5f, -1f), new Vector2(3f, 3f), new Vector3(9f, 0f, 0f), lv.horizontal, lv.mover, route, false);
            GiantDesk("ig.desk.5", 551.2f, 559.2f, -4f, 2f, 4.5f, a.wood, a.offWhite, route);
            Mover("ig.mover.lift", new Vector3(561.3f, 4.5f, -1f), new Vector2(3f, 3f), new Vector3(0f, 3f, 0f), lv.lift, lv.mover, route, true);
            WalkRect("ig.shelf.1", 563.4f, 571.4f, -4f, 1f, 7.5f, 7.5f, ArenaPlatformKind.Platform, a.wood, route);
            WalkRect("ig.shelf.2", 573.4f, 581.4f, -6f, 0f, 7.5f, 7.5f, ArenaPlatformKind.Platform, a.wood, route);
            WalkRect("ig.shelf.3", 583.4f, 591.4f, -12f, -6f, 7.5f, 7.5f, ArenaPlatformKind.Platform, a.wood, route);
            WalkRect("ig.shelf.4", 593.4f, 601.4f, -18f, -12f, 7.5f, 7.5f, ArenaPlatformKind.Platform, a.wood, route);
            WalkRect("ig.shelf.5", 603.4f, 611.4f, -20f, -12f, 7.5f, 7.5f, ArenaPlatformKind.Platform, a.wood, route);
            WalkRect("ig.exit_ledge", 612f, IgHallMaxX, -20f, -12f, IgHallExitSill, 0.5f, ArenaPlatformKind.Platform, a.offWhite, route);

            // Static fallback (D-240): 25-degree ramp along the north wall, then walkways to the exit ledge.
            float run = IgHallExitSill / Mathf.Tan(RampDegrees * Mathf.Deg2Rad); // 16.1 m
            Ramp("ig.ramp.north", new Vector3(602f - run, 0f, 2.5f), new Vector3(602f, IgHallExitSill, 2.5f), 3f, a.white, route);
            WalkRect("ig.walkway.n", 602f, IgHallMaxX, 1f, IgHallMaxZ, IgHallExitSill, IgHallExitSill, ArenaPlatformKind.Platform, a.offWhite, route);
            WalkRect("ig.walkway.e", 617f, IgHallMaxX, -12f, 1f, IgHallExitSill, IgHallExitSill, ArenaPlatformKind.Platform, a.offWhite, route);

            // Floor props (D-062), away from the route, plus the D-146 test target and F button.
            Transform props = new GameObject("Hall Props (office/home motifs)").transform;
            props.SetParent(hall, false);
            Block("Sofa Seat", new Vector3(540f, 0.25f, IgHallMinZ + 0.5f), new Vector3(2.4f, 0.5f, 0.9f), a.offWhite, props);
            Block("Sofa Back", new Vector3(540f, 0.65f, IgHallMinZ + 0.1f), new Vector3(2.4f, 0.8f, 0.2f), a.offWhite, props);
            Prop("Floor Lamp Pole", PrimitiveType.Cylinder, props, new Vector3(504f, 0.9f, IgHallMinZ + 1f), new Vector3(0.05f, 0.9f, 0.05f), a.offWhite, Vector3.zero, true);
            Prop("Floor Lamp Shade", PrimitiveType.Sphere, props, new Vector3(504f, 1.9f, IgHallMinZ + 1f), Vector3.one * 0.45f, wallMat, Vector3.zero, false);
            Prop("Plant Pot", PrimitiveType.Cylinder, props, new Vector3(618f, 0.25f, IgHallMinZ + 1f), new Vector3(0.5f, 0.25f, 0.5f), a.offWhite, Vector3.zero, true);
            Prop("Plant", PrimitiveType.Sphere, props, new Vector3(618f, 0.85f, IgHallMinZ + 1f), Vector3.one * 0.8f, a.plant, Vector3.zero, false);
            Block("Lone Door Frame Post L", new Vector3(560f, 1.2f, -26f), new Vector3(0.12f, 2.4f, 0.12f), a.offWhite, props);
            Block("Lone Door Frame Post R", new Vector3(561.2f, 1.2f, -26f), new Vector3(0.12f, 2.4f, 0.12f), a.offWhite, props);
            Block("Lone Door Frame Head", new Vector3(560.6f, 2.46f, -26f), new Vector3(1.32f, 0.12f, 0.12f), a.offWhite, props);
            BuildHallTestPieces(props, anchorT, a.offWhite, a.target);
            return hallPortal;
        }

        /// A giant desk: a walkable 0.3 m top on four 0.6 m legs (the space under it is >= 2 m high, D-186).
        static void GiantDesk(string id, float minX, float maxX, float minZ, float maxZ, float top, Material topMat, Material legMat, Transform parent)
        {
            const float slab = 0.3f, leg = 0.6f, inset = 0.3f + leg * 0.5f;
            WalkRect(id, minX, maxX, minZ, maxZ, top, slab, ArenaPlatformKind.Platform, topMat, parent);
            float legHeight = top - slab;
            foreach (float x in new[] { minX + inset, maxX - inset })
                foreach (float z in new[] { minZ + inset, maxZ - inset })
                    Block(id + " leg", new Vector3(x, legHeight * 0.5f, z), new Vector3(leg, legHeight, leg), legMat, parent);
        }

        // ---------- building blocks ----------

        /// The room shell's floor becomes the start of the reachability graph (D-186).
        static void MarkShellFloor(Transform root, string id)
        {
            Transform floor = root.Find("Room Shell/Floor");
            if (floor != null) AddPlatformMarker(floor.gameObject, id, ArenaPlatformKind.Floor);
            else Debug.LogError($"[TWR] '{root.name}': 'Room Shell/Floor' not found; reachability can't start from the floor (D-242).");
        }

        /// A walkable box for the D-186 graph, from its top face rectangle. thickness = top for solid towers.
        static GameObject WalkRect(string id, float minX, float maxX, float minZ, float maxZ, float top, float thickness,
                                   ArenaPlatformKind kind, Material mat, Transform parent)
        {
            GameObject go = Block(id, new Vector3((minX + maxX) * 0.5f, top - thickness * 0.5f, (minZ + maxZ) * 0.5f),
                                  new Vector3(maxX - minX, thickness, maxZ - minZ), mat, parent);
            AddPlatformMarker(go, id, kind);
            return go;
        }

        /// Straight stairs: 'count' steps of 'depth' from 'start' along X (alongX) or Z, in direction dir (+1/-1), across
        /// fixedMin..fixedMax. Each step is 'rise' above the last (first = baseTop + rise) and solid down to 'bottom'.
        static void Stairs(string idPrefix, bool alongX, float start, int dir, float fixedMin, float fixedMax, int count, float depth,
                           float baseTop, float rise, float bottom, Material mat, Transform parent)
        {
            for (int i = 0; i < count; i++)
            {
                float top = baseTop + rise * (i + 1);
                float p = start + dir * depth * i, q = start + dir * depth * (i + 1);
                float lo = Mathf.Min(p, q), hi = Mathf.Max(p, q);
                string id = $"{idPrefix}.{i + 1:00}";
                if (alongX) WalkRect(id, lo, hi, fixedMin, fixedMax, top, top - bottom, ArenaPlatformKind.Step, mat, parent);
                else WalkRect(id, fixedMin, fixedMax, lo, hi, top, top - bottom, ArenaPlatformKind.Step, mat, parent);
            }
        }

        /// A 0.3 m thick tilted walkway whose top face runs from 'low' to 'high' (centres of its two end edges).
        static GameObject Ramp(string id, Vector3 low, Vector3 high, float width, Material mat, Transform parent)
        {
            Vector3 run = high - low;
            Quaternion rotation = Quaternion.LookRotation(run.normalized, Vector3.up);
            const float thickness = 0.3f;
            Vector3 centre = (low + high) * 0.5f - rotation * Vector3.up * (thickness * 0.5f);
            GameObject go = Block(id, centre, new Vector3(width, thickness, run.magnitude), mat, parent);
            go.transform.rotation = rotation;
            AddPlatformMarker(go, id, ArenaPlatformKind.Ramp);
            return go;
        }

        /// <summary>
        /// A moving platform (D-241) built at point A: an unscaled root with the walkable BoxCollider (top face at 'topAtA'),
        /// a kinematic Rigidbody, a visual slab child, the MovingPlatform, an ArenaPlatform marker (D-242) and a
        /// NavMeshModifier that keeps it out of the bake (D-243). Lifts also carve the NavMesh while standing still, so
        /// Seekers walk around a lift that is down.
        /// </summary>
        static MovingPlatform Mover(string id, Vector3 topAtA, Vector2 size, Vector3 travel, MovingPlatformData data, Material mat,
                                    Transform parent, bool carveWhenStill)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.position = topAtA;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, MoverThickness, size.y);
            box.center = new Vector3(0f, -MoverThickness * 0.5f, 0f);
            var body = go.AddComponent<Rigidbody>(); // kinematic: a moving collider without physics pushes
            body.isKinematic = true;
            body.useGravity = false;
            LocalBlock("Slab", go.transform, box.center, box.size, mat, false, false);

            var mover = go.AddComponent<MovingPlatform>();
            Assign(mover, "data", data);
            var so = new SerializedObject(mover);
            so.FindProperty("travel").vector3Value = travel;
            so.ApplyModifiedPropertiesWithoutUndo();
            AddPlatformMarker(go, id, ArenaPlatformKind.Platform);
            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            if (carveWhenStill)
            {
                var obstacle = go.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = box.center;
                obstacle.size = box.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
            }
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            return mover;
        }

        /// A wall along X (alongX) or Z at fixedCoord with one RunDoor-sized opening whose sill is 'sill' metres up
        /// (D-240 raised exits). Piece names start with 'name' so the shell can be measured (D-242).
        static void RaisedOpeningWall(Transform parent, string name, bool alongX, float fixedCoord, float min, float max, float height,
                                      float centre, float sill, Material mat)
        {
            float left = centre - RunDoorWidth * 0.5f, right = centre + RunDoorWidth * 0.5f, top = sill + RunDoorHeight;
            void Piece(string label, float from, float to, float bottom, float upTo)
            {
                if (to - from < 0.01f || upTo - bottom < 0.01f) return;
                float along = (from + to) * 0.5f, length = to - from, y = (bottom + upTo) * 0.5f, tall = upTo - bottom;
                Vector3 position = alongX ? new Vector3(along, y, fixedCoord) : new Vector3(fixedCoord, y, along);
                Vector3 scale = alongX ? new Vector3(length, tall, 0.2f) : new Vector3(0.2f, tall, length);
                Block(label, position, scale, mat, parent);
            }
            Piece($"{name} (1)", min, left, 0f, height);
            Piece($"{name} (2)", right, max, 0f, height);
            Piece($"{name} (below opening)", left, right, 0f, sill);
            Piece($"{name} (above opening)", left, right, top, height);
        }

        /// A 1 m high, solid parapet standing on a ledge top (D-245).
        static void Parapet(string name, float minX, float maxX, float minZ, float maxZ, float floorY, Material mat, Transform parent) =>
            Block(name, new Vector3((minX + maxX) * 0.5f, floorY + 0.5f, (minZ + maxZ) * 0.5f), new Vector3(maxX - minX, 1f, maxZ - minZ), mat, parent);

        // ---------- Seeker jump links (D-243) ----------

        /// <summary>
        /// One NavMeshLink per pair of static platforms (or platform + floor) that a Seeker may jump between: up to 0.9 m up
        /// (both ways) or a drop of up to 3.5 m (one way), over a gap of up to 2 m; stairs and ramps are left to the NavMesh.
        /// A tower standing on the floor gets its link over the edge that faces the room, on a side where nothing is in the
        /// way. Ends are checked for clearance (walls, cover, other platforms, mover paths). Call after all geometry is built.
        /// </summary>
        static int AddSeekerLinks(Transform root, PlayerTuning tuning)
        {
            Physics.SyncTransforms();
            Scene scene = SceneManager.GetActiveScene();
            PlatformGraph graph = BuildPlatformGraph(scene, tuning, false);
            List<PlatformNode> all = graph.Nodes; // stairs and ramps too: link ends must stay clear of them
            var nodes = all.Where(n => n.Kind == ArenaPlatformKind.Floor || n.Kind == ArenaPlatformKind.Platform).ToList();
            var movers = InScene<MovingPlatform>(scene);
            Transform parent = new GameObject("Seeker Jump Links (NavMeshLink, D-243)").transform;
            parent.SetParent(root, false);

            int made = 0;
            for (int i = 0; i < nodes.Count; i++)
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    PlatformNode lo = nodes[i].ExitTop <= nodes[j].ExitTop ? nodes[i] : nodes[j];
                    PlatformNode hi = lo == nodes[i] ? nodes[j] : nodes[i];
                    float dy = hi.EntryTop - lo.ExitTop;
                    if (dy > SeekerLinkDrop || Covers(hi, lo)) continue;
                    if (!HasNavMeshArea(lo) || !HasNavMeshArea(hi)) continue; // too small to get NavMesh: no link end there
                    if (!TryLinkPoints(lo, hi, dy, all, movers, out Vector3 loPoint, out Vector3 hiPoint, out float width)) continue;
                    if (made >= MaxSeekerLinks)
                    {
                        Debug.LogWarning($"[TWR] '{root.name}': more than {MaxSeekerLinks} Seeker links possible; the rest are skipped (D-247).");
                        return made;
                    }
                    bool both = dy <= SeekerLinkUp;
                    Vector3 start = both ? loPoint : hiPoint, end = both ? hiPoint : loPoint;
                    var go = new GameObject($"Link {lo.Id} {(both ? "<->" : "<-")} {hi.Id}");
                    go.transform.SetParent(parent, false);
                    go.transform.position = start;
                    var link = go.AddComponent<NavMeshLink>();
                    link.agentTypeID = 0;
                    link.startPoint = Vector3.zero;
                    link.endPoint = end - start;
                    link.width = width;
                    link.costModifier = SeekerLinkCost;
                    link.bidirectional = both;
                    link.autoUpdate = false;
                    link.area = 0;
                    made++;
                }
            Debug.Log($"[TWR] '{root.name}': {made} Seeker jump links (D-243).");
            return made;
        }

        static bool TryLinkPoints(PlatformNode lo, PlatformNode hi, float dy, List<PlatformNode> nodes, List<MovingPlatform> movers,
                                  out Vector3 loPoint, out Vector3 hiPoint, out float width)
        {
            loPoint = hiPoint = Vector3.zero;
            width = 1f;
            if (Covers(lo, hi))
            {
                // hi stands on lo (a tower or gallery on the floor): jump over one of hi's edges, the room-facing one first,
                // at the middle of the edge or a little to either side if something is in the way there.
                if (dy <= WalkStepUp) return false;
                Vector3 c = hi.Centre, toward = lo.Centre - c;
                Vector3[] sides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                float[] slides = { 0f, -0.2f, 0.2f, -0.35f, 0.35f }; // fractions of the edge length
                foreach (Vector3 d in sides.OrderByDescending(s => Vector3.Dot(s, toward)))
                {
                    bool onX = Mathf.Abs(d.x) > 0.5f;
                    float halfAlong = onX ? (hi.MaxX - hi.MinX) * 0.5f : (hi.MaxZ - hi.MinZ) * 0.5f;
                    float across = onX ? hi.MaxZ - hi.MinZ : hi.MaxX - hi.MinX;
                    Vector3 edgeDir = onX ? Vector3.forward : Vector3.right;
                    foreach (float s in slides)
                    {
                        if (s != 0f && across < 6f) continue; // short edges: the middle only
                        Vector3 slide = edgeDir * (s * across);
                        Vector3 top = new Vector3(c.x, hi.ExitTop, c.z) + d * (halfAlong - 0.6f) + slide;
                        Vector3 below = new Vector3(c.x, lo.ExitTop, c.z) + d * (halfAlong + 1.0f) + slide;
                        if (!LinkEndClear(below, lo, nodes, movers) || !LinkEndClear(top, hi, nodes, movers)) continue;
                        loPoint = below;
                        hiPoint = top;
                        width = Mathf.Clamp(across - 0.8f, 0.8f, 2f);
                        return true;
                    }
                }
                return false;
            }

            float gapX = Mathf.Max(0f, Mathf.Max(lo.MinX - hi.MaxX, hi.MinX - lo.MaxX));
            float gapZ = Mathf.Max(0f, Mathf.Max(lo.MinZ - hi.MaxZ, hi.MinZ - lo.MaxZ));
            float gap = Mathf.Sqrt(gapX * gapX + gapZ * gapZ);
            float overlapX = Mathf.Min(lo.MaxX, hi.MaxX) - Mathf.Max(lo.MinX, hi.MinX);
            float overlapZ = Mathf.Min(lo.MaxZ, hi.MaxZ) - Mathf.Max(lo.MinZ, hi.MinZ);
            if (overlapX > 0.05f && overlapZ > 0.05f) return false; // partly stacked: no clean edge
            if (gap > SeekerLinkGap || (dy <= WalkStepUp && gap <= 0.15f)) return false; // too far / the NavMesh walks it

            ClosestOnAxis(lo.MinX, lo.MaxX, hi.MinX, hi.MaxX, out float lx, out float hx, out bool apartX);
            ClosestOnAxis(lo.MinZ, lo.MaxZ, hi.MinZ, hi.MaxZ, out float lz, out float hz, out bool apartZ);
            Vector3 loC = lo.Centre, hiC = hi.Centre;
            if (apartX) { lx += Mathf.Sign(loC.x - lx) * 0.4f; hx += Mathf.Sign(hiC.x - hx) * 0.6f; }
            if (apartZ) { lz += Mathf.Sign(loC.z - lz) * 0.4f; hz += Mathf.Sign(hiC.z - hz) * 0.6f; }
            loPoint = new Vector3(lx, lo.ExitTop, lz);
            hiPoint = new Vector3(hx, hi.EntryTop, hz);
            float shared = apartX && apartZ ? 0f : apartX ? overlapZ : overlapX;
            width = Mathf.Clamp(shared - 0.8f, 0.6f, 2f);
            return LinkEndClear(loPoint, lo, nodes, movers) && LinkEndClear(hiPoint, hi, nodes, movers);
        }

        /// Walkable area this platform's top keeps after the bake trims the agent radius off every side (m²).
        static float NavMeshArea(PlatformNode n) =>
            Mathf.Max(0f, n.MaxX - n.MinX - 2f * SeekerAgentRadius) * Mathf.Max(0f, n.MaxZ - n.MinZ - 2f * SeekerAgentRadius);

        static bool HasNavMeshArea(PlatformNode n) => n.Kind == ArenaPlatformKind.Floor || NavMeshArea(n) >= MinNavMeshRegionArea;

        /// The point on each interval nearest the other (the middle of the overlap if they overlap).
        static void ClosestOnAxis(float aMin, float aMax, float bMin, float bMax, out float onA, out float onB, out bool apart)
        {
            float overlapMin = Mathf.Max(aMin, bMin), overlapMax = Mathf.Min(aMax, bMax);
            apart = overlapMax <= overlapMin + 0.05f;
            if (!apart) { onA = onB = (overlapMin + overlapMax) * 0.5f; return; }
            if (aMax <= bMin + 0.05f) { onA = aMax; onB = bMin; }
            else { onA = aMin; onB = bMax; }
        }

        /// A Seeker can stand at this link end: on its own surface, not on/under another platform or a mover's path, and no
        /// wall or cover in a Seeker-sized capsule above it.
        static bool LinkEndClear(Vector3 p, PlatformNode own, List<PlatformNode> nodes, List<MovingPlatform> movers)
        {
            const float margin = 0.4f;
            if (p.x < own.MinX + 0.2f || p.x > own.MaxX - 0.2f || p.z < own.MinZ + 0.2f || p.z > own.MaxZ - 0.2f) return false;
            foreach (PlatformNode n in nodes)
            {
                if (n == own || n.Kind == ArenaPlatformKind.Floor) continue;
                bool inside = p.x > n.MinX - margin && p.x < n.MaxX + margin && p.z > n.MinZ - margin && p.z < n.MaxZ + margin;
                if (inside && n.ExitTop > p.y - 0.1f && n.Bottom < p.y + 2.2f) return false; // something there or just above
            }
            foreach (MovingPlatform m in movers)
            {
                var box = m.GetComponent<BoxCollider>();
                if (box == null) continue;
                Bounds a = box.bounds, b = new Bounds(a.center + m.Travel, a.size);
                a.Encapsulate(b);
                a.Expand(new Vector3(2f * margin, 0f, 2f * margin));
                bool insideXZ = p.x > a.min.x && p.x < a.max.x && p.z > a.min.z && p.z < a.max.z;
                if (insideXZ && p.y > a.min.y - 2.2f && p.y < a.max.y + 0.5f) return false; // a lift's floor spot, or under a low mover
            }
            int mask = LayerMask.GetMask("Default");
            return !Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.35f, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
