using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TWR.Enemies;

namespace TWR.UI
{
    /// <summary>
    /// The boss HP bar at the top of the HUD (D-185, D-086). Lives in the Core scene's HUD and has no reference to any
    /// boss (D-161): it appears on BossEvents.Started, shows the boss's localized name (BossData.displayName via
    /// LocalizedTextBinding, D-084) and follows BossHealth.Changed; it hides on BossEvents.Defeated or Despawned
    /// (death fade finished or the arena unloaded). Event-driven: no work per frame.
    /// </summary>
    public class BossHealthBarUI : MonoBehaviour
    {
        [Tooltip("Shown/hidden with alpha (stays active so it keeps listening).")]
        [SerializeField] CanvasGroup group;
        [Tooltip("Fill RectTransform inside the bar; its anchorMax.x is set to the HP fraction.")]
        [SerializeField] RectTransform fill;
        [SerializeField] Image fillImage;
        [SerializeField] TMP_Text nameLabel;
        [SerializeField] Color normalColour = new Color(0.12f, 0.12f, 0.12f, 0.9f);
        [SerializeField] Color phaseTwoColour = new Color(0.55f, 0.05f, 0.05f, 0.95f);

        LocalizedTextBinding nameBinding;
        BossHealth tracked;
        GameObject trackedBoss;

        void Awake()
        {
            nameBinding = new LocalizedTextBinding(this, SetName); // "[key]" if localization is unavailable
            SetVisible(false);
        }

        void OnEnable()
        {
            BossEvents.Started += HandleStarted;
            BossEvents.PhaseChanged += HandlePhaseChanged;
            BossEvents.Defeated += HandleDefeated;
            BossEvents.Despawned += HandleDespawned;
        }

        void OnDisable()
        {
            BossEvents.Started -= HandleStarted;
            BossEvents.PhaseChanged -= HandlePhaseChanged;
            BossEvents.Defeated -= HandleDefeated;
            BossEvents.Despawned -= HandleDespawned;
            Untrack();
        }

        void HandleStarted(GameObject boss, string bossId)
        {
            Untrack();
            if (boss == null) return;
            tracked = boss.GetComponent<BossHealth>(); // once per fight
            trackedBoss = boss;
            if (tracked == null) return;
            tracked.Changed += HandleChanged;
            BossEncounter encounter = boss.GetComponent<BossEncounter>();
            nameBinding.Set(encounter != null && encounter.Data != null ? encounter.Data.displayName : null);
            if (fillImage != null) fillImage.color = tracked.Phase >= 2 ? phaseTwoColour : normalColour;
            HandleChanged(tracked.Health, tracked.MaxHealth);
            SetVisible(true);
        }

        void HandlePhaseChanged(GameObject boss, string bossId, int phase)
        {
            if (boss == trackedBoss && fillImage != null) fillImage.color = phase >= 2 ? phaseTwoColour : normalColour;
        }

        void HandleDefeated(GameObject boss, string bossId)
        {
            if (boss == trackedBoss) Untrack();
        }

        void HandleDespawned(GameObject boss)
        {
            if (boss == trackedBoss) Untrack();
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
            if (fillImage != null) fillImage.enabled = fraction > 0f;
        }

        void Untrack()
        {
            if (tracked != null) tracked.Changed -= HandleChanged;
            tracked = null;
            trackedBoss = null;
            nameBinding?.Clear();
            SetVisible(false);
        }

        void SetName(string text)
        {
            if (nameLabel != null) nameLabel.text = text;
        }

        void SetVisible(bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
    }
}
