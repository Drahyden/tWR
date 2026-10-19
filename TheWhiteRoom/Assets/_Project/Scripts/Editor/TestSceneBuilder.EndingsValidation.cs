using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;
using TWR.Audio;
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
    /// [TWR] validation for R5 (D-195 to D-212), used by "TWR/Run/Build Endings + Main Menu", "TWR/Run/Build Run Scenes"
    /// and "TWR/Run/Validate Run Setup":
    /// - the RunConfig has an EndingCatalog; the launch endings cover Escape/Discover x Good/Poor exactly once (D-028,
    ///   D-203) and a sample result per cell selects the expected ending; ids unique; secret endings outrank launch ones;
    /// - every ending's scene exists in Scenes/Run and is enabled in Build Settings; its title (UI) and line (Subtitles)
    ///   keys exist; every end-screen / main-menu key exists (D-084);
    /// - each ending scene: one EndingDirector pointing at its EndingData, one SectionEntry with a locked door behind,
    ///   NO Seekers, spawn points, puzzles, switches, bosses, pickups, section exits, portals or Core objects (D-204);
    ///   scripted timelines fit 10-30 s (D-199); Overtime has its button on the Interactable layer (D-196);
    /// - MainMenu is build index 0 and Core index 1 (D-210); the menu scene has its controller, buttons and event system
    ///   and no Core objects; it has a wired Credits screen and CreditsData matches docs/audio/LICENSES.md (D-229).
    /// </summary>
    public static partial class TestSceneBuilder
    {
        /// Fixed UI keys used by the end-of-run screen and the main menu (D-084, D-200, D-202).
        static readonly string[] R5UiKeys =
        {
            "endscreen.path.escape", "endscreen.path.discover", "endscreen.score.good", "endscreen.score.poor",
            "endscreen.kills", "endscreen.puzzles", "endscreen.time", "endscreen.seed", "endscreen.endings_found",
            "endscreen.slot.locked", "endscreen.new_run", "endscreen.main_menu",
            "menu.title", "menu.new_run", "menu.settings", "menu.quit", "menu.settings.title", "menu.settings.placeholder", "menu.back",
            "interact.generic", "sign.exit",
        };

        static bool ValidateEndings()
        {
            var report = new StringBuilder();
            report.AppendLine("[TWR] Endings, end screen + main menu (R5, D-195 to D-212)");
            bool ok = true;

            var config = AssetDatabase.LoadAssetAtPath<RunConfig>(RunConfigPath);
            EndingCatalog catalog = config != null ? config.endingCatalog : null;
            ok &= Check(report, "RunConfig has an EndingCatalog (D-203)", catalog != null,
                        catalog != null ? AssetDatabase.GetAssetPath(catalog) : "missing - run TWR > Run > Build Endings + Main Menu");
            ok &= Check(report, "RunConfig main menu scene (D-202)", config != null && config.mainMenuSceneName == MainMenuSceneName,
                        config != null ? config.mainMenuSceneName : "no RunConfig");
            if (catalog == null)
            {
                Debug.LogError(report.ToString());
                return false;
            }

            // Catalog rules (shared with the runtime check).
            var errors = new List<string>();
            var warnings = new List<string>();
            catalog.Validate(errors, warnings);
            foreach (string e in errors) report.AppendLine("  PROBLEM: " + e);
            foreach (string w in warnings) report.AppendLine("  WARNING: " + w);
            ok &= errors.Count == 0;

            // Selection per cell with a sample result (score 100 = Good, 0 = Poor) and an empty record.
            var empty = new StubEndingsRecord();
            foreach (RunPath path in new[] { RunPath.Escape, RunPath.Discover })
                foreach (bool good in new[] { true, false })
                {
                    RunResult sample = good ? new RunResult(path, 4, 4, 1, 1, 600f, 1u, false, true)
                                            : new RunResult(path, 0, 4, 0, 1, 600f, 1u, false, true);
                    EndingData chosen = catalog.Select(sample, empty);
                    ok &= Check(report, $"{path} + {(good ? "Good" : "Poor")} (score {sample.Score:0}) selects a launch ending",
                                chosen != null && !chosen.isSecret, chosen != null ? chosen.DisplayId : "none");
                }
            report.AppendLine($"  launch endings: {catalog.LaunchCount} (end screen 'X / {catalog.LaunchCount}', D-208)");
            ok &= Check(report, "four launch endings (D-025)", catalog.LaunchCount == 4, catalog.LaunchCount.ToString());

            // Scenes, Build Settings, localization keys.
            var enabled = new HashSet<string>(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)));
            foreach (EndingData ending in catalog.endings)
            {
                if (ending == null) continue;
                bool exists = !string.IsNullOrEmpty(ending.sceneName) && File.Exists(RunScenePath(ending.sceneName));
                ok &= Check(report, $"'{ending.DisplayId}': scene '{ending.sceneName}' exists and is in Build Settings", exists && enabled.Contains(ending.sceneName),
                            $"file {(exists ? "yes" : "NO")}, build settings {(enabled.Contains(ending.sceneName) ? "yes" : "NO")}");
                ok &= Check(report, $"'{ending.DisplayId}': title key", KeyExists(ending.title), KeyName(ending.title));
                if (ending.presentation == EndingPresentation.Scripted)
                    ok &= Check(report, $"'{ending.DisplayId}': inner-voice subtitle key (D-199)", KeyExists(ending.voiceLine), KeyName(ending.voiceLine));
                else if (ending.voiceLine != null && !ending.voiceLine.IsEmpty)
                    ok &= Check(report, $"'{ending.DisplayId}': voice line key", KeyExists(ending.voiceLine), KeyName(ending.voiceLine));
                if (exists) ok &= ValidateEndingScene(report, ending);
            }

            var missingKeys = R5UiKeys.Where(k => !ProjectSetup.TableHasEnglish("UI", k)).ToList();
            ok &= Check(report, $"{R5UiKeys.Length} end-screen / menu keys in table 'UI' (D-084)", missingKeys.Count == 0,
                        missingKeys.Count == 0 ? "" : "missing: " + string.Join(", ", missingKeys) + " (run TWR > Setup Project; also missing if the English text is empty)");

            // Main menu (D-202, D-210).
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            bool menuFirst = scenes.Length > 0 && scenes[0].enabled && scenes[0].path == RunScenePath(MainMenuSceneName);
            bool coreSecond = scenes.Length > 1 && scenes[1].enabled && scenes[1].path == RunScenePath(CoreSceneName);
            ok &= Check(report, "MainMenu is build index 0, Core index 1 (D-210)", menuFirst && coreSecond,
                        scenes.Length > 1 ? $"0: {Path.GetFileNameWithoutExtension(scenes[0].path)}, 1: {Path.GetFileNameWithoutExtension(scenes[1].path)}" : "too few scenes");
            if (File.Exists(RunScenePath(MainMenuSceneName))) ok &= WithScene(RunScenePath(MainMenuSceneName), scene => InspectMainMenu(report, scene));
            else ok &= Check(report, "MainMenu scene exists", false, RunScenePath(MainMenuSceneName));
            ok &= ValidateCredits(report); // D-229

            if (ok) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
            return ok;
        }

        static bool ValidateEndingScene(StringBuilder report, EndingData ending)
        {
            return WithScene(RunScenePath(ending.sceneName), scene =>
            {
                bool ok = true;
                string p = $"  [{ending.sceneName}] ";
                List<EndingDirector> directors = InScene<EndingDirector>(scene);
                ok &= Check(report, p + "one EndingDirector for this EndingData", directors.Count == 1 && directors[0].Ending == ending,
                            directors.Count == 1 ? (directors[0].Ending != null ? directors[0].Ending.DisplayId : "no EndingData") : directors.Count + " found");
                List<SectionEntry> entries = InScene<SectionEntry>(scene);
                ok &= Check(report, p + "one entry spawn", entries.Count == 1, entries.Count.ToString());
                if (entries.Count == 1) ok &= CheckEntryDoor(report, scene, entries[0]);

                // D-204: endings register no enemies or puzzles and never change the result.
                int forbidden = InScene<Seeker>(scene).Count + InScene<SeekerSpawnPoint>(scene).Count + InScene<PuzzleController>(scene).Count +
                                InScene<ShootableSwitch>(scene).Count + InScene<BossEncounter>(scene).Count + InScene<BossMarker>(scene).Count +
                                InScene<ItemPickup>(scene).Count + InScene<PickupGroup>(scene).Count + InScene<SectionExit>(scene).Count +
                                InScene<Portal>(scene).Count;
                ok &= Check(report, p + "no Seekers, spawn points, puzzles, switches, bosses, pickups, exits, portals (D-204)", forbidden == 0, forbidden.ToString());
                int coreOnly = InScene<PlayerController>(scene).Count + InScene<RunManager>(scene).Count + InScene<AmbientBed>(scene).Count +
                               InScene<PortalRenderManager>(scene).Count + InScene<EventSystem>(scene).Count;
                ok &= Check(report, p + "no Core-only objects", coreOnly == 0, coreOnly.ToString());

                if (directors.Count != 1) return false;
                EndingDirector d = directors[0];
                List<OvertimeButton> buttons = InScene<OvertimeButton>(scene);
                if (ending.presentation == EndingPresentation.Scripted)
                {
                    float duration = d.DurationSeconds;
                    ok &= Check(report, p + $"scripted length {EndingCatalog.ScriptedMinSeconds}-{EndingCatalog.ScriptedMaxSeconds} s (D-199)",
                                duration >= EndingCatalog.ScriptedMinSeconds && duration <= EndingCatalog.ScriptedMaxSeconds, $"{duration:0.#} s");
                    ok &= Check(report, p + "voice line inside the scene", d.VoiceLineAtSeconds < duration, $"at {d.VoiceLineAtSeconds:0.#} s");
                    bool stepsOk = true;
                    float lastEnd = 0f;
                    foreach (EndingCameraStep step in d.CameraSteps)
                    {
                        stepsOk &= step.target != null && step.startSeconds >= lastEnd - 0.001f && step.startSeconds + step.seconds <= duration;
                        lastEnd = step.startSeconds + step.seconds;
                    }
                    ok &= Check(report, p + "camera steps have targets, run in order, end in time", stepsOk, d.CameraSteps.Length + " step(s)");
                    if (d.TintAlpha > 0f)
                    {
                        float tintEnd = d.TintStartSeconds + new SerializedObject(d).FindProperty("tintSeconds").floatValue;
                        ok &= Check(report, p + "tint starts after the fade-in and ends in time", d.TintStartSeconds >= 1f && tintEnd <= duration, $"{d.TintStartSeconds:0.#}-{tintEnd:0.#} s");
                    }
                    ok &= Check(report, p + "no Overtime button in a scripted ending", buttons.Count == 0, buttons.Count.ToString());
                }
                else
                {
                    int interactable = LayerMask.NameToLayer(InteractableLayerName);
                    bool buttonOk = buttons.Count == 1 && buttons[0].GetComponent<Collider>() != null &&
                                    buttons[0].gameObject.layer == interactable &&
                                    new SerializedObject(buttons[0]).FindProperty("director").objectReferenceValue == d;
                    ok &= Check(report, p + "one Overtime button (IInteractable, Interactable layer, wired to the director; D-196)", buttonOk, buttons.Count.ToString());
                }
                return ok;
            });
        }

        static bool InspectMainMenu(StringBuilder report, Scene scene)
        {
            bool ok = true;
            List<MainMenuController> menus = InScene<MainMenuController>(scene);
            if (menus.Count == 1)
            {
                var so = new SerializedObject(menus[0]);
                string[] fields = { "config", "mainPanel", "settingsPanel", "creditsPanel", "newRunButton", "settingsButton",
                                    "creditsButton", "quitButton", "backButton", "creditsBackButton" };
                var missing = fields.Where(f => so.FindProperty(f).objectReferenceValue == null).ToList();
                ok &= Check(report, "  [MainMenu] controller wired (New run, Settings, Credits, Quit, Back)", missing.Count == 0,
                            missing.Count == 0 ? "" : "missing: " + string.Join(", ", missing) + " (rebuild: TWR > Run > Build Endings + Main Menu)");
            }
            else ok &= Check(report, "  [MainMenu] one MainMenuController", false, menus.Count.ToString());
            List<CreditsScreen> credits = InScene<CreditsScreen>(scene);
            bool creditsWired = credits.Count == 1 && new[] { "data", "headingTemplate", "lineTemplate" }
                                .All(f => new SerializedObject(credits[0]).FindProperty(f).objectReferenceValue != null);
            ok &= Check(report, "  [MainMenu] Credits screen with data and templates (D-229)", creditsWired,
                        credits.Count == 1 ? "" : credits.Count + " CreditsScreen(s)");
            int eventSystems = InScene<EventSystem>(scene).Count;
            ok &= Check(report, "  [MainMenu] event system", eventSystems == 1, eventSystems.ToString());
            int cameras = InScene<Camera>(scene).Count;
            ok &= Check(report, "  [MainMenu] camera", cameras >= 1, cameras.ToString());
            int coreOnly = InScene<PlayerController>(scene).Count + InScene<RunManager>(scene).Count;
            ok &= Check(report, "  [MainMenu] loads alone: no player / RunManager (D-210)", coreOnly == 0, coreOnly.ToString());
            return ok;
        }

        /// Opens a scene additively (if not open), runs the check, closes it again.
        static bool WithScene(string path, System.Func<Scene, bool> check)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }
            try
            {
                return check(scene);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static bool KeyExists(LocalizedString text) =>
            text != null && !text.IsEmpty && ProjectSetup.TableHasEnglish(text.TableReference.TableCollectionName, text.TableEntryReference.Key);

        static string KeyName(LocalizedString text) =>
            text == null || text.IsEmpty ? "(empty)" : $"{text.TableReference.TableCollectionName}/{text.TableEntryReference.Key}";
    }
}
