using UnityEngine;

namespace TWR.Combat
{
    /// <summary>
    /// Cheap muzzle flash (D-123, D-100): a bright unlit quad (Bloom makes it glow) and a short
    /// point light, both shown for a few hundredths of a second. The quad gets a random roll each shot
    /// so repeated flashes don't look identical. No particles, no allocations.
    /// </summary>
    public class MuzzleFlash : MonoBehaviour
    {
        [SerializeField] Renderer flashRenderer;
        [SerializeField] Light flashLight;

        float hideTime;
        bool showing;

        void Awake()
        {
            Show(false);
        }

        void OnDisable()
        {
            Show(false);
        }

        public void Play(float duration)
        {
            hideTime = Time.time + duration;
            if (flashRenderer != null)
                flashRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            Show(true);
        }

        void Update()
        {
            if (showing && Time.time >= hideTime) Show(false);
        }

        void Show(bool on)
        {
            showing = on;
            if (flashRenderer != null) flashRenderer.enabled = on;
            if (flashLight != null) flashLight.enabled = on;
        }
    }
}
