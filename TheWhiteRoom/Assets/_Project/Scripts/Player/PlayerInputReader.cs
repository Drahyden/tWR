using UnityEngine;
using UnityEngine.InputSystem;

namespace TWR.Player
{
    /// <summary>
    /// The ONE place the player reads keyboard and mouse (D-038: fixed keys, no rebinding).
    /// Reads actions by name from our TWR_Controls asset, so no generated C# class is needed.
    /// ControlsInverted flips move and look for future reversed-control rooms (D-053d).
    /// Also locks and hides the cursor while playing.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerInputReader : MonoBehaviour
    {
        [Tooltip("Assets/_Project/Settings/Input/TWR_Controls.inputactions")]
        [SerializeField] InputActionAsset actions;
        [SerializeField] string actionMapName = "Player";
        [SerializeField] bool lockCursor = true;

        InputActionMap map;
        InputAction moveAction, lookAction, sprintAction, crouchAction, jumpAction;
        InputAction leanLeftAction, leanRightAction, interactAction;

        /// <summary>Reversed controls (D-053d). Room scripts set this on enter/exit.</summary>
        public bool ControlsInverted { get; set; }

        /// <summary>WASD as x = strafe, y = forward. Already inverted if ControlsInverted.</summary>
        public Vector2 Move
        {
            get
            {
                if (moveAction == null) return Vector2.zero;
                Vector2 v = moveAction.ReadValue<Vector2>();
                return ControlsInverted ? -v : v;
            }
        }

        /// <summary>Mouse delta in pixels this frame. Already inverted if ControlsInverted.</summary>
        public Vector2 Look
        {
            get
            {
                if (lookAction == null) return Vector2.zero;
                Vector2 v = lookAction.ReadValue<Vector2>();
                return ControlsInverted ? -v : v;
            }
        }

        public bool SprintHeld => sprintAction != null && sprintAction.IsPressed();
        public bool CrouchHeld => crouchAction != null && crouchAction.IsPressed();
        public bool JumpPressedThisFrame => jumpAction != null && jumpAction.WasPressedThisFrame();
        public bool InteractPressedThisFrame => interactAction != null && interactAction.WasPressedThisFrame();

        /// <summary>-1 = lean left (Q), +1 = lean right (E), 0 = none/both.</summary>
        public float LeanAxis
        {
            get
            {
                float axis = 0f;
                if (leanLeftAction != null && leanLeftAction.IsPressed()) axis -= 1f;
                if (leanRightAction != null && leanRightAction.IsPressed()) axis += 1f;
                return axis;
            }
        }

        void Awake()
        {
            if (actions == null)
            {
                Debug.LogError("[PlayerInputReader] No InputActionAsset assigned. Assign TWR_Controls.", this);
                enabled = false;
                return;
            }

            map = actions.FindActionMap(actionMapName, true);
            moveAction = map.FindAction("Move", true);
            lookAction = map.FindAction("Look", true);
            sprintAction = map.FindAction("Sprint", true);
            crouchAction = map.FindAction("Crouch", true);
            jumpAction = map.FindAction("Jump", true);
            leanLeftAction = map.FindAction("LeanLeft", true);
            leanRightAction = map.FindAction("LeanRight", true);
            interactAction = map.FindAction("Interact", true);
        }

        void OnEnable()
        {
            map?.Enable();
            SetCursorLocked(lockCursor);
        }

        void OnDisable()
        {
            map?.Disable();
            SetCursorLocked(false);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && enabled) SetCursorLocked(lockCursor);
        }

        /// <summary>Pause menu (later) can call this to free the mouse.</summary>
        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
