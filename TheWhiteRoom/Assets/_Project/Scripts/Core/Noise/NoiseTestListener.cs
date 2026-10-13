using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Test-only listener: logs to the Console and flashes its renderer when it hears a noise.
    /// Stand-in "ears" to prove the noise system works (D-043, D-079). It also shows wall muffling
    /// (D-131, D-116): the log says e.g. "muffled by 1 wall (x0.50), effective 5.0 m", and noises that
    /// reached it in raw radius but were muffled out are logged as NOT heard. Logs build strings: test scenes only.
    /// </summary>
    public class NoiseTestListener : MonoBehaviour, INoiseListener
    {
        [SerializeField] bool logToConsole = true;
        [Tooltip("Also log noises that were inside the raw radius but muffled below the distance by walls.")]
        [SerializeField] bool logMuffledOut = true;
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
        NoiseSystem.ListenerCheckedHandler onChecked;

        public Vector3 HearingPosition => transform.position;

        void Awake()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            if (targetRenderer != null && targetRenderer.sharedMaterial != null
                && targetRenderer.sharedMaterial.HasProperty(BaseColorId))
                idleColour = targetRenderer.sharedMaterial.GetColor(BaseColorId);
            onChecked = HandleChecked; // cached delegate
        }

        void OnEnable()
        {
            NoiseSystem.Register(this);
            NoiseSystem.ListenerChecked += onChecked;
        }

        void OnDisable()
        {
            NoiseSystem.Unregister(this);
            NoiseSystem.ListenerChecked -= onChecked;
        }

        public void OnNoiseHeard(in HeardNoise heard)
        {
            if (ignoreOwnNoises && heard.Noise.Source == gameObject) return;

            if (logToConsole)
                Debug.Log($"[NoiseTestListener] '{name}' heard {Describe(heard)}", this);

            flashColour = NoiseDebugDrawer.ColorFor(heard.Noise.Type);
            flashTimer = flashSeconds;
            ApplyColour(flashColour);
        }

        // Noises that were in raw range but walls made them too quiet to reach us.
        void HandleChecked(in HeardNoise result)
        {
            if (!logToConsole || !logMuffledOut || result.IsHeard) return;
            if (!ReferenceEquals(result.Listener, this)) return;
            if (ignoreOwnNoises && result.Noise.Source == gameObject) return;
            Debug.Log($"[NoiseTestListener] '{name}' did NOT hear {Describe(result)}", this);
        }

        static string Describe(in HeardNoise heard)
        {
            NoiseEvent noise = heard.Noise;
            string source = noise.Source != null ? noise.Source.name : "unknown";
            string walls = heard.WallCount == 0
                ? $"clear (no wall), radius {noise.Radius:0.0} m"
                : $"muffled by {heard.WallCount} wall{(heard.WallCount > 1 ? "s" : "")} (x{heard.MuffleFactor:0.00}), " +
                  $"effective {heard.EffectiveRadius:0.0} m of {noise.Radius:0.0} m";
            return $"{noise.Type} from {source} at {heard.Distance:0.0} m: {walls}";
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
