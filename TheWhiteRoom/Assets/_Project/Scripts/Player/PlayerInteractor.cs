using System;
using UnityEngine;
using UnityEngine.Localization;
using TWR.Core;
using TWR.Interaction;

namespace TWR.Player
{
    /// <summary>
    /// Finds what the player is looking at (ray from screen centre, ~2.5 m) and uses it on F (D-032, D-038).
    /// Only colliders on the Interactable layer count; walls in between block the ray.
    /// On use: calls IInteractable.Interact, emits an Interact noise (D-079) and raises
    /// InteractionEvents.OnInteracted for future voice triggers (D-080). FocusChanged drives the prompt (D-091).
    /// </summary>
    [DefaultExecutionOrder(30)]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] PlayerTuning tuning;
        [SerializeField] PlayerInputReader input;
        [SerializeField] Camera playerCamera;
        [Tooltip("Layer(s) interactables live on. Defaults to the 'Interactable' layer if left empty.")]
        [SerializeField] LayerMask interactableMask;
        [Tooltip("Solid layers that block the interaction ray (walls).")]
        [SerializeField] LayerMask blockingMask = 1; // Default layer

        static readonly Vector3 ScreenCentre = new Vector3(0.5f, 0.5f, 0f);

        IInteractable focused;                  // what the prompt shows and F uses
        LocalizedString focusedPrompt;          // its prompt when focus was last announced
        Collider lastCollider;                  // cache: GetComponent only runs for a NEW collider
        IInteractable lastColliderInteractable;
        Vector3 lastHitPoint;

        /// <summary>Raised when the looked-at interactable changes (null = nothing).</summary>
        public event Action<IInteractable> FocusChanged;

        public IInteractable Current => focused;

        void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (interactableMask.value == 0) interactableMask = LayerMask.GetMask("Interactable");
            if (interactableMask.value == 0)
                Debug.LogWarning("[PlayerInteractor] No 'Interactable' layer found. Create it in Tags and Layers.", this);

            // The held gun (Weapon layer, D-123) and trigger volumes (Ignore Raycast) must never
            // block the interaction ray, even if blockingMask is set to Everything.
            rayMask = (interactableMask.value | blockingMask.value) & ~LayerMask.GetMask("Weapon", "Ignore Raycast");
        }

        int rayMask;

        void Update()
        {
            if (tuning == null || playerCamera == null) return;

            UpdateFocus();

            if (focused != null && input != null && input.InteractPressedThisFrame)
            {
                IInteractable target = focused;
                target.Interact(gameObject);
                NoiseSystem.Emit(lastHitPoint, tuning.interactNoise, NoiseType.Interact, gameObject);
                InteractionEvents.RaiseInteracted(gameObject, target);
            }
        }

        void UpdateFocus()
        {
            IInteractable found = null;
            Ray ray = playerCamera.ViewportPointToRay(ScreenCentre);
            if (Physics.Raycast(ray, out RaycastHit hit, tuning.interactDistance, rayMask, QueryTriggerInteraction.Ignore))
            {
                lastHitPoint = hit.point;
                if (hit.collider != lastCollider)
                {
                    lastCollider = hit.collider;
                    bool onInteractableLayer = (interactableMask.value & (1 << hit.collider.gameObject.layer)) != 0;
                    lastColliderInteractable = onInteractableLayer ? hit.collider.GetComponentInParent<IInteractable>() : null;
                }
                found = lastColliderInteractable;
            }
            else
            {
                lastCollider = null;
                lastColliderInteractable = null;
            }

            // Treat destroyed or currently unusable objects as "nothing".
            if (found is UnityEngine.Object unityObject && unityObject == null) found = null;
            if (found != null && !found.CanInteract) found = null;

            SetFocus(found);
        }

        void SetFocus(IInteractable target)
        {
            // Also re-announce when the SAME object swaps its prompt (e.g. a door going from
            // "Locked" to "Press F to open" while you look at it, D-127). Reference compare: no garbage.
            LocalizedString prompt = target != null ? target.Prompt : null;
            if (ReferenceEquals(target, focused) && ReferenceEquals(prompt, focusedPrompt)) return;
            focused = target;
            focusedPrompt = prompt;
            FocusChanged?.Invoke(focused);
        }
    }
}
