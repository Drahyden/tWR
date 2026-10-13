using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Test-only listener: logs to the Console and flashes its renderer when it hears a noise.
    /// Stand-in for a future Seeker's "ears" (D-043) to prove the noise system works (D-079).
    /// </summary>
    public class NoiseTestListener : MonoBehaviour, INoiseListener
    {
        [SerializeField] bool logToConsole = true;
        [SerializeField] bool ignoreOwnNoises = true;
        [SerializeField] float flashSeconds = 0.4f;
        [Tooltip("Renderer to flash. Auto-found on this object if empty.")]
        [SerializeField] Renderer targetRenderer;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock block;
        Color idleColour = Color.white;
        Color flashColour = Color.white;
        float flashTimer;

        public Vector3 HearingPosition => transform.position;

        void Awake()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            if (targetRenderer != null && targetRenderer.sharedMaterial != null
                && targetRenderer.sharedMaterial.HasProperty(BaseColorId))
                idleColour = targetRenderer.sharedMaterial.GetColor(BaseColorId);
        }

        void OnEnable() => NoiseSystem.Register(this);
        void OnDisable() => NoiseSystem.Unregister(this);

        public void OnNoiseHeard(NoiseEvent noise)
        {
            if (ignoreOwnNoises && noise.Source == gameObject) return;

            if (logToConsole)
            {
                float distance = Vector3.Distance(noise.Position, HearingPosition);
                Debug.Log($"[NoiseTestListener] '{name}' heard {noise.Type} from " +
                          $"{(noise.Source != null ? noise.Source.name : "unknown")} at {distance:0.0} m (radius {noise.Radius:0.0} m)", this);
            }

            flashColour = NoiseDebugDrawer.ColorFor(noise.Type);
            flashTimer = flashSeconds;
            ApplyColour(flashColour);
        }

        void Update()
        {
            if (flashTimer <= 0f) return;

            flashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(flashTimer / flashSeconds);
            ApplyColour(Color.Lerp(idleColour, flashColour, t));
        }

        void ApplyColour(Color c)
        {
            if (targetRenderer == null) return;
            targetRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(EmissionColorId, c * 2f);
            targetRenderer.SetPropertyBlock(block);
        }
    }
}
