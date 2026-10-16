using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using TWR.Endings;
using TWR.Run;

namespace TWR.UI
{
    /// <summary>
    /// The end-of-run screen (D-092, D-200, D-208, D-209). Shows the ending's title and replaceable placeholder graphic,
    /// the path, the score with its kill and optional-puzzle breakdown and Good/Poor (D-152), the run time (D-206), the
    /// seed as 8 hex characters (D-153) and "Endings found: X / 4" with one slot per launch ending (unseen = black
    /// silhouette) plus found secret endings (unfound secrets are not shown, D-208). Buttons New run and Main menu raise
    /// events; the EndingSequence acts on them. Mouse-driven, Esc does nothing here (D-209). Every text is localized
    /// (table 'UI', D-084): values are filled into localized patterns like "Run time: {0}". Built once per screen
    /// (allocations are fine here); hidden it costs nothing.
    /// </summary>
    public class EndOfRunScreen : MonoBehaviour
    {
        [SerializeField] Canvas canvas;
        [SerializeField] CanvasGroup group;

        [Header("Labels")]
        [SerializeField] TMP_Text titleLabel;
        [SerializeField] Image graphic;
        [SerializeField] TMP_Text pathLabel;
        [SerializeField] TMP_Text scoreLabel;
        [SerializeField] TMP_Text killsLabel;
        [SerializeField] TMP_Text puzzlesLabel;
        [SerializeField] TMP_Text timeLabel;
        [SerializeField] TMP_Text seedLabel;
        [SerializeField] TMP_Text endingsFoundLabel;

        [Header("Ending slots - D-200, D-208")]
        [Tooltip("Row the slots live in (a layout group places them).")]
        [SerializeField] RectTransform slotRow;
        [Tooltip("First slot: an Image with a TMP label child. Copied for every further slot.")]
        [SerializeField] Image slotTemplate;
        [SerializeField] Color lockedColour = new Color(0.05f, 0.05f, 0.05f, 1f);
        [SerializeField] Color placeholderColour = new Color(0.75f, 0.75f, 0.75f, 1f);

        [Header("Buttons")]
        [SerializeField] Button newRunButton;
        [SerializeField] Button mainMenuButton;

        [Header("Localized patterns (table 'UI')")]
        [SerializeField] LocalizedString pathEscape = new LocalizedString("UI", "endscreen.path.escape");
        [SerializeField] LocalizedString pathDiscover = new LocalizedString("UI", "endscreen.path.discover");
        [SerializeField] LocalizedString scoreGood = new LocalizedString("UI", "endscreen.score.good");
        [SerializeField] LocalizedString scorePoor = new LocalizedString("UI", "endscreen.score.poor");
        [SerializeField] LocalizedString kills = new LocalizedString("UI", "endscreen.kills");
        [SerializeField] LocalizedString puzzles = new LocalizedString("UI", "endscreen.puzzles");
        [SerializeField] LocalizedString runTime = new LocalizedString("UI", "endscreen.time");
        [SerializeField] LocalizedString seed = new LocalizedString("UI", "endscreen.seed");
        [SerializeField] LocalizedString endingsFound = new LocalizedString("UI", "endscreen.endings_found");
        [SerializeField] LocalizedString lockedSlot = new LocalizedString("UI", "endscreen.slot.locked");

        /// <summary>"New run" was clicked (D-157).</summary>
        public event Action NewRunRequested;
        /// <summary>"Main menu" was clicked (D-202).</summary>
        public event Action MainMenuRequested;

        public bool IsShown { get; private set; }

        readonly List<PatternLine> lines = new List<PatternLine>(12);
        readonly List<Image> slots = new List<Image>(6);
        readonly List<PatternLine> slotLines = new List<PatternLine>(6);
        PatternLine title, path, score, killLine, puzzleLine, time, seedLine, found;

        void Awake()
        {
            title = AddLine(titleLabel);
            path = AddLine(pathLabel);
            score = AddLine(scoreLabel);
            killLine = AddLine(killsLabel);
            puzzleLine = AddLine(puzzlesLabel);
            time = AddLine(timeLabel);
            seedLine = AddLine(seedLabel);
            found = AddLine(endingsFoundLabel);
            if (slotTemplate != null) slots.Add(slotTemplate);

            if (newRunButton != null) newRunButton.onClick.AddListener(HandleNewRun);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(HandleMainMenu);
            Hide();
        }

        PatternLine AddLine(TMP_Text label)
        {
            var line = new PatternLine(this, label);
            lines.Add(line);
            return line;
        }

        /// <summary>Fill and show the screen (D-200).</summary>
        public void Show(RunResult result, EndingData ending, EndingCatalog catalog, IEndingsRecord record)
        {
            IsShown = true;
            if (canvas != null) canvas.enabled = true;
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }
            SetButtonsInteractable(true);

            title.Set(ending != null ? ending.title : null, null);
            if (graphic != null)
            {
                graphic.sprite = ending != null ? ending.graphic : null;
                graphic.color = graphic.sprite != null ? Color.white : placeholderColour;
            }

            if (result != null)
            {
                path.Set(result.Path == RunPath.Discover ? pathDiscover : pathEscape, "Path: " + result.Path);
                string scoreText = result.Score.ToString("0.#", CultureInfo.InvariantCulture);
                score.Set(result.IsGood ? scoreGood : scorePoor, "Score: {0} / 100", scoreText);
                killLine.Set(kills, "Enemies killed: {0} / {1} ({2} pts)", result.Kills, result.EnemiesSpawned, Points(result.KillPoints));
                puzzleLine.Set(puzzles, "Optional puzzles: {0} / {1} ({2} pts)", result.OptionalSolved, result.OptionalAvailable, Points(result.PuzzlePoints));
                time.Set(runTime, "Run time: {0}", RunResult.FormatTime(result.RunTimeSeconds));
                seedLine.Set(seed, "Seed {0}", RunSeed.Format(result.Seed));
            }

            FillSlots(catalog, record);
        }

        /// <summary>Hide the screen and stop listening to localization.</summary>
        public void Hide()
        {
            IsShown = false;
            if (canvas != null) canvas.enabled = false;
            if (group != null)
            {
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            for (int i = 0; i < lines.Count; i++) lines[i].Clear();
            for (int i = 0; i < slotLines.Count; i++) slotLines[i].Clear();
        }

        void FillSlots(EndingCatalog catalog, IEndingsRecord record)
        {
            int launch = 0, launchFound = 0, used = 0;
            EndingData[] all = catalog != null && catalog.endings != null ? catalog.endings : new EndingData[0];
            for (int i = 0; i < all.Length; i++)
            {
                EndingData e = all[i];
                if (e == null) continue;
                bool isFound = record != null && record.HasFound(e.id);
                if (!e.isSecret)
                {
                    launch++;
                    if (isFound) launchFound++;
                }
                else if (!isFound) continue; // secret endings stay hidden until found (D-208)

                Image slot = GetSlot(used);
                PatternLine label = GetSlotLine(used);
                used++;
                if (slot == null) continue;
                slot.gameObject.SetActive(true);
                slot.sprite = e.graphic;
                slot.color = isFound ? (e.graphic != null ? Color.white : placeholderColour) : lockedColour;
                if (isFound) label.Set(e.title, e.DisplayId);
                else label.Set(lockedSlot, "???");
            }
            for (int i = used; i < slots.Count; i++) slots[i].gameObject.SetActive(false);

            found.Set(endingsFound, "Endings found: {0} / {1}", launchFound, launch);
        }

        Image GetSlot(int index)
        {
            if (slotTemplate == null) return null;
            while (slots.Count <= index)
            {
                Image copy = Instantiate(slotTemplate, slotRow != null ? slotRow : slotTemplate.transform.parent);
                copy.name = slotTemplate.name + " " + (slots.Count + 1);
                slots.Add(copy);
            }
            return slots[index];
        }

        PatternLine GetSlotLine(int index)
        {
            while (slotLines.Count <= index)
            {
                Image slot = GetSlot(slotLines.Count);
                slotLines.Add(new PatternLine(this, slot != null ? slot.GetComponentInChildren<TMP_Text>(true) : null));
            }
            return slotLines[index];
        }

        static string Points(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

        void HandleNewRun()
        {
            if (!IsShown) return;
            SetButtonsInteractable(false); // one click only
            NewRunRequested?.Invoke();
        }

        void HandleMainMenu()
        {
            if (!IsShown) return;
            SetButtonsInteractable(false);
            MainMenuRequested?.Invoke();
        }

        void SetButtonsInteractable(bool value)
        {
            if (newRunButton != null) newRunButton.interactable = value;
            if (mainMenuButton != null) mainMenuButton.interactable = value;
        }

        /// <summary>One label = a localized pattern + its values. Re-formats when the language changes.</summary>
        sealed class PatternLine
        {
            readonly TMP_Text label;
            readonly LocalizedTextBinding binding;
            object[] args;
            string fallback;

            public PatternLine(Behaviour owner, TMP_Text label)
            {
                this.label = label;
                binding = new LocalizedTextBinding(owner, HandlePattern);
            }

            /// <param name="fallbackPattern">English pattern used when localization is unavailable (testing).</param>
            public void Set(LocalizedString pattern, string fallbackPattern, params object[] values)
            {
                args = values;
                fallback = fallbackPattern;
                binding.Clear();
                if (pattern == null || pattern.IsEmpty)
                {
                    HandlePattern(fallbackPattern);
                    return;
                }
                if (LocalizationGuard.Current == LocalizationGuard.State.Unavailable)
                {
                    HandlePattern(fallbackPattern ?? LocalizationGuard.FallbackText(pattern));
                    return;
                }
                binding.Set(pattern);
            }

            public void Clear() => binding.Clear();

            void HandlePattern(string pattern)
            {
                if (label == null) return;
                if (string.IsNullOrEmpty(pattern)) pattern = fallback ?? string.Empty;
                if (args == null || args.Length == 0)
                {
                    label.text = pattern;
                    return;
                }
                try
                {
                    label.text = string.Format(CultureInfo.CurrentCulture, pattern, args);
                }
                catch (FormatException)
                {
                    label.text = pattern; // a broken translation must never crash the end screen
                }
            }
        }
    }
}
