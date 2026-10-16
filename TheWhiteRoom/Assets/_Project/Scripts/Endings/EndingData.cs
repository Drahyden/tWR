using System;
using UnityEngine;
using UnityEngine.Localization;
using TWR.Run;

namespace TWR.Endings
{
    /// <summary>Which performance band an ending wants (D-028, D-152: Good = score 60+).</summary>
    public enum EndingPerformance
    {
        Any,
        Good,
        Poor
    }

    /// <summary>How an ending plays (D-199 scripted, D-196 Overtime is the interactive exception).</summary>
    public enum EndingPresentation
    {
        /// <summary>10-30 s in-engine scene, no control, one inner-voice line, fade, end screen (D-199).</summary>
        Scripted,
        /// <summary>The player keeps move/look/crouch/interact until something in the scene finishes it (D-196, D-207).</summary>
        Interactive
    }

    /// <summary>
    /// When an ending is picked (D-028, D-203, D-204). Path None = any path, performance Any = any score. The extra fields
    /// let a secret ending (D-025, D-208) ask for more without code changes: a minimum score, and/or endings that must
    /// already have been found in earlier runs.
    /// </summary>
    [Serializable]
    public struct EndingRule
    {
        [Tooltip("Escape or Discover (D-060). None = matches both paths.")]
        public RunPath path;
        [Tooltip("Good = score 60+, Poor = below 60 (D-152). Any = both.")]
        public EndingPerformance performance;
        [Tooltip("Extra condition for secret endings: score must be at least this (0 = no condition).")]
        [Range(0f, 100f)] public float minScore;
        [Tooltip("Extra condition for secret endings: these EndingData ids must already be saved as found (D-201).")]
        public string[] requiredFoundEndings;

        public bool Matches(RunResult result, IEndingsRecord record)
        {
            if (result == null) return false;
            if (path != RunPath.None && result.Path != path) return false;
            if (performance == EndingPerformance.Good && !result.IsGood) return false;
            if (performance == EndingPerformance.Poor && result.IsGood) return false;
            if (result.Score < minScore) return false;
            if (requiredFoundEndings != null)
                for (int i = 0; i < requiredFoundEndings.Length; i++)
                {
                    string id = requiredFoundEndings[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (record == null || !record.HasFound(id)) return false;
                }
            return true;
        }

        /// <summary>True if this rule covers the path x performance cell (ignoring the extra secret conditions).</summary>
        public bool CoversCell(RunPath cellPath, bool cellGood)
        {
            if (path != RunPath.None && path != cellPath) return false;
            if (performance == EndingPerformance.Good && !cellGood) return false;
            if (performance == EndingPerformance.Poor && cellGood) return false;
            return true;
        }

        public override string ToString() =>
            $"{(path == RunPath.None ? "any path" : path.ToString())} + {performance}" +
            (minScore > 0f ? $", score >= {minScore:0}" : "") +
            (requiredFoundEndings != null && requiredFoundEndings.Length > 0 ? ", needs " + string.Join("/", requiredFoundEndings) : "");
    }

    /// <summary>
    /// One ending as data (D-203): a stable id (saved, D-212), a localized title (D-084), the inner-voice line's subtitle
    /// key (D-199), a replaceable placeholder graphic for the end screen (D-092, D-200), its own content scene (D-199,
    /// D-210), how it is presented and how it fades out, and its selection rule + priority (D-204). isSecret hides it on
    /// the end screen until found (D-208). A secret 5th ending = a new asset with a higher priority added to the
    /// EndingCatalog; no code change. Create via: Create > TWR > Endings > Ending Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Ending_", menuName = "TWR/Endings/Ending Data")]
    public class EndingData : ScriptableObject
    {
        [Header("Identity - D-203, D-212")]
        [Tooltip("Stable id written to the save file. Never rename after release, e.g. 'ending.clock_out'.")]
        public string id;
        [Tooltip("Ending title on the end-of-run screen (table 'UI').")]
        public LocalizedString title = new LocalizedString();

        [Header("Presentation - D-196, D-199, D-200")]
        public EndingPresentation presentation = EndingPresentation.Scripted;
        [Tooltip("Content scene loaded additively next to Core (must be in Build Settings, D-210).")]
        public string sceneName;
        [Tooltip("Subtitle key of the one placeholder inner-voice line (table 'Subtitles', D-199). Empty = no line.")]
        public LocalizedString voiceLine = new LocalizedString();
        [Tooltip("Replaceable placeholder image on the end-of-run screen (D-092). Locked = drawn as a black silhouette.")]
        public Sprite graphic;
        [Tooltip("Colour the screen fades to before the end screen: white for scripted endings (D-199), black for Overtime (D-196).")]
        public Color finishFadeColour = Color.white;
        [Tooltip("Length of that fade, seconds.")]
        [Min(0f)] public float finishFadeSeconds = 1.5f;

        [Header("Selection - D-028, D-204, D-208")]
        [Tooltip("Secret endings get no silhouette on the end screen until found and don't count in 'X / 4' (D-208).")]
        public bool isSecret;
        [Tooltip("Highest priority wins when several endings match. Secret endings should be higher than the launch four.")]
        public int priority;
        public EndingRule rule;

        public bool Matches(RunResult result, IEndingsRecord record) => rule.Matches(result, record);

        /// <summary>Id for logs ("(no id)" if empty).</summary>
        public string DisplayId => string.IsNullOrEmpty(id) ? "(no id) " + name : id;
    }
}
