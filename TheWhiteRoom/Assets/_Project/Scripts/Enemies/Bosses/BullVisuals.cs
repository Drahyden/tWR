using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>What the Bull's body is doing, for BullVisuals (D-181).</summary>
    public enum BullPose
    {
        Idle,
        Walk,
        Telegraph,
        Charge,
        Skid,
        Stun,
        StompWindUp,
        Recover
    }

    /// <summary>
    /// Placeholder animation of the Bull (D-069 look, D-181 telegraphs; the real quadruped rig is a Blender task) using
    /// only transform tweens and a colour tint, no Animator:
    /// - charge telegraph: paws with a front leg, roars (head shake) and lowers its head; tinted warm;
    /// - FAST charge telegraph (D-182): the same moves but the body flashes RED, clearly different;
    /// - charge: leans forward, legs gallop; stun: head hangs, body sways, pale blue pulse (visible, D-181);
    /// - stomp: rears up on its hind legs during the wind-up, slams down.
    /// The brain calls Apply once per frame. The tint uses one MaterialPropertyBlock and is only re-applied when the
    /// colour changes (no allocations, D-100).
    /// </summary>
    public class BullVisuals : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Pivot of the whole model (child of the Bull root, at the feet).")]
        [SerializeField] Transform model;
        [SerializeField] Transform head;
        [SerializeField] Transform legFrontLeft;
        [SerializeField] Transform legFrontRight;
        [SerializeField] Transform legBackLeft;
        [SerializeField] Transform legBackRight;
        [Tooltip("Skin renderers that get the telegraph / stun tint.")]
        [SerializeField] Renderer[] tintRenderers = new Renderer[0];
        [SerializeField] Color telegraphTint = new Color(1f, 0.72f, 0.55f);
        [SerializeField] Color fastTelegraphTint = new Color(1f, 0.05f, 0.05f);
        [SerializeField] Color stunTint = new Color(0.55f, 0.68f, 1f);

        MaterialPropertyBlock block;
        Color[] baseColours;
        Color lastTint = new Color(-1f, -1f, -1f, -1f);
        float gait;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            baseColours = new Color[tintRenderers.Length];
            for (int i = 0; i < tintRenderers.Length; i++)
            {
                Material m = tintRenderers[i] != null ? tintRenderers[i].sharedMaterial : null;
                baseColours[i] = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
            }
        }

        /// <param name="t01">Progress through the current pose (telegraph, stun, wind-up), 0..1.</param>
        /// <param name="fast">The current/next charge is a fast one (red telegraph).</param>
        /// <param name="speed">Current ground speed (m/s), drives the leg swing.</param>
        public void Apply(BullPose pose, float t01, bool fast, float speed)
        {
            float time = Time.time;
            float modelPitch = 0f, modelRoll = 0f, modelLift = 0f, headPitch = 0f, headYaw = 0f, paw = 0f;
            float gaitRate = 0f;
            Color tint = Color.white;
            float tintAmount = 0f;

            switch (pose)
            {
                case BullPose.Walk:
                    gaitRate = speed * 1.6f;
                    break;
                case BullPose.Telegraph:
                    // Paw (first 60%), roar = head shake (30-75%), head lowers over the whole telegraph.
                    if (t01 < 0.6f) paw = Mathf.Sin(time * 14f) * 35f;
                    if (t01 > 0.3f && t01 < 0.75f) headYaw = Mathf.Sin(time * 40f) * 8f;
                    headPitch = Mathf.Lerp(0f, 28f, t01);
                    modelPitch = Mathf.Lerp(0f, 5f, t01);
                    tint = fast ? fastTelegraphTint : telegraphTint;
                    tintAmount = fast ? (Mathf.Sin(time * 50f) > 0f ? 1f : 0.35f) : 0.4f + 0.3f * t01;
                    break;
                case BullPose.Charge:
                    gaitRate = speed * 1.2f;
                    headPitch = 28f;
                    modelPitch = 8f;
                    if (fast) { tint = fastTelegraphTint; tintAmount = 0.5f; }
                    break;
                case BullPose.Skid:
                    headPitch = 20f;
                    modelPitch = -4f;
                    break;
                case BullPose.Stun:
                    headPitch = 40f;
                    modelRoll = Mathf.Sin(time * 3f) * 7f;
                    modelPitch = 6f;
                    tint = stunTint;
                    tintAmount = 0.45f + 0.35f * Mathf.Sin(time * 6f);
                    break;
                case BullPose.StompWindUp:
                    modelPitch = Mathf.Lerp(0f, -24f, t01);
                    modelLift = Mathf.Lerp(0f, 0.6f, t01);
                    headPitch = -10f;
                    tint = telegraphTint;
                    tintAmount = 0.3f + 0.4f * t01;
                    break;
                case BullPose.Recover:
                    headPitch = Mathf.Lerp(20f, 0f, t01);
                    break;
            }

            gait += gaitRate * Time.deltaTime;
            float swing = gaitRate > 0f ? Mathf.Sin(gait) * 30f : 0f;

            if (model != null)
            {
                model.localRotation = Quaternion.Euler(modelPitch, 0f, modelRoll);
                model.localPosition = new Vector3(0f, modelLift, 0f);
            }
            if (head != null) head.localRotation = Quaternion.Euler(headPitch, headYaw, 0f);
            SetLeg(legFrontLeft, swing + paw);
            SetLeg(legFrontRight, -swing);
            SetLeg(legBackLeft, -swing);
            SetLeg(legBackRight, swing);
            SetTint(tintAmount > 0f ? tint : Color.white, Mathf.Clamp01(tintAmount));
        }

        static void SetLeg(Transform leg, float pitch)
        {
            if (leg != null) leg.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void SetTint(Color tint, float amount)
        {
            Color key = new Color(tint.r, tint.g, tint.b, amount);
            if (key == lastTint) return;
            lastTint = key;
            for (int i = 0; i < tintRenderers.Length; i++)
            {
                if (tintRenderers[i] == null) continue;
                tintRenderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color.Lerp(baseColours[i], tint, amount));
                tintRenderers[i].SetPropertyBlock(block);
            }
        }
    }
}
