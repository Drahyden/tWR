using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using TWR.Audio;
using TWR.Portals;
using TWR.Run;

namespace TWR.EditorTools
{
    /// <summary>
    /// [TWR] audio validation (D-227), menu "TWR/Audio/Validate Audio (A1)", also run by "TWR/Run/Validate Run Setup",
    /// "TWR/Run/Build Run Scenes" and "TWR/Audio/Build Audio (A1)". It READS the developer-made TWR_Mixer (never edits it)
    /// and checks:
    /// - mixer: Update Mode = Unscaled Time; the five user groups each with their STATE child group; the five exposed
    ///   volume parameters (at 0 dB); the four snapshots; a Duck Volume on MusicState and AmbienceState fed by Sends on
    ///   VoiceState (D-219, D-220, D-222). Every problem says what to add by hand.
    /// - Resources/AudioRoutingSettings and Resources/AudioLibrary are filled (every AudioIds ID; required ones have clips).
    /// - every AudioSource in every enabled Build Settings scene and every prefab under Assets/_Project has a mixer group
    ///   (a STATE group), and no 3D source reaches 100 m or more (D-144).
    /// - each run content / ending scene has reverb zones, and portal scenes use one preset on both sides (D-224).
    /// - office one-shots are off in silent rooms and boss arenas (D-223); no runtime script uses the static
    ///   play-at-point helper of AudioSource; every audio file has a row in docs/audio/LICENSES.md (D-218).
    /// Opens scenes additively and closes them again.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const float MaxAudioDistance = 100f;

        [MenuItem("TWR/Audio/Validate Audio (A1)")]
        public static void ValidateAudioMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (ValidateAudio()) Debug.Log("[TWR] Audio setup is valid (D-219 to D-227).");
            else Debug.LogError("[TWR] Audio setup has problems (see the [TWR] Audio report above).");
        }

        static bool ValidateAudio()
        {
            var report = new StringBuilder();
            report.AppendLine("[TWR] Audio A1 (D-219 to D-227)");
            bool ok = ValidateMixer(report, out AudioMixer mixer);
            ok &= ValidateRoutingAsset(report, mixer);
            ok &= ValidateAudioLibraryAsset(report);
            ok &= ValidateOfficeAmbienceFlags(report);
            ok &= ValidateAudioInBuildScenes(report);
            ok &= ValidateAudioInPrefabs(report);
            ok &= ValidateNoPlayAtPoint(report);
            ok &= ValidateLicenseLog(report);

            if (ok) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
            return ok;
        }

        // ---------- mixer (read only) ----------

        static bool ValidateMixer(StringBuilder report, out AudioMixer mixer)
        {
            mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (!Check(report, "TWR_Mixer at " + MixerPath, mixer != null,
                       mixer != null ? "" : "MISSING: create it (Create > Audio Mixer) with groups Music, Ambience, SFX, Voice, UI"))
                return false;

            bool ok = Check(report, "Update Mode = Unscaled Time (fades work while paused)", mixer.updateMode == AudioMixerUpdateMode.UnscaledTime,
                            mixer.updateMode == AudioMixerUpdateMode.UnscaledTime ? "" :
                            $"is {mixer.updateMode}: select TWR_Mixer in the Project window, Inspector > Update Mode = Unscaled Time");

            HashSet<string> exposedNames = ExposedParameterNames(mixer);
            for (int i = 0; i < AudioRouting.BusCount; i++)
            {
                var bus = (AudioBus)i;
                string userName = AudioRouting.UserGroupName(bus), stateName = AudioRouting.StateGroupName(bus);
                AudioMixerGroup user = AudioRouting.FindGroup(mixer, userName);
                AudioMixerGroup state = AudioRouting.FindGroup(mixer, stateName);
                string detail;
                bool groupOk;
                if (user == null) { groupOk = false; detail = $"no group '{userName}' under Master"; }
                else if (state == null) { groupOk = false; detail = $"MISSING: right-click '{userName}' in Groups > Add child group, rename it '{stateName}'"; }
                else if (!IsChildGroup(user, state)) { groupOk = false; detail = $"'{stateName}' must be a child of '{userName}' (drag it onto '{userName}')"; }
                else { groupOk = true; detail = ""; }
                ok &= Check(report, $"group {userName} > {stateName} (D-219)", groupOk, detail);

                string parameter = AudioRouting.VolumeParameter(bus);
                bool hasValue = mixer.GetFloat(parameter, out float db);
                bool exposed = exposedNames != null ? exposedNames.Contains(parameter) : hasValue;
                ok &= Check(report, $"exposed parameter '{parameter}' (Volume of '{userName}', D-225)", exposed,
                            exposed ? (hasValue ? $"{db:0.#} dB" : "") : $"MISSING: select '{userName}', right-click its Volume in the Inspector > " +
                                      $"Expose ... to script, then rename it '{parameter}' in the mixer's Exposed Parameters list");
                if (exposed && hasValue && Mathf.Abs(db) > 0.1f)
                    report.AppendLine($"    note: '{userName}' fader is {db:0.#} dB; leave the five user faders at 0 dB (the sliders set them at runtime).");
            }

            for (int i = 0; i < AudioRouting.SnapshotCount; i++)
            {
                string name = AudioRouting.SnapshotName((AudioSnapshotKind)i);
                AudioMixerSnapshot snapshot = mixer.FindSnapshot(name);
                ok &= Check(report, $"snapshot '{name}' (D-220)", snapshot != null, snapshot != null ? "" : "MISSING: Snapshots '+' in the Audio Mixer window");
                if (snapshot != null && i != (int)AudioSnapshotKind.Default && SnapshotLooksEmpty(snapshot))
                    report.AppendLine($"    note: snapshot '{name}' has no stored values yet; set its state-group faders (see the A1 notes).");
            }

            ok &= ValidateDucking(report, mixer);
            return ok;
        }

        /// The mixer's exposed parameter names, read (not edited) through SerializedObject. Null if it can't be read.
        static HashSet<string> ExposedParameterNames(AudioMixer mixer)
        {
            SerializedProperty list = new SerializedObject(mixer).FindProperty("m_ExposedParameters");
            if (list == null || !list.isArray) return null;
            var names = new HashSet<string>();
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty name = list.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (name != null) names.Add(name.stringValue);
            }
            return names.Count == 0 && list.arraySize > 0 ? null : names; // unreadable layout: fall back to GetFloat
        }

        /// Reads the editor-side group (AudioMixerGroupController) through SerializedObject. True if it can't tell.
        static bool IsChildGroup(AudioMixerGroup parent, AudioMixerGroup child)
        {
            SerializedProperty children = new SerializedObject(parent).FindProperty("m_Children");
            if (children == null || !children.isArray) return true;
            for (int i = 0; i < children.arraySize; i++)
            {
                Object element = children.GetArrayElementAtIndex(i).objectReferenceValue;
                if (element == child || (element != null && element.name == child.name)) return true;
            }
            return false;
        }

        static bool SnapshotLooksEmpty(AudioMixerSnapshot snapshot)
        {
            SerializedProperty values = new SerializedObject(snapshot).FindProperty("m_FloatValues");
            return values != null && values.isArray && values.arraySize == 0;
        }

        /// D-222: Duck Volume on MusicState + AmbienceState, each fed by a Send on VoiceState.
        static bool ValidateDucking(StringBuilder report, AudioMixer mixer)
        {
            AudioMixerGroup music = AudioRouting.FindGroup(mixer, AudioRouting.StateGroupName(AudioBus.Music));
            AudioMixerGroup ambience = AudioRouting.FindGroup(mixer, AudioRouting.StateGroupName(AudioBus.Ambience));
            AudioMixerGroup voice = AudioRouting.FindGroup(mixer, AudioRouting.StateGroupName(AudioBus.Voice));
            if (music == null || ambience == null || voice == null)
            {
                report.AppendLine("  voice ducking (D-222): not checked until MusicState, AmbienceState and VoiceState exist");
                return false;
            }

            if (!TryGetEffects(music, out List<Object> musicEffects) || !TryGetEffects(ambience, out List<Object> ambienceEffects) ||
                !TryGetEffects(voice, out List<Object> voiceEffects))
            {
                report.AppendLine("  voice ducking (D-222): can't be read in this Unity version; check by hand (Duck Volume + Sends)");
                return true;
            }

            Object musicDuck = musicEffects.FirstOrDefault(e => EffectName(e) == "Duck Volume");
            Object ambienceDuck = ambienceEffects.FirstOrDefault(e => EffectName(e) == "Duck Volume");
            var sendTargets = new List<Object>();
            foreach (Object effect in voiceEffects)
            {
                if (EffectName(effect) != "Send") continue;
                Object target = new SerializedObject(effect).FindProperty("m_SendTarget")?.objectReferenceValue;
                if (target != null) sendTargets.Add(target);
            }

            bool ok = Check(report, "Duck Volume on MusicState (D-222)", musicDuck != null,
                            musicDuck != null ? "" : "MISSING: select MusicState > Add Effect > Duck Volume");
            ok &= Check(report, "Duck Volume on AmbienceState (D-222)", ambienceDuck != null,
                        ambienceDuck != null ? "" : "MISSING: select AmbienceState > Add Effect > Duck Volume");
            ok &= Check(report, "Send on VoiceState -> MusicState Duck Volume", musicDuck != null && sendTargets.Contains(musicDuck),
                        "select VoiceState > Add Effect > Send, Receive = 'MusicState\\Duck Volume', Send Level 0 dB");
            ok &= Check(report, "Send on VoiceState -> AmbienceState Duck Volume", ambienceDuck != null && sendTargets.Contains(ambienceDuck),
                        "select VoiceState > Add Effect > Send (a second one), Receive = 'AmbienceState\\Duck Volume', Send Level 0 dB");
            return ok;
        }

        static bool TryGetEffects(AudioMixerGroup group, out List<Object> effects)
        {
            effects = new List<Object>();
            SerializedProperty list = new SerializedObject(group).FindProperty("m_Effects");
            if (list == null || !list.isArray) return false;
            for (int i = 0; i < list.arraySize; i++)
            {
                Object effect = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (effect != null) effects.Add(effect);
            }
            return true;
        }

        static string EffectName(Object effect)
        {
            if (effect == null) return null;
            SerializedProperty name = new SerializedObject(effect).FindProperty("m_EffectName");
            return name != null ? name.stringValue : null;
        }

        // ---------- assets ----------

        static bool ValidateRoutingAsset(StringBuilder report, AudioMixer mixer)
        {
            var routing = AssetDatabase.LoadAssetAtPath<AudioRoutingSettings>(AudioRoutingPath);
            if (!Check(report, "Resources/AudioRoutingSettings", routing != null, routing != null ? "" : "MISSING: run TWR > Audio > Build Audio (A1)"))
                return false;
            bool ok = Check(report, "  routing uses TWR_Mixer", mixer != null && routing.mixer == mixer, routing.mixer != null ? routing.mixer.name : "none");
            var missing = new List<string>();
            for (int i = 0; i < AudioRouting.BusCount; i++)
            {
                AudioMixerGroup group = routing.GetStateGroup((AudioBus)i);
                if (group == null || group.name != AudioRouting.StateGroupName((AudioBus)i)) missing.Add(AudioRouting.StateGroupName((AudioBus)i));
            }
            for (int i = 0; i < AudioRouting.SnapshotCount; i++)
                if (routing.GetSnapshot((AudioSnapshotKind)i) == null) missing.Add("snapshot " + AudioRouting.SnapshotName((AudioSnapshotKind)i));
            ok &= Check(report, "  routing has all state groups + snapshots", missing.Count == 0,
                        missing.Count == 0 ? "" : "missing " + string.Join(", ", missing) + " (add them to the mixer, then run Build Audio (A1) again)");
            return ok;
        }

        static bool ValidateAudioLibraryAsset(StringBuilder report)
        {
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AudioLibraryPath);
            if (!Check(report, "Resources/AudioLibrary (D-227)", library != null, library != null ? "" : "MISSING: run TWR > Audio > Build Audio (A1)"))
                return false;
            var missingIds = new List<string>();
            var noClips = new List<string>();
            var empty = new List<string>();
            foreach (string id in AudioIds.All)
            {
                if (!library.TryGet(id, out AudioLibrary.Entry entry)) { missingIds.Add(id); continue; }
                if (entry.HasClips) continue;
                if (AudioIds.Required.Contains(id)) noClips.Add(id);
                else empty.Add(id);
            }
            bool ok = Check(report, "  every AudioIds ID is in the library", missingIds.Count == 0, string.Join(", ", missingIds));
            ok &= Check(report, "  required sounds have clips", noClips.Count == 0, string.Join(", ", noClips));
            if (empty.Count > 0) report.AppendLine("    note: no clips yet (silent until A3): " + string.Join(", ", empty));
            return ok;
        }

        static bool ValidateOfficeAmbienceFlags(StringBuilder report)
        {
            var wrong = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SectionData)))
            {
                var section = AssetDatabase.LoadAssetAtPath<SectionData>(AssetDatabase.GUIDToAssetPath(guid));
                if (section != null && section.officeAmbience && !SectionData.OfficeAmbienceAllowed(section.type)) wrong.Add(section.name);
            }
            return Check(report, "office one-shots off in silent rooms + boss arenas (D-223)", wrong.Count == 0,
                         wrong.Count == 0 ? "" : string.Join(", ", wrong) + " (untick Office Ambience)");
        }

        // ---------- scenes + prefabs ----------

        static bool ValidateAudioInBuildScenes(StringBuilder report)
        {
            bool ok = true;
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            {
                if (!entry.enabled || !File.Exists(entry.path)) continue;
                Scene scene = SceneManager.GetSceneByPath(entry.path);
                bool openedHere = false;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
                    openedHere = true;
                }
                try
                {
                    var problems = new List<string>();
                    List<AudioSource> sources = InScene<AudioSource>(scene);
                    foreach (AudioSource source in sources) CheckAudioSource(source, problems);
                    ok &= Check(report, $"scene '{scene.name}': {sources.Count} AudioSource(s) routed, 3D < {MaxAudioDistance:0} m", problems.Count == 0,
                                problems.Count == 0 ? "" : "\n      " + string.Join("\n      ", problems) +
                                                           "\n      (rebuild with TWR > Audio > Build Audio (A1), or route it by hand to its *State group)");
                    ok &= CheckReverbZones(report, scene, entry.path);
                }
                finally
                {
                    if (openedHere) EditorSceneManager.CloseScene(scene, true);
                }
            }
            return ok;
        }

        static bool ValidateAudioInPrefabs(StringBuilder report)
        {
            bool ok = true;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                AudioSource[] sources = prefab.GetComponentsInChildren<AudioSource>(true);
                if (sources.Length == 0) continue;
                var problems = new List<string>();
                foreach (AudioSource source in sources) CheckAudioSource(source, problems);
                ok &= Check(report, $"prefab '{Path.GetFileName(path)}': {sources.Length} AudioSource(s)", problems.Count == 0,
                            problems.Count == 0 ? "" : "\n      " + string.Join("\n      ", problems) + "\n      (run TWR > Audio > Build Audio (A1))");
            }
            return ok;
        }

        static void CheckAudioSource(AudioSource source, List<string> problems)
        {
            string where = HierarchyPath(source.transform);
            AudioMixerGroup group = source.outputAudioMixerGroup;
            if (group == null) problems.Add($"{where}: no mixer group (D-219)");
            else if (!group.name.EndsWith("State")) problems.Add($"{where}: routed to '{group.name}', must be a state group (e.g. SFXState, D-219)");
            if (source.spatialBlend > 0f && source.maxDistance >= MaxAudioDistance)
                problems.Add($"{where}: 3D max distance {source.maxDistance:0} m (must be < {MaxAudioDistance:0} m, D-144, D-227)");
        }

        static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (Transform current = t; current != null; current = current.parent) parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// Content and ending scenes of the run need reverb zones; a scene with portals must use one preset (D-224).
        static bool CheckReverbZones(StringBuilder report, Scene scene, string path)
        {
            if (!path.StartsWith(RunScenesFolder + "/")) return true;
            if (scene.name == CoreSceneName || scene.name == MainMenuSceneName) return true;
            List<ReverbZoneSetup> zones = InScene<ReverbZoneSetup>(scene);
            bool ok = Check(report, $"  '{scene.name}' reverb zones (D-224)", zones.Count > 0,
                            zones.Count > 0 ? string.Join(", ", zones.Select(z => z.Space.ToString())) : "none: run TWR > Run > Build Run Scenes");
            if (InScene<Portal>(scene).Count > 0 && zones.Count > 0)
            {
                bool same = zones.All(z => z.Space == zones[0].Space);
                ok &= Check(report, $"  '{scene.name}' both portal sides use the same reverb preset (D-224)", same, same ? zones[0].Space.ToString() : "mixed");
            }
            ok &= Check(report, $"  '{scene.name}' reverb zones have the ReverbPresets asset", zones.All(z => z.Presets != null), "");
            return ok;
        }

        // ---------- code + license log ----------

        /// D-227: no runtime script may use the static play-at-point helper (it makes unrouted, unpooled sources).
        static bool ValidateNoPlayAtPoint(StringBuilder report)
        {
            string token = "PlayClip" + "AtPoint"; // split so this file doesn't match itself
            var offenders = new List<string>();
            string scripts = Root + "/Scripts";
            if (Directory.Exists(scripts))
                foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
                    if (File.ReadAllText(file).Contains(token)) offenders.Add(Path.GetFileName(file));
            return Check(report, "no AudioSource play-at-point calls in scripts (D-227)", offenders.Count == 0,
                         offenders.Count == 0 ? "" : string.Join(", ", offenders) + " (use a routed, pooled AudioSource)");
        }

        /// D-218, D-227: every audio file under Assets/_Project has a row (its file name) in docs/audio/LICENSES.md.
        static bool ValidateLicenseLog(StringBuilder report)
        {
            string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/audio/LICENSES.md"));
            if (!File.Exists(logPath))
            {
                report.AppendLine($"  note: license log not found at {logPath}; not checked (D-218).");
                return true;
            }
            string log = File.ReadAllText(logPath);
            var missing = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root }))
            {
                string fileName = Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid));
                if (!log.Contains(fileName)) missing.Add(fileName);
            }
            return Check(report, "every audio file has a LICENSES.md row (D-218)", missing.Count == 0,
                         missing.Count == 0 ? "" : "add rows to docs/audio/LICENSES.md for: " + string.Join(", ", missing.Distinct()));
        }
    }
}
