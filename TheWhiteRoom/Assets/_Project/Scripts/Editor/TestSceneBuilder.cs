using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TWR.Core;
using TWR.Interaction;
using TWR.Player;
using TWR.UI;

namespace TWR.EditorTools
{
    /// <summary>
    /// Editor menu "TWR/Build Test Scene": builds the white player-controller test scene (D-107 step 1)
    /// from code so nobody hand-edits scene files. Creates (only if missing) the Interactable layer,
    /// white emissive materials (D-064), a Bloom volume profile, the PlayerTuning asset and the Player
    /// prefab, then lays out a cheap greybox room (D-100) to test every move, lean, crouch, interact and noise.
    /// Step 2 (pistol, D-107): also the Weapon layer, gun-on-top rendering, Ammo item + Pistol GunData assets,
    /// and it UPGRADES an existing Player prefab with health, inventory, weapons and the placeholder pistol
    /// (see TestSceneBuilder.Combat.cs). "TWR/Rebuild Player Prefab" overwrites the prefab from scratch.
    /// Step 3 (switch puzzle, D-107): a puzzle room east of the white room with three pistol-only switches
    /// and a locked exit door (see TestSceneBuilder.Puzzle.cs; D-124 to D-130).
    /// Step 4 (Seeker, D-107; D-043): Player + Enemy layers, NoiseSettings (wall muffling), the Seeker prefab,
    /// a patrolling Seeker spawn point in the puzzle room, RunStats and a baked + saved NavMesh
    /// (see TestSceneBuilder.Seeker.cs; D-132 confirmed, D-131 and D-133 to D-138).
    /// Step 5 (hub lobby + portal door, D-107; D-057, D-097): Portal / PortalFallback layers, the Portal renderer,
    /// portal shader/materials/settings, a lobby south of the white room with five doors (player starts there),
    /// the see-through portal door and the impossible hall 500 m away (see TestSceneBuilder.Portal.cs;
    /// proposed D-139 to D-147).
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/Test_PlayerController.unity";
        const string PrefabPath = Root + "/Prefabs/Player.prefab";
        const string TuningPath = Root + "/Data/Player/PlayerTuning_Default.asset";
        const string InputPath = Root + "/Settings/Input/TWR_Controls.inputactions";
        const string VolumeProfilePath = Root + "/Settings/Rendering/VP_WhiteRoom.asset";
        const string MaterialFolder = Root + "/Art/Materials";
        const string InteractableLayerName = "Interactable";
        const string WeaponLayerName = "Weapon";

        [MenuItem("TWR/Build Test Scene")]
        public static void BuildTestScene()
        {
            // Project setup first (TMP Essentials, localization). If TMP has to be imported,
            // the scene is built once that import finishes.
            ProjectSetup.Run(BuildTestSceneAfterSetup);
        }

        static void BuildTestSceneAfterSetup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            // New scene FIRST, assets SECOND. NewScene unloads every asset the old scene didn't use, so an
            // asset loaded or created before it (e.g. a fresh PuzzleData) can come back as a dead reference
            // and be saved into the scene as "None". Loading after NewScene keeps every reference valid.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!PrepareSharedAssets(out int interactableLayer, out PlayerTuning tuning, out InputActionAsset input))
                return;
            if (!PrepareCombatAssets(out CombatAssets combat))
                return;
            if (!PreparePuzzleAssets(out PuzzleAssets puzzle))
                return;
            if (!PrepareSeekerAssets(out SeekerAssets seeker)) // SeekerData, sound set, NoiseSettings, Seeker prefab
                return;
            if (!PreparePortalAssets(out PortalAssets portal)) // PortalSettings, shader + materials, Portal renderer, swing DoorData
                return;

            Material white = GetOrCreateMaterial("M_White", new Color(0.95f, 0.95f, 0.95f), Color.black);
            Material whiteEmissive = GetOrCreateMaterial("M_WhiteEmissive", Color.white, Color.white * 1.6f);
            Material offWhite = GetOrCreateMaterial("M_OffWhite", new Color(0.85f, 0.85f, 0.83f), Color.black);
            Material buttonMat = GetOrCreateMaterial("M_TestButton", new Color(1f, 0.2f, 0.2f), new Color(0.5f, 0.1f, 0.1f));
            Material listenerMat = GetOrCreateMaterial("M_NoiseListener", new Color(0.6f, 0.6f, 0.6f), new Color(0.2f, 0.2f, 0.2f));
            VolumeProfile profile = GetOrCreateVolumeProfile();

            // Lighting: flat bright ambient + one soft, shadowless light for shape readability (cheap, D-100).
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.75f, 0.75f, 0.78f);
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.6f;
            light.shadows = LightShadows.None;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var volumeGO = new GameObject("Global Volume (Bloom)");
            var volume = volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            BuildRoom(white, whiteEmissive, offWhite);

            // Interactable test button on a pedestal.
            Block("Button Pedestal", new Vector3(-3f, 0.5f, 5f), new Vector3(0.6f, 1f, 0.6f), offWhite, null);
            GameObject button = Block("Test Button", new Vector3(-3f, 1.15f, 5f), new Vector3(0.3f, 0.3f, 0.3f), buttonMat, null);
            button.layer = interactableLayer;
            GameObjectUtility.SetStaticEditorFlags(button, 0);
            button.AddComponent<TestButtonInteractable>();

            // Noise test listener (flashes + logs when it hears something).
            var listenerGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            listenerGO.name = "Noise Test Listener";
            listenerGO.transform.position = new Vector3(6f, 1f, 4f);
            listenerGO.transform.localScale = Vector3.one * 0.6f;
            listenerGO.GetComponent<MeshRenderer>().sharedMaterial = listenerMat;
            listenerGO.AddComponent<NoiseTestListener>();

            new GameObject("Noise Debug Drawer").AddComponent<NoiseDebugDrawer>();

            // Shooting targets, ammo pickups, damage zone (step 2).
            BuildCombatTestProps(combat);

            // Pistol switch puzzle room east of the white room (step 3).
            BuildPuzzleTest(combat, puzzle, white, whiteEmissive, offWhite);

            // Seeker spawn point + patrol route, muffling test listeners/marks, RunStats (step 4).
            BuildSeekerTest(seeker);

            // Hub lobby with five doors, the portal door and the impossible hall 500 m away (step 5).
            BuildLobbyAndPortal(portal, puzzle, white, offWhite);

            // Player: starts in the lobby in front of the black entry door, facing into the lobby (proposed D-145).
            GameObject player = GetOrCreatePlayerInstance(tuning, input, interactableLayer, combat, false);
            player.transform.SetPositionAndRotation(PlayerStartPosition, Quaternion.identity);

            GameObject hud = BuildHud(player.GetComponent<PlayerInteractor>());
            BuildCombatHud(hud, player);
            BuildPortalRuntime(portal, player); // PortalRenderManager (needs the player camera)

            // Last, once all geometry exists: bake the NavMesh and save it as an asset next to the scene.
            BakeAndSaveNavMesh();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Selection.activeGameObject = player;

            bool wiringOk = ValidatePuzzleWiring();
            wiringOk &= ValidateSeekerSetup();
            wiringOk &= ValidatePortalSetup(portal, player, tuning);
            bool savedOk = ValidateSavedSceneReferences(TuningPath, PuzzleDataPath, DoorDataPath,
                SeekerPrefabPath, SeekerDataPath, SeekerSoundSetPath, NavMeshAssetPath,
                PortalSettingsPath, PortalSurfaceMaterialPath, PortalFallbackMaterialPath, PortalShaderPath,
                PortalDoorDataPath, PortalRenderSetup.RendererPath);
            if (wiringOk && savedOk)
                Debug.Log("[TWR] Built " + ScenePath + ". Press Play to test.");
            else
                Debug.LogError("[TWR] Built " + ScenePath + " but some references are missing (see errors above).");
        }

        [MenuItem("TWR/Rebuild Player Prefab")]
        public static void RebuildPlayerPrefab()
        {
            if (!PrepareSharedAssets(out int interactableLayer, out PlayerTuning tuning, out InputActionAsset input))
                return;
            if (!PrepareCombatAssets(out CombatAssets combat))
                return;
            if (!EditorUtility.DisplayDialog("Rebuild Player Prefab",
                    "This overwrites " + PrefabPath + " (your changes to it will be lost). Continue?", "Rebuild", "Cancel"))
                return;

            GameObject temp = CreatePlayerHierarchy(tuning, input, interactableLayer);
            EnsurePlayerCombat(temp, combat, tuning, input);
            AssignSharedAssets(temp, tuning, input);
            ValidateSharedAssets(temp);
            PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);
            Debug.Log("[TWR] Rebuilt " + PrefabPath);
        }

        // ---------- shared assets ----------

        internal static bool PrepareSharedAssets(out int interactableLayer, out PlayerTuning tuning, out InputActionAsset input)
        {
            tuning = null;
            input = null;
            interactableLayer = -1;

            if (AssetDatabase.FindAssets("t:TMP_Settings").Length == 0)
            {
                EditorUtility.DisplayDialog("TextMeshPro resources missing",
                    "Import TMP Essentials first:\nWindow > TextMeshPro > Import TMP Essential Resources.\nThen run this menu again.", "OK");
                return false;
            }

            input = FindInputActions();
            if (input == null)
            {
                EditorUtility.DisplayDialog("Input actions missing",
                    "Could not find TWR_Controls.inputactions (expected at " + InputPath + ").", "OK");
                return false;
            }

            interactableLayer = EnsureLayer(InteractableLayerName);
            int weaponLayer = EnsureLayer(WeaponLayerName);
            int playerLayer = EnsureLayer(PlayerLayerName); // player's capsule: never a wall for noise/sight (D-131)
            int enemyLayer = EnsureLayer(EnemyLayerName);   // Seeker body: shootable, never a wall (D-134)
            int portalLayer = EnsureLayer(PortalLayerName);           // live portal screens: player camera only (proposed D-140)
            int portalFallbackLayer = EnsureLayer(PortalFallbackLayerName); // white stand-ins: portal cameras only
            if (interactableLayer < 0 || weaponLayer < 0 || playerLayer < 0 || enemyLayer < 0 || portalLayer < 0 || portalFallbackLayer < 0)
            {
                EditorUtility.DisplayDialog("No free layer", "Could not create the 'Interactable' / 'Weapon' / 'Player' / 'Enemy' / 'Portal' / 'PortalFallback' layers: all user layers are used.", "OK");
                return false;
            }

            // Draw the held gun on top of walls without an overlay camera (D-123).
            WeaponRenderSetup.Ensure(weaponLayer);

            EnsureFolder(Root + "/Data/Player");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(MaterialFolder);
            EnsureFolder(Root + "/Settings/Rendering");

            tuning = FindOrCreateTuning();
            if (tuning == null)
            {
                EditorUtility.DisplayDialog("PlayerTuning missing", "Could not create " + TuningPath, "OK");
                return false;
            }

            // Wall-muffling settings, found by NoiseSystem through Resources (D-131).
            EnsureNoiseSettings();
            return true;
        }

        /// The project's input actions: the expected path first, then any asset named TWR_Controls.
        static InputActionAsset FindInputActions()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            if (asset != null) return asset;

            AssetDatabase.ImportAsset(InputPath, ImportAssetOptions.ForceSynchronousImport);
            asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            if (asset != null) return asset;

            foreach (string guid in AssetDatabase.FindAssets("TWR_Controls t:InputActionAsset"))
            {
                asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) return asset;
            }
            return null;
        }

        /// The default PlayerTuning: the expected path, then any PlayerTuning in the project, else a new one.
        /// Always returns the copy loaded from disk so prefab references to it survive saving.
        static PlayerTuning FindOrCreateTuning()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<PlayerTuning>(TuningPath);
            if (tuning != null) return tuning;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(PlayerTuning));
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<PlayerTuning>(AssetDatabase.GUIDToAssetPath(guids[0]));

            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<PlayerTuning>(), TuningPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(TuningPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<PlayerTuning>(TuningPath);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++) // 0-7 are Unity's / reserved
            {
                SerializedProperty element = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(element.stringValue))
                {
                    element.stringValue = layerName;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[TWR] Created layer '" + layerName + "' at index " + i);
                    return i;
                }
            }
            return -1;
        }

        static Material GetOrCreateMaterial(string name, Color baseColour, Color emission)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(lit);
            material.SetColor("_BaseColor", baseColour);
            material.SetFloat("_Smoothness", 0.15f);
            if (emission.maxColorComponent > 0f)
            {
                // Emissive white walls + Bloom = the overexposed White Room look (D-064).
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static VolumeProfile GetOrCreateVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(1.2f);
            bloom.scatter.Override(0.75f);
            AddComponentToProfileAsset(bloom, profile);

            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
            AddComponentToProfileAsset(tonemapping, profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static void AddComponentToProfileAsset(VolumeComponent component, VolumeProfile profile)
        {
            component.name = component.GetType().Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        // ---------- room ----------

        static void BuildRoom(Material floorMat, Material wallMat, Material propMat)
        {
            Transform room = new GameObject("Room").transform;

            // Shell: 20 x 20 m, 4 m high, glowing walls and ceiling.
            Block("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f), floorMat, room);
            Block("Ceiling", new Vector3(0f, 4.1f, 0f), new Vector3(20f, 0.2f, 20f), wallMat, room);
            Block("Wall N", new Vector3(0f, 2f, 10.1f), new Vector3(20.4f, 4f, 0.2f), wallMat, room);
            BuildSouthWallWithDoorway(wallMat, room); // doorway into the step-5 lobby (TestSceneBuilder.Portal.cs)
            BuildEastWallWithDoorway(wallMat, room); // doorway into the step-3 puzzle room (TestSceneBuilder.Puzzle.cs)
            Block("Wall W", new Vector3(-10.1f, 2f, 0f), new Vector3(0.2f, 4f, 20f), wallMat, room);

            // Crouch tunnel: ceiling at 1.3 m (standing needs 1.8 m) -> must crouch, can't stand up inside.
            Transform tunnel = new GameObject("Crouch Tunnel (1.3 m)").transform;
            tunnel.SetParent(room, false);
            Block("Tunnel Side L", new Vector3(-6.85f, 0.65f, 0f), new Vector3(0.2f, 1.3f, 4f), propMat, tunnel);
            Block("Tunnel Side R", new Vector3(-5.15f, 0.65f, 0f), new Vector3(0.2f, 1.3f, 4f), propMat, tunnel);
            Block("Tunnel Ceiling", new Vector3(-6f, 1.4f, 0f), new Vector3(1.9f, 0.2f, 4f), propMat, tunnel);

            // Step (0.4 m, above step offset, so jump onto it) and ledge (0.9 m, needs a full jump).
            Block("Step 0.4m", new Vector3(4f, 0.2f, -4f), new Vector3(2f, 0.4f, 2f), propMat, room);
            Block("Ledge 0.9m", new Vector3(6f, 0.45f, -4f), new Vector3(2f, 0.9f, 2f), propMat, room);

            // Cover blocks to lean around.
            Block("Cover A", new Vector3(0f, 1.1f, 4f), new Vector3(1f, 2.2f, 1f), propMat, room);
            Block("Cover B", new Vector3(3f, 1.1f, 6f), new Vector3(1.2f, 2.2f, 1.2f), propMat, room);
        }

        static GameObject Block(string name, Vector3 position, Vector3 scale, Material material, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        // ---------- player ----------

        static GameObject GetOrCreatePlayerInstance(PlayerTuning tuning, InputActionAsset input, int interactableLayer,
                                                    CombatAssets combat, bool forceRebuild)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject root;
            if (prefab != null && !forceRebuild)
            {
                // Upgrade the existing prefab in place (adds step-2 parts only if missing), then place it.
                UpgradePlayerPrefab(combat, tuning, input);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }
            else
            {
                root = CreatePlayerHierarchy(tuning, input, interactableLayer);
                EnsurePlayerCombat(root, combat, tuning, input);
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            }

            // An existing prefab may have lost its asset references, so wire them every build
            // and write the fix back into the prefab.
            if (AssignSharedAssets(root, tuning, input) > 0)
                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
            ValidateSharedAssets(root);
            return root;
        }

        /// Fills every empty "tuning" (PlayerTuning) and "actions" (InputActionAsset) field on the player
        /// and its children. Returns how many fields were filled.
        static int AssignSharedAssets(GameObject player, PlayerTuning tuning, InputActionAsset input)
        {
            int filled = 0;
            foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue; // missing script
                var so = new SerializedObject(behaviour);
                filled += FillIfEmpty(so, "tuning", tuning);
                if (behaviour is PlayerInputReader)
                    filled += FillIfEmpty(so, "actions", input);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return filled;
        }

        static int FillIfEmpty(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) return 0;
            if (property.objectReferenceValue != null) return 0;
            property.objectReferenceValue = value;
            return 1;
        }

        static void ValidateSharedAssets(GameObject player)
        {
            foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var so = new SerializedObject(behaviour);
                foreach (string fieldName in new[] { "tuning", "actions" })
                {
                    SerializedProperty property = so.FindProperty(fieldName);
                    if (property != null && property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue == null)
                        Debug.LogError($"[TWR] {behaviour.GetType().Name}.{fieldName} is still empty. Try TWR > Rebuild Player Prefab.", behaviour);
                }
            }
        }

        static GameObject CreatePlayerHierarchy(PlayerTuning tuning, InputActionAsset input, int interactableLayer)
        {
            var root = new GameObject("Player");
            root.tag = "Player";

            var cc = root.AddComponent<CharacterController>();
            cc.height = tuning.standingHeight;
            cc.radius = tuning.controllerRadius;
            cc.center = new Vector3(0f, tuning.standingHeight * 0.5f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.04f;

            var reader = root.AddComponent<PlayerInputReader>();
            root.AddComponent<CharacterControllerMotor>(); // the only CharacterController user (D-053c swap point)
            var locomotion = root.AddComponent<PlayerLocomotion>();
            var look = root.AddComponent<PlayerLook>();
            var lean = root.AddComponent<PlayerLean>();
            var interactor = root.AddComponent<PlayerInteractor>();
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            var sounds = root.AddComponent<PlayerMovementSounds>();
            var controller = root.AddComponent<PlayerController>();

            // Player > Head (eye height + pitch) > LeanPivot (lean offset + roll) > PlayerCamera (head bob).
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, tuning.standingEyeHeight, 0f);

            var leanPivot = new GameObject("LeanPivot").transform;
            leanPivot.SetParent(head, false);

            var cameraGO = new GameObject("PlayerCamera");
            cameraGO.tag = "MainCamera";
            cameraGO.transform.SetParent(leanPivot, false);
            var cam = cameraGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.fieldOfView = tuning.fieldOfView;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cameraGO.AddComponent<AudioListener>();
            var cameraData = cam.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true; // needed for Bloom (D-064)
            cameraData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.FastApproximateAntialiasing;
            var headBob = cameraGO.AddComponent<PlayerHeadBob>();

            Assign(reader, "actions", input);
            Assign(locomotion, "tuning", tuning); Assign(locomotion, "input", reader); Assign(locomotion, "head", head);
            Assign(look, "tuning", tuning); Assign(look, "input", reader); Assign(look, "head", head); Assign(look, "playerCamera", cam);
            Assign(lean, "tuning", tuning); Assign(lean, "input", reader); Assign(lean, "locomotion", locomotion);
            Assign(lean, "head", head); Assign(lean, "leanPivot", leanPivot);
            Assign(interactor, "tuning", tuning); Assign(interactor, "input", reader); Assign(interactor, "playerCamera", cam);
            AssignLayerMask(interactor, "interactableMask", 1 << interactableLayer);
            Assign(sounds, "tuning", tuning); Assign(sounds, "locomotion", locomotion);
            Assign(headBob, "tuning", tuning); Assign(headBob, "locomotion", locomotion);
            Assign(controller, "playerCamera", cam);

            return root;
        }

        // ---------- HUD ----------

        static GameObject BuildHud(PlayerInteractor interactor)
        {
            var canvasGO = new GameObject("HUD");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Tiny centre dot so you know where the interaction ray points.
            var dotGO = new GameObject("Crosshair Dot", typeof(RectTransform));
            dotGO.transform.SetParent(canvasGO.transform, false);
            var dot = dotGO.AddComponent<Image>();
            dot.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            dot.raycastTarget = false;
            ((RectTransform)dotGO.transform).sizeDelta = new Vector2(6f, 6f);

            // Prompt label (text comes from localization at runtime, D-084).
            var labelGO = new GameObject("Interaction Prompt", typeof(RectTransform));
            labelGO.transform.SetParent(canvasGO.transform, false);
            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text = string.Empty;
            label.fontSize = 32f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.12f, 0.12f, 0.12f);
            label.raycastTarget = false;
            var labelRect = (RectTransform)labelGO.transform;
            labelRect.sizeDelta = new Vector2(900f, 60f);
            labelRect.anchoredPosition = new Vector2(0f, -90f);

            var prompt = canvasGO.AddComponent<InteractionPromptUI>();
            Assign(prompt, "interactor", interactor);
            Assign(prompt, "label", label);
            return canvasGO;
        }

        // ---------- helpers ----------

        static void Assign(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[TWR] Field '{fieldName}' not found on {target.GetType().Name}. Assign it by hand.", target);
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AssignLayerMask(Object target, string fieldName, int mask)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[TWR] Field '{fieldName}' not found on {target.GetType().Name}.", target);
                return;
            }
            property.intValue = mask;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
