using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TWR.Audio;

namespace TWR.EditorTools
{
    /// <summary>
    /// [TWR] loop check (D-228), menu "TWR/Audio/Check Loops"; also run by "TWR/Audio/Validate Audio (A1)" as WARNINGS
    /// (never errors). For every AudioLibrary entry with "loop" ticked (the fluorescent hum) it reads the clip's samples
    /// and reports:
    /// - leading / trailing silence: milliseconds quieter than -60 dBFS at the start / end (warning above 5 ms: a gap);
    /// - the seam jump: |last sample - first sample| per channel (warning above 0.05: a click at the loop point);
    /// - the loudness (RMS) of the first and last 10 ms, as a hint.
    /// How the samples are read: AudioClip.GetData only works on Decompress On Load clips, and the hum is (rightly)
    /// Streaming. So the check copies the file to a temporary folder (Assets/_TWR_LoopCheckTemp), which AudioImportRules
    /// imports as plain PCM, reads that copy and deletes the folder again. The real asset and its .meta are never changed
    /// (no reimport, nothing new for git). Clips that are already Decompress On Load are read directly. Anything that fails
    /// gives a clear "can't verify" warning. Results are cached per file + import settings for the editor session.
    /// </summary>
    public static class AudioLoopCheck
    {
        const float SilenceThreshold = 0.001f;     // -60 dBFS
        const float MaxSilenceMs = 5f;
        const float MaxSeamJump = 0.05f;
        const float RmsWindowSeconds = 0.01f;      // 10 ms

        public class Result
        {
            public string id, path, clipName;
            public bool measured;
            public string reason;                  // why it couldn't be measured
            public float seconds;
            public int channels, frequency;
            public float leadingMs, trailingMs;
            public float[] seamJump;               // per channel
            public float rmsStartDb, rmsEndDb;
            public bool HasWarning => !measured || leadingMs > MaxSilenceMs || trailingMs > MaxSilenceMs || MaxJump > MaxSeamJump;

            public float MaxJump
            {
                get
                {
                    float max = 0f;
                    if (seamJump != null) foreach (float j in seamJump) max = Mathf.Max(max, j);
                    return max;
                }
            }
        }

        static readonly Dictionary<string, Result> cache = new Dictionary<string, Result>();

        // ---------- menu ----------

        [MenuItem("TWR/Audio/Check Loops")]
        public static void CheckLoopsMenu()
        {
            List<Result> results = CheckLibraryLoops(out string problem);
            if (problem != null)
            {
                Debug.LogWarning("[TWR] Loop check (D-228): " + problem);
                return;
            }
            int warnings = 0;
            foreach (Result result in results)
            {
                if (result.HasWarning) { warnings++; Debug.LogWarning(Describe(result)); }
                else Debug.Log(Describe(result));
            }
            if (results.Count == 0) Debug.LogWarning("[TWR] Loop check (D-228): no AudioLibrary entry has 'loop' ticked and a clip.");
            else if (warnings == 0) Debug.Log($"[TWR] Loop check OK: {results.Count} loop clip(s) (D-228).");
            else Debug.LogWarning($"[TWR] Loop check: {warnings} of {results.Count} loop clip(s) need a look (see above, D-228).");
        }

        /// For the audio validation: one line per loop clip in the report; problems also logged as [TWR] warnings.
        public static void AppendToReport(StringBuilder report)
        {
            List<Result> results = CheckLibraryLoops(out string problem);
            if (problem != null)
            {
                report.AppendLine("  loop check (D-228): not run - " + problem);
                return;
            }
            if (results.Count == 0) report.AppendLine("  loop check (D-228): no loop clips in the AudioLibrary");
            foreach (Result result in results)
            {
                report.AppendLine("  " + Describe(result).Replace("[TWR] ", ""));
                if (result.HasWarning) Debug.LogWarning(Describe(result));
            }
        }

        // ---------- measuring ----------

        /// Every clip of every library entry with loop ticked. problem = why nothing could be checked (else null).
        public static List<Result> CheckLibraryLoops(out string problem)
        {
            problem = null;
            var results = new List<Result>();
            var library = Resources.Load<AudioLibrary>(AudioLibrary.ResourcePath);
            if (library == null)
            {
                problem = "Resources/AudioLibrary not found; run TWR > Audio > Build Audio (A1).";
                return results;
            }
            if (library.entries == null) return results;
            foreach (AudioLibrary.Entry entry in library.entries)
            {
                if (entry == null || !entry.loop || entry.clips == null) continue;
                foreach (AudioClip clip in entry.clips)
                    if (clip != null) results.Add(Check(entry.id, clip));
            }
            return results;
        }

        static Result Check(string id, AudioClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            string key = path + "|" + AssetDatabase.GetAssetDependencyHash(path);
            if (cache.TryGetValue(key, out Result cached) && cached.measured) { cached.id = id; return cached; }

            var result = new Result { id = id, path = path, clipName = clip.name };
            if (string.IsNullOrEmpty(path))
                result.reason = "the clip is not an asset file";
            else if (TryReadSamples(clip, path, out float[] data, out int channels, out int frequency, out string reason))
                Measure(result, data, channels, frequency);
            else
                result.reason = reason;
            if (result.measured) cache[key] = result;
            return result;
        }

        static bool TryReadSamples(AudioClip clip, string path, out float[] data, out int channels, out int frequency, out string reason)
        {
            // 1. Already Decompress On Load: GetData works on the clip itself.
            if (clip.loadType == AudioClipLoadType.DecompressOnLoad && TryGetData(clip, out data, out reason))
            {
                channels = clip.channels;
                frequency = clip.frequency;
                return true;
            }

            // 2. Streaming / Compressed In Memory: read a temporary PCM copy (the real asset is not touched).
            string folder = AudioImportRules.LoopCheckTempFolder;
            string tempPath = folder + "/" + Path.GetFileName(path);
            data = null;
            channels = 0;
            frequency = 0;
            try
            {
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
                File.Copy(Path.GetFullPath(path), Path.GetFullPath(tempPath), true);
                AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);
                var copy = AssetDatabase.LoadAssetAtPath<AudioClip>(tempPath);
                if (copy == null)
                {
                    reason = "the temporary PCM copy could not be imported";
                    return false;
                }
                if (copy.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    reason = "the temporary copy did not import as Decompress On Load (is AudioImportRules compiled?)";
                    return false;
                }
                if (!TryGetData(copy, out data, out reason)) return false;
                channels = copy.channels;
                frequency = copy.frequency;
                return true;
            }
            catch (IOException e)
            {
                reason = "could not copy the file (" + e.Message + ")";
                return false;
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }

        static bool TryGetData(AudioClip clip, out float[] data, out string reason)
        {
            data = null;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                reason = $"the sample data is not loaded (state {clip.loadState})";
                return false;
            }
            if (clip.samples <= 0 || clip.channels <= 0)
            {
                reason = "the clip has no samples";
                return false;
            }
            data = new float[clip.samples * clip.channels]; // editor only: one big read, never at runtime
            if (!clip.GetData(data, 0))
            {
                reason = "AudioClip.GetData failed";
                data = null;
                return false;
            }
            reason = null;
            return true;
        }

        static void Measure(Result result, float[] data, int channels, int frequency)
        {
            int frames = data.Length / channels;
            result.channels = channels;
            result.frequency = frequency;
            result.seconds = frames / (float)frequency;

            int firstLoud = 0;
            while (firstLoud < frames && FramePeak(data, firstLoud, channels) < SilenceThreshold) firstLoud++;
            int lastLoud = frames - 1;
            while (lastLoud > firstLoud && FramePeak(data, lastLoud, channels) < SilenceThreshold) lastLoud--;
            result.leadingMs = firstLoud * 1000f / frequency;
            result.trailingMs = (frames - 1 - lastLoud) * 1000f / frequency;

            result.seamJump = new float[channels];
            for (int c = 0; c < channels; c++)
                result.seamJump[c] = Mathf.Abs(data[(frames - 1) * channels + c] - data[c]);

            int window = Mathf.Clamp(Mathf.RoundToInt(frequency * RmsWindowSeconds), 1, frames);
            result.rmsStartDb = ToDb(Rms(data, 0, window, channels));
            result.rmsEndDb = ToDb(Rms(data, frames - window, window, channels));
            result.measured = true;
        }

        static float FramePeak(float[] data, int frame, int channels)
        {
            float peak = 0f;
            for (int c = 0; c < channels; c++) peak = Mathf.Max(peak, Mathf.Abs(data[frame * channels + c]));
            return peak;
        }

        static float Rms(float[] data, int startFrame, int frameCount, int channels)
        {
            double sum = 0;
            int start = startFrame * channels, end = (startFrame + frameCount) * channels;
            for (int i = start; i < end; i++) sum += data[i] * (double)data[i];
            return (float)System.Math.Sqrt(sum / System.Math.Max(1, end - start));
        }

        static float ToDb(float linear) => 20f * Mathf.Log10(Mathf.Max(linear, 0.000001f));

        // ---------- text ----------

        public static string Describe(Result r)
        {
            string head = $"[TWR] Loop '{r.id}' ({Path.GetFileName(r.path)}";
            if (!r.measured)
                return $"{head}): can't verify the loop - {r.reason}. Check it by ear: play it with Loop on in the Inspector preview (D-228).";

            var text = new StringBuilder();
            text.Append($"{head}, {r.seconds:0.0} s, {r.channels} ch, {r.frequency} Hz): ");
            text.Append($"silence start {r.leadingMs:0.0} ms / end {r.trailingMs:0.0} ms, seam jump ");
            for (int c = 0; c < r.seamJump.Length; c++)
                text.Append((c > 0 ? " / " : "") + (r.seamJump.Length == 2 ? (c == 0 ? "L " : "R ") : $"ch{c + 1} ") + r.seamJump[c].ToString("0.000"));
            text.Append($", RMS first 10 ms {r.rmsStartDb:0.0} dBFS / last 10 ms {r.rmsEndDb:0.0} dBFS");
            if (!r.HasWarning)
            {
                text.Append(": OK");
                return text.ToString();
            }

            text.Append(": WARNING");
            if (r.leadingMs > MaxSilenceMs || r.trailingMs > MaxSilenceMs)
                text.Append($"\n    - more than {MaxSilenceMs:0} ms of silence (below -60 dBFS) at the {(r.leadingMs > MaxSilenceMs ? (r.trailingMs > MaxSilenceMs ? "start and end" : "start") : "end")}: " +
                            "the loop has an audible gap. Trim it in an audio editor (e.g. Audacity: zoom in, select the quiet part, Delete, " +
                            "export again under the same file name).");
            if (r.MaxJump > MaxSeamJump)
                text.Append($"\n    - the waveform jumps by {r.MaxJump:0.000} at the loop point (more than {MaxSeamJump}): an audible click. " +
                            "Crossfade the loop point in an audio editor (blend the last ~0.5 s into the start), or cut both ends at a zero crossing" +
                            (Mathf.Abs(r.rmsStartDb - r.rmsEndDb) > 3f ? $"; the start and end also differ by {Mathf.Abs(r.rmsStartDb - r.rmsEndDb):0.0} dB in loudness." : "."));
            return text.ToString();
        }
    }
}
