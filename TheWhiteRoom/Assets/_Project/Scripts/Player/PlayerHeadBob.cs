using UnityEngine;

namespace TWR.Player
{
    /// <summary>
    /// Optional head bob on the camera while walking. OFF by default because the Settings toggle
    /// for it is still open in the GDD (D-090). Bob speed follows distance walked so it matches footsteps.
    /// Sits on the Camera object; only writes the camera's localPosition.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class PlayerHeadBob : MonoBehaviour
    {
        [SerializeField] PlayerTuning tuning;
        [SerializeField] PlayerLocomotion locomotion;
        [Tooltip("How fast the camera settles back when you stop.")]
        [SerializeField] float returnSpeed = 6f;

        float phase;
        Vector3 restPosition;

        void Awake()
        {
            if (locomotion == null) locomotion = GetComponentInParent<PlayerLocomotion>();
            restPosition = transform.localPosition;
        }

        void LateUpdate()
        {
            if (tuning == null || locomotion == null) return;

            bool bobbing = tuning.headBobEnabled && locomotion.IsGrounded && locomotion.HorizontalSpeed > 0.1f;
            if (bobbing)
            {
                phase += locomotion.HorizontalSpeed * tuning.headBobFrequency * Mathf.PI * 2f * Time.deltaTime;
                if (phase > Mathf.PI * 2f) phase -= Mathf.PI * 2f;
                float y = Mathf.Sin(phase * 2f) * tuning.headBobAmount;
                float x = Mathf.Cos(phase) * tuning.headBobAmount * 0.5f;
                transform.localPosition = restPosition + new Vector3(x, y, 0f);
            }
            else
            {
                transform.localPosition = Vector3.Lerp(transform.localPosition, restPosition, returnSpeed * Time.deltaTime);
            }
        }
    }
}
