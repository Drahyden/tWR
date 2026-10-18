using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace TWR.UI
{
    /// <summary>
    /// The scrolling Credits list (D-229), on the ScrollRect of the main menu's Credits panel. The first time it opens it
    /// clones a heading template and a line template once per entry of CreditsData (fixed sections first, then the ones
    /// generated from docs/audio/LICENSES.md). Headings are localized through LocalizedTextBinding (D-084); lines are
    /// plain text. Each time the panel opens the list starts at the top and slowly scrolls down; the mouse wheel or a
    /// drag takes over and stops the auto-scroll. No allocations after the first open.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public class CreditsScreen : MonoBehaviour, IScrollHandler, IBeginDragHandler
    {
        [SerializeField] CreditsData data;
        [Tooltip("Inactive TMP label cloned for each section heading (child of the ScrollRect content).")]
        [SerializeField] TMP_Text headingTemplate;
        [Tooltip("Inactive TMP label cloned for each credit line (child of the ScrollRect content).")]
        [SerializeField] TMP_Text lineTemplate;
        [Tooltip("Empty space above each heading except the first, in pixels.")]
        [SerializeField] float sectionSpacing = 48f;

        ScrollRect scroll;
        readonly List<(LocalizedTextBinding binding, LocalizedString text)> headings = new List<(LocalizedTextBinding, LocalizedString)>();
        bool built;
        bool autoScrolling;
        float waitUntil;

        void Awake()
        {
            scroll = GetComponent<ScrollRect>();
            if (data == null) Debug.LogWarning("[Credits] No CreditsData assigned. Run TWR > Credits > Build Credits (D-229).", this);
        }

        void OnEnable()
        {
            if (!built) Build();
            foreach (var (binding, text) in headings) binding.Set(text);

            Canvas.ForceUpdateCanvases();
            if (scroll.content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1f; // top
            autoScrolling = data != null && data.autoScrollSpeed > 0f;
            waitUntil = Time.unscaledTime + (data != null ? data.autoScrollDelay : 0f);
        }

        void OnDisable()
        {
            foreach (var (binding, _) in headings) binding.Clear();
        }

        void Update()
        {
            if (!autoScrolling || Time.unscaledTime < waitUntil) return;
            RectTransform content = scroll.content;
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            float max = content.rect.height - viewport.rect.height;
            Vector2 position = content.anchoredPosition;
            if (position.y >= max)
            {
                autoScrolling = false; // reached the end
                return;
            }
            position.y = Mathf.Min(max, position.y + data.autoScrollSpeed * Time.unscaledDeltaTime);
            content.anchoredPosition = position;
        }

        // The player scrolls or drags: hand control over to them.
        public void OnScroll(PointerEventData eventData) => autoScrolling = false;
        public void OnBeginDrag(PointerEventData eventData) => autoScrolling = false;

        void Build()
        {
            built = true;
            if (data == null || headingTemplate == null || lineTemplate == null)
            {
                if (headingTemplate == null || lineTemplate == null)
                    Debug.LogWarning("[Credits] Heading or line template missing. Rebuild with TWR > Run > Build Endings + Main Menu.", this);
                return;
            }
            Transform parent = headingTemplate.transform.parent;
            bool first = true;
            AddSections(data.fixedSections, parent, ref first);
            AddSections(data.generatedSections, parent, ref first);
        }

        void AddSections(CreditsData.Section[] sections, Transform parent, ref bool first)
        {
            if (sections == null) return;
            foreach (CreditsData.Section section in sections)
            {
                if (section == null || section.lines == null || section.lines.Length == 0) continue;
                if (!first) AddSpacer(parent);
                first = false;

                TMP_Text heading = Instantiate(headingTemplate, parent);
                heading.name = "Heading";
                heading.gameObject.SetActive(true);
                if (section.heading != null && !section.heading.IsEmpty)
                    headings.Add((new LocalizedTextBinding(this, value => heading.text = value), section.heading));
                else
                    heading.text = string.Empty;

                foreach (string line in section.lines)
                {
                    TMP_Text label = Instantiate(lineTemplate, parent);
                    label.name = "Line";
                    label.text = line;
                    label.gameObject.SetActive(true);
                }
            }
        }

        void AddSpacer(Transform parent)
        {
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(parent, false);
            var layout = spacer.GetComponent<LayoutElement>();
            layout.minHeight = sectionSpacing;
            layout.preferredHeight = sectionSpacing;
        }
    }
}
