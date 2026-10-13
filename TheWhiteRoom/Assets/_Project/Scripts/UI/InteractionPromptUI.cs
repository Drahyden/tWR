using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using TWR.Interaction;
using TWR.Player;

namespace TWR.UI
{
    /// <summary>
    /// Minimal interaction prompt (D-091): shows the looked-at object's LOCALIZED prompt text (D-084)
    /// in a TextMeshPro label, hides it when looking at nothing. Event-driven: it only does work
    /// when the focus changes or the language changes, never per frame.
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        [SerializeField] PlayerInteractor interactor;
        [SerializeField] TMP_Text label;

        LocalizedString currentPrompt;
        LocalizedString.ChangeHandler onPromptChanged;

        void Awake()
        {
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);
            if (interactor == null) interactor = FindFirstObjectByType<PlayerInteractor>(); // once, not in Update
            onPromptChanged = HandlePromptChanged; // cache delegate: no allocation per subscribe
            if (label != null) label.enabled = false;
        }

        void OnEnable()
        {
            if (interactor != null) interactor.FocusChanged += HandleFocusChanged;
        }

        void OnDisable()
        {
            if (interactor != null) interactor.FocusChanged -= HandleFocusChanged;
            SetPrompt(null);
        }

        void HandleFocusChanged(IInteractable target)
        {
            SetPrompt(target != null ? target.Prompt : null);
        }

        void SetPrompt(LocalizedString prompt)
        {
            if (currentPrompt != null) currentPrompt.StringChanged -= onPromptChanged;
            currentPrompt = (prompt != null && !prompt.IsEmpty) ? prompt : null;

            if (currentPrompt != null)
                currentPrompt.StringChanged += onPromptChanged; // fires right away with the translated text
            else if (label != null)
                label.enabled = false;
        }

        void HandlePromptChanged(string value)
        {
            if (label == null) return;
            label.text = value;
            label.enabled = currentPrompt != null;
        }
    }
}
