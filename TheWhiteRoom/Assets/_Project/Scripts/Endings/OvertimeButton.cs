using UnityEngine;
using UnityEngine.Localization;
using TWR.Interaction;

namespace TWR.Endings
{
    /// <summary>
    /// The one button in the Overtime room (D-196): a plain IInteractable, not a puzzle (D-204), so it is never counted
    /// by RunStats. Press F once: the cap sinks a little and the EndingDirector finishes the ending (the screen goes
    /// black, then the end-of-run screen). The PlayerInteractor already raises InteractionEvents for the inner voice
    /// (D-080). Its collider must be on the Interactable layer (set up by the builder).
    /// </summary>
    public class OvertimeButton : MonoBehaviour, IInteractable
    {
        [Tooltip("Table 'UI', key 'interact.generic' (\"Press F to interact\").")]
        [SerializeField] LocalizedString prompt = new LocalizedString("UI", "interact.generic");
        [SerializeField] string interactionId = "ending.overtime.button";
        [SerializeField] EndingDirector director;
        [Tooltip("How far the button sinks when pressed (metres).")]
        [SerializeField] float pressDepth = 0.03f;

        bool pressed;

        public LocalizedString Prompt => prompt;
        public string InteractionId => interactionId;
        public bool CanInteract => !pressed && isActiveAndEnabled;

        public void Interact(GameObject interactor)
        {
            if (pressed) return;
            pressed = true;
            transform.localPosition += Vector3.down * pressDepth;
            if (director != null) director.Finish();
            else Debug.LogWarning("[Endings] Overtime button has no EndingDirector.", this);
        }
    }
}
