using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using TWR.Audio;
using TWR.Enemies;
using TWR.Portals;
using TWR.Run;
using TWR.Scares;

namespace TWR.EditorTools
{
    /// <summary>
    /// Audio step A5, jump scares (D-236 to D-239). Part of "TWR/Run/Build Run Scenes" (menu "TWR/Audio/Build Scares (A5)"
    /// runs that same full build):
    /// - Data/Scares: ScareSettings (rules, D-238), Scare_Relief and Scare_BehindYou (D-236, stinger = sfx.stinger.violin,
    ///   swappable per scare, D-239), ScareBlackoutProfile (a dark post-exposure override, D-238). Created once; your edits
    ///   are kept, only the scare list and the figure prefab are kept filled.
    /// - Prefabs/ScareFigure: the Seeker-look prop (capsule body + black eye, no collider, no AI, Ignore Raycast layer).
    /// - Core: the ScareDirector with its 2D stinger source (SFXState, reverbZoneMix 0.3, D-237) and the blackout Volume.
    /// - Section_Puzzle: the "Relief" anchor (target = the exit door). Section_ImpossibleGeometry: the "Behind you" anchor
    ///   on a spot 3.5 m south of the hall's return portal, armed by crossing the room portal "ig.portal".
    /// - ValidateScares: run by the [TWR] audio validation (D-239).
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string ScareDataFolder = Root + "/Data/Scares";
        const string ScareSettingsPath = ScareDataFolder + "/ScareSettings.asset";
        const string ScareReliefPath = ScareDataFolder + "/Scare_Relief.asset";
        const string ScareBehindYouPath = ScareDataFolder + "/Scare_BehindYou.asset";
        const string ScareBlackoutProfilePath = ScareDataFolder + "/ScareBlackoutProfile.asset";
        const string ScareFigurePrefabPath = Root + "/Prefabs/ScareFigure.prefab";

        // Puzzle room exit door, floor centre of the raised opening on the exit ledge (D-240, D-245; consts from
        // TestSceneBuilder.LargeSections.cs, so no cross-file static order problem).
        static readonly Vector3 PuzzleExitDoorPoint = new Vector3(LpMaxX + 0.1f, LpExitSill, LpExitZ);
        // "Behind you" spot: in the hall, 1.5 m in from the return portal wall and 3.5 m south of the portal (clear of its
        // 1 m vestibule, D-142). The player arrives facing east, so the spot is behind them.
        static readonly Vector3 BehindYouSpot = new Vector3(HallPortalPosition.x + 1.5f, 0f, HallPortalPosition.z - 3.5f);
        const string ImpossibleRoomPortalId = "ig.portal";
        const float BlackoutExposure = -10f;

        static bool scareFigureBuiltThisPass;

        struct ScareAssets
        {
            public ScareSettings settings;
            public ScareData relief, behindYou;
            public VolumeProfile blackout;
            public GameObject figure;
        }

        [MenuItem("TWR/Audio/Build Scares (A5)")]
        public static void BuildScaresMenu()
        {
            Debug.Log("[TWR] Scares A5 (D-236 to D-239) are part of the run scenes: rebuilding them all ...");
            BuildRunScenesMenu();
        }

        // ---------- assets ----------

        /// Scare data, settings, blackout profile and the figure prefab (created if missing, your values kept).
        static bool PrepareScareAssets(out ScareAssets s)
        {
            s = default;
            EnsureFolder(ScareDataFolder);
            s.relief = LoadOrCreate<ScareData>(ScareReliefPath, InitReliefScare);
            s.behindYou = LoadOrCreate<ScareData>(ScareBehindYouPath, InitBehindYouScare);
            s.blackout = EnsureScareBlackoutProfile();
            s.figure = EnsureScareFigurePrefab();
            s.settings = LoadOrCreate<ScareSettings>(ScareSettingsPath, settings =>
                settings.sightBlockers = LayerMask.GetMask("Default", TWR.Core.GameLayers.Interactable)); // walls + doors (like D-131)
            if (s.settings == null || s.relief == null || s.behindYou == null || s.blackout == null || s.figure == null)
            {
                Debug.LogError("[TWR] Could not create the scare assets in " + ScareDataFolder + " (A5).");
                return false;
            }

            bool changed = false;
            var list = new List<ScareData>();
            if (s.settings.scares != null) list.AddRange(s.settings.scares.Where(d => d != null));
            foreach (ScareData data in new[] { s.relief, s.behindYou })
                if (!list.Contains(data)) { list.Add(data); changed = true; }
            if (changed || s.settings.scares == null || list.Count != s.settings.scares.Length) s.settings.scares = list.ToArray();
            if (s.settings.figurePrefab != s.figure) { s.settings.figurePrefab = s.figure; changed = true; }
            if (changed)
            {
                EditorUtility.SetDirty(s.settings);
                AssetDatabase.SaveAssets();
            }
            return true;
        }

        /// D-236 Scare 1 "Relief": lights + hum cut, 0.6 s dark, hit as the lights return, flicker removes the figure.
        static void InitReliefScare(ScareData d)
        {
            d.id = "scare.relief";
            d.trigger = ScareTrigger.FacingExitAfterSolve;
            d.template = SectionType.Puzzle;
            d.stingerId = AudioIds.StingerViolin;
            d.stingerVolume = 1f;
            d.musicDipDb = -80f;      // D-237: music and ambience cut to silence with the lights
            d.ambienceDipDb = -80f;
            d.dipAttackSeconds = 0.05f;
            d.dipHoldSeconds = 0f;
            d.dipReleaseSeconds = 2f;
            d.preHitDarkSeconds = 0.6f;
            d.figureVisibleSeconds = 0.3f;
            d.flickerDarkSeconds = 0.12f;
            d.removeFigureWhenUnobserved = false;
            d.vanishIfCloserThan = 0f;
            d.triggerDistance = 5f;
            d.viewConeDegrees = 60f;
            d.unobservedSeconds = 0f;
            d.figureDistance = 3f;
        }

        /// D-236 Scare 2 "Behind you": stinger on sight, music -12 dB, figure removed when unobserved.
        static void InitBehindYouScare(ScareData d)
        {
            d.id = "scare.behind_you";
            d.trigger = ScareTrigger.SeenAfterUnobserved;
            d.template = SectionType.ImpossibleGeometry;
            d.stingerId = AudioIds.StingerViolin;
            d.stingerVolume = 1f;
            d.musicDipDb = -12f;      // D-237
            d.ambienceDipDb = 0f;
            d.dipAttackSeconds = 0.05f;
            d.dipHoldSeconds = 0.3f;
            d.dipReleaseSeconds = 2f;
            d.preHitDarkSeconds = 0f;
            d.figureVisibleSeconds = 0f;
            d.flickerDarkSeconds = 0f;
            d.removeFigureWhenUnobserved = true;
            d.vanishIfCloserThan = 1.5f;
            d.triggerDistance = 10f;
            d.viewConeDegrees = 60f;
            d.unobservedSeconds = 3f;
            d.figureDistance = 3f;
        }

        /// A global-volume profile that only overrides post exposure (very dark). Weight 0 until a scare needs it (D-238).
        static VolumeProfile EnsureScareBlackoutProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ScareBlackoutProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ScareBlackoutProfilePath);
                Debug.Log("[TWR] Created " + ScareBlackoutProfilePath);
            }
            if (!profile.TryGet(out ColorAdjustments adjustments))
            {
                adjustments = profile.Add<ColorAdjustments>(false);
                AddComponentToProfileAsset(adjustments, profile);
            }
            // Only post exposure is overridden; an earlier build turned on every override at neutral values.
            adjustments.contrast.overrideState = false;
            adjustments.colorFilter.overrideState = false;
            adjustments.hueShift.overrideState = false;
            adjustments.saturation.overrideState = false;
            if (!adjustments.postExposure.overrideState || adjustments.postExposure.value > -4f)
                adjustments.postExposure.Override(BlackoutExposure);
            EditorUtility.SetDirty(adjustments);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// The Seeker-look prop (D-067 skin, black eye), rebuilt once per build pass so changes here reach it.
        static GameObject EnsureScareFigurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ScareFigurePrefabPath);
            if (existing != null && scareFigureBuiltThisPass) return existing;

            EnsureFolder(Root + "/Prefabs");
            Material skin = GetOrCreateMaterial("M_SeekerSkin", new Color(0.93f, 0.86f, 0.78f), Color.black);
            Material eye = GetOrCreateMaterial("M_SeekerEye", new Color(0.01f, 0.01f, 0.01f), Color.black);
            var root = new GameObject("Scare Figure");
            try
            {
                // Same proportions as the Seeker body, a little taller and thinner: it should read as "one of them, but wrong".
                Transform body = new GameObject("Body").transform;
                body.SetParent(root.transform, false);
                SeekerPart("Torso", PrimitiveType.Capsule, body, new Vector3(0f, 1.05f, 0f), new Vector3(0.7f, 1.05f, 0.7f), skin);
                SeekerPart("Eye (black)", PrimitiveType.Sphere, body, new Vector3(0f, 1.72f, 0.29f), new Vector3(0.26f, 0.15f, 0.12f), eye);
                SetLayerRecursively(root, LayerMask.NameToLayer(TWR.Core.GameLayers.IgnoreRaycast)); // never blocks rays (no colliders anyway)
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ScareFigurePrefabPath);
                scareFigureBuiltThisPass = saved != null;
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------- scenes ----------

        /// Core: the ScareDirector + 2D stinger source (SFXState) + blackout Volume (D-237, D-238).
        static bool BuildScareDirector()
        {
            if (!PrepareScareAssets(out ScareAssets s)) return false;
            var go = new GameObject("Scare Director (jump scares, D-236 to D-239)");

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;     // 2D: the hit is in your head, not in the room
            source.reverbZoneMix = 0.3f;  // a little of the room's reverb (D-237)
            source.dopplerLevel = 0f;
            source.priority = 16;         // never stolen by other SFX
            RouteAudio(source, AudioBus.SFX); // D-237: stingers on SFXState

            var volumeGO = new GameObject("Scare Blackout Volume (post exposure, D-238)");
            volumeGO.transform.SetParent(go.transform, false);
            var volume = volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;   // above the Bloom volume and the future D-071 volume
            volume.weight = 0f;
            volume.sharedProfile = s.blackout;
            volume.enabled = false;   // only while dark

            var director = go.AddComponent<ScareDirector>();
            Assign(director, "settings", s.settings);
            Assign(director, "stingerSource", source);
            Assign(director, "blackout", volume);
            return true;
        }

        /// Section_Puzzle: "Relief" (D-236). The figure is placed at runtime between the player and the exit door.
        static bool AddReliefScare(Transform parent)
        {
            if (!PrepareScareAssets(out ScareAssets s)) return false;
            ScareAnchor anchor = BuildScareAnchor(parent, s, s.relief, "Scare: Relief (D-236)",
                                                  PuzzleExitDoorPoint + Vector3.left * 2f, Quaternion.LookRotation(Vector3.left));
            var target = new GameObject("Exit Door Point");
            target.transform.SetParent(anchor.transform, false);
            target.transform.position = PuzzleExitDoorPoint;
            Assign(anchor, "target", target.transform);
            return true;
        }

        /// Section_ImpossibleGeometry: "Behind you" (D-236). The anchor's position is the spot.
        static bool AddBehindYouScare(Transform parent)
        {
            if (!PrepareScareAssets(out ScareAssets s)) return false;
            ScareAnchor anchor = BuildScareAnchor(parent, s, s.behindYou, "Scare: Behind you (D-236, D-053a)",
                                                  BehindYouSpot, Quaternion.LookRotation(Vector3.right));
            var so = new SerializedObject(anchor);
            so.FindProperty("armOnPortalId").stringValue = ImpossibleRoomPortalId;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        static ScareAnchor BuildScareAnchor(Transform parent, ScareAssets s, ScareData data, string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var figure = (GameObject)PrefabUtility.InstantiatePrefab(s.figure, go.transform);
            figure.name = "Scare Figure (prop: no AI, no collider, D-236)";
            figure.transform.SetPositionAndRotation(position, rotation);
            figure.SetActive(false);
            var anchor = go.AddComponent<ScareAnchor>();
            Assign(anchor, "data", data);
            Assign(anchor, "figure", figure);
            return anchor;
        }

        // ---------- [TWR] validation (D-239) ----------

        /// Part of the [TWR] audio validation: data, stingers + license rows, flicker safety, the Core director and the anchors.
        static bool ValidateScares(StringBuilder report)
        {
            report.AppendLine("[TWR] Jump scares A5 (D-236 to D-239)");
            var settings = AssetDatabase.LoadAssetAtPath<ScareSettings>(ScareSettingsPath);
            if (!Check(report, "Data/Scares/ScareSettings", settings != null, settings != null ? "" : "MISSING: run TWR > Run > Build Run Scenes"))
                return false;

            List<ScareData> scares = settings.scares != null ? settings.scares.Where(d => d != null).ToList() : new List<ScareData>();
            bool ok = Check(report, "  scares listed in ScareSettings", scares.Count > 0, scares.Count + " scare(s)");
            var duplicates = scares.GroupBy(d => d.DisplayId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            ok &= Check(report, "  scare ids are unique", duplicates.Count == 0, string.Join(", ", duplicates));
            ok &= Check(report, "  figure prefab: set, no collider, no AI (D-236)", FigureIsProp(settings.figurePrefab, out string figureDetail), figureDetail);

            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AudioLibraryPath);
            var stingerFiles = new List<string>();
            float minInterval = 1f / Mathf.Max(0.01f, settings.maxFlashesPerSecond);
            foreach (ScareData d in scares)
            {
                string label = $"  '{d.DisplayId}'";
                AudioLibrary.Entry entry = null;
                bool hasClip = library != null && library.TryGet(d.stingerId, out entry) && entry.HasClips;
                ok &= Check(report, $"{label} stinger '{d.stingerId}' has a clip in the AudioLibrary (D-239)", hasClip,
                            hasClip ? "" : $"add the ID to AudioIds and drag a clip into {AudioLibraryPath}");
                if (hasClip)
                    foreach (AudioClip clip in entry.clips)
                        if (clip != null) stingerFiles.Add(Path.GetFileName(AssetDatabase.GetAssetPath(clip)));

                ok &= Check(report, $"{label} template {d.template} allows scares (not silent / fork / boss arena, D-238)",
                            ScareData.TemplateAllowed(d.template), "");
                bool flickerOk = d.FlashInterval >= minInterval - 0.0001f && d.FlickerSeconds <= settings.maxFlickerSeconds + 0.0001f;
                string interval = float.IsPositiveInfinity(d.FlashInterval) ? "single flash" : $"{d.FlashInterval:0.00} s between flashes";
                ok &= Check(report, $"{label} flicker <= {settings.maxFlashesPerSecond:0} flashes/s and <= {settings.maxFlickerSeconds:0.0} s (D-238)",
                            flickerOk, $"{interval}, {d.FlickerSeconds:0.00} s in total");
                if (d.trigger == ScareTrigger.SeenAfterUnobserved && !d.removeFigureWhenUnobserved)
                    report.AppendLine($"    warning: {label.Trim()} is 'Behind you' but its figure is removed by a flicker, not by looking away (D-236)");
            }
            List<string> licence = LicenseLogCheck.ProblemsFor(stingerFiles.Distinct(), out bool logFound);
            ok &= Check(report, "  stinger clips have a verified LICENSES.md row (D-218)", licence.Count == 0,
                        !logFound ? "log not found, not checked" : licence.Count == 0 ? string.Join(", ", stingerFiles.Distinct()) : string.Join("; ", licence));
            if (stingerFiles.Count > 0)
                report.AppendLine("    note: CC BY stingers need the Credits screen (D-229) before any build ships them.");

            ok &= ValidateScareScenes(report, settings, scares);
            return ok;
        }

        static bool FigureIsProp(GameObject prefab, out string detail)
        {
            if (prefab == null) { detail = "no figure prefab (run TWR > Run > Build Run Scenes)"; return false; }
            var problems = new List<string>();
            if (prefab.GetComponentsInChildren<Collider>(true).Length > 0) problems.Add("has colliders");
            if (prefab.GetComponentsInChildren<Seeker>(true).Length > 0 || prefab.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true).Length > 0)
                problems.Add("has AI (Seeker / NavMeshAgent)");
            if (prefab.GetComponentsInChildren<Renderer>(true).Length == 0) problems.Add("has nothing to see");
            detail = problems.Count == 0 ? prefab.name : string.Join(", ", problems);
            return problems.Count == 0;
        }

        /// Opens every run scene (additively, closes it again): exactly one ScareDirector, in Core, set up per D-237/D-238;
        /// every ScareAnchor sits in a scene whose SectionData template matches its scare (never silent / fork / boss / lobby /
        /// ending, D-238) and is complete; every scare has an anchor somewhere.
        static bool ValidateScareScenes(StringBuilder report, ScareSettings settings, List<ScareData> scares)
        {
            var typeByScene = new Dictionary<string, SectionType>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SectionData)))
            {
                var section = AssetDatabase.LoadAssetAtPath<SectionData>(AssetDatabase.GUIDToAssetPath(guid));
                if (section != null && !string.IsNullOrEmpty(section.sceneName)) typeByScene[section.sceneName] = section.type;
            }

            bool ok = true;
            int directors = 0;
            var anchored = new HashSet<ScareData>();
            if (!Directory.Exists(RunScenesFolder)) return Check(report, "  run scenes", false, RunScenesFolder + " not found");
            foreach (string file in Directory.GetFiles(RunScenesFolder, "*.unity"))
            {
                string path = file.Replace('\\', '/');
                Scene scene = SceneManager.GetSceneByPath(path);
                bool openedHere = false;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    openedHere = true;
                }
                try
                {
                    foreach (ScareDirector director in InScene<ScareDirector>(scene))
                    {
                        directors++;
                        bool inCore = scene.name == CoreSceneName;
                        string detail = inCore ? DirectorDetail(director) : "must be in Core only (D-161)";
                        ok &= Check(report, $"  ScareDirector in '{scene.name}' (D-237, D-238)", inCore && detail == "OK", inCore && detail == "OK" ? "" : detail);
                    }
                    foreach (ScareAnchor anchor in InScene<ScareAnchor>(scene))
                    {
                        var problems = new List<string>();
                        ScareData data = anchor.Data;
                        if (data == null) problems.Add("no ScareData");
                        else
                        {
                            anchored.Add(data);
                            if (!scares.Contains(data)) problems.Add($"'{data.DisplayId}' is not listed in ScareSettings");
                            if (!typeByScene.TryGetValue(scene.name, out SectionType type)) problems.Add("not a section scene (lobby, fork, endings and Core never get scares, D-238)");
                            else if (type != data.template) problems.Add($"scene is a {type} section, the scare wants {data.template}");
                            else if (!ScareData.TemplateAllowed(type)) problems.Add($"{type} sections never get scares (D-238)");
                            if (data.trigger == ScareTrigger.FacingExitAfterSolve && anchor.Target == null) problems.Add("Relief needs a Target (exit door point)");
                            if (data.trigger == ScareTrigger.SeenAfterUnobserved)
                            {
                                if (string.IsNullOrEmpty(anchor.ArmOnPortalId)) problems.Add("Behind you needs 'Arm On Portal Id'");
                                else if (!InScene<Portal>(scene).Any(p => p.PortalId == anchor.ArmOnPortalId))
                                    problems.Add($"no portal with id '{anchor.ArmOnPortalId}' in this scene");
                            }
                        }
                        if (anchor.Figure == null) problems.Add("no figure");
                        else if (anchor.Figure.GetComponentsInChildren<Collider>(true).Length > 0) problems.Add("figure has colliders");
                        ok &= Check(report, $"  anchor '{HierarchyPath(anchor.transform)}' in '{scene.name}'", problems.Count == 0, string.Join("; ", problems));
                    }
                }
                finally
                {
                    if (openedHere) EditorSceneManager.CloseScene(scene, true);
                }
            }
            ok &= Check(report, "  exactly one ScareDirector (Core)", directors == 1, directors + " found");
            var unplaced = scares.Where(d => !anchored.Contains(d)).Select(d => d.DisplayId).ToList();
            if (unplaced.Count > 0)
            {
                string text = "scare(s) with no ScareAnchor in any run scene (they can never fire): " + string.Join(", ", unplaced);
                report.AppendLine("    warning: " + text);
                Debug.LogWarning("[TWR] Scares: " + text + " (D-236).");
            }
            return ok;
        }

        static string DirectorDetail(ScareDirector director)
        {
            var so = new SerializedObject(director);
            var problems = new List<string>();
            if (so.FindProperty("settings").objectReferenceValue == null) problems.Add("no ScareSettings");

            var source = so.FindProperty("stingerSource").objectReferenceValue as AudioSource;
            if (source == null) problems.Add("no stinger AudioSource");
            else
            {
                string group = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : null;
                bool routed = group == "SFXState" || (group == "SFX" && missingStateGroups.Contains("SFXState"));
                if (!routed) problems.Add($"stinger source routed to '{group ?? "nothing"}', must be SFXState (D-237)");
                if (source.spatialBlend > 0f) problems.Add("stinger source must be 2D (D-237)");
            }

            var volume = so.FindProperty("blackout").objectReferenceValue as Volume;
            if (volume == null) problems.Add("no blackout Volume");
            else
            {
                if (!volume.isGlobal) problems.Add("blackout Volume must be global");
                if (volume.weight > 0f || volume.enabled) problems.Add("blackout Volume must start disabled at weight 0");
                bool dark = volume.sharedProfile != null && volume.sharedProfile.TryGet(out ColorAdjustments adjust) &&
                            adjust.postExposure.overrideState && adjust.postExposure.value <= -4f;
                if (!dark) problems.Add($"blackout profile needs a Color Adjustments post exposure override (about {BlackoutExposure:0}, D-238)");
            }
            return problems.Count == 0 ? "OK" : string.Join("; ", problems);
        }
    }
}
