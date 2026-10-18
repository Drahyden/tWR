using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Localization;
using UnityEngine.UI;
using TWR.Audio;
using TWR.Run;
using TWR.UI;

namespace TWR.EditorTools
{
    /// <summary>
    /// Audio step A1 (D-213 to D-227), menu "TWR/Audio/Build Audio (A1)" (also part of "TWR/Run/Build Run Scenes"):
    /// - Placeholder WAVs in Audio/Placeholder (hum loop, phone, printer, keyboard, UI click; AudioPlaceholderWav), each
    ///   listed in docs/audio/LICENSES.md (D-218).
    /// - Data/Resources/AudioRoutingSettings: TWR_Mixer, its five STATE groups and four snapshots, found BY NAME (the
    ///   developer-made .mixer file is never edited). Missing groups/snapshots are reported with [TWR] errors that say what
    ///   to add by hand (D-219, D-220).
    /// - Data/Resources/AudioLibrary: every AudioIds ID, filled with the placeholders where empty (D-227).
    /// - Data/Audio/AmbientBedSettings and Data/Audio/ReverbPresets (D-223, D-224).
    /// - Then it rebuilds the sandbox and the run scenes, which route every AudioSource they make through RouteAudio
    ///   (player/gun/enemy/boss/doors = SFXState, ambient bed = AmbienceState, D-219), add the reverb zones per section
    ///   template (ReverbLayout, D-224), the Sound settings panel in the main menu (D-225) and click sounds on every menu
    ///   button, and finally runs the [TWR] audio validation (TestSceneBuilder.AudioValidation.cs, D-227).
    /// Assets are loaded or created AFTER EditorSceneManager.NewScene in the scene builders, like everywhere else.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string AudioFolder = Root + "/Audio";
        const string MixerPath = AudioFolder + "/TWR_Mixer.mixer";
        const string PlaceholderAudioFolder = AudioFolder + "/Placeholder";
        const string AudioDataFolder = Root + "/Data/Audio";
        const string ResourcesDataFolder = Root + "/Data/Resources";
        const string AudioRoutingPath = ResourcesDataFolder + "/AudioRoutingSettings.asset";
        const string AudioLibraryPath = ResourcesDataFolder + "/AudioLibrary.asset";
        const string AmbientBedSettingsPath = AudioDataFolder + "/AmbientBedSettings.asset";
        const string ReverbPresetsPath = AudioDataFolder + "/ReverbPresets.asset";

        /// Doors and other builder-made mechanisms: audible across a room, silent long before 100 m (D-144, D-227).
        const float MechanismAudioMaxDistance = 30f;

        struct AudioAssets
        {
            public AudioRoutingSettings routing;
            public AudioLibrary library;
            public AmbientBedSettings ambient;
            public ReverbPresets reverb;
        }

        /// One [TWR] error per problem per build pass (e.g. a missing group would otherwise be reported for every door).
        static readonly HashSet<string> reportedAudioProblems = new HashSet<string>();

        // ---------- menu ----------

        [MenuItem("TWR/Audio/Build Audio (A1)")]
        public static void BuildAudioMenu()
        {
            ProjectSetup.Run(BuildAudioAfterSetup); // also adds the Sound settings strings (D-084)
        }

        static void BuildAudioAfterSetup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            reportedAudioProblems.Clear();
            Debug.Log("[TWR] Audio A1 (D-213 to D-227): placeholder clips, AudioLibrary, mixer routing, reverb presets; " +
                      "then the sandbox and every run scene are rebuilt so all AudioSources are routed ...");
            if (!PrepareAudioAssets(out _))
            {
                Debug.LogError("[TWR] Audio A1: could not create the audio assets (see errors above).");
                return;
            }
            BuildTestSceneAfterSetup();   // the sandbox is in Build Settings too: its sources must be routed
            BuildRunScenesAfterSetup();   // run scenes + ending scenes + main menu, then all validations incl. audio
        }

        // ---------- assets ----------

        /// Placeholders, routing settings, library, ambient bed settings, reverb presets (created if missing, empty
        /// fields filled, your edits kept). Safe to call often; call AFTER NewScene inside scene builders.
        static bool PrepareAudioAssets(out AudioAssets audio)
        {
            audio = default;
            EnsureFolder(PlaceholderAudioFolder);
            EnsureFolder(AudioDataFolder);
            EnsureFolder(ResourcesDataFolder);

            Dictionary<string, AudioClip> placeholders = EnsurePlaceholderClips();
            audio.routing = EnsureAudioRouting();
            audio.library = EnsureAudioLibrary(placeholders);
            audio.ambient = LoadOrCreate<AmbientBedSettings>(AmbientBedSettingsPath, null);
            audio.reverb = LoadOrCreate<ReverbPresets>(ReverbPresetsPath, r => r.presets = ReverbPresets.DefaultPresets());
            if (audio.reverb != null) EnsureReverbPresets(audio.reverb);
            AssetDatabase.SaveAssets();
            return audio.routing != null && audio.library != null && audio.ambient != null && audio.reverb != null;
        }

        /// Writes the missing placeholder WAVs and imports them. Returns file name -> clip.
        static Dictionary<string, AudioClip> EnsurePlaceholderClips()
        {
            var clips = new Dictionary<string, AudioClip>();
            foreach (AudioPlaceholderWav.Placeholder placeholder in AudioPlaceholderWav.All)
            {
                string path = PlaceholderAudioFolder + "/" + placeholder.fileName;
                if (AudioPlaceholderWav.WriteIfMissing(PlaceholderAudioFolder, placeholder))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    ConfigureNewPlaceholderImport(path, placeholder.fileName == AudioPlaceholderWav.HumFile);
                    Debug.Log($"[TWR] Created placeholder clip {path} ({placeholder.description}, own work, D-218).");
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) clips[placeholder.fileName] = clip;
                else Debug.LogError("[TWR] Could not import the placeholder clip " + path);
            }
            return clips;
        }

        /// Only for files the builder just wrote, so your later import settings are kept.
        static void ConfigureNewPlaceholderImport(string path, bool isLoop)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) return;
            importer.forceToMono = true;
            AudioImporterSampleSettings sample = importer.defaultSampleSettings;
            sample.loadType = AudioClipLoadType.DecompressOnLoad;
            // The loop stays PCM so it repeats without a gap; the short one-shots are tiny anyway.
            sample.compressionFormat = isLoop ? AudioCompressionFormat.PCM : AudioCompressionFormat.ADPCM;
            importer.defaultSampleSettings = sample;
            importer.SaveAndReimport();
        }

        /// Data/Resources/AudioRoutingSettings with the mixer parts found by name (D-219, D-220).
        static AudioRoutingSettings EnsureAudioRouting()
        {
            EnsureFolder(ResourcesDataFolder);
            var routing = LoadOrCreate<AudioRoutingSettings>(AudioRoutingPath, null);
            if (routing == null) return null;

            AudioMixer mixer = LoadTwrMixer();
            bool changed = false;
            if (mixer == null)
            {
                ReportAudioProblemOnce("mixer", "[TWR] TWR_Mixer not found at " + MixerPath + ". Create it there (Project window > right-click > " +
                                                "Create > Audio Mixer, name it TWR_Mixer) with the groups Music, Ambience, SFX, Voice, UI (D-219).");
                return routing;
            }
            if (routing.mixer != mixer) { routing.mixer = mixer; changed = true; }

            for (int i = 0; i < AudioRouting.BusCount; i++)
            {
                var bus = (AudioBus)i;
                AudioMixerGroup group = AudioRouting.FindGroup(mixer, AudioRouting.StateGroupName(bus));
                if (group == null) ReportMissingStateGroup(bus);
                changed |= SetStateGroup(routing, bus, group);
            }
            for (int i = 0; i < AudioRouting.SnapshotCount; i++)
            {
                var kind = (AudioSnapshotKind)i;
                AudioMixerSnapshot snapshot = mixer.FindSnapshot(AudioRouting.SnapshotName(kind));
                if (snapshot == null)
                    ReportAudioProblemOnce("snapshot." + kind, $"[TWR] TWR_Mixer has no snapshot '{AudioRouting.SnapshotName(kind)}'. In the Audio Mixer " +
                                                             "window: Snapshots '+', name it exactly like that (D-220).");
                changed |= SetSnapshot(routing, kind, snapshot);
            }
            if (changed)
            {
                EditorUtility.SetDirty(routing);
                AssetDatabase.SaveAssets();
            }
            return routing;
        }

        static AudioMixer LoadTwrMixer()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer != null) return mixer;
            foreach (string guid in AssetDatabase.FindAssets("TWR_Mixer t:AudioMixer"))
            {
                mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetDatabase.GUIDToAssetPath(guid));
                if (mixer != null) return mixer;
            }
            return null;
        }

        static void ReportMissingStateGroup(AudioBus bus)
        {
            string user = AudioRouting.UserGroupName(bus), state = AudioRouting.StateGroupName(bus);
            ReportAudioProblemOnce("group." + state,
                $"[TWR] TWR_Mixer has no group '{state}' (child of '{user}'). Open the mixer (double-click TWR_Mixer), in Groups " +
                $"right-click '{user}' > Add child group, rename it '{state}'. Sources meant for it stay unrouted until then (D-219).");
        }

        static bool SetStateGroup(AudioRoutingSettings routing, AudioBus bus, AudioMixerGroup group)
        {
            switch (bus)
            {
                case AudioBus.Music: if (routing.musicState == group) return false; routing.musicState = group; return true;
                case AudioBus.Ambience: if (routing.ambienceState == group) return false; routing.ambienceState = group; return true;
                case AudioBus.SFX: if (routing.sfxState == group) return false; routing.sfxState = group; return true;
                case AudioBus.Voice: if (routing.voiceState == group) return false; routing.voiceState = group; return true;
                default: if (routing.uiState == group) return false; routing.uiState = group; return true;
            }
        }

        static bool SetSnapshot(AudioRoutingSettings routing, AudioSnapshotKind kind, AudioMixerSnapshot snapshot)
        {
            switch (kind)
            {
                case AudioSnapshotKind.Silent: if (routing.silentSnapshot == snapshot) return false; routing.silentSnapshot = snapshot; return true;
                case AudioSnapshotKind.Ending: if (routing.endingSnapshot == snapshot) return false; routing.endingSnapshot = snapshot; return true;
                case AudioSnapshotKind.Paused: if (routing.pausedSnapshot == snapshot) return false; routing.pausedSnapshot = snapshot; return true;
                default: if (routing.defaultSnapshot == snapshot) return false; routing.defaultSnapshot = snapshot; return true;
            }
        }

        /// Default volume/pitch per ID, and which placeholder fills it while it has no clips.
        struct LibraryDefault
        {
            public string id, placeholder;
            public float volumeMin, volumeMax, pitchMin, pitchMax;
        }

        static readonly LibraryDefault[] LibraryDefaults =
        {
            new LibraryDefault { id = AudioIds.AmbienceHum, placeholder = AudioPlaceholderWav.HumFile, volumeMin = 1f, volumeMax = 1f, pitchMin = 1f, pitchMax = 1f },
            new LibraryDefault { id = AudioIds.OfficePhone, placeholder = AudioPlaceholderWav.PhoneFile, volumeMin = 0.8f, volumeMax = 1f, pitchMin = 0.95f, pitchMax = 1.03f },
            new LibraryDefault { id = AudioIds.OfficePrinter, placeholder = AudioPlaceholderWav.PrinterFile, volumeMin = 0.8f, volumeMax = 1f, pitchMin = 0.92f, pitchMax = 1.05f },
            new LibraryDefault { id = AudioIds.OfficeKeyboard, placeholder = AudioPlaceholderWav.KeyboardFile, volumeMin = 0.6f, volumeMax = 0.9f, pitchMin = 0.92f, pitchMax = 1.08f },
            new LibraryDefault { id = AudioIds.UiClick, placeholder = AudioPlaceholderWav.UiClickFile, volumeMin = 0.6f, volumeMax = 0.6f, pitchMin = 1f, pitchMax = 1f },
            new LibraryDefault { id = AudioIds.UiBack, placeholder = AudioPlaceholderWav.UiClickFile, volumeMin = 0.55f, volumeMax = 0.55f, pitchMin = 0.8f, pitchMax = 0.8f },
        };

        /// Data/Resources/AudioLibrary: adds every missing AudioIds ID; fills EMPTY entries with their placeholder.
        static AudioLibrary EnsureAudioLibrary(Dictionary<string, AudioClip> placeholders)
        {
            var library = LoadOrCreate<AudioLibrary>(AudioLibraryPath, null);
            if (library == null) return null;

            var entries = new List<AudioLibrary.Entry>();
            if (library.entries != null)
                foreach (AudioLibrary.Entry e in library.entries) if (e != null) entries.Add(e);
            bool changed = library.entries == null || entries.Count != library.entries.Length;

            foreach (string id in AudioIds.All)
            {
                AudioLibrary.Entry entry = entries.Find(e => e.id == id);
                LibraryDefault defaults = System.Array.Find(LibraryDefaults, d => d.id == id);
                if (entry == null)
                {
                    entry = new AudioLibrary.Entry { id = id };
                    if (defaults.id == id)
                    {
                        entry.volumeMin = defaults.volumeMin;
                        entry.volumeMax = defaults.volumeMax;
                        entry.pitchMin = defaults.pitchMin;
                        entry.pitchMax = defaults.pitchMax;
                    }
                    entries.Add(entry);
                    changed = true;
                }
                if (!entry.HasClips && defaults.id == id && placeholders.TryGetValue(defaults.placeholder, out AudioClip clip))
                {
                    entry.clips = new[] { clip };
                    changed = true;
                    Debug.Log($"[TWR] AudioLibrary: '{id}' uses the placeholder {defaults.placeholder} (swap it in {AudioLibraryPath}, D-227).", library);
                }
            }
            if (changed)
            {
                library.entries = entries.ToArray();
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
            }
            return library;
        }

        /// Adds a default preset for any space the asset doesn't list yet (your values are kept).
        static void EnsureReverbPresets(ReverbPresets presets)
        {
            var list = new List<ReverbPresets.Preset>();
            if (presets.presets != null) list.AddRange(presets.presets);
            bool changed = false;
            foreach (ReverbPresets.Preset preset in ReverbPresets.DefaultPresets())
            {
                if (presets.Get(preset.space) != null) continue;
                list.Add(preset);
                changed = true;
            }
            if (!changed) return;
            presets.presets = list.ToArray();
            EditorUtility.SetDirty(presets);
        }

        static void ReportAudioProblemOnce(string key, string message)
        {
            if (reportedAudioProblems.Add(key)) Debug.LogError(message);
        }

        // ---------- routing helpers (used by every builder that makes an AudioSource, D-219) ----------

        /// Send a builder-made AudioSource to the bus's STATE group. Reports a missing group once per build.
        static void RouteAudio(AudioSource source, AudioBus bus)
        {
            if (source == null) return;
            var routing = AssetDatabase.LoadAssetAtPath<AudioRoutingSettings>(AudioRoutingPath);
            if (routing == null || routing.mixer == null) routing = EnsureAudioRouting();
            AudioMixerGroup group = routing != null ? routing.GetStateGroup(bus) : null;
            if (group == null)
            {
                ReportMissingStateGroup(bus);
                return;
            }
            source.outputAudioMixerGroup = group;
        }

        /// A 3D sound effect source (mechanisms, D-219): no play on awake, no doppler, bounded distance (< 100 m, D-227).
        static void ConfigureSfxSource3D(AudioSource source, float minDistance, float maxDistance)
        {
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            RouteAudio(source, AudioBus.SFX);
        }

        // ---------- ambient bed (D-077, D-223) ----------

        /// The ambient bed: 2D hum source (AmbienceState, reverbZoneMix 0) + AmbientBed with its settings.
        /// follow = the player (Core), or null = the AudioListener found at runtime (sandbox).
        static AmbientBed BuildAmbientBed(Transform follow, bool officeSoundsOnStart)
        {
            var go = new GameObject("Ambient Bed (2D hum + distant office one-shots, D-077, D-223)");
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = true;
            source.dopplerLevel = 0f;
            source.reverbZoneMix = 0f;
            RouteAudio(source, AudioBus.Ambience);

            var bed = go.AddComponent<AmbientBed>();
            EnsureFolder(AudioDataFolder);
            Assign(bed, "settings", LoadOrCreate<AmbientBedSettings>(AmbientBedSettingsPath, null));
            if (follow != null) Assign(bed, "follow", follow);
            var so = new SerializedObject(bed);
            so.FindProperty("officeSoundsOnStart").boolValue = officeSoundsOnStart;
            so.ApplyModifiedPropertiesWithoutUndo();
            return bed;
        }

        // ---------- reverb zones per section template (D-224) ----------

        struct ReverbRoom
        {
            public string name;
            public Vector3 centre, size;
            public ReverbSpace space;
        }

        static ReverbRoom Room(string name, Vector3 centre, Vector3 size, ReverbSpace space) =>
            new ReverbRoom { name = name, centre = centre, size = size, space = space };

        /// Room shell of BuildSectionShell(width, depth, height): x -w/2..w/2, z 4..4+depth.
        static ReverbRoom ShellRoom(string name, float width, float depth, float height, ReverbSpace space) =>
            Room(name, new Vector3(0f, height * 0.5f, VestibuleDepth + depth * 0.5f), new Vector3(width, height, depth), space);

        /// Room of EndingRoom(width, depth, height): x -w/2..w/2, z 0..depth.
        static ReverbRoom EndingShellRoom(string name, float width, float depth, float height, ReverbSpace space) =>
            Room(name, new Vector3(0f, height * 0.5f, depth * 0.5f), new Vector3(width, height, depth), space);

        /// Which rooms of which scene get which reverb (sizes match the builders). Portal pairs: both sides LargeHall.
        static ReverbRoom[] ReverbLayout(string sceneName)
        {
            Vector3 hallCentre = new Vector3((HallMinX + HallMaxX) * 0.5f, HallHeight * 0.5f, (HallMinZ + HallMaxZ) * 0.5f);
            Vector3 hallSize = new Vector3(HallMaxX - HallMinX, HallHeight, HallMaxZ - HallMinZ);
            switch (sceneName)
            {
                case LobbySceneName:
                    return new[]
                    {
                        Room("Lobby", new Vector3((LobbyMinX + LobbyMaxX) * 0.5f, LobbyHeight * 0.5f, (LobbyMinZ + LobbyMaxZ) * 0.5f),
                             new Vector3(LobbyMaxX - LobbyMinX, LobbyHeight, LobbyMaxZ - LobbyMinZ), ReverbSpace.LargeHall),
                        Room("Impossible Hall (portal pair: same preset)", hallCentre, hallSize, ReverbSpace.LargeHall),
                    };
                case "Section_Puzzle":
                    return new[] { Room("Puzzle Room", new Vector3(16.2f, PuzzleRoomHeight * 0.5f, 0f), new Vector3(12f, PuzzleRoomHeight, 12f), ReverbSpace.MediumRoom) };
                case "Section_Combat":
                    return new[] { ShellRoom("Combat Room", 20f, 16f, 4.5f, ReverbSpace.MediumRoom) };
                case "Section_Stealth":
                    return new[] { ShellRoom("Stealth Room", 24f, 18f, 4f, ReverbSpace.MediumRoom) };
                case "Section_ImpossibleGeometry":
                    return new[]
                    {
                        ShellRoom("Portal Room (portal pair: same preset)", 12f, 14f, 5f, ReverbSpace.LargeHall),
                        Room("Impossible Hall (portal pair: same preset)", hallCentre, hallSize, ReverbSpace.LargeHall),
                    };
                case "Section_Silent":
                    return new[] { ShellRoom("Silent Room", 12f, 12f, 6f, ReverbSpace.MediumRoom) };
                case "Fork":
                    return new[] { ShellRoom("Fork Room", 14f, 10f, 5f, ReverbSpace.SmallRoom) };
                case "Boss_Escape":
                    return new[] { ShellRoom("Bull Arena", BullArenaWidth, BullArenaLength, BullArenaHeight, ReverbSpace.BossArena) };
                case "Boss_Discover":
                    return new[] { ShellRoom("Preacher Arena", PreacherArenaWidth, PreacherArenaLength, PreacherArenaHeight, ReverbSpace.BossArena) };
                case "Ending_ClockOut":
                    return new[] { EndingShellRoom("Office", 16f, 18f, 3f, ReverbSpace.MediumRoom) };
                case "Ending_Overtime":
                    return new[] { EndingShellRoom("Empty White Room", 40f, 60f, 14f, ReverbSpace.LargeHall) };
                case "Ending_FloorPlan":
                    return new[] { EndingShellRoom("Basement", 14f, 14f, 2.8f, ReverbSpace.SmallRoom) };
                case "Ending_Absorbed":
                    return new[] { EndingShellRoom("Basement", 14f, 12f, 3f, ReverbSpace.SmallRoom) };
                default:
                    return new ReverbRoom[0];
            }
        }

        /// Adds this scene's reverb zones (spherical: full effect inside about half the room, fading out just past
        /// its corners). Does nothing for scenes without a layout (Core, MainMenu).
        static void AddReverbZones(string sceneName)
        {
            ReverbRoom[] rooms = ReverbLayout(sceneName);
            if (rooms.Length == 0) return;
            EnsureFolder(AudioDataFolder);
            var presets = LoadOrCreate<ReverbPresets>(ReverbPresetsPath, r => r.presets = ReverbPresets.DefaultPresets());

            Transform parent = new GameObject("Reverb Zones (D-224)").transform;
            foreach (ReverbRoom room in rooms)
            {
                ReverbPresets.Preset preset = presets != null ? presets.Get(room.space) : null;
                float margin = preset != null ? preset.fadeMargin : 3f;
                var go = new GameObject($"Reverb Zone: {room.name} ({room.space})");
                go.transform.SetParent(parent, false);
                go.transform.position = room.centre;
                var zone = go.AddComponent<AudioReverbZone>();
                zone.minDistance = 0.5f * Mathf.Max(room.size.x, Mathf.Max(room.size.y, room.size.z));
                zone.maxDistance = 0.5f * room.size.magnitude + margin;
                var setup = go.AddComponent<ReverbZoneSetup>();
                setup.Configure(presets, room.space);
                EditorUtility.SetDirty(setup);
            }
        }

        // ---------- Sound settings panel (D-225) ----------

        static readonly (AudioBus bus, string key)[] SoundSliders =
        {
            (AudioBus.Music, "settings.sound.music"),
            (AudioBus.Ambience, "settings.sound.ambience"),
            (AudioBus.SFX, "settings.sound.sfx"),
            (AudioBus.Voice, "settings.sound.voice"),
            (AudioBus.UI, "settings.sound.ui"),
        };

        /// "Sound" heading + five labelled 0-100 sliders on this panel, plus the AudioSettingsPanel that drives them.
        /// The panel is reusable: the future pause menu can build the same thing (D-225, D-087).
        static AudioSettingsPanel BuildAudioSettingsPanel(GameObject panel, float topY)
        {
            Color dark = new Color(0.1f, 0.1f, 0.1f), grey = new Color(0.3f, 0.3f, 0.3f);
            UiText("Sound Heading (localized)", panel.transform, new Vector2(0f, topY), new Vector2(1000f, 60f), 40f,
                   new LocalizedString("UI", "settings.sound.title"), dark, TextAlignmentOptions.Center);

            var rows = new List<(AudioBus bus, Slider slider, TMP_Text value)>();
            for (int i = 0; i < SoundSliders.Length; i++)
            {
                float y = topY - 80f - i * 74f;
                var (bus, key) = SoundSliders[i];
                UiText($"{bus} Label (localized)", panel.transform, new Vector2(-330f, y), new Vector2(380f, 50f), 32f,
                       new LocalizedString("UI", key), dark, TextAlignmentOptions.Right);
                Slider slider = UiSlider($"{bus} Slider (0-100)", panel.transform, new Vector2(110f, y), new Vector2(460f, 34f));
                TMP_Text value = UiText($"{bus} Value", panel.transform, new Vector2(410f, y), new Vector2(110f, 50f), 30f, null,
                                        grey, TextAlignmentOptions.Left);
                value.text = "100";
                rows.Add((bus, slider, value));
            }

            var settings = panel.AddComponent<AudioSettingsPanel>();
            var so = new SerializedObject(settings);
            SerializedProperty list = so.FindProperty("rows");
            list.arraySize = rows.Count;
            for (int i = 0; i < rows.Count; i++)
            {
                SerializedProperty row = list.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("bus").enumValueIndex = (int)rows[i].bus;
                row.FindPropertyRelative("slider").objectReferenceValue = rows[i].slider;
                row.FindPropertyRelative("valueLabel").objectReferenceValue = rows[i].value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }

        /// A plain uGUI slider (background, fill, handle) going 0..100 in whole numbers.
        static Slider UiSlider(string name, Transform parent, Vector2 position, Vector2 size)
        {
            RectTransform root = UiRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

            RectTransform background = UiRect("Background", root, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            StretchRect(background, new Vector2(0f, 0.3f), new Vector2(1f, 0.7f), Vector2.zero, Vector2.zero);
            background.gameObject.AddComponent<Image>().color = new Color(0.78f, 0.78f, 0.78f, 1f);

            RectTransform fillArea = UiRect("Fill Area", root, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            StretchRect(fillArea, new Vector2(0f, 0.3f), new Vector2(1f, 0.7f), new Vector2(5f, 0f), new Vector2(-15f, 0f));
            RectTransform fill = UiRect("Fill", fillArea, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            StretchRect(fill, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(10f, 0f));
            fill.gameObject.AddComponent<Image>().color = new Color(0.14f, 0.14f, 0.14f, 1f);

            RectTransform handleArea = UiRect("Handle Slide Area", root, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            StretchRect(handleArea, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
            RectTransform handle = UiRect("Handle", handleArea, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            StretchRect(handle, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(22f, 0f));
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.05f, 0.05f, 0.05f, 1f);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            slider.value = 100f;
            return slider;
        }

        static void StretchRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        /// Click sound on a menu button (UIState via the audio bootstrap, D-219).
        static void AddButtonSound(Button button, string soundId)
        {
            var sound = button.gameObject.AddComponent<UiButtonSound>();
            sound.SoundId = soundId;
            EditorUtility.SetDirty(sound);
        }

        // ---------- run assets (D-223) ----------

        /// D-223: office one-shots are never on in silent rooms or boss arenas. Other templates keep your setting.
        static void EnforceOfficeAmbienceRule(SectionData section)
        {
            if (section == null || !section.officeAmbience || SectionData.OfficeAmbienceAllowed(section.type)) return;
            section.officeAmbience = false;
            EditorUtility.SetDirty(section);
            Debug.Log($"[TWR] {section.name}: office ambience switched off ({section.type}, D-223).", section);
        }
    }
}
