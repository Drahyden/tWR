using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Rendering;
using TWR.Combat;
using TWR.Puzzles;

namespace TWR.EditorTools
{
    /// <summary>
    /// Step 3 (pistol switch puzzle, D-107; D-105) part of "TWR/Build Test Scene". Creates the PuzzleData
    /// and DoorData assets (D-124, D-127, D-128) and builds a 12 x 12 x 7 m puzzle room east of the
    /// white room: three pistol-only switches (chest height, 5.5 m up a bare wall, behind bars), the
    /// puzzle-area trigger, an ammo pickup (D-130), the locked exit door with a small corridor
    /// behind it and the PuzzleEvents console logger (D-126). Every reference is wired here.
    /// The scene is rebuilt from scratch each run, so re-running it is always clean.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string PuzzleDataPath = Root + "/Data/Puzzles/Puzzle_TestSwitches.asset";
        const string DoorDataPath = Root + "/Data/Puzzles/Door_Default.asset";

        // Doorway from the white room into the puzzle room (in Wall E at x = 10.1).
        const float MainDoorwayHalfWidth = 1.2f;
        const float MainDoorwayHeight = 3f;

        // Puzzle room interior: x 10.2..22.2, z -6..6, y 0..7.
        const float PuzzleRoomHeight = 7f;
        const float HighSwitchHeight = 5.5f; // eye height (1.65) + jump (1.1) + reach (2.5) = 5.25 < 5.5: unreachable by hand
        // Bars in front of the caged switch: thin colliders, gaps a bullet passes but a body (0.7 m wide) can't.
        const int BarCount = 12;
        const float BarThickness = 0.06f;

        struct PuzzleAssets
        {
            public PuzzleData data;
            public DoorData door;
            public int interactableLayer;
        }

        // ---------- assets ----------

        static bool PreparePuzzleAssets(out PuzzleAssets assets)
        {
            assets = default;
            assets.interactableLayer = LayerMask.NameToLayer(InteractableLayerName);
            if (assets.interactableLayer < 0)
            {
                Debug.LogError("[TWR] 'Interactable' layer missing. Run TWR > Setup Project.");
                return false;
            }

            EnsureFolder(Root + "/Data/Puzzles");

            // Slice puzzle: main path (not optional), all switches on in any order, switches latch (D-124).
            assets.data = LoadOrCreate<PuzzleData>(PuzzleDataPath, data =>
            {
                data.puzzleId = "test.switches";
                data.isOptional = false;
                data.solveMode = PuzzleSolveMode.AllActive;
                data.switchBehaviour = SwitchBehaviour.Latch;
                data.switchNoiseRadius = 4f;
                data.resetNoiseRadius = 4f;
            });
            assets.door = LoadOrCreate<DoorData>(DoorDataPath, door =>
            {
                door.openStyle = DoorOpenStyle.Slide;
                door.slideDistance = 1.6f;
                door.unlockNoiseRadius = 8f;
                door.openNoiseRadius = 10f;
            });
            if (assets.data == null || assets.door == null)
            {
                Debug.LogError("[TWR] Could not create " + PuzzleDataPath + " / " + DoorDataPath);
                return false;
            }

            // Repair empty prompt links on older copies (D-084).
            if (assets.door.lockedPrompt == null || assets.door.lockedPrompt.IsEmpty)
            {
                assets.door.lockedPrompt = new LocalizedString("UI", "door.locked");
                EditorUtility.SetDirty(assets.door);
            }
            if (assets.door.openPrompt == null || assets.door.openPrompt.IsEmpty)
            {
                assets.door.openPrompt = new LocalizedString("UI", "door.open");
                EditorUtility.SetDirty(assets.door);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        // ---------- white room wall with doorway ----------

        /// The white room's east wall (x = 10.1) with a 2.4 x 3 m doorway into the puzzle room.
        static void BuildEastWallWithDoorway(Material wallMat, Transform room)
        {
            float sideLength = 10f - MainDoorwayHalfWidth;
            float sideCentre = MainDoorwayHalfWidth + sideLength * 0.5f;
            Block("Wall E (south)", new Vector3(10.1f, 2f, -sideCentre), new Vector3(0.2f, 4f, sideLength), wallMat, room);
            Block("Wall E (north)", new Vector3(10.1f, 2f, sideCentre), new Vector3(0.2f, 4f, sideLength), wallMat, room);
            float lintelHeight = 4f - MainDoorwayHeight;
            Block("Wall E (lintel)", new Vector3(10.1f, MainDoorwayHeight + lintelHeight * 0.5f, 0f),
                  new Vector3(0.2f, lintelHeight, MainDoorwayHalfWidth * 2f), wallMat, room);
        }

        // ---------- puzzle room ----------

        /// Returns the exit door's lock (the run's puzzle section adds its SectionExitGate there, D-165).
        static DoorLock BuildPuzzleTest(CombatAssets combat, PuzzleAssets assets, Material floorMat, Material wallMat, Material propMat)
        {
            Material switchMat = GetOrCreateMaterial("M_PuzzleSwitch", new Color(0.55f, 0.05f, 0.04f), new Color(0.3f, 0.03f, 0.02f));
            Material lockMat = GetOrCreateMaterial("M_DoorLockLight", new Color(1f, 0.08f, 0.05f), new Color(1f, 0.08f, 0.05f));
            Material doorMat = GetOrCreateMaterial("M_Door", new Color(0.78f, 0.78f, 0.8f), Color.black);
            Material barMat = GetOrCreateMaterial("M_Bars", new Color(0.3f, 0.3f, 0.32f), Color.black);
            Material darkMat = GetOrCreateMaterial("M_DarkCavity", new Color(0.02f, 0.02f, 0.02f), Color.black);
            Material ammoMat = GetOrCreateMaterial("M_AmmoBox", new Color(0.35f, 0.45f, 0.2f), new Color(0.1f, 0.15f, 0.05f));

            Transform group = new GameObject("Puzzle Test (switches)").transform;
            BuildPuzzleRoomShell(group, floorMat, wallMat);

            // Three pistol-only switches (D-125; D-118 gun filter). List order = sequence order if
            // the PuzzleData is ever switched to Sequence mode.
            var switches = new ShootableSwitch[3];
            switches[0] = BuildSwitch("Switch A (chest height)", "switch.easy", new Vector3(13f, 1.3f, 6f), Vector3.back, propMat, switchMat, group);
            switches[1] = BuildSwitch("Switch B (high wall)", "switch.high", new Vector3(22.2f, HighSwitchHeight, 3f), Vector3.left, propMat, switchMat, group);
            switches[2] = BuildSwitch("Switch C (behind bars)", "switch.caged", new Vector3(17f, 1.2f, -6f), Vector3.forward, propMat, switchMat, group);
            BuildCage(group, propMat, barMat);

            // Ammo on the way in (D-130): the puzzle stays solvable even after emptying the gun.
            AmmoPickup("Ammo Pickup (puzzle)", new Vector3(12.5f, 0f, -4.5f), combat, ammoMat, group);

            // Puzzle-area trigger just inside the doorway (Started, D-126).
            GameObject area = TriggerVolume("Puzzle Area Trigger", new Vector3(11.3f, 0f, 0f), new Vector3(1.8f, 3f, 3f), combat, group);
            var areaTrigger = area.AddComponent<PuzzleAreaTrigger>();

            DoorLock doorLock = BuildExitDoor(group, assets, doorMat, lockMat, darkMat, floorMat, wallMat);

            // The controller owns all state and links switches, trigger and outputs (D-124).
            var controllerGO = new GameObject("Puzzle Controller (test.switches)");
            controllerGO.transform.SetParent(group, false);
            controllerGO.transform.position = new Vector3(16.2f, 0f, 0f);
            var controller = controllerGO.AddComponent<PuzzleController>();
            Assign(controller, "data", assets.data);
            Assign(controller, "startArea", areaTrigger);
            AssignArray(controller, "switches", switches);
            AssignArray(controller, "outputs", new Object[] { doorLock });

            new GameObject("Puzzle Event Logger (test)").AddComponent<PuzzleEventLogger>().transform.SetParent(group, false);
            return doorLock;
        }

        static void BuildPuzzleRoomShell(Transform group, Material floorMat, Material wallMat)
        {
            Transform shell = new GameObject("Puzzle Room Shell").transform;
            shell.SetParent(group, false);
            float h = PuzzleRoomHeight;

            Block("Floor", new Vector3(16.2f, -0.1f, 0f), new Vector3(12.4f, 0.2f, 12.4f), floorMat, shell);
            Block("Ceiling", new Vector3(16.2f, h + 0.1f, 0f), new Vector3(12.4f, 0.2f, 12.4f), wallMat, shell);
            Block("Wall N", new Vector3(16.2f, h * 0.5f, 6.1f), new Vector3(12.4f, h + 0.2f, 0.2f), wallMat, shell);
            Block("Wall S", new Vector3(16.2f, h * 0.5f, -6.1f), new Vector3(12.4f, h + 0.2f, 0.2f), wallMat, shell);
            // Above the white room's 4 m ceiling, the shared west wall continues up to 7 m.
            Block("Wall W (upper)", new Vector3(10.1f, (4f + h + 0.2f) * 0.5f, 0f), new Vector3(0.2f, h + 0.2f - 4f, 12.4f), wallMat, shell);

            // East wall (x = 22.3) with the 1.6 x 2.6 m exit door opening at z = -3.3..-1.7.
            Block("Wall E (south)", new Vector3(22.3f, h * 0.5f, -4.75f), new Vector3(0.2f, h + 0.2f, 2.9f), wallMat, shell);
            Block("Wall E (north)", new Vector3(22.3f, h * 0.5f, 2.25f), new Vector3(0.2f, h + 0.2f, 7.9f), wallMat, shell);
            Block("Wall E (above door)", new Vector3(22.3f, (2.6f + h + 0.2f) * 0.5f, -2.5f), new Vector3(0.2f, h + 0.2f - 2.6f, 1.6f), wallMat, shell);
        }

        /// Switch on a wall: off-white backplate + a red/green button that takes the bullets, plus a small light.
        /// "surface" = point on the wall face, "facing" = direction out of the wall into the room.
        static ShootableSwitch BuildSwitch(string name, string id, Vector3 surface, Vector3 facing,
                                           Material plateMat, Material switchMat, Transform parent)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(surface, Quaternion.LookRotation(facing));

            PuzzlePart("Backplate", root, new Vector3(0f, 0f, 0.025f), new Vector3(0.6f, 0.6f, 0.05f), plateMat);
            GameObject button = PuzzlePart("Button", root, new Vector3(0f, 0f, 0.1f), new Vector3(0.34f, 0.34f, 0.1f), switchMat);
            // Default layer (NOT Interactable): bullets hit it, the F ray finds nothing to use (shoot only).

            var lightGO = new GameObject("Indicator Light");
            lightGO.transform.SetParent(root, false);
            lightGO.transform.localPosition = new Vector3(0f, 0f, 0.45f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 1.5f;
            light.shadows = LightShadows.None;

            var sw = button.AddComponent<ShootableSwitch>();
            var so = new SerializedObject(sw);
            so.FindProperty("switchId").stringValue = id;
            // Developer decision: these switches are pistol-only (D-118 gun-specific path, D-105).
            so.FindProperty("requirement.requireSpecificGun").boolValue = true;
            so.FindProperty("requirement.requiredGun").enumValueIndex = (int)GunType.Pistol;
            so.FindProperty("allowInteract").boolValue = false;
            so.FindProperty("indicatorRenderer").objectReferenceValue = button.GetComponent<Renderer>();
            so.FindProperty("indicatorLight").objectReferenceValue = light;
            so.ApplyModifiedPropertiesWithoutUndo();
            return sw;
        }

        /// Alcove around Switch C: solid side walls and a row of thin bars 2.7 m in front of the switch,
        /// more than the 2.5 m interaction reach. No glass (glass is the revolver's job, D-105).
        static void BuildCage(Transform group, Material sideMat, Material barMat)
        {
            Transform cage = new GameObject("Cage (bars with gaps)").transform;
            cage.SetParent(group, false);
            float h = PuzzleRoomHeight;
            const float left = 15.5f, right = 18.5f, front = -3.3f;

            Block("Cage Side W", new Vector3(left - 0.1f, h * 0.5f, (front - 6f) * 0.5f), new Vector3(0.2f, h, 6f + front), sideMat, cage);
            Block("Cage Side E", new Vector3(right + 0.1f, h * 0.5f, (front - 6f) * 0.5f), new Vector3(0.2f, h, 6f + front), sideMat, cage);

            float pitch = (right - left) / BarCount;
            for (int i = 0; i < BarCount; i++)
            {
                float x = left + pitch * 0.5f + pitch * i;
                Block("Bar " + i, new Vector3(x, h * 0.5f, front), new Vector3(BarThickness, h, BarThickness), barMat, cage);
            }
        }

        /// Sliding exit door in the east wall + lock light + a short corridor behind it (D-127).
        static DoorLock BuildExitDoor(Transform group, PuzzleAssets assets, Material doorMat, Material lockMat,
                                      Material darkMat, Material floorMat, Material wallMat)
        {
            // Root on the floor in the middle of the opening; local forward faces into the puzzle room,
            // so local +X (the slide direction) is world +Z: the panel slides into the wall.
            var doorGO = new GameObject("Exit Door (locked)");
            doorGO.transform.SetParent(group, false);
            doorGO.transform.SetPositionAndRotation(new Vector3(22.3f, 0f, -2.5f), Quaternion.LookRotation(Vector3.left));
            var audio = doorGO.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;

            GameObject panel = PuzzlePart("Panel", doorGO.transform, new Vector3(0f, 1.3f, 0f), new Vector3(1.6f, 2.6f, 0.12f), doorMat);
            panel.layer = assets.interactableLayer; // F finds it; bullets also stop on it like a wall (D-118)
            var body = panel.AddComponent<Rigidbody>(); // kinematic: a moving collider without physics pushes
            body.isKinematic = true;
            body.useGravity = false;

            // Step 4: a closed door muffles noise like a door (0.6, D-131) and blocks Seeker paths.
            // The panel's layer (Interactable) is left out of the NavMesh bake, so a CARVING obstacle cuts the
            // doorway out while closed and moves away with the panel when it opens (D-135: Seekers can't open doors).
            panel.AddComponent<TWR.Core.NoiseOccluder>().Kind = TWR.Core.NoiseOccluderKind.DoorPanel;
            var obstacle = panel.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = Vector3.one; // local units: the panel's scale makes it 1.6 x 2.6 x 0.12 m
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;

            // Lock light above the door on the puzzle side (red = locked, green = open; D-065).
            GameObject lockLight = Block("Door Lock Light", new Vector3(22.17f, 2.85f, -2.5f), new Vector3(0.06f, 0.14f, 0.4f), lockMat, group);
            GameObjectUtility.SetStaticEditorFlags(lockLight, 0);
            Object.DestroyImmediate(lockLight.GetComponent<Collider>());
            var lampGO = new GameObject("Door Lock Lamp");
            lampGO.transform.SetParent(group, false);
            lampGO.transform.position = new Vector3(21.85f, 2.85f, -2.5f);
            var lamp = lampGO.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 2.5f;
            lamp.intensity = 1.2f;
            lamp.shadows = LightShadows.None;

            var doorLock = doorGO.AddComponent<DoorLock>();
            Assign(doorLock, "data", assets.door);
            Assign(doorLock, "lockLight", lockLight.GetComponent<Renderer>());
            Assign(doorLock, "lockLamp", lamp);

            var door = doorGO.AddComponent<Door>();
            Assign(door, "data", assets.door);
            Assign(door, "doorLock", doorLock);
            Assign(door, "panel", panel.transform);
            var so = new SerializedObject(door);
            so.FindProperty("interactionId").stringValue = "door.puzzle_exit";
            so.ApplyModifiedPropertiesWithoutUndo();

            // Small corridor behind the door so it clearly leads somewhere. Where it REALLY leads comes later
            // (D-053b, D-058); for now it ends at a dark opening (D-065: dark cavities = danger / portals).
            Transform corridor = new GameObject("Corridor Behind Door").transform;
            corridor.SetParent(group, false);
            Block("Floor", new Vector3(23.9f, -0.1f, -2.5f), new Vector3(3.2f, 0.2f, 4.2f), floorMat, corridor);
            Block("Ceiling", new Vector3(23.9f, 3.1f, -2.5f), new Vector3(3.2f, 0.2f, 4.2f), wallMat, corridor);
            Block("Wall N", new Vector3(23.9f, 1.5f, -0.4f), new Vector3(3.2f, 3.2f, 0.2f), wallMat, corridor);
            Block("Wall S", new Vector3(23.9f, 1.5f, -4.6f), new Vector3(3.2f, 3.2f, 0.2f), wallMat, corridor);
            Block("Wall E", new Vector3(25.5f, 1.5f, -2.5f), new Vector3(0.2f, 3.2f, 4.4f), wallMat, corridor);
            GameObject cavity = Block("Dark Opening (placeholder)", new Vector3(25.39f, 1.2f, -2.5f), new Vector3(0.02f, 2.2f, 1.2f), darkMat, corridor);
            Object.DestroyImmediate(cavity.GetComponent<Collider>());

            return doorLock;
        }

        /// Cube child in local space, NOT static (it changes colour or moves). Keeps its collider.
        static GameObject PuzzlePart(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // ---------- validation ----------

        /// Logs what every PuzzleController and Door in the open scene is connected to, and an error for
        /// anything empty. Returns false if something the puzzle needs is missing.
        static bool ValidatePuzzleWiring()
        {
            bool ok = true;
            int interactableLayer = LayerMask.NameToLayer(InteractableLayerName);

            foreach (var controller in Object.FindObjectsByType<PuzzleController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(controller);
                var data = so.FindProperty("data").objectReferenceValue as PuzzleData;
                var startArea = so.FindProperty("startArea").objectReferenceValue;
                var report = new System.Text.StringBuilder();
                report.AppendLine($"[TWR] Puzzle wiring: '{controller.name}'");
                report.AppendLine(data != null
                    ? $"  data: {data.name} (id '{data.puzzleId}', optional {data.isOptional}, {data.solveMode}, {data.switchBehaviour})"
                    : "  data: MISSING");
                report.AppendLine($"  start area: {(startArea != null ? startArea.name : "MISSING")}");

                SerializedProperty switches = so.FindProperty("switches");
                report.AppendLine($"  switches ({switches.arraySize}):");
                for (int i = 0; i < switches.arraySize; i++)
                {
                    var sw = switches.GetArrayElementAtIndex(i).objectReferenceValue as ShootableSwitch;
                    if (sw == null) { report.AppendLine($"    [{i}] MISSING"); ok = false; continue; }
                    var swSo = new SerializedObject(sw);
                    bool specific = swSo.FindProperty("requirement.requireSpecificGun").boolValue;
                    string gun = specific ? ((GunType)swSo.FindProperty("requirement.requiredGun").enumValueIndex).ToString() : "any gun";
                    report.AppendLine($"    [{i}] {sw.transform.parent.name} / '{swSo.FindProperty("switchId").stringValue}' ({gun})");
                }

                SerializedProperty outputs = so.FindProperty("outputs");
                report.AppendLine($"  outputs ({outputs.arraySize}):");
                for (int i = 0; i < outputs.arraySize; i++)
                {
                    var output = outputs.GetArrayElementAtIndex(i).objectReferenceValue;
                    report.AppendLine(output != null ? $"    [{i}] {output.name} ({output.GetType().Name})" : $"    [{i}] MISSING");
                    if (output == null) ok = false;
                }

                if (data == null || startArea == null || switches.arraySize == 0 || outputs.arraySize == 0) ok = false;
                if (ok) Debug.Log(report.ToString(), controller);
                else Debug.LogError(report.ToString(), controller);
            }

            foreach (var door in Object.FindObjectsByType<Door>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(door);
                var data = so.FindProperty("data").objectReferenceValue as DoorData;
                var doorLock = so.FindProperty("doorLock").objectReferenceValue;
                var panel = so.FindProperty("panel").objectReferenceValue;
                string Key(LocalizedString s) => s == null || s.IsEmpty ? "EMPTY" : $"{s.TableReference.TableCollectionName}/{s.TableEntryReference.Key}";
                var panelGO = (panel as Transform)?.gameObject;
                bool doorOk = data != null && doorLock != null && panelGO != null && panelGO.layer == interactableLayer;
                string lockMode = "";
                if (doorLock != null)
                {
                    var lockSo = new SerializedObject(doorLock);
                    SerializedProperty permanent = lockSo.FindProperty("permanentlyLocked");
                    lockMode = permanent != null && permanent.boolValue ? " (PERMANENTLY locked, D-145)"
                             : lockSo.FindProperty("startsLocked").boolValue ? " (locked until its puzzle is solved)" : " (unlocked)";
                }
                string line = $"[TWR] Door wiring: '{door.name}'  data: {(data != null ? data.name : "MISSING")}, " +
                              $"lock: {(doorLock != null ? doorLock.name + lockMode : "MISSING")}, panel: {(panel != null ? panel.name : "MISSING")}, " +
                              $"panel layer: {(panelGO != null ? LayerMask.LayerToName(panelGO.layer) : "-")}" +
                              (data != null ? $", prompts: locked={Key(data.lockedPrompt)} open={Key(data.openPrompt)}" : "");
                if (doorOk) Debug.Log(line, door);
                else { Debug.LogError(line, door); ok = false; }
            }
            return ok;
        }

        /// Reads back the SAVED scene's dependency list: if an asset the scene must reference is not in it,
        /// the reference was lost on save (the bug that left PuzzleData/DoorData empty).
        static bool ValidateSavedSceneReferences(params string[] requiredAssetPaths)
        {
            var dependencies = new System.Collections.Generic.HashSet<string>(AssetDatabase.GetDependencies(ScenePath, true));
            bool ok = true;
            foreach (string path in requiredAssetPaths)
            {
                if (dependencies.Contains(path)) continue;
                Debug.LogError($"[TWR] The saved scene does not reference {path}. Its field was saved as None.");
                ok = false;
            }
            return ok;
        }

        static void AssignArray(Object target, string fieldName, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null || !property.isArray)
            {
                Debug.LogError($"[TWR] Array field '{fieldName}' not found on {target.GetType().Name}. Assign it by hand.", target);
                return;
            }
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
