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
        InputAction shootAction, reloadAction, weaponScrollAction;
        readonly InputAction[] weaponSlotActions = new InputAction[5];

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

        // ---- Weapons (D-110: LMB shoot, R reload, 1-5 / wheel switch gun; D-088) ----
        public bool ShootPressedThisFrame => shootAction != null && shootAction.WasPressedThisFrame();
        /// <summary>Held trigger, for automatic guns later (AK, D-049).</summary>
        public bool ShootHeld => shootAction != null && shootAction.IsPressed();
        public bool ReloadPressedThisFrame => reloadAction != null && reloadAction.WasPressedThisFrame();

        /// <summary>Slot index 0-4 if a number key 1-5 was pressed this frame, otherwise -1.</summary>
        public int WeaponSlotPressedThisFrame
        {
            get
            {
                for (int i = 0; i < weaponSlotActions.Length; i++)
                    if (weaponSlotActions[i] != null && weaponSlotActions[i].WasPressedThisFrame()) return i;
                return -1;
            }
        }

        /// <summary>+1 = wheel scrolled up (next gun), -1 = down (previous gun), 0 = no scroll this frame.</summary>
        public int WeaponScrollStep
        {
            get
            {
                if (weaponScrollAction == null) return 0;
                float y = weaponScrollAction.ReadValue<Vector2>().y;
                return y > 0.01f ? 1 : y < -0.01f ? -1 : 0;
            }
        }

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

            // Weapon actions (step 2). Looked up softly so an old copy of the asset only warns once.
            shootAction = FindOptional("Shoot");
            reloadAction = FindOptional("Reload");
            weaponScrollAction = FindOptional("WeaponScroll");
            for (int i = 0; i < weaponSlotActions.Length; i++)
                weaponSlotActions[i] = FindOptional("Weapon" + (i + 1));
        }

        InputAction FindOptional(string actionName)
        {
            InputAction action = map.FindAction(actionName, false);
            if (action == null)
                Debug.LogWarning($"[PlayerInputReader] Action '{actionName}' missing from {actions.name}. Re-import TWR_Controls.inputactions.", this);
            return action;
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
