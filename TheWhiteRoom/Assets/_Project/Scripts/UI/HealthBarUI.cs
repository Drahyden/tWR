using UnityEngine;
using UnityEngine.UI;
using TWR.Player;

namespace TWR.UI
{
    /// <summary>
    /// HP bar on the HUD (D-034, D-086, D-122). Listens to PlayerHealth.Changed and stretches the
    /// fill image's right edge to the HP fraction (works without a sprite). Turns red when HP is low.
    /// Event-driven: no work per frame. No text, so nothing to localize.
    /// </summary>
    public class HealthBarUI : MonoBehaviour
    {
        [SerializeField] PlayerHealth health;
        [Tooltip("Fill RectTransform inside the bar background. Its anchorMax.x is set to the HP fraction.")]
        [SerializeField] RectTransform fill;
        [SerializeField] Image fillImage;
        [SerializeField] Color normalColour = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        [SerializeField] Color lowColour = new Color(0.8f, 0.1f, 0.1f, 0.95f);
        [Range(0f, 1f)] [SerializeField] float lowThreshold = 0.3f;

        void Awake()
        {
            if (health == null) health = FindFirstObjectByType<PlayerHealth>(); // once, not in Update
            if (fillImage == null && fill != null) fillImage = fill.GetComponent<Image>();
        }

        void OnEnable()
        {
            if (health == null) return;
            health.Changed += HandleChanged;
            HandleChanged(health.Current, health.Max);
        }

        void OnDisable()
        {
            if (health != null) health.Changed -= HandleChanged;
        }

        void HandleChanged(float current, float max)
        {
            float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (fill != null)
            {
                Vector2 anchorMax = fill.anchorMax;
                anchorMax.x = fraction;
                fill.anchorMax = anchorMax;
            }
            if (fillImage != null)
            {
                fillImage.color = fraction <= lowThreshold ? lowColour : normalColour;
                fillImage.enabled = fraction > 0f; // an empty bar shows nothing (avoids a flipped sliver)
            }
        }
    }
}
