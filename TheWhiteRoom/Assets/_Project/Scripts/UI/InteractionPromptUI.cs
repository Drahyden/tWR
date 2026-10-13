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
        System.Action<bool> onGuardResolved;
        bool subscribed;

        void Awake()
        {
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);
            if (interactor == null) interactor = FindFirstObjectByType<PlayerInteractor>(); // once, not in Update
            onPromptChanged = HandlePromptChanged; // cache delegate: no allocation per subscribe
            onGuardResolved = HandleGuardResolved;
            LocalizationGuard.WhenResolved(onGuardResolved); // check localization early, not on first look
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
            if (currentPrompt != null && subscribed) currentPrompt.StringChanged -= onPromptChanged;
            subscribed = false;
            currentPrompt = (prompt != null && !prompt.IsEmpty) ? prompt : null;

            if (currentPrompt == null)
            {
                if (label != null) label.enabled = false;
                return;
            }

            // Only touch the Localization package once it is known to work; otherwise it logs
            // an error per lookup. The guard warns once and we show the key instead.
            switch (LocalizationGuard.Current)
            {
                case LocalizationGuard.State.Ready:
                    currentPrompt.StringChanged += onPromptChanged; // fires right away with the translated text
                    subscribed = true;
                    break;
                case LocalizationGuard.State.Unavailable:
                    HandlePromptChanged(LocalizationGuard.FallbackText(currentPrompt));
                    break;
                default:
                    LocalizationGuard.WhenResolved(onGuardResolved); // re-applies the current prompt when known
                    break;
            }
        }

        void HandleGuardResolved(bool ready)
        {
            if (this != null && isActiveAndEnabled) SetPrompt(currentPrompt);
        }

        void HandlePromptChanged(string value)
        {
            if (label == null) return;
            label.text = value;
            label.enabled = currentPrompt != null;
        }
    }
}
