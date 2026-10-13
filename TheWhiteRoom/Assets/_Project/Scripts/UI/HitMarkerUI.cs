using UnityEngine;
using UnityEngine.UI;
using TWR.Combat;

namespace TWR.UI
{
    /// <summary>
    /// Hit marker (D-091, D-122): a small X around the crosshair dot that flashes for a moment
    /// when a bullet damages something or triggers a puzzle object. Kills flash red and slightly bigger.
    /// Listens to WeaponInventory.HitLanded; fades a CanvasGroup, so no allocations.
    /// </summary>
    public class HitMarkerUI : MonoBehaviour
    {
        [SerializeField] WeaponInventory weapons;
        [SerializeField] CanvasGroup group;
        [Tooltip("The X's bar images, recoloured for kills.")]
        [SerializeField] Graphic[] bars;
        [SerializeField] float flashSeconds = 0.15f;
        [SerializeField] Color hitColour = new Color(0.1f, 0.1f, 0.1f, 1f);
        [SerializeField] Color killColour = new Color(0.85f, 0.1f, 0.1f, 1f);
        [SerializeField] float killScale = 1.4f;

        float timer;

        void Awake()
        {
            if (weapons == null) weapons = FindFirstObjectByType<WeaponInventory>(); // once, not in Update
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 0f;
        }

        void OnEnable()
        {
            if (weapons != null) weapons.HitLanded += HandleHit;
        }

        void OnDisable()
        {
            if (weapons != null) weapons.HitLanded -= HandleHit;
            timer = 0f;
            if (group != null) group.alpha = 0f;
        }

        void HandleHit(HitResult result)
        {
            bool kill = (result & HitResult.Killed) != 0;
            Color colour = kill ? killColour : hitColour;
            if (bars != null)
                foreach (Graphic bar in bars)
                    if (bar != null) bar.color = colour;
            transform.localScale = Vector3.one * (kill ? killScale : 1f);
            timer = flashSeconds;
            if (group != null) group.alpha = 1f;
        }

        void Update()
        {
            if (timer <= 0f) return;
            timer -= Time.deltaTime;
            if (group != null) group.alpha = Mathf.Clamp01(timer / flashSeconds);
        }
    }
}
