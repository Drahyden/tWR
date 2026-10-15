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
    /// R3 (D-173 to D-175): a switch is one "slot" of its puzzle. defaultActive = used by the authored layout (seed 0 /
    /// sandbox); a seeded variant may switch the slot off completely (slotRoot hidden: not visible, not shootable).
    /// orderMarks are small tally dots under the switch that show its place in a Sequence variant (1, 2 or 3 dots, no
    /// text); the clue glow is the ordered light-up of D-175. Once its puzzle is solved the switch is frozen (D-174):
    /// hits are ignored (no hit marker, nothing changes).
    /// </summary>
    public class ShootableSwitch : MonoBehaviour, IShootable, IInteractable
    {
        [Tooltip("Stable ID sent with PuzzleEvents.SwitchActivated (D-080), e.g. \"switch.high\". Also the slot id rolls sort by (D-172). Empty = object name.")]
        [SerializeField] string switchId = "";
        [Tooltip("Which gun counts (D-118). Default = any gun. Pistol-only switch: tick requireSpecificGun, pick Pistol.")]
        [SerializeField] GunRequirement requirement = GunRequirement.Any;

        [Header("Optional F interaction - D-112 (off = shoot only)")]
        [SerializeField] bool allowInteract = false;
        [Tooltip("Only used when allowInteract is on. String Table 'UI'.")]
        [SerializeField] LocalizedString prompt = new LocalizedString("UI", "interact.generic");

        [Header("Slot - D-173 (seeded puzzle variants)")]
        [Tooltip("Active in the authored layout (seed 0 / sandbox). Off = a spare slot only seeded variants may use.")]
        [SerializeField] bool defaultActive = true;
        [Tooltip("Everything that belongs to this slot (backplate, button, light). Hidden when the slot is unused. Empty = this object.")]
        [SerializeField] GameObject slotRoot;
        [Tooltip("Tally dots under the switch: the first N show its place in a Sequence variant (D-175). Hidden otherwise.")]
        [SerializeField] GameObject[] orderMarks = new GameObject[0];

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
        bool clueOn;
        Color clueColour = new Color(1f, 0.85f, 0.35f);

        public string SwitchId => string.IsNullOrEmpty(switchId) ? name : switchId;
        public GunRequirement Requirement => requirement;
        public bool IsShownOn => shownOn;
        public bool DefaultActive => defaultActive;
        public GameObject SlotRoot => slotRoot != null ? slotRoot : gameObject;
        public int OrderMarkCount => orderMarks != null ? orderMarks.Length : 0;

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

        /// <summary>Called by the owning PuzzleController. index -1 / owner null = this slot is not used.</summary>
        public void Bind(PuzzleController owner, int switchIndex)
        {
            controller = owner;
            index = switchIndex;
        }

        /// <summary>Show or hide the whole slot (D-173: unused slots are neither visible nor shootable).</summary>
        public void SetSlotEnabled(bool enabledSlot)
        {
            GameObject root = SlotRoot;
            if (root.activeSelf != enabledSlot) root.SetActive(enabledSlot);
        }

        /// <summary>Show 'position' tally dots (1-based place in a Sequence variant); 0 hides them (D-175).</summary>
        public void ShowOrderMark(int position)
        {
            if (orderMarks == null) return;
            for (int i = 0; i < orderMarks.Length; i++)
                if (orderMarks[i] != null) orderMarks[i].SetActive(i < position);
        }

        public bool OnShot(in ShotHitInfo hit)
        {
            if (!requirement.Accepts(hit.Gun)) return false; // wrong gun: ignored, no hit marker
            if (controller == null)
            {
                Debug.LogWarning($"[ShootableSwitch] '{name}' is not in any PuzzleController's switch list.", this);
                return true;
            }
            if (controller.IsSolved) return false; // D-174: solved is final, the switch is frozen (no hit marker)
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

        /// <summary>Clue glow on/off (D-175). While on it overrides the on/off colour; the state is kept underneath.</summary>
        public void SetClueGlow(bool on, Color colour)
        {
            clueOn = on;
            clueColour = colour;
            ApplyVisual();
        }

        void ApplyVisual()
        {
            Color colour = clueOn ? clueColour : shownOn ? onColour : offColour;
            bool bright = clueOn || shownOn;
            if (indicatorRenderer != null)
            {
                if (block == null) block = new MaterialPropertyBlock();
                indicatorRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, colour);
                block.SetColor(EmissionColorId, colour * (bright ? onEmission : offEmission));
                indicatorRenderer.SetPropertyBlock(block);
            }
            if (indicatorLight != null)
            {
                indicatorLight.color = colour;
                indicatorLight.intensity = bright ? onLightIntensity : offLightIntensity;
            }
        }
    }
}
