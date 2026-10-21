using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Rendering;
using TWR.Enemies;
using TWR.Items;
using TWR.Puzzles;

namespace TWR.EditorTools
{
    /// <summary>
    /// Run Structure step R3 (seeded section rolls, D-172 to D-177) authoring for "TWR/Run/Build Run Scenes".
    /// The R2 layout stays as the curated default (seed 0): its spawn points and pickup positions are marked isDefault.
    /// The R2 spawn points are "curated only" (not rollable), because they stand in view of the entry or closer than
    /// 15 m, which D-176 forbids for rolled spots. Instead each enemy section gets enemyCount + 1 hidden roll spots,
    /// each behind an office partition and with its own patrol route (stealth routes all cross the main room, so any
    /// pair still covers it, D-169). Every pickup goes into a PickupGroup with 4-6 candidate spots (the puzzle room's
    /// ammo spots all inside the puzzle area, D-130). The puzzle room gets a 4th (spare) switch slot, three tally dots
    /// under every slot for Sequence variants (D-175), the variant pool (D-173, hints D-178), its puzzle area box and
    /// the ordered light-up clue. Every id is a stable string so rolls never depend on hierarchy order (D-172).
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string PuzzleVariantPoolPath = Root + "/Data/Puzzles/PuzzleVariantPool_Main.asset";
        const float PartitionHeight = 2.4f;
        const float PartitionThickness = 0.15f;

        // ---------- variant pool (D-173, D-178) ----------

        /// The D-173 pool: AllActive+Latch, Sequence+Latch, AllActive+Toggle, each with its D-178 hint key.
        static PuzzleVariantPool PreparePuzzleVariantPool()
        {
            EnsureFolder(Root + "/Data/Puzzles");
            PuzzleVariantPool pool = LoadOrCreate<PuzzleVariantPool>(PuzzleVariantPoolPath, FillVariantPool);
            if (pool == null)
            {
                Debug.LogError("[TWR] Could not create " + PuzzleVariantPoolPath);
                return null;
            }
            bool changed = false;
            if (pool.variants == null || pool.variants.Count == 0)
            {
                FillVariantPool(pool);
                changed = true;
            }
            foreach (PuzzleVariant v in pool.variants)
            {
                if (v == null || (v.hint != null && !v.hint.IsEmpty)) continue;
                v.hint = new LocalizedString("Subtitles", HintKey(v.solveMode, v.switchBehaviour)); // repair empty hints
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(pool);
                AssetDatabase.SaveAssets();
            }
            return pool;
        }

        static void FillVariantPool(PuzzleVariantPool pool)
        {
            pool.activeSwitchCount = 3;
            pool.variants = new List<PuzzleVariant>
            {
                Variant(PuzzleSolveMode.AllActive, SwitchBehaviour.Latch),
                Variant(PuzzleSolveMode.Sequence, SwitchBehaviour.Latch),
                Variant(PuzzleSolveMode.AllActive, SwitchBehaviour.Toggle),
                // Never Sequence+Toggle (D-173).
            };
        }

        static PuzzleVariant Variant(PuzzleSolveMode mode, SwitchBehaviour behaviour) => new PuzzleVariant
        {
            solveMode = mode,
            switchBehaviour = behaviour,
            hint = new LocalizedString("Subtitles", HintKey(mode, behaviour)),
        };

        /// "hint.puzzle.allactive_latch" etc. (the keys ProjectSetup adds to the Subtitles table, D-178).
        static string HintKey(PuzzleSolveMode mode, SwitchBehaviour behaviour) =>
            "hint.puzzle." + mode.ToString().ToLowerInvariant() + "_" + behaviour.ToString().ToLowerInvariant();

        // ---------- puzzle section ----------

        /// Call after BuildLargePuzzleRoom in the run's puzzle section (D-240, D-244; NOT in the sandbox).
        static bool AuthorPuzzleSectionRolls(Transform root, RunSceneAssets a)
        {
            // Enemies: the curated floor patrol (seed 0) and two hidden spots under the exit ledge for rolls, each behind a
            // partition (D-176). Same ids as before, so existing seeds pick the same spot (D-246).
            Transform enemies = new GameObject("Enemies (curated + R3 roll spots, D-176)").transform;
            enemies.SetParent(root, false);
            BuildSeekerSpawn(enemies, a, "Seeker (floor patrol, curated only)", "puzzle.enemy.r2_patrol", true, false,
                new Vector3(42f, 0f, 5f), Vector3.left,
                new[] { new Vector3(46f, 0f, 5f), new Vector3(20f, 0f, 5f), new Vector3(20f, 0f, -6f), new Vector3(44f, 0f, -8f) });
            BuildSeekerSpawn(enemies, a, "Seeker (NE, under the ledge, roll spot)", "puzzle.enemy.ne", false, true,
                new Vector3(55.5f, 0f, 10f), Vector3.back,
                new[] { new Vector3(55.5f, 0f, 9.5f), new Vector3(40f, 0f, 6f), new Vector3(24f, 0f, 5f), new Vector3(44f, 0f, -9f) });
            BuildSeekerSpawn(enemies, a, "Seeker (SE, under the ledge, roll spot)", "puzzle.enemy.se", false, true,
                new Vector3(55.5f, 0f, -10f), Vector3.forward,
                new[] { new Vector3(55.5f, 0f, -9.5f), new Vector3(42f, 0f, -10f), new Vector3(22f, 0f, -7f), new Vector3(40f, 0f, 6f) });
            Partition(root, "Partition (hides the NE roll spot, D-176)", new Vector3(53.2f, 0f, 9.5f), new Vector3(PartitionThickness, PartitionHeight, 5f), a.offWhite);
            Partition(root, "Partition (hides the SE roll spot, D-176)", new Vector3(53.2f, 0f, -9.5f), new Vector3(PartitionThickness, PartitionHeight, 5f), a.offWhite);

            // Switch slots (D-173, D-244): A, B, C are the defaults (seed 0), D under the mover is the spare.
            PuzzleController controller = Object.FindAnyObjectByType<PuzzleController>();
            if (controller == null)
            {
                Debug.LogError("[TWR] Puzzle section: no PuzzleController found after BuildLargePuzzleRoom.");
                return false;
            }
            PuzzleVariantPool pool = PreparePuzzleVariantPool();
            if (pool == null) return false;

            var slots = new List<ShootableSwitch>();
            SerializedProperty existing = new SerializedObject(controller).FindProperty("switches");
            for (int i = 0; i < existing.arraySize; i++)
                if (existing.GetArrayElementAtIndex(i).objectReferenceValue is ShootableSwitch sw) slots.Add(sw);
            if (slots.Count < 4)
            {
                Debug.LogError($"[TWR] Puzzle section: expected 4 switch slots from BuildLargePuzzleRoom, found {slots.Count} (D-244).");
                return false;
            }

            Material markMat = GetOrCreateMaterial("M_OrderMark", Color.white, Color.white * 2f);
            for (int i = 0; i < slots.Count; i++)
                ConfigureSlot(slots[i], i < 3, markMat, pool.activeSwitchCount); // first three = the curated defaults

            Assign(controller, "variantPool", pool);
            var controllerSo = new SerializedObject(controller);
            // The whole large room (x 10.2..58.2, z -12..12, 12 m high): D-130 ammo and every slot must stay inside (D-244).
            controllerSo.FindProperty("puzzleArea").boundsValue = new Bounds(new Vector3((LpMinX + LpMaxX) * 0.5f, LpHeight * 0.5f, 0f),
                                                                              new Vector3(LpMaxX - LpMinX, LpHeight, LpMaxZ - LpMinZ));
            controllerSo.ApplyModifiedPropertiesWithoutUndo();
            if (controller.GetComponent<PuzzleSequenceClue>() == null) controller.gameObject.AddComponent<PuzzleSequenceClue>();

            // Ammo (D-130, D-177): one box, four spots inside the room, all reachable before the solve: two on the floor
            // near the entry, one on the gallery, one on the third tower (D-244).
            GameObject ammoGO = GameObject.Find("Ammo Pickup (puzzle)");
            ItemPickup ammo = ammoGO != null ? ammoGO.GetComponent<ItemPickup>() : null;
            if (ammo == null)
            {
                Debug.LogError("[TWR] Puzzle section: 'Ammo Pickup (puzzle)' not found after BuildLargePuzzleRoom.");
                return false;
            }
            BuildPickupGroup(root, "puzzle.ammo", new[] { ammo }, a.puzzle.data.puzzleId, new[]
            {
                ("puzzle.ammo.sw", new Vector3(12.5f, 0f, -4.5f), true),
                ("puzzle.ammo.nw", new Vector3(11.6f, 0f, 4.6f), false),
                ("puzzle.ammo.mid", new Vector3(33f, 3f, 10.5f), false),   // gallery
                ("puzzle.ammo.se", new Vector3(29.2f, 2.7f, -6.2f), false), // tower 3
            });
            return true;
        }

        /// defaultActive, slotRoot (the whole switch: plate, button, light) and three hidden tally dots (D-175).
        static void ConfigureSlot(ShootableSwitch sw, bool defaultActive, Material markMat, int marks)
        {
            Transform slotRoot = sw.transform.parent;
            var dots = new GameObject[marks];
            for (int i = 0; i < marks; i++)
            {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dot.name = "Order Mark " + (i + 1) + " (D-175)";
                Object.DestroyImmediate(dot.GetComponent<Collider>()); // never stops bullets
                dot.transform.SetParent(slotRoot, false);
                dot.transform.localPosition = new Vector3((i - (marks - 1) * 0.5f) * 0.15f, -0.42f, 0.03f);
                dot.transform.localScale = new Vector3(0.09f, 0.09f, 0.05f);
                var renderer = dot.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = markMat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                dot.SetActive(false); // shown by the PuzzleController only for Sequence variants
                dots[i] = dot;
            }
            var so = new SerializedObject(sw);
            so.FindProperty("defaultActive").boolValue = defaultActive;
            so.FindProperty("slotRoot").objectReferenceValue = slotRoot.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssignArray(sw, "orderMarks", dots);
        }

        // ---------- combat + stealth roll spots (D-176) ----------

        /// Six hidden roll spots (enemyCount 4 + 2, D-243) in the large combat room, each behind a partition or the central
        /// pillar, at least 15 m from the entry. The four old ids keep their names (D-246).
        static void AuthorCombatRollSpots(Transform root, Transform enemies, RunSceneAssets a)
        {
            Transform parts = new GameObject("Partitions (hide the roll spots from the entry, D-176)").transform;
            parts.SetParent(root, false);
            Partition(parts, "Partition NW", new Vector3(-13.5f, 0f, 58.5f), new Vector3(4f, PartitionHeight, PartitionThickness), a.offWhite);
            Partition(parts, "Partition NE", new Vector3(13.5f, 0f, 58.5f), new Vector3(4f, PartitionHeight, PartitionThickness), a.offWhite);
            Partition(parts, "Partition W", new Vector3(-13.5f, 0f, 28.5f), new Vector3(4f, PartitionHeight, PartitionThickness), a.offWhite);
            Partition(parts, "Partition E", new Vector3(13.5f, 0f, 28.5f), new Vector3(4f, PartitionHeight, PartitionThickness), a.offWhite);
            Partition(parts, "Partition N (under the exit ledge)", new Vector3(0f, 0f, 62.5f), new Vector3(6f, PartitionHeight, PartitionThickness), a.offWhite);

            BuildSeekerSpawn(enemies, a, "Seeker (NW pocket, roll spot)", "combat.enemy.nw", false, true, new Vector3(-13.5f, 0f, 60f), Vector3.back,
                new[] { new Vector3(-13.5f, 0f, 60.5f), new Vector3(-8f, 0f, 54f), new Vector3(-12f, 0f, 36f), new Vector3(-4f, 0f, 45f) });
            BuildSeekerSpawn(enemies, a, "Seeker (NE pocket, roll spot)", "combat.enemy.ne", false, true, new Vector3(13.5f, 0f, 60f), Vector3.back,
                new[] { new Vector3(13.5f, 0f, 60.5f), new Vector3(8f, 0f, 54f), new Vector3(12f, 0f, 36f), new Vector3(4f, 0f, 45f) });
            BuildSeekerSpawn(enemies, a, "Seeker (W pocket, roll spot)", "combat.enemy.w", false, true, new Vector3(-13.5f, 0f, 30f), Vector3.back,
                new[] { new Vector3(-13.5f, 0f, 30.5f), new Vector3(-12f, 0f, 16f), new Vector3(-3f, 0f, 24f), new Vector3(-4f, 0f, 36f) });
            BuildSeekerSpawn(enemies, a, "Seeker (E pocket, roll spot)", "combat.enemy.e", false, true, new Vector3(13.5f, 0f, 30f), Vector3.back,
                new[] { new Vector3(13.5f, 0f, 30.5f), new Vector3(12f, 0f, 16f), new Vector3(3f, 0f, 24f), new Vector3(4f, 0f, 36f) });
            BuildSeekerSpawn(enemies, a, "Seeker (under the exit ledge, roll spot)", "combat.enemy.n", false, true, new Vector3(0f, 0f, 66f), Vector3.back,
                new[] { new Vector3(0f, 0f, 66.5f), new Vector3(-9f, 0f, 61f), new Vector3(9f, 0f, 61f), new Vector3(0f, 0f, 54f) });
            BuildSeekerSpawn(enemies, a, "Seeker (behind the pillar, roll spot)", "combat.enemy.c", false, true, new Vector3(0f, 0f, 52.5f), Vector3.back,
                new[] { new Vector3(0f, 0f, 53f), new Vector3(-6f, 0f, 54f), new Vector3(6f, 0f, 44f), new Vector3(-6f, 0f, 44f) });
        }

        /// Three hidden roll spots (enemyCount 2 + 1). Every route crosses the main room, so any pair patrols it (D-169).
        static void AuthorStealthRollSpots(Transform root, Transform enemies, RunSceneAssets a)
        {
            Transform parts = new GameObject("Partitions (hide the roll spots from the entry, D-176)").transform;
            parts.SetParent(root, false);
            Partition(parts, "Partition NE", new Vector3(9.25f, 0f, 19.5f), new Vector3(5.5f, PartitionHeight, PartitionThickness), a.offWhite);
            Partition(parts, "Partition E", new Vector3(9f, 0f, 15.5f), new Vector3(PartitionThickness, PartitionHeight, 5f), a.offWhite);
            Partition(parts, "Partition N", new Vector3(-1f, 0f, 19f), new Vector3(6f, PartitionHeight, PartitionThickness), a.offWhite);

            BuildSeekerSpawn(enemies, a, "Seeker (NE pocket, roll spot)", "stealth.enemy.ne", false, true, new Vector3(10f, 0f, 20.8f), Vector3.left,
                new[] { new Vector3(9.5f, 0f, 20.8f), new Vector3(-3f, 0f, 20.6f), new Vector3(-3f, 0f, 16f), new Vector3(5f, 0f, 16f) });
            BuildSeekerSpawn(enemies, a, "Seeker (E pocket, roll spot)", "stealth.enemy.e", false, true, new Vector3(10.8f, 0f, 16.8f), Vector3.back,
                new[] { new Vector3(10.5f, 0f, 12f), new Vector3(-6.5f, 0f, 9.5f), new Vector3(-6.5f, 0f, 16.5f), new Vector3(4f, 0f, 16.5f) });
            BuildSeekerSpawn(enemies, a, "Seeker (behind the N partition, roll spot)", "stealth.enemy.n", false, true, new Vector3(-1f, 0f, 21f), Vector3.back,
                new[] { new Vector3(-1f, 0f, 20.8f), new Vector3(-7f, 0f, 18f), new Vector3(-7f, 0f, 8f), new Vector3(4f, 0f, 10.5f) });
        }

        // ---------- pickups (D-177) ----------

        /// A PickupGroup with its candidate spots. The default spots must be where the pickups were authored (R2).
        static PickupGroup BuildPickupGroup(Transform parent, string groupId, ItemPickup[] pickups, string puzzleAreaId,
                                            (string id, Vector3 position, bool isDefault)[] spots)
        {
            var go = new GameObject($"Pickup Group '{groupId}' (D-177)");
            go.transform.SetParent(parent, false);
            var spotComponents = new PickupSpot[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                var spotGO = new GameObject($"Spot {spots[i].id}{(spots[i].isDefault ? " (default)" : "")}");
                spotGO.transform.SetParent(go.transform, false);
                spotGO.transform.position = spots[i].position;
                var spot = spotGO.AddComponent<PickupSpot>();
                var spotSo = new SerializedObject(spot);
                spotSo.FindProperty("spotId").stringValue = spots[i].id;
                spotSo.FindProperty("isDefault").boolValue = spots[i].isDefault;
                spotSo.ApplyModifiedPropertiesWithoutUndo();
                spotComponents[i] = spot;
            }

            var group = go.AddComponent<PickupGroup>();
            AssignArray(group, "pickups", pickups);
            AssignArray(group, "spots", spotComponents);
            var so = new SerializedObject(group);
            so.FindProperty("groupId").stringValue = groupId;
            so.FindProperty("puzzleAreaId").stringValue = puzzleAreaId ?? "";
            so.ApplyModifiedPropertiesWithoutUndo();
            return group;
        }

        /// Office partition (greybox) standing on the floor: bottom-centre at floorCentre.
        static void Partition(Transform parent, string name, Vector3 floorCentre, Vector3 size, Material material)
        {
            Block(name, floorCentre + Vector3.up * (size.y * 0.5f), size, material, parent);
        }
    }
}
