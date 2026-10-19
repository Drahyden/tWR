#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TWR.Audio;
using TWR.Player;
using TWR.Enemies;
using TWR.Endings;
using TWR.Save;
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
    /// R3 (D-172): under every plan stop it lists what was rolled there (enemy spawn ids, pickup spot ids, puzzle
    /// variant + active switches in order) once the section has loaded this run; seed 0 shows "curated, fixed".
    /// R4 (D-185): a Boss block: buttons "Go to Bull arena" / "Go to Preacher arena" (through the real SectionLoader,
    /// RunManager.DebugGoToBossArena), "Set boss HP" (absolute, or a percentage like 50%), "Force next Bull charge fast",
    /// and the live boss state: HP, phase, attack counter / next fast-charge count, current teleport target.
    /// R5 (D-201, D-203, D-205): an Endings block: one button per ending in the catalog that plays it at once with a fake
    /// RunResult and records it as found (like a real run end), "Finish ending now", the saved endings and "Clear saved
    /// endings" (next run is the curated first run again). Closing the overlay never gives input back during a scripted
    /// ending or on the end screen (D-207).
    /// A2 (D-235): a Music section (TWR.Audio.MusicDebugPanel): state, track, layers with on/off + volume, puzzle timer,
    /// chaser count, force buttons. While this overlay exists it is the F1 owner; the director's music-only overlay
    /// (MainMenu) stays passive.
    /// </summary>
    [DisallowMultipleComponent]
    public class RunDebugOverlay : MonoBehaviour
    {
        const float Width = 470f;
        const string Highlight = "#FFD54A";
        const string Dim = "#8A8A8A";

        // Buttons only record what to do; Update runs it, so the GUI never changes layout mid-event.
        enum PendingAction { None, NextSection, ChooseEscape, ChooseDiscover, EndAsDeath, Quit, StartWithSeed, GoToBull, GoToPreacher, SetBossHealth, ForceFastCharge,
                             TriggerEnding, FinishEnding, ClearEndings }

        RunManager manager;
        PendingAction pending;
        uint pendingSeed;
        EndingData pendingEnding;
        float pendingBossHealth;
        string bossHealthText = "50%";
        string bossMessage = "";
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
            MusicDebugPanel.RegisterHost(); // A2: this overlay owns F1 and draws the Music section (D-235)
            SceneManager.sceneLoaded += HandleSceneLoaded;
            if (loader != null) loader.TransitionFinished += HandleTransitionFinished;
        }

        void OnDisable()
        {
            MusicDebugPanel.UnregisterHost();
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
                case PendingAction.GoToBull: manager.DebugGoToBossArena(RunPath.Escape); break;      // Escape = Bull (D-148)
                case PendingAction.GoToPreacher: manager.DebugGoToBossArena(RunPath.Discover); break; // Discover = Preacher (D-148)
                case PendingAction.SetBossHealth: SetBossHealth(pendingBossHealth); break;
                case PendingAction.ForceFastCharge: ForceFastCharge(); break;
                case PendingAction.TriggerEnding: manager.DebugTriggerEnding(pendingEnding, true); break; // records (D-205)
                case PendingAction.FinishEnding: manager.FinishEnding(); break;
                case PendingAction.ClearEndings: if (manager.EndingsRecord != null) manager.EndingsRecord.ClearAll(); break;
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
                // ...nor during a scripted ending / the end screen (D-207, D-209).
                if ((health == null || health.IsAlive) && (manager == null || !manager.BlocksPlayerInput))
                    pausedReader.enabled = true; // OnEnable locks the cursor again
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
                GUILayout.Space(6f);
                DrawBoss();
                GUILayout.Space(6f);
                DrawEndings();
            }
            GUILayout.Space(6f);
            MusicDebugPanel.Draw(rich); // A2 (D-235): state, layers, timers, force buttons
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
                string ids = manager.EndingsRecord is EndingsSave save ? string.Join(", ", save.FoundIds) : "";
                GUILayout.Label($"Endings saved: <b>{manager.EndingsRecord.SeenEndingCount}</b> {(ids.Length > 0 ? "(" + ids + ")" : "")}" +
                                $"  -> next run: {(manager.EndingsRecord.SeenEndingCount == 0 ? "curated" : "random")}", rich);
                if (GUILayout.Button("Clear saved endings (D-201)")) pending = PendingAction.ClearEndings;
            }
        }

        void DrawPlan(RunPlan plan)
        {
            int current = manager.CurrentStopIndex;
            RunPath active = manager.ActivePath;

            GUILayout.Label("<b>Plan</b>   <size=11>(rolls per section, D-172)</size>", rich);
            Line(current == RunPlan.LobbyIndex, false, "Hub lobby (connector)");
            Rolls(RunPlan.LobbyIndex, plan, false, true);
            for (int i = 0; i < plan.PreForkSections.Count; i++)
            {
                Line(current == i, false, $"{plan.SectionNumber(i)}. {Describe(plan.PreForkSections[i])}");
                Rolls(i, plan, false, false);
            }
            Line(current == plan.ForkIndex, false, $"Fork room: {Describe(plan.ForkRoom)} (connector)");
            Rolls(plan.ForkIndex, plan, false, false);

            DrawBranch(plan, RunPath.Escape, current, active);
            DrawBranch(plan, RunPath.Discover, current, active);
        }

        void DrawBranch(RunPlan plan, RunPath branch, int current, RunPath active)
        {
            bool isActive = active == branch;
            bool dimmed = active != RunPath.None && !isActive;
            GUILayout.Label(dimmed ? $"<color={Dim}>{branch} branch</color>" : $"<b>{branch} branch</b>{(isActive ? " (chosen)" : "")}", rich);
            Line(isActive && current == plan.PathSectionIndex, dimmed, $"   {plan.SectionNumber(plan.PathSectionIndex)}. {Describe(plan.GetPathSection(branch))}");
            Rolls(plan.PathSectionIndex, plan, dimmed, false);
            Line(isActive && current == plan.BossIndex, dimmed, $"   {plan.SectionNumber(plan.BossIndex)}. {Describe(plan.GetBossArena(branch))}");
            Rolls(plan.BossIndex, plan, dimmed, false);
        }

        /// <summary>
        /// What was rolled at this stop (D-172): the stored report once the section has loaded this run, otherwise
        /// "curated, fixed" (seed 0) or "not loaded yet". Rolls depend on the scene's authored spots, so unvisited
        /// sections can't be previewed without loading them.
        /// </summary>
        void Rolls(int stopIndex, RunPlan plan, bool otherBranch, bool isLobby)
        {
            string text;
            if (isLobby) text = "rolls: none, the lobby is fixed (D-179)";
            else if (!otherBranch && manager.RollReports.TryGetValue(stopIndex, out SectionRollReport report))
            {
                string head = report.Mode == SectionRollMode.Curated ? "curated, fixed" : "rolled";
                const string NewLine = "\n      ";
                text = head + ": " + (report.Lines.Count > 0 ? string.Join(NewLine, report.Lines) : "nothing to roll");
                if (report.Problems.Count > 0)
                    text += NewLine + $"<color=#FF7A6A>{report.Problems.Count} fallback(s): {string.Join("; ", report.Problems)}</color>";
            }
            else if (plan.IsCurated) text = "curated, fixed (authored defaults)";
            else text = otherBranch ? "other branch" : "not loaded yet";
            GUILayout.Label($"<color={Dim}><size=11>      {text}</size></color>", rich);
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
            if (GUILayout.Button(manager.CurrentStopIndex == manager.Plan.BossIndex ? "Finish boss arena -> run complete + ending (F2)" : "Next section (F2)"))
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

        // ---------- endings (R5, D-203, D-205) ----------

        void DrawEndings()
        {
            GUILayout.Label("<b>Endings (D-203)</b>   <size=11>trigger = fake result + recorded as found</size>", rich);
            if (manager.IsInEnding)
            {
                RunResult r = manager.LastResult;
                GUILayout.Label($"In ending <b>{(manager.CurrentEnding != null ? manager.CurrentEnding.id : "?")}</b>: {(r != null ? r.Describe() : "no result")}", rich);
                if (GUILayout.Button("Finish ending now (fade + end screen)")) pending = PendingAction.FinishEnding;
            }
            EndingCatalog catalog = manager.Config != null ? manager.Config.endingCatalog : null;
            if (catalog == null || catalog.endings == null)
            {
                GUILayout.Label($"<color={Dim}>No EndingCatalog in the RunConfig (TWR > Run > Build Endings + Main Menu).</color>", rich);
                return;
            }
            foreach (EndingData e in catalog.endings)
            {
                if (e == null) continue;
                bool seen = manager.EndingsRecord != null && manager.EndingsRecord.HasFound(e.id);
                if (GUILayout.Button($"Trigger '{e.id}' ({e.rule}){(e.isSecret ? " [secret]" : "")}{(seen ? "  - found" : "")}"))
                {
                    pendingEnding = e;
                    pending = PendingAction.TriggerEnding;
                }
            }
        }

        // ---------- boss (R4, D-185) ----------

        void DrawBoss()
        {
            GUILayout.Label("<b>Boss (D-185)</b>", rich);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Go to Bull arena (Escape)")) pending = PendingAction.GoToBull;
            if (GUILayout.Button("Go to Preacher arena (Discover)")) pending = PendingAction.GoToPreacher;
            GUILayout.EndHorizontal();

            BossEncounter boss = BossEncounter.Active;
            if (boss == null)
            {
                GUILayout.Label($"<color={Dim}>No boss loaded (go to an arena first).</color>", rich);
                return;
            }

            BossHealth health = boss.Health;
            string hp = health != null ? $"{health.Health:0} / {health.MaxHealth:0} ({health.Normalized * 100f:0}%)" : "?";
            GUILayout.Label($"'{boss.BossId}'   HP <b>{hp}</b>   phase <b>{(health != null ? health.Phase : 0)}</b>   " +
                            $"started: {(boss.IsStarted ? "yes" : "no")}   defeated: {(boss.IsDefeated ? "yes" : "no")}   " +
                            $"damage x{(health != null ? health.DamageMultiplier : 1f):0.#}", rich);
            GUILayout.Label($"Boss stream (D-189): {boss.StreamMode}, seed {RunSeed.Format(boss.StreamSeed)}", rich);
            if (boss.Brain != null) GUILayout.Label(boss.Brain.DebugState, rich);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Set HP (e.g. 300 or 25%):", rich, GUILayout.Width(170f));
            bossHealthText = GUILayout.TextField(bossHealthText, 10, GUILayout.Width(80f));
            if (GUILayout.Button("Set boss HP"))
            {
                if (TryParseBossHealth(bossHealthText, health, out float value))
                {
                    pendingBossHealth = value;
                    pending = PendingAction.SetBossHealth;
                }
                else bossMessage = "Not a number: '" + bossHealthText + "'";
            }
            GUILayout.EndHorizontal();
            if (boss.Brain is BullBrain && GUILayout.Button("Force next Bull charge fast")) pending = PendingAction.ForceFastCharge;
            if (!string.IsNullOrEmpty(bossMessage)) GUILayout.Label(bossMessage, rich);
        }

        /// "300" = absolute HP, "25%" = share of max HP.
        static bool TryParseBossHealth(string text, BossHealth health, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            bool percent = text.EndsWith("%");
            if (percent) text = text.Substring(0, text.Length - 1).Trim();
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float number))
                return false;
            value = percent && health != null ? health.MaxHealth * number / 100f : number;
            return true;
        }

        void SetBossHealth(float value)
        {
            BossEncounter boss = BossEncounter.Active;
            if (boss == null || boss.Health == null) return;
            boss.Health.DebugSetHealth(value);
            bossMessage = $"Boss HP set to {Mathf.Max(0f, value):0}.";
            Debug.Log($"[Run] Debug: boss '{boss.BossId}' HP set to {Mathf.Max(0f, value):0} (D-185).", boss);
        }

        void ForceFastCharge()
        {
            if (BossEncounter.Active != null && BossEncounter.Active.Brain is BullBrain bull)
            {
                bull.ForceNextChargeFast();
                bossMessage = "Next Bull charge will be FAST.";
            }
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
