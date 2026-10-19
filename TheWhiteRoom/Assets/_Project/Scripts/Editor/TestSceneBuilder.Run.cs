using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TWR.Run;
using TWR.Stats;

namespace TWR.EditorTools
{
    /// <summary>
    /// Run Structure step R1 (D-156 to D-160) part of the test-scene tools.
    /// - Assets (created only if missing, empty fields repaired) under Assets/_Project/Data/Run/: one SectionData
    ///   per D-155 template (puzzle, combat, stealth, impossible geometry, silent: path None; fork room; boss arena
    ///   Escape = bull, Discover = preacher, D-148), the curated first-run plan (D-160) and the RunConfig.
    /// - Scene: a "Run Manager (bootstrap)" object with the RunConfig. It survives scene loads and owns the
    ///   run-scoped RunStats (D-158), so the old standalone "Run Stats" object is removed.
    /// - Menus: "TWR/Run/Create Run Assets" (assets + bootstrap into Test_PlayerController) and
    ///   "TWR/Run/Validate Run Setup" (config, curated plan, bootstrap, lobby scene in Build Settings).
    /// "TWR/Build Test Scene" runs the same steps. Every step logs with "[TWR]".
    /// R2: the real run scenes are built by "TWR/Run/Build Run Scenes" (TestSceneBuilder.RunScenes.cs); the bootstrap
    /// in the sandbox test scene has no SectionLoader, so it keeps the R1 behaviour there.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string RunDataFolder = Root + "/Data/Run";
        const string RunSectionsFolder = RunDataFolder + "/Sections";
        const string RunConfigPath = RunDataFolder + "/RunConfig.asset";
        const string CuratedPlanPath = RunDataFolder + "/CuratedRunPlan_FirstRun.asset";
        const string RunBootstrapName = "Run Manager (bootstrap)";

        /// The section assets R1 creates (D-155). Paths are fixed so re-running never makes duplicates.
        struct RunAssets
        {
            public SectionData puzzle, combat, stealth, impossibleGeometry, silent;
            public SectionData fork, bossEscape, bossDiscover;
            public CuratedRunPlan curated;
            public RunConfig config;
        }

        // ---------- menus ----------

        [MenuItem("TWR/Run/Create Run Assets")]
        public static void CreateRunAssetsMenu()
        {
            Debug.Log("[TWR] Run setup (R1): creating run assets and the RunManager bootstrap...");

            // Open the test scene FIRST, assets SECOND (same reason as BuildTestSceneAfterSetup: opening a
            // scene can unload assets the old scene didn't use, leaving dead references).
            bool sceneReady = OpenTestSceneForRun();

            if (!PrepareRunAssets(out RunConfig config))
                return;
            EnsureLobbyInBuildSettings(config);

            if (sceneReady)
            {
                Scene scene = SceneManager.GetActiveScene();
                EnsureRunBootstrap(config);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[TWR] Saved " + scene.path);
            }

            if (ValidateRunSetup())
                Debug.Log("[TWR] Run setup complete. Press Play, then F1 for the run debug overlay.");
            else
                Debug.LogError("[TWR] Run setup finished with problems (see errors above).");
        }

        [MenuItem("TWR/Run/Validate Run Setup")]
        public static void ValidateRunSetupMenu()
        {
            bool ok = ValidateRunSetup();
            // R2 (D-161): the run scenes, if they have been built.
            if (File.Exists(RunScenePath(CoreSceneName)))
            {
                ok &= ValidateRunScenes();
                ok &= ValidateEndings(); // R5 (D-195 to D-212)
            }
            else Debug.LogWarning("[TWR] The run scenes are not built yet. Run TWR > Run > Build Run Scenes (R2).");
            ok &= ValidateAudio(); // A1: mixer structure, routing, max distance, reverb, license log (D-219 to D-227)
            ok &= ValidateLocalization(); // keys without English text, Addressables play mode (D-084)

            if (ok)
                Debug.Log("[TWR] Run setup is valid.");
            else
                Debug.LogError("[TWR] Run setup has problems (see errors above).");
        }

        /// Makes the test scene the open scene. False if it doesn't exist yet or the user cancelled.
        static bool OpenTestSceneForRun()
        {
            if (SceneManager.GetActiveScene().path == ScenePath) return true;

            if (!File.Exists(ScenePath))
            {
                Debug.LogWarning("[TWR] " + ScenePath + " doesn't exist yet. Creating the run assets only; " +
                                 "run TWR > Build Test Scene to get a scene with the RunManager.");
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[TWR] Cancelled: the test scene was not opened, so no RunManager was added. Assets are still created.");
                return false;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[TWR] Opened " + ScenePath);
            return true;
        }

        // ---------- assets ----------

        /// Creates (only if missing) every run asset and repairs empty references. Call AFTER opening/creating the scene.
        static bool PrepareRunAssets(out RunConfig config)
        {
            config = null;
            EnsureFolder(RunSectionsFolder);

            var a = new RunAssets
            {
                puzzle = Section("Section_Puzzle", "section.puzzle", SectionType.Puzzle, RunPath.None, "puzzle"),
                combat = Section("Section_Combat", "section.combat", SectionType.Combat, RunPath.None, "combat"),
                stealth = Section("Section_Stealth", "section.stealth", SectionType.Stealth, RunPath.None, "stealth"),
                impossibleGeometry = Section("Section_ImpossibleGeometry", "section.impossible", SectionType.ImpossibleGeometry, RunPath.None, "portal"),
                silent = Section("Section_Silent", "section.silent", SectionType.Silent, RunPath.None, "silent"),
                fork = Section("Section_ForkRoom", "connector.fork", SectionType.Fork, RunPath.None, "connector"),
                bossEscape = Section("Section_BossArena_Escape_Bull", "boss.escape.bull", SectionType.BossArena, RunPath.Escape, "boss", "bull"),
                bossDiscover = Section("Section_BossArena_Discover_Preacher", "boss.discover.preacher", SectionType.BossArena, RunPath.Discover, "boss", "preacher"),
            };
            if (a.puzzle == null || a.combat == null || a.stealth == null || a.impossibleGeometry == null || a.silent == null ||
                a.fork == null || a.bossEscape == null || a.bossDiscover == null)
            {
                Debug.LogError("[TWR] Could not create the section assets in " + RunSectionsFolder);
                return false;
            }

            // Curated first run, D-160: puzzle > combat > impossible geometry > fork > Escape: stealth / Discover: silent > boss (D-148).
            a.curated = LoadOrCreate<CuratedRunPlan>(CuratedPlanPath, null);
            if (a.curated == null)
            {
                Debug.LogError("[TWR] Could not create " + CuratedPlanPath);
                return false;
            }
            bool curatedChanged = false;
            if (a.curated.preForkSections == null || a.curated.preForkSections.Length == 0)
            {
                a.curated.preForkSections = new[] { a.puzzle, a.combat, a.impossibleGeometry };
                curatedChanged = true;
            }
            curatedChanged |= FillIfNull(ref a.curated.forkRoom, a.fork, "curated fork room");
            curatedChanged |= FillIfNull(ref a.curated.escapePathSection, a.stealth, "curated Escape path section");
            curatedChanged |= FillIfNull(ref a.curated.escapeBossArena, a.bossEscape, "curated Escape boss arena");
            curatedChanged |= FillIfNull(ref a.curated.discoverPathSection, a.silent, "curated Discover path section");
            curatedChanged |= FillIfNull(ref a.curated.discoverBossArena, a.bossDiscover, "curated Discover boss arena");
            if (curatedChanged)
            {
                EditorUtility.SetDirty(a.curated);
                Debug.Log("[TWR] Filled the curated first-run plan (D-160): puzzle > combat > impossible geometry > fork > " +
                          "Escape: stealth + bull arena / Discover: silent + preacher arena.");
            }

            // RunConfig: pool of the five general templates, the fork room, both boss arenas, the curated plan.
            a.config = LoadOrCreate<RunConfig>(RunConfigPath, null);
            if (a.config == null)
            {
                Debug.LogError("[TWR] Could not create " + RunConfigPath);
                return false;
            }
            bool configChanged = false;
            if (a.config.sectionPool == null || a.config.sectionPool.Length == 0)
            {
                a.config.sectionPool = new[] { a.puzzle, a.combat, a.stealth, a.impossibleGeometry, a.silent };
                configChanged = true;
                Debug.Log("[TWR] RunConfig: section pool = puzzle, combat, stealth, impossible geometry, silent (D-155).");
            }
            if (a.config.bossArenas == null || a.config.bossArenas.Length == 0)
            {
                a.config.bossArenas = new[] { a.bossEscape, a.bossDiscover };
                configChanged = true;
                Debug.Log("[TWR] RunConfig: boss arenas = Escape (bull), Discover (preacher) (D-148).");
            }
            configChanged |= FillIfNull(ref a.config.forkRoom, a.fork, "RunConfig fork room");
            configChanged |= FillIfNull(ref a.config.curatedPlan, a.curated, "RunConfig curated plan");
            if (string.IsNullOrEmpty(a.config.lobbySceneName))
            {
                a.config.lobbySceneName = Path.GetFileNameWithoutExtension(ScenePath);
                configChanged = true;
                Debug.Log("[TWR] RunConfig: lobby scene = " + a.config.lobbySceneName);
            }
            if (configChanged) EditorUtility.SetDirty(a.config);

            AssetDatabase.SaveAssets();
            config = a.config;
            Debug.Log("[TWR] Run assets ready in " + RunDataFolder);
            return true;
        }

        static SectionData Section(string fileName, string id, SectionType type, RunPath path, params string[] tags)
        {
            return LoadOrCreate<SectionData>(RunSectionsFolder + "/" + fileName + ".asset", s =>
            {
                s.id = id;
                s.type = type;
                s.path = path;
                s.tags = tags;
            });
        }

        static bool FillIfNull<T>(ref T field, T value, string label) where T : Object
        {
            if (field != null) return false;
            field = value;
            Debug.Log("[TWR] Set " + label + " = " + value.name);
            return true;
        }

        // ---------- scene ----------

        /// Adds the RunManager bootstrap to the open scene if missing (or fills its empty config) and removes the
        /// old standalone RunStats object from step 4: the RunManager now owns RunStats (D-158).
        static RunManager EnsureRunBootstrap(RunConfig config)
        {
            RemoveStandaloneRunStats();

            RunManager[] existing = Object.FindObjectsByType<RunManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing.Length > 0)
            {
                RunManager manager = existing[0];
                if (manager.Config == null)
                {
                    Assign(manager, "config", config);
                    Debug.Log("[TWR] Assigned the RunConfig to the existing " + manager.name);
                }
                else Debug.Log("[TWR] Scene already has a RunManager: " + manager.name);
                return manager;
            }
            return BuildRunBootstrap(config);
        }

        /// New root object with the RunManager. RunStats and the debug overlay are added at runtime by the RunManager.
        static RunManager BuildRunBootstrap(RunConfig config)
        {
            var go = new GameObject(RunBootstrapName);
            var manager = go.AddComponent<RunManager>();
            Assign(manager, "config", config);
            Debug.Log("[TWR] Added '" + RunBootstrapName + "' with " + RunConfigPath + " to the scene.");
            return manager;
        }

        static void RemoveStandaloneRunStats()
        {
            foreach (RunStats stats in Object.FindObjectsByType<RunStats>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (stats.GetComponent<RunManager>() != null) continue;
                GameObject go = stats.gameObject;
                Debug.Log("[TWR] Removed the standalone RunStats on '" + go.name + "' (the RunManager owns RunStats now, D-158).");
                // The step-4 object only held RunStats: remove it entirely; otherwise just the component.
                if (go.GetComponents<Component>().Length <= 2 && go.transform.childCount == 0) Object.DestroyImmediate(go);
                else Object.DestroyImmediate(stats);
            }
        }

        // ---------- build settings ----------

        /// Adds (or re-enables) the RunConfig's lobby scene in Build Settings so the RunManager can load it (D-157).
        static bool EnsureLobbyInBuildSettings(RunConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.lobbySceneName)) return false;

            string lobbyPath = FindScenePath(config.lobbySceneName);
            if (lobbyPath == null && config.lobbySceneName == LobbySceneName)
            {
                // R2 run lobby not built yet: the sandbox doesn't need it (it reloads itself, R1 behaviour).
                Debug.LogWarning("[TWR] The run's Lobby scene is not built yet. Run TWR > Run > Build Run Scenes (R2).");
                return true;
            }
            if (lobbyPath == null)
            {
                Debug.LogError($"[TWR] Lobby scene '{config.lobbySceneName}' (RunConfig) was not found in the project. " +
                               "Run TWR > Build Test Scene, or fix the name in " + RunConfigPath);
                return false;
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            int index = scenes.FindIndex(s => s.path == lobbyPath);
            if (index >= 0 && scenes[index].enabled) return true;

            if (index >= 0)
            {
                scenes[index] = new EditorBuildSettingsScene(lobbyPath, true);
                Debug.Log("[TWR] Enabled the lobby scene " + lobbyPath + " in Build Settings.");
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(lobbyPath, true));
                Debug.Log("[TWR] Added the lobby scene " + lobbyPath + " to Build Settings.");
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            return true;
        }

        static string FindScenePath(string sceneName)
        {
            foreach (string guid in AssetDatabase.FindAssets(sceneName + " t:Scene"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == sceneName) return path;
            }
            return null;
        }

        // ---------- validation ----------

        /// Logs [TWR] errors/warnings for everything the run needs. Also adds the lobby scene to Build Settings if missing.
        static bool ValidateRunSetup()
        {
            bool ok = true;

            var config = AssetDatabase.LoadAssetAtPath<RunConfig>(RunConfigPath);
            if (config == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:" + nameof(RunConfig));
                if (guids.Length > 0) config = AssetDatabase.LoadAssetAtPath<RunConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (config == null)
            {
                Debug.LogError("[TWR] No RunConfig found (expected " + RunConfigPath + "). Run TWR > Run > Create Run Assets.");
                return false;
            }

            // Config, pool, fork room, boss arenas, curated plan (shared check with the RunManager).
            var errors = new List<string>();
            var warnings = new List<string>();
            config.Validate(errors, warnings);
            foreach (string e in errors) Debug.LogError("[TWR] RunConfig: " + e, config);
            foreach (string w in warnings) Debug.LogWarning("[TWR] RunConfig: " + w, config);
            ok &= errors.Count == 0;

            // Every section asset in the project: boss arenas must have a path (D-156).
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SectionData)))
            {
                var section = AssetDatabase.LoadAssetAtPath<SectionData>(AssetDatabase.GUIDToAssetPath(guid));
                if (section != null && section.type == SectionType.BossArena && section.path == RunPath.None)
                {
                    Debug.LogError($"[TWR] Section '{section.name}' is a boss arena without a path (D-148, D-156).", section);
                    ok = false;
                }
            }

            // The two plans the game can produce right now: curated (seed 0) and one random seed.
            RunPlan curated = RunPlanGenerator.FromCurated(config.curatedPlan);
            if (curated != null) Debug.Log("[TWR] Curated plan (seed 00000000): " + curated.Describe());
            RunPlan sample = RunPlanGenerator.Create(0x1234ABCD, config);
            if (sample != null) Debug.Log("[TWR] Sample plan (seed 1234ABCD): " + sample.Describe());

            // Lobby scene in Build Settings (added if missing).
            ok &= EnsureLobbyInBuildSettings(config);

            // Bootstrap in the open scene.
            Scene scene = SceneManager.GetActiveScene();
            RunManager[] managers = Object.FindObjectsByType<RunManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (managers.Length == 0)
            {
                Debug.LogWarning($"[TWR] The open scene '{scene.name}' has no RunManager. Run TWR > Run > Create Run Assets " +
                                 "(adds it to " + ScenePath + ").");
            }
            else
            {
                if (managers.Length > 1)
                    Debug.LogWarning($"[TWR] The open scene has {managers.Length} RunManagers; only the first one survives in Play mode.");
                foreach (RunManager m in managers)
                {
                    if (m.Config == null)
                    {
                        Debug.LogError($"[TWR] RunManager on '{m.name}' has no RunConfig.", m);
                        ok = false;
                    }
                    else Debug.Log($"[TWR] RunManager on '{m.name}' uses {AssetDatabase.GetAssetPath(m.Config)} (lobby '{m.Config.lobbySceneName}').", m);
                }
            }
            foreach (RunStats stats in Object.FindObjectsByType<RunStats>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (stats.GetComponent<RunManager>() == null)
                    Debug.LogWarning($"[TWR] Standalone RunStats on '{stats.name}' would count the run twice. Run TWR > Run > Create Run Assets to remove it.", stats);

            return ok;
        }
    }
}
