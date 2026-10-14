using UnityEngine;

namespace TWR.Run
{
    /// <summary>
    /// Where the player appears when a section (or the lobby) loads (D-165, D-167). Exactly one per content scene,
    /// inside the entry vestibule with the one-way locked entry door behind it (D-145 locked mode). The SectionLoader
    /// teleports the persistent player here (feet = this position, facing = this object's yaw) while the screen is
    /// white. Only a marker: no logic, no per-frame cost.
    /// </summary>
    [DisallowMultipleComponent]
    public class SectionEntry : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Vector3 feet = transform.position;
            Gizmos.color = new Color(0.2f, 1f, 0.4f);
            Gizmos.DrawWireCube(feet + Vector3.up * 0.9f, new Vector3(0.7f, 1.8f, 0.7f));
            Vector3 eye = feet + Vector3.up * 1.65f;
            Gizmos.DrawLine(eye, eye + transform.forward * 1f);
        }
    }
}
