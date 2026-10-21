using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using TWR.Enemies;
using TWR.Level;
using TWR.Portals;
using TWR.Puzzles;
using TWR.Run;
using TWR.Scares;

namespace TWR.EditorTools
{
    /// <summary>
    /// [TWR] checks for the large section rooms (D-240 to D-247), run per scene from the R2 run-scene validation
    /// ("TWR/Run/Validate Run Setup" and after "TWR/Run/Build Run Scenes"):
    /// - size of the room shell (puzzle 48 x 24 x 12, combat 40 x 64 x 10, IG hall 120 x 40 x 14 m) and the raised exit sill;
    /// - the D-186 reachability graph incl. moving platforms (sampled every 1 m, D-242): every platform reachable and with a
    ///   way back down, headroom, ramps; the exit reachable (combat: on static platforms only);
    /// - every puzzle switch slot shootable from a reachable spot (line of sight, pistol range), and whether it shows from
    ///   the floor (switch.high must not, D-244);
    /// - moving platforms (D-241): data + blockers, translate on one axis, travel <= 12 m (lifts <= 6 m), top >= 3 x 3 m,
    ///   >= 4 m from portals, >= 2.2 m above what lies under a horizontal path, >= 0.5 m from static platforms in its height
    ///   band, >= 2 m headroom over its whole path, kept out of the NavMesh bake;
    /// - Seeker links (D-243): both ends on the NavMesh, cost >= 3; counts and lights (D-247); camera far clip >= 150 m;
    /// - scares (D-245): Relief's door point at the sill with the trigger zone and figure spot on the exit ledge; Behind you
    ///   seen from a reachable platform within its trigger distance.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const float MinMoverTop = 2.95f;          // D-241: tops >= 3 x 3 m
        const float MaxMoverTravel = 12f, MaxLiftTravel = 6f;
        const float MinMoverPortalDistance = 4f;
        const float MinMoverUnderClearance = 2.2f;
        const float MinMoverSideGap = 0.5f, MoverBandHeight = 2f;
        const float PistolRange = 100f;           // D-119
        const int MaxStaticPlatforms = 40, MaxMovers = 4; // D-247
        const float MinLargeRoomFarClip = 150f;   // D-247

        struct LargeRoomSpec
        {
            public string scene, shell;
            public Vector3 size;      // x, height, z (inner)
            public float exitSill;
            public bool staticExit;   // D-242: combat exit must not need a mover
        }

        static readonly LargeRoomSpec[] LargeRooms =
        {
            new LargeRoomSpec { scene = "Section_Puzzle", shell = "Room Shell", size = new Vector3(48f, 12f, 24f), exitSill = 6f },
            new LargeRoomSpec { scene = "Section_Combat", shell = "Room Shell", size = new Vector3(40f, 10f, 64f), exitSill = 4.5f, staticExit = true },
            new LargeRoomSpec { scene = "Section_ImpossibleGeometry", shell = "Hall Shell", size = new Vector3(120f, 14f, 40f), exitSill = 7.5f },
        };

        static bool ValidateLargeSection(StringBuilder report, Scene scene)
        {
            int index = System.Array.FindIndex(LargeRooms, r => r.scene == scene.name);
            if (index < 0) return true;
            LargeRoomSpec spec = LargeRooms[index];
            report.AppendLine("  -- large room with platforms (D-240 to D-247) --");
            TWR.Player.PlayerTuning tuning = ReachabilityTuning();
            Physics.SyncTransforms();
            bool ok = true;

            // Size (D-240).
            Transform shell = InScene<Transform>(scene).FirstOrDefault(t => t.name == spec.shell);
            Vector3 size = shell != null ? MeasureShell(shell) : Vector3.zero;
            bool sized = Mathf.Abs(size.x - spec.size.x) <= SizeTolerance && Mathf.Abs(size.y - spec.size.y) <= SizeTolerance &&
                         Mathf.Abs(size.z - spec.size.z) <= SizeTolerance;
            ok &= Check(report, $"room '{spec.shell}' is {spec.size.x:0} x {spec.size.z:0} m, {spec.size.y:0} m high (D-240)", sized,
                        shell != null ? $"measured {size.x:0.0} x {size.z:0.0} x {size.y:0.0} m" : "shell not found");

            // Reachability incl. movers (D-242).
            PlatformGraph graph = BuildPlatformGraph(scene, tuning);
            ok &= CheckPlatformGraph(report, graph, tuning, scene.name, "D-240, D-242");
            PlatformGraph statics = BuildPlatformGraph(scene, tuning, false);

            // Raised exit (D-240, D-242).
            Transform exitDoor = ExitDoorOf(scene);
            if (exitDoor == null) ok &= Check(report, "raised exit door (SectionExitGate)", false, "none found");
            else
            {
                Vector3 inside = exitDoor.position + exitDoor.forward * 1f;
                bool atSill = Mathf.Abs(exitDoor.position.y - spec.exitSill) <= 0.1f;
                bool reachable = graph.IsReachable(inside, out string onWhat);
                bool staticOk = !spec.staticExit || statics.IsReachable(inside, out _);
                ok &= Check(report, $"exit sill at {spec.exitSill:0.0} m, reachable{(spec.staticExit ? " on static platforms only" : "")} (D-240, D-242)",
                            atSill && reachable && staticOk,
                            $"sill {exitDoor.position.y:0.00} m, stands on '{onWhat}', reachable {(reachable ? "yes" : "NO")}" +
                            (spec.staticExit ? $", without movers {(staticOk ? "yes" : "NO")}" : ""));
            }

            List<Collider> blockers = SolidColliders(scene);
            ok &= CheckSwitchSightlines(report, scene, graph, blockers);
            ok &= CheckMovers(report, scene, statics, spec);
            ok &= CheckLargeRoomBudget(report, scene, graph);
            ok &= CheckLargeRoomScares(report, scene, statics, exitDoor, spec, blockers);
            return ok;
        }

        /// Inner size (x, height, z) between the shell's wall, floor and ceiling pieces.
        static Vector3 MeasureShell(Transform shell)
        {
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
            return new Vector3(wallE - wallW, ceiling - floorTop, wallN - wallS);
        }

        /// The exit door (the DoorLock with a SectionExitGate); its +Z faces into the room.
        static Transform ExitDoorOf(Scene scene)
        {
            SectionExitGate gate = InScene<SectionExitGate>(scene).FirstOrDefault();
            return gate != null ? gate.transform : null;
        }

        /// Eye points (1.65 m up) of reachable standing spots: a 4 m grid on floors, centre + corners of every static
        /// platform, the centre of every mover sample. onlyFloor = just the floor grid.
        static List<Vector3> ReachableEyes(PlatformGraph graph, bool onlyFloor)
        {
            var eyes = new List<Vector3>();
            foreach (PlatformNode n in graph.Nodes.Where(n => n.Reachable))
            {
                if (n.Kind == ArenaPlatformKind.Floor)
                {
                    for (float x = n.MinX + 2f; x < n.MaxX - 1f; x += 4f)
                        for (float z = n.MinZ + 2f; z < n.MaxZ - 1f; z += 4f)
                            eyes.Add(new Vector3(x, n.ExitTop + 1.65f, z));
                    continue;
                }
                if (onlyFloor || n.Kind == ArenaPlatformKind.Ramp) continue;
                foreach (Vector3 o in n.Offsets)
                {
                    PlatformNode at = n.At(o);
                    eyes.Add(at.Centre + Vector3.up * 1.65f);
                    if (n.Moving) continue;
                    float ix = Mathf.Min(0.5f, (at.MaxX - at.MinX) * 0.25f), iz = Mathf.Min(0.5f, (at.MaxZ - at.MinZ) * 0.25f);
                    foreach (float x in new[] { at.MinX + ix, at.MaxX - ix })
                        foreach (float z in new[] { at.MinZ + iz, at.MaxZ - iz })
                            eyes.Add(new Vector3(x, at.ExitTop + 1.65f, z));
                }
            }
            return eyes;
        }

        // ---------- switches (D-242, D-244) ----------

        static bool CheckSwitchSightlines(StringBuilder report, Scene scene, PlatformGraph graph, List<Collider> blockers)
        {
            List<ShootableSwitch> switches = InScene<ShootableSwitch>(scene);
            if (switches.Count == 0) return true;
            bool ok = true;
            List<Vector3> eyes = ReachableEyes(graph, false), floorEyes = ReachableEyes(graph, true);
            foreach (ShootableSwitch sw in switches)
            {
                Vector3 target = sw.transform.position + sw.transform.forward * 0.12f; // just in front of the button face
                bool Seen(List<Vector3> from) => from.Where(e => Vector3.Distance(e, target) <= PistolRange)
                                                     .OrderBy(e => (e - target).sqrMagnitude)
                                                     .Any(e => Vector3.Dot(e - target, sw.transform.forward) > 0f && !Blocked(e, target, blockers));
                bool shootable = Seen(eyes);
                bool fromFloor = Seen(floorEyes);
                bool hiddenOk = sw.SwitchId != "switch.high" || !fromFloor; // D-244: B only from the platforms
                ok &= Check(report, $"  slot '{sw.SwitchId}': shootable from a reachable spot{(sw.SwitchId == "switch.high" ? ", hidden from the floor (D-244)" : "")} (D-242)",
                            shootable && hiddenOk, $"from platforms {(shootable ? "yes" : "NO")}, from the floor {(fromFloor ? "yes" : "no")}");
            }
            return ok;
        }

        // ---------- moving platforms (D-241) ----------

        static bool CheckMovers(StringBuilder report, Scene scene, PlatformGraph statics, LargeRoomSpec spec)
        {
            List<MovingPlatform> movers = InScene<MovingPlatform>(scene);
            List<Portal> portals = InScene<Portal>(scene);
            bool ok = Check(report, $"moving platforms: <= {MaxMovers} (D-247)", movers.Count <= MaxMovers, movers.Count.ToString());
            float ceiling = spec.size.y;
            foreach (MovingPlatform m in movers)
            {
                var problems = new List<string>();
                var box = m.GetComponent<BoxCollider>();
                Vector3 t = m.Travel;
                bool lift = m.IsLift;
                int axes = (Mathf.Abs(t.x) > 0.01f ? 1 : 0) + (Mathf.Abs(t.y) > 0.01f ? 1 : 0) + (Mathf.Abs(t.z) > 0.01f ? 1 : 0);
                if (m.Data == null) problems.Add("no MovingPlatformData");
                else if (m.Data.blockers.value == 0) problems.Add("data has no blockers (it would push people)");
                if (axes != 1) problems.Add("must move along one axis (horizontal or vertical)");
                if (t.magnitude > (lift ? MaxLiftTravel : MaxMoverTravel) + 0.01f) problems.Add($"travel {t.magnitude:0.0} m > {(lift ? MaxLiftTravel : MaxMoverTravel):0} m");
                if (box == null) problems.Add("no BoxCollider");
                if (m.GetComponent<ArenaPlatform>() == null) problems.Add("no ArenaPlatform marker (not in the reachability check)");
                var modifier = m.GetComponent<NavMeshModifier>();
                if (modifier == null || !modifier.ignoreFromBuild) problems.Add("not kept out of the NavMesh bake (D-243)");
                if (box != null)
                {
                    Bounds a = box.bounds;
                    if (a.size.x < MinMoverTop || a.size.z < MinMoverTop) problems.Add($"top {a.size.x:0.0} x {a.size.z:0.0} m < 3 x 3");
                    Bounds sweep = a;
                    sweep.Encapsulate(new Bounds(a.center + t, a.size));
                    foreach (Portal p in portals)
                    {
                        Vector3 closest = sweep.ClosestPoint(p.transform.position);
                        if (Vector3.Distance(closest, p.transform.position) < MinMoverPortalDistance) problems.Add($"within 4 m of portal '{p.PortalId}'");
                    }
                    if (ceiling - sweep.max.y < MinHeadroom) problems.Add($"only {ceiling - sweep.max.y:0.0} m to the ceiling");
                    foreach (PlatformNode n in statics.Nodes)
                    {
                        float ox = Mathf.Min(sweep.max.x, n.MaxX) - Mathf.Max(sweep.min.x, n.MinX);
                        float oz = Mathf.Min(sweep.max.z, n.MaxZ) - Mathf.Max(sweep.min.z, n.MinZ);
                        bool overlaps = ox > 0.05f && oz > 0.05f;
                        if (overlaps && n.ExitTop <= sweep.min.y + 0.01f)
                        {
                            float under = a.min.y - n.ExitTop; // the lowest the slab gets over this piece
                            if (!lift && under < MinMoverUnderClearance) problems.Add($"only {under:0.0} m above '{n.Id}' (D-241 wants 2.2 m)");
                        }
                        else if (overlaps && n.Bottom >= sweep.max.y - 0.01f)
                        {
                            if (n.Bottom - sweep.max.y < MinHeadroom) problems.Add($"only {n.Bottom - sweep.max.y:0.0} m headroom under '{n.Id}'");
                        }
                        else if (n.Kind != ArenaPlatformKind.Floor && n.ExitTop > sweep.min.y - MoverBandHeight && n.ExitTop < sweep.max.y + MoverBandHeight)
                        {
                            float gx = Mathf.Max(0f, Mathf.Max(sweep.min.x - n.MaxX, n.MinX - sweep.max.x));
                            float gz = Mathf.Max(0f, Mathf.Max(sweep.min.z - n.MaxZ, n.MinZ - sweep.max.z));
                            float gap = Mathf.Sqrt(gx * gx + gz * gz);
                            if (gap < MinMoverSideGap - 0.01f) problems.Add($"{gap:0.00} m from '{n.Id}' in its height band (D-241 wants 0.5 m)");
                        }
                    }
                }
                ok &= Check(report, $"  mover '{m.name}' ({(lift ? "lift" : "horizontal")}, {t.magnitude:0.0} m, {(m.Data != null ? m.Data.speed : 0f):0.0} m/s)",
                            problems.Count == 0, string.Join("; ", problems.Distinct()));
            }
            return ok;
        }

        // ---------- budget, links, lights, far clip (D-243, D-247) ----------

        static bool CheckLargeRoomBudget(StringBuilder report, Scene scene, PlatformGraph graph)
        {
            int statics = graph.Nodes.Count(n => n.Kind != ArenaPlatformKind.Floor && !n.Moving);
            bool ok = Check(report, $"static platforms <= {MaxStaticPlatforms} (D-247)", statics <= MaxStaticPlatforms, statics.ToString());

            List<NavMeshLink> links = InScene<NavMeshLink>(scene);
            int badEnds = 0, cheap = 0;
            var offMesh = new List<string>();
            foreach (NavMeshLink link in links)
            {
                Vector3 start = link.transform.TransformPoint(link.startPoint), end = link.transform.TransformPoint(link.endPoint);
                bool startOk = NavMesh.SamplePosition(start, out _, 0.6f, NavMesh.AllAreas);
                bool endOk = NavMesh.SamplePosition(end, out _, 0.6f, NavMesh.AllAreas);
                if (!startOk || !endOk)
                {
                    badEnds++;
                    var where = new List<string>();
                    if (!startOk) where.Add("start " + SurfaceUnder(graph, start));
                    if (!endOk) where.Add("end " + SurfaceUnder(graph, end));
                    string detail = string.Join(", ", where);
                    offMesh.Add($"'{link.name}': {detail}");
                    Debug.LogWarning($"[TWR] {scene.name}: Seeker link '{link.name}' has no NavMesh under its {detail} (D-243).", link);
                }
                if (link.costModifier < SeekerLinkCost - 0.01f) cheap++;
            }
            ok &= Check(report, $"Seeker links (D-243): <= {MaxSeekerLinks}, both ends on the NavMesh, cost >= {SeekerLinkCost:0}",
                        links.Count <= MaxSeekerLinks && badEnds == 0 && cheap == 0,
                        $"{links.Count} links, {badEnds} with an end off the mesh, {cheap} too cheap");
            foreach (string line in offMesh) report.AppendLine("    off the NavMesh: " + line);
            if (InScene<SeekerSpawnPoint>(scene).Count > 0 && links.Count == 0)
                report.AppendLine("  WARNING: Seekers here but no jump links: they can only use stairs and ramps (D-243).");

            int shadowLights = InScene<Light>(scene).Count(l => l.shadows != LightShadows.None);
            ok &= Check(report, "no shadow-casting realtime lights (emissive lighting only, D-247)", shadowLights == 0, shadowLights.ToString());

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Camera cam = prefab != null ? prefab.GetComponentInChildren<Camera>(true) : null;
            float farClip = cam != null ? cam.farClipPlane : 0f;
            foreach (ArenaCameraFarClip raise in InScene<ArenaCameraFarClip>(scene)) farClip = Mathf.Max(farClip, raise.FarClip);
            ok &= Check(report, $"player camera far clip >= {MinLargeRoomFarClip:0} m (D-247)", farClip >= MinLargeRoomFarClip,
                        cam != null ? $"{farClip:0} m" : "no camera on " + PrefabPath);
            return ok;
        }

        /// The platform a link end stands on, its size and how much NavMesh it can have after the bake's agent-radius trim
        /// (patches under minRegionArea, 2 m², are dropped), so a "no NavMesh" message says which surface to widen.
        static string SurfaceUnder(PlatformGraph graph, Vector3 p)
        {
            PlatformNode on = graph.Nodes
                .Where(n => !n.Moving && p.x >= n.MinX - 0.3f && p.x <= n.MaxX + 0.3f && p.z >= n.MinZ - 0.3f && p.z <= n.MaxZ + 0.3f &&
                            Mathf.Abs(p.y - n.ExitTop) <= 0.5f)
                .OrderByDescending(n => n.ExitTop).FirstOrDefault();
            if (on == null) return $"at {p} (on no marked platform: inside a wall or cover?)";
            float w = on.MaxX - on.MinX, d = on.MaxZ - on.MinZ, area = NavMeshArea(on);
            string why = on.Kind == ArenaPlatformKind.Floor ? "the end is probably in the trimmed strip along a wall or block"
                       : area < MinNavMeshRegionArea ? $"only {area:0.00} m² after the {SeekerAgentRadius} m trim, the bake drops < {MinNavMeshRegionArea:0} m²: widen it"
                       : $"{area:0.00} m² of NavMesh, the end is probably in the trimmed {SeekerAgentRadius} m edge strip";
            return $"on '{on.Id}' ({w:0.0} x {d:0.0} m, top {on.ExitTop:0.00} m): {why}";
        }

        // ---------- scares (D-245) ----------

        static bool CheckLargeRoomScares(StringBuilder report, Scene scene, PlatformGraph statics, Transform exitDoor, LargeRoomSpec spec,
                                         List<Collider> blockers)
        {
            bool ok = true;
            foreach (ScareAnchor anchor in InScene<ScareAnchor>(scene))
            {
                ScareData data = anchor.Data;
                if (data == null) continue;
                if (data.trigger == ScareTrigger.FacingExitAfterSolve)
                {
                    var problems = new List<string>();
                    if (anchor.Target == null) problems.Add("no Target");
                    else
                    {
                        Vector3 door = anchor.Target.position;
                        if (Mathf.Abs(door.y - spec.exitSill) > 0.1f) problems.Add($"Target at {door.y:0.0} m, the sill is at {spec.exitSill:0.0} m");
                        if (exitDoor != null && Vector3.Distance(Flat(door), Flat(exitDoor.position)) > 0.3f) problems.Add("Target is not the exit door");
                        Vector3 inward = exitDoor != null ? exitDoor.forward : Vector3.left;
                        foreach (float d in new[] { 1f, data.figureDistance, data.triggerDistance })
                        {
                            Vector3 p = door + inward * d;
                            PlatformNode on = statics.Nodes.FirstOrDefault(n => !n.Moving && p.x >= n.MinX && p.x <= n.MaxX && p.z >= n.MinZ && p.z <= n.MaxZ &&
                                                                                Mathf.Abs(n.ExitTop - spec.exitSill) <= 0.1f);
                            if (on == null) problems.Add($"{d:0.0} m in front of the door is not on the exit ledge");
                        }
                        if (data.triggerHeightTolerance > 1f) problems.Add($"height tolerance {data.triggerHeightTolerance:0.0} m (> 1 m: could fire from below)");
                    }
                    ok &= Check(report, "Relief: door point at the sill, trigger zone + figure spot on the exit ledge (D-245)", problems.Count == 0, string.Join("; ", problems));
                }
                else if (data.trigger == ScareTrigger.SeenAfterUnobserved)
                {
                    Vector3 spot = anchor.transform.position, chest = spot + Vector3.up * 1.2f;
                    bool fromClimb = statics.Nodes.Where(n => n.Reachable && n.Kind != ArenaPlatformKind.Floor)
                                                   .Select(n => n.Centre + Vector3.up * 1.65f)
                                                   .Any(e => ScareDirector.FlatDistance(e, spot) <= data.triggerDistance && !Blocked(e, chest, blockers));
                    ok &= Check(report, "Behind you: the spot is seen from the first climb within its trigger distance (D-245)", fromClimb,
                                $"spot {spot}, trigger distance {data.triggerDistance:0} m");
                }
            }
            return ok;
        }
    }
}
