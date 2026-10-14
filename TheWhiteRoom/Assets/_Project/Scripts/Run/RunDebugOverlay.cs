#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TWR.Player;
using TWR.Stats;

namespace TWR.Run
{
    /// <summary>
    /// Developer-only run overlay (D-153, D-159): exists only in the editor and development builds, and its
    /// text is not localized (D-159 exempts it from D-084). F1 opens/closes it, F2 skips to the next section
    /// (fixed keys via Keyboard.current, no rebinding, D-038). Shows the seed (8 hex), curated yes/no, the
    /// whole plan with the current stop highlighted and both branches, the active path and live stats + score
    /// (D-152, D-158). Buttons: next section, choose Escape/Discover at the fork, end run as death, quit run,
    /// start a run with a typed seed, and "pretend endings saved". While open it pauses the PlayerInputReader
    /// (frees the cursor, stops look/shoot) and restores it on close. Added at runtime by the RunManager.
    /// R2 (D-161, D-167): in Core mode "next section" / F2 go through the real SectionLoader (fade + scene load), the
    /// header shows the loaded content scene, and the pause is re-applied after each transition.
    /// </summary>
    [DisallowMultipleComponent]
    public class RunDebugOverlay : MonoBehaviour
    {
        const float Width = 470f;
        const string Highlight = "#FFD54A";
        const string Dim = "#8A8A8A";

        // Buttons only record what to do; Update runs it, so the GUI never changes layout mid-event.
        enum PendingAction { None, NextSection, ChooseEscape, ChooseDiscover, EndAsDeath, Quit, StartWithSeed }

        RunManager manager;
        PendingAction pending;
        uint pendingSeed;
        bool open;
        string seedText = "";
        string message = "";
        Vector2 scroll;
        GUIStyle rich;
        PlayerInputReader pausedReader;

        SectionLoader loader;

        void Awake()
        {
            manager = GetComponent<RunManager>();
            loader = GetComponent<SectionLoader>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            if (loader != null) loader.TransitionFinished += HandleTransitionFinished;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (loader != null) loader.TransitionFinished -= HandleTransitionFinished;
            if (open) SetOpen(false);
        }

        void HandleTransitionFinished()
        {
            // The SectionLoader just gave the player its input back; keep it paused while the overlay is open.
            if (!open) return;
            pausedReader = null;
            PausePlayerInput();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || manager == null) return;

            if (keyboard.f1Key.wasPressedThisFrame) SetOpen(!open);
            if (keyboard.f2Key.wasPressedThisFrame) manager.GoToNextSection();

            PendingAction action = pending;
            pending = PendingAction.None;
            switch (action)
            {
                case PendingAction.NextSection: manager.GoToNextSection(); break;
                case PendingAction.ChooseEscape: manager.ChoosePath(RunPath.Escape); break;
                case PendingAction.ChooseDiscover: manager.ChoosePath(RunPath.Discover); break;
                case PendingAction.EndAsDeath: manager.EndRun(RunEndReason.Death); break;
                case PendingAction.Quit: manager.QuitRun(); break;
                case PendingAction.StartWithSeed: manager.StartRunWithSeed(pendingSeed); break;
            }
        }

        // ---------- cursor / player input ----------

        void SetOpen(bool value)
        {
            open = value;
            if (open) PausePlayerInput();
            else ResumePlayerInput();
        }

        void PausePlayerInput()
        {
            // Called on open and after a scene load, never per frame.
            PlayerInputReader reader = FindAnyObjectByType<PlayerInputReader>();
            if (reader != null && reader.enabled)
            {
                reader.enabled = false; // its OnDisable stops the actions and frees the cursor
                pausedReader = reader;
            }
            else if (reader != null)
            {
                reader.SetCursorLocked(false); // the pause-menu hook (cursor free, visible)
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void ResumePlayerInput()
        {
            if (pausedReader != null)
            {
                // Don't wake a dead player's input (PlayerHealth turned it off on purpose).
                PlayerHealth health = pausedReader.GetComponent<PlayerHealth>();
                if (health == null || health.IsAlive) pausedReader.enabled = true; // OnEnable locks the cursor again
            }
            pausedReader = null;
        }

        void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The lobby reloaded with a fresh player whose input is on: pause that one instead.
            if (!open) return;
            pausedReader = null;
            PausePlayerInput();
        }

        // ---------- drawing (IMGUI, debug only: allocations are acceptable here) ----------

        void OnGUI()
        {
            if (!open || manager == null) return;
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

            float height = Mathf.Min(Screen.height - 20f, 720f);
            GUILayout.BeginArea(new Rect(10f, 10f, Width, height), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("<b>RUN DEBUG</b>   (F1 close, F2 next section)", rich);
            RunPlan plan = manager.Plan;
            if (plan == null)
            {
                GUILayout.Label("No run: the lobby is loading, or the RunConfig is missing (see Console).", rich);
            }
            else
            {
                DrawHeader(plan);
                GUILayout.Space(6f);
                DrawPlan(plan);
                GUILayout.Space(6f);
                DrawStats();
                GUILayout.Space(6f);
                DrawActions();
            }
            GUILayout.Space(6f);
            DrawSeedEntry();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawHeader(RunPlan plan)
        {
            GUILayout.Label($"Run #{manager.RunNumber}   Seed <b>{RunSeed.Format(plan.Seed)}</b>   Curated: <b>{(plan.IsCurated ? "yes" : "no")}</b>", rich);
            GUILayout.Label($"Active path: <b>{manager.ActivePath}</b>   Sections: {plan.SectionCount} (+ lobby, fork room)", rich);
            if (loader != null)
                GUILayout.Label($"Loaded scene: <b>{loader.CurrentContentScene ?? "(none)"}</b>{(manager.IsTransitioning ? "   (transition running...)" : "")}", rich);
            else
                GUILayout.Label($"<color={Dim}>Sandbox mode: sections are only tracked, no scenes load (play the Core scene for real sections).</color>", rich);

            if (manager.EndingsRecord is StubEndingsRecord stub)
            {
                bool pretend = GUILayout.Toggle(stub.PretendEndingsSaved, " Pretend endings saved (next runs random, not curated)");
                if (pretend != stub.PretendEndingsSaved) stub.PretendEndingsSaved = pretend;
            }
            else if (manager.EndingsRecord != null)
            {
                GUILayout.Label($"Endings saved: {manager.EndingsRecord.SeenEndingCount}", rich);
            }
        }

        void DrawPlan(RunPlan plan)
        {
            int current = manager.CurrentStopIndex;
            RunPath active = manager.ActivePath;

            GUILayout.Label("<b>Plan</b>", rich);
            Line(current == RunPlan.LobbyIndex, false, "Hub lobby (connector)");
            for (int i = 0; i < plan.PreForkSections.Count; i++)
                Line(current == i, false, $"{plan.SectionNumber(i)}. {Describe(plan.PreForkSections[i])}");
            Line(current == plan.ForkIndex, false, $"Fork room: {Describe(plan.ForkRoom)} (connector)");

            DrawBranch(plan, RunPath.Escape, current, active);
            DrawBranch(plan, RunPath.Discover, current, active);
        }

        void DrawBranch(RunPlan plan, RunPath branch, int current, RunPath active)
        {
            bool isActive = active == branch;
            bool dimmed = active != RunPath.None && !isActive;
            GUILayout.Label(dimmed ? $"<color={Dim}>{branch} branch</color>" : $"<b>{branch} branch</b>{(isActive ? " (chosen)" : "")}", rich);
            Line(isActive && current == plan.PathSectionIndex, dimmed, $"   {plan.SectionNumber(plan.PathSectionIndex)}. {Describe(plan.GetPathSection(branch))}");
            Line(isActive && current == plan.BossIndex, dimmed, $"   {plan.SectionNumber(plan.BossIndex)}. {Describe(plan.GetBossArena(branch))}");
        }

        void Line(bool isCurrent, bool dimmed, string text)
        {
            if (isCurrent) GUILayout.Label($"<color={Highlight}><b>> {text}   (current)</b></color>", rich);
            else if (dimmed) GUILayout.Label($"<color={Dim}>  {text}</color>", rich);
            else GUILayout.Label("  " + text, rich);
        }

        static string Describe(SectionData s) => s != null ? $"{s.DisplayId} ({s.type})" : "(missing)";

        void DrawStats()
        {
            var stats = manager.Stats;
            if (stats == null) return;
            GUILayout.Label("<b>Stats (D-152, D-158)</b>", rich);
            GUILayout.Label($"Kills by player: {stats.PlayerKills} / enemies spawned: {stats.EnemiesRegistered}", rich);
            GUILayout.Label($"Optional puzzles: {stats.OptionalPuzzlesSolved} / {stats.OptionalPuzzlesTotal}   (all puzzles {stats.PuzzlesSolved}/{stats.PuzzlesTotal})", rich);
            GUILayout.Label($"Score: <b>{stats.Score:0.#}</b> / 100  ->  <b>{(stats.IsGood ? "Good" : "Poor")}</b> (Good at {RunStats.GoodScoreThreshold:0}+)", rich);
        }

        void DrawActions()
        {
            GUILayout.Label("<b>Actions</b>", rich);
            if (GUILayout.Button(manager.CurrentStopIndex == manager.Plan.BossIndex ? "Finish boss arena -> run complete (F2)" : "Next section (F2)"))
                pending = PendingAction.NextSection;

            if (manager.IsAtFork && manager.ActivePath == RunPath.None)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Choose Escape (EXIT door)")) pending = PendingAction.ChooseEscape;
                if (GUILayout.Button("Choose Discover (BASEMENT door)")) pending = PendingAction.ChooseDiscover;
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("End run as Death")) pending = PendingAction.EndAsDeath;
            if (GUILayout.Button("Quit run (D-093)")) pending = PendingAction.Quit;
            GUILayout.EndHorizontal();
        }

        void DrawSeedEntry()
        {
            GUILayout.Label("<b>Seed</b> (hex: 8 chars, 0x.. or with A-F; otherwise decimal; 0 = curated)", rich);
            GUILayout.BeginHorizontal();
            seedText = GUILayout.TextField(seedText, 16, GUILayout.Width(140f));
            if (GUILayout.Button("Use current") && manager.Plan != null) seedText = RunSeed.Format(manager.Plan.Seed);
            if (GUILayout.Button("Start run with this seed"))
            {
                if (RunSeed.TryParse(seedText, out uint seed))
                {
                    message = "Starting run with seed " + RunSeed.Format(seed);
                    pendingSeed = seed;
                    pending = PendingAction.StartWithSeed;
                }
                else message = "Not a valid 32-bit seed: '" + seedText + "'";
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message, rich);
        }
    }
}
#endif
