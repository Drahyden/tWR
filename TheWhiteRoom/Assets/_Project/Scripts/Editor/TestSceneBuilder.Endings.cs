using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using TWR.Audio;
using TWR.Endings;
using TWR.Run;
using TWR.UI;

namespace TWR.EditorTools
{
    /// <summary>
    /// Run Structure step R5 (D-195 to D-212): endings, end-of-run screen and main menu, all generated from code.
    /// Menu "TWR/Run/Build Endings + Main Menu" (also part of "TWR/Run/Build Run Scenes"):
    /// - Assets: one EndingData per launch ending (Data/Endings: Clock Out = Escape+Good, Overtime = Escape+Poor,
    ///   Floor Plan = Discover+Good, Absorbed = Discover+Poor; D-195 to D-198, D-203), placeholder PNG graphics
    ///   (Art/Endings, replaceable), the EndingCatalog, and the RunConfig's catalog + main menu scene name.
    /// - Scenes in Scenes/Run: Ending_ClockOut, Ending_Overtime, Ending_FloorPlan, Ending_Absorbed (greybox, no NavMesh,
    ///   no enemies/puzzles, D-204) and MainMenu (D-202). Core is rebuilt so it gets the end-of-run screen (D-200), the
    ///   subtitle line (D-089) and the EndingSequence (D-207).
    /// - Build Settings: MainMenu first (index 0), then Core, the content scenes and the ending scenes (D-210).
    /// Validation lives in TestSceneBuilder.EndingsValidation.cs. Assets are created AFTER NewScene, like every builder.
    /// Existing EndingData assets keep their edited values; only empty fields are filled.
    /// A1 (D-225): the main menu's Settings screen holds the Sound settings panel (TestSceneBuilder.Audio.cs) and every menu
    /// and end-screen button gets a click sound (UiButtonSound).
    /// D-229: the main menu also gets a Credits button and panel (TestSceneBuilder.Credits.cs).
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string EndingsDataFolder = Root + "/Data/Endings";
        const string EndingCatalogPath = EndingsDataFolder + "/EndingCatalog.asset";
        const string EndingArtFolder = Root + "/Art/Endings";
        const string MainMenuSceneName = "MainMenu";
        const int EndingPriority = 10;

        struct EndingSpec
        {
            public string asset, id, scene, titleKey, lineKey;
            public RunPath path;
            public EndingPerformance performance;
            public EndingPresentation presentation;
            public Color fade;
            public float fadeSeconds;
            public Color artBackground, artMotif;
            public int motif; // 0 door, 1 button, 2 floor plan, 3 row of figures
            public System.Func<bool> build;
        }

        static readonly EndingSpec[] EndingSpecs =
        {
            new EndingSpec
            {
                asset = "Ending_ClockOut", id = "ending.clock_out", scene = "Ending_ClockOut",
                titleKey = "ending.clock_out.title", lineKey = "ending.clock_out.line",
                path = RunPath.Escape, performance = EndingPerformance.Good, presentation = EndingPresentation.Scripted,
                fade = Color.white, fadeSeconds = 1.5f,
                artBackground = new Color(0.82f, 0.84f, 0.86f), artMotif = new Color(0.02f, 0.02f, 0.02f), motif = 0,
                build = BuildEndingClockOutScene,
            },
            new EndingSpec
            {
                asset = "Ending_Overtime", id = "ending.overtime", scene = "Ending_Overtime",
                titleKey = "ending.overtime.title", lineKey = null,
                path = RunPath.Escape, performance = EndingPerformance.Poor, presentation = EndingPresentation.Interactive,
                fade = Color.black, fadeSeconds = 0.25f,
                artBackground = new Color(0.98f, 0.98f, 0.98f), artMotif = new Color(0.85f, 0.08f, 0.05f), motif = 1,
                build = BuildEndingOvertimeScene,
            },
            new EndingSpec
            {
                asset = "Ending_FloorPlan", id = "ending.floor_plan", scene = "Ending_FloorPlan",
                titleKey = "ending.floor_plan.title", lineKey = "ending.floor_plan.line",
                path = RunPath.Discover, performance = EndingPerformance.Good, presentation = EndingPresentation.Scripted,
                fade = Color.white, fadeSeconds = 1.5f,
                artBackground = new Color(0.95f, 0.95f, 0.93f), artMotif = new Color(0.1f, 0.1f, 0.12f), motif = 2,
                build = BuildEndingFloorPlanScene,
            },
            new EndingSpec
            {
                asset = "Ending_Absorbed", id = "ending.absorbed", scene = "Ending_Absorbed",
                titleKey = "ending.absorbed.title", lineKey = "ending.absorbed.line",
                path = RunPath.Discover, performance = EndingPerformance.Poor, presentation = EndingPresentation.Scripted,
                fade = Color.white, fadeSeconds = 2f,
                artBackground = new Color(0.93f, 0.86f, 0.78f), artMotif = new Color(0.05f, 0.03f, 0.03f), motif = 3,
                build = BuildEndingAbsorbedScene,
            },
        };

        // ---------- menu ----------

        [MenuItem("TWR/Run/Build Endings + Main Menu")]
        public static void BuildEndingsMenu()
        {
            ProjectSetup.Run(BuildEndingsAfterSetup); // adds the R5 localization entries first
        }

        static void BuildEndingsAfterSetup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (!File.Exists(RunScenePath(LobbySceneName)))
            {
                Debug.LogWarning("[TWR] The run scenes are not built yet: building all of them (R2 to R5) now.");
                BuildRunScenesAfterSetup();
                return;
            }

            Debug.Log("[TWR] Building R5 (D-195 to D-212): Core (end screen, subtitles, ending sequence), 4 ending scenes, MainMenu ...");
            EnsureFolder(RunScenesFolder);
            reportedAudioProblems.Clear(); // A1: report each missing mixer part once per build
            bool built = BuildRunScene(CoreSceneName, BuildCoreScene, false);
            built &= BuildEndingScenesAndMenu();
            bool configured = ConfigureEndingAssets();
            ConfigureRunBuildSettings();

            EditorSceneManager.OpenScene(RunScenePath(CoreSceneName), OpenSceneMode.Single);
            bool valid = ValidateRunSetup();
            valid &= ValidateRunScenes();
            valid &= ValidateEndings();
            valid &= ValidateAudio(); // A1 (D-227)
            valid &= ValidateLocalization(); // every localized text has a key with English (D-084)

            if (built && configured && valid)
                Debug.Log("[TWR] Endings + main menu built and valid. Open " + RunScenePath(MainMenuSceneName) + " and press Play (or Play in Core for a run).");
            else
                Debug.LogError("[TWR] Endings + main menu built with problems (see errors above).");
        }

        /// The four ending scenes and the main menu (each starts from a new empty scene).
        static bool BuildEndingScenesAndMenu()
        {
            bool ok = true;
            foreach (EndingSpec spec in EndingSpecs) ok &= BuildRunScene(spec.scene, spec.build, false); // no Seekers: no NavMesh
            ok &= BuildRunScene(MainMenuSceneName, BuildMainMenuScene, false);
            return ok;
        }

        // ---------- assets ----------

        /// EndingData x4 + graphics + catalog (created if missing, empty fields filled). Call AFTER NewScene.
        static bool PrepareEndingAssets(out EndingCatalog catalog)
        {
            catalog = null;
            EnsureFolder(EndingsDataFolder);
            EnsureFolder(EndingArtFolder);

            var datas = new List<EndingData>();
            foreach (EndingSpec spec in EndingSpecs)
            {
                EndingSpec s = spec;
                EndingData data = LoadOrCreate<EndingData>(EndingsDataFolder + "/" + s.asset + ".asset", e =>
                {
                    e.id = s.id;
                    e.sceneName = s.scene;
                    e.presentation = s.presentation;
                    e.finishFadeColour = s.fade;
                    e.finishFadeSeconds = s.fadeSeconds;
                    e.isSecret = false;
                    e.priority = EndingPriority;
                    e.rule = new EndingRule { path = s.path, performance = s.performance, minScore = 0f, requiredFoundEndings = new string[0] };
                });
                if (data == null)
                {
                    Debug.LogError("[TWR] Could not create the EndingData " + s.asset);
                    return false;
                }

                bool changed = false;
                if (string.IsNullOrEmpty(data.id)) { data.id = s.id; changed = true; }
                if (string.IsNullOrEmpty(data.sceneName)) { data.sceneName = s.scene; changed = true; }
                if (data.title == null || data.title.IsEmpty) { data.title = new LocalizedString("UI", s.titleKey); changed = true; }
                if (!string.IsNullOrEmpty(s.lineKey) && (data.voiceLine == null || data.voiceLine.IsEmpty))
                {
                    data.voiceLine = new LocalizedString("Subtitles", s.lineKey);
                    changed = true;
                }
                if (data.graphic == null)
                {
                    data.graphic = GetOrCreateEndingSprite(s.asset, s.artBackground, s.artMotif, s.motif);
                    changed |= data.graphic != null;
                }
                if (changed) EditorUtility.SetDirty(data);
                datas.Add(data);
            }

            catalog = LoadOrCreate<EndingCatalog>(EndingCatalogPath, null);
            if (catalog == null)
            {
                Debug.LogError("[TWR] Could not create " + EndingCatalogPath);
                return false;
            }
            // Keep anything already listed (e.g. a secret 5th ending); add the launch four if missing.
            var list = new List<EndingData>();
            if (catalog.endings != null)
                foreach (EndingData e in catalog.endings)
                    if (e != null && !list.Contains(e)) list.Add(e);
            bool catalogChanged = catalog.endings == null || list.Count != catalog.endings.Length;
            foreach (EndingData e in datas)
                if (!list.Contains(e)) { list.Add(e); catalogChanged = true; }
            if (catalogChanged)
            {
                catalog.endings = list.ToArray();
                EditorUtility.SetDirty(catalog);
                Debug.Log($"[TWR] Ending catalog: {string.Join(", ", list.ConvertAll(e => e.id))} (D-203).", catalog);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        static EndingData LoadEndingData(string assetName)
        {
            return AssetDatabase.LoadAssetAtPath<EndingData>(EndingsDataFolder + "/" + assetName + ".asset");
        }

        /// RunConfig: ending catalog + main menu scene (D-202, D-203, D-210).
        static bool ConfigureEndingAssets()
        {
            if (!PrepareEndingAssets(out EndingCatalog catalog)) return false;
            if (!PrepareRunAssets(out RunConfig config)) return false;
            bool changed = false;
            if (config.endingCatalog == null) { config.endingCatalog = catalog; changed = true; }
            if (config.mainMenuSceneName != MainMenuSceneName) { config.mainMenuSceneName = MainMenuSceneName; changed = true; }
            if (changed) EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TWR] RunConfig: ending catalog '{AssetDatabase.GetAssetPath(config.endingCatalog)}', main menu scene '{config.mainMenuSceneName}' (D-203, D-210).", config);
            return true;
        }

        /// A 320 x 180 placeholder PNG imported as a Sprite (replace the file to change the end-screen art, D-200).
        static Sprite GetOrCreateEndingSprite(string fileName, Color background, Color motif, int motifKind)
        {
            string path = EndingArtFolder + "/" + fileName + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            const int w = 320, h = 180;
            var pixels = new Color32[w * h];
            Color32 bg = background, fg = motif;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = InMotif(motifKind, x, y, w, h) ? fg : bg;

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) Debug.Log("[TWR] Created placeholder ending graphic " + path);
            else Debug.LogWarning("[TWR] Could not import " + path + " as a Sprite; the end screen shows a grey box instead.");
            return sprite;
        }

        static bool InMotif(int kind, int x, int y, int w, int h)
        {
            int cx = w / 2;
            switch (kind)
            {
                case 0: // a black door in the middle (D-195: the door is still behind him)
                    return x > cx - 24 && x < cx + 24 && y > 28 && y < 130;
                case 1: // one red button (D-196)
                {
                    int dx = x - cx, dy = y - h / 2;
                    return dx * dx + dy * dy < 18 * 18;
                }
                case 2: // a floor plan: room outlines (D-197)
                {
                    bool outer = (x > 40 && x < 280 && y > 25 && y < 155) && (x < 44 || x > 276 || y < 29 || y > 151);
                    bool walls = (x > 128 && x < 132 && y > 25 && y < 110) || (y > 88 && y < 92 && x > 40 && x < 200) ||
                                 (x > 208 && x < 212 && y > 60 && y < 155);
                    return outer || walls;
                }
                default: // a row of figures (D-198)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        int fx = 60 + i * 50;
                        int dx = x - fx;
                        if (dx * dx < 12 * 12 && y > 30 && y < 120) return true;
                        int hy = y - 132;
                        if (dx * dx + hy * hy < 11 * 11) return true;
                    }
                    return false;
                }
            }
        }

        // ---------- ending scenes (D-195 to D-199, D-207) ----------

        struct EndingSceneAssets
        {
            public RunSceneAssets run;
            public EndingData ending;
            public Material carpet, concrete, basementFloor, tie, badge, planLine;
        }

        static bool PrepareEndingScene(string asset, out EndingSceneAssets e)
        {
            e = default;
            if (!PrepareRunSceneAssets(out e.run, false)) return false;
            if (!PrepareEndingAssets(out _)) return false;
            e.ending = LoadEndingData(asset);
            if (e.ending == null)
            {
                Debug.LogError("[TWR] EndingData " + asset + " is missing.");
                return false;
            }
            e.carpet = GetOrCreateMaterial("M_OfficeCarpet", new Color(0.42f, 0.45f, 0.5f), Color.black);
            e.concrete = GetOrCreateMaterial("M_BasementConcrete", new Color(0.36f, 0.36f, 0.37f), Color.black);
            e.basementFloor = GetOrCreateMaterial("M_BasementFloor", new Color(0.22f, 0.22f, 0.23f), Color.black);
            e.tie = GetOrCreateMaterial("M_CoworkerTie", new Color(0.45f, 0.05f, 0.08f), Color.black);
            e.badge = GetOrCreateMaterial("M_CoworkerBadge", new Color(0.92f, 0.95f, 1f), new Color(0.1f, 0.12f, 0.2f));
            e.planLine = GetOrCreateMaterial("M_FloorPlanLine", new Color(0.05f, 0.05f, 0.07f), Color.black);
            AssetDatabase.SaveAssets();
            return true;
        }

        /// Room x -width/2..width/2, z 0..depth, with a permanently locked door (the way in, no way back) in the south
        /// wall at the origin and the spawn 1.6 m in front of it facing +Z (D-165 pattern; D-196 "no exit").
        static RunDoor EndingRoom(Transform root, EndingSceneAssets e, float width, float depth, float height, Material floor,
                                  Material walls, Material ceiling, Material doorMat)
        {
            RunSceneAssets a = e.run;
            Transform shell = new GameObject("Room Shell").transform;
            shell.SetParent(root, false);
            float minX = -width * 0.5f, maxX = width * 0.5f;
            Block("Floor", new Vector3(0f, -0.1f, depth * 0.5f), new Vector3(width + 0.4f, 0.2f, depth + 0.4f), floor, shell);
            Block("Ceiling", new Vector3(0f, height + 0.1f, depth * 0.5f), new Vector3(width + 0.4f, 0.2f, depth + 0.4f), ceiling, shell);
            WallAlongZ(shell, "Wall W", minX - 0.1f, -0.2f, depth + 0.2f, height, walls, 0f, 0f);
            WallAlongZ(shell, "Wall E", maxX + 0.1f, -0.2f, depth + 0.2f, height, walls, 0f, 0f);
            WallAlongX(shell, "Wall N", depth + 0.1f, minX - 0.2f, maxX + 0.2f, height, walls, 0f, 0f);
            WallAlongX(shell, "Wall S (door)", 0f, minX - 0.2f, maxX + 0.2f, height, walls, RunDoorWidth, RunDoorHeight, 0f);

            RunDoor door = BuildRunDoor("Door Behind Daniel (permanently locked)", "door.ending.entry", root, Vector3.zero, Quaternion.identity,
                                        a.puzzle.door, doorMat, a.lockLight, true, true, a.interactableLayer);
            BuildSectionEntry(root, new Vector3(0f, 0.05f, 1.6f), Quaternion.identity);
            return door;
        }

        static EndingDirector AddEndingDirector(Transform root, EndingData ending, float duration, float lineAt)
        {
            var go = new GameObject("Ending Director (D-199)");
            go.transform.SetParent(root, false);
            var director = go.AddComponent<EndingDirector>();
            var so = new SerializedObject(director);
            so.FindProperty("ending").objectReferenceValue = ending;
            so.FindProperty("durationSeconds").floatValue = duration;
            so.FindProperty("voiceLineAtSeconds").floatValue = lineAt;
            so.FindProperty("tintAlpha").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return director;
        }

        /// Camera target = feet position + view (yaw, pitch in degrees; positive pitch looks down).
        static Transform CameraTarget(Transform parent, string name, Vector3 feet, float yaw, float pitch)
        {
            var t = new GameObject("Camera Target: " + name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(feet, Quaternion.Euler(pitch, yaw, 0f));
            return t;
        }

        static void SetCameraSteps(EndingDirector director, params (Transform target, float start, float seconds, float fov)[] steps)
        {
            var so = new SerializedObject(director);
            SerializedProperty list = so.FindProperty("cameraSteps");
            list.arraySize = steps.Length;
            for (int i = 0; i < steps.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("target").objectReferenceValue = steps[i].target;
                element.FindPropertyRelative("startSeconds").floatValue = steps[i].start;
                element.FindPropertyRelative("seconds").floatValue = steps[i].seconds;
                element.FindPropertyRelative("fieldOfView").floatValue = steps[i].fov;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetTint(EndingDirector director, Color colour, float alpha, float start, float seconds)
        {
            var so = new SerializedObject(director);
            so.FindProperty("tintColour").colorValue = colour;
            so.FindProperty("tintAlpha").floatValue = alpha;
            so.FindProperty("tintStartSeconds").floatValue = start;
            so.FindProperty("tintSeconds").floatValue = seconds;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// Primitive child in LOCAL space (capsules, cylinders). Not static.
        static GameObject LocalShape(PrimitiveType shape, string name, Transform parent, Vector3 localPosition, Vector3 localScale,
                                     Material material, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// An aberration prop (D-195, D-198): a skin capsule with two black eye dots, optionally in office attire (tie +
        /// ID badge). Props only: no Seeker, no spawn point, never registered in RunStats (D-204). Faces its local +Z.
        static void Aberration(Transform parent, EndingSceneAssets e, string name, Vector3 feet, float yaw, bool officeAttire, float height = 1.8f)
        {
            RunSceneAssets a = e.run;
            var root = new GameObject(name + " (prop, not a Seeker)").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            float half = height * 0.5f;
            LocalShape(PrimitiveType.Capsule, "Body", root, new Vector3(0f, half, 0f), new Vector3(0.55f, half, 0.55f), a.skin, true);
            float eyeY = height - 0.25f;
            LocalBlock("Eye L", root, new Vector3(-0.1f, eyeY, 0.25f), new Vector3(0.07f, 0.05f, 0.03f), a.eye, false, false);
            LocalBlock("Eye R", root, new Vector3(0.1f, eyeY, 0.25f), new Vector3(0.07f, 0.05f, 0.03f), a.eye, false, false);
            if (!officeAttire) return;
            LocalBlock("Tie", root, new Vector3(0f, height - 0.65f, 0.27f), new Vector3(0.08f, 0.45f, 0.03f), e.tie, false, false);
            LocalBlock("ID Badge", root, new Vector3(0.15f, height - 0.5f, 0.26f), new Vector3(0.1f, 0.13f, 0.02f), e.badge, false, false);
        }

        // Clock Out (Escape + Good, D-195): the ordinary office, slightly wrong; the black door is still behind him.
        static bool BuildEndingClockOutScene()
        {
            if (!PrepareEndingScene("Ending_ClockOut", out EndingSceneAssets e)) return false;
            RunSceneAssets a = e.run;
            Transform root = new GameObject("Ending: Clock Out (Escape + Good, D-195)").transform;
            const float width = 16f, depth = 18f, height = 3f;
            EndingRoom(root, e, width, depth, height, e.carpet, a.offWhite, a.white, a.blackDoor); // the black door (D-021)

            Transform props = new GameObject("Office (ordinary, slightly wrong)").transform;
            props.SetParent(root, false);
            for (int row = 0; row < 3; row++)
            {
                float z = 5f + row * 4f;
                Desk($"Desk W{row + 1}", new Vector3(-4f, 0f, z), props, a.offWhite, a.wood);
                Desk($"Desk E{row + 1}", new Vector3(4f, 0f, z), props, a.offWhite, a.wood);
                // Ceiling panels; the middle one of row 2 is far too bright (wrong).
                Block($"Ceiling Panel W{row + 1}", new Vector3(-4f, height - 0.02f, z), new Vector3(1.2f, 0.04f, 0.6f), a.whiteEmissive, props);
                Block($"Ceiling Panel E{row + 1}", new Vector3(4f, height - 0.02f, z), new Vector3(1.2f, 0.04f, 0.6f), row == 1 ? a.glow : a.whiteEmissive, props);
            }
            Prop("Plant", PrimitiveType.Cylinder, props, new Vector3(-7.2f, 0.5f, 16.8f), new Vector3(0.5f, 0.5f, 0.5f), a.plant, Vector3.zero, true);
            // A wall clock with no hands, and a "window" that is pure white light.
            Prop("Wall Clock (no hands)", PrimitiveType.Cylinder, props, new Vector3(0f, 2.3f, depth - 0.02f), new Vector3(0.5f, 0.02f, 0.5f), a.white, new Vector3(90f, 0f, 0f), false);
            Prop("Window (only white)", PrimitiveType.Cube, props, new Vector3(width * 0.5f - 0.02f, 1.6f, 9f), new Vector3(0.04f, 1.4f, 5f), a.glow, Vector3.zero, false);
            // The same desk once more, turned to face the wall.
            Desk("Desk Facing The Wall", new Vector3(-6.6f, 0f, 2.2f), props, a.offWhite, a.wood);

            // Coworkers: aberrations in office attire, all turned towards Daniel (D-195). Props, not Seekers.
            Transform coworkers = new GameObject("Coworkers (props, D-195)").transform;
            coworkers.SetParent(root, false);
            Aberration(coworkers, e, "Coworker 1", new Vector3(-4f, 0f, 6.4f), 160f, true);
            Aberration(coworkers, e, "Coworker 2", new Vector3(4f, 0f, 6.4f), 200f, true);
            Aberration(coworkers, e, "Coworker 3", new Vector3(-4f, 0f, 10.4f), 170f, true);
            Aberration(coworkers, e, "Coworker 4", new Vector3(4.2f, 0f, 14.4f), 190f, true, 2.05f);
            Aberration(coworkers, e, "Coworker 5", new Vector3(-2.2f, 0f, 16.5f), 180f, true);

            Transform cams = new GameObject("Camera Targets").transform;
            cams.SetParent(root, false);
            EndingDirector director = AddEndingDirector(root, e.ending, 16f, 10.5f);
            SetCameraSteps(director,
                (CameraTarget(cams, "walk in", new Vector3(0f, 0.05f, 8f), 0f, 0f), 1f, 6f, 0f),
                (CameraTarget(cams, "turn back to the black door", new Vector3(0f, 0.05f, 8f), 180f, 0f), 8f, 2.5f, 0f));
            return true;
        }

        // Overtime (Escape + Poor, D-196): a big empty white room, no exit, one button. Interactive.
        static bool BuildEndingOvertimeScene()
        {
            if (!PrepareEndingScene("Ending_Overtime", out EndingSceneAssets e)) return false;
            RunSceneAssets a = e.run;
            Transform root = new GameObject("Ending: Overtime (Escape + Poor, D-196)").transform;
            const float depth = 60f;
            RunDoor door = EndingRoom(root, e, 40f, depth, 14f, a.white, a.whiteEmissive, a.glow, a.door);

            // The EXIT sign over the door he came through (D-150): there is no other way out.
            Transform frame = door.door.transform; // local +Z = into the room
            LocalBlock("EXIT Sign Box", frame, new Vector3(0f, 3.3f, 0.16f), new Vector3(0.9f, 0.32f, 0.1f), a.exitSign, false, false);
            WorldText("EXIT Sign Text", frame, new Vector3(0f, 3.3f, 0.215f), new Vector2(0.8f, 0.26f),
                      new LocalizedString("UI", "sign.exit"), new Color(0.3f, 1f, 0.4f));

            EndingDirector director = AddEndingDirector(root, e.ending, 10f, 0f); // interactive: no timeline

            Transform button = new GameObject("The Button (D-196)").transform;
            button.SetParent(root, false);
            Block("Pedestal", new Vector3(0f, 0.5f, 40f), new Vector3(0.7f, 1f, 0.7f), a.offWhite, button);
            GameObject cap = LocalBlock("Button Cap (IInteractable, not a puzzle)", button, new Vector3(0f, 1.04f, 40f),
                                        new Vector3(0.3f, 0.08f, 0.3f), a.lockLight, false, true);
            cap.layer = a.interactableLayer;
            var overtime = cap.AddComponent<OvertimeButton>();
            Assign(overtime, "director", director);
            return true;
        }

        // Floor Plan (Discover + Good, D-197): in the basement Daniel finds what the White Room is and stays.
        static bool BuildEndingFloorPlanScene()
        {
            if (!PrepareEndingScene("Ending_FloorPlan", out EndingSceneAssets e)) return false;
            RunSceneAssets a = e.run;
            Transform root = new GameObject("Ending: Floor Plan (Discover + Good, D-197)").transform;
            const float height = 2.8f;
            EndingRoom(root, e, 14f, 14f, height, e.basementFloor, e.concrete, e.concrete, a.blackDoor); // the BASEMENT door

            Transform props = new GameObject("Basement").transform;
            props.SetParent(root, false);
            for (int i = 0; i < 3; i++)
                Block($"Strip Light {i + 1}", new Vector3(-4f + i * 4f, height - 0.03f, 7f), new Vector3(0.15f, 0.05f, 10f), a.whiteEmissive, props);
            Prop("Pipe A", PrimitiveType.Cylinder, props, new Vector3(-6.4f, height - 0.3f, 7f), new Vector3(0.18f, 7f, 0.18f), e.concrete, new Vector3(90f, 0f, 0f), false);
            Prop("Pipe B", PrimitiveType.Cylinder, props, new Vector3(-6.0f, height - 0.3f, 7f), new Vector3(0.12f, 7f, 0.12f), e.concrete, new Vector3(90f, 0f, 0f), false);

            // The table with the floor plan of the White Room (kept mysterious, D-197).
            Transform table = new GameObject("Table With The Floor Plan").transform;
            table.SetParent(props, false);
            Block("Table Top", new Vector3(0f, 0.82f, 8f), new Vector3(3f, 0.06f, 2f), a.wood, table);
            Block("Table Leg", new Vector3(0f, 0.4f, 8f), new Vector3(0.3f, 0.8f, 0.3f), e.concrete, table);
            Block("Plan Sheet", new Vector3(0f, 0.856f, 8f), new Vector3(2.8f, 0.01f, 1.8f), a.signPlate, table);
            (Vector3 c, Vector3 s)[] lines =
            {
                (new Vector3(0f, 0.863f, 7.15f), new Vector3(2.5f, 0.004f, 0.02f)), (new Vector3(0f, 0.863f, 8.85f), new Vector3(2.5f, 0.004f, 0.02f)),
                (new Vector3(-1.25f, 0.863f, 8f), new Vector3(0.02f, 0.004f, 1.7f)), (new Vector3(1.25f, 0.863f, 8f), new Vector3(0.02f, 0.004f, 1.7f)),
                (new Vector3(-0.4f, 0.863f, 7.6f), new Vector3(0.02f, 0.004f, 0.9f)), (new Vector3(0.3f, 0.863f, 8.3f), new Vector3(1.9f, 0.004f, 0.02f)),
                (new Vector3(0.7f, 0.863f, 7.5f), new Vector3(0.02f, 0.004f, 0.7f)),
            };
            for (int i = 0; i < lines.Length; i++) LocalBlock($"Plan Line {i + 1}", table, lines[i].c, lines[i].s, e.planLine, false, false);
            LocalBlock("You Are Here", table, new Vector3(-0.85f, 0.866f, 7.45f), new Vector3(0.05f, 0.01f, 0.05f), a.glow, false, false);
            Prop("Chair (pulled out, waiting)", PrimitiveType.Cube, props, new Vector3(0f, 0.45f, 6.4f), new Vector3(0.5f, 0.08f, 0.5f), a.offWhite, Vector3.zero, false);
            Prop("Chair Back", PrimitiveType.Cube, props, new Vector3(0f, 0.8f, 6.15f), new Vector3(0.5f, 0.6f, 0.06f), a.offWhite, Vector3.zero, false);

            Transform cams = new GameObject("Camera Targets").transform;
            cams.SetParent(root, false);
            EndingDirector director = AddEndingDirector(root, e.ending, 18f, 11.5f);
            SetCameraSteps(director,
                (CameraTarget(cams, "walk to the table", new Vector3(0f, 0.05f, 6.4f), 0f, 10f), 1f, 6f, 0f),
                (CameraTarget(cams, "sit down and look at the plan", new Vector3(0f, -0.45f, 6.5f), 0f, 55f), 8f, 3f, 0f));
            return true;
        }

        // Absorbed (Discover + Poor, D-198): too late; Daniel takes his place among the aberrations.
        static bool BuildEndingAbsorbedScene()
        {
            if (!PrepareEndingScene("Ending_Absorbed", out EndingSceneAssets e)) return false;
            RunSceneAssets a = e.run;
            Transform root = new GameObject("Ending: Absorbed (Discover + Poor, D-198)").transform;
            const float depth = 12f, height = 3f;
            EndingRoom(root, e, 14f, depth, height, e.basementFloor, e.concrete, e.concrete, a.blackDoor);

            Transform props = new GameObject("Basement (the truth, too late)").transform;
            props.SetParent(root, false);
            Block("Strip Light", new Vector3(0f, height - 0.03f, 6f), new Vector3(0.15f, 0.05f, 8f), a.whiteEmissive, props);
            // The floor plan again, huge, on the back wall above the line of figures.
            Block("Wall Plan", new Vector3(0f, 2.3f, depth - 0.02f), new Vector3(8f, 1f, 0.02f), a.signPlate, props);
            Block("Wall Plan Line A", new Vector3(-1f, 2.3f, depth - 0.04f), new Vector3(0.03f, 0.9f, 0.01f), e.planLine, props);
            Block("Wall Plan Line B", new Vector3(1.5f, 2.1f, depth - 0.04f), new Vector3(3f, 0.03f, 0.01f), e.planLine, props);

            // The line of aberrations along the back wall, facing the room; one gap in the middle is his (D-198).
            Transform line = new GameObject("Aberrations (props, D-198)").transform;
            line.SetParent(root, false);
            float[] xs = { -5f, -3.4f, -1.4f, 1.4f, 3.4f, 5f };
            for (int i = 0; i < xs.Length; i++)
                Aberration(line, e, $"Aberration {i + 1}", new Vector3(xs[i], 0f, depth - 1.2f), 180f, false, 1.8f + (i % 2) * 0.25f);

            Transform cams = new GameObject("Camera Targets").transform;
            cams.SetParent(root, false);
            EndingDirector director = AddEndingDirector(root, e.ending, 16f, 10f);
            SetCameraSteps(director,
                (CameraTarget(cams, "walk into the gap", new Vector3(0f, 0.05f, depth - 1.2f), 0f, 0f), 1f, 6f, 0f),
                (CameraTarget(cams, "turn around: one of them", new Vector3(0f, 0.05f, depth - 1.2f), 180f, 5f), 8f, 2.5f, 50f));
            SetTint(director, new Color(0.93f, 0.86f, 0.78f), 0.55f, 9f, 4f); // the view turns to skin (placeholder)
            return true;
        }

        // ---------- main menu (D-202, D-209, D-210) ----------

        static bool BuildMainMenuScene()
        {
            if (!PrepareRunAssets(out RunConfig config)) return false;
            // D-229: credits regenerated from docs/audio/LICENSES.md (unverified rows are logged; they only block player builds)
            if (!BuildCreditsData(out CreditsData credits, out _)) return false;

            var cameraGO = new GameObject("Menu Camera");
            cameraGO.tag = "MainCamera";
            var cam = cameraGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.96f, 0.96f, 0.95f);
            cameraGO.AddComponent<AudioListener>();
            BuildEventSystem();

            GameObject canvasGO = NewUiCanvas("Main Menu (D-202)", 0);
            Transform c = canvasGO.transform;
            UiText("Title (localized)", c, new Vector2(0f, 300f), new Vector2(1400f, 140f), 96f, new LocalizedString("UI", "menu.title"),
                   new Color(0.1f, 0.1f, 0.1f), TextAlignmentOptions.Center);

            GameObject main = StretchedPanel("Main Panel", c);
            Button newRun = UiButton("New Run Button", main.transform, new Vector2(0f, 40f), new Vector2(420f, 86f), new LocalizedString("UI", "menu.new_run"));
            Button settings = UiButton("Settings Button", main.transform, new Vector2(0f, -70f), new Vector2(420f, 86f), new LocalizedString("UI", "menu.settings"));
            Button creditsButton = UiButton("Credits Button", main.transform, new Vector2(0f, -180f), new Vector2(420f, 86f), new LocalizedString("UI", "menu.credits"));
            Button quit = UiButton("Quit Button", main.transform, new Vector2(0f, -290f), new Vector2(420f, 86f), new LocalizedString("UI", "menu.quit"));

            // A1 (D-225): the Settings screen now holds the Sound settings panel (five sliders, saved when leaving).
            GameObject settingsPanel = StretchedPanel("Settings Panel (Sound settings, D-090, D-225)", c);
            UiText("Settings Title", settingsPanel.transform, new Vector2(0f, 200f), new Vector2(1000f, 80f), 56f,
                   new LocalizedString("UI", "menu.settings.title"), new Color(0.1f, 0.1f, 0.1f), TextAlignmentOptions.Center);
            BuildAudioSettingsPanel(settingsPanel, 120f);
            Button back = UiButton("Back Button", settingsPanel.transform, new Vector2(0f, -370f), new Vector2(420f, 86f),
                                   new LocalizedString("UI", "menu.back"), AudioIds.UiBack);
            settingsPanel.SetActive(false);

            GameObject creditsPanel = BuildCreditsPanel(c, credits, out Button creditsBack); // D-229

            var menu = canvasGO.AddComponent<MainMenuController>();
            Assign(menu, "config", config);
            Assign(menu, "mainPanel", main);
            Assign(menu, "settingsPanel", settingsPanel);
            Assign(menu, "creditsPanel", creditsPanel);
            Assign(menu, "newRunButton", newRun);
            Assign(menu, "settingsButton", settings);
            Assign(menu, "creditsButton", creditsButton);
            Assign(menu, "quitButton", quit);
            Assign(menu, "backButton", back);
            Assign(menu, "creditsBackButton", creditsBack);
            return true;
        }

        // ---------- Core UI: end-of-run screen + subtitles (called from BuildCoreScene) ----------

        /// The end-of-run screen canvas (D-200), above the white fader (500) so it shows on the white/black screen.
        static EndOfRunScreen BuildEndOfRunScreen()
        {
            GameObject canvasGO = NewUiCanvas("End Of Run Screen (D-200)", 600);
            var group = canvasGO.AddComponent<CanvasGroup>();
            Transform c = canvasGO.transform;

            RectTransform background = UiRect("Background", c, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = Vector2.zero;
            background.offsetMax = Vector2.zero;
            background.gameObject.AddComponent<Image>().color = new Color(0.97f, 0.97f, 0.96f, 1f); // blocks clicks to the game

            Color dark = new Color(0.1f, 0.1f, 0.1f), grey = new Color(0.4f, 0.4f, 0.4f);
            TMP_Text title = UiText("Ending Title", c, new Vector2(0f, 410f), new Vector2(1600f, 110f), 80f, null, dark, TextAlignmentOptions.Center);

            RectTransform graphicRect = UiRect("Ending Graphic (replaceable, D-092)", c, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                               new Vector2(-430f, 110f), new Vector2(640f, 360f));
            var graphic = graphicRect.gameObject.AddComponent<Image>();
            graphic.preserveAspect = true;
            graphic.raycastTarget = false;

            TMP_Text path = UiText("Path", c, new Vector2(390f, 250f), new Vector2(760f, 50f), 36f, null, dark, TextAlignmentOptions.Left);
            TMP_Text score = UiText("Score + Good/Poor", c, new Vector2(390f, 190f), new Vector2(760f, 50f), 36f, null, dark, TextAlignmentOptions.Left);
            TMP_Text kills = UiText("Kills Breakdown", c, new Vector2(390f, 130f), new Vector2(760f, 50f), 30f, null, dark, TextAlignmentOptions.Left);
            TMP_Text puzzles = UiText("Optional Puzzles Breakdown", c, new Vector2(390f, 80f), new Vector2(760f, 50f), 30f, null, dark, TextAlignmentOptions.Left);
            TMP_Text time = UiText("Run Time", c, new Vector2(390f, 20f), new Vector2(760f, 50f), 30f, null, dark, TextAlignmentOptions.Left);
            TMP_Text seed = UiText("Seed (small, D-153)", c, new Vector2(390f, -30f), new Vector2(760f, 40f), 22f, null, grey, TextAlignmentOptions.Left);
            TMP_Text found = UiText("Endings Found", c, new Vector2(0f, -190f), new Vector2(1200f, 56f), 38f, null, dark, TextAlignmentOptions.Center);

            RectTransform row = UiRect("Ending Slots", c, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -300f), new Vector2(1500f, 150f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 36f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            RectTransform slotRect = UiRect("Slot", row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(192f, 108f));
            var slot = slotRect.gameObject.AddComponent<Image>();
            slot.preserveAspect = true;
            slot.raycastTarget = false;
            UiText("Slot Label", slotRect, new Vector2(0f, -76f), new Vector2(260f, 34f), 22f, null, dark, TextAlignmentOptions.Center);

            Button newRun = UiButton("New Run Button", c, new Vector2(-220f, -450f), new Vector2(360f, 84f), new LocalizedString("UI", "endscreen.new_run"));
            Button mainMenu = UiButton("Main Menu Button", c, new Vector2(220f, -450f), new Vector2(360f, 84f), new LocalizedString("UI", "endscreen.main_menu"));

            var screen = canvasGO.AddComponent<EndOfRunScreen>();
            Assign(screen, "canvas", canvasGO.GetComponent<Canvas>());
            Assign(screen, "group", group);
            Assign(screen, "titleLabel", title);
            Assign(screen, "graphic", graphic);
            Assign(screen, "pathLabel", path);
            Assign(screen, "scoreLabel", score);
            Assign(screen, "killsLabel", kills);
            Assign(screen, "puzzlesLabel", puzzles);
            Assign(screen, "timeLabel", time);
            Assign(screen, "seedLabel", seed);
            Assign(screen, "endingsFoundLabel", found);
            Assign(screen, "slotRow", row);
            Assign(screen, "slotTemplate", slot);
            Assign(screen, "newRunButton", newRun);
            Assign(screen, "mainMenuButton", mainMenu);
            canvasGO.GetComponent<Canvas>().enabled = false; // hidden until a run ends
            return screen;
        }

        /// Bottom-centre subtitle line (D-089), above the white fader so it stays readable during an ending's fade.
        static SubtitleUI BuildSubtitleUi()
        {
            var canvasGO = new GameObject("Subtitles (D-089)");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 550;
            AddScaler(canvasGO);
            var group = canvasGO.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            RectTransform panel = UiRect("Panel", canvasGO.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1500f, 110f));
            var image = panel.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.55f);
            image.raycastTarget = false;
            TMP_Text label = UiText("Line (localized, table Subtitles)", panel, Vector2.zero, new Vector2(1440f, 100f), 34f, null,
                                    Color.white, TextAlignmentOptions.Center);
            ((RectTransform)label.transform).anchorMin = new Vector2(0.5f, 0.5f);
            ((RectTransform)label.transform).anchorMax = new Vector2(0.5f, 0.5f);

            var ui = canvasGO.AddComponent<SubtitleUI>();
            Assign(ui, "label", label);
            Assign(ui, "group", group);
            return ui;
        }

        /// CanvasGroups on the HUD parts endings hide (HP bar, ammo, gun name; D-207).
        static Object[] GameplayHudGroups(GameObject hud)
        {
            var groups = new List<Object>();
            foreach (string part in new[] { "HP Bar", "Ammo", "Gun Name" })
            {
                Transform t = hud.transform.Find(part);
                if (t == null)
                {
                    Debug.LogWarning($"[TWR] HUD part '{part}' not found; it stays visible in endings.");
                    continue;
                }
                CanvasGroup g = GetOrAdd<CanvasGroup>(t.gameObject);
                g.interactable = false;
                g.blocksRaycasts = false;
                groups.Add(g);
            }
            return groups.ToArray();
        }

        // ---------- small UI helpers ----------

        static GameObject NewUiCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            AddScaler(go);
            go.AddComponent<GraphicRaycaster>(); // buttons need it
            return go;
        }

        static void AddScaler(GameObject go)
        {
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        static GameObject StretchedPanel(string name, Transform parent)
        {
            RectTransform rect = UiRect(name, parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect.gameObject;
        }

        /// TMP UI label. text != null = a fixed localized label (LocalizedWorldText); null = filled by code at runtime.
        static TMP_Text UiText(string name, Transform parent, Vector2 position, Vector2 size, float fontSize, LocalizedString text,
                               Color colour, TextAlignmentOptions alignment)
        {
            RectTransform rect = UiRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = colour;
            label.raycastTarget = false;
            label.text = ProjectSetup.PreviewText(text); // editor preview (English from the table); LocalizedWorldText sets it at runtime
            if (text != null)
            {
                var localized = rect.gameObject.AddComponent<LocalizedWorldText>();
                localized.Text = text;
                EditorUtility.SetDirty(localized);
            }
            return label;
        }

        /// Menu / end-screen button with a localized label and a click sound (UIState, A1 D-219; soundId from AudioIds).
        static Button UiButton(string name, Transform parent, Vector2 position, Vector2 size, LocalizedString text,
                               string soundId = AudioIds.UiClick)
        {
            RectTransform rect = UiRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.14f, 0.14f, 0.14f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            TMP_Text label = UiText("Label (localized)", rect, Vector2.zero, size, 34f, text, Color.white, TextAlignmentOptions.Center);
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            AddButtonSound(button, soundId);
            return button;
        }
    }
}
