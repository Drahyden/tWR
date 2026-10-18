using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TWR.Audio;
using TWR.Core;
using TWR.Enemies;
using TWR.Endings;
using TWR.Items;
using TWR.Player;
using TWR.Portals;
using TWR.Puzzles;
using TWR.Run;
using TWR.UI;

namespace TWR.EditorTools
{
    /// <summary>
    /// Run Structure step R2 ("sections become real", D-161 to D-171). Menu "TWR/Run/Build Run Scenes" builds, from
    /// code, every scene in Assets/_Project/Scenes/Run:
    /// - Core (D-161): player, HUD, RunManager + SectionLoader + RunConfig, ambient bed, light + global Bloom volume,
    ///   white fade overlay (D-167), event system. No geometry. Stays loaded and active for the whole game.
    /// - Lobby (D-163): the slice lobby without the white room; the old doorway is now the unlocked run door.
    /// - Section_Puzzle (existing puzzle room + Seeker, exit gated by MainPuzzlesSolved), Section_Combat (3 Seekers,
    ///   cover, health), Section_Stealth (D-169: 2 patrols, crouch/cover route, health), Section_ImpossibleGeometry
    ///   (D-164: own portal pair, exit inside the hall), Section_Silent (D-168), Fork (D-150, D-166: EXIT door +
    ///   black BASEMENT door, health), Boss_Escape / Boss_Discover (R4: the Bull and Preacher arenas, built in
    ///   TestSceneBuilder.BossArenas.cs, D-180 to D-194).
    /// Every content scene: entry vestibule with a one-way locked door behind the SectionEntry, an exit door with a
    /// SectionExitGate and a SectionExit trigger in the corridor behind it, and its own baked + saved NavMesh. Then it
    /// fills SectionData.sceneName / exitCondition, the RunConfig scene names and Build Settings (Core first) and
    /// validates everything with [TWR] logs. Geometry is cheap greybox (D-100). TWR/Build Test Scene is untouched.
    /// Like the other builders, every asset is loaded/created AFTER EditorSceneManager.NewScene.
    /// R3 (D-172 to D-177, TestSceneBuilder.RunRolls.cs): the R2 spawn points and pickup positions stay as the curated
    /// defaults; every scene gets stable ids, hidden roll-only spawn spots (with partitions that hide them from the
    /// entry), pickup groups with candidate spots, and the puzzle room a 4th switch slot, tally dots and a variant pool.
    /// R5 (D-195 to D-212, TestSceneBuilder.Endings.cs): Core also gets the end-of-run screen, the subtitle line and the
    /// EndingSequence; the four ending scenes and the MainMenu scene are built too, and Build Settings become
    /// MainMenu (index 0), Core, content scenes, ending scenes (D-210).
    /// A1 (D-213 to D-227, TestSceneBuilder.Audio.cs): the audio assets are prepared with the other assets, every AudioSource
    /// made here is routed to its mixer state group, Core's ambient bed gets the RunAmbienceDriver, each content and ending
    /// scene gets its reverb zones, and the [TWR] audio validation runs at the end.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string RunScenesFolder = Root + "/Scenes/Run";
        const string CoreSceneName = "Core";
        const string LobbySceneName = "Lobby";
        const string HealthItemPath = Root + "/Data/Items/Item_Health.asset";
        const string SoundscapeSettingsPath = Root + "/Data/Audio/SoundscapeSettings.asset";

        // Every run door (entry, exit, fork) has this opening: same as the slice puzzle exit door (D-127).
        const float RunDoorWidth = 1.6f;
        const float RunDoorHeight = 2.6f;
        // Entry vestibule: 4 x 4 m, 3.2 m high (D-165).
        const float VestibuleWidth = 4f;
        const float VestibuleDepth = 4f;
        const float VestibuleHeight = 3.2f;

        /// Content scenes in Build Settings order (Core goes first, D-161).
        static readonly string[] RunContentScenes =
        {
            LobbySceneName, "Section_Puzzle", "Section_Combat", "Section_ImpossibleGeometry", "Section_Stealth",
            "Section_Silent", "Fork", "Boss_Escape", "Boss_Discover",
        };

        /// SectionData asset (Data/Run/Sections) -> its scene.
        static readonly (string asset, string scene)[] SectionScenes =
        {
            ("Section_Puzzle", "Section_Puzzle"),
            ("Section_Combat", "Section_Combat"),
            ("Section_Stealth", "Section_Stealth"),
            ("Section_ImpossibleGeometry", "Section_ImpossibleGeometry"),
            ("Section_Silent", "Section_Silent"),
            ("Section_ForkRoom", "Fork"),
            ("Section_BossArena_Escape_Bull", "Boss_Escape"),
            ("Section_BossArena_Discover_Preacher", "Boss_Discover"),
        };

        static string RunScenePath(string sceneName) => RunScenesFolder + "/" + sceneName + ".unity";

        struct RunSceneAssets
        {
            public int interactableLayer;
            public PlayerTuning tuning;
            public InputActionAsset input;
            public CombatAssets combat;
            public PuzzleAssets puzzle;
            public SeekerAssets seeker;
            public PortalAssets portal;
            public RunConfig config;
            public HealthItemData health;
            public SoundscapeSettings soundscape;
            public AudioAssets audio;            // A1 (D-219 to D-227)
            public BullData bull;
            public PreacherData preacher;
            public BossSoundSet bullSounds, preacherSounds;
            public Material white, whiteEmissive, offWhite, glow, door, blackDoor, lockLight, dark, ammo, wood, plant, target;
            public Material healthBox, healthCross, signPlate, exitSign, skin, eye;
            public Material cracked, orb, horn;
        }

        struct RunDoor
        {
            public Door door;
            public DoorLock doorLock;
            public Transform panelRoot;
        }

        struct ExitPassage
        {
            public RunDoor door;
            public SectionExit exit;
            public SectionExitGate gate;
        }

        // ---------- menu ----------

        [MenuItem("TWR/Run/Build Run Scenes")]
        public static void BuildRunScenesMenu()
        {
            // TMP Essentials + localization tables first (adds the R2 strings), then the scenes.
            ProjectSetup.Run(BuildRunScenesAfterSetup);
        }

        static void BuildRunScenesAfterSetup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Debug.Log("[TWR] Building the run scenes (R2 to R5, D-161 to D-212; audio A1, D-213 to D-227) in " + RunScenesFolder + " ...");
            EnsureFolder(RunScenesFolder);
            seekerPrefabBuiltThisPass = false;
            reportedAudioProblems.Clear(); // A1: report each missing mixer part once per build

            bool built = true;
            built &= BuildRunScene(CoreSceneName, BuildCoreScene, false);
            built &= BuildRunScene(LobbySceneName, BuildLobbyScene, true);
            built &= BuildRunScene("Section_Puzzle", BuildPuzzleSectionScene, true);
            built &= BuildRunScene("Section_Combat", BuildCombatSectionScene, true);
            built &= BuildRunScene("Section_Stealth", BuildStealthSectionScene, true);
            built &= BuildRunScene("Section_ImpossibleGeometry", BuildImpossibleSectionScene, true);
            built &= BuildRunScene("Section_Silent", BuildSilentSectionScene, true);
            built &= BuildRunScene("Fork", BuildForkScene, true);
            built &= BuildRunScene("Boss_Escape", BuildBossEscapeScene, true);
            built &= BuildRunScene("Boss_Discover", BuildBossDiscoverScene, true);
            built &= BuildEndingScenesAndMenu(); // R5: endings + main menu (D-195 to D-202)

            bool configured = ConfigureRunAssetsForScenes();
            configured &= ConfigureEndingAssets(); // ending catalog + main menu in the RunConfig (D-203, D-210)
            ConfigureRunBuildSettings();

            EditorSceneManager.OpenScene(RunScenePath(CoreSceneName), OpenSceneMode.Single);
            bool valid = ValidateRunSetup();
            valid &= ValidateRunScenes();
            valid &= ValidateEndings();
            valid &= ValidateAudio(); // A1 (D-227)

            if (built && configured && valid)
                Debug.Log("[TWR] Run scenes built and valid. Press Play in " + RunScenePath(CoreSceneName) + " (open) for a run, or in " +
                          RunScenePath(MainMenuSceneName) + " for the full game flow.");
            else
                Debug.LogError("[TWR] Run scenes built with problems (see errors above).");
        }

        static bool BuildRunScene(string sceneName, System.Func<bool> build, bool bakeNavMesh)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool ok = build();
            if (ok) AddReverbZones(sceneName); // A1: reverb per section template (D-224); none in Core / MainMenu
            if (ok && bakeNavMesh)
            {
                // Each section bakes its own NavMesh, saved next to the scene (D-135, D-161).
                NavMeshSurface surface = BakeAndSaveNavMesh(RunScenesFolder + "/" + sceneName);
                ok &= surface != null && surface.navMeshData != null;
            }
            bool saved = EditorSceneManager.SaveScene(scene, RunScenePath(sceneName));
            if (ok && saved) Debug.Log("[TWR] Built " + RunScenePath(sceneName));
            else Debug.LogError($"[TWR] Problems while building {RunScenePath(sceneName)} (saved: {saved}).");
            return ok && saved;
        }

        // ---------- assets ----------

        /// Everything the run scenes use. Call AFTER NewScene (see the class summary).
        static bool PrepareRunSceneAssets(out RunSceneAssets a, bool withSeeker)
        {
            a = default;
            if (!PrepareSharedAssets(out a.interactableLayer, out a.tuning, out a.input)) return false;
            if (!PrepareCombatAssets(out a.combat)) return false;
            if (!PreparePuzzleAssets(out a.puzzle)) return false;
            if (withSeeker && !PrepareSeekerAssetsOncePerBuild(out a.seeker)) return false;
            if (!PreparePortalAssets(out a.portal)) return false;
            if (!PrepareRunAssets(out a.config)) return false;

            EnsureFolder(Root + "/Data/Audio");
            a.health = LoadOrCreate<HealthItemData>(HealthItemPath, h =>
            {
                h.itemId = "health";
                h.maxStack = 1;
                h.pickupAmount = 1;        // one use per pickup
                h.pickupNoiseRadius = 2f;  // quiet, like ammo (D-121)
                h.healAmount = 25f;        // D-171 default
            });
            a.soundscape = LoadOrCreate<SoundscapeSettings>(SoundscapeSettingsPath, null);
            if (!PrepareAudioAssets(out a.audio)) // A1: placeholders, library, routing, reverb presets (D-219 to D-227)
            {
                Debug.LogError("[TWR] Could not create the audio assets (A1).");
                return false;
            }
            if (a.health == null || a.soundscape == null || !PrepareBossAssets(ref a)) // R4 BossData (TestSceneBuilder.BossArenas.cs)
            {
                Debug.LogError("[TWR] Could not create the run assets (health item, soundscape settings, boss data).");
                return false;
            }
            if (a.health.displayName == null || a.health.displayName.IsEmpty)
            {
                a.health.displayName = new LocalizedString("UI", "item.health.name");
                EditorUtility.SetDirty(a.health);
            }

            a.white = GetOrCreateMaterial("M_White", new Color(0.95f, 0.95f, 0.95f), Color.black);
            a.whiteEmissive = GetOrCreateMaterial("M_WhiteEmissive", Color.white, Color.white * 1.6f);
            a.offWhite = GetOrCreateMaterial("M_OffWhite", new Color(0.85f, 0.85f, 0.83f), Color.black);
            a.glow = GetOrCreateMaterial("M_LobbyGlow", Color.white, Color.white * 2.4f);
            a.door = GetOrCreateMaterial("M_Door", new Color(0.78f, 0.78f, 0.8f), Color.black);
            a.blackDoor = GetOrCreateMaterial("M_BlackDoor", new Color(0.015f, 0.015f, 0.015f), Color.black);
            a.lockLight = GetOrCreateMaterial("M_DoorLockLight", new Color(1f, 0.08f, 0.05f), new Color(1f, 0.08f, 0.05f));
            a.dark = GetOrCreateMaterial("M_DarkCavity", new Color(0.02f, 0.02f, 0.02f), Color.black);
            a.ammo = GetOrCreateMaterial("M_AmmoBox", new Color(0.35f, 0.45f, 0.2f), new Color(0.1f, 0.15f, 0.05f));
            a.wood = GetOrCreateMaterial("M_PaleWood", new Color(0.75f, 0.68f, 0.58f), Color.black);
            a.plant = GetOrCreateMaterial("M_PlantGreen", new Color(0.35f, 0.55f, 0.35f), Color.black);
            a.target = GetOrCreateMaterial("M_ShootTarget", new Color(0.2f, 0.5f, 1f), new Color(0.1f, 0.25f, 0.5f));
            a.healthBox = GetOrCreateMaterial("M_HealthBox", new Color(0.95f, 0.95f, 0.95f), new Color(0.2f, 0.2f, 0.2f));
            a.healthCross = GetOrCreateMaterial("M_HealthCross", new Color(0.85f, 0.08f, 0.08f), new Color(0.6f, 0.04f, 0.04f));
            a.signPlate = GetOrCreateMaterial("M_SignPlateWhite", Color.white, Color.white * 0.8f);
            a.exitSign = GetOrCreateMaterial("M_ExitSignBox", new Color(0.04f, 0.07f, 0.05f), Color.black);
            a.skin = GetOrCreateMaterial("M_SeekerSkin", new Color(0.93f, 0.86f, 0.78f), Color.black);
            a.eye = GetOrCreateMaterial("M_SeekerEye", new Color(0.01f, 0.01f, 0.01f), Color.black);
            a.cracked = GetOrCreateMaterial("M_ColumnCracked", new Color(0.8f, 0.78f, 0.74f), Color.black);   // D-190 breakable columns
            a.orb = GetOrCreateMaterial("M_PreacherOrb", new Color(0.04f, 0.02f, 0.02f), new Color(0.6f, 0.04f, 0.04f)); // D-184 projectiles
            a.horn = GetOrCreateMaterial("M_BoneHorn", new Color(0.9f, 0.88f, 0.8f), Color.black);
            AssetDatabase.SaveAssets();
            return true;
        }

        static bool seekerPrefabBuiltThisPass;

        /// The first section with Seekers rebuilds the Seeker prefab (so code changes reach it); later sections in the
        /// same pass just load it, so the scenes saved earlier keep pointing at the same prefab contents.
        static bool PrepareSeekerAssetsOncePerBuild(out SeekerAssets assets)
        {
            if (seekerPrefabBuiltThisPass)
            {
                assets = default;
                assets.enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
                assets.data = AssetDatabase.LoadAssetAtPath<SeekerData>(SeekerDataPath);
                assets.sounds = AssetDatabase.LoadAssetAtPath<SeekerSoundSet>(SeekerSoundSetPath);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SeekerPrefabPath);
                assets.prefab = prefab != null ? prefab.GetComponent<Seeker>() : null;
                if (assets.data != null && assets.sounds != null && assets.prefab != null) return true;
            }
            seekerPrefabBuiltThisPass = PrepareSeekerAssets(out assets);
            return seekerPrefabBuiltThisPass;
        }

        // ---------- Core (D-161) ----------

        static bool BuildCoreScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, false)) return false;

            BuildGlobalLighting(GetOrCreateVolumeProfile()); // Core is the active scene: its lighting + volume apply (D-141)

            GameObject player = GetOrCreatePlayerInstance(a.tuning, a.input, a.interactableLayer, a.combat, false);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity); // moved to each SectionEntry at runtime
            GameObject hud = BuildHud(player.GetComponent<PlayerInteractor>());
            BuildCombatHud(hud, player);
            BuildBossHud(hud);                          // R4: boss HP bar, hidden until a fight starts (D-185)
            BuildPortalRuntime(a.portal, player);      // PortalRenderManager: portals come and go with the sections
            AmbientBed bed = BuildAmbientBed(player.transform, false); // keeps playing across section loads (D-167, D-223)
            ScreenFader fader = BuildScreenFader();
            BuildSubtitleUi();                          // R5: subtitle line, on by default (D-089)
            EndOfRunScreen endScreen = BuildEndOfRunScreen(); // R5: end-of-run screen (D-200)
            BuildEventSystem();

            RunManager manager = BuildRunBootstrap(a.config);
            var loader = manager.gameObject.AddComponent<SectionLoader>(); // switches the RunManager to Core mode
            Assign(loader, "player", player.GetComponent<PlayerController>());
            Assign(loader, "fader", fader);
            manager.gameObject.AddComponent<VoiceEventLogger>();
            var ambience = bed.gameObject.AddComponent<RunAmbienceDriver>(); // office one-shots per section (D-223)
            Assign(ambience, "bed", bed);
            Assign(ambience, "manager", manager);
            var endings = manager.gameObject.AddComponent<EndingSequence>(); // R5: player mode, fade, end screen (D-207)
            Assign(endings, "endScreen", endScreen);
            Assign(endings, "hudCanvas", hud.GetComponent<Canvas>());
            AssignArray(endings, "gameplayHud", GameplayHudGroups(hud));
            // RunStats and the debug overlay (F1/F2) are added by the RunManager at runtime.
            return true;
        }

        /// Top-most canvas: full-screen white (CanvasGroup alpha = fade) + a centre line for the end screen.
        static ScreenFader BuildScreenFader()
        {
            var canvasGO = new GameObject("Screen Fader (white, D-167)");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // above the HUD
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var group = canvasGO.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform white = UiRect("White", canvasGO.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            white.anchorMin = Vector2.zero;
            white.anchorMax = Vector2.one;
            white.offsetMin = Vector2.zero;
            white.offsetMax = Vector2.zero;
            var image = white.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;

            RectTransform labelRect = UiRect("Message (localized)", canvasGO.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                             Vector2.zero, new Vector2(1400f, 120f));
            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = string.Empty;
            label.fontSize = 64f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.15f, 0.15f, 0.15f);
            label.raycastTarget = false;
            label.enabled = false;

            var fader = canvasGO.AddComponent<ScreenFader>();
            Assign(fader, "group", group);
            Assign(fader, "messageLabel", label);
            Assign(fader, "background", image); // R5: endings fade to white/black/tint (D-196, D-199)
            return fader;
        }

        static void BuildEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>(); // New Input System UI module (D-038); default UI actions are assigned on enable
        }

        // ---------- Lobby (D-163) ----------

        static bool BuildLobbyScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, false)) return false;

            // The slice lobby with its five doors, the portal door and the impossible hall 500 m away (D-145, D-146).
            // The ambient bed lives in Core (D-161).
            BuildLobbyAndPortal(a.portal, a.puzzle, a.white, a.offWhite, false);

            Transform root = new GameObject("Run Lobby (D-163)").transform;
            // No white room behind the lobby: close the lower 4 m of the north wall around the run door.
            WallAlongX(root, "Lobby Wall N (lower, run door)", LobbyMaxZ - 0.1f, LobbyMinX - 0.2f, LobbyMaxX + 0.2f, 4f, a.glow,
                       RunDoorWidth, RunDoorHeight, 0f);
            BuildExitPassage(root, a, "Run Door (to section 1, D-163)", "door.run", new Vector3(0f, 0f, LobbyMaxZ - 0.1f), Vector3.forward,
                             RunPath.None, "lobby.run_door", false, false, SectionExitCondition.None, a.door);

            // The player arrives in front of the permanently locked black door (D-021, D-145): that is the "entry door".
            BuildSectionEntry(root, PlayerStartPosition, Quaternion.identity);

            // The two locked side doors stay locked; their D-060 reservation is superseded by the fork room (D-163).
            RenameIfFound("Door 4 - Path Door A (reserved D-060, locked)", "Door 4 - Locked Door A (permanently locked, may become a run door, D-163)");
            RenameIfFound("Door 5 - Path Door B (reserved D-060, locked)", "Door 5 - Locked Door B (permanently locked, may become a run door, D-163)");
            return true;
        }

        // ---------- Section: puzzle (D-155, D-165 MainPuzzlesSolved) ----------

        static bool BuildPuzzleSectionScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, true)) return false;

            // The slice puzzle room (x 10.2..22.2): 3 pistol switches, ammo, locked exit door + corridor (D-124 to D-130).
            DoorLock exitLock = BuildPuzzleTest(a.combat, a.puzzle, a.white, a.whiteEmissive, a.offWhite);

            Transform root = new GameObject("Section: Puzzle (D-155, D-165)").transform;
            // R3: the R2 Seeker (same spawn + patrol, curated only) plus two hidden roll spots; the 4th switch slot,
            // tally dots, variant pool, puzzle area and the rolled ammo spots (D-172 to D-177).
            if (!AuthorPuzzleSectionRolls(root, a)) return false;
            // West wall of the puzzle room with the 2.4 x 3 m doorway (was the white room's east wall).
            BuildEastWallWithDoorway(a.whiteEmissive, root);
            // Entry vestibule west of the doorway; the one-way door faces east into it.
            BuildEntryVestibule(root, a, new Vector3(5.9f, 0f, 0f), Vector3.right);

            // The exit opens when every main puzzle is solved (D-165). The PuzzleController also unlocks it as before.
            AddExitGate(exitLock, SectionExitCondition.MainPuzzlesSolved);
            GameObject trigger = TriggerVolume("Section Exit (puzzle.exit)", new Vector3(24.2f, 0f, -2.5f), new Vector3(2f, 3f, 3.6f), a.combat, root);
            ConfigureExit(trigger.AddComponent<SectionExit>(), RunPath.None, "puzzle.exit");
            return true;
        }

        // ---------- Section: combat (D-155, D-165 None, D-170 fixed spawns, D-171 health) ----------

        static bool BuildCombatSectionScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, true)) return false;
            Transform root = new GameObject("Section: Combat (D-155)").transform;

            // Room x -10..10, z 4..20, 4.5 m high; exit north at x = 0, never locked (D-165).
            BuildSectionShell(root, a, 20f, 16f, 4.5f, a.whiteEmissive, new[] { 0f }, true, SectionExitCondition.None);

            Transform cover = new GameObject("Cover (greybox)").transform;
            cover.SetParent(root, false);
            Block("Low Cover A", new Vector3(-4f, 0.7f, 8f), new Vector3(1.2f, 1.4f, 1.2f), a.offWhite, cover);
            Block("Low Cover B", new Vector3(4f, 0.7f, 8f), new Vector3(1.2f, 1.4f, 1.2f), a.offWhite, cover);
            Block("Low Wall (centre)", new Vector3(0f, 0.6f, 12f), new Vector3(3f, 1.2f, 0.6f), a.offWhite, cover);
            Block("Tall Cover W", new Vector3(-9f, 1.1f, 13f), new Vector3(1f, 2.2f, 2f), a.offWhite, cover);
            Block("Tall Cover E", new Vector3(9f, 1.1f, 13f), new Vector3(1f, 2.2f, 2f), a.offWhite, cover);

            // The three R2 Seekers (D-170) stay as the curated layout (seed 0); seeded runs pick 3 of 4 hidden spots (D-176).
            Transform enemies = new GameObject("Enemies (R2 curated + R3 roll spots, D-170, D-176)").transform;
            enemies.SetParent(root, false);
            BuildSeekerSpawn(enemies, a, "Seeker A (west loop)", "combat.enemy.r2_west", true, false, new Vector3(-5f, 0f, 17f), Vector3.back,
                new[] { new Vector3(-7f, 0f, 10f), new Vector3(-7f, 0f, 17f), new Vector3(-3f, 0f, 17f), new Vector3(-3f, 0f, 10f) });
            BuildSeekerSpawn(enemies, a, "Seeker B (east loop)", "combat.enemy.r2_east", true, false, new Vector3(5f, 0f, 17f), Vector3.back,
                new[] { new Vector3(7f, 0f, 10f), new Vector3(7f, 0f, 17f), new Vector3(3f, 0f, 17f), new Vector3(3f, 0f, 10f) });
            BuildSeekerSpawn(enemies, a, "Seeker C (exit guard)", "combat.enemy.r2_exit", true, false, new Vector3(0f, 0f, 18.5f), Vector3.back,
                new[] { new Vector3(-2f, 0f, 18.5f), new Vector3(2f, 0f, 18.5f) });
            AuthorCombatRollSpots(root, enemies, a);

            ItemPickup health = HealthPickup("Health Pickup (combat, D-171)", new Vector3(-8.5f, 0f, 6f), a, root);
            ItemPickup ammo = AmmoPickup("Ammo Pickup (combat)", new Vector3(8.5f, 0f, 6f), a.combat, a.ammo, root);
            BuildPickupGroup(root, "combat.health", new[] { health }, "", new[]
            {
                ("combat.health.sw", new Vector3(-8.5f, 0f, 6f), true),
                ("combat.health.se", new Vector3(8.5f, 0f, 10f), false),
                ("combat.health.n", new Vector3(-1.5f, 0f, 16f), false),
                ("combat.health.ne", new Vector3(6.5f, 0f, 15f), false),
            });
            BuildPickupGroup(root, "combat.ammo", new[] { ammo }, "", new[]
            {
                ("combat.ammo.se", new Vector3(8.5f, 0f, 6f), true),
                ("combat.ammo.w", new Vector3(-6f, 0f, 12.5f), false),
                ("combat.ammo.mid", new Vector3(2.5f, 0f, 10f), false),
                ("combat.ammo.nw", new Vector3(-3f, 0f, 18.8f), false),
            });
            return true;
        }

        // ---------- Section: stealth (D-169) ----------

        static bool BuildStealthSectionScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, true)) return false;
            Transform root = new GameObject("Section: Stealth (D-169)").transform;

            // Room x -12..12, z 4..22, 4 m high; exit in the north-west corner (x -10.75), never locked (D-165, D-169).
            const float exitX = -10.75f;
            BuildSectionShell(root, a, 24f, 18f, 4f, a.whiteEmissive, new[] { exitX }, true, SectionExitCondition.None);

            // Cover route: a side corridor along the west wall behind a full-height partition, with a low crouch-only
            // stretch (1.3 m) the Seekers can't walk through (their NavMesh needs 2 m).
            Transform route = new GameObject("Crouch / Cover Route (west side, D-169)").transform;
            route.SetParent(root, false);
            Block("Partition Wall", new Vector3(-9.4f, 2f, 13f), new Vector3(0.2f, 4f, 14f), a.whiteEmissive, route);
            Block("Crouch Ceiling (1.3 m)", new Vector3(-10.75f, 2.65f, 12.5f), new Vector3(2.5f, 2.7f, 3f), a.offWhite, route);

            Transform cover = new GameObject("Cover (greybox)").transform;
            cover.SetParent(root, false);
            Block("Tall Cover S", new Vector3(0f, 1.1f, 6.5f), new Vector3(1.5f, 2.2f, 1f), a.offWhite, cover);
            Block("Tall Cover Middle", new Vector3(-1f, 1.1f, 14.5f), new Vector3(1.2f, 2.2f, 1.2f), a.offWhite, cover);
            Block("Tall Cover E", new Vector3(8.5f, 1.1f, 12f), new Vector3(1.2f, 2.2f, 1.2f), a.offWhite, cover);

            // Two patrolling Seekers over the main route (D-169). The R2 pair is the curated layout (seed 0); seeded runs
            // pick 2 of 3 hidden spots whose patrols all cross the main room, so any pair satisfies D-169 (D-176).
            Transform enemies = new GameObject("Enemies (R2 curated + R3 roll spots, D-169, D-176)").transform;
            enemies.SetParent(root, false);
            BuildSeekerSpawn(enemies, a, "Seeker A (big loop)", "stealth.enemy.r2_loop", true, false, new Vector3(6f, 0f, 17f), Vector3.left,
                new[] { new Vector3(-4f, 0f, 8f), new Vector3(6f, 0f, 8f), new Vector3(6f, 0f, 17f), new Vector3(-4f, 0f, 17f) });
            BuildSeekerSpawn(enemies, a, "Seeker B (cross patrol)", "stealth.enemy.r2_cross", true, false, new Vector3(-6f, 0f, 12f), Vector3.right,
                new[] { new Vector3(-6f, 0f, 12f), new Vector3(4f, 0f, 12f) });
            AuthorStealthRollSpots(root, enemies, a);

            ItemPickup health = HealthPickup("Health Pickup (stealth route, D-171)", new Vector3(exitX, 0f, 7.5f), a, root);
            ItemPickup ammo = AmmoPickup("Ammo Pickup (stealth)", new Vector3(10.5f, 0f, 6f), a.combat, a.ammo, root);
            BuildPickupGroup(root, "stealth.health", new[] { health }, "", new[]
            {
                ("stealth.health.route_s", new Vector3(exitX, 0f, 7.5f), true),
                ("stealth.health.route_n", new Vector3(exitX, 0f, 17.5f), false),
                ("stealth.health.n", new Vector3(4f, 0f, 20.8f), false),
                ("stealth.health.e", new Vector3(10.5f, 0f, 9f), false),
            });
            BuildPickupGroup(root, "stealth.ammo", new[] { ammo }, "", new[]
            {
                ("stealth.ammo.se", new Vector3(10.5f, 0f, 6f), true),
                ("stealth.ammo.sw", new Vector3(-7.5f, 0f, 6f), false),
                ("stealth.ammo.mid", new Vector3(3f, 0f, 14f), false),
                ("stealth.ammo.nw", new Vector3(-7f, 0f, 20.8f), false),
            });
            return true;
        }

        // ---------- Section: impossible geometry (D-164) ----------

        static bool BuildImpossibleSectionScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, false)) return false;
            Transform root = new GameObject("Section: Impossible Geometry (D-164)").transform;

            // Room x -6..6, z 4..18, 5 m high, no exit of its own: the exit is in the hall behind the portal.
            BuildSectionShell(root, a, 12f, 14f, 5f, a.whiteEmissive, new float[0], false, SectionExitCondition.None);

            // Its own portal pair (same parts as D-146): a freestanding portal door facing the entry, and the 30 x 20 m
            // hall 500 m east (>= 200 m apart, separate NavMesh, D-144) with the always-open return portal.
            Portal roomPortal = BuildPortalDoor(a.portal, a.offWhite, a.glow, root, "Portal Door (freestanding, D-164)",
                                                new Vector3(0f, 0f, 12f), 180f, "ig.portal", "door.ig_portal");
            Transform hall = new GameObject("Impossible Hall (+500 m X, D-054, D-164)").transform;
            hall.SetParent(root, false);
            Portal hallPortal = BuildHall(a.portal, hall, a.white, a.offWhite, a.wood, a.plant, a.target, "ig.hall.return", HallPortalPosition.z);
            Assign(roomPortal, "destination", hallPortal);
            Assign(hallPortal, "destination", roomPortal);

            // The section exit is inside the hall (east wall), so crossing the portal is required (D-164).
            BuildExitPassage(root, a, "Exit (in the hall, D-164)", "door.exit", new Vector3(HallMaxX + 0.1f, 0f, HallPortalPosition.z),
                             Vector3.right, RunPath.None, "impossible.exit", true, true, SectionExitCondition.None, a.door);
            return true;
        }

        // ---------- Section: silent (D-168) ----------

        static bool BuildSilentSectionScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, false)) return false;
            Transform root = new GameObject("Section: Silent (D-168)").transform;

            // A tall, empty, very bright room (D-064, D-082). Exit never locked (D-165).
            BuildSectionShell(root, a, 12f, 12f, 6f, a.glow, new[] { 0f }, true, SectionExitCondition.None);

            Transform props = new GameObject("Props (office motifs, D-062)").transform;
            props.SetParent(root, false);
            Desk("Lone Desk", new Vector3(-3f, 0f, 10f), props, a.offWhite, a.wood);
            Prop("Chair Facing The Wall", PrimitiveType.Cube, props, new Vector3(3.5f, 0.45f, 13.5f), new Vector3(0.5f, 0.08f, 0.5f), a.offWhite, Vector3.zero, true);
            Prop("Chair Back", PrimitiveType.Cube, props, new Vector3(3.5f, 0.8f, 13.25f), new Vector3(0.5f, 0.6f, 0.06f), a.offWhite, Vector3.zero, true);

            var silentGO = new GameObject("Silent Room (ambient fades out, voice suppressed, D-168)");
            silentGO.transform.SetParent(root, false);
            Assign(silentGO.AddComponent<SilentSection>(), "settings", a.soundscape);
            return true;
        }

        // ---------- Fork room (D-149, D-150, D-166) ----------

        static bool BuildForkScene()
        {
            if (!PrepareRunSceneAssets(out RunSceneAssets a, false)) return false;
            Transform root = new GameObject("Fork Room (D-149, D-150, D-166)").transform;

            const float depth = 10f;
            const float escapeX = -3.5f, discoverX = 3.5f;
            BuildSectionShell(root, a, 14f, depth, 5f, a.whiteEmissive, new[] { escapeX, discoverX }, false, SectionExitCondition.None);
            float doorZ = VestibuleDepth + depth + 0.1f;

            // Two ordinary unlocked doors (opening commits nothing); walking through one commits the path (D-166).
            ExitPassage escape = BuildExitPassage(root, a, "Escape Door (EXIT sign, D-150)", "door.fork.exit",
                new Vector3(escapeX, 0f, doorZ), Vector3.forward, RunPath.Escape, "fork.escape", false, false, SectionExitCondition.None, a.door);
            ExitPassage discover = BuildExitPassage(root, a, "Discover Door (black, BASEMENT plate, D-150)", "door.fork.basement",
                new Vector3(discoverX, 0f, doorZ), Vector3.forward, RunPath.Discover, "fork.discover", false, false, SectionExitCondition.None, a.blackDoor);

            // EXIT sign above the Escape door frame; white BASEMENT plate on the black door's upper middle (moves with it).
            Transform escapeDoor = escape.door.door.transform; // local +Z = into the room
            LocalBlock("EXIT Sign Box", escapeDoor, new Vector3(0f, 3.3f, 0.16f), new Vector3(0.9f, 0.32f, 0.1f), a.exitSign, false, false);
            WorldText("EXIT Sign Text", escapeDoor, new Vector3(0f, 3.3f, 0.215f), new Vector2(0.8f, 0.26f),
                      new LocalizedString("UI", "sign.exit"), new Color(0.3f, 1f, 0.4f));
            Transform plateParent = discover.door.panelRoot;
            LocalBlock("BASEMENT Plate", plateParent, new Vector3(0f, 2.1f, 0.07f), new Vector3(0.8f, 0.22f, 0.02f), a.signPlate, false, false);
            WorldText("BASEMENT Plate Text", plateParent, new Vector3(0f, 2.1f, 0.085f), new Vector2(0.72f, 0.17f),
                      new LocalizedString("UI", "sign.basement"), new Color(0.02f, 0.02f, 0.02f));

            // Inner-voice hooks when the player first comes close to each door (D-080, D-166). Placeholder subtitles.
            GameObject seenExit = TriggerVolume("Voice: EXIT door seen", new Vector3(escapeX, 0f, doorZ - 2.6f), new Vector3(3f, 3f, 3f), a.combat, root);
            seenExit.AddComponent<ForkDoorSeenTrigger>().Configure("fork.exit", new LocalizedString("Subtitles", "fork.exit_door"));
            GameObject seenBasement = TriggerVolume("Voice: BASEMENT door seen", new Vector3(discoverX, 0f, doorZ - 2.6f), new Vector3(3f, 3f, 3f), a.combat, root);
            seenBasement.AddComponent<ForkDoorSeenTrigger>().Configure("fork.basement", new LocalizedString("Subtitles", "fork.basement_door"));

            var forkGO = new GameObject("Fork Room Controller (locks the door not taken, D-166)");
            forkGO.transform.SetParent(root, false);
            var fork = forkGO.AddComponent<ForkRoom>();
            Assign(fork, "escapeExit", escape.exit);
            Assign(fork, "escapeDoor", escape.door.door);
            Assign(fork, "escapeLock", escape.door.doorLock);
            Assign(fork, "discoverExit", discover.exit);
            Assign(fork, "discoverDoor", discover.door.door);
            Assign(fork, "discoverLock", discover.door.doorLock);

            ItemPickup health = HealthPickup("Health Pickup (fork room, D-171)", new Vector3(5.5f, 0f, 6.5f), a, root);
            BuildPickupGroup(root, "fork.health", new[] { health }, "", new[]
            {
                ("fork.health.se", new Vector3(5.5f, 0f, 6.5f), true),
                ("fork.health.sw", new Vector3(-5.5f, 0f, 6.5f), false),
                ("fork.health.nw", new Vector3(-6f, 0f, 11f), false),
                ("fork.health.ne", new Vector3(6f, 0f, 11f), false),
            });
            return true;
        }

        // ---------- shared section pieces ----------

        /// Standard layout (world = local, the entry door at the origin): entry vestibule z 0..4 (x -2..2), main room
        /// x -width/2..width/2, z 4..4+depth, openings in the north wall at northDoorXs. withStandardExit = an exit door
        /// at northDoorXs[0] with its gate (fallback condition) and the SectionExit corridor behind it.
        static void BuildSectionShell(Transform root, RunSceneAssets a, float width, float depth, float height, Material wallMat,
                                      float[] northDoorXs, bool withStandardExit, SectionExitCondition fallback)
        {
            BuildEntryVestibule(root, a, Vector3.zero, Vector3.forward);

            float minX = -width * 0.5f, maxX = width * 0.5f, minZ = VestibuleDepth, maxZ = VestibuleDepth + depth;
            Transform shell = new GameObject("Room Shell").transform;
            shell.SetParent(root, false);
            float floorStart = minZ - 0.1f, floorEnd = maxZ + 0.2f;
            Block("Floor", new Vector3(0f, -0.1f, (floorStart + floorEnd) * 0.5f), new Vector3(width + 0.4f, 0.2f, floorEnd - floorStart), a.white, shell);
            Block("Ceiling", new Vector3(0f, height + 0.1f, (floorStart + floorEnd) * 0.5f), new Vector3(width + 0.4f, 0.2f, floorEnd - floorStart), wallMat, shell);
            WallAlongZ(shell, "Wall W", minX - 0.1f, minZ - 0.1f, maxZ + 0.2f, height, wallMat, 0f, 0f);
            WallAlongZ(shell, "Wall E", maxX + 0.1f, minZ - 0.1f, maxZ + 0.2f, height, wallMat, 0f, 0f);
            // South wall: open over the vestibule's full width and height.
            WallAlongX(shell, "Wall S", minZ, minX - 0.2f, maxX + 0.2f, height, wallMat, VestibuleWidth, VestibuleHeight, 0f);
            WallAlongX(shell, "Wall N", maxZ + 0.1f, minX - 0.2f, maxX + 0.2f, height, wallMat, RunDoorWidth, RunDoorHeight, northDoorXs);

            if (withStandardExit && northDoorXs.Length > 0)
                BuildExitPassage(root, a, "Exit", "door.exit", new Vector3(northDoorXs[0], 0f, maxZ + 0.1f), Vector3.forward,
                                 RunPath.None, "exit", true, true, fallback, a.door);
        }

        /// 4 x 4 x 3.2 m vestibule. doorFloorPoint = the entry door in its back wall; inward = into the section. The
        /// front side is open (the room's wall provides the opening). The one-way entry door is permanently locked
        /// (D-145 mode, D-165) and the SectionEntry stands 1.6 m in front of it, facing inward.
        static void BuildEntryVestibule(Transform parent, RunSceneAssets a, Vector3 doorFloorPoint, Vector3 inward)
        {
            var frame = new GameObject("Entry Vestibule (one-way, D-165)").transform;
            frame.SetParent(parent, false);
            frame.SetPositionAndRotation(doorFloorPoint, Quaternion.LookRotation(inward));

            float half = VestibuleWidth * 0.5f, h = VestibuleHeight, mid = VestibuleDepth * 0.5f, length = VestibuleDepth + 0.2f;
            LocalBlock("Floor", frame, new Vector3(0f, -0.1f, mid), new Vector3(VestibuleWidth + 0.4f, 0.2f, length), a.white, true, true);
            LocalBlock("Ceiling", frame, new Vector3(0f, h + 0.1f, mid), new Vector3(VestibuleWidth + 0.4f, 0.2f, length), a.whiteEmissive, true, true);
            LocalBlock("Wall L", frame, new Vector3(-half - 0.1f, h * 0.5f, mid), new Vector3(0.2f, h + 0.2f, length), a.whiteEmissive, true, true);
            LocalBlock("Wall R", frame, new Vector3(half + 0.1f, h * 0.5f, mid), new Vector3(0.2f, h + 0.2f, length), a.whiteEmissive, true, true);

            // Back wall with the entry door opening.
            float side = half + 0.2f - RunDoorWidth * 0.5f;
            float sideCentre = RunDoorWidth * 0.5f + side * 0.5f;
            LocalBlock("Back Wall L", frame, new Vector3(-sideCentre, h * 0.5f, 0f), new Vector3(side, h, 0.2f), a.whiteEmissive, true, true);
            LocalBlock("Back Wall R", frame, new Vector3(sideCentre, h * 0.5f, 0f), new Vector3(side, h, 0.2f), a.whiteEmissive, true, true);
            LocalBlock("Back Wall Lintel", frame, new Vector3(0f, (RunDoorHeight + h) * 0.5f, 0f), new Vector3(RunDoorWidth, h - RunDoorHeight, 0.2f), a.whiteEmissive, true, true);

            BuildRunDoor("Entry Door (one-way, permanently locked)", "door.entry", frame, Vector3.zero, Quaternion.identity,
                         a.puzzle.door, a.door, a.lockLight, true, true, a.interactableLayer);
            BuildSectionEntry(frame, new Vector3(0f, 0.05f, 1.6f), Quaternion.identity, true);
        }

        /// A door in a wall (the wall itself is built by the caller) + a 2.4 x 4 m corridor behind it ending in a dark
        /// opening (D-065), with a SectionExit trigger 1.6 to 3.6 m past the door (D-165). outward = from the room
        /// through the door. addGate = a SectionExitGate on the door (D-165).
        static ExitPassage BuildExitPassage(Transform parent, RunSceneAssets a, string name, string doorId, Vector3 doorFloorPoint,
                                            Vector3 outward, RunPath path, string exitId, bool startsLocked, bool addGate,
                                            SectionExitCondition fallback, Material panelMat)
        {
            var frame = new GameObject(name).transform;
            frame.SetParent(parent, false);
            frame.SetPositionAndRotation(doorFloorPoint, Quaternion.LookRotation(outward));

            var passage = new ExitPassage();
            // The door faces back into the room (its +Z), like every other D-127 door.
            passage.door = BuildRunDoor("Door", doorId, frame, Vector3.zero, Quaternion.Euler(0f, 180f, 0f), a.puzzle.door, panelMat,
                                        a.lockLight, startsLocked, false, a.interactableLayer);

            LocalBlock("Corridor Floor", frame, new Vector3(0f, -0.1f, 2.15f), new Vector3(2.8f, 0.2f, 4.1f), a.white, true, true);
            LocalBlock("Corridor Ceiling", frame, new Vector3(0f, 3.3f, 2.15f), new Vector3(2.8f, 0.2f, 4.1f), a.whiteEmissive, true, true);
            LocalBlock("Corridor Wall L", frame, new Vector3(-1.3f, 1.6f, 2.15f), new Vector3(0.2f, 3.4f, 4.1f), a.whiteEmissive, true, true);
            LocalBlock("Corridor Wall R", frame, new Vector3(1.3f, 1.6f, 2.15f), new Vector3(0.2f, 3.4f, 4.1f), a.whiteEmissive, true, true);
            LocalBlock("Corridor End", frame, new Vector3(0f, 1.6f, 4.3f), new Vector3(2.8f, 3.4f, 0.2f), a.whiteEmissive, true, true);
            LocalBlock("Dark Opening (D-065)", frame, new Vector3(0f, 1.2f, 4.19f), new Vector3(1.2f, 2.2f, 0.02f), a.dark, false, false);

            GameObject trigger = TriggerVolume("Section Exit (" + exitId + ")", frame.TransformPoint(new Vector3(0f, 0f, 2.6f)),
                                               new Vector3(2.4f, 3f, 2f), a.combat, frame);
            passage.exit = trigger.AddComponent<SectionExit>();
            ConfigureExit(passage.exit, path, exitId);

            if (addGate) passage.gate = AddExitGate(passage.door.doorLock, fallback);
            return passage;
        }

        /// Slide door (D-127) whose moving part is an unscaled "Panel" (signs and plates can ride on it) holding the
        /// 1.6 x 2.6 m slab. root local +Z = the side the player opens it from. Lock light above (red/green, D-065).
        static RunDoor BuildRunDoor(string name, string doorId, Transform parent, Vector3 localPosition, Quaternion localRotation,
                                    DoorData data, Material panelMat, Material lockMat, bool startsLocked, bool permanentlyLocked,
                                    int interactableLayer)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = localRotation;
            var audio = root.AddComponent<AudioSource>();
            ConfigureSfxSource3D(audio, 1f, MechanismAudioMaxDistance); // mechanisms -> SFXState (D-219)

            var panelRoot = new GameObject("Panel");
            panelRoot.transform.SetParent(root.transform, false);
            var body = panelRoot.AddComponent<Rigidbody>(); // kinematic: a moving collider without physics pushes
            body.isKinematic = true;
            body.useGravity = false;
            GameObject slab = PuzzlePart("Panel Slab", panelRoot.transform, new Vector3(0f, RunDoorHeight * 0.5f, 0f),
                                         new Vector3(RunDoorWidth, RunDoorHeight, 0.12f), panelMat);
            slab.layer = interactableLayer; // F finds it; bullets stop on it like a wall
            slab.AddComponent<NoiseOccluder>().Kind = NoiseOccluderKind.DoorPanel; // D-131
            var obstacle = slab.AddComponent<NavMeshObstacle>(); // D-135: a closed door blocks Seeker paths
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;

            Renderer light = null;
            if (lockMat != null)
                light = LocalBlock("Lock Light", root.transform, new Vector3(0f, RunDoorHeight + 0.25f, 0.13f), new Vector3(0.4f, 0.12f, 0.06f),
                                   lockMat, false, false).GetComponent<Renderer>();

            var doorLock = root.AddComponent<DoorLock>();
            Assign(doorLock, "data", data);
            if (light != null) Assign(doorLock, "lockLight", light);
            var lockSo = new SerializedObject(doorLock);
            lockSo.FindProperty("startsLocked").boolValue = startsLocked || permanentlyLocked;
            lockSo.FindProperty("permanentlyLocked").boolValue = permanentlyLocked;
            lockSo.ApplyModifiedPropertiesWithoutUndo();

            var door = root.AddComponent<Door>();
            Assign(door, "data", data);
            Assign(door, "doorLock", doorLock);
            Assign(door, "panel", panelRoot.transform);
            var doorSo = new SerializedObject(door);
            doorSo.FindProperty("interactionId").stringValue = doorId;
            doorSo.ApplyModifiedPropertiesWithoutUndo();

            return new RunDoor { door = door, doorLock = doorLock, panelRoot = panelRoot.transform };
        }

        static SectionExitGate AddExitGate(DoorLock doorLock, SectionExitCondition fallback)
        {
            var gate = doorLock.gameObject.AddComponent<SectionExitGate>();
            Assign(gate, "exitLock", doorLock);
            var so = new SerializedObject(gate);
            so.FindProperty("fallbackCondition").enumValueIndex = (int)fallback;
            so.ApplyModifiedPropertiesWithoutUndo();
            return gate;
        }

        static void ConfigureExit(SectionExit exit, RunPath path, string exitId)
        {
            var so = new SerializedObject(exit);
            so.FindProperty("path").enumValueIndex = (int)path;
            so.FindProperty("exitId").stringValue = exitId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildSectionEntry(Transform parent, Vector3 position, Quaternion rotation, bool local = false)
        {
            var go = new GameObject("Section Entry (spawn, D-165)");
            go.transform.SetParent(parent, false);
            if (local)
            {
                go.transform.localPosition = position;
                go.transform.localRotation = rotation;
            }
            else go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<SectionEntry>();
        }

        /// Spawn point + its own patrol waypoints (world positions). The Seeker spawns into this scene at runtime
        /// (D-135, D-170). R3 (D-172, D-176): spotId = stable id; isDefault = curated layout (seed 0); allowInRolls =
        /// may be picked by a seeded roll (only spots that pass the D-176 rules; the validation checks them).
        static SeekerSpawnPoint BuildSeekerSpawn(Transform parent, RunSceneAssets a, string name, string spotId, bool isDefault,
                                                 bool allowInRolls, Vector3 position, Vector3 facing, Vector3[] route)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            var waypoints = new Transform[route.Length];
            for (int i = 0; i < route.Length; i++)
            {
                waypoints[i] = new GameObject("Waypoint " + (i + 1)).transform;
                waypoints[i].SetParent(group, false);
                waypoints[i].position = route[i];
            }
            var spawnGO = new GameObject("Seeker Spawn Point");
            spawnGO.transform.SetParent(group, false);
            spawnGO.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));
            var spawn = spawnGO.AddComponent<SeekerSpawnPoint>();
            Assign(spawn, "seekerPrefab", a.seeker.prefab);
            AssignArray(spawn, "patrolRoute", waypoints);
            var so = new SerializedObject(spawn);
            so.FindProperty("spotId").stringValue = spotId;
            so.FindProperty("isDefault").boolValue = isDefault;
            so.FindProperty("allowInRolls").boolValue = allowInRolls;
            so.ApplyModifiedPropertiesWithoutUndo();
            return spawn;
        }

        /// Walk-over health pickup (D-171): the ItemPickup flow with the health item (heals 25, not used at full HP).
        static ItemPickup HealthPickup(string name, Vector3 position, RunSceneAssets a, Transform parent)
        {
            GameObject root = TriggerVolume(name, position, new Vector3(0.8f, 1f, 0.8f), a.combat, parent);
            var pickup = root.AddComponent<ItemPickup>();
            Assign(pickup, "item", a.health);
            Transform box = GunPart("Box", root.transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.36f, 0.24f, 0.36f), Vector3.zero, a.healthBox);
            Transform crossA = GunPart("Cross A", root.transform, new Vector3(0f, 0.245f, 0f), new Vector3(0.26f, 0.012f, 0.08f), Vector3.zero, a.healthCross);
            Transform crossB = GunPart("Cross B", root.transform, new Vector3(0f, 0.245f, 0f), new Vector3(0.08f, 0.012f, 0.26f), Vector3.zero, a.healthCross);
            box.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (Transform t in new[] { box, crossA, crossB }) t.gameObject.layer = a.combat.ignoreRaycastLayer;
            return pickup;
        }

        /// World-space TextMeshPro label with a LocalizedWorldText (D-084). localPosition is in parent space; the text
        /// faces the parent's +Z side (TMP text is read from its -Z side, so it is turned 180 degrees).
        static void WorldText(string name, Transform parent, Vector3 localPosition, Vector2 size, LocalizedString text, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = size;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.1f;
            tmp.fontSizeMax = 12f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = colour;
            tmp.text = "[" + text.TableEntryReference.Key + "]"; // editor preview only; replaced by the localized text at runtime
            var localized = go.AddComponent<LocalizedWorldText>();
            localized.Text = text;
            EditorUtility.SetDirty(localized);
        }

        /// A straight wall along X at z (0.2 m thick, from the floor up to height) with door-sized gaps at openingCenters.
        static void WallAlongX(Transform parent, string name, float z, float minX, float maxX, float height, Material material,
                               float openingWidth, float openingHeight, params float[] openingCenters)
        {
            Wall(parent, name, true, z, minX, maxX, height, material, openingWidth, openingHeight, openingCenters);
        }

        /// The same along Z at x.
        static void WallAlongZ(Transform parent, string name, float x, float minZ, float maxZ, float height, Material material,
                               float openingWidth, float openingHeight, params float[] openingCenters)
        {
            Wall(parent, name, false, x, minZ, maxZ, height, material, openingWidth, openingHeight, openingCenters);
        }

        static void Wall(Transform parent, string name, bool alongX, float fixedCoord, float min, float max, float height,
                         Material material, float openingWidth, float openingHeight, float[] openingCenters)
        {
            float[] centers = openingCenters != null ? (float[])openingCenters.Clone() : new float[0];
            System.Array.Sort(centers);
            float cursor = min;
            int piece = 0;

            void Segment(string label, float from, float to, float bottom, float top)
            {
                if (to - from < 0.01f || top - bottom < 0.01f) return;
                float along = (from + to) * 0.5f, length = to - from, y = (bottom + top) * 0.5f, tall = top - bottom;
                Vector3 position = alongX ? new Vector3(along, y, fixedCoord) : new Vector3(fixedCoord, y, along);
                Vector3 scale = alongX ? new Vector3(length, tall, 0.2f) : new Vector3(0.2f, tall, length);
                Block(label, position, scale, material, parent);
            }

            foreach (float c in centers)
            {
                float left = c - openingWidth * 0.5f, right = c + openingWidth * 0.5f;
                Segment($"{name} ({++piece})", cursor, left, 0f, height);
                Segment($"{name} (above opening {piece})", left, right, openingHeight, height);
                cursor = right;
            }
            Segment($"{name} ({++piece})", cursor, max, 0f, height);
        }

        static void RenameIfFound(string oldName, string newName)
        {
            GameObject go = GameObject.Find(oldName);
            if (go != null) go.name = newName;
        }

        // ---------- assets + build settings after the scenes ----------

        /// SectionData.sceneName + exitCondition (D-165 defaults) and the RunConfig scene names (D-161, D-163).
        static bool ConfigureRunAssetsForScenes()
        {
            if (!PrepareRunAssets(out RunConfig config)) return false;
            bool ok = true;
            foreach (var (asset, scene) in SectionScenes)
            {
                var section = AssetDatabase.LoadAssetAtPath<SectionData>(RunSectionsFolder + "/" + asset + ".asset");
                if (section == null)
                {
                    Debug.LogError($"[TWR] Section asset {RunSectionsFolder}/{asset}.asset is missing. Run TWR > Run > Create Run Assets.");
                    ok = false;
                    continue;
                }
                SectionExitCondition condition = SectionData.DefaultExitCondition(section.type);
                int enemies = SectionData.DefaultEnemyCount(section.type); // D-176: fixed per template
                if (section.sceneName != scene || section.exitCondition != condition || section.enemyCount != enemies)
                {
                    section.sceneName = scene;
                    section.exitCondition = condition;
                    section.enemyCount = enemies;
                    EditorUtility.SetDirty(section);
                }
                EnforceOfficeAmbienceRule(section); // A1: no office one-shots in silent rooms / boss arenas (D-223)
                Debug.Log($"[TWR] {section.name}: scene '{scene}', exit condition {condition} (D-165), enemy count {enemies} (D-176).", section);
            }

            if (config.lobbySceneName != LobbySceneName || config.coreSceneName != CoreSceneName)
            {
                config.lobbySceneName = LobbySceneName;
                config.coreSceneName = CoreSceneName;
                EditorUtility.SetDirty(config);
            }
            Debug.Log($"[TWR] RunConfig: core scene '{config.coreSceneName}', lobby scene '{config.lobbySceneName}' (D-161, D-163).", config);
            AssetDatabase.SaveAssets();
            return ok;
        }

        /// MainMenu first (index 0, D-210), then Core, every content scene and the ending scenes; scenes that were already
        /// listed (sandbox, ...) stay after them.
        static void ConfigureRunBuildSettings()
        {
            var ordered = new List<EditorBuildSettingsScene>();
            if (File.Exists(RunScenePath(MainMenuSceneName))) ordered.Add(new EditorBuildSettingsScene(RunScenePath(MainMenuSceneName), true));
            else Debug.LogWarning("[TWR] MainMenu scene not built yet; Core is index 0 for now. Run TWR > Run > Build Endings + Main Menu.");
            ordered.Add(new EditorBuildSettingsScene(RunScenePath(CoreSceneName), true));
            foreach (string sceneName in RunContentScenes)
                ordered.Add(new EditorBuildSettingsScene(RunScenePath(sceneName), true));
            foreach (EndingSpec spec in EndingSpecs)
                if (File.Exists(RunScenePath(spec.scene))) ordered.Add(new EditorBuildSettingsScene(RunScenePath(spec.scene), true));
            var runPaths = new HashSet<string>(ordered.Select(s => s.path));
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
                if (!runPaths.Contains(existing.path)) ordered.Add(existing);
            EditorBuildSettings.scenes = ordered.ToArray();
            Debug.Log("[TWR] Build Settings: " + string.Join(", ", ordered.Select((s, i) => $"{i}: {System.IO.Path.GetFileNameWithoutExtension(s.path)}{(s.enabled ? "" : " (off)")}")));
        }
    }
}
