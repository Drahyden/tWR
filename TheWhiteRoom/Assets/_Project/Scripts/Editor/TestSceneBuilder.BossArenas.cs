using System.Collections.Generic;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Localization;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TWR.Enemies;
using TWR.Items;
using TWR.Run;
using TWR.UI;

namespace TWR.EditorTools
{
    /// <summary>
    /// Run Structure step R4 (D-180 to D-194): builds the two real boss arenas from code, called by
    /// "TWR/Run/Build Run Scenes". Geometry is authored in code (fixed numbers and formulas, no randomness), greybox and
    /// cheap (D-100, D-193): emissive white walls, no shadow casting, static batching, a NavMesh baked per arena (D-161).
    /// - Boss_Escape (D-180): 72 m wide x 200 m long x 12 m high. Entrance at the south end, exit door at the far north
    ///   end (BossDefeated, D-165). 12 full-height columns along the length: 6 unbreakable, 6 breakable with a cracked
    ///   look, rubble and a carving NavMeshObstacle (D-190). The Bull (D-048, D-181, D-182).
    /// - Boss_Discover (D-183): 48 m wide x 40 m long x 160 m tall. Floor-level entrance and exit on opposite sides. A
    ///   path of platforms of varying sizes winds up around the walls for 3 laps (about 60 m, dense = within fight reach),
    ///   starting with stairs from the floor so the player can always climb back; above it only wall light bands for
    ///   atmosphere. Every rise and gap follows D-186 (rises 0.55-0.85 m, gaps about 1-1.4 m). 25 landings with dark
    ///   cavities (D-065) + 4 cavities on the floor, cover on landings and on the floor. The Preacher (D-047, D-184).
    /// Both: 4 ammo pickups (+ respawn while the boss lives, D-188) and 1 health pickup (D-194) with rolled spots (D-177),
    /// the camera far-clip raiser (D-193), and the BossData assets (D-185, D-192). Also the HUD boss bar for Core.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string BullDataPath = Root + "/Data/Enemies/Boss_Bull.asset";
        const string PreacherDataPath = Root + "/Data/Enemies/Boss_Preacher.asset";
        const string BullSoundsPath = Root + "/Data/Enemies/BossSounds_Bull.asset";
        const string PreacherSoundsPath = Root + "/Data/Enemies/BossSounds_Preacher.asset";

        // D-180 (Bull) and D-183 (Preacher) arena sizes; D-192: Bull arena height 12 m.
        const float BullArenaWidth = 72f, BullArenaLength = 200f, BullArenaHeight = 12f;
        const float PreacherArenaWidth = 48f, PreacherArenaLength = 40f, PreacherArenaHeight = 160f;
        const float ArenaFarClip = 300f; // D-193: at least 250 m

        // Preacher platform path (a rectangle 5 m inside the walls), laps and step pattern (D-183, D-186).
        const float PathMinX = -19f, PathMaxX = 19f, PathMinZ = 9f, PathMaxZ = 39f;
        const float CornerLandingSize = 7f, MidLandingSize = 6f, PlatformThickness = 0.5f;
        const float FirstLandingTop = 1.5f;
        const int PreacherLaps = 3;
        static readonly float[] StepLengths = { 2.4f, 3.0f, 2.0f, 2.6f };
        static readonly float[] StepWidths = { 2.6f, 3.4f, 3.0f, 2.2f };
        static readonly float[] StepRises = { 0.8f, 0.6f, 0.85f, 0.7f, 0.75f, 0.55f };
        static readonly string[] CornerNames = { "sw", "nw", "ne", "se" };
        static readonly string[] SideNames = { "w", "n", "e", "s" };

        // ---------- assets (D-185, D-192) ----------

        /// BullData / PreacherData + their sound sets. Created once; empty references are repaired on every build.
        static bool PrepareBossAssets(ref RunSceneAssets a)
        {
            EnsureFolder(Root + "/Data/Enemies");
            a.bullSounds = LoadOrCreate<BossSoundSet>(BullSoundsPath, null);
            a.preacherSounds = LoadOrCreate<BossSoundSet>(PreacherSoundsPath, null);
            a.bull = LoadOrCreate<BullData>(BullDataPath, d => { d.bossId = "boss.bull"; d.maxHealth = 1000f; });
            a.preacher = LoadOrCreate<PreacherData>(PreacherDataPath, d => { d.bossId = "boss.preacher"; d.maxHealth = 750f; });
            if (a.bull == null || a.preacher == null || a.bullSounds == null || a.preacherSounds == null) return false;
            RepairBossData(a.bull, "boss.bull.name", a.bullSounds);
            RepairBossData(a.preacher, "boss.preacher.name", a.preacherSounds);
            return true;
        }

        static void RepairBossData(BossData data, string nameKey, BossSoundSet sounds)
        {
            bool dirty = false;
            if (data.displayName == null || data.displayName.IsEmpty)
            {
                data.displayName = new LocalizedString("UI", nameKey); // D-084, D-193
                dirty = true;
            }
            if (data.sounds == null)
            {
                data.sounds = sounds;
                dirty = true;
            }
            int mask = data.environmentMask & ~ActorLayersMask();
            if (mask == 0) mask = ObstacleLayersMask(); // Default + Interactable: walls, columns, platforms, cover, doors
            if (mask != data.environmentMask)
            {
                data.environmentMask = mask;
                dirty = true;
            }
            if (dirty) EditorUtility.SetDirty(data);
        }

        // ---------- Boss 2: the Bull (Escape, D-180 to D-182, D-190) ----------

        static bool BuildBossEscapeScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, false)) return false;
            Transform root = new GameObject("Boss Arena: Escape / The Bull (D-148, D-180)").transform;

            // x -36..36, z 4..204 (entrance vestibule z 0..4), 12 m high; exit door at the far end, x = 0 (D-180, D-165).
            BuildSectionShell(root, a, BullArenaWidth, BullArenaLength, BullArenaHeight, a.whiteEmissive, new[] { 0f }, true,
                              SectionExitCondition.BossDefeated);

            Transform columns = new GameObject("Columns (6 unbreakable + 6 breakable, D-190)").transform;
            columns.SetParent(root, false);
            var layout = new (Vector2 xz, bool breakable)[]
            {
                (new Vector2(-16f, 36f), false), (new Vector2(16f, 36f), true),
                (new Vector2(0f, 58f), true),
                (new Vector2(-24f, 78f), false), (new Vector2(24f, 78f), true),
                (new Vector2(-9f, 100f), true), (new Vector2(9f, 100f), false),
                (new Vector2(0f, 124f), false),
                (new Vector2(-22f, 146f), true), (new Vector2(22f, 146f), false),
                (new Vector2(-10f, 170f), false), (new Vector2(12f, 186f), true),
            };
            for (int i = 0; i < layout.Length; i++)
                BuildBullColumn(columns, a, $"bull.column.{i + 1:00}", layout[i].xz, layout[i].breakable);

            float arenaMidZ = VestibuleDepth + BullArenaLength * 0.5f;
            var fightArea = new Bounds(new Vector3(0f, BullArenaHeight * 0.5f, arenaMidZ + 0.5f),
                                       new Vector3(BullArenaWidth, BullArenaHeight + 2f, BullArenaLength - 1f));
            BossEncounter boss = BuildBull(root, a, new Vector3(0f, 0f, 46f), fightArea);

            // 4 ammo (D-188) on 8 spots and 1 health (D-194) on 4 spots, positions rolled (D-177).
            BuildBossPickups(root, a, boss, "boss.bull", new[]
            {
                ("boss.bull.ammo.w1", new Vector3(-32f, 0f, 20f), true),
                ("boss.bull.ammo.e1", new Vector3(32f, 0f, 20f), true),
                ("boss.bull.ammo.w2", new Vector3(-32f, 0f, 90f), true),
                ("boss.bull.ammo.e2", new Vector3(32f, 0f, 110f), true),
                ("boss.bull.ammo.w3", new Vector3(-32f, 0f, 150f), false),
                ("boss.bull.ammo.e3", new Vector3(32f, 0f, 160f), false),
                ("boss.bull.ammo.mid1", new Vector3(0f, 0f, 82f), false),
                ("boss.bull.ammo.mid2", new Vector3(-4f, 0f, 140f), false),
            }, new[]
            {
                ("boss.bull.health.sw", new Vector3(-30f, 0f, 12f), true),
                ("boss.bull.health.e", new Vector3(30f, 0f, 60f), false),
                ("boss.bull.health.mid", new Vector3(-14f, 0f, 112f), false),
                ("boss.bull.health.ne", new Vector3(30f, 0f, 180f), false),
            });

            AddFarClip(root);
            DisableShadows(root);
            return true;
        }

        /// A 3 x 3 m column from floor to ceiling. Unbreakable = plain solid block. Breakable (D-190) = cracked look,
        /// rubble (no colliders) shown when it breaks, left out of the NavMesh bake (NavMeshModifier) and carving with a
        /// NavMeshObstacle instead, so the gap opens up when it breaks.
        static void BuildBullColumn(Transform parent, RunSceneAssets a, string id, Vector2 xz, bool breakable)
        {
            const float size = 3f;
            var root = new GameObject($"Column {id} ({(breakable ? "breakable, cracked" : "unbreakable")})");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(xz.x, 0f, xz.y);
            var column = root.AddComponent<BullColumn>();

            GameObject solid = LocalBlock("Solid", root.transform, new Vector3(0f, BullArenaHeight * 0.5f, 0f),
                                          new Vector3(size, BullArenaHeight, size), breakable ? a.cracked : a.offWhite, !breakable, true);
            var so = new SerializedObject(column);
            so.FindProperty("columnId").stringValue = id;
            so.FindProperty("breakable").boolValue = breakable;
            so.FindProperty("solid").objectReferenceValue = solid;
            if (breakable)
            {
                // Crack marks (thin black strips on the faces, D-190 "look cracked"); more appear after the first hit.
                Transform cracks = new GameObject("Cracks").transform;
                cracks.SetParent(solid.transform, false);
                CrackStrip(cracks, a, new Vector3(0f, -0.1f, 0.501f), new Vector3(0.05f, 0.35f, 0.01f), 18f);
                CrackStrip(cracks, a, new Vector3(0.501f, 0.05f, 0f), new Vector3(0.01f, 0.3f, 0.05f), -25f);
                CrackStrip(cracks, a, new Vector3(-0.501f, -0.2f, 0.1f), new Vector3(0.01f, 0.25f, 0.05f), 30f);
                CrackStrip(cracks, a, new Vector3(0.1f, 0.15f, -0.501f), new Vector3(0.05f, 0.3f, 0.01f), -15f);
                GameObject extra = new GameObject("Extra Cracks (after 1st hit)");
                extra.transform.SetParent(solid.transform, false);
                CrackStrip(extra.transform, a, new Vector3(-0.2f, -0.25f, 0.502f), new Vector3(0.06f, 0.3f, 0.01f), -35f);
                CrackStrip(extra.transform, a, new Vector3(0.502f, -0.3f, 0.2f), new Vector3(0.01f, 0.3f, 0.06f), 40f);
                CrackStrip(extra.transform, a, new Vector3(0.25f, -0.15f, -0.502f), new Vector3(0.06f, 0.25f, 0.01f), 25f);
                CrackStrip(extra.transform, a, new Vector3(-0.502f, 0.1f, -0.2f), new Vector3(0.01f, 0.35f, 0.06f), -20f);

                GameObject rubble = new GameObject("Rubble (no colliders, never stuns)");
                rubble.transform.SetParent(root.transform, false);
                LocalBlock("Rubble A", rubble.transform, new Vector3(-0.6f, 0.4f, 0.3f), new Vector3(1.6f, 0.8f, 1.2f), a.cracked, false, false)
                    .transform.localRotation = Quaternion.Euler(0f, 20f, 8f);
                LocalBlock("Rubble B", rubble.transform, new Vector3(0.8f, 0.3f, -0.5f), new Vector3(1.2f, 0.6f, 1.4f), a.cracked, false, false)
                    .transform.localRotation = Quaternion.Euler(5f, -30f, 0f);
                LocalBlock("Rubble C", rubble.transform, new Vector3(0.1f, 0.25f, 1.1f), new Vector3(1.0f, 0.5f, 0.9f), a.cracked, false, false)
                    .transform.localRotation = Quaternion.Euler(0f, 50f, -10f);
                LocalBlock("Stump", rubble.transform, new Vector3(0f, 0.6f, 0f), new Vector3(2.2f, 1.2f, 2.2f), a.cracked, false, false);

                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = new Vector3(0f, BullArenaHeight * 0.5f, 0f);
                obstacle.size = new Vector3(size, BullArenaHeight, size);
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                var modifier = root.AddComponent<NavMeshModifier>();
                modifier.ignoreFromBuild = true; // carved at runtime instead, so breaking it opens the NavMesh (D-190)

                so.FindProperty("rubble").objectReferenceValue = rubble;
                so.FindProperty("extraCracks").objectReferenceValue = extra;
                so.FindProperty("obstacle").objectReferenceValue = obstacle;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CrackStrip(Transform parent, RunSceneAssets a, Vector3 localPosition, Vector3 localScale, float roll)
        {
            GameObject strip = LocalBlock("Crack", parent, localPosition, localScale, a.dark, false, false);
            strip.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
        }

        /// The Bull: big fleshy quadruped placeholder (D-048, D-069), NavMeshAgent for walking, kinematic body for the
        /// charge, Enemy layer (shootable, D-134), BossMarker (not scored, D-158).
        static BossEncounter BuildBull(Transform parent, RunSceneAssets a, Vector3 position, Bounds fightArea)
        {
            var go = new GameObject("Boss - The Bull (D-048, D-069, D-181)");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.back));

            var box = go.AddComponent<BoxCollider>(); // what bullets hit
            box.size = new Vector3(2.4f, 2.8f, 4.8f);
            box.center = new Vector3(0f, 1.5f, 0.3f);
            var body = go.AddComponent<Rigidbody>(); // kinematic: moved by the agent or the charge, never by physics
            body.isKinematic = true;
            body.useGravity = false;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 1.2f;
            agent.height = 2.8f;
            agent.baseOffset = 0f;

            Transform model = new GameObject("Model").transform;
            model.SetParent(go.transform, false);
            var skin = new List<Renderer>();
            skin.Add(BossPart("Body", PrimitiveType.Capsule, model, new Vector3(0f, 1.6f, 0f), new Vector3(2.2f, 2.3f, 2.2f), new Vector3(90f, 0f, 0f), a.skin));
            skin.Add(BossPart("Hump", PrimitiveType.Sphere, model, new Vector3(0f, 2.4f, 0.9f), new Vector3(1.9f, 1.3f, 1.8f), Vector3.zero, a.skin));
            BossPart("Spine Ridge", PrimitiveType.Cube, model, new Vector3(0f, 2.65f, -0.3f), new Vector3(0.18f, 0.25f, 3.2f), Vector3.zero, a.horn);

            Transform head = new GameObject("Head").transform;
            head.SetParent(model, false);
            head.localPosition = new Vector3(0f, 2.0f, 2.3f);
            skin.Add(BossPart("Skull", PrimitiveType.Sphere, head, new Vector3(0f, -0.1f, 0.5f), new Vector3(1.3f, 1.2f, 1.5f), Vector3.zero, a.skin));
            BossPart("Horn L", PrimitiveType.Cylinder, head, new Vector3(-0.75f, 0.5f, 0.5f), new Vector3(0.16f, 0.55f, 0.16f), new Vector3(20f, 0f, 50f), a.horn);
            BossPart("Horn R", PrimitiveType.Cylinder, head, new Vector3(0.75f, 0.5f, 0.5f), new Vector3(0.16f, 0.55f, 0.16f), new Vector3(20f, 0f, -50f), a.horn);
            BossPart("Eye L", PrimitiveType.Sphere, head, new Vector3(-0.35f, 0.05f, 1.15f), Vector3.one * 0.22f, Vector3.zero, a.eye);
            BossPart("Eye R", PrimitiveType.Sphere, head, new Vector3(0.35f, 0.05f, 1.15f), Vector3.one * 0.22f, Vector3.zero, a.eye);

            Transform[] legs = new Transform[4];
            string[] legNames = { "Leg FL", "Leg FR", "Leg BL", "Leg BR" };
            Vector3[] hips = { new Vector3(-0.75f, 1.1f, 1.4f), new Vector3(0.75f, 1.1f, 1.4f), new Vector3(-0.75f, 1.1f, -1.4f), new Vector3(0.75f, 1.1f, -1.4f) };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = new GameObject(legNames[i]).transform;
                legs[i].SetParent(model, false);
                legs[i].localPosition = hips[i];
                skin.Add(BossPart("Leg", PrimitiveType.Cylinder, legs[i], new Vector3(0f, -0.55f, 0f), new Vector3(0.45f, 0.55f, 0.45f), Vector3.zero, a.skin));
            }
            Transform eye = new GameObject("Eye Point").transform;
            eye.SetParent(go.transform, false);
            eye.localPosition = new Vector3(0f, 2.2f, 3.4f);

            SetBossLayer(go);
            go.AddComponent<BossMarker>();
            var health = go.AddComponent<BossHealth>();
            Assign(health, "data", a.bull);
            BossAudio sounds = AddBossAudio(go, a.bullSounds);
            var visuals = go.AddComponent<BullVisuals>();
            Assign(visuals, "model", model);
            Assign(visuals, "head", head);
            Assign(visuals, "legFrontLeft", legs[0]);
            Assign(visuals, "legFrontRight", legs[1]);
            Assign(visuals, "legBackLeft", legs[2]);
            Assign(visuals, "legBackRight", legs[3]);
            AssignArray(visuals, "tintRenderers", skin.ToArray());

            var death = go.AddComponent<BossDeath>();
            BossEncounter encounter = AddEncounter(go, a.bull, health, death, sounds, fightArea);
            var brain = go.AddComponent<BullBrain>();
            Assign(brain, "data", a.bull);
            Assign(brain, "encounter", encounter);
            Assign(brain, "health", health);
            Assign(brain, "visuals", visuals);
            Assign(brain, "sounds", sounds);
            Assign(brain, "eye", eye);
            Assign(death, "data", a.bull);
            AssignArray(death, "disableOnDeath", new Object[] { brain, visuals });
            return encounter;
        }

        // ---------- Boss 1: the Preacher (Discover, D-183, D-184, D-187) ----------

        /// One walkable piece of the Preacher arena (D-183), produced by PreacherLayout.
        struct PlatformSpec
        {
            public string Id;
            public Vector3 Top;       // centre of the top face
            public Vector2 Size;      // footprint x, z
            public float Thickness;   // steps go down to the floor
            public ArenaPlatformKind Kind;
            public bool Landing;
            public bool Corner;
            public Vector3 Outward;   // towards the wall(s): the cavity side
            public int Lap;
        }

        static bool BuildBossDiscoverScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, false)) return false;
            Transform root = new GameObject("Boss Arena: Discover / The Preacher (D-148, D-183)").transform;

            // x -24..24, z 4..44, 160 m tall. Entrance (south) and exit (north) both at floor level (D-183).
            BuildSectionShell(root, a, PreacherArenaWidth, PreacherArenaLength, PreacherArenaHeight, a.whiteEmissive, new[] { 0f }, true,
                              SectionExitCondition.BossDefeated);
            Transform floor = root.Find("Room Shell/Floor");
            if (floor != null) AddPlatformMarker(floor.gameObject, "preacher.floor", ArenaPlatformKind.Floor);
            else Debug.LogError("[TWR] Preacher arena: 'Room Shell/Floor' not found; reachability can't start from the floor.");

            // Platforms (D-183, D-186): stairs + 3 laps of steps and landings around the walls.
            List<PlatformSpec> specs = PreacherLayout();
            Transform platforms = new GameObject("Platforms (3 laps, D-183, D-186)").transform;
            platforms.SetParent(root, false);
            Transform cavitiesRoot = new GameObject("Dark Cavities (teleport points, D-065, D-187)").transform;
            cavitiesRoot.SetParent(root, false);
            var cavities = new List<PreacherCavity>();
            var landingTops = new Dictionary<string, Vector3>();
            foreach (PlatformSpec s in specs)
            {
                Vector3 centre = s.Top - new Vector3(0f, s.Thickness * 0.5f, 0f);
                GameObject block = Block(s.Id, centre, new Vector3(s.Size.x, s.Thickness, s.Size.y),
                                         s.Landing ? a.offWhite : a.white, platforms);
                AddPlatformMarker(block, s.Id, s.Kind);
                if (!s.Landing) continue;
                landingTops[s.Id] = s.Top;

                // A dark cavity on every landing, against the wall side (D-065, D-183).
                float push = s.Corner ? (CornerLandingSize * 0.5f - 1.2f) : (MidLandingSize * 0.5f - 0.7f);
                Vector3 offset = s.Corner ? new Vector3(Mathf.Sign(s.Outward.x) * push, 0f, Mathf.Sign(s.Outward.z) * push) : s.Outward * push;
                cavities.Add(BuildCavity(cavitiesRoot, a, s.Id.Replace("preacher.", "preacher.cavity."), s.Top + offset, -s.Outward.normalized));
                // Cover on the inner edge of the side landings (blocks volleys, D-184).
                if (!s.Corner)
                {
                    Vector3 along = new Vector3(Mathf.Abs(s.Outward.z), 0f, Mathf.Abs(s.Outward.x));
                    Vector3 size = along * 3.5f + new Vector3(Mathf.Abs(s.Outward.x), 0f, Mathf.Abs(s.Outward.z)) * 0.5f + Vector3.up * 1.3f;
                    Block(s.Id + " cover", s.Top - s.Outward * (MidLandingSize * 0.5f - 0.3f) + Vector3.up * 0.65f, size, a.offWhite, platforms);
                }
            }

            // Four cavities on the floor, facing the room centre (fights that never need climbing, D-187).
            Vector3 centreOfRoom = new Vector3(0f, 0f, VestibuleDepth + PreacherArenaLength * 0.5f);
            foreach (var (id, p) in new[]
            {
                ("preacher.cavity.floor.sw", new Vector3(-12f, 0f, 17f)), ("preacher.cavity.floor.se", new Vector3(12f, 0f, 17f)),
                ("preacher.cavity.floor.nw", new Vector3(-12f, 0f, 31f)), ("preacher.cavity.floor.ne", new Vector3(12f, 0f, 31f)),
            })
                cavities.Add(BuildCavity(cavitiesRoot, a, id, p, (centreOfRoom - p).normalized));

            // Floor cover (D-184: volleys can be blocked).
            Transform cover = new GameObject("Floor Cover").transform;
            cover.SetParent(root, false);
            Block("Pillar SW", new Vector3(-5f, 1.2f, 14f), new Vector3(1.2f, 2.4f, 1.2f), a.offWhite, cover);
            Block("Pillar SE", new Vector3(5f, 1.2f, 14f), new Vector3(1.2f, 2.4f, 1.2f), a.offWhite, cover);
            Block("Pillar NW", new Vector3(-6f, 1.2f, 30f), new Vector3(1.2f, 2.4f, 1.2f), a.offWhite, cover);
            Block("Pillar NE", new Vector3(6f, 1.2f, 30f), new Vector3(1.2f, 2.4f, 1.2f), a.offWhite, cover);
            Block("Low Wall (centre)", new Vector3(0f, 0.6f, 22f), new Vector3(3f, 1.2f, 0.5f), a.offWhite, cover);

            // Atmosphere above the fight zone: glowing bands on the walls up to the ceiling (no platforms, D-183).
            Transform bands = new GameObject("Upper Light Bands (atmosphere, not platforms)").transform;
            bands.SetParent(root, false);
            for (float y = 72f; y < PreacherArenaHeight - 4f; y += 14f)
            {
                Block($"Band W {y:0}", new Vector3(-PreacherArenaWidth * 0.5f + 0.05f, y, centreOfRoom.z), new Vector3(0.1f, 0.4f, PreacherArenaLength - 2f), a.glow, bands);
                Block($"Band E {y:0}", new Vector3(PreacherArenaWidth * 0.5f - 0.05f, y, centreOfRoom.z), new Vector3(0.1f, 0.4f, PreacherArenaLength - 2f), a.glow, bands);
            }
            foreach (Collider c in bands.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            // The Preacher starts at the north-east floor cavity.
            PreacherCavity start = cavities.Find(c => c.CavityId == "preacher.cavity.floor.ne");
            Transform startPoint = start != null ? start.StandPoint : null;
            var fightArea = new Bounds(new Vector3(0f, PreacherArenaHeight * 0.5f, centreOfRoom.z + 0.25f),
                                       new Vector3(PreacherArenaWidth, PreacherArenaHeight + 2f, PreacherArenaLength - 0.5f));
            BossEncounter boss = BuildPreacher(root, a, startPoint != null ? startPoint.position : new Vector3(12f, 0f, 30f),
                                               startPoint != null ? startPoint.rotation : Quaternion.identity, fightArea, cavities.ToArray());

            Vector3 OnLanding(string id, Vector3 fallback, float inward)
            {
                if (!landingTops.TryGetValue(id, out Vector3 top)) return fallback;
                PlatformSpec s = specs.Find(x => x.Id == id);
                Vector3 dir = s.Corner ? new Vector3(Mathf.Sign(s.Outward.x), 0f, Mathf.Sign(s.Outward.z)).normalized : s.Outward;
                return top - dir * inward;
            }

            // 4 ammo (D-188) on 7 spots and 1 health (D-194) on 3 spots: floor and the lower lap (D-187: no climbing needed).
            BuildBossPickups(root, a, boss, "boss.preacher", new[]
            {
                ("boss.preacher.ammo.floor.w", new Vector3(-9f, 0f, 22f), true),
                ("boss.preacher.ammo.floor.e", new Vector3(9f, 0f, 22f), true),
                ("boss.preacher.ammo.floor.n", new Vector3(0f, 0f, 35f), true),
                ("boss.preacher.ammo.l0.mid.w", OnLanding("preacher.l0.mid.w", new Vector3(-17f, 0f, 24f), 1.2f), true),
                ("boss.preacher.ammo.floor.s", new Vector3(8f, 0f, 12f), false),
                ("boss.preacher.ammo.l0.ne", OnLanding("preacher.l0.ne", new Vector3(17f, 0f, 37f), 1.0f), false),
                ("boss.preacher.ammo.l0.mid.e", OnLanding("preacher.l0.mid.e", new Vector3(17f, 0f, 24f), 1.2f), false),
            }, new[]
            {
                ("boss.preacher.health.floor.e", new Vector3(14f, 0f, 24f), true),
                ("boss.preacher.health.l0.nw", OnLanding("preacher.l0.nw", new Vector3(-17f, 0f, 37f), 1.0f), false),
                ("boss.preacher.health.l0.mid.n", OnLanding("preacher.l0.mid.n", new Vector3(0f, 0f, 37f), 1.2f), false),
            });

            AddFarClip(root);
            DisableShadows(root);
            Debug.Log($"[TWR] Preacher arena: {specs.Count} platforms, {cavities.Count} cavities, top landing at " +
                      $"{specs[specs.Count - 1].Top.y:0.0} m (D-183, D-187).");
            return true;
        }

        /// <summary>
        /// The Preacher platform path (D-183, D-186), authored as formulas: 5 stair steps from the floor (0.25 m each) up to
        /// the first corner landing (1.5 m) at the south-west corner, then 3 laps around the walls (north along the west
        /// wall, east along the north wall, ...). Each side: steps, a 6 x 6 m side landing, steps, a 7 x 7 m corner
        /// landing. Each next piece rises 0.55-0.85 m (pattern); the gaps are spread evenly at about 1-1.4 m. Laps are
        /// about 20 m apart, so headroom is never a problem. Same output every time.
        /// </summary>
        static List<PlatformSpec> PreacherLayout()
        {
            var specs = new List<PlatformSpec>();
            Vector2[] corners = { new Vector2(PathMinX, PathMinZ), new Vector2(PathMinX, PathMaxZ), new Vector2(PathMaxX, PathMaxZ), new Vector2(PathMaxX, PathMinZ) };

            // Stairs from the floor to the first landing, coming from the east (D-183: you can always climb back).
            float landingEdge = PathMinX + CornerLandingSize * 0.5f;
            for (int i = 0; i < 5; i++)
            {
                float stepTop = 0.25f * (i + 1);
                float x = landingEdge + (4.5f - i) * 0.6f;
                specs.Add(new PlatformSpec
                {
                    Id = $"preacher.stairs.{i + 1}", Top = new Vector3(x, stepTop, PathMinZ), Size = new Vector2(0.6f, 3f),
                    Thickness = stepTop, Kind = ArenaPlatformKind.Step,
                });
            }

            int rise = 0, step = 0;
            float top = FirstLandingTop;
            specs.Add(Landing(corners[0], top, 0, true, CornerNames[0], Vector3.zero));
            for (int lap = 0; lap < PreacherLaps; lap++)
            {
                for (int side = 0; side < 4; side++)
                {
                    Vector2 from = corners[side], to = corners[(side + 1) % 4];
                    Vector2 dir = (to - from).normalized;
                    float length = Vector2.Distance(from, to);
                    Vector3 outward = new Vector3(-dir.y, 0f, dir.x); // left of the walking direction = towards the wall

                    float halfway = length * 0.5f;
                    top = FillSteps(specs, from, dir, CornerLandingSize * 0.5f, halfway - MidLandingSize * 0.5f, top, lap, ref rise, ref step);
                    top += StepRises[rise++ % StepRises.Length];
                    specs.Add(Landing(from + dir * halfway, top, lap, false, "mid." + SideNames[side], outward));

                    top = FillSteps(specs, from, dir, halfway + MidLandingSize * 0.5f, length - CornerLandingSize * 0.5f, top, lap, ref rise, ref step);
                    top += StepRises[rise++ % StepRises.Length];
                    int nextCorner = (side + 1) % 4;
                    int cornerLap = nextCorner == 0 ? lap + 1 : lap;
                    specs.Add(Landing(to, top, cornerLap, true, CornerNames[nextCorner], Vector3.zero));
                }
            }
            return specs;
        }

        static PlatformSpec Landing(Vector2 xz, float top, int lap, bool corner, string name, Vector3 outward)
        {
            float size = corner ? CornerLandingSize : MidLandingSize;
            if (corner) // diagonal into the room corner (relative to the room centre, not the world origin)
                outward = new Vector3(Mathf.Sign(xz.x - (PathMinX + PathMaxX) * 0.5f), 0f, Mathf.Sign(xz.y - (PathMinZ + PathMaxZ) * 0.5f)).normalized;
            return new PlatformSpec
            {
                Id = $"preacher.l{lap}.{name}", Top = new Vector3(xz.x, top, xz.y), Size = new Vector2(size, size),
                Thickness = PlatformThickness, Kind = ArenaPlatformKind.Platform, Landing = true, Corner = corner,
                Outward = outward, Lap = lap,
            };
        }

        /// Steps between two landing edges (distances from 'from' along dir): picks the step count whose even gaps are
        /// closest to 1.2 m within 0.8-1.6 m (D-186 allows about 2.1 m at the highest rise), then places them.
        static float FillSteps(List<PlatformSpec> specs, Vector2 from, Vector2 dir, float start, float end, float top, int lap,
                               ref int rise, ref int step)
        {
            float span = end - start;
            int bestCount = 1;
            float bestScore = float.MaxValue;
            for (int n = 1; n <= 8; n++)
            {
                float lengths = 0f;
                for (int k = 0; k < n; k++) lengths += StepLengths[(step + k) % StepLengths.Length];
                float gap = (span - lengths) / (n + 1);
                if (gap < 0.3f) break;
                float score = Mathf.Abs(gap - 1.2f) + (gap < 0.8f || gap > 1.6f ? 10f : 0f);
                if (score < bestScore) { bestScore = score; bestCount = n; }
            }

            float total = 0f;
            for (int k = 0; k < bestCount; k++) total += StepLengths[(step + k) % StepLengths.Length];
            float even = (span - total) / (bestCount + 1);
            float cursor = start + even;
            bool alongX = Mathf.Abs(dir.x) > 0.5f;
            for (int k = 0; k < bestCount; k++)
            {
                float l = StepLengths[step % StepLengths.Length];
                float w = StepWidths[step % StepWidths.Length];
                top += StepRises[rise++ % StepRises.Length];
                Vector2 centre = from + dir * (cursor + l * 0.5f);
                specs.Add(new PlatformSpec
                {
                    Id = $"preacher.l{lap}.step.{step + 1:000}", Top = new Vector3(centre.x, top, centre.y),
                    Size = alongX ? new Vector2(l, w) : new Vector2(w, l), Thickness = PlatformThickness,
                    Kind = ArenaPlatformKind.Platform, Lap = lap,
                });
                cursor += l + even;
                step++;
            }
            return top;
        }

        /// A black recessed booth (D-065) standing on a platform or the floor, open towards 'facing'; the Preacher's stand
        /// point is 1.5 m in front of it.
        static PreacherCavity BuildCavity(Transform parent, RunSceneAssets a, string id, Vector3 floorPoint, Vector3 facing)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.001f) facing = Vector3.forward;
            var go = new GameObject("Cavity " + id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(floorPoint, Quaternion.LookRotation(facing.normalized));
            const float w = 2.2f, h = 3.4f, d = 1.2f;
            LocalBlock("Back", go.transform, new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.2f), a.dark, true, true);
            LocalBlock("Side L", go.transform, new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.2f, h, d), a.dark, true, true);
            LocalBlock("Side R", go.transform, new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.2f, h, d), a.dark, true, true);
            LocalBlock("Roof", go.transform, new Vector3(0f, h + 0.1f, 0f), new Vector3(w + 0.2f, 0.2f, d), a.dark, true, true);
            LocalBlock("Inner Floor", go.transform, new Vector3(0f, 0.01f, 0f), new Vector3(w, 0.02f, d), a.dark, true, false);

            var stand = new GameObject("Stand Point").transform;
            stand.SetParent(go.transform, false);
            stand.localPosition = new Vector3(0f, 0f, 1.5f);
            stand.localRotation = Quaternion.identity;

            var cavity = go.AddComponent<PreacherCavity>();
            var so = new SerializedObject(cavity);
            so.FindProperty("cavityId").stringValue = id;
            so.FindProperty("standPoint").objectReferenceValue = stand;
            so.ApplyModifiedPropertiesWithoutUndo();
            return cavity;
        }

        /// The Preacher: tall, four long arms (D-068) placeholder; no NavMeshAgent (it teleports, D-184); Enemy layer;
        /// projectile pool and the destination ghost live next to it in the arena.
        static BossEncounter BuildPreacher(Transform parent, RunSceneAssets a, Vector3 position, Quaternion rotation, Bounds fightArea,
                                           PreacherCavity[] cavities)
        {
            var go = new GameObject("Boss - The Preacher (D-047, D-068, D-184)");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1.4f, 4.4f, 1.2f);
            box.center = new Vector3(0f, 2.2f, 0f);

            Transform model = new GameObject("Model").transform;
            model.SetParent(go.transform, false);
            BossPart("Robe", PrimitiveType.Capsule, model, new Vector3(0f, 1.7f, 0f), new Vector3(0.9f, 1.7f, 0.9f), Vector3.zero, a.skin);
            BossPart("Chest", PrimitiveType.Capsule, model, new Vector3(0f, 3.1f, 0f), new Vector3(0.75f, 0.7f, 0.6f), Vector3.zero, a.skin);
            BossPart("Head", PrimitiveType.Sphere, model, new Vector3(0f, 4.05f, 0.05f), new Vector3(0.5f, 0.7f, 0.55f), Vector3.zero, a.skin);
            BossPart("Eye L", PrimitiveType.Sphere, model, new Vector3(-0.11f, 4.15f, 0.27f), Vector3.one * 0.13f, Vector3.zero, a.eye);
            BossPart("Eye R", PrimitiveType.Sphere, model, new Vector3(0.11f, 4.15f, 0.27f), Vector3.one * 0.13f, Vector3.zero, a.eye);
            var arms = new Transform[4];
            Vector3[] shoulders = { new Vector3(-0.5f, 3.5f, 0f), new Vector3(0.5f, 3.5f, 0f), new Vector3(-0.45f, 3.0f, 0.05f), new Vector3(0.45f, 3.0f, 0.05f) };
            for (int i = 0; i < 4; i++)
            {
                arms[i] = new GameObject("Arm " + (i + 1)).transform;
                arms[i].SetParent(model, false);
                arms[i].localPosition = shoulders[i];
                arms[i].localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? -1f : 1f) * (i < 2 ? 12f : 22f));
                BossPart("Long Arm", PrimitiveType.Cylinder, arms[i], new Vector3(0f, -1.1f, 0f), new Vector3(0.13f, 1.1f, 0.13f), Vector3.zero, a.skin);
            }
            Transform hands = new GameObject("Hands (projectile origin)").transform;
            hands.SetParent(go.transform, false);
            hands.localPosition = new Vector3(0f, 3.3f, 0.9f);

            SetBossLayer(go);
            go.AddComponent<BossMarker>();
            var health = go.AddComponent<BossHealth>();
            Assign(health, "data", a.preacher);
            BossAudio sounds = AddBossAudio(go, a.preacherSounds);
            var visuals = go.AddComponent<PreacherVisuals>();
            Assign(visuals, "model", model);
            AssignArray(visuals, "arms", arms);
            var death = go.AddComponent<BossDeath>();
            BossEncounter encounter = AddEncounter(go, a.preacher, health, death, sounds, fightArea);

            // Pool (D-193: at most 30) and the destination telegraph ghost (D-184), both outside the boss hierarchy.
            var poolGO = new GameObject("Preacher Projectiles (pool of 30, D-193)");
            poolGO.transform.SetParent(parent, false);
            var pool = poolGO.AddComponent<BossProjectilePool>();
            Assign(pool, "material", a.orb);

            var ghost = new GameObject("Teleport Telegraph Ghost (D-184)");
            ghost.transform.SetParent(parent, false);
            var ghostRenderers = new List<Renderer>
            {
                BossPart("Silhouette", PrimitiveType.Capsule, ghost.transform, new Vector3(0f, 2.1f, 0f), new Vector3(1.0f, 2.1f, 1.0f), Vector3.zero, a.glow),
                BossPart("Halo", PrimitiveType.Sphere, ghost.transform, new Vector3(0f, 4.1f, 0f), Vector3.one * 0.8f, Vector3.zero, a.glow),
            };
            var ghostAudio = ghost.AddComponent<AudioSource>();
            ConfigureBossAudioSource(ghostAudio);

            var brain = go.AddComponent<PreacherBrain>();
            Assign(brain, "data", a.preacher);
            Assign(brain, "encounter", encounter);
            Assign(brain, "health", health);
            Assign(brain, "sounds", sounds);
            Assign(brain, "visuals", visuals);
            Assign(brain, "hands", hands);
            Assign(brain, "projectiles", pool);
            Assign(brain, "telegraphGhost", ghost.transform);
            AssignArray(brain, "ghostRenderers", ghostRenderers.ToArray());
            Assign(brain, "ghostAudio", ghostAudio);
            AssignArray(brain, "cavities", cavities);
            Assign(death, "data", a.preacher);
            AssignArray(death, "disableOnDeath", new Object[] { brain, visuals });
            return encounter;
        }

        // ---------- shared boss pieces ----------

        static BossEncounter AddEncounter(GameObject go, BossData data, BossHealth health, BossDeath death, BossAudio sounds, Bounds fightArea)
        {
            var encounter = go.AddComponent<BossEncounter>();
            Assign(encounter, "data", data);
            Assign(encounter, "health", health);
            Assign(encounter, "death", death);
            Assign(encounter, "sounds", sounds);
            var so = new SerializedObject(encounter);
            so.FindProperty("fightArea").boundsValue = fightArea;
            so.ApplyModifiedPropertiesWithoutUndo();
            return encounter;
        }

        static BossAudio AddBossAudio(GameObject go, BossSoundSet set)
        {
            var source = go.AddComponent<AudioSource>();
            ConfigureBossAudioSource(source);
            var audio = go.AddComponent<BossAudio>();
            Assign(audio, "sounds", set);
            return audio;
        }

        /// 3D, audible across most of an arena (bosses are loud), never a noise event (D-138).
        static void ConfigureBossAudioSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = 90f;
        }

        static void SetBossLayer(GameObject go)
        {
            int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
            if (enemyLayer >= 0) SetLayerRecursively(go, enemyLayer); // shootable, never baked into the NavMesh (D-134)
            else Debug.LogError("[TWR] 'Enemy' layer missing. Run TWR > Setup Project.");
        }

        /// 4 ammo pickups (D-188, respawning while the boss lives) and 1 health pickup (D-194, no respawn), each in a
        /// PickupGroup whose spots are rolled (D-177). The first spots flagged default are the authored positions.
        static void BuildBossPickups(Transform root, RunSceneAssets a, BossEncounter boss, string prefix,
                                     (string id, Vector3 position, bool isDefault)[] ammoSpots,
                                     (string id, Vector3 position, bool isDefault)[] healthSpots)
        {
            var ammo = new List<ItemPickup>();
            foreach (var spot in ammoSpots)
                if (spot.isDefault) ammo.Add(AmmoPickup($"Ammo Pickup ({spot.id}, D-188)", spot.position, a.combat, a.ammo, root));
            ItemPickup health = null;
            foreach (var spot in healthSpots)
                if (spot.isDefault) { health = HealthPickup($"Health Pickup ({spot.id}, D-194)", spot.position, a, root); break; }

            BuildPickupGroup(root, prefix + ".ammo", ammo.ToArray(), "", ammoSpots);
            if (health != null) BuildPickupGroup(root, prefix + ".health", new[] { health }, "", healthSpots);

            var respawnGO = new GameObject("Ammo Respawn (while the boss lives, D-188)");
            respawnGO.transform.SetParent(root, false);
            var respawner = respawnGO.AddComponent<BossPickupRespawner>();
            Assign(respawner, "boss", boss);
            AssignArray(respawner, "pickups", ammo.ToArray());
        }

        static void AddFarClip(Transform root)
        {
            var go = new GameObject("Camera Far Clip 300 m (D-193)");
            go.transform.SetParent(root, false);
            var farClip = go.AddComponent<ArenaCameraFarClip>();
            var so = new SerializedObject(farClip);
            so.FindProperty("farClip").floatValue = ArenaFarClip;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddPlatformMarker(GameObject go, string id, ArenaPlatformKind kind)
        {
            var marker = go.AddComponent<ArenaPlatform>();
            var so = new SerializedObject(marker);
            so.FindProperty("platformId").stringValue = id;
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// D-193: emissive light only; nothing in the arenas casts or receives realtime shadows.
        static void DisableShadows(Transform root)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        /// Primitive body part without a collider (the boss root's box is what bullets hit).
        static Renderer BossPart(string name, PrimitiveType shape, Transform parent, Vector3 localPosition, Vector3 localScale,
                                 Vector3 localEuler, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        // ---------- HUD boss bar (Core scene, D-185) ----------

        /// Top-centre bar with the localized boss name above it; hidden until BossEvents.Started.
        static void BuildBossHud(GameObject canvas)
        {
            RectTransform root = UiRect("Boss Bar (D-185)", canvas.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                        new Vector2(0f, -36f), new Vector2(900f, 64f));
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            RectTransform nameRect = UiRect("Boss Name (localized)", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(900f, 36f));
            var label = nameRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = string.Empty;
            label.fontSize = 28f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.12f, 0.12f, 0.12f);
            label.raycastTarget = false;

            RectTransform bar = UiRect("Bar", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(900f, 16f));
            var background = bar.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.15f);
            background.raycastTarget = false;
            RectTransform fill = UiRect("Fill", bar, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(2f, 2f);
            fill.offsetMax = new Vector2(-2f, -2f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.raycastTarget = false;

            var ui = root.gameObject.AddComponent<BossHealthBarUI>();
            Assign(ui, "group", group);
            Assign(ui, "fill", fill);
            Assign(ui, "fillImage", fillImage);
            Assign(ui, "nameLabel", label);
        }
    }
}
