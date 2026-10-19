using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using TWR.Audio;
using TWR.Endings;
using TWR.Run;

namespace TWR.EditorTools
{
    /// <summary>
    /// Audio step A2, adaptive music (D-230 to D-235), menu "TWR/Audio/Build Music (A2)". No scene is rebuilt: the
    /// MusicDirector creates itself at runtime. The builder:
    /// 1. runs the A1 asset step first (routing settings, AudioLibrary with every AudioIds ID incl. the music IDs);
    /// 2. applies the import rule to every file in Audio/Music: Streaming, Vorbis quality 70, Preserve sample rate,
    ///    Load In Background (loops AND ending stingers; AudioImportRules, D-228), fixing only what is off;
    /// 3. discovers the stems of each state BY FILE NAME (mus_&lt;state&gt;_a_&lt;stem&gt;, no fixed stem set, D-231) and
    ///    creates/updates one MusicData per track in Data/Audio/Music (your onAtEntry ticks and times are kept; new
    ///    stems get the D-232 default: on at entry, except the drums in Exploration / Puzzle / Boss);
    /// 4. fills Data/Resources/MusicLibrary (tracks per state, boss tracks, main menu scene name from the RunConfig);
    /// 5. registers every track in the AudioLibrary under its AudioIds music ID (D-228, D-235);
    /// 6. puts each scripted ending's stinger into its EndingData.music if empty; Overtime gets none (D-221, D-234);
    /// 7. runs the [TWR] music validation (TestSceneBuilder.MusicValidation.cs).
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string MusicFilesFolder = AudioImportRules.MusicFolder;
        const string MusicDataFolder = AudioDataFolder + "/Music";
        const string MusicLibraryPath = ResourcesDataFolder + "/MusicLibrary.asset";
        /// The SUNO variant A2 uses (mus_&lt;state&gt;_a...). Other variants are reported, not used.
        const string MusicVariant = "a";

        struct MusicTrackSpec
        {
            public string key, asset, audioId, bossId;
            public MusicStateKind state;
        }

        /// The looping states (file key = the part after "mus_"). Bosses must match BossData.bossId.
        static readonly MusicTrackSpec[] MusicTrackSpecs =
        {
            new MusicTrackSpec { key = "lobby", asset = "MusicData_Lobby", audioId = AudioIds.MusicLobby, state = MusicStateKind.Lobby },
            new MusicTrackSpec { key = "explore", asset = "MusicData_Explore", audioId = AudioIds.MusicExplore, state = MusicStateKind.Exploration },
            new MusicTrackSpec { key = "puzzle", asset = "MusicData_Puzzle", audioId = AudioIds.MusicPuzzle, state = MusicStateKind.Puzzle },
            new MusicTrackSpec { key = "combat", asset = "MusicData_Combat", audioId = AudioIds.MusicCombat, state = MusicStateKind.Combat },
            new MusicTrackSpec { key = "boss_bull", asset = "MusicData_BossBull", audioId = AudioIds.MusicBossBull, state = MusicStateKind.Boss, bossId = "boss.bull" },
            new MusicTrackSpec { key = "boss_preacher", asset = "MusicData_BossPreacher", audioId = AudioIds.MusicBossPreacher, state = MusicStateKind.Boss, bossId = "boss.preacher" },
        };

        struct MusicEndingSpec
        {
            public string key, endingAsset, audioId;
            /// False = registered only, never assigned or played (Overtime, D-221, D-234).
            public bool plays;
        }

        static readonly MusicEndingSpec[] MusicEndingSpecs =
        {
            new MusicEndingSpec { key = "end_clockout", endingAsset = "Ending_ClockOut", audioId = AudioIds.MusicEndClockOut, plays = true },
            new MusicEndingSpec { key = "end_overtime", endingAsset = "Ending_Overtime", audioId = AudioIds.MusicEndOvertime, plays = false },
            new MusicEndingSpec { key = "end_floorplan", endingAsset = "Ending_FloorPlan", audioId = AudioIds.MusicEndFloorPlan, plays = true },
            new MusicEndingSpec { key = "end_absorbed", endingAsset = "Ending_Absorbed", audioId = AudioIds.MusicEndAbsorbed, plays = true },
        };

        /// One audio file in Audio/Music, split by name: mus_&lt;key&gt;_&lt;variant&gt;[_&lt;stem&gt;].
        class MusicFile
        {
            public string path, fileName, key, variant, stem;
            public AudioClip clip;
        }

        // ---------- menu ----------

        [MenuItem("TWR/Audio/Build Music (A2)")]
        public static void BuildMusicMenu()
        {
            reportedAudioProblems.Clear();
            Debug.Log("[TWR] Music A2 (D-230 to D-235): import settings, stems by file name, MusicData, MusicLibrary, AudioLibrary IDs, ending stingers ...");
            if (!PrepareAudioAssets(out AudioAssets audio))
            {
                Debug.LogError("[TWR] Music A2: the A1 audio assets could not be prepared (see errors above). Run TWR > Audio > Build Audio (A1).");
                return;
            }
            EnsureFolder(MusicDataFolder);

            List<MusicFile> files = DiscoverMusicFiles(new List<string>());
            int reimported = 0;
            foreach (MusicFile file in files)
                if (AudioImportRules.EnsureImportSettings(file.path)) reimported++;
            if (reimported > 0) files = DiscoverMusicFiles(new List<string>()); // fresh clip references after the reimport

            var datas = new Dictionary<string, MusicData>();
            foreach (MusicTrackSpec spec in MusicTrackSpecs)
            {
                MusicData data = EnsureMusicData(spec, files);
                if (data != null) datas[spec.key] = data;
            }
            EnsureMusicLibrary(datas, files);
            RegisterMusicInAudioLibrary(audio.library, datas, files);
            AssignEndingStingers(files);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TWR] Music A2: {files.Count} music file(s), {reimported} reimported, {datas.Count} MusicData asset(s) in {MusicDataFolder}.");
            var report = new StringBuilder();
            report.AppendLine("[TWR] Music A2 (D-230 to D-235)");
            bool ok = ValidateMusic(report);
            if (ok)
            {
                Debug.Log(report.ToString());
                Debug.Log("[TWR] Music built and valid. Press Play in MainMenu (thin lobby track) or Core; F1 shows the Music section. " +
                          "Run TWR > Audio > Validate Audio (A1) for the full audio check (scenes, prefabs, mixer).");
            }
            else
            {
                Debug.LogError(report.ToString());
                Debug.LogError("[TWR] Music built with problems (see the [TWR] Music report above).");
            }
        }

        // ---------- discovery ----------

        static IEnumerable<string> AllMusicKeys() =>
            MusicTrackSpecs.Select(s => s.key).Concat(MusicEndingSpecs.Select(s => s.key));

        /// Every audio file in Audio/Music, parsed by name. Names that don't fit go to problems (and are skipped).
        static List<MusicFile> DiscoverMusicFiles(List<string> problems)
        {
            var files = new List<MusicFile>();
            if (!Directory.Exists(MusicFilesFolder))
            {
                problems.Add($"folder {MusicFilesFolder} not found");
                return files;
            }
            // Longest key first, so 'boss_bull' would win over a shorter key that is its prefix.
            List<string> keys = AllMusicKeys().OrderByDescending(k => k.Length).ToList();
            foreach (string full in Directory.GetFiles(MusicFilesFolder).OrderBy(f => f, System.StringComparer.Ordinal))
            {
                string ext = Path.GetExtension(full).ToLowerInvariant();
                if (ext != ".ogg" && ext != ".wav" && ext != ".mp3") continue;
                string path = full.Replace('\\', '/');
                string fileName = Path.GetFileName(path);
                string name = Path.GetFileNameWithoutExtension(path);
                if (name != name.ToLowerInvariant()) { problems.Add($"{fileName}: use lower case only"); continue; }
                if (name.Contains(".") || name.Contains(" ")) { problems.Add($"{fileName}: no dots or spaces in the name"); continue; }
                if (!name.StartsWith("mus_")) { problems.Add($"{fileName}: music files start with 'mus_'"); continue; }
                string key = keys.FirstOrDefault(k => name.StartsWith("mus_" + k + "_"));
                if (key == null) { problems.Add($"{fileName}: unknown state (expected mus_<{string.Join("|", AllMusicKeys())}>_a[_<stem>])"); continue; }

                string rest = name.Substring(("mus_" + key + "_").Length); // "a_bass" or "a"
                int split = rest.IndexOf('_');
                string variant = split < 0 ? rest : rest.Substring(0, split);
                string stem = split < 0 ? null : rest.Substring(split + 1);
                if (variant.Length == 0 || (stem != null && stem.Length == 0)) { problems.Add($"{fileName}: expected mus_{key}_<variant>[_<stem>]"); continue; }

                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                files.Add(new MusicFile
                {
                    path = path, fileName = fileName, key = key, variant = variant, stem = stem,
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path),
                });
            }
            return files;
        }

        /// The stems of one looping track (variant a), sorted by stem name. Falls back to a full mix (no stem) named "full".
        static List<MusicFile> StemsOf(string key, List<MusicFile> files)
        {
            List<MusicFile> stems = files.Where(f => f.key == key && f.variant == MusicVariant && f.stem != null)
                                         .OrderBy(f => f.stem, System.StringComparer.Ordinal).ToList();
            if (stems.Count == 0)
            {
                MusicFile fullMix = files.FirstOrDefault(f => f.key == key && f.variant == MusicVariant && f.stem == null);
                if (fullMix != null) stems.Add(fullMix);
            }
            return stems;
        }

        static MusicFile EndingFileOf(string key, List<MusicFile> files) =>
            files.FirstOrDefault(f => f.key == key && f.variant == MusicVariant && f.stem == null);

        static string StemName(MusicFile file) => file.stem ?? "full";

        // ---------- assets ----------

        /// D-232 default for a NEW stem: on at entry, except the drums in Exploration, Puzzle and Boss.
        static bool DefaultOnAtEntry(MusicStateKind state, bool isDrums)
        {
            switch (state)
            {
                case MusicStateKind.Exploration:
                case MusicStateKind.Puzzle:
                case MusicStateKind.Boss:
                    return !isDrums;
                default:
                    return true; // Lobby (all stems), Combat (all)
            }
        }

        static MusicData EnsureMusicData(MusicTrackSpec spec, List<MusicFile> files)
        {
            string path = MusicDataFolder + "/" + spec.asset + ".asset";
            MusicData data = LoadOrCreate<MusicData>(path, d =>
            {
                d.state = spec.state;
                d.trackName = "mus_" + spec.key + "_" + MusicVariant;
                d.bossId = spec.bossId;
            });
            if (data == null)
            {
                Debug.LogError("[TWR] Could not create " + path);
                return null;
            }

            bool changed = false;
            if (data.state != spec.state) { data.state = spec.state; changed = true; }
            string trackName = "mus_" + spec.key + "_" + MusicVariant;
            if (data.trackName != trackName) { data.trackName = trackName; changed = true; }
            if (!string.IsNullOrEmpty(spec.bossId) && data.bossId != spec.bossId) { data.bossId = spec.bossId; changed = true; }

            List<MusicFile> stems = StemsOf(spec.key, files);
            MusicData.Stem[] old = data.stems ?? new MusicData.Stem[0];
            string before = StemSignature(old); // before the loop below updates clips in place
            var list = new List<MusicData.Stem>();
            foreach (MusicFile file in stems)
            {
                string stemName = StemName(file);
                MusicData.Stem existing = old.FirstOrDefault(s => s != null && s.name == stemName);
                if (existing == null)
                {
                    existing = new MusicData.Stem { name = stemName, onAtEntry = DefaultOnAtEntry(spec.state, stemName == data.drumsStem) };
                    Debug.Log($"[TWR] {spec.asset}: new stem '{stemName}' ({file.fileName}), on at entry: {(existing.onAtEntry ? "yes" : "no")} (D-232).", data);
                }
                existing.clip = file.clip;
                list.Add(existing);
            }
            foreach (MusicData.Stem gone in old)
                if (gone != null && list.All(s => s.name != gone.name))
                    Debug.LogWarning($"[TWR] {spec.asset}: stem '{gone.name}' has no file any more; removed (D-231).", data);

            if (before != StemSignature(list) || old.Length != list.Count) { data.stems = list.ToArray(); changed = true; }
            if (stems.Count == 0)
                Debug.LogError($"[TWR] {spec.asset}: no file mus_{spec.key}_{MusicVariant}_<stem> in {MusicFilesFolder}: this state is silent (D-231).", data);

            if (changed)
            {
                EditorUtility.SetDirty(data);
                Debug.Log($"[TWR] {spec.asset}: {list.Count} stem(s): {string.Join(", ", list.Select(s => s.name + (s.onAtEntry ? "" : " (later)")))}.", data);
            }
            return data;
        }

        static string StemSignature(IEnumerable<MusicData.Stem> stems) =>
            string.Join("|", stems.Where(s => s != null).Select(s => s.name + ":" + (s.clip != null ? AssetDatabase.GetAssetPath(s.clip) : "-")));

        static void EnsureMusicLibrary(Dictionary<string, MusicData> datas, List<MusicFile> files)
        {
            MusicLibrary library = LoadOrCreate<MusicLibrary>(MusicLibraryPath, null);
            if (library == null)
            {
                Debug.LogError("[TWR] Could not create " + MusicLibraryPath);
                return;
            }
            bool changed = false;
            changed |= SetTrack(ref library.lobby, datas, "lobby");
            changed |= SetTrack(ref library.exploration, datas, "explore");
            changed |= SetTrack(ref library.puzzle, datas, "puzzle");
            changed |= SetTrack(ref library.combat, datas, "combat");

            var bosses = new List<MusicData>();
            if (library.bosses != null) foreach (MusicData b in library.bosses) if (b != null && !bosses.Contains(b)) bosses.Add(b);
            foreach (MusicTrackSpec spec in MusicTrackSpecs)
                if (spec.state == MusicStateKind.Boss && datas.TryGetValue(spec.key, out MusicData boss) && !bosses.Contains(boss))
                    bosses.Add(boss);
            if (library.bosses == null || !bosses.SequenceEqual(library.bosses)) { library.bosses = bosses.ToArray(); changed = true; }

            var config = AssetDatabase.LoadAssetAtPath<RunConfig>(RunConfigPath);
            if (config != null && !string.IsNullOrEmpty(config.mainMenuSceneName) && library.mainMenuSceneName != config.mainMenuSceneName)
            {
                library.mainMenuSceneName = config.mainMenuSceneName;
                changed = true;
            }
            if (library.debugEndingClip == null)
            {
                MusicFile first = MusicEndingSpecs.Where(s => s.plays).Select(s => EndingFileOf(s.key, files)).FirstOrDefault(f => f != null);
                if (first != null && first.clip != null) { library.debugEndingClip = first.clip; changed = true; }
            }
            if (changed)
            {
                EditorUtility.SetDirty(library);
                Debug.Log($"[TWR] MusicLibrary filled: {MusicLibraryPath} (D-235).", library);
            }
        }

        static bool SetTrack(ref MusicData slot, Dictionary<string, MusicData> datas, string key)
        {
            if (!datas.TryGetValue(key, out MusicData data) || slot == data) return false;
            if (slot != null) Debug.Log($"[TWR] MusicLibrary: '{key}' slot was {slot.name}, now {data.name}.");
            slot = data;
            return true;
        }

        /// D-228, D-235: one AudioLibrary entry per track, holding every stem (or the stinger). Builder-owned: the files decide.
        static void RegisterMusicInAudioLibrary(AudioLibrary library, Dictionary<string, MusicData> datas, List<MusicFile> files)
        {
            if (library == null) return;
            bool changed = false;
            var entries = new List<AudioLibrary.Entry>(library.entries ?? new AudioLibrary.Entry[0]);
            foreach (MusicTrackSpec spec in MusicTrackSpecs)
            {
                AudioClip[] clips = datas.TryGetValue(spec.key, out MusicData data) && data.stems != null
                    ? data.stems.Where(s => s != null && s.clip != null).Select(s => s.clip).ToArray()
                    : new AudioClip[0];
                changed |= SetLibraryClips(entries, spec.audioId, clips);
            }
            foreach (MusicEndingSpec spec in MusicEndingSpecs)
            {
                MusicFile file = EndingFileOf(spec.key, files);
                changed |= SetLibraryClips(entries, spec.audioId, file != null && file.clip != null ? new[] { file.clip } : new AudioClip[0]);
            }
            if (!changed) return;
            library.entries = entries.ToArray();
            EditorUtility.SetDirty(library);
            Debug.Log("[TWR] AudioLibrary: music IDs registered (" + string.Join(", ", AudioIds.Music) + ", D-228, D-235).", library);
        }

        static bool SetLibraryClips(List<AudioLibrary.Entry> entries, string id, AudioClip[] clips)
        {
            AudioLibrary.Entry entry = entries.Find(e => e != null && e.id == id);
            if (entry == null)
            {
                entry = new AudioLibrary.Entry { id = id };
                entries.Add(entry);
            }
            else if (entry.clips != null && entry.clips.SequenceEqual(clips)) return false;
            entry.clips = clips;
            return true;
        }

        /// D-234: the stinger goes into the EndingData of each scripted ending if its slot is empty (your choice is kept).
        /// Overtime is never filled (D-221); the validation warns if it has one.
        static void AssignEndingStingers(List<MusicFile> files)
        {
            foreach (MusicEndingSpec spec in MusicEndingSpecs)
            {
                if (!spec.plays) continue;
                EndingData ending = LoadEndingData(spec.endingAsset);
                MusicFile file = EndingFileOf(spec.key, files);
                if (ending == null)
                {
                    Debug.LogWarning($"[TWR] {spec.endingAsset} not found in {EndingsDataFolder}: run TWR > Run > Build Endings + Main Menu, then Build Music (A2) again.");
                    continue;
                }
                if (ending.music != null || file == null || file.clip == null) continue;
                ending.music = file.clip;
                EditorUtility.SetDirty(ending);
                Debug.Log($"[TWR] {spec.endingAsset}: ending stinger = {file.fileName} (plays once on scene start, D-234).", ending);
            }
        }
    }
}
