using UnityEngine;

namespace TWR.Player
{
    /// <summary>
    /// Mouse look (D-032): turns the whole body left/right (yaw) and tilts the Head up/down (pitch).
    /// Stores yaw/pitch itself so portals can set them exactly via SetLookRotation (D-097).
    /// Also applies the field of view from PlayerTuning to the player camera.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] PlayerTuning tuning;
        [SerializeField] PlayerInputReader input;
        [Tooltip("Child transform at eye height. Pitch is applied here.")]
        [SerializeField] Transform head;
        [SerializeField] Camera playerCamera;

        float yaw;
        float pitch;

        public float Yaw => yaw;
        public float Pitch => pitch;
        public Transform Head => head;
        public Camera PlayerCamera => playerCamera;

        void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (head == null) head = transform.Find("Head");

            yaw = transform.eulerAngles.y;
            pitch = 0f;
            ApplyFieldOfView();
        }

        public void ApplyFieldOfView()
        {
            if (playerCamera != null && tuning != null)
                playerCamera.fieldOfView = tuning.fieldOfView;
        }

        void Update()
        {
            if (tuning == null || input == null) return;

            // Mouse delta is already "per frame", so no Time.deltaTime here.
            Vector2 look = input.Look * tuning.mouseSensitivity;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, tuning.minPitch, tuning.maxPitch);
            ApplyRotation();
        }

        /// <summary>Set the view direction instantly (used by Teleport / portals, D-097). Roll is ignored.</summary>
        public void SetLookRotation(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            yaw = euler.y;
            float min = tuning != null ? tuning.minPitch : -85f;
            float max = tuning != null ? tuning.maxPitch : 85f;
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), min, max);
            ApplyRotation();
        }

        void ApplyRotation()
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (head != null) head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
