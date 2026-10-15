using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Placeholder animation of the Preacher (D-068 look: elongated, four arms; the real rig is a Blender task) with
    /// transform tweens only: a slow hover-sway, the four arms rising during a volley wind-up and while casting, and a quick
    /// "pop" in size when it appears after a teleport (D-184). The brain calls Apply once per frame; no allocations.
    /// </summary>
    public class PreacherVisuals : MonoBehaviour
    {
        [Tooltip("Pivot of the whole model (child of the Preacher root, at the feet).")]
        [SerializeField] Transform model;
        [Tooltip("The four arm pivots (at the shoulders).")]
        [SerializeField] Transform[] arms = new Transform[0];
        [Tooltip("How far the arms swing up when casting (degrees).")]
        [SerializeField] float raiseDegrees = 110f;
        [Tooltip("Seconds of the appear pop.")]
        [SerializeField] float popSeconds = 0.25f;

        Quaternion[] armRest;
        float popStart = -100f;

        void Awake()
        {
            armRest = new Quaternion[arms.Length];
            for (int i = 0; i < arms.Length; i++)
                armRest[i] = arms[i] != null ? arms[i].localRotation : Quaternion.identity;
        }

        /// <summary>It just appeared at a cavity: play the size pop.</summary>
        public void Pop() => popStart = Time.time;

        /// <param name="cast01">0 = arms down, 1 = arms fully raised (volley).</param>
        public void Apply(float cast01)
        {
            float time = Time.time;
            if (model != null)
            {
                model.localPosition = new Vector3(0f, Mathf.Sin(time * 1.3f) * 0.08f, 0f);
                model.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 0.9f) * 2.5f);
                float pop = Mathf.Clamp01((time - popStart) / Mathf.Max(0.01f, popSeconds));
                float scale = pop < 1f ? Mathf.Lerp(0.2f, 1f, 1f - (1f - pop) * (1f - pop)) : 1f;
                model.localScale = new Vector3(scale, scale, scale);
            }
            for (int i = 0; i < arms.Length; i++)
            {
                if (arms[i] == null) continue;
                float wobble = Mathf.Sin(time * 2f + i) * 6f;
                arms[i].localRotation = armRest[i] * Quaternion.Euler(-raiseDegrees * cast01 + wobble, 0f, 0f);
            }
        }
    }
}
