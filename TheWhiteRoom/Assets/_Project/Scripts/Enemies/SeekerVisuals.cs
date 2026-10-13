using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Everything you SEE of the placeholder Seeker (D-067 pale fleshy capsule with a black eye):
    /// a coloured emissive band shows its state for testing (D-133: patrol white, suspicious yellow,
    /// investigate orange, chase/attack red, search purple, return pale blue); the melee wind-up leans the body
    /// back and flashes the band (D-134 telegraph); hits flash the skin red; death tips the body over,
    /// darkens it and shrinks it away (D-136, placeholder for D-051). Uses MaterialPropertyBlocks and
    /// only writes when something changed, so idle Seekers cost nothing (D-100). No text, no UI.
    /// </summary>
    public class SeekerVisuals : MonoBehaviour
    {
        [Tooltip("Visual root with its pivot at the feet. Leans and tips over; the collider/agent stay on the Seeker root.")]
        [SerializeField] Transform body;
        [SerializeField] Renderer skinRenderer;
        [Tooltip("Emissive ring that shows the state colour.")]
        [SerializeField] Renderer stateBand;

        [Header("State colours (testing aid, not player-facing text)")]
        [SerializeField] Color patrolColour = new Color(0.85f, 0.85f, 0.85f);
        [SerializeField] Color suspiciousColour = new Color(1f, 0.9f, 0.1f);
        [SerializeField] Color investigateColour = new Color(1f, 0.45f, 0.05f);
        [SerializeField] Color hostileColour = new Color(1f, 0.05f, 0.05f);
        [SerializeField] Color searchColour = new Color(0.6f, 0.15f, 1f);
        [SerializeField] Color returnColour = new Color(0.55f, 0.75f, 1f);
        [SerializeField] float bandEmission = 2.5f;

        [Header("Telegraph / hit / death")]
        [SerializeField] float windUpLeanDegrees = 22f;
        [SerializeField] float strikeLungeDegrees = 14f;
        [SerializeField] float strikeSeconds = 0.18f;
        [SerializeField] float hurtFlashSeconds = 0.12f;
        [SerializeField] Color hurtColour = new Color(0.9f, 0.1f, 0.1f);
        [SerializeField] Color deadTint = new Color(0.25f, 0.18f, 0.16f);
        [SerializeField] float collapseDegrees = 85f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock skinBlock;
        MaterialPropertyBlock bandBlock;
        Color skinColour = Color.white;
        Color bandColour;
        Quaternion bodyRestRotation;
        Vector3 bodyRestScale;
        float hurtTimer;
        float strikeTimer;
        bool windingUp;
        Vector3 fallAxis = Vector3.right;

        void Awake()
        {
            skinBlock = new MaterialPropertyBlock();
            bandBlock = new MaterialPropertyBlock();
            if (body == null) body = transform;
            bodyRestRotation = body.localRotation;
            bodyRestScale = body.localScale;
            if (skinRenderer != null && skinRenderer.sharedMaterial != null && skinRenderer.sharedMaterial.HasProperty(BaseColorId))
                skinColour = skinRenderer.sharedMaterial.GetColor(BaseColorId);
            ShowState(SeekerState.Patrol);
        }

        /// <summary>Band colour for a state. Dead turns the band off.</summary>
        public void ShowState(SeekerState state)
        {
            Color c;
            switch (state)
            {
                case SeekerState.Suspicious:  c = suspiciousColour; break;
                case SeekerState.Investigate: c = investigateColour; break;
                case SeekerState.Chase:
                case SeekerState.Attack:      c = hostileColour; break;
                case SeekerState.Search:      c = searchColour; break;
                case SeekerState.Return:      c = returnColour; break;
                case SeekerState.Dead:        c = Color.black; break;
                default:                      c = patrolColour; break;
            }
            bandColour = c;
            SetBand(c, state == SeekerState.Dead ? 0f : bandEmission);
        }

        /// <summary>Melee telegraph, t = 0..1 through the wind-up: lean back further and flash the band brighter.</summary>
        public void SetWindUp(float t)
        {
            windingUp = true;
            t = Mathf.Clamp01(t);
            body.localRotation = bodyRestRotation * Quaternion.Euler(-windUpLeanDegrees * t, 0f, 0f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 6f);
            SetBand(Color.Lerp(bandColour, Color.white, pulse), bandEmission * (1f + 2f * t));
        }

        /// <summary>Wind-up over: quick forward lunge (struck = true) or straight back to rest (cancelled).</summary>
        public void EndWindUp(bool struck)
        {
            windingUp = false;
            strikeTimer = struck ? strikeSeconds : 0f;
            body.localRotation = bodyRestRotation;
            SetBand(bandColour, bandEmission);
        }

        public void FlashHurt()
        {
            hurtTimer = hurtFlashSeconds;
            SetSkin(hurtColour);
        }

        /// <summary>Death begins: falls away from the killing blow's direction.</summary>
        public void BeginDeath(Vector3 hitDirection)
        {
            windingUp = false;
            strikeTimer = 0f;
            hurtTimer = 0f;
            ShowState(SeekerState.Dead);
            Vector3 flat = Vector3.ProjectOnPlane(hitDirection, Vector3.up);
            if (flat.sqrMagnitude < 0.001f) flat = -transform.forward; // no direction: fall backwards
            fallAxis = Vector3.Cross(Vector3.up, flat.normalized);
        }

        /// <summary>collapse01: tipping over + darkening. fade01: shrinking away.</summary>
        public void SetDeath(float collapse01, float fade01)
        {
            float ease = collapse01 * collapse01; // accelerate like a fall
            body.rotation = Quaternion.AngleAxis(collapseDegrees * ease, fallAxis) * transform.rotation * bodyRestRotation;
            SetSkin(Color.Lerp(skinColour, deadTint, collapse01));
            body.localScale = bodyRestScale * (1f - Mathf.Clamp01(fade01));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (hurtTimer > 0f)
            {
                hurtTimer -= dt;
                SetSkin(hurtTimer > 0f ? Color.Lerp(skinColour, hurtColour, hurtTimer / hurtFlashSeconds) : skinColour);
            }
            if (strikeTimer > 0f && !windingUp)
            {
                strikeTimer -= dt;
                float k = Mathf.Clamp01(strikeTimer / strikeSeconds);
                body.localRotation = bodyRestRotation * Quaternion.Euler(strikeLungeDegrees * Mathf.Sin(k * Mathf.PI), 0f, 0f);
            }
        }

        void SetSkin(Color c)
        {
            if (skinRenderer == null) return;
            skinRenderer.GetPropertyBlock(skinBlock);
            skinBlock.SetColor(BaseColorId, c);
            skinRenderer.SetPropertyBlock(skinBlock);
        }

        void SetBand(Color c, float emission)
        {
            if (stateBand == null) return;
            stateBand.GetPropertyBlock(bandBlock);
            bandBlock.SetColor(BaseColorId, c);
            bandBlock.SetColor(EmissionColorId, c * emission);
            stateBand.SetPropertyBlock(bandBlock);
        }
    }
}
