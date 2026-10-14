using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TWR.Core;
using TWR.Stats;

namespace TWR.Run
{
    /// <summary>
    /// Owns the current run (D-015, D-057, D-151, D-156 to D-159). At run start it picks the seed: a debug override if
    /// one is queued, seed 0 = the curated first run while no endings are saved (D-154, D-159), otherwise a random
    /// non-zero seed. It builds the RunPlan, steps through it (lobby, pre-fork sections, fork room, path section, boss
    /// arena), and fixes the path when ChoosePath is called at the fork (D-060, D-149). It also owns the run-scoped
    /// RunStats (D-158).
    /// Two modes:
    /// - Core mode (R2, D-161): a SectionLoader sits on the same object in the persistent Core scene. Every stop loads
    ///   its content scene additively (SectionData.sceneName) with a fade (D-167); section exits call HandleSectionExit;
    ///   the fork door you walk through commits the path (D-166); the boss arena exit shows the placeholder end screen
    ///   (D-092). Death, quit and run complete reset the persistent player to run-start defaults through IRunResettable
    ///   (D-162) and load the Lobby content scene with a new plan (D-157). Raises SectionStarted/SectionEnded for music
    ///   (D-075) and the matching VoiceEvents for the inner voice (D-080).
    /// - Sandbox mode (the Test_PlayerController scene, no SectionLoader): R1 behaviour. Sections are only tracked,
    ///   the object survives scene loads (DontDestroyOnLoad) and the run end reloads that scene.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public class RunManager : MonoBehaviour, IRunHandler
    {
        /// <summary>Editor only: which content scene was open when Play was pressed (set by RunScenePlayModeStart).</summary>
        public const string EditorStartSceneKey = "TWR.Run.EditorStartScene";
        const string LobbyId = "lobby";
        static readonly RunPath[] Branches = { RunPath.Escape, RunPath.Discover };

        [SerializeField] RunConfig config;
        [Tooltip("Write [Run] lines to the Console.")]
        [SerializeField] bool logRun = true;

        /// <summary>The surviving RunManager, or null if none exists.</summary>
        public static RunManager Instance { get; private set; }

        public RunConfig Config => config;
        /// <summary>The current plan. Null for the moment between EndRun and the lobby finishing its load.</summary>
        public RunPlan Plan { get; private set; }
        public uint Seed => Plan != null ? Plan.Seed : 0u;
        public bool IsCurated => Plan != null && Plan.IsCurated;
        /// <summary>-1 = hub lobby (RunPlan.LobbyIndex), then pre-fork sections, fork room, path section, boss arena.</summary>
        public int CurrentStopIndex { get; private set; } = RunPlan.LobbyIndex;
        /// <summary>None until the player picks a fork door (D-060, D-156).</summary>
        public RunPath ActivePath { get; private set; } = RunPath.None;
        public SectionData CurrentSection => Plan != null ? Plan.GetStop(CurrentStopIndex, ActivePath) : null;
        public bool IsAtFork => Plan != null && CurrentStopIndex == Plan.ForkIndex;
        public bool IsRunActive => Plan != null && !isEnding;
        /// <summary>How many runs started since Play began (debug only).</summary>
        public int RunNumber { get; private set; }
        public RunStats Stats { get; private set; }

        /// <summary>The scene swapper (Core mode only, D-161). Null in the sandbox test scene.</summary>
        public SectionLoader Loader => loader;
        /// <summary>True when sections are real scenes loaded next to Core (D-161).</summary>
        public bool IsCoreMode => loader != null;
        /// <summary>A section transition or the end screen is running.</summary>
        public bool IsTransitioning => loader != null && (loader.IsBusy || completing);

        /// <summary>Who tells us whether endings are saved (D-159). A stub until D-099 save data exists.</summary>
        public IEndingsRecord EndingsRecord { get; set; } = new StubEndingsRecord();

        /// <summary>A new run began (plan, seed and stats are ready).</summary>
        public event Action<RunManager> RunStarted;
        /// <summary>(stop index, section). Section is null for the hub lobby.</summary>
        public event Action<int, SectionData> SectionChanged;
        /// <summary>(stop index, section). Core mode: the section is loaded and visible-to-be (music D-075, voice D-080).</summary>
        public event Action<int, SectionData> SectionStarted;
        /// <summary>(stop index, section). Core mode: the player left the section (exit, death, quit, run complete).</summary>
        public event Action<int, SectionData> SectionEnded;
        /// <summary>The player committed to a path at the fork.</summary>
        public event Action<RunPath> PathChosen;
        /// <summary>The run is over; the lobby loads next and a new run starts.</summary>
        public event Action<RunEndReason> RunEnded;

        SectionLoader loader;
        string homeSceneName;
        Action onSectionLoaded;
        Action onRunCompleteFinished;
        bool isEnding;
        bool completing;
        bool pendingNewRun = true;
        bool sceneLoadSeen;
        bool hasSeedOverride;
        uint seedOverride;

        // ---------- lifetime ----------

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Sandbox: the test scene reloaded and brought its own bootstrap copy; the original keeps running.
                enabled = false;
                Destroy(gameObject);
                return;
            }

            Instance = this;
            homeSceneName = gameObject.scene.name;
            loader = GetComponent<SectionLoader>();
            onSectionLoaded = HandleSectionLoaded;
            onRunCompleteFinished = FinishRunComplete;

            if (!IsCoreMode)
            {
                transform.SetParent(null); // DontDestroyOnLoad only works on root objects
                DontDestroyOnLoad(gameObject);
            }
            // Core mode: the Core scene is never unloaded, so this object simply stays in it (D-161).

            Stats = GetComponent<RunStats>();
            if (Stats == null) Stats = gameObject.AddComponent<RunStats>();
            if (IsCoreMode && config != null) Stats.IgnoredSceneName = config.coreSceneName;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GetComponent<RunDebugOverlay>() == null) gameObject.AddComponent<RunDebugOverlay>();
#endif

            RunHooks.Register(this);
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ValidateConfig();
        }

        void Start()
        {
            if (Instance != this) return;
            // Normally sceneLoaded already started the first run. If it didn't fire for the first scene, do it here.
            if (!sceneLoadSeen) HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            if (IsCoreMode) LoadFirstContent();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            RunHooks.Unregister(this);
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            sceneLoadSeen = true;
            if (pendingNewRun)
            {
                pendingNewRun = false;
                isEnding = false;
                StartNewRun();
            }
            // Puzzles in every loaded section count as available this run (D-158). RunStats skips the Core scene.
            Stats.AddLoadedScene(scene);
        }

        // ---------- run start ----------

        /// <summary>
        /// Build a new plan and reset stats (and, in Core mode, the persistent player, D-162), without loading any
        /// scene. Seed: queued debug override, else 0 (curated) while no endings are saved, else random non-zero.
        /// </summary>
        public void StartNewRun()
        {
            completing = false;
            if (config == null)
            {
                Debug.LogError("[Run] RunManager has no RunConfig. Run TWR > Run > Create Run Assets.", this);
                Plan = null;
                return;
            }

            uint seed;
            string source;
            if (hasSeedOverride)
            {
                seed = seedOverride;
                hasSeedOverride = false;
                source = "debug seed";
            }
            else if (EndingsRecord == null || EndingsRecord.SeenEndingCount == 0)
            {
                seed = RunSeed.Curated;
                source = "no endings saved -> curated first run";
            }
            else
            {
                seed = RunSeed.NewRandom();
                source = "random";
            }

            RunPlan plan = RunPlanGenerator.Create(seed, config);
            if (plan == null)
            {
                string problem = config.curatedPlan == null ? "not assigned" : (config.curatedPlan.IsValid(out string p) ? "?" : p);
                Debug.LogError($"[Run] Curated run plan unusable ({problem}). Using a random seed instead. Run TWR > Run > Validate Run Setup.", this);
                seed = RunSeed.NewRandom();
                source = "random (curated plan unusable)";
                plan = RunPlanGenerator.Create(seed, config);
            }

            Plan = plan;
            CurrentStopIndex = RunPlan.LobbyIndex;
            ActivePath = RunPath.None;
            RunNumber++;
            Stats.ResetForNewRun();
            if (IsCoreMode) ResetPlayer();

            Log($"Run #{RunNumber} started | seed {RunSeed.Format(plan.Seed)} ({source}) | curated: {(plan.IsCurated ? "yes" : "no")} | " +
                $"{plan.SectionCount} sections | plan: {plan.Describe()}");
            WarnAboutMissingPieces(plan);

            RunStarted?.Invoke(this);
            SectionChanged?.Invoke(CurrentStopIndex, null);
        }

        /// <summary>The one reset entry point for the persistent player (D-162): HP, ammo, guns, inventory, abilities.</summary>
        void ResetPlayer()
        {
            GameObject player = loader.Player != null ? loader.Player.gameObject : null;
            if (player == null)
            {
                Debug.LogError("[Run] The SectionLoader has no player, so it can't be reset (D-162).", this);
                return;
            }
            int parts = RunResets.ResetAll(player);
            Log($"Player reset to run-start defaults ({parts} parts: HP, magazines + reserve, guns, inventory, abilities; D-162, D-120).");
        }

        /// <summary>Core mode, first frame: load the lobby (or, in the editor, the section scene you pressed Play in).</summary>
        void LoadFirstContent()
        {
            string first = config != null ? config.lobbySceneName : null;
#if UNITY_EDITOR
            string requested = UnityEditor.SessionState.GetString(EditorStartSceneKey, string.Empty);
            UnityEditor.SessionState.EraseString(EditorStartSceneKey);
            if (!string.IsNullOrEmpty(requested) && config != null &&
                requested != config.lobbySceneName && requested != config.coreSceneName)
            {
                if (JumpToSectionScene(requested)) first = requested;
                else Debug.LogWarning($"[Run] Editor: scene '{requested}' is not part of this run's plan; starting in the lobby instead.", this);
            }
#endif
            if (!CanLoad(first))
            {
                Debug.LogError($"[Run] Lobby scene '{first}' is not in Build Settings. Run TWR > Run > Build Run Scenes.", this);
                return;
            }
            loader.Load(first, onSectionLoaded);
        }

        /// <summary>Editor convenience: start the run directly at the stop that uses this scene.</summary>
        bool JumpToSectionScene(string sceneName)
        {
            if (Plan == null) return false;
            for (int i = 0; i < Plan.PreForkSections.Count; i++)
                if (UsesScene(Plan.PreForkSections[i], sceneName)) return JumpTo(i, RunPath.None);
            if (UsesScene(Plan.ForkRoom, sceneName)) return JumpTo(Plan.ForkIndex, RunPath.None);
            foreach (RunPath branch in Branches)
            {
                if (UsesScene(Plan.GetPathSection(branch), sceneName)) return JumpTo(Plan.PathSectionIndex, branch);
                if (UsesScene(Plan.GetBossArena(branch), sceneName)) return JumpTo(Plan.BossIndex, branch);
            }
            return false;
        }

        static bool UsesScene(SectionData section, string sceneName) => section != null && section.sceneName == sceneName;

        bool JumpTo(int stopIndex, RunPath path)
        {
            CurrentStopIndex = stopIndex;
            ActivePath = path;
            Log($"Editor start: jumped straight to {RunPlan.Name(CurrentSection)}{(path != RunPath.None ? " on path " + path : "")}.");
            SectionChanged?.Invoke(CurrentStopIndex, CurrentSection);
            return true;
        }

        // ---------- moving through the run ----------

        /// <summary>
        /// Go to the next stop of the plan. At the fork a path must be chosen first. Past the boss arena the run is
        /// complete. Core mode: loads the stop's scene through the SectionLoader (D-167); called by section exits and
        /// by the debug overlay (F2). Sandbox mode: only tracks the stop (R1).
        /// </summary>
        public bool GoToNextSection()
        {
            if (!IsRunActive) return false;
            if (IsTransitioning)
            {
                Log("A section transition is already running; ignored.");
                return false;
            }
            if (CurrentStopIndex == Plan.ForkIndex && ActivePath == RunPath.None)
            {
                Debug.LogWarning("[Run] At the fork room: choose Escape or Discover first (D-060).", this);
                return false;
            }
            if (CurrentStopIndex >= Plan.BossIndex)
            {
                if (IsCoreMode)
                {
                    BeginRunComplete();
                    return true;
                }
                Log("Boss arena finished -> run complete (end screen D-092 comes later).");
                EndRun(RunEndReason.Completed);
                return true;
            }

            int nextIndex = CurrentStopIndex + 1;
            SectionData section = Plan.GetStop(nextIndex, ActivePath);
            if (IsCoreMode && (section == null || !CanLoad(section.sceneName)))
            {
                Debug.LogError($"[Run] Can't load the next stop '{RunPlan.Name(section)}': its scene " +
                               $"'{(section != null ? section.sceneName : "")}' is empty or not in Build Settings. Run TWR > Run > Build Run Scenes.", this);
                return false;
            }

            if (IsCoreMode) RaiseSectionEnded();
            CurrentStopIndex = nextIndex;
            if (CurrentStopIndex == Plan.ForkIndex)
                Log($"Fork room: {RunPlan.Name(section)} (connector) - choose Escape or Discover");
            else
                Log($"Section {Plan.SectionNumber(CurrentStopIndex)}/{Plan.SectionCount}: {RunPlan.Name(section)}" +
                    $"{(section != null ? " (" + section.type + ")" : "")}{(ActivePath != RunPath.None ? " on path " + ActivePath : "")}");
            SectionChanged?.Invoke(CurrentStopIndex, section);
            if (IsCoreMode) loader.Load(section.sceneName, onSectionLoaded);
            return true;
        }

        /// <summary>
        /// A SectionExit was entered (D-165). In the fork room the exit's path is committed first (D-166: walking
        /// through a door commits, opening it does not). Returns false if nothing happened (e.g. mid-transition).
        /// </summary>
        public bool HandleSectionExit(SectionExit exit)
        {
            if (exit == null || !IsRunActive || IsTransitioning) return false;
            if (IsCoreMode && exit.gameObject.scene.name != loader.CurrentContentScene) return false; // leftover from an old scene

            if (IsAtFork)
            {
                if (exit.Path == RunPath.None)
                {
                    Debug.LogWarning($"[Run] Fork exit '{exit.ExitId}' has no path set (D-166).", exit);
                    return false;
                }
                if (ActivePath == RunPath.None)
                {
                    if (!ChoosePath(exit.Path)) return false;
                }
                else if (ActivePath != exit.Path)
                {
                    Debug.LogWarning($"[Run] Path {ActivePath} is already chosen (debug overlay); the {exit.Path} door doesn't lead anywhere now.", exit);
                    return false;
                }
            }
            Log($"Exit '{exit.ExitId}' reached.");
            return GoToNextSection();
        }

        /// <summary>The player picked a fork door (D-060, D-149). Only works once, in the fork room.</summary>
        public bool ChoosePath(RunPath path)
        {
            if (!IsRunActive || path == RunPath.None) return false;
            if (ActivePath != RunPath.None)
            {
                Debug.LogWarning($"[Run] Path already chosen ({ActivePath}).", this);
                return false;
            }
            if (CurrentStopIndex != Plan.ForkIndex)
            {
                Debug.LogWarning("[Run] A path can only be chosen in the fork room.", this);
                return false;
            }

            ActivePath = path;
            Log($"Path chosen: {path} -> {RunPlan.Name(Plan.GetPathSection(path))} then {RunPlan.Name(Plan.GetBossArena(path))}");
            PathChosen?.Invoke(path);
            return true;
        }

        void HandleSectionLoaded()
        {
            SectionData section = CurrentSection;
            SectionStarted?.Invoke(CurrentStopIndex, section);
            VoiceEvents.RaiseSectionStarted(SectionId(section));
        }

        void RaiseSectionEnded()
        {
            SectionData section = CurrentSection;
            SectionEnded?.Invoke(CurrentStopIndex, section);
            VoiceEvents.RaiseSectionEnded(SectionId(section));
        }

        static string SectionId(SectionData section) => section != null ? section.DisplayId : LobbyId;

        // ---------- run end ----------

        /// <summary>Core mode: the boss arena exit shows the placeholder end screen (D-092), then ends the run.</summary>
        void BeginRunComplete()
        {
            completing = true;
            Log("Boss arena finished -> run complete (placeholder end screen, D-092).");
            loader.PlayRunComplete(config.runCompleteMessage, config.runCompleteHoldSeconds, onRunCompleteFinished);
        }

        void FinishRunComplete()
        {
            completing = false;
            EndRun(RunEndReason.Completed);
        }

        /// <summary>Quit mid-run (D-093): resets the run like death. The future pause menu calls this.</summary>
        public void QuitRun() => EndRun(RunEndReason.Quit);

        /// <summary>
        /// Debug only (D-153): queue a seed and restart. If a run is going, it ends (reason Debug) and the
        /// lobby loads; the queued seed is used once, by the next run.
        /// </summary>
        public void StartRunWithSeed(uint seed)
        {
            hasSeedOverride = true;
            seedOverride = seed;
            Log($"Debug seed {RunSeed.Format(seed)} queued for the next run.");
            if (IsRunActive) EndRun(RunEndReason.Debug);
            else if (!isEnding) StartNewRun();
        }

        /// <summary>
        /// End the run (D-157): log the result, discard the plan, go back to the hub lobby. The new run starts as soon
        /// as the lobby has loaded (new seed, or the curated plan while no endings are saved); in Core mode that also
        /// resets the persistent player (D-162) while the screen is white.
        /// </summary>
        public void EndRun(RunEndReason reason)
        {
            if (!IsRunActive) return;
            isEnding = true;
            completing = false;

            string where = CurrentStopIndex == RunPlan.LobbyIndex ? "hub lobby" : RunPlan.Name(CurrentSection);
            Log($"Run #{RunNumber} ended: {reason} | seed {RunSeed.Format(Plan.Seed)} | reached {where} | path {ActivePath} | {Stats.Summary()}");
            if (IsCoreMode) RaiseSectionEnded();
            RunEnded?.Invoke(reason);

            Plan = null;
            pendingNewRun = true;
            LoadLobby();
        }

        void LoadLobby()
        {
            if (IsCoreMode)
            {
                string lobby = config != null ? config.lobbySceneName : null;
                if (CanLoad(lobby))
                {
                    loader.Load(lobby, onSectionLoaded); // sceneLoaded starts the new run (and resets the player)
                    return;
                }
                Debug.LogError($"[Run] Lobby scene '{lobby}' is not in Build Settings. Starting the new run in place. " +
                               "Run TWR > Run > Build Run Scenes.", this);
                pendingNewRun = false;
                isEnding = false;
                StartNewRun();
                return;
            }

            // Sandbox (R1): the test scene is its own lobby, so reload the scene this RunManager came from.
            if (CanLoad(homeSceneName))
            {
                SceneManager.LoadScene(homeSceneName);
                return;
            }

            Scene active = SceneManager.GetActiveScene();
            Debug.LogError($"[Run] Scene '{homeSceneName}' is not in Build Settings. Reloading '{active.name}' instead. " +
                           "Run TWR > Run > Validate Run Setup.", this);
            if (active.buildIndex >= 0)
            {
                SceneManager.LoadScene(active.buildIndex);
                return;
            }

            // Nothing loadable: start the new run in place so the game doesn't get stuck.
            pendingNewRun = false;
            isEnding = false;
            StartNewRun();
        }

        static bool CanLoad(string sceneName) => !string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);

        // IRunHandler (RunHooks): PlayerHealth and the future pause menu reach us through TWR.Core.
        void IRunHandler.HandlePlayerDeath() => EndRun(RunEndReason.Death);
        void IRunHandler.HandleQuit() => QuitRun();

        // ---------- checks + logs ----------

        void ValidateConfig()
        {
            if (config == null)
            {
                Debug.LogError("[Run] RunManager has no RunConfig assigned. Run TWR > Run > Create Run Assets.", this);
                return;
            }
            var errors = new List<string>();
            var warnings = new List<string>();
            config.Validate(errors, warnings);
            foreach (string e in errors) Debug.LogError("[Run] RunConfig: " + e, config);
            foreach (string w in warnings) Debug.LogWarning("[Run] RunConfig: " + w, config);
        }

        void WarnAboutMissingPieces(RunPlan plan)
        {
            if (plan.PreForkSections.Count == 0) Debug.LogError("[Run] Plan has no pre-fork sections (empty pool?).", this);
            if (plan.ForkRoom == null) Debug.LogError("[Run] Plan has no fork room.", this);
            if (plan.EscapePathSection == null || plan.DiscoverPathSection == null) Debug.LogError("[Run] Plan is missing a path section.", this);
            if (plan.EscapeBossArena == null || plan.DiscoverBossArena == null) Debug.LogError("[Run] Plan is missing a boss arena (D-148).", this);
        }

        void Log(string message)
        {
            if (logRun) Debug.Log("[Run] " + message, this);
        }
    }
}
