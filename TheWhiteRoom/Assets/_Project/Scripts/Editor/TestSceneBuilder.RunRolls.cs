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

        /// Call after BuildPuzzleTest in the run's puzzle section (NOT in the sandbox).
        static bool AuthorPuzzleSectionRolls(Transform root, RunSceneAssets a)
        {
            // Enemies: the R2 Seeker (same spawn + patrol) is the curated default; two hidden corner spots for rolls.
            Transform enemies = new GameObject("Enemies (R2 curated + R3 roll spots, D-176)").transform;
            enemies.SetParent(root, false);
            BuildSeekerSpawn(enemies, a, "Seeker (R2 patrol, curated only)", "puzzle.enemy.r2_patrol", true, false,
                new Vector3(20f, 0f, 3.5f), Vector3.back,
                new[] { new Vector3(20f, 0f, -1.5f), new Vector3(14f, 0f, -1.5f), new Vector3(14f, 0f, 3.5f), new Vector3(20f, 0f, 3.5f) });
            BuildSeekerSpawn(enemies, a, "Seeker (NE corner, roll spot)", "puzzle.enemy.ne", false, true,
                new Vector3(21.6f, 0f, 5.5f), Vector3.left,
                new[] { new Vector3(21.5f, 0f, 3.6f), new Vector3(14f, 0f, 4.6f), new Vector3(13.5f, 0f, -2f), new Vector3(20.5f, 0f, -0.5f) });
            BuildSeekerSpawn(enemies, a, "Seeker (SE corner behind the cage, roll spot)", "puzzle.enemy.se", false, true,
                new Vector3(21.6f, 0f, -5.5f), Vector3.forward,
                new[] { new Vector3(21.4f, 0f, -4.6f), new Vector3(19.5f, 0f, 0.5f), new Vector3(13f, 0f, 2.5f), new Vector3(13f, 0f, -2f) });
            // The SE corner is hidden by the cage wall; the NE corner needs a partition.
            Partition(root, "Partition (hides the NE roll spot, D-176)", new Vector3(20.6f, 0f, 5.15f), new Vector3(PartitionThickness, 2.6f, 1.7f), a.offWhite);

            // Switch slots (D-173): the three R2 switches are the defaults, plus a spare slot on the south wall.
            PuzzleController controller = Object.FindAnyObjectByType<PuzzleController>();
            if (controller == null)
            {
                Debug.LogError("[TWR] Puzzle section: no PuzzleController found after BuildPuzzleTest.");
                return false;
            }
            PuzzleVariantPool pool = PreparePuzzleVariantPool();
            if (pool == null) return false;

            var slots = new List<ShootableSwitch>();
            SerializedProperty existing = new SerializedObject(controller).FindProperty("switches");
            for (int i = 0; i < existing.arraySize; i++)
                if (existing.GetArrayElementAtIndex(i).objectReferenceValue is ShootableSwitch sw) slots.Add(sw);

            Material switchMat = GetOrCreateMaterial("M_PuzzleSwitch", new Color(0.55f, 0.05f, 0.04f), new Color(0.3f, 0.03f, 0.02f));
            Transform puzzleGroup = controller.transform.parent;
            slots.Add(BuildSwitch("Switch D (south wall, spare slot)", "switch.south", new Vector3(13.5f, 2.2f, -6f), Vector3.forward,
                                  a.offWhite, switchMat, puzzleGroup));

            Material markMat = GetOrCreateMaterial("M_OrderMark", Color.white, Color.white * 2f);
            for (int i = 0; i < slots.Count; i++)
                ConfigureSlot(slots[i], i < 3, markMat, pool.activeSwitchCount); // first three = R2 defaults

            AssignArray(controller, "switches", slots.ToArray());
            Assign(controller, "variantPool", pool);
            var controllerSo = new SerializedObject(controller);
            // The puzzle room interior (x 10.2..22.2, z -6..6, 7 m high): D-130 ammo must stay inside.
            controllerSo.FindProperty("puzzleArea").boundsValue = new Bounds(new Vector3(16.2f, 3.5f, 0f), new Vector3(12f, 7f, 12f));
            controllerSo.ApplyModifiedPropertiesWithoutUndo();
            if (controller.GetComponent<PuzzleSequenceClue>() == null) controller.gameObject.AddComponent<PuzzleSequenceClue>();

            // Ammo (D-130, D-177): one box, four spots, all inside the puzzle room in front of the exit door.
            GameObject ammoGO = GameObject.Find("Ammo Pickup (puzzle)");
            ItemPickup ammo = ammoGO != null ? ammoGO.GetComponent<ItemPickup>() : null;
            if (ammo == null)
            {
                Debug.LogError("[TWR] Puzzle section: 'Ammo Pickup (puzzle)' not found after BuildPuzzleTest.");
                return false;
            }
            BuildPickupGroup(root, "puzzle.ammo", new[] { ammo }, a.puzzle.data.puzzleId, new[]
            {
                ("puzzle.ammo.sw", new Vector3(12.5f, 0f, -4.5f), true),
                ("puzzle.ammo.nw", new Vector3(11.6f, 0f, 4.6f), false),
                ("puzzle.ammo.mid", new Vector3(17.5f, 0f, 1f), false),
                ("puzzle.ammo.se", new Vector3(20.2f, 0f, -4.9f), false),
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

        /// Four hidden roll spots (enemyCount 3 + 1), each behind a partition, at least 15 m from the entry.
        static void AuthorCombatRollSpots(Transform root, Transform enemies, RunSceneAssets a)
        {
            Transform parts = new GameObject("Partitions (hide the roll spots from the entry, D-176)").transform;
            parts.SetParent(root, false);
            Partition(parts, "Partition NW", new Vector3(-7.75f, 0f, 18f), new Vector3(4.5f, PartitionHeight, PartitionThickness), a.offWhite);
            Partition(parts, "Partition NE", new Vector3(7.75f, 0f, 18f), new Vector3(4.5f, PartitionHeight, PartitionThickness), a.offWhite);
            Partition(parts, "Partition W", new Vector3(-8f, 0f, 14.5f), new Vector3(PartitionThickness, PartitionHeight, 4f), a.offWhite);
            Partition(parts, "Partition E", new Vector3(8f, 0f, 14.5f), new Vector3(PartitionThickness, PartitionHeight, 4f), a.offWhite);

            BuildSeekerSpawn(enemies, a, "Seeker (NW pocket, roll spot)", "combat.enemy.nw", false, true, new Vector3(-8.6f, 0f, 19.2f), Vector3.right,
                new[] { new Vector3(-8.2f, 0f, 19.2f), new Vector3(-3f, 0f, 19.2f), new Vector3(-5.5f, 0f, 9.5f), new Vector3(-6.5f, 0f, 16.8f) });
            BuildSeekerSpawn(enemies, a, "Seeker (NE pocket, roll spot)", "combat.enemy.ne", false, true, new Vector3(8.6f, 0f, 19.2f), Vector3.left,
                new[] { new Vector3(8.2f, 0f, 19.2f), new Vector3(3f, 0f, 19.2f), new Vector3(5.5f, 0f, 9.5f), new Vector3(6.5f, 0f, 16.8f) });
            BuildSeekerSpawn(enemies, a, "Seeker (W pocket, roll spot)", "combat.enemy.w", false, true, new Vector3(-9.2f, 0f, 15.3f), Vector3.forward,
                new[] { new Vector3(-9.1f, 0f, 16.9f), new Vector3(-2f, 0f, 16.5f), new Vector3(-1.5f, 0f, 9f), new Vector3(-6f, 0f, 7f) });
            BuildSeekerSpawn(enemies, a, "Seeker (E pocket, roll spot)", "combat.enemy.e", false, true, new Vector3(9.2f, 0f, 15.3f), Vector3.forward,
                new[] { new Vector3(9.1f, 0f, 16.9f), new Vector3(2f, 0f, 16.5f), new Vector3(1.5f, 0f, 9f), new Vector3(6f, 0f, 7f) });
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
