using System;
using UnityEngine;
using UnityEngine.Localization;

namespace TWR.UI
{
    /// <summary>
    /// What the Credits screen shows (D-229): sections, each with a localized heading (table 'UI', D-084) and plain lines
    /// (names and titles are never localized). Fixed sections (developer, tools) are yours to edit in the Inspector.
    /// Generated sections are rewritten from docs/audio/LICENSES.md rows with attribution = y by TWR > Credits > Build
    /// Credits, by the main menu builder and before every player build, so don't edit those by hand.
    /// </summary>
    [CreateAssetMenu(menuName = "TWR/Credits Data", fileName = "CreditsData")]
    public class CreditsData : ScriptableObject
    {
        [Serializable]
        public class Section
        {
            [Tooltip("Table 'UI', e.g. key 'credits.heading.created_by'.")]
            public LocalizedString heading = new LocalizedString();
            [Tooltip("Plain text, shown as written (not localized, D-229).")]
            public string[] lines = new string[0];
        }

        [Tooltip("Hand-edited sections shown first (developer, tools). Kept when credits are regenerated.")]
        public Section[] fixedSections = new Section[0];

        [Tooltip("Written from docs/audio/LICENSES.md (attribution = y). Regenerated automatically - don't edit by hand.")]
        public Section[] generatedSections = new Section[0];

        [Tooltip("Auto-scroll speed in pixels per second (1920x1080 reference). 0 = no auto-scroll.")]
        [Min(0f)] public float autoScrollSpeed = 45f;

        [Tooltip("Seconds the list waits at the top before it starts scrolling.")]
        [Min(0f)] public float autoScrollDelay = 1.5f;
    }
}
