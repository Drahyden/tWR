using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using TWR.Audio;
using TWR.Endings;
using TWR.Enemies;

namespace TWR.EditorTools
{
    /// <summary>
    /// [TWR] music validation (D-230 to D-235), menu "TWR/Audio/Validate Music (A2)"; also part of "TWR/Audio/Validate
    /// Audio (A1)" (so every builder that validates audio checks music too) and run at the end of Build Music (A2).
    /// Reads, never edits. Checks:
    /// - naming: every file in Audio/Music is mus_&lt;state&gt;_a[_&lt;stem&gt;] with a known state (error otherwise);
    /// - every state has stems and every ending its stinger file (error), other variants are noted;
    /// - Resources/MusicLibrary and one MusicData per state, each stem with a clip, no more than MusicData.MaxStems,
    ///   a drums stem where a rule needs one (warning), the menu stem in the Lobby track (error), a boss track for every
    ///   BossData.bossId (error);
    /// - stem lengths inside a track are identical (warning, with samples and seconds: they would drift apart, D-231);
    /// - import settings: Streaming, Vorbis 70, Preserve sample rate (AudioImportRules.RuleProblems, error);
    /// - a LICENSES.md row per file that is not TO VERIFY / UNKNOWN (LicenseLogCheck, error, D-218, D-227);
    /// - every music file is used by a MusicData or an EndingData and registered in the AudioLibrary; the music
    ///   routing target (MusicState) exists; Overtime has no stinger assigned (D-221, D-234).
    /// Warnings are also logged on their own as [TWR] warnings.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        [MenuItem("TWR/Audio/Validate Music (A2)")]
        public static void ValidateMusicMenu()
        {
            var report = new StringBuilder();
            report.AppendLine("[TWR] Music A2 (D-230 to D-235)");
            bool ok = ValidateMusic(report);
            if (ok) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
        }

        static bool ValidateMusic(StringBuilder report)
        {
            report.AppendLine("  -- music (A2, D-230 to D-235) --");
            var naming = new List<string>();
            List<MusicFile> files = DiscoverMusicFiles(naming);
            bool ok = Check(report, $"music file names mus_<state>_a[_<stem>] ({files.Count} file(s) in {MusicFilesFolder})", naming.Count == 0,
                            naming.Count == 0 ? "" : "\n      " + string.Join("\n      ", naming));

            List<string> otherVariants = files.Where(f => f.variant != MusicVariant).Select(f => f.fileName).ToList();
            if (otherVariants.Count > 0) report.AppendLine($"    note: only variant '{MusicVariant}' is used; not used: {string.Join(", ", otherVariants)}");
            List<string> vocals = files.Where(f => f.stem == "vocals").Select(f => f.fileName).ToList();
            if (vocals.Count > 0) report.AppendLine($"    note: vocals stems must be wordless (choir, D-231): {string.Join(", ", vocals)}");

            ok &= ValidateMusicFiles(report, files);
            ok &= ValidateMusicAssets(report, files);
            ok &= ValidateMusicEndings(report, files);
            ok &= ValidateMusicRegistration(report, files);
            return ok;
        }

        // ---------- files ----------

        static bool ValidateMusicFiles(StringBuilder report, List<MusicFile> files)
        {
            bool ok = true;
            var missing = new List<string>();
            foreach (MusicTrackSpec spec in MusicTrackSpecs)
            {
                List<MusicFile> stems = StemsOf(spec.key, files);
                if (stems.Count == 0) { missing.Add($"mus_{spec.key}_{MusicVariant}_<stem> ({spec.state})"); continue; }
                if (stems.Count == 1 && stems[0].stem == null)
                    report.AppendLine($"    note: mus_{spec.key}_{MusicVariant} has no stems, its full mix is used as one layer (no layering)");
                CheckStemLengths(report, "mus_" + spec.key + "_" + MusicVariant, stems);
            }
            foreach (MusicEndingSpec spec in MusicEndingSpecs)
                if (EndingFileOf(spec.key, files) == null) missing.Add($"mus_{spec.key}_{MusicVariant} (ending)");
            ok &= Check(report, "every music state has stems and every ending its stinger (D-231, D-234)", missing.Count == 0,
                        missing.Count == 0 ? "" : "missing " + string.Join(", ", missing));

            var importProblems = new List<string>();
            foreach (MusicFile file in files)
            {
                List<string> problems = AudioImportRules.RuleProblems(file.path);
                if (problems.Count > 0) importProblems.Add($"{file.fileName}: {string.Join(", ", problems)}");
            }
            ok &= Check(report, "music import settings: Streaming, Vorbis 70, Preserve sample rate (D-228)", importProblems.Count == 0,
                        importProblems.Count == 0 ? "" : "\n      " + string.Join("\n      ", importProblems) + "\n      (run TWR > Audio > Build Music (A2))");

            List<string> licence = LicenseLogCheck.ProblemsFor(files.Select(f => f.fileName), out bool logFound);
            if (!logFound) report.AppendLine("    note: docs/audio/LICENSES.md not found; music licences not checked (D-218).");
            else ok &= Check(report, "every music file has a verified LICENSES.md row (D-218, D-227)", licence.Count == 0,
                             licence.Count == 0 ? "" : string.Join("; ", licence));
            return ok;
        }

        /// D-231: stems of one track must have the same length, or they drift apart a little more on every loop.
        static void CheckStemLengths(StringBuilder report, string track, List<MusicFile> stems)
        {
            List<MusicFile> withClips = stems.Where(s => s.clip != null).ToList();
            if (withClips.Count < 2) return;
            int minSamples = withClips.Min(s => s.clip.samples), maxSamples = withClips.Max(s => s.clip.samples);
            bool sameRate = withClips.All(s => s.clip.frequency == withClips[0].clip.frequency);
            if (minSamples == maxSamples && sameRate)
            {
                report.AppendLine($"  {track} stem lengths: OK - {withClips.Count} stems, {minSamples} samples ({Seconds(withClips[0].clip)})");
                return;
            }
            string lengths = string.Join(", ", withClips.Select(s => $"{StemName(s)} {s.clip.samples} samples @ {s.clip.frequency} Hz ({Seconds(s.clip)})"));
            int diff = maxSamples - minSamples;
            float diffMs = diff * 1000f / Mathf.Max(1, withClips[0].clip.frequency);
            string text = $"{track}: stem lengths differ by {diff} samples ({diffMs:0.#} ms){(sameRate ? "" : ", sample rates differ")}: {lengths}. " +
                          "They start together but drift apart by this much per loop; trim them to the same length in FADR / an editor (D-231).";
            report.AppendLine("    warning: " + text);
            Debug.LogWarning("[TWR] Music: " + text);
        }

        static string Seconds(AudioClip clip) => clip.frequency > 0 ? $"{(double)clip.samples / clip.frequency:0.000} s" : "? s";

        // ---------- MusicLibrary + MusicData ----------

        static bool ValidateMusicAssets(StringBuilder report, List<MusicFile> files)
        {
            var library = AssetDatabase.LoadAssetAtPath<MusicLibrary>(MusicLibraryPath);
            if (!Check(report, "Resources/MusicLibrary (D-235)", library != null, library != null ? "" : "MISSING: run TWR > Audio > Build Music (A2)"))
                return false;

            bool ok = true;
            var slots = new (string name, MusicData data, MusicTrackSpec spec)[]
            {
                ("lobby", library.lobby, MusicTrackSpecs[0]),
                ("exploration", library.exploration, MusicTrackSpecs[1]),
                ("puzzle", library.puzzle, MusicTrackSpecs[2]),
                ("combat", library.combat, MusicTrackSpecs[3]),
            };
            foreach (var slot in slots)
            {
                ok &= Check(report, $"  MusicLibrary.{slot.name}", slot.data != null, slot.data != null ? slot.data.name : "EMPTY: run Build Music (A2)");
                if (slot.data != null) ok &= ValidateMusicData(report, slot.data, slot.spec, files);
            }

            foreach (MusicTrackSpec spec in MusicTrackSpecs.Where(s => s.state == MusicStateKind.Boss))
            {
                MusicData boss = library.bosses?.FirstOrDefault(b => b != null && b.bossId == spec.bossId);
                ok &= Check(report, $"  MusicLibrary boss track '{spec.bossId}'", boss != null, boss != null ? boss.name : "MISSING: run Build Music (A2)");
                if (boss != null) ok &= ValidateMusicData(report, boss, spec, files);
            }

            // Every boss in the game has a track (D-213): BossData.bossId must match a MusicData.bossId.
            var unmatched = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BossData)))
            {
                var data = AssetDatabase.LoadAssetAtPath<BossData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && (library.bosses == null || library.bosses.All(b => b == null || b.bossId != data.bossId))) unmatched.Add($"{data.name} ('{data.bossId}')");
            }
            ok &= Check(report, "  every BossData.bossId has a boss track (D-213)", unmatched.Count == 0,
                        unmatched.Count == 0 ? "" : string.Join(", ", unmatched));

            string menuScene = library.mainMenuSceneName;
            bool menuInBuild = EditorBuildSettings.scenes.Any(s => s.enabled && System.IO.Path.GetFileNameWithoutExtension(s.path) == menuScene);
            ok &= Check(report, $"  main menu scene '{menuScene}' is in Build Settings (D-230)", menuInBuild,
                        menuInBuild ? "" : "set MusicLibrary.mainMenuSceneName to the menu scene, or run TWR > Run > Build Endings + Main Menu");
            return ok;
        }

        static bool ValidateMusicData(StringBuilder report, MusicData data, MusicTrackSpec spec, List<MusicFile> files)
        {
            var problems = new List<string>();
            var warnings = new List<string>();
            if (data.state != spec.state) problems.Add($"state is {data.state}, expected {spec.state}");
            if (data.stems == null || data.stems.Length == 0) problems.Add("no stems");
            else
            {
                if (data.stems.Length > MusicData.MaxStems) problems.Add($"{data.stems.Length} stems, only {MusicData.MaxStems} play");
                foreach (MusicData.Stem stem in data.stems)
                    if (stem == null || stem.clip == null) problems.Add($"stem '{stem?.name}' has no clip");
                var fileStems = StemsOf(spec.key, files).Select(StemName).ToList();
                var dataStems = data.stems.Where(s => s != null).Select(s => s.name).ToList();
                if (!fileStems.OrderBy(s => s).SequenceEqual(dataStems.OrderBy(s => s)))
                    problems.Add($"stems [{string.Join(", ", dataStems)}] don't match the files [{string.Join(", ", fileStems)}] (run Build Music (A2))");

                bool hasDrums = data.stems.Any(s => s != null && s.name == data.drumsStem);
                bool needsDrums = spec.state == MusicStateKind.Puzzle || spec.state == MusicStateKind.Combat || spec.state == MusicStateKind.Boss;
                if (needsDrums && !hasDrums) warnings.Add($"no '{data.drumsStem}' stem: the D-232 drums rule does nothing");
                if (spec.state == MusicStateKind.Lobby && data.stems.All(s => s == null || s.name != data.menuStem))
                    problems.Add($"menu stem '{data.menuStem}' not found: the main menu would be silent (D-230)");
                if (data.stems.All(s => s == null || !s.onAtEntry)) warnings.Add("no stem is on at entry: the state starts silent");
                if (spec.state == MusicStateKind.Exploration && hasDrums && data.stems.First(s => s != null && s.name == data.drumsStem).onAtEntry)
                    warnings.Add("drums are on in Exploration (D-232: all but drums)");
            }
            if (data.layerFadeSeconds <= 0f || data.crossfadeSeconds <= 0f) warnings.Add("a fade time is 0 (hard cuts)");
            if (spec.state == MusicStateKind.Combat && data.drumsOutDelaySeconds < data.layerFadeSeconds)
                warnings.Add($"drumsOutDelaySeconds {data.drumsOutDelaySeconds:0.#} < layerFadeSeconds {data.layerFadeSeconds:0.#}: the drums don't finish fading before the state changes");

            foreach (string w in warnings)
            {
                report.AppendLine($"    warning: {data.name}: {w}");
                Debug.LogWarning($"[TWR] Music: {data.name}: {w} (D-232).", data);
            }
            string summary = data.stems == null ? "" : string.Join(", ", data.stems.Where(s => s != null).Select(s => s.name + (s.onAtEntry ? "" : " (later)")));
            return Check(report, $"    {data.name} ({spec.state}): {summary}", problems.Count == 0, string.Join("; ", problems));
        }

        // ---------- endings ----------

        static bool ValidateMusicEndings(StringBuilder report, List<MusicFile> files)
        {
            bool ok = true;
            foreach (MusicEndingSpec spec in MusicEndingSpecs)
            {
                EndingData ending = LoadEndingData(spec.endingAsset);
                if (ending == null)
                {
                    report.AppendLine($"    note: {spec.endingAsset} not found; its stinger is checked once the endings are built.");
                    continue;
                }
                bool scripted = ending.presentation == EndingPresentation.Scripted;
                if (spec.plays && scripted)
                    ok &= Check(report, $"  {spec.endingAsset} stinger (D-234)", ending.music != null,
                                ending.music != null ? ending.music.name : "EMPTY: run Build Music (A2) or drag mus_" + spec.key + "_a into EndingData.music");
                else if (ending.music != null)
                {
                    string text = $"{spec.endingAsset} has a stinger ({ending.music.name}) but is {(scripted ? "marked not to play" : "interactive")}: it is never played (Overtime stays without music, D-221). Clear EndingData.music.";
                    report.AppendLine("    warning: " + text);
                    Debug.LogWarning("[TWR] Music: " + text, ending);
                }
                else report.AppendLine($"  {spec.endingAsset}: no stinger (D-221, D-234): OK");
            }

            // Any other ending (e.g. a secret 5th) that is scripted and has no stinger: a hint.
            var catalog = AssetDatabase.LoadAssetAtPath<EndingCatalog>(EndingCatalogPath);
            if (catalog != null && catalog.endings != null)
                foreach (EndingData e in catalog.endings)
                    if (e != null && e.presentation == EndingPresentation.Scripted && e.music == null &&
                        MusicEndingSpecs.All(s => s.endingAsset != e.name))
                        report.AppendLine($"    note: ending '{e.DisplayId}' has no stinger (EndingData.music); it plays without music.");
            return ok;
        }

        // ---------- registration + routing ----------

        static bool ValidateMusicRegistration(StringBuilder report, List<MusicFile> files)
        {
            // Every file is used by a MusicData / EndingData (or is the reserved Overtime file) and is in the AudioLibrary.
            var used = new HashSet<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(MusicData)))
            {
                var data = AssetDatabase.LoadAssetAtPath<MusicData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data?.stems != null) foreach (MusicData.Stem s in data.stems) if (s?.clip != null) used.Add(s.clip);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(EndingData)))
            {
                var e = AssetDatabase.LoadAssetAtPath<EndingData>(AssetDatabase.GUIDToAssetPath(guid));
                if (e != null && e.music != null) used.Add(e.music);
            }
            var reservedKeys = new HashSet<string>(MusicEndingSpecs.Where(s => !s.plays).Select(s => s.key));
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AudioLibraryPath);
            var registered = new HashSet<AudioClip>();
            if (library?.entries != null)
                foreach (AudioLibrary.Entry entry in library.entries)
                    if (entry?.clips != null && AudioIds.Music.Contains(entry.id))
                        foreach (AudioClip c in entry.clips) if (c != null) registered.Add(c);

            var unused = new List<string>();
            var unregistered = new List<string>();
            foreach (MusicFile f in files)
            {
                if (f.clip == null) continue;
                bool usedHere = used.Contains(f.clip) || (reservedKeys.Contains(f.key) && f.stem == null) || f.variant != MusicVariant;
                if (!usedHere) unused.Add(f.fileName);
                if (f.variant == MusicVariant && !registered.Contains(f.clip)) unregistered.Add(f.fileName);
            }
            bool ok = Check(report, "every music file is played by a MusicData / EndingData (D-233, D-234)", unused.Count == 0,
                            unused.Count == 0 ? "" : string.Join(", ", unused) + " (run Build Music (A2))");
            ok &= Check(report, "every music file is registered in the AudioLibrary (D-228, D-235)", unregistered.Count == 0,
                        unregistered.Count == 0 ? "" : string.Join(", ", unregistered) + " (run Build Music (A2))");
            if (reservedKeys.Count > 0)
                report.AppendLine("    note: registered but not played (D-221, D-234): " + string.Join(", ", reservedKeys.Select(k => "mus_" + k + "_a")));

            // Routing: the director sends every music source to MusicState (D-219, D-233).
            var routing = AssetDatabase.LoadAssetAtPath<AudioRoutingSettings>(AudioRoutingPath);
            bool routed = routing != null && routing.GetStateGroup(AudioBus.Music) != null;
            ok &= Check(report, "music is routed to MusicState (D-219, D-233)", routed,
                        routed ? "" : "AudioRoutingSettings has no MusicState: music would play on 'Music' or unrouted (see the TWR_Mixer steps; run Build Audio (A1))");
            report.AppendLine("    note: music stems are not seam-checked by Check Loops (long files); listen to the loop point in Play mode.");
            return ok;
        }
    }
}
