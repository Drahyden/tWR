using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace TWR.UI
{
    /// <summary>
    /// Full-screen white overlay for section transitions (D-167: fade to white, ~0.4 s) and the placeholder
    /// "run complete" screen (D-092). Lives in the Core scene on its own top-most canvas, above the HUD.
    /// FadeTo is a coroutine step the SectionLoader waits on; it uses unscaled time, so a paused game can't stall it.
    /// The optional message is a localized string (D-084) shown on the white. Starts opaque, so the empty Core scene
    /// is never visible before the lobby has loaded. No work while nothing fades.
    /// R5 (D-196, D-198, D-199): the overlay colour can change: endings fade to white, Overtime to black, and Absorbed
    /// tints the view skin-coloured; FadeTo(alpha, colour, seconds) blends both, ResetColour() goes back to white.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        [Tooltip("CanvasGroup on the white overlay; its alpha is the fade.")]
        [SerializeField] CanvasGroup group;
        [Tooltip("Centre text for the end screen. Hidden unless ShowMessage is called.")]
        [SerializeField] TMP_Text messageLabel;
        [Tooltip("Start fully white (Core: hides the empty world until the first section is in).")]
        [SerializeField] bool startOpaque = true;
        [Tooltip("The full-screen image whose colour is changed by FadeTo(alpha, colour, ...). Optional.")]
        [SerializeField] Graphic background;

        LocalizedTextBinding messageBinding;
        Color defaultColour = Color.white;

        public float Alpha => group != null ? group.alpha : 0f;

        void Awake()
        {
            messageBinding = new LocalizedTextBinding(this, HandleMessageText);
            if (background != null) defaultColour = background.color;
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

        /// <summary>Fade to target alpha while blending the overlay colour to colour (D-196, D-199). yield return it.</summary>
        public IEnumerator FadeTo(float target, Color colour, float seconds)
        {
            if (group == null) yield break;
            if (background == null)
            {
                yield return FadeTo(target, seconds);
                yield break;
            }
            colour.a = 1f;
            float startAlpha = group.alpha;
            Color startColour = background.color;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / seconds;
                group.alpha = Mathf.Lerp(startAlpha, target, t);
                background.color = Color.Lerp(startColour, colour, t);
                yield return null;
            }
            group.alpha = target;
            background.color = colour;
        }

        /// <summary>Back to the normal white overlay (keeps the current alpha).</summary>
        public void ResetColour()
        {
            if (background != null) background.color = defaultColour;
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
