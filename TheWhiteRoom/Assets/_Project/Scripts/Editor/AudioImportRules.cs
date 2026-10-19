using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TWR.EditorTools
{
    /// <summary>
    /// Import settings for the real audio files (D-228, D-100):
    /// - Audio/Ambience (loops, e.g. amb_fluorescent_hum_01): Streaming (Compressed In Memory is also accepted), Vorbis
    ///   quality about 0.7, Preserve sample rate, Load In Background on, Force To Mono off. Streaming keeps a 38 s stereo
    ///   loop out of memory; looping itself is set on the AudioSource.
    /// - Audio/SFX (short one-shots, e.g. the violin stinger): Decompress On Load (Compressed In Memory also accepted),
    ///   Preserve sample rate, Preload Audio Data on, Load In Background off, so a jump scare plays the same frame it fires.
    /// - Audio/Music (A2, D-235): the same loop rule as Ambience for every file, stems AND the ending stingers (they are
    ///   20-40 s full mixes, so Streaming keeps them out of memory too; they start on a white screen, where the stream
    ///   start-up is never heard). Looping is set by the MusicDirector on its sources, never in the importer.
    /// - Audio/Placeholder is NOT touched (the builder sets those when it writes them).
    /// Brand-new files get the settings in OnPreprocessAudio (no extra import). EnsureProjectAudio (run by
    /// TWR > Audio > Build Audio (A1)) fixes existing files, but only values outside the accepted range and only then
    /// reimports, so running it again changes nothing. It also gives the loop check's temporary copy PCM + Decompress On
    /// Load so its samples can be read (AudioLoopCheck).
    /// </summary>
    public class AudioImportRules : AssetPostprocessor
    {
        public const string AmbienceFolder = "Assets/_Project/Audio/Ambience";
        public const string SfxFolder = "Assets/_Project/Audio/SFX";
        public const string MusicFolder = "Assets/_Project/Audio/Music";
        /// Temporary copies made by the loop check (outside _Project, deleted right after).
        public const string LoopCheckTempFolder = "Assets/_TWR_LoopCheckTemp";

        const float LoopQuality = 0.7f;
        const float QualityTolerance = 0.1f;
        const string StandalonePlatform = "Standalone";

        enum Rule { None, Loop, OneShot, LoopCheckTemp }

        static Rule RuleFor(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return Rule.None;
            string path = assetPath.Replace('\\', '/');
            if (path.StartsWith(LoopCheckTempFolder + "/")) return Rule.LoopCheckTemp;
            if (path.StartsWith(AmbienceFolder + "/") || path.StartsWith(MusicFolder + "/")) return Rule.Loop;
            if (path.StartsWith(SfxFolder + "/")) return Rule.OneShot;
            return Rule.None;
        }

        // ---------- new files (first import) ----------

        void OnPreprocessAudio()
        {
            Rule rule = RuleFor(assetPath);
            if (rule == Rule.None) return;
            var importer = (AudioImporter)assetImporter;
            // Existing files keep whatever the developer set; EnsureProjectAudio only fixes values outside the rule.
            if (rule != Rule.LoopCheckTemp && !importer.importSettingsMissing) return;
            ApplyFull(importer, rule);
        }

        /// The complete settings for a new file.
        static void ApplyFull(AudioImporter importer, Rule rule)
        {
            AudioImporterSampleSettings sample = importer.defaultSampleSettings;
            importer.forceToMono = false;
            sample.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            switch (rule)
            {
                case Rule.Loop:
                    importer.loadInBackground = true;
                    sample.loadType = AudioClipLoadType.Streaming;
                    sample.compressionFormat = AudioCompressionFormat.Vorbis;
                    sample.quality = LoopQuality;
                    break;
                case Rule.OneShot:
                    importer.loadInBackground = false;
                    sample.loadType = AudioClipLoadType.DecompressOnLoad;
                    sample.compressionFormat = AudioCompressionFormat.Vorbis;
                    sample.quality = LoopQuality;
                    sample.preloadAudioData = true;
                    break;
                case Rule.LoopCheckTemp:
                    // Plain PCM decoded once on import, loaded synchronously: AudioClip.GetData works on it.
                    importer.loadInBackground = false;
                    sample.loadType = AudioClipLoadType.DecompressOnLoad;
                    sample.compressionFormat = AudioCompressionFormat.PCM;
                    sample.preloadAudioData = true;
                    break;
            }
            importer.defaultSampleSettings = sample;
        }

        // ---------- existing files (builder step) ----------

        /// Checks every clip in Audio/Ambience, Audio/SFX and Audio/Music and fixes only what breaks the rule (D-228). Imports files
        /// Unity hasn't seen yet. Logs one [TWR] line per changed file. Returns the number of changed files.
        public static int EnsureProjectAudio()
        {
            int changed = 0;
            foreach (string folder in new[] { AmbienceFolder, SfxFolder, MusicFolder })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".ogg" && ext != ".wav" && ext != ".mp3") continue;
                    string path = file.Replace('\\', '/');
                    if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); // new: OnPreprocessAudio sets it
                    if (EnsureImportSettings(path)) changed++;
                }
            }
            return changed;
        }

        /// Read only: what breaks this file's folder rule (empty list = fine). Used by the [TWR] music validation (D-235).
        public static List<string> RuleProblems(string path)
        {
            var problems = new List<string>();
            Rule rule = RuleFor(path);
            if (rule != Rule.Loop && rule != Rule.OneShot) return problems;
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer))
            {
                problems.Add("not imported");
                return problems;
            }
            AudioImporterSampleSettings sample = importer.defaultSampleSettings;
            if (importer.forceToMono) problems.Add("Force To Mono is on");
            if (sample.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate) problems.Add("Sample Rate is not Preserve");
            if (rule == Rule.Loop)
            {
                if (sample.loadType == AudioClipLoadType.DecompressOnLoad) problems.Add("Load Type is Decompress On Load (Streaming wanted)");
                if (sample.compressionFormat != AudioCompressionFormat.Vorbis) problems.Add($"Compression is {sample.compressionFormat} (Vorbis wanted)");
                else if (Mathf.Abs(sample.quality - LoopQuality) > QualityTolerance) problems.Add($"Quality {Mathf.RoundToInt(sample.quality * 100f)} (70 wanted)");
                if (!importer.loadInBackground) problems.Add("Load In Background is off");
            }
            else
            {
                if (sample.loadType == AudioClipLoadType.Streaming) problems.Add("Load Type is Streaming (Decompress On Load wanted)");
                if (!sample.preloadAudioData) problems.Add("Preload Audio Data is off");
            }
            if (importer.ContainsSampleSettingsOverride(StandalonePlatform)) problems.Add("has a Windows (Standalone) override");
            return problems;
        }

        /// Fix this file's settings if they break its folder's rule. True if it was changed (and reimported).
        public static bool EnsureImportSettings(string path)
        {
            Rule rule = RuleFor(path);
            if (rule != Rule.Loop && rule != Rule.OneShot) return false;
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) return false;

            var changes = new List<string>();
            AudioImporterSampleSettings sample = importer.defaultSampleSettings;
            if (importer.forceToMono) { importer.forceToMono = false; changes.Add("Force To Mono off"); }
            if (sample.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate)
            {
                sample.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                changes.Add("Sample Rate = Preserve");
            }

            if (rule == Rule.Loop)
            {
                if (sample.loadType == AudioClipLoadType.DecompressOnLoad)
                {
                    sample.loadType = AudioClipLoadType.Streaming;
                    changes.Add("Load Type = Streaming");
                }
                if (sample.compressionFormat != AudioCompressionFormat.Vorbis)
                {
                    sample.compressionFormat = AudioCompressionFormat.Vorbis;
                    sample.quality = LoopQuality;
                    changes.Add("Compression = Vorbis, Quality 70");
                }
                else if (Mathf.Abs(sample.quality - LoopQuality) > QualityTolerance)
                {
                    changes.Add($"Quality {Mathf.RoundToInt(sample.quality * 100f)} -> 70");
                    sample.quality = LoopQuality;
                }
                if (!importer.loadInBackground) { importer.loadInBackground = true; changes.Add("Load In Background on"); }
            }
            else
            {
                if (sample.loadType == AudioClipLoadType.Streaming)
                {
                    sample.loadType = AudioClipLoadType.DecompressOnLoad;
                    changes.Add("Load Type = Decompress On Load");
                }
                if (!sample.preloadAudioData) { sample.preloadAudioData = true; changes.Add("Preload Audio Data on"); }
                if (importer.loadInBackground) { importer.loadInBackground = false; changes.Add("Load In Background off"); }
            }

            if (importer.ContainsSampleSettingsOverride(StandalonePlatform))
                Debug.LogWarning($"[TWR] {path} has a Windows (Standalone) override in its import settings; it wins over the " +
                                 "Default tab these rules set. Untick 'Override for Windows' unless you want it (D-228).", importer);

            if (changes.Count == 0) return false;
            importer.defaultSampleSettings = sample;
            importer.SaveAndReimport();
            Debug.Log($"[TWR] Import settings of {path}: {string.Join(", ", changes)} (D-228).",
                      AssetDatabase.LoadAssetAtPath<AudioClip>(path));
            return true;
        }
    }
}
