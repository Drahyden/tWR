using UnityEngine;
using TWR.Player;

namespace TWR.Enemies
{
    /// <summary>
    /// Big arenas need a longer view distance (D-193: camera far clip at least 250 m; the Bull arena is 200 m long, the
    /// Preacher arena 160 m tall). While this arena scene is loaded it raises the persistent player camera's far clip
    /// plane (Core scene, D-161; found once on enable) and puts the old value back when the arena unloads, so the other
    /// sections and the Test_PlayerController sandbox keep their 200 m. Portal cameras copy the player camera's clip planes
    /// (D-141), and arenas have no portals, so portals are unaffected.
    /// </summary>
    public class ArenaCameraFarClip : MonoBehaviour
    {
        [Tooltip("Far clip plane while this arena is loaded (m). D-193: at least 250.")]
        [SerializeField, Min(50f)] float farClip = 300f;

        Camera playerCamera;
        float previous;

        public float FarClip => farClip;

        void OnEnable()
        {
            PlayerController player = FindFirstObjectByType<PlayerController>(); // once per arena load
            playerCamera = player != null ? player.PlayerCamera : null;
            if (playerCamera == null) return;
            previous = playerCamera.farClipPlane;
            if (playerCamera.farClipPlane < farClip) playerCamera.farClipPlane = farClip;
        }

        void OnDisable()
        {
            if (playerCamera != null) playerCamera.farClipPlane = previous;
            playerCamera = null;
        }
    }
}
