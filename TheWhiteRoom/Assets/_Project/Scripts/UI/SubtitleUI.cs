using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using TWR.Core;

namespace TWR.UI
{
    /// <summary>Subtitle on/off (D-089). On by default; the Settings menu (D-090) will toggle and save it later (D-099).</summary>
    public static class SubtitleSettings
    {
        public static bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Shows the inner voice's subtitle lines at the bottom of the screen (D-089, D-084): ending lines (D-199) plus the
    /// fork-door comments (D-166) and puzzle hints (D-178) that already carry subtitle keys. Text comes from the
    /// "Subtitles" table through LocalizedTextBinding. Each line stays for holdSeconds (unscaled time), then hides.
    /// Lives in Core on its own canvas above the white fader, so a line stays readable during the ending's fade.
    /// Cheap: one float compare per frame while a line is up.
    /// </summary>
    public class SubtitleUI : MonoBehaviour
    {
        [SerializeField] TMP_Text label;
        [SerializeField] CanvasGroup group;
        [Tooltip("How long one line stays on screen (seconds).")]
        [SerializeField] [Min(0.5f)] float holdSeconds = 5f;

        LocalizedTextBinding binding;
        float hideAt;
        bool showing;

        void Awake()
        {
            binding = new LocalizedTextBinding(this, HandleText);
            SetVisible(false);
        }

        void OnEnable()
        {
            VoiceEvents.EndingLine += HandleLine;
            VoiceEvents.ForkDoorSeen += HandleLine;
            VoiceEvents.PuzzleHint += HandleLine;
        }

        void OnDisable()
        {
            VoiceEvents.EndingLine -= HandleLine;
            VoiceEvents.ForkDoorSeen -= HandleLine;
            VoiceEvents.PuzzleHint -= HandleLine;
            binding.Clear();
            SetVisible(false);
        }

        void HandleLine(string cueId, LocalizedString subtitle) => Show(subtitle);

        /// <summary>Show one localized line now (replaces the current one).</summary>
        public void Show(LocalizedString subtitle)
        {
            if (!SubtitleSettings.Enabled || subtitle == null || subtitle.IsEmpty) return;
            binding.Clear();
            binding.Set(subtitle);
            hideAt = Time.unscaledTime + holdSeconds;
            SetVisible(true);
        }

        void Update()
        {
            if (showing && Time.unscaledTime >= hideAt)
            {
                binding.Clear();
                SetVisible(false);
            }
        }

        void HandleText(string value)
        {
            if (label != null) label.text = value;
        }

        void SetVisible(bool visible)
        {
            showing = visible;
            if (group != null) group.alpha = visible ? 1f : 0f;
        }
    }
}
