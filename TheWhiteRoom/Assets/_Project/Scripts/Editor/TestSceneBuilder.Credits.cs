using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using TWR.Audio;
using TWR.UI;

namespace TWR.EditorTools
{
    /// <summary>
    /// Credits (D-229). Menu "TWR/Credits/Build Credits" (also run by the main menu builder and before every player build):
    /// - reads docs/audio/LICENSES.md (D-218) and writes one credit line per row with Attribution required = y into the
    ///   generated section of Data/Credits/CreditsData.asset. The line is the "Credit: ..." sentence from the Notes column
    ///   (up to its first full stop); without one it is pieced together from name, source and license, with a warning.
    ///   Fixed sections (developer, tools) are created once and then left to you.
    /// - a row with attribution = y that is still TO VERIFY / UNKNOWN is an error and FAILS the player build, so a CC BY
    ///   asset never ships without a checked credit.
    /// - "TWR/Credits/Validate Credits" (also part of the R5 validation): the asset matches the log, heading keys exist
    ///   (D-084), and the main menu has a wired Credits screen.
    /// The main menu's Credits panel itself (scroll view, templates, Back) is built by BuildCreditsPanel below.
    /// </summary>
    public static partial class TestSceneBuilder
    {
        const string CreditsFolder = Root + "/Data/Credits";
        const string CreditsDataPath = CreditsFolder + "/CreditsData.asset";
        const string CreditsAudioHeadingKey = "credits.heading.audio";

        /// UI keys used by the Credits button and screen (D-084, D-229).
        static readonly string[] CreditsUiKeys =
            { "menu.credits", "credits.title", "credits.heading.created_by", "credits.heading.made_with", CreditsAudioHeadingKey };

        static string LicenseLogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/audio/LICENSES.md"));

        static readonly Regex CreditSentence = new Regex(@"Credit:\s*(.+?)\.(?=\s|$)");

        struct AttributionRow
        {
            public string file;
            public string line;
            public bool unverified;   // TO VERIFY / UNKNOWN: blocks a build
            public bool composed;     // no "Credit:" in Notes: line pieced together (warning)
        }

        [MenuItem("TWR/Credits/Build Credits")]
        public static void BuildCreditsMenu()
        {
            ProjectSetup.Run(() =>
            {
                if (BuildCreditsData(out _, out int blockers) && blockers == 0)
                    Debug.Log("[TWR] Credits built from docs/audio/LICENSES.md (D-229): " + CreditsDataPath);
            });
        }

        [MenuItem("TWR/Credits/Validate Credits")]
        public static void ValidateCreditsMenu()
        {
            var report = new StringBuilder();
            report.AppendLine("[TWR] Credits (D-229)");
            if (ValidateCredits(report)) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());
        }

        /// <summary>
        /// Creates or updates CreditsData from the license log. Returns false (data may be null) if the log can't be read.
        /// blockers = attribution rows that are not verified yet; the build step refuses to build while any exist.
        /// </summary>
        internal static bool BuildCreditsData(out CreditsData data, out int blockers)
        {
            data = null;
            blockers = 0;
            if (!ReadAttributionRows(out List<AttributionRow> rows, out string error))
            {
                Debug.LogError("[TWR] Credits (D-229): " + error);
                return false;
            }

            foreach (AttributionRow row in rows)
            {
                if (row.unverified)
                {
                    blockers++;
                    Debug.LogError($"[TWR] Credits (D-229): '{row.file}' needs attribution but its license is still TO VERIFY / " +
                                   "UNKNOWN in docs/audio/LICENSES.md. Player builds are blocked until it is checked.");
                }
                else if (row.composed)
                {
                    Debug.LogWarning($"[TWR] Credits (D-229): '{row.file}' has no 'Credit: ...' sentence in its Notes; using " +
                                     $"\"{row.line}\". Add one to docs/audio/LICENSES.md to control the exact wording.");
                }
            }

            EnsureFolder(CreditsFolder);
            data = AssetDatabase.LoadAssetAtPath<CreditsData>(CreditsDataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<CreditsData>();
                data.fixedSections = new[]
                {
                    // Placeholder name: change it in the Inspector (Data/Credits/CreditsData), it is never overwritten.
                    NewSection("credits.heading.created_by", "Drahyden"),
                    NewSection("credits.heading.made_with", "Unity", "SUNO", "FADR"),
                };
                AssetDatabase.CreateAsset(data, CreditsDataPath);
                Debug.Log("[TWR] Created " + CreditsDataPath + ". Edit the developer and tool credits there (D-229).", data);
            }

            string[] lines = rows.Select(r => r.line).Distinct().ToArray();
            CreditsData.Section[] generated = lines.Length > 0 ? new[] { NewSection(CreditsAudioHeadingKey, lines) } : new CreditsData.Section[0];
            if (!SameSections(data.generatedSections, generated))
            {
                data.generatedSections = generated;
                EditorUtility.SetDirty(data);
                Debug.Log($"[TWR] Credits: {lines.Length} attribution line(s) written from docs/audio/LICENSES.md (D-229).", data);
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        static CreditsData.Section NewSection(string headingKey, params string[] lines) =>
            new CreditsData.Section { heading = new LocalizedString("UI", headingKey), lines = lines };

        static bool SameSections(CreditsData.Section[] a, CreditsData.Section[] b)
        {
            if (a == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null || a[i].heading == null || a[i].heading.TableEntryReference.Key != b[i].heading.TableEntryReference.Key) return false;
                if (a[i].lines == null || !a[i].lines.SequenceEqual(b[i].lines)) return false;
            }
            return true;
        }

        /// Rows of the license log with Attribution required = y, columns found by their header names.
        static bool ReadAttributionRows(out List<AttributionRow> rows, out string error)
        {
            rows = new List<AttributionRow>();
            error = null;
            if (!File.Exists(LicenseLogPath))
            {
                error = "license log not found at " + LicenseLogPath + " (D-218).";
                return false;
            }

            string[] header = null;
            int file = -1, source = -1, original = -1, license = -1, attribution = -1, notes = -1;
            foreach (string raw in File.ReadAllLines(LicenseLogPath))
            {
                string line = raw.Trim();
                if (!line.StartsWith("|") || line.StartsWith("|---")) continue;
                string[] cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (header == null)
                {
                    header = cells;
                    file = Column(header, "File");
                    source = Column(header, "Source");
                    original = Column(header, "Original");
                    license = Column(header, "License");
                    attribution = Column(header, "Attribution");
                    notes = Column(header, "Notes");
                    if (attribution < 0 || notes < 0)
                    {
                        error = "docs/audio/LICENSES.md has no 'Attribution required' or 'Notes' column.";
                        return false;
                    }
                    continue;
                }
                if (attribution >= cells.Length || !cells[attribution].StartsWith("y", StringComparison.OrdinalIgnoreCase)) continue;

                var row = new AttributionRow
                {
                    file = Plain(Cell(cells, file)),
                    unverified = line.Contains("TO VERIFY") || line.Contains("UNKNOWN"),
                };
                Match credit = CreditSentence.Match(Cell(cells, notes));
                if (credit.Success)
                {
                    row.line = Plain(credit.Groups[1].Value);
                }
                else
                {
                    row.composed = true;
                    row.line = string.Join(" - ", new[] { Cell(cells, original), Cell(cells, source), Cell(cells, license) }
                                                  .Select(Plain).Where(s => s.Length > 0 && s != "—"));
                }
                rows.Add(row);
            }
            if (header == null)
            {
                error = "docs/audio/LICENSES.md has no table.";
                return false;
            }
            return true;
        }

        static int Column(string[] header, string startsWith) =>
            Array.FindIndex(header, h => h.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase));

        static string Cell(string[] cells, int index) => index >= 0 && index < cells.Length ? cells[index] : string.Empty;

        /// Markdown removed: backticks and bold markers.
        static string Plain(string text) => text.Replace("`", "").Replace("**", "").Trim();

        // ---------- validation ----------

        /// D-229: CreditsData exists and matches the license log, no attribution row is unverified, heading keys exist.
        static bool ValidateCredits(StringBuilder report)
        {
            bool ok = true;
            var data = AssetDatabase.LoadAssetAtPath<CreditsData>(CreditsDataPath);
            ok &= Check(report, "CreditsData asset (D-229)", data != null, data != null ? "" : CreditsDataPath + " missing (run TWR > Credits > Build Credits)");

            bool read = ReadAttributionRows(out List<AttributionRow> rows, out string error);
            ok &= Check(report, "license log readable (D-218)", read, error);
            if (read)
            {
                var unverified = rows.Where(r => r.unverified).Select(r => r.file).ToList();
                ok &= Check(report, "attribution rows verified (no TO VERIFY / UNKNOWN)", unverified.Count == 0,
                            unverified.Count == 0 ? $"{rows.Count} row(s) with attribution = y" : "blocks builds: " + string.Join(", ", unverified));
                if (data != null)
                {
                    var expected = rows.Select(r => r.line).Distinct().ToList();
                    var actual = (data.generatedSections ?? new CreditsData.Section[0])
                                 .Where(s => s != null && s.lines != null).SelectMany(s => s.lines).ToList();
                    var missing = expected.Except(actual).ToList();
                    var extra = actual.Except(expected).ToList();
                    ok &= Check(report, "credits match docs/audio/LICENSES.md", missing.Count == 0 && extra.Count == 0,
                                missing.Count + extra.Count == 0 ? "" :
                                $"missing {missing.Count}, outdated {extra.Count} (run TWR > Credits > Build Credits; builds do it automatically)");
                }
            }

            if (data != null)
            {
                var sections = (data.fixedSections ?? new CreditsData.Section[0]).Concat(data.generatedSections ?? new CreditsData.Section[0]);
                var badHeadings = sections.Where(s => s != null && !KeyExists(s.heading)).Select(s => KeyName(s?.heading)).ToList();
                ok &= Check(report, "credits headings localized (table 'UI', D-084)", badHeadings.Count == 0,
                            badHeadings.Count == 0 ? "" : "missing: " + string.Join(", ", badHeadings) + " (run TWR > Setup Project)");
            }

            var missingKeys = CreditsUiKeys.Where(k => !ProjectSetup.TableHasKey("UI", k)).ToList();
            ok &= Check(report, $"{CreditsUiKeys.Length} credits keys in table 'UI' (D-084)", missingKeys.Count == 0,
                        missingKeys.Count == 0 ? "" : "missing: " + string.Join(", ", missingKeys) + " (run TWR > Setup Project)");
            return ok;
        }

        // ---------- main menu panel ----------

        /// The Credits panel of the main menu (D-229): title, a mouse-wheel scroll view filled at runtime by CreditsScreen
        /// from CreditsData (two inactive label templates), and a Back button. Starts hidden.
        static GameObject BuildCreditsPanel(Transform canvas, CreditsData data, out Button back)
        {
            Color dark = new Color(0.1f, 0.1f, 0.1f), grey = new Color(0.3f, 0.3f, 0.3f);
            GameObject panel = StretchedPanel("Credits Panel (D-229)", canvas);
            UiText("Credits Title", panel.transform, new Vector2(0f, 400f), new Vector2(1000f, 80f), 56f,
                   new LocalizedString("UI", "credits.title"), dark, TextAlignmentOptions.Center);

            RectTransform view = UiRect("Credits Scroll View", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                        new Vector2(0f, -10f), new Vector2(1300f, 640f));
            view.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // invisible, catches wheel and drag
            view.gameObject.AddComponent<RectMask2D>();

            RectTransform content = UiRect("Content", view, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            StretchRect(content, new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(40, 40, 20, 260); // space at the end so the last line scrolls up into view
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TMP_Text headingTemplate = UiText("Heading Template", content, Vector2.zero, new Vector2(1200f, 56f), 40f, null,
                                              dark, TextAlignmentOptions.Center);
            headingTemplate.fontStyle = FontStyles.Bold;
            headingTemplate.text = "Heading";
            headingTemplate.gameObject.SetActive(false);
            TMP_Text lineTemplate = UiText("Line Template", content, Vector2.zero, new Vector2(1200f, 40f), 30f, null,
                                           grey, TextAlignmentOptions.Center);
            lineTemplate.text = "Credit line";
            lineTemplate.gameObject.SetActive(false);

            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 12f;

            var screen = view.gameObject.AddComponent<CreditsScreen>();
            Assign(screen, "data", data);
            Assign(screen, "headingTemplate", headingTemplate);
            Assign(screen, "lineTemplate", lineTemplate);

            back = UiButton("Credits Back Button", panel.transform, new Vector2(0f, -400f), new Vector2(420f, 86f),
                            new LocalizedString("UI", "menu.back"), AudioIds.UiBack);
            panel.SetActive(false);
            return panel;
        }
    }

    /// <summary>
    /// Before every player build: regenerates the credits from docs/audio/LICENSES.md and stops the build if an asset that
    /// needs attribution is not verified yet (D-229: credits are required before any build that ships a CC BY asset).
    /// </summary>
    class CreditsBuildStep : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!TestSceneBuilder.BuildCreditsData(out CreditsData data, out int blockers) || data == null)
                throw new BuildFailedException("[TWR] Credits (D-229): could not read docs/audio/LICENSES.md (see the Console).");
            if (blockers > 0)
                throw new BuildFailedException($"[TWR] Credits (D-229): {blockers} asset(s) need attribution but are not verified in docs/audio/LICENSES.md.");
        }
    }
}
