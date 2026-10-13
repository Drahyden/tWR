using UnityEngine;
using UnityEngine.Localization;

namespace TWR.Interaction
{
    /// <summary>
    /// Sample interactable for testing: a button that cycles its colour each time you press F on it.
    /// Proves the interaction raycast, localized prompt (D-084, D-091) and OnInteracted event (D-080).
    /// </summary>
    public class TestButtonInteractable : MonoBehaviour, IInteractable
    {
        [Tooltip("String Table 'UI', key 'interact.generic' by default.")]
        [SerializeField] LocalizedString prompt = new LocalizedString("UI", "interact.generic");
        [SerializeField] string interactionId = "test.button";
        [SerializeField] Color[] colours = { new Color(1f, 0.2f, 0.2f), new Color(0.2f, 1f, 0.3f), new Color(0.2f, 0.5f, 1f) };
        [Tooltip("Renderer to recolour. Auto-found on this object if empty.")]
        [SerializeField] Renderer targetRenderer;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock block;
        int colourIndex;

        public LocalizedString Prompt => prompt;
        public string InteractionId => interactionId;
        public bool CanInteract => isActiveAndEnabled;

        void Awake()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            ApplyColour();
        }

        public void Interact(GameObject interactor)
        {
            if (colours == null || colours.Length == 0) return;
            colourIndex = (colourIndex + 1) % colours.Length;
            ApplyColour();
        }

        void ApplyColour()
        {
            if (targetRenderer == null || colours == null || colours.Length == 0) return;
            Color c = colours[colourIndex];
            targetRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(EmissionColorId, c);
            targetRenderer.SetPropertyBlock(block);
        }
    }
}
