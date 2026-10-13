using UnityEngine;
using UnityEngine.Localization;
using TWR.Combat;
using TWR.Interaction;

namespace TWR.Puzzles
{
    /// <summary>
    /// A switch you trigger by shooting it (D-125; D-036, D-105 pistol = switches, D-118).
    /// It holds no puzzle state: it reports accepted hits to its PuzzleController and shows the state
    /// the controller tells it, with colour and light only, no UI (D-065): dark red = off, bright green = on.
    /// "requirement" is the optional gun filter (D-118): default = any gun; the slice switches are Pistol only.
    /// Shoot-only by default; tick allowInteract to also allow F (then it goes through IInteractable, D-112,
    /// and its collider must be on the Interactable layer).
    /// </summary>
    public class ShootableSwitch : MonoBehaviour, IShootable, IInteractable
    {
        [Tooltip("Stable ID sent with PuzzleEvents.SwitchActivated (D-080), e.g. \"switch.high\". Empty = object name.")]
        [SerializeField] string switchId = "";
        [Tooltip("Which gun counts (D-118). Default = any gun. Pistol-only switch: tick requireSpecificGun, pick Pistol.")]
        [SerializeField] GunRequirement requirement = GunRequirement.Any;

        [Header("Optional F interaction - D-112 (off = shoot only)")]
        [SerializeField] bool allowInteract = false;
        [Tooltip("Only used when allowInteract is on. String Table 'UI'.")]
        [SerializeField] LocalizedString prompt = new LocalizedString("UI", "interact.generic");

        [Header("State display - D-065 (light, not UI)")]
        [Tooltip("Renderer that changes colour. Auto-found on this object if empty.")]
        [SerializeField] Renderer indicatorRenderer;
        [Tooltip("Optional small light that takes the same colour.")]
        [SerializeField] Light indicatorLight;
        [SerializeField] Color offColour = new Color(0.55f, 0.05f, 0.04f);
        [SerializeField] Color onColour = new Color(0.15f, 1f, 0.3f);
        [Tooltip("Emission multiplier while off (dim) and on (bright, blooms).")]
        [SerializeField] float offEmission = 0.6f;
        [SerializeField] float onEmission = 3f;
        [SerializeField] float offLightIntensity = 0.4f;
        [SerializeField] float onLightIntensity = 1.6f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        PuzzleController controller;
        int index = -1;
        MaterialPropertyBlock block;
        bool shownOn;

        public string SwitchId => switchId;
        public GunRequirement Requirement => requirement;
        public bool IsShownOn => shownOn;

        // IInteractable (only active when allowInteract is ticked).
        public LocalizedString Prompt => prompt;
        public string InteractionId => switchId;
        public bool CanInteract => allowInteract && isActiveAndEnabled && controller != null && controller.CanPress(index);

        void Awake()
        {
            if (string.IsNullOrEmpty(switchId)) switchId = name; // cached once, no per-event allocation
            if (indicatorRenderer == null) indicatorRenderer = GetComponent<Renderer>();
            ApplyVisual();
        }

        /// <summary>Called once by the owning PuzzleController.</summary>
        public void Bind(PuzzleController owner, int switchIndex)
        {
            controller = owner;
            index = switchIndex;
        }

        public bool OnShot(in ShotHitInfo hit)
        {
            if (!requirement.Accepts(hit.Gun)) return false; // wrong gun: ignored, no hit marker
            if (controller == null)
            {
                Debug.LogWarning($"[ShootableSwitch] '{name}' is not in any PuzzleController's switch list.", this);
                return true;
            }
            controller.HandleSwitchTriggered(index, hit.Shooter);
            return true; // a valid hit on a switch: show the normal hit marker (D-091)
        }

        public void Interact(GameObject interactor)
        {
            if (!allowInteract || controller == null) return;
            controller.HandleSwitchTriggered(index, interactor);
        }

        /// <summary>Show on/off. Called by the PuzzleController only.</summary>
        public void ShowState(bool on)
        {
            shownOn = on;
            ApplyVisual();
        }

        void ApplyVisual()
        {
            Color colour = shownOn ? onColour : offColour;
            if (indicatorRenderer != null)
            {
                if (block == null) block = new MaterialPropertyBlock();
                indicatorRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, colour);
                block.SetColor(EmissionColorId, colour * (shownOn ? onEmission : offEmission));
                indicatorRenderer.SetPropertyBlock(block);
            }
            if (indicatorLight != null)
            {
                indicatorLight.color = colour;
                indicatorLight.intensity = shownOn ? onLightIntensity : offLightIntensity;
            }
        }
    }
}
