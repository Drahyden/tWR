using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TWR.Core;
using TWR.Stats;

namespace TWR.Run
{
    /// <summary>
    /// Owns the current run (D-015, D-057, D-151, D-156 to D-159). One instance survives scene loads
    /// (DontDestroyOnLoad); a second copy (e.g. when the lobby scene reloads) destroys itself.
    /// At run start it picks the seed: a debug override if one is queued, seed 0 = the curated first run while
    /// no endings are saved (D-154, D-159), otherwise a random non-zero seed. It builds the RunPlan, steps
    /// through it (lobby, pre-fork sections, fork room, path section, boss arena), and fixes the path when
    /// ChoosePath is called at the fork (D-060, D-149). Death (D-034), quitting (D-093) and finishing the boss
    /// arena end the run: back to the hub lobby scene with a new plan (D-157). It also owns the run-scoped
    /// RunStats (D-158). R1 does not load section scenes yet; "sections" are only tracked (R2 loads them).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public class RunManager : MonoBehaviour, IRunHandler
    {
        [SerializeField] RunConfig config;
        [Tooltip("Write [Run] lines to the Console.")]
        [SerializeField] bool logRun = true;

        /// <summary>The surviving RunManager, or null if none exists.</summary>
        public static RunManager Instance { get; private set; }

        public RunConfig Config => config;
        /// <summary>The current plan. Null for the moment between EndRun and the lobby finishing its reload.</summary>
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

        /// <summary>Who tells us whether endings are saved (D-159). A stub until D-099 save data exists.</summary>
        public IEndingsRecord EndingsRecord { get; set; } = new StubEndingsRecord();

        /// <summary>A new run began (plan, seed and stats are ready).</summary>
        public event Action<RunManager> RunStarted;
        /// <summary>(stop index, section). Section is null for the hub lobby.</summary>
        public event Action<int, SectionData> SectionChanged;
        /// <summary>The player committed to a path at the fork.</summary>
        public event Action<RunPath> PathChosen;
        /// <summary>The run is over; the lobby reloads next and a new run starts.</summary>
        public event Action<RunEndReason> RunEnded;

        bool isEnding;
        bool pendingNewRun = true;
        bool sceneLoadSeen;
        bool hasSeedOverride;
        uint seedOverride;

        // ---------- lifetime ----------

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // The lobby scene reloaded and brought its own bootstrap copy: the original keeps running.
                enabled = false;
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null); // DontDestroyOnLoad only works on root objects
            DontDestroyOnLoad(gameObject);

            Stats = GetComponent<RunStats>();
            if (Stats == null) Stats = gameObject.AddComponent<RunStats>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GetComponent<RunDebugOverlay>() == null) gameObject.AddComponent<RunDebugOverlay>();
#endif

            RunHooks.Register(this);
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ValidateConfig();
        }

        void Start()
        {
            // Normally sceneLoaded already started the first run. If it didn't fire for the first scene, do it here.
            if (Instance != this) return;
            if (!sceneLoadSeen) HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
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
            // Puzzles in every loaded scene count as available this run (D-158).
            Stats.AddLoadedScene(scene);
        }

        // ---------- run start ----------

        /// <summary>
        /// Build a new plan and reset stats, without reloading any scene. Seed: queued debug override, else
        /// 0 (curated) while no endings are saved, else random non-zero (D-154, D-159).
        /// </summary>
        public void StartNewRun()
        {
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

            Log($"Run #{RunNumber} started | seed {RunSeed.Format(plan.Seed)} ({source}) | curated: {(plan.IsCurated ? "yes" : "no")} | " +
                $"{plan.SectionCount} sections | plan: {plan.Describe()}");
            WarnAboutMissingPieces(plan);

            RunStarted?.Invoke(this);
            SectionChanged?.Invoke(CurrentStopIndex, null);
        }

        // ---------- moving through the run ----------

        /// <summary>
        /// Go to the next stop of the plan. At the fork a path must be chosen first. Past the boss arena the
        /// run is complete (stub until the end screen, D-092): it ends and returns to the lobby.
        /// R2 will call this from section exits; for now the debug overlay does (F2).
        /// </summary>
        public bool GoToNextSection()
        {
            if (!IsRunActive) return false;

            if (CurrentStopIndex == Plan.ForkIndex && ActivePath == RunPath.None)
            {
                Debug.LogWarning("[Run] At the fork room: choose Escape or Discover first (D-060).", this);
                return false;
            }
            if (CurrentStopIndex >= Plan.BossIndex)
            {
                Log("Boss arena finished -> run complete (end screen D-092 comes later).");
                EndRun(RunEndReason.Completed);
                return true;
            }

            CurrentStopIndex++;
            SectionData section = CurrentSection;
            if (CurrentStopIndex == Plan.ForkIndex)
                Log($"Fork room: {RunPlan.Name(section)} (connector) - choose Escape or Discover");
            else
                Log($"Section {Plan.SectionNumber(CurrentStopIndex)}/{Plan.SectionCount}: {RunPlan.Name(section)}" +
                    $"{(section != null ? " (" + section.type + ")" : "")}{(ActivePath != RunPath.None ? " on path " + ActivePath : "")}");
            SectionChanged?.Invoke(CurrentStopIndex, section);
            return true;
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

        // ---------- run end ----------

        /// <summary>Quit mid-run (D-093): resets the run like death. The future pause menu calls this.</summary>
        public void QuitRun() => EndRun(RunEndReason.Quit);

        /// <summary>
        /// Debug only (D-153): queue a seed and restart. If a run is going, it ends (reason Debug) and the
        /// lobby reloads; the queued seed is used once, by the next run.
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
        /// End the run (D-157): log the result, discard the plan, reload the hub lobby scene; the new run
        /// starts as soon as the lobby has loaded (new seed, or the curated plan while no endings are saved).
        /// </summary>
        public void EndRun(RunEndReason reason)
        {
            if (!IsRunActive) return;
            isEnding = true;

            string where = CurrentStopIndex == RunPlan.LobbyIndex ? "hub lobby" : RunPlan.Name(CurrentSection);
            Log($"Run #{RunNumber} ended: {reason} | seed {RunSeed.Format(Plan.Seed)} | reached {where} | path {ActivePath} | {Stats.Summary()}");
            RunEnded?.Invoke(reason);

            Plan = null;
            pendingNewRun = true;
            LoadLobby();
        }

        void LoadLobby()
        {
            string lobby = config != null ? config.lobbySceneName : null;
            if (!string.IsNullOrEmpty(lobby) && Application.CanStreamedLevelBeLoaded(lobby))
            {
                SceneManager.LoadScene(lobby);
                return;
            }

            Scene active = SceneManager.GetActiveScene();
            Debug.LogError($"[Run] Lobby scene '{lobby}' is not in Build Settings. Reloading '{active.name}' instead. " +
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
