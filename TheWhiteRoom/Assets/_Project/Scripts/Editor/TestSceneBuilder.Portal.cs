using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Localization;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TWR.Audio;
using TWR.Combat;
using TWR.Core;
using TWR.Items;
using TWR.Player;
using TWR.Portals;
using TWR.Puzzles;

namespace TWR.EditorTools
{
    /// <summary>
    /// Step 5 (hub lobby + see-through portal door, D-107; D-057, D-097) part of "TWR/Build Test Scene".
    /// Implements decisions D-139 to D-147:
    /// - assets: PortalSettings (D-140), the portal screen shader + materials, the "Portal" URP renderer (D-139),
    ///   a swing DoorData for the portal door, the Portal / PortalFallback layers;
    /// - a bright lobby south of the white room (D-145; D-010, D-021, D-057, D-062, D-064) with five doors: the
    ///   black entry door (permanently locked), an open doorway to the white room, the portal door, and two
    ///   permanently locked path doors reserved for D-060;
    /// - the portal door: a freestanding frame + short wall with a swing panel (D-146), paired with a return
    ///   portal in a 30 x 20 x 8 m white hall 500 m away on X (D-144, D-054), with one shootable test target;
    /// - a 2D ambient hum (D-144, D-077), the PortalRenderManager, and the PortalTraveller on the Player;
    /// - [TWR] validation logs for every link.
    /// Like steps 2 to 4, every asset is loaded/created AFTER EditorSceneManager.NewScene.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string PortalSettingsPath = Root + "/Data/Portals/PortalSettings.asset";
        const string PortalShaderPath = Root + "/Art/Shaders/PortalSurface.shader";
        const string PortalSurfaceMaterialPath = MaterialFolder + "/M_PortalSurface.mat";
        const string PortalFallbackMaterialPath = MaterialFolder + "/M_PortalFallback.mat";
        const string PortalDoorDataPath = Root + "/Data/Puzzles/Door_PortalSwing.asset";
        const string PortalLayerName = GameLayers.Portal;
        const string PortalFallbackLayerName = GameLayers.PortalFallback;

        // Opening of both portals (D-142: at least 1.2 x 2.4 m). 3 m tall so a jump through doesn't bonk.
        const float OpeningWidth = 1.4f;
        const float OpeningHeight = 3f;

        // Lobby interior: x -8..8, z -24.2..-10.0, 5 m high (taller than the white room: "annoyingly bright", D-064).
        const float LobbyMinX = -8f, LobbyMaxX = 8f, LobbyMinZ = -24.2f, LobbyMaxZ = -10f, LobbyHeight = 5f;
        const float LobbyDoorwayHalfWidth = 1.2f, LobbyDoorwayHeight = 3f;

        // Lobby portal: freestanding frame, front facing south (towards the player start). Hall portal: in the hall's
        // west wall, front facing east into the hall. The pair turns the view by 90 degrees (tests the yaw maths).
        static readonly Vector3 LobbyPortalPosition = new Vector3(3.5f, 0f, -16f);
        const float LobbyPortalYaw = 180f;
        static readonly Vector3 HallPortalPosition = new Vector3(500f, 0f, -16f);
        const float HallPortalYaw = 90f;

        // Hall interior: x 500..530, z -26..-6, 8 m high (bigger than the 6 m of lobby behind the frame, D-054).
        const float HallMinX = 500f, HallMaxX = 530f, HallMinZ = -26f, HallMaxZ = -6f, HallHeight = 8f;

        static readonly Vector3 PlayerStartPosition = new Vector3(0f, 0.05f, -22.8f); // in front of the black door, facing north
        static readonly Vector3 LobbyProbe = new Vector3(-2f, 0f, -19f);
        static readonly Vector3 HallProbe = new Vector3(515f, 0f, -10f);

        const float MinPortalSeparation = 200f;  // D-144
        const float MinVestibuleDepth = 1f;      // D-142
        const float PickupPortalClearance = 1.5f; // D-143

        struct PortalAssets
        {
            public PortalSettings settings;
            public Shader shader;
            public Material surface;
            public Material fallback;
            public DoorData swingDoor;
            public UniversalRendererData renderer;
            public int rendererIndex;
            public int portalLayer;
            public int fallbackLayer;
        }

        // ---------- assets ----------

        static bool PreparePortalAssets(out PortalAssets assets)
        {
            assets = default;
            assets.portalLayer = LayerMask.NameToLayer(PortalLayerName);
            assets.fallbackLayer = LayerMask.NameToLayer(PortalFallbackLayerName);
            if (assets.portalLayer < 0 || assets.fallbackLayer < 0)
            {
                Debug.LogError("[TWR] 'Portal' or 'PortalFallback' layer missing. Run TWR > Setup Project.");
                return false;
            }

            EnsureFolder(Root + "/Data/Portals");
            EnsureFolder(Root + "/Art/Shaders");

            assets.renderer = PortalRenderSetup.Ensure();
            assets.rendererIndex = PortalRenderSetup.IndexInActivePipeline(assets.renderer);

            assets.shader = AssetDatabase.LoadAssetAtPath<Shader>(PortalShaderPath);
            if (assets.shader == null)
            {
                AssetDatabase.ImportAsset(PortalShaderPath, ImportAssetOptions.ForceSynchronousImport);
                assets.shader = AssetDatabase.LoadAssetAtPath<Shader>(PortalShaderPath);
            }
            if (assets.shader == null)
            {
                Debug.LogError("[TWR] Portal shader missing at " + PortalShaderPath + ". Pull the latest files and try again.");
                return false;
            }

            assets.surface = AssetDatabase.LoadAssetAtPath<Material>(PortalSurfaceMaterialPath);
            if (assets.surface == null)
            {
                AssetDatabase.CreateAsset(new Material(assets.shader), PortalSurfaceMaterialPath);
                assets.surface = AssetDatabase.LoadAssetAtPath<Material>(PortalSurfaceMaterialPath);
                Debug.Log("[TWR] Created " + PortalSurfaceMaterialPath);
            }
            else if (assets.surface.shader != assets.shader)
            {
                assets.surface.shader = assets.shader;
                EditorUtility.SetDirty(assets.surface);
            }
            // Flat emissive white for portals seen inside a portal view and over-budget portals (D-140).
            assets.fallback = GetOrCreateUnlitMaterial("M_PortalFallback", new Color(2.2f, 2.2f, 2.2f, 1f));

            assets.settings = LoadOrCreate<PortalSettings>(PortalSettingsPath, null);
            assets.swingDoor = LoadOrCreate<DoorData>(PortalDoorDataPath, door =>
            {
                door.openStyle = DoorOpenStyle.Swing;
                door.swingAngle = 95f;     // swings towards the lobby side (the portal's front, D-146)
                door.openSeconds = 1.1f;
                door.unlockNoiseRadius = 8f;
                door.openNoiseRadius = 10f; // D-128
            });
            if (assets.settings == null || assets.swingDoor == null)
            {
                Debug.LogError("[TWR] Could not create " + PortalSettingsPath + " / " + PortalDoorDataPath);
                return false;
            }

            if (assets.settings.portalRenderer != assets.renderer)
            {
                assets.settings.portalRenderer = assets.renderer;
                EditorUtility.SetDirty(assets.settings);
            }
            if (assets.swingDoor.lockedPrompt == null || assets.swingDoor.lockedPrompt.IsEmpty)
            {
                assets.swingDoor.lockedPrompt = new LocalizedString("UI", "door.locked");
                EditorUtility.SetDirty(assets.swingDoor);
            }
            if (assets.swingDoor.openPrompt == null || assets.swingDoor.openPrompt.IsEmpty)
            {
                assets.swingDoor.openPrompt = new LocalizedString("UI", "door.open");
                EditorUtility.SetDirty(assets.swingDoor);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        // ---------- player (called from EnsurePlayerCombat, so every prefab path gets it) ----------

        /// The player camera never draws the white portal stand-ins; the PortalTraveller watches the un-leaned Head.
        static void EnsurePlayerPortalParts(GameObject root, Camera cam)
        {
            int fallbackLayer = LayerMask.NameToLayer(PortalFallbackLayerName);
            if (fallbackLayer >= 0) cam.cullingMask &= ~(1 << fallbackLayer);

            var traveller = GetOrAdd<PortalTraveller>(root);
            Assign(traveller, "controller", root.GetComponent<PlayerController>());
            Assign(traveller, "pivot", root.transform.Find("Head"));
        }

        // ---------- white room south wall (doorway into the lobby) ----------

        /// The white room's south wall (z = -10.1) with a 2.4 x 3 m doorway: the lobby's "ordinary way through".
        static void BuildSouthWallWithDoorway(Material wallMat, Transform room)
        {
            float sideLength = 10.2f - LobbyDoorwayHalfWidth;
            float sideCentre = LobbyDoorwayHalfWidth + sideLength * 0.5f;
            Block("Wall S (west)", new Vector3(-sideCentre, 2f, -10.1f), new Vector3(sideLength, 4f, 0.2f), wallMat, room);
            Block("Wall S (east)", new Vector3(sideCentre, 2f, -10.1f), new Vector3(sideLength, 4f, 0.2f), wallMat, room);
            float lintel = 4f - LobbyDoorwayHeight;
            Block("Wall S (lintel)", new Vector3(0f, LobbyDoorwayHeight + lintel * 0.5f, -10.1f),
                  new Vector3(LobbyDoorwayHalfWidth * 2f, lintel, 0.2f), wallMat, room);
        }

        // ---------- lobby ----------

        static void BuildLobbyAndPortal(PortalAssets assets, PuzzleAssets puzzle, Material white, Material offWhite)
        {
            Material glow = GetOrCreateMaterial("M_LobbyGlow", Color.white, Color.white * 2.4f); // overexposed white (D-064)
            Material blackDoor = GetOrCreateMaterial("M_BlackDoor", new Color(0.015f, 0.015f, 0.015f), Color.black);
            Material pathDoor = GetOrCreateMaterial("M_Door", new Color(0.78f, 0.78f, 0.8f), Color.black);
            Material lockMat = GetOrCreateMaterial("M_DoorLockLight", new Color(1f, 0.08f, 0.05f), new Color(1f, 0.08f, 0.05f));
            Material plant = GetOrCreateMaterial("M_PlantGreen", new Color(0.35f, 0.55f, 0.35f), Color.black);
            Material wood = GetOrCreateMaterial("M_PaleWood", new Color(0.75f, 0.68f, 0.58f), Color.black);
            Material targetMat = GetOrCreateMaterial("M_ShootTarget", new Color(0.2f, 0.5f, 1f), new Color(0.1f, 0.25f, 0.5f));

            Transform lobby = new GameObject("Lobby (hub, D-057)").transform;
            BuildLobbyShell(lobby, white, glow, offWhite);

            // Door 1: the black entry door (D-021), permanently locked. The player starts right in front of it.
            BuildLockedDoor("Door 1 - Black Entry Door (locked)", "door.black_entry", new Vector3(0f, 0f, LobbyMinZ), Vector3.forward,
                            new Vector2(1.0f, 2.2f), blackDoor, offWhite, null, puzzle, lobby);
            // Door 2: the ordinary way through is the open doorway in the north wall (BuildSouthWallWithDoorway).
            // Door 4 and 5: reserved for the D-060 path doors (escape / discover), permanently locked for now.
            BuildLockedDoor("Door 4 - Path Door A (reserved D-060, locked)", "door.path_a", new Vector3(LobbyMinX, 0f, -18f), Vector3.right,
                            new Vector2(1.2f, 2.4f), pathDoor, offWhite, lockMat, puzzle, lobby);
            BuildLockedDoor("Door 5 - Path Door B (reserved D-060, locked)", "door.path_b", new Vector3(LobbyMaxX, 0f, -19.5f), Vector3.left,
                            new Vector2(1.2f, 2.4f), pathDoor, offWhite, lockMat, puzzle, lobby);

            BuildLobbyProps(lobby, offWhite, wood, plant);

            // Door 3: the portal door + the impossible hall it shows.
            Portal lobbyPortal = BuildLobbyPortalDoor(assets, offWhite, glow, lobby);
            Transform hall = new GameObject("Impossible Hall (+500 m X, D-054)").transform;
            Portal hallPortal = BuildHall(assets, hall, white, offWhite, wood, plant, targetMat);

            // Two-way pair (D-146, D-147): each side's destination is the other one.
            Assign(lobbyPortal, "destination", hallPortal);
            Assign(hallPortal, "destination", lobbyPortal);

            // Non-spatial ambient hum over both spaces (D-144, D-077): no audio pop when crossing.
            var ambientGO = new GameObject("Ambient Bed (2D hum)");
            var source = ambientGO.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = true;
            ambientGO.AddComponent<AmbientBed>();
        }

        static void BuildLobbyShell(Transform lobby, Material floorMat, Material glowMat, Material trimMat)
        {
            Transform shell = new GameObject("Lobby Shell").transform;
            shell.SetParent(lobby, false);
            float h = LobbyHeight;
            float cx = (LobbyMinX + LobbyMaxX) * 0.5f, cz = (LobbyMinZ + LobbyMaxZ) * 0.5f;
            float sx = LobbyMaxX - LobbyMinX, sz = LobbyMaxZ - LobbyMinZ;

            Block("Floor", new Vector3(cx, -0.1f, cz), new Vector3(sx + 0.4f, 0.2f, sz), floorMat, shell);
            Block("Ceiling", new Vector3(cx, h + 0.1f, cz), new Vector3(sx + 0.4f, 0.2f, sz), glowMat, shell);
            Block("Wall S", new Vector3(cx, h * 0.5f, LobbyMinZ - 0.1f), new Vector3(sx + 0.4f, h + 0.2f, 0.2f), glowMat, shell);
            Block("Wall W", new Vector3(LobbyMinX - 0.1f, h * 0.5f, cz), new Vector3(0.2f, h + 0.2f, sz), glowMat, shell);
            Block("Wall E", new Vector3(LobbyMaxX + 0.1f, h * 0.5f, cz), new Vector3(0.2f, h + 0.2f, sz), glowMat, shell);
            // North: the white room's south wall (with the doorway) is the lower 4 m; this strip closes 4..5 m.
            Block("Wall N (upper strip)", new Vector3(cx, (4f + h + 0.2f) * 0.5f, -10.1f), new Vector3(sx + 0.4f, h + 0.2f - 4f, 0.2f), glowMat, shell);

            // Plain trim around the doorway so it reads as "the ordinary way through" (door 2).
            float x = LobbyDoorwayHalfWidth + 0.06f;
            Block("Doorway Trim W", new Vector3(-x, LobbyDoorwayHeight * 0.5f, -10.25f), new Vector3(0.12f, LobbyDoorwayHeight, 0.1f), trimMat, shell);
            Block("Doorway Trim E", new Vector3(x, LobbyDoorwayHeight * 0.5f, -10.25f), new Vector3(0.12f, LobbyDoorwayHeight, 0.1f), trimMat, shell);
            Block("Doorway Trim Top", new Vector3(0f, LobbyDoorwayHeight + 0.06f, -10.25f), new Vector3(x * 2f + 0.12f, 0.12f, 0.1f), trimMat, shell);
        }

        /// A door panel on a wall face that never opens (D-145): Door + permanently locked DoorLock.
        /// floorPoint = where the door meets the floor on the wall face; facing = into the room.
        static Door BuildLockedDoor(string name, string id, Vector3 floorPoint, Vector3 facing, Vector2 size,
                                    Material panelMat, Material trimMat, Material lockMat, PuzzleAssets puzzle, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floorPoint, Quaternion.LookRotation(facing));
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;

            GameObject panel = PuzzlePart("Panel", root.transform, new Vector3(0f, size.y * 0.5f, 0.04f), new Vector3(size.x, size.y, 0.08f), panelMat);
            panel.layer = puzzle.interactableLayer; // F finds it ("Locked"); bullets stop on it like a wall

            float postX = size.x * 0.5f + 0.06f;
            LocalBlock("Trim L", root.transform, new Vector3(-postX, size.y * 0.5f + 0.03f, 0.05f), new Vector3(0.12f, size.y + 0.06f, 0.1f), trimMat, true, true);
            LocalBlock("Trim R", root.transform, new Vector3(postX, size.y * 0.5f + 0.03f, 0.05f), new Vector3(0.12f, size.y + 0.06f, 0.1f), trimMat, true, true);
            LocalBlock("Trim Top", root.transform, new Vector3(0f, size.y + 0.06f, 0.05f), new Vector3(size.x + 0.36f, 0.12f, 0.1f), trimMat, true, true);

            Renderer light = null;
            if (lockMat != null)
                light = LocalBlock("Lock Light", root.transform, new Vector3(0f, size.y + 0.3f, 0.03f), new Vector3(0.4f, 0.12f, 0.06f), lockMat, false, false).GetComponent<Renderer>();

            var doorLock = root.AddComponent<DoorLock>();
            Assign(doorLock, "data", puzzle.door);
            if (light != null) Assign(doorLock, "lockLight", light);
            var lockSo = new SerializedObject(doorLock);
            lockSo.FindProperty("startsLocked").boolValue = true;
            lockSo.FindProperty("permanentlyLocked").boolValue = true;
            lockSo.ApplyModifiedPropertiesWithoutUndo();

            var door = root.AddComponent<Door>();
            Assign(door, "data", puzzle.door);
            Assign(door, "doorLock", doorLock);
            Assign(door, "panel", panel.transform);
            var doorSo = new SerializedObject(door);
            doorSo.FindProperty("interactionId").stringValue = id;
            doorSo.ApplyModifiedPropertiesWithoutUndo();
            return door;
        }

        /// Office / home motifs as primitives (D-062), placed clear of the doors, the start and the portal vestibule.
        static void BuildLobbyProps(Transform lobby, Material offWhite, Material wood, Material plant)
        {
            Transform props = new GameObject("Lobby Props (office/home motifs, D-062)").transform;
            props.SetParent(lobby, false);

            // Reception desk with an office chair behind it.
            Block("Reception Desk", new Vector3(-4f, 0.5f, -13f), new Vector3(2.6f, 1f, 0.8f), offWhite, props);
            Block("Reception Desk Top", new Vector3(-4f, 1.03f, -13f), new Vector3(2.8f, 0.06f, 1f), wood, props);
            Prop("Chair Seat", PrimitiveType.Cube, props, new Vector3(-4f, 0.45f, -12.2f), new Vector3(0.5f, 0.08f, 0.5f), offWhite, Vector3.zero, true);
            Prop("Chair Back", PrimitiveType.Cube, props, new Vector3(-4f, 0.8f, -11.95f), new Vector3(0.5f, 0.6f, 0.06f), offWhite, Vector3.zero, true);
            Prop("Chair Pole", PrimitiveType.Cylinder, props, new Vector3(-4f, 0.21f, -12.2f), new Vector3(0.06f, 0.2f, 0.06f), offWhite, Vector3.zero, false);

            // Water cooler and filing cabinet by the west wall.
            Prop("Water Cooler", PrimitiveType.Cylinder, props, new Vector3(-7.4f, 0.5f, -11f), new Vector3(0.35f, 0.5f, 0.35f), offWhite, Vector3.zero, true);
            Prop("Water Bottle", PrimitiveType.Sphere, props, new Vector3(-7.4f, 1.2f, -11f), Vector3.one * 0.35f, offWhite, Vector3.zero, false);
            Block("Filing Cabinet", new Vector3(-7.65f, 0.65f, -13.2f), new Vector3(0.5f, 1.3f, 0.6f), offWhite, props);

            // Waiting couch by the east wall.
            Block("Couch Seat", new Vector3(7.4f, 0.25f, -13.5f), new Vector3(0.8f, 0.5f, 2.2f), offWhite, props);
            Block("Couch Back", new Vector3(7.75f, 0.65f, -13.5f), new Vector3(0.2f, 0.8f, 2.2f), offWhite, props);

            // Potted plants, a coat rack by the black door, a wall clock above the doorway.
            Prop("Plant Pot NE", PrimitiveType.Cylinder, props, new Vector3(7.3f, 0.25f, -11f), new Vector3(0.5f, 0.25f, 0.5f), offWhite, Vector3.zero, true);
            Prop("Plant NE", PrimitiveType.Sphere, props, new Vector3(7.3f, 0.85f, -11f), Vector3.one * 0.8f, plant, Vector3.zero, false);
            Prop("Plant Pot SW", PrimitiveType.Cylinder, props, new Vector3(-7.3f, 0.25f, -23.3f), new Vector3(0.5f, 0.25f, 0.5f), offWhite, Vector3.zero, true);
            Prop("Plant SW", PrimitiveType.Sphere, props, new Vector3(-7.3f, 0.85f, -23.3f), Vector3.one * 0.8f, plant, Vector3.zero, false);
            Prop("Coat Rack", PrimitiveType.Cylinder, props, new Vector3(1.6f, 0.9f, -23.6f), new Vector3(0.05f, 0.9f, 0.05f), wood, Vector3.zero, true);
            Prop("Coat Rack Top", PrimitiveType.Sphere, props, new Vector3(1.6f, 1.85f, -23.6f), Vector3.one * 0.12f, wood, Vector3.zero, false);
            Prop("Wall Clock", PrimitiveType.Cylinder, props, new Vector3(-4f, 3.3f, -10.23f), new Vector3(0.5f, 0.02f, 0.5f), offWhite, new Vector3(90f, 0f, 0f), false);
        }

        // ---------- the portal door (lobby side) ----------

        /// Freestanding frame + short wall with a swing panel (D-146). Root = the Portal's pose:
        /// bottom centre of the opening, local forward = front (south, towards the start).
        static Portal BuildLobbyPortalDoor(PortalAssets assets, Material trimMat, Material wallMat, Transform lobby)
        {
            var root = new GameObject("Door 3 - Portal Door (freestanding)");
            root.transform.SetParent(lobby, false);
            root.transform.SetPositionAndRotation(LobbyPortalPosition, Quaternion.Euler(0f, LobbyPortalYaw, 0f));
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;

            // Short wall: 1 m either side of the opening, 3.6 m tall, 0.2 m thick, centred on the portal plane.
            float side = OpeningWidth * 0.5f + 0.5f;
            LocalBlock("Wall Left", root.transform, new Vector3(-side, 1.8f, 0f), new Vector3(1f, 3.6f, 0.2f), wallMat, true, true);
            LocalBlock("Wall Right", root.transform, new Vector3(side, 1.8f, 0f), new Vector3(1f, 3.6f, 0.2f), wallMat, true, true);
            LocalBlock("Wall Above", root.transform, new Vector3(0f, OpeningHeight + 0.3f, 0f), new Vector3(OpeningWidth, 0.6f, 0.2f), wallMat, true, true);
            BuildFrameTrim(root.transform, trimMat, 0.24f);

            // Swing panel: the hinge sits on the opening's +X edge, on the front face; +95 degrees swings it towards
            // the front (lobby side), clear of the opening. The hinge is the Door's "panel" (it rotates).
            var hinge = new GameObject("Panel Hinge");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(OpeningWidth * 0.5f, 0f, 0.1f);
            int interactable = LayerMask.NameToLayer(InteractableLayerName);
            hinge.layer = interactable;
            var body = hinge.AddComponent<Rigidbody>(); // kinematic: a moving collider without physics pushes
            body.isKinematic = true;
            body.useGravity = false;
            GameObject panel = PuzzlePart("Panel", hinge.transform, new Vector3(-OpeningWidth * 0.5f, OpeningHeight * 0.5f, 0.03f),
                                          new Vector3(OpeningWidth, OpeningHeight, 0.06f), trimMat);
            panel.layer = interactable;
            panel.AddComponent<NoiseOccluder>().Kind = NoiseOccluderKind.DoorPanel; // D-131
            var obstacle = panel.AddComponent<NavMeshObstacle>(); // D-135: closed door blocks paths
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;

            var doorLock = root.AddComponent<DoorLock>(); // unlocked: locking stays in D-127, not in the portal (D-147)
            Assign(doorLock, "data", assets.swingDoor);
            var lockSo = new SerializedObject(doorLock);
            lockSo.FindProperty("startsLocked").boolValue = false;
            lockSo.ApplyModifiedPropertiesWithoutUndo();

            var door = root.AddComponent<Door>();
            Assign(door, "data", assets.swingDoor);
            Assign(door, "doorLock", doorLock);
            Assign(door, "panel", hinge.transform);
            var doorSo = new SerializedObject(door);
            doorSo.FindProperty("interactionId").stringValue = "door.portal";
            doorSo.ApplyModifiedPropertiesWithoutUndo();

            return BuildPortal("Portal (lobby side)", "lobby.portal", root.transform, door, assets);
        }

        /// Thin off-white frame around the opening on the front face (also hides the screen's edges).
        static void BuildFrameTrim(Transform root, Material trimMat, float depth)
        {
            float x = OpeningWidth * 0.5f + 0.04f;
            LocalBlock("Frame Post L", root, new Vector3(-x, (OpeningHeight + 0.08f) * 0.5f, 0f), new Vector3(0.08f, OpeningHeight + 0.08f, depth), trimMat, true, true);
            LocalBlock("Frame Post R", root, new Vector3(x, (OpeningHeight + 0.08f) * 0.5f, 0f), new Vector3(0.08f, OpeningHeight + 0.08f, depth), trimMat, true, true);
            LocalBlock("Frame Head", root, new Vector3(0f, OpeningHeight + 0.04f, 0f), new Vector3(OpeningWidth + 0.16f, 0.08f, depth), trimMat, true, true);
        }

        /// Portal component + screen (Portal layer) + white stand-in (PortalFallback layer) + its camera.
        static Portal BuildPortal(string name, string id, Transform parent, Door door, PortalAssets assets)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "Screen (Portal layer)";
            Object.DestroyImmediate(screen.GetComponent<Collider>()); // never blocks walking, bullets or noise
            screen.transform.SetParent(go.transform, false);
            screen.transform.localPosition = new Vector3(0f, OpeningHeight * 0.5f, -0.001f);
            screen.transform.localScale = new Vector3(OpeningWidth + 0.04f, OpeningHeight + 0.02f, 0.002f);
            screen.layer = assets.portalLayer;
            var screenRenderer = screen.GetComponent<MeshRenderer>();
            screenRenderer.sharedMaterial = assets.surface;
            screenRenderer.shadowCastingMode = ShadowCastingMode.Off;
            screenRenderer.receiveShadows = false;
            screenRenderer.lightProbeUsage = LightProbeUsage.Off;
            screenRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fallback.name = "Fallback White (PortalFallback layer)";
            Object.DestroyImmediate(fallback.GetComponent<Collider>());
            fallback.transform.SetParent(go.transform, false);
            fallback.transform.localPosition = new Vector3(0f, OpeningHeight * 0.5f, 0f);
            fallback.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // a Quad faces -Z; turn it to face the front
            fallback.transform.localScale = new Vector3(OpeningWidth, OpeningHeight, 1f);
            fallback.layer = assets.fallbackLayer;
            var fallbackRenderer = fallback.GetComponent<MeshRenderer>();
            fallbackRenderer.sharedMaterial = assets.fallback;
            fallbackRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fallbackRenderer.receiveShadows = false;

            var portal = go.AddComponent<Portal>();
            PortalCamera view = BuildPortalCamera(go.transform, portal, id, assets);

            var so = new SerializedObject(portal);
            so.FindProperty("portalId").stringValue = id;
            so.FindProperty("openingSize").vector2Value = new Vector2(OpeningWidth, OpeningHeight);
            so.FindProperty("door").objectReferenceValue = door;
            so.FindProperty("surface").objectReferenceValue = screenRenderer;
            so.FindProperty("fallback").objectReferenceValue = fallbackRenderer;
            so.FindProperty("fallbackMaterial").objectReferenceValue = assets.fallback;
            so.FindProperty("view").objectReferenceValue = view;
            so.ApplyModifiedPropertiesWithoutUndo();
            return portal;
        }

        /// Standalone Base camera, saved DISABLED and without a target texture (both are runtime-only, D-139).
        static PortalCamera BuildPortalCamera(Transform parent, Portal portal, string id, PortalAssets assets)
        {
            var go = new GameObject("Portal Camera (view through " + id + ")");
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = null;
            cam.depth = -10f; // before the player camera
            cam.cullingMask = PortalCameraCullingMask(assets);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.allowHDR = true;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;

            var data = cam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false; // bloom + tonemapping once, on the main camera (D-141)
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;
            data.stopNaN = false;
            data.dithering = false;
            data.SetRenderer(assets.rendererIndex);

            var view = go.AddComponent<PortalCamera>();
            Assign(view, "portal", portal);
            return view;
        }

        /// Everything except the gun, the player's body, trigger volumes and the live portal screens (no recursion).
        static int PortalCameraCullingMask(PortalAssets assets)
        {
            int excluded = (1 << assets.portalLayer);
            foreach (string layerName in new[] { WeaponLayerName, PlayerLayerName, IgnoreRaycastLayerName })
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0) excluded |= 1 << layer;
            }
            return ~excluded;
        }

        // ---------- the impossible hall ----------

        /// 30 x 20 x 8 m white hall 500 m east of everything (D-144, D-146), with the return portal in its
        /// west wall (an alcove behind it so you can step in), office/home props and one shootable target.
        static Portal BuildHall(PortalAssets assets, Transform hall, Material floorMat, Material trimMat, Material wood,
                                Material plant, Material targetMat)
        {
            Material wallMat = GetOrCreateMaterial("M_WhiteEmissive", Color.white, Color.white * 1.6f);
            float h = HallHeight;
            float cx = (HallMinX + HallMaxX) * 0.5f, cz = (HallMinZ + HallMaxZ) * 0.5f;
            float sx = HallMaxX - HallMinX, sz = HallMaxZ - HallMinZ;

            Transform shell = new GameObject("Hall Shell").transform;
            shell.SetParent(hall, false);
            Block("Floor", new Vector3(cx, -0.1f, cz), new Vector3(sx + 0.4f, 0.2f, sz + 0.4f), floorMat, shell);
            Block("Ceiling", new Vector3(cx, h + 0.1f, cz), new Vector3(sx + 0.4f, 0.2f, sz + 0.4f), wallMat, shell);
            Block("Wall N", new Vector3(cx, h * 0.5f, HallMaxZ + 0.1f), new Vector3(sx + 0.4f, h + 0.2f, 0.2f), wallMat, shell);
            Block("Wall S", new Vector3(cx, h * 0.5f, HallMinZ - 0.1f), new Vector3(sx + 0.4f, h + 0.2f, 0.2f), wallMat, shell);
            Block("Wall E", new Vector3(HallMaxX + 0.1f, h * 0.5f, cz), new Vector3(0.2f, h + 0.2f, sz + 0.4f), wallMat, shell);

            // West wall (x = 499.9) with the portal opening at z -16.7..-15.3, 0..3 m.
            float zPortal = HallPortalPosition.z;
            float half = OpeningWidth * 0.5f;
            float southLength = (zPortal - half) - HallMinZ;
            float northLength = HallMaxZ - (zPortal + half);
            Block("Wall W (south)", new Vector3(HallMinX - 0.1f, h * 0.5f, HallMinZ + southLength * 0.5f), new Vector3(0.2f, h + 0.2f, southLength), wallMat, shell);
            Block("Wall W (north)", new Vector3(HallMinX - 0.1f, h * 0.5f, HallMaxZ - northLength * 0.5f), new Vector3(0.2f, h + 0.2f, northLength), wallMat, shell);
            Block("Wall W (above portal)", new Vector3(HallMinX - 0.1f, (OpeningHeight + h + 0.2f) * 0.5f, zPortal), new Vector3(0.2f, h + 0.2f - OpeningHeight, OpeningWidth), wallMat, shell);

            // Alcove behind the return portal (1.2 m deep, exactly as wide as the opening): room to step through.
            Transform alcove = new GameObject("Return Portal Alcove").transform;
            alcove.SetParent(hall, false);
            Block("Alcove Floor", new Vector3(HallMinX - 0.7f, -0.1f, zPortal), new Vector3(1.4f, 0.2f, OpeningWidth + 0.4f), floorMat, alcove);
            Block("Alcove Ceiling", new Vector3(HallMinX - 0.7f, OpeningHeight + 0.1f, zPortal), new Vector3(1.4f, 0.2f, OpeningWidth + 0.4f), trimMat, alcove);
            Block("Alcove Side S", new Vector3(HallMinX - 0.7f, OpeningHeight * 0.5f, zPortal - half - 0.1f), new Vector3(1.4f, OpeningHeight, 0.2f), trimMat, alcove);
            Block("Alcove Side N", new Vector3(HallMinX - 0.7f, OpeningHeight * 0.5f, zPortal + half + 0.1f), new Vector3(1.4f, OpeningHeight, 0.2f), trimMat, alcove);
            Block("Alcove Back", new Vector3(HallMinX - 1.3f, OpeningHeight * 0.5f, zPortal), new Vector3(0.2f, OpeningHeight, OpeningWidth + 0.4f), trimMat, alcove);

            // Return portal: no panel, always open (D-146).
            var anchor = new GameObject("Return Portal (hall side)");
            anchor.transform.SetParent(hall, false);
            anchor.transform.SetPositionAndRotation(HallPortalPosition, Quaternion.Euler(0f, HallPortalYaw, 0f));
            BuildFrameTrim(anchor.transform, trimMat, 0.16f);
            Portal hallPortal = BuildPortal("Portal (hall side)", "hall.return", anchor.transform, null, assets);

            // Props (D-062), all well clear of the 1 m vestibule in front of the portal.
            Transform props = new GameObject("Hall Props (office/home motifs)").transform;
            props.SetParent(hall, false);
            Desk("Desk A", new Vector3(508f, 0f, -21f), props, trimMat, wood);
            Desk("Desk B", new Vector3(508f, 0f, -11f), props, trimMat, wood);
            Desk("Desk C", new Vector3(522f, 0f, -21f), props, trimMat, wood);
            Desk("Desk D", new Vector3(522f, 0f, -11f), props, trimMat, wood);
            Block("Bookshelf", new Vector3(512f, 1f, HallMaxZ - 0.25f), new Vector3(3f, 2f, 0.4f), wood, props);
            Block("Sofa Seat", new Vector3(526f, 0.25f, HallMinZ + 0.5f), new Vector3(2.4f, 0.5f, 0.9f), trimMat, props);
            Block("Sofa Back", new Vector3(526f, 0.65f, HallMinZ + 0.1f), new Vector3(2.4f, 0.8f, 0.2f), trimMat, props);
            Prop("Floor Lamp Pole", PrimitiveType.Cylinder, props, new Vector3(504f, 0.9f, HallMinZ + 1f), new Vector3(0.05f, 0.9f, 0.05f), trimMat, Vector3.zero, true);
            Prop("Floor Lamp Shade", PrimitiveType.Sphere, props, new Vector3(504f, 1.9f, HallMinZ + 1f), Vector3.one * 0.45f, wallMat, Vector3.zero, false);
            Prop("Plant Pot", PrimitiveType.Cylinder, props, new Vector3(529f, 0.25f, HallMaxZ - 1f), new Vector3(0.5f, 0.25f, 0.5f), trimMat, Vector3.zero, true);
            Prop("Plant", PrimitiveType.Sphere, props, new Vector3(529f, 0.85f, HallMaxZ - 1f), Vector3.one * 0.8f, plant, Vector3.zero, false);
            // A lonely door frame standing in the hall (Superliminal-style oddity, D-011).
            Block("Lone Door Frame Post L", new Vector3(524f, 1.2f, -8.6f), new Vector3(0.12f, 2.4f, 0.12f), trimMat, props);
            Block("Lone Door Frame Post R", new Vector3(525.2f, 1.2f, -8.6f), new Vector3(0.12f, 2.4f, 0.12f), trimMat, props);
            Block("Lone Door Frame Head", new Vector3(524.6f, 2.46f, -8.6f), new Vector3(1.32f, 0.12f, 0.12f), trimMat, props);

            // One shootable test target, straight ahead when you look through the lobby portal (shoot-through test).
            Block("Target Pillar", new Vector3(515f, 1.5f, zPortal), new Vector3(0.6f, 3f, 0.6f), trimMat, props);
            GameObject target = Block("Shootable Target (through the portal)", new Vector3(514.65f, 1.6f, zPortal), new Vector3(0.1f, 0.7f, 0.7f), targetMat, props);
            GameObjectUtility.SetStaticEditorFlags(target, 0);
            target.AddComponent<TestShootableTarget>(); // any gun

            // F-through-the-portal test (D-143): a button just past the vestibule, beside the line of entry,
            // close enough to reach with the 2.5 m interaction ray from right in front of the lobby portal.
            Material buttonMat = GetOrCreateMaterial("M_TestButton", new Color(1f, 0.2f, 0.2f), new Color(0.5f, 0.1f, 0.1f));
            Transform anchorT = anchor.transform;
            Vector3 pedestalFloor = anchorT.TransformPoint(new Vector3(0.95f, 0f, 1.3f));
            Block("Button Pedestal (hall)", pedestalFloor + Vector3.up * 0.5f, new Vector3(0.5f, 1f, 0.5f), trimMat, props);
            GameObject button = Block("Test Button (F through the portal)", pedestalFloor + Vector3.up * 1.15f, new Vector3(0.3f, 0.3f, 0.3f), buttonMat, props);
            button.layer = LayerMask.NameToLayer(InteractableLayerName);
            GameObjectUtility.SetStaticEditorFlags(button, 0);
            var buttonInteractable = button.AddComponent<TWR.Interaction.TestButtonInteractable>();
            var buttonSo = new SerializedObject(buttonInteractable);
            buttonSo.FindProperty("interactionId").stringValue = "hall.test_button";
            buttonSo.ApplyModifiedPropertiesWithoutUndo();

            return hallPortal;
        }

        static void Desk(string name, Vector3 floorCentre, Transform parent, Material legMat, Material topMat)
        {
            Transform desk = new GameObject(name).transform;
            desk.SetParent(parent, false);
            Block("Top", floorCentre + new Vector3(0f, 0.75f, 0f), new Vector3(1.6f, 0.05f, 0.8f), topMat, desk);
            Block("Side L", floorCentre + new Vector3(-0.76f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.75f), legMat, desk);
            Block("Side R", floorCentre + new Vector3(0.76f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.75f), legMat, desk);
            Prop("Chair", PrimitiveType.Cube, desk, floorCentre + new Vector3(0f, 0.45f, 0.75f), new Vector3(0.5f, 0.08f, 0.5f), legMat, Vector3.zero, true);
            Prop("Chair Back", PrimitiveType.Cube, desk, floorCentre + new Vector3(0f, 0.8f, 1f), new Vector3(0.5f, 0.6f, 0.06f), legMat, Vector3.zero, true);
        }

        // ---------- runtime objects that need the player ----------

        static void BuildPortalRuntime(PortalAssets assets, GameObject player)
        {
            var go = new GameObject("Portal Render Manager");
            var manager = go.AddComponent<PortalRenderManager>();
            Assign(manager, "settings", assets.settings);
            Assign(manager, "playerCamera", player.GetComponentInChildren<Camera>(true));
        }

        // ---------- small helpers ----------

        /// Cube child in LOCAL space. isStatic = batching static (never for moving or runtime-scaled things).
        static GameObject LocalBlock(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material,
                                     bool isStatic, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, isStatic ? StaticEditorFlags.BatchingStatic : 0);
            return go;
        }

        /// Primitive prop in WORLD space; keepCollider = blocks walking/bullets (and the NavMesh).
        static void Prop(string name, PrimitiveType shape, Transform parent, Vector3 position, Vector3 scale, Material material,
                         Vector3 euler, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // ---------- validation ----------

        /// Logs every portal link the slice depends on (D-139 to D-147). Returns false if anything is wrong.
        static bool ValidatePortalSetup(PortalAssets assets, GameObject player, PlayerTuning tuning)
        {
            Physics.SyncTransforms(); // edit-mode physics queries need the new colliders' positions
            bool ok = true;

            Portal[] portals = Object.FindObjectsByType<Portal>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            if (portals.Length == 0)
            {
                Debug.LogError("[TWR] Portals: no Portal in the scene.");
                return false;
            }

            ok &= ValidatePortalAssets(assets);
            foreach (Portal portal in portals)
                ok &= ValidatePortal(portal, assets, tuning);
            ok &= ValidatePortalNavMesh(portals);
            ok &= ValidatePickupsNearPortals(portals);
            ok &= ValidatePlayerForPortals(player);
            ok &= ValidateManager(assets, player);
            return ok;
        }

        static bool ValidatePortalAssets(PortalAssets assets)
        {
            var report = new StringBuilder();
            bool ok = true;
            PortalSettings s = AssetDatabase.LoadAssetAtPath<PortalSettings>(PortalSettingsPath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(PortalShaderPath);
            var surface = AssetDatabase.LoadAssetAtPath<Material>(PortalSurfaceMaterialPath);
            var fallback = AssetDatabase.LoadAssetAtPath<Material>(PortalFallbackMaterialPath);

            report.AppendLine(s != null
                ? $"[TWR] Portal assets: PortalSettings found ({PortalSettingsPath}): resolution x{s.resolutionScale}, max live portals {s.maxRenderedPortals}, " +
                  $"recursion {s.recursionDepth}, near box {s.nearModeDistance} m / {s.nearBoxDepth} m deep, oblique min {s.obliqueMinDistance} m"
                : $"[TWR] Portal assets: PortalSettings MISSING at {PortalSettingsPath}");
            bool shaderErrors = shader != null && ShaderUtil.ShaderHasError(shader);
            report.AppendLine($"  shader: {(shader != null ? shader.name + (shaderErrors ? " - HAS COMPILE ERRORS (select it in the Project window)" : shader.isSupported ? " (compiles, supported)" : " (NOT supported on this GPU)") : "MISSING")}");
            report.AppendLine($"  screen material: {(surface != null ? surface.name + " -> " + (surface.shader != null ? surface.shader.name : "no shader") : "MISSING")}");
            report.AppendLine($"  fallback material: {(fallback != null ? fallback.name : "MISSING")}");
            if (s == null || shader == null || shaderErrors || !shader.isSupported || surface == null || surface.shader != shader || fallback == null) ok = false;

            ScriptableRendererData rendererData = s != null ? s.portalRenderer : null;
            report.AppendLine($"  Portal renderer: {(rendererData != null ? AssetDatabase.GetAssetPath(rendererData) : "MISSING in PortalSettings")}");
            if (rendererData != null)
            {
                report.AppendLine($"    renderer features: {rendererData.rendererFeatures.Count} (must be 0: no SSAO, no Render Objects)");
                if (rendererData.rendererFeatures.Count > 0) ok = false;
                ok &= PortalRenderSetup.DescribeRegistration(rendererData, report);
            }
            else ok = false;

            if (ok) Debug.Log(report.ToString(), s);
            else Debug.LogError(report.ToString(), s);
            return ok;
        }

        static bool ValidatePortal(Portal portal, PortalAssets assets, PlayerTuning tuning)
        {
            var so = new SerializedObject(portal);
            var destination = so.FindProperty("destination").objectReferenceValue as Portal;
            var door = so.FindProperty("door").objectReferenceValue as Door;
            var surface = so.FindProperty("surface").objectReferenceValue as MeshRenderer;
            var fallback = so.FindProperty("fallback").objectReferenceValue as Renderer;
            var fallbackMat = so.FindProperty("fallbackMaterial").objectReferenceValue as Material;
            var view = so.FindProperty("view").objectReferenceValue as PortalCamera;
            Vector2 opening = so.FindProperty("openingSize").vector2Value;
            string id = so.FindProperty("portalId").stringValue;

            bool ok = true;
            var report = new StringBuilder();
            Transform t = portal.transform;
            report.AppendLine($"[TWR] Portal '{id}' at {t.position} yaw {t.eulerAngles.y:0}: opening {opening.x} x {opening.y} m (min 1.2 x 2.4), " +
                              $"door: {(door != null ? door.name : "none (always open)")}");
            if (opening.x < 1.2f || opening.y < 2.4f) ok = false;

            // Pairing (D-142, D-144, D-147).
            if (destination == null || destination == portal)
            {
                report.AppendLine("  destination: MISSING");
                ok = false;
            }
            else
            {
                bool yawOnly = Portal.DiffersOnlyByYaw(portal, destination, out string problem);
                var backSo = new SerializedObject(destination);
                bool twoWay = backSo.FindProperty("destination").objectReferenceValue == portal;
                float separation = Vector3.Distance(t.position, destination.transform.position);
                float turn = Mathf.DeltaAngle(0f, destination.transform.eulerAngles.y + 180f - t.eulerAngles.y);
                report.AppendLine($"  destination: '{backSo.FindProperty("portalId").stringValue}' ({(twoWay ? "two-way" : "ONE-WAY")}), " +
                                  $"differ only by yaw: {(yawOnly ? "yes" : "NO - " + problem)}, view turns {turn:0} deg, " +
                                  $"distance {separation:0.0} m (min {MinPortalSeparation})");
                if (!yawOnly || separation < MinPortalSeparation) ok = false;

                // Round trip: a point in front of this portal mapped there and back must land where it started.
                Vector3 probe = t.TransformPoint(new Vector3(0.3f, 1.6f, -0.2f));
                Vector3 there = MapThrough(portal, destination, probe);
                Vector3 back = MapThrough(destination, portal, there);
                bool roundTrip = (back - probe).sqrMagnitude < 1e-6f;
                report.AppendLine($"  pair maths round trip: {(roundTrip ? "OK" : "WRONG (" + back + " vs " + probe + ")")}");
                ok &= roundTrip;
            }

            // Vestibule on the exit side (in front of THIS portal: arrivals from the other side come out here).
            ok &= CheckVestibule(portal, tuning, report);

            // Screen, stand-in and camera.
            bool screenOk = surface != null && surface.gameObject.layer == assets.portalLayer && surface.GetComponent<Collider>() == null
                            && surface.sharedMaterial == assets.surface;
            bool fallbackOk = fallback != null && fallback.gameObject.layer == assets.fallbackLayer && fallbackMat != null;
            report.AppendLine($"  screen: {(surface != null ? $"{surface.name}, layer {LayerMask.LayerToName(surface.gameObject.layer)}, material {(surface.sharedMaterial != null ? surface.sharedMaterial.name : "none")}" : "MISSING")}" +
                              $"; fallback: {(fallback != null ? $"layer {LayerMask.LayerToName(fallback.gameObject.layer)}, material {(fallbackMat != null ? fallbackMat.name : "MISSING")}" : "MISSING")}");
            ok &= screenOk && fallbackOk;
            ok &= DescribePortalCamera(view, portal, assets, report);

            if (ok) Debug.Log(report.ToString(), portal);
            else Debug.LogError(report.ToString(), portal);
            return ok;
        }

        static Vector3 MapThrough(Portal from, Portal to, Vector3 world)
        {
            Vector3 local = from.transform.InverseTransformPoint(world);
            return to.transform.TransformPoint(new Vector3(-local.x, local.y, -local.z));
        }

        /// D-142: at least 1 m in front of the portal is free of colliders for a standing player capsule,
        /// and the floor there is at the portal's height. Door panels (Interactable) are ignored: they swing away.
        static bool CheckVestibule(Portal portal, PlayerTuning tuning, StringBuilder report)
        {
            Transform t = portal.transform;
            float radius = tuning != null ? tuning.controllerRadius : 0.35f;
            float height = tuning != null ? tuning.standingHeight : 1.8f;
            int mask = 1 << 0; // Default: walls, floors, props
            bool clear = true;
            for (float z = 0f; z <= MinVestibuleDepth + 0.001f; z += 0.25f)
            {
                Vector3 feet = t.TransformPoint(new Vector3(0f, 0.05f, z));
                Vector3 bottom = feet + Vector3.up * (radius + 0.02f);
                Vector3 top = feet + Vector3.up * (height - radius);
                if (Physics.CheckCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore)) clear = false;
            }
            Vector3 above = t.TransformPoint(new Vector3(0f, 1f, MinVestibuleDepth));
            bool floorOk = Physics.Raycast(above, Vector3.down, out RaycastHit floor, 2f, mask, QueryTriggerInteraction.Ignore)
                           && Mathf.Abs(floor.point.y - t.position.y) < 0.05f;
            report.AppendLine($"  vestibule (front, {MinVestibuleDepth} m deep, capsule r {radius} h {height}): {(clear ? "clear" : "BLOCKED")}, " +
                              $"floor at portal height: {(floorOk ? "yes" : "NO")}");
            return clear && floorOk;
        }

        static bool DescribePortalCamera(PortalCamera view, Portal portal, PortalAssets assets, StringBuilder report)
        {
            if (view == null)
            {
                report.AppendLine("  camera: MISSING");
                return false;
            }
            Camera cam = view.GetComponent<Camera>();
            var data = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            var viewSo = new SerializedObject(view);
            bool linked = viewSo.FindProperty("portal").objectReferenceValue == portal;
            if (cam == null || data == null)
            {
                report.AppendLine("  camera: Camera or URP camera data MISSING");
                return false;
            }
            int rendererIndex = new SerializedObject(data).FindProperty("m_RendererIndex").intValue;
            int expectedMask = PortalCameraCullingMask(assets);
            bool ok = linked && !cam.enabled && cam.targetTexture == null && cam.cullingMask == expectedMask
                      && !data.renderPostProcessing && data.requiresDepthOption == CameraOverrideOption.Off
                      && data.requiresColorOption == CameraOverrideOption.Off && cam.allowHDR && !cam.allowMSAA
                      && data.renderType == CameraRenderType.Base && cam.depth < 0f;
            report.AppendLine($"  camera: '{cam.name}' linked {(linked ? "yes" : "NO")}, saved disabled {(!cam.enabled ? "yes" : "NO")}, " +
                              $"target texture {(cam.targetTexture == null ? "none (made at runtime)" : "SAVED - WRONG")}, depth {cam.depth}, " +
                              $"post-processing {(data.renderPostProcessing ? "ON - WRONG" : "off")}, depth/opaque texture {data.requiresDepthOption}/{data.requiresColorOption}, " +
                              $"HDR {(cam.allowHDR ? "on" : "OFF")}, MSAA {(cam.allowMSAA ? "ON" : "off")}, renderer index {rendererIndex} " +
                              $"(Portal renderer in active URP asset: {assets.rendererIndex})");
            report.AppendLine($"    culling mask: everything except {MaskNames(~cam.cullingMask)} {(cam.cullingMask == expectedMask ? "(OK)" : "- WRONG, expected except " + MaskNames(~expectedMask))}");
            if (rendererIndex != assets.rendererIndex) ok = false;
            return ok;
        }

        /// D-144: the lobby and the hall are physically apart and their NavMeshes don't connect.
        static bool ValidatePortalNavMesh(Portal[] portals)
        {
            var path = new NavMeshPath();
            bool lobbyOnMesh = NavMesh.SamplePosition(LobbyProbe, out NavMeshHit lobby, 1f, NavMesh.AllAreas);
            bool whiteRoomOnMesh = NavMesh.SamplePosition(WhiteRoomProbe, out NavMeshHit whiteRoom, 1f, NavMesh.AllAreas);
            bool lobbyJoined = lobbyOnMesh && whiteRoomOnMesh
                               && NavMesh.CalculatePath(lobby.position, whiteRoom.position, NavMesh.AllAreas, path)
                               && path.status == NavMeshPathStatus.PathComplete;

            bool hallOnMesh = NavMesh.SamplePosition(HallProbe, out NavMeshHit hall, 1f, NavMesh.AllAreas);
            bool hallReachable = lobbyOnMesh && hallOnMesh
                                 && NavMesh.CalculatePath(lobby.position, hall.position, NavMesh.AllAreas, path)
                                 && path.status == NavMeshPathStatus.PathComplete;

            float minSeparation = float.MaxValue;
            foreach (Portal p in portals)
                if (p.Destination != null) minSeparation = Mathf.Min(minSeparation, Vector3.Distance(p.transform.position, p.Destination.transform.position));

            string line = $"[TWR] Portal separation (D-144): spaces {minSeparation:0.0} m apart (min {MinPortalSeparation}); " +
                          $"lobby -> white room path: {(lobbyJoined ? "OK" : "MISSING")}; " +
                          $"hall NavMesh: {(hallOnMesh ? "own island" : "none")}, lobby -> hall path: {(hallReachable ? "CONNECTED - WRONG" : "none (Seekers can't follow)")}";
            bool ok = lobbyJoined && !hallReachable && minSeparation >= MinPortalSeparation;
            if (ok) Debug.Log(line);
            else Debug.LogError(line);
            return ok;
        }

        /// D-143: no walk-over pickups within 1.5 m of a portal opening (they'd be collected "through" it).
        static bool ValidatePickupsNearPortals(Portal[] portals)
        {
            bool ok = true;
            int checkedCount = 0;
            foreach (ItemPickup pickup in Object.FindObjectsByType<ItemPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                checkedCount++;
                foreach (Portal portal in portals)
                {
                    Vector3 local = portal.transform.InverseTransformPoint(pickup.transform.position);
                    Vector2 size = portal.OpeningSize;
                    Vector3 closest = new Vector3(Mathf.Clamp(local.x, -size.x * 0.5f, size.x * 0.5f), Mathf.Clamp(local.y, 0f, size.y), 0f);
                    float distance = Vector3.Distance(local, closest);
                    if (distance >= PickupPortalClearance) continue;
                    Debug.LogError($"[TWR] Pickup '{pickup.name}' is {distance:0.00} m from portal '{portal.PortalId}' (min {PickupPortalClearance} m).", pickup);
                    ok = false;
                }
            }
            if (ok) Debug.Log($"[TWR] Portal pickup clearance: {checkedCount} pickups, none within {PickupPortalClearance} m of a portal.");
            return ok;
        }

        static bool ValidatePlayerForPortals(GameObject player)
        {
            Vector3 p = player.transform.position;
            bool inLobby = p.x > LobbyMinX && p.x < LobbyMaxX && p.z > LobbyMinZ && p.z < LobbyMaxZ;
            Camera cam = player.GetComponentInChildren<Camera>(true);
            int fallbackLayer = LayerMask.NameToLayer(PortalFallbackLayerName);
            bool cameraOk = cam != null && fallbackLayer >= 0 && (cam.cullingMask & (1 << fallbackLayer)) == 0;
            var traveller = player.GetComponent<PortalTraveller>();
            Object pivot = traveller != null ? new SerializedObject(traveller).FindProperty("pivot").objectReferenceValue : null;
            Object controller = traveller != null ? new SerializedObject(traveller).FindProperty("controller").objectReferenceValue : null;
            bool travellerOk = traveller != null && pivot != null && pivot.name == "Head" && controller != null;

            string line = $"[TWR] Player start: {p} facing {player.transform.forward} -> in the lobby: {(inLobby ? "yes" : "NO")}; " +
                          $"player camera hides PortalFallback: {(cameraOk ? "yes" : "NO")}; " +
                          $"PortalTraveller: {(traveller != null ? $"pivot {(pivot != null ? pivot.name : "MISSING")} (un-leaned), controller {(controller != null ? "OK" : "MISSING")}" : "MISSING")}";
            bool ok = inLobby && cameraOk && travellerOk;
            if (ok) Debug.Log(line, player);
            else Debug.LogError(line, player);
            return ok;
        }

        static bool ValidateManager(PortalAssets assets, GameObject player)
        {
            PortalRenderManager[] managers = Object.FindObjectsByType<PortalRenderManager>(FindObjectsSortMode.None);
            if (managers.Length != 1)
            {
                Debug.LogError($"[TWR] Portal render manager: expected 1, found {managers.Length}.");
                return false;
            }
            var so = new SerializedObject(managers[0]);
            Object settings = so.FindProperty("settings").objectReferenceValue;
            Object cam = so.FindProperty("playerCamera").objectReferenceValue;
            // D-141: one look everywhere, so post-processing comes from GLOBAL volumes only.
            int volumes = 0, localVolumes = 0;
            foreach (Volume volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                volumes++;
                if (!volume.isGlobal) localVolumes++;
            }
            bool ok = settings == assets.settings && cam != null && cam == player.GetComponentInChildren<Camera>(true) && localVolumes == 0;
            string line = $"[TWR] Portal render manager: settings {(settings != null ? settings.name : "MISSING")}, player camera {(cam != null ? cam.name : "MISSING")}; " +
                          $"volumes: {volumes} ({(localVolumes == 0 ? "all global" : localVolumes + " LOCAL - would look different through a portal")})";
            if (ok) Debug.Log(line, managers[0]);
            else Debug.LogError(line, managers[0]);
            return ok;
        }
    }
}
