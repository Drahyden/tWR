using System;
using UnityEngine;
using UnityEngine.Localization;
using TWR.Core;
using TWR.Interaction;

namespace TWR.Puzzles
{
    /// <summary>
    /// A door the player opens with F (D-127; D-112). While its DoorLock is locked, F shows the
    /// localized "Locked" prompt and does nothing else (PlayerInteractor still raises the interaction event
    /// and Interact noise). Once unlocked, the prompt is "Press F to open" and F slides or swings the panel
    /// open procedurally, with a ~10 m Mechanism noise credited to the player (D-128).
    /// It only opens: where the door leads (D-053b, D-058) is a separate, later component.
    /// The panel's collider sits on the Interactable layer and also stops bullets like a wall.
    /// </summary>
    public class Door : MonoBehaviour, IInteractable
    {
        [SerializeField] DoorData data;
        [Tooltip("Optional. Empty = an ordinary unlocked door.")]
        [SerializeField] DoorLock doorLock;
        [Tooltip("The moving part (has the collider). Its closed pose is read on Awake.")]
        [SerializeField] Transform panel;
        [Tooltip("Stable ID for voice triggers (D-080).")]
        [SerializeField] string interactionId = "door";

        Vector3 closedPosition;
        Quaternion closedRotation;
        AudioSource audioSource;
        float openTimer;
        bool opening;
        bool isOpen;

        public bool IsLocked => doorLock != null && doorLock.IsLocked;
        public bool IsOpen => isOpen;

        /// <summary>Raised when the door starts opening. (door, opener)</summary>
        public event Action<Door, GameObject> OpenStarted;

        // IInteractable. The prompt switches by itself when the lock opens; PlayerInteractor notices.
        public LocalizedString Prompt => data == null ? null : (IsLocked ? data.lockedPrompt : data.openPrompt);
        public string InteractionId => interactionId;
        public bool CanInteract => data != null && panel != null && isActiveAndEnabled && !opening && !isOpen;

        void Awake()
        {
            if (data == null || panel == null)
                Debug.LogError($"[Door] '{name}' needs DoorData and a panel. Run TWR > Build Test Scene.", this);
            if (panel != null)
            {
                closedPosition = panel.localPosition;
                closedRotation = panel.localRotation;
            }
            audioSource = GetComponent<AudioSource>();
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract) return;
            if (IsLocked)
            {
                PlayClip(data.lockedClip); // "rattle"; the prompt already says Locked
                return;
            }
            Open(interactor);
        }

        /// <summary>Start opening (if unlocked). opener is credited for the noise.</summary>
        public void Open(GameObject opener)
        {
            if (opening || isOpen || IsLocked || data == null || panel == null) return;
            opening = true;
            openTimer = 0f;
            NoiseSystem.Emit(transform.position, data.openNoiseRadius, NoiseType.Mechanism, opener);
            PlayClip(data.openClip);
            OpenStarted?.Invoke(this, opener);
        }

        void Update()
        {
            if (!opening) return;

            openTimer += Time.deltaTime;
            float t = Mathf.Clamp01(openTimer / data.openSeconds);
            ApplyPose(t * t * (3f - 2f * t)); // smoothstep: eases in and out

            if (t >= 1f)
            {
                opening = false;
                isOpen = true;
            }
        }

        void ApplyPose(float amount)
        {
            if (data.openStyle == DoorOpenStyle.Slide)
                panel.localPosition = closedPosition + Vector3.right * (data.slideDistance * amount);
            else
                panel.localRotation = closedRotation * Quaternion.Euler(0f, data.swingAngle * amount, 0f);
        }

        void PlayClip(AudioClip clip)
        {
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
        }
    }
}
