using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// [TWR] audio validation (D-227, D-228), menu "TWR/Audio/Validate Audio (A1)", also run by "TWR/Run/Validate Run Setup",
    /// "TWR/Run/Build Run Scenes" and "TWR/Audio/Build Audio (A1)". It READS the developer-made TWR_Mixer (never edits it)
    /// and checks:
    /// - mixer: Update Mode = Unscaled Time; the five user groups each with their STATE child group; the five exposed
    ///   volume parameters (at 0 dB); the four snapshots; a Duck Volume on MusicState and AmbienceState fed by Sends on
    ///   VoiceState (D-219, D-220, D-222). Everything missing is listed once, as numbered steps to do by hand.
    /// - Resources/AudioRoutingSettings and Resources/AudioLibrary are filled: every AudioIds constant has an entry; empty
    ///   entries are errors for IDs code plays from the library, warnings for fallback / reserved IDs (D-228).
    /// - the loop check (AudioLoopCheck: silence and seam jump of loop clips) as warnings only (D-228).
    /// - every AudioSource in every enabled Build Settings scene and every prefab under Assets/_Project has a mixer group
    ///   (a STATE group; the parent group counts while that state group is missing from the mixer, which the mixer
    ///   list already reports), and no 3D source reaches 100 m or more (D-144).
    /// - each run content / ending scene has reverb zones, and portal scenes use one preset on both sides (D-224).
    /// - office one-shots are off in silent rooms and boss arenas (D-223); no runtime script uses the static
    ///   play-at-point helper of AudioSource; every audio file has a row in docs/audio/LICENSES.md (D-218).
    /// Opens scenes additively and closes them again.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const float MaxAudioDistance = 100f;

        /// State groups TWR_Mixer doesn't have (filled by ValidateAudio): sources on their parent group are not flagged.
        static readonly HashSet<string> missingStateGroups = new HashSet<string>();

        [MenuItem("TWR/Audio/Validate Audio (A1)")]
        public static void ValidateAudioMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (ValidateAudio()) Debug.Log("[TWR] Audio setup is valid (D-219 to D-228).");
            else Debug.LogError("[TWR] Audio setup has problems (see the [TWR] Audio report above).");
        }

        static bool ValidateAudio()
        {
            var report = new StringBuilder();
            report.AppendLine("[TWR] Audio A1 (D-219 to D-228)");
            bool ok = ValidateMixer(report, out AudioMixer mixer);
            missingStateGroups.Clear();
            if (mixer != null)
                for (int i = 0; i < AudioRouting.BusCount; i++)
                    if (AudioRouting.FindGroup(mixer, AudioRouting.StateGroupName((AudioBus)i)) == null)
                        missingStateGroups.Add(AudioRouting.StateGroupName((AudioBus)i));
            ok &= ValidateRoutingAsset(report, mixer);
            ok &= ValidateAudioLibraryAsset(report);
            AudioLoopCheck.AppendToReport(report); // warnings only (D-228)
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

            // Everything to add by hand, collected here and printed as ONE numbered list after the checks (D-219 to D-222).
            var todo = new List<string>();

            bool unscaled = mixer.updateMode == AudioMixerUpdateMode.UnscaledTime;
            bool ok = Check(report, "Update Mode = Unscaled Time (fades work while paused)", unscaled, unscaled ? "" : $"MISSING (is {mixer.updateMode})");
            if (!unscaled) todo.Add("Select TWR_Mixer in the Project window > Inspector > Update Mode = Unscaled Time (D-219).");

            HashSet<string> exposedNames = ExposedParameterNames(mixer);
            for (int i = 0; i < AudioRouting.BusCount; i++)
            {
                var bus = (AudioBus)i;
                string userName = AudioRouting.UserGroupName(bus), stateName = AudioRouting.StateGroupName(bus);
                AudioMixerGroup user = AudioRouting.FindGroup(mixer, userName);
                AudioMixerGroup state = AudioRouting.FindGroup(mixer, stateName);
                string detail;
                bool groupOk;
                if (user == null)
                {
                    groupOk = false;
                    detail = $"MISSING '{userName}'";
                    todo.Add($"Groups: right-click Master > Add child group, rename it '{userName}'; then right-click '{userName}' > " +
                             $"Add child group, rename it '{stateName}' (D-219).");
                }
                else if (state == null)
                {
                    groupOk = false;
                    detail = $"MISSING '{stateName}' (sources use '{userName}' until then)";
                    todo.Add($"Groups: right-click '{userName}' > Add child group, rename it '{stateName}' (D-219).");
                }
                else if (!IsChildGroup(user, state))
                {
                    groupOk = false;
                    detail = $"'{stateName}' is not a child of '{userName}'";
                    todo.Add($"Groups: drag '{stateName}' onto '{userName}' so it becomes its child (D-219).");
                }
                else { groupOk = true; detail = ""; }
                ok &= Check(report, $"group {userName} > {stateName} (D-219)", groupOk, detail);

                string parameter = AudioRouting.VolumeParameter(bus);
                bool hasValue = mixer.GetFloat(parameter, out float db);
                bool exposed = exposedNames != null ? exposedNames.Contains(parameter) : hasValue;
                ok &= Check(report, $"exposed parameter '{parameter}' (Volume of '{userName}', D-225)", exposed,
                            exposed ? (hasValue ? $"{db:0.#} dB" : "") : "MISSING");
                if (!exposed)
                    todo.Add($"Select '{userName}', right-click its Volume in the Inspector > Expose ... to script, then rename it " +
                             $"'{parameter}' in the mixer's Exposed Parameters list (D-225).");
                if (exposed && hasValue && Mathf.Abs(db) > 0.1f)
                    report.AppendLine($"    note: '{userName}' fader is {db:0.#} dB; leave the five user faders at 0 dB (the sliders set them at runtime).");
            }

            var emptySnapshots = new List<string>();
            for (int i = 0; i < AudioRouting.SnapshotCount; i++)
            {
                string name = AudioRouting.SnapshotName((AudioSnapshotKind)i);
                AudioMixerSnapshot snapshot = mixer.FindSnapshot(name);
                ok &= Check(report, $"snapshot '{name}' (D-220)", snapshot != null, snapshot != null ? "" : "MISSING");
                if (snapshot == null) todo.Add($"Audio Mixer window > Snapshots '+', name it '{name}' (D-220).");
                else if (i != (int)AudioSnapshotKind.Default && SnapshotLooksEmpty(snapshot)) emptySnapshots.Add(name);
            }
            if (emptySnapshots.Count > 0)
                report.AppendLine("    note: snapshot(s) " + string.Join(", ", emptySnapshots) + " have no stored values yet (set after the state groups exist).");

            ok &= ValidateDucking(report, mixer, todo);

            if (emptySnapshots.Count > 0)
                todo.Add("After the state groups exist: select each snapshot (" + string.Join(", ", emptySnapshots) + ") and set its " +
                         "state-group faders as in the A1 notes (D-220, D-221).");
            if (todo.Count > 0)
            {
                report.AppendLine($"  >> TWR_Mixer: {todo.Count} thing(s) to add by hand (double-click {MixerPath} to open it; the .mixer file is never edited by code):");
                for (int i = 0; i < todo.Count; i++) report.AppendLine($"     {i + 1}. {todo[i]}");
                report.AppendLine("     Then run TWR > Audio > Build Audio (A1) again so every source is routed to its State group.");
            }
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

        const string DuckMusicStep = "Select MusicState > Add Effect > Duck Volume (D-222).";
        const string DuckAmbienceStep = "Select AmbienceState > Add Effect > Duck Volume (D-222).";
        const string SendMusicStep = "Select VoiceState > Add Effect > Send, Receive = 'MusicState\\Duck Volume', Send Level 0 dB (D-222).";
        const string SendAmbienceStep = "Select VoiceState > Add Effect > Send (a second one), Receive = 'AmbienceState\\Duck Volume', Send Level 0 dB (D-222).";

        /// D-222: Duck Volume on MusicState + AmbienceState, each fed by a Send on VoiceState. Missing steps go to todo.
        static bool ValidateDucking(StringBuilder report, AudioMixer mixer, List<string> todo)
        {
            AudioMixerGroup music = AudioRouting.FindGroup(mixer, AudioRouting.StateGroupName(AudioBus.Music));
            AudioMixerGroup ambience = AudioRouting.FindGroup(mixer, AudioRouting.StateGroupName(AudioBus.Ambience));
            AudioMixerGroup voice = AudioRouting.FindGroup(mixer, AudioRouting.StateGroupName(AudioBus.Voice));
            if (music == null || ambience == null || voice == null)
            {
                report.AppendLine("  voice ducking (D-222): PROBLEM - MISSING (needs MusicState, AmbienceState and VoiceState first)");
                todo.Add("After the state groups exist: " + DuckMusicStep);
                todo.Add(DuckAmbienceStep);
                todo.Add(SendMusicStep);
                todo.Add(SendAmbienceStep);
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

            bool musicSend = musicDuck != null && sendTargets.Contains(musicDuck);
            bool ambienceSend = ambienceDuck != null && sendTargets.Contains(ambienceDuck);
            bool ok = Check(report, "Duck Volume on MusicState (D-222)", musicDuck != null, musicDuck != null ? "" : "MISSING");
            ok &= Check(report, "Duck Volume on AmbienceState (D-222)", ambienceDuck != null, ambienceDuck != null ? "" : "MISSING");
            ok &= Check(report, "Send on VoiceState -> MusicState Duck Volume", musicSend, musicSend ? "" : "MISSING");
            ok &= Check(report, "Send on VoiceState -> AmbienceState Duck Volume", ambienceSend, ambienceSend ? "" : "MISSING");
            if (musicDuck == null) todo.Add(DuckMusicStep);
            if (ambienceDuck == null) todo.Add(DuckAmbienceStep);
            if (!musicSend) todo.Add(SendMusicStep);
            if (!ambienceSend) todo.Add(SendAmbienceStep);
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
                        missing.Count == 0 ? "" : "missing " + string.Join(", ", missing) + " (see the TWR_Mixer steps above)");
            return ok;
        }

        /// D-227, D-228: every AudioIds constant is in AudioIds.All and in exactly one of Required / Fallback / Reserved,
        /// and has a library entry. Empty entries: [TWR] ERROR for IDs code plays straight from the library (Required),
        /// [TWR] WARNING for fallback-only and reserved-for-later IDs. Clips nothing plays yet (the stinger) are fine.
        static bool ValidateAudioLibraryAsset(StringBuilder report)
        {
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AudioLibraryPath);
            if (!Check(report, "Resources/AudioLibrary (D-227)", library != null, library != null ? "" : "MISSING: run TWR > Audio > Build Audio (A1)"))
                return false;

            // Every constant in AudioIds, read by reflection so a constant forgotten in All is caught too.
            var constants = new List<string>();
            foreach (FieldInfo field in typeof(AudioIds).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.IsLiteral && field.FieldType == typeof(string)) constants.Add((string)field.GetRawConstantValue());

            var notInAll = constants.Where(id => !AudioIds.All.Contains(id)).ToList();
            var unclassified = new List<string>();
            foreach (string id in constants)
            {
                int groups = (AudioIds.Required.Contains(id) ? 1 : 0) + (AudioIds.Fallback.Contains(id) ? 1 : 0) + (AudioIds.Reserved.Contains(id) ? 1 : 0);
                if (groups != 1) unclassified.Add(id);
            }
            bool ok = Check(report, "  every AudioIds constant is listed in AudioIds.All", notInAll.Count == 0,
                            notInAll.Count == 0 ? "" : string.Join(", ", notInAll) + " (add them to AudioIds.All)");
            ok &= Check(report, "  every AudioIds constant is in exactly one of Required / Fallback / Reserved", unclassified.Count == 0,
                        unclassified.Count == 0 ? "" : string.Join(", ", unclassified));

            var missingIds = new List<string>();
            var requiredEmpty = new List<string>();
            var fallbackEmpty = new List<string>();
            var reservedEmpty = new List<string>();
            foreach (string id in constants)
            {
                if (!library.TryGet(id, out AudioLibrary.Entry entry)) { missingIds.Add(id); continue; }
                if (entry.HasClips) continue;
                if (AudioIds.Reserved.Contains(id)) reservedEmpty.Add(id);
                else if (AudioIds.Fallback.Contains(id)) fallbackEmpty.Add(id);
                else requiredEmpty.Add(id); // Required, or unclassified: treat as used by code
            }
            ok &= Check(report, "  every AudioIds ID has a library entry", missingIds.Count == 0,
                        missingIds.Count == 0 ? "" : string.Join(", ", missingIds) + " (run TWR > Audio > Build Audio (A1))");
            ok &= Check(report, "  IDs used by code have a clip (D-228)", requiredEmpty.Count == 0,
                        requiredEmpty.Count == 0 ? "" : "no clip: " + string.Join(", ", requiredEmpty) + $" (drag a clip into {AudioLibraryPath})");
            if (fallbackEmpty.Count > 0)
            {
                string text = "no clip yet (only played when the object's own clip slot is empty; silent until A3): " + string.Join(", ", fallbackEmpty);
                report.AppendLine("    warning: " + text);
                Debug.LogWarning("[TWR] AudioLibrary: " + text + " (D-228).", library);
            }
            if (reservedEmpty.Count > 0)
            {
                string text = "no clip for reserved ID(s) " + string.Join(", ", reservedEmpty) + " (not played yet, but register the file now)";
                report.AppendLine("    warning: " + text);
                Debug.LogWarning("[TWR] AudioLibrary: " + text + " (D-228).", library);
            }
            var reservedFilled = AudioIds.Reserved.Where(id => !reservedEmpty.Contains(id) && !missingIds.Contains(id)).ToList();
            if (reservedFilled.Count > 0)
                report.AppendLine("    note: registered for later, nothing plays it yet (fine): " + string.Join(", ", reservedFilled) + " (A5, D-081, D-228)");

            // Loops are flagged (the loop check uses the flag) and no music is in the library yet (A2).
            var loopNotFlagged = AudioIds.Loops.Where(id => library.TryGet(id, out AudioLibrary.Entry e) && !e.loop).ToList();
            if (loopNotFlagged.Count > 0)
            {
                string text = "'loop' is not ticked for " + string.Join(", ", loopNotFlagged) + " (run Build Audio (A1) or tick it in the asset)";
                report.AppendLine("    warning: " + text);
                Debug.LogWarning("[TWR] AudioLibrary: " + text + " (D-228).", library);
            }
            var musicClips = new List<string>();
            if (library.entries != null)
                foreach (AudioLibrary.Entry entry in library.entries)
                    if (entry?.clips != null)
                        foreach (AudioClip clip in entry.clips)
                            if (clip != null && AssetDatabase.GetAssetPath(clip).StartsWith(AudioFolder + "/Music/")) musicClips.Add($"{entry.id}: {clip.name}");
            if (musicClips.Count > 0)
            {
                string text = "music clips are in the library before A2 (they'd be loaded with it from Resources): " + string.Join(", ", musicClips);
                report.AppendLine("    warning: " + text);
                Debug.LogWarning("[TWR] AudioLibrary: " + text + " (D-228).", library);
            }
            var unknown = library.entries == null ? new List<string>() :
                library.entries.Where(e => e != null && !string.IsNullOrEmpty(e.id) && !constants.Contains(e.id)).Select(e => e.id).ToList();
            if (unknown.Count > 0) report.AppendLine("    note: entries with IDs not in AudioIds (nothing can ask for them): " + string.Join(", ", unknown));
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
                    int fallback = 0;
                    foreach (AudioSource source in sources) if (CheckAudioSource(source, problems)) fallback++;
                    ok &= Check(report, $"scene '{scene.name}': {sources.Count} AudioSource(s) routed, 3D < {MaxAudioDistance:0} m{FallbackNote(fallback)}", problems.Count == 0,
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
                int fallback = 0;
                foreach (AudioSource source in sources) if (CheckAudioSource(source, problems)) fallback++;
                ok &= Check(report, $"prefab '{Path.GetFileName(path)}': {sources.Length} AudioSource(s){FallbackNote(fallback)}", problems.Count == 0,
                            problems.Count == 0 ? "" : "\n      " + string.Join("\n      ", problems) + "\n      (run TWR > Audio > Build Audio (A1))");
            }
            return ok;
        }

        /// Adds this source's problems. Returns true if it sits on a user group whose state group the mixer lacks: that's
        /// the expected fallback (reported once in the TWR_Mixer list), not a per-source problem.
        static bool CheckAudioSource(AudioSource source, List<string> problems)
        {
            string where = HierarchyPath(source.transform);
            AudioMixerGroup group = source.outputAudioMixerGroup;
            bool onFallback = false;
            if (group == null) problems.Add($"{where}: no mixer group (D-219)");
            else if (!group.name.EndsWith("State"))
            {
                onFallback = missingStateGroups.Contains(group.name + "State");
                if (!onFallback) problems.Add($"{where}: routed to '{group.name}', must be a state group (e.g. SFXState, D-219)");
            }
            if (source.spatialBlend > 0f && source.maxDistance >= MaxAudioDistance)
                problems.Add($"{where}: 3D max distance {source.maxDistance:0} m (must be < {MaxAudioDistance:0} m, D-144, D-227)");
            return onFallback;
        }

        static string FallbackNote(int count) =>
            count > 0 ? $"; {count} on their parent group until the State groups exist (see the TWR_Mixer steps)" : "";

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
