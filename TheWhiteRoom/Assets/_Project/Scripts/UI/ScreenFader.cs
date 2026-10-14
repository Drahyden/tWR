using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

namespace TWR.UI
{
    /// <summary>
    /// Full-screen white overlay for section transitions (D-167: fade to white, ~0.4 s) and the placeholder
    /// "run complete" screen (D-092). Lives in the Core scene on its own top-most canvas, above the HUD.
    /// FadeTo is a coroutine step the SectionLoader waits on; it uses unscaled time, so a paused game can't stall it.
    /// The optional message is a localized string (D-084) shown on the white. Starts opaque, so the empty Core scene
    /// is never visible before the lobby has loaded. No work while nothing fades.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        [Tooltip("CanvasGroup on the white overlay; its alpha is the fade.")]
        [SerializeField] CanvasGroup group;
        [Tooltip("Centre text for the end screen. Hidden unless ShowMessage is called.")]
        [SerializeField] TMP_Text messageLabel;
        [Tooltip("Start fully white (Core: hides the empty world until the first section is in).")]
        [SerializeField] bool startOpaque = true;

        LocalizedTextBinding messageBinding;

        public float Alpha => group != null ? group.alpha : 0f;

        void Awake()
        {
            messageBinding = new LocalizedTextBinding(this, HandleMessageText);
            if (group != null)
            {
                group.alpha = startOpaque ? 1f : 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            if (messageLabel != null)
            {
                messageLabel.text = string.Empty;
                messageLabel.enabled = false;
            }
        }

        void OnDisable()
        {
            messageBinding?.Clear();
        }

        /// <summary>Fade the white overlay to target alpha (0 = clear, 1 = white) over seconds. yield return it.</summary>
        public IEnumerator FadeTo(float target, float seconds)
        {
            if (group == null) yield break;
            float start = group.alpha;
            if (seconds <= 0f || Mathf.Approximately(start, target))
            {
                group.alpha = target;
                yield break;
            }
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(start, target, elapsed / seconds);
                yield return null;
            }
            group.alpha = target;
        }

        /// <summary>Show a localized line in the middle of the white (placeholder end screen).</summary>
        public void ShowMessage(LocalizedString text)
        {
            if (messageLabel == null) return;
            messageLabel.enabled = true;
            messageBinding.Set(text);
        }

        public void HideMessage()
        {
            messageBinding?.Clear();
            if (messageLabel == null) return;
            messageLabel.text = string.Empty;
            messageLabel.enabled = false;
        }

        void HandleMessageText(string value)
        {
            if (messageLabel != null) messageLabel.text = value;
        }
    }
}
