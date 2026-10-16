using TMPro;
using UnityEngine;
using UnityEngine.Localization;

namespace TWR.UI
{
    /// <summary>
    /// Puts a localized string on a TextMeshPro label (D-084): world-space signs like the fork room's "EXIT" sign and the
    /// "BASEMENT" plate on the black door (D-150, D-166), and (R5) fixed UI labels such as menu and end-screen buttons. Uses the same safe LocalizedTextBinding as the HUD:
    /// if localization isn't set up it shows the key once instead of logging errors. Event-driven, no per-frame work.
    /// </summary>
    public class LocalizedWorldText : MonoBehaviour
    {
        [Tooltip("Table 'UI', e.g. key 'sign.exit'.")]
        [SerializeField] LocalizedString text = new LocalizedString();

        TMP_Text label;
        LocalizedTextBinding binding;

        /// <summary>The localized string shown. Set by the scene builder; changing it at runtime takes effect on re-enable.</summary>
        public LocalizedString Text { get => text; set => text = value; }

        void Awake()
        {
            label = GetComponent<TMP_Text>(); // a TextMeshPro (3D) label on the same object
            if (label == null) Debug.LogWarning($"[LocalizedWorldText] '{name}' has no TextMeshPro label.", this);
            binding = new LocalizedTextBinding(this, HandleText);
        }

        void OnEnable() => binding.Set(text);
        void OnDisable() => binding.Clear();

        void HandleText(string value)
        {
            if (label != null) label.text = value;
        }
    }
}
