using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TWR.Portals
{
    /// <summary>
    /// All tunable numbers for the see-through portals (D-097, D-100) in one asset.
    /// Implements D-140 (budget: render-texture resolution scale, at most 2 portals rendered,
    /// no portal-in-portal), the D-142 crossing numbers (near-plane box, oblique-plane limits) and
    /// holds the dedicated "Portal" URP renderer from D-139. Built and filled by TWR > Build Test Scene.
    /// Create via: Project window > right-click > Create > TWR > Portal Settings.
    /// </summary>
    [CreateAssetMenu(fileName = "PortalSettings", menuName = "TWR/Portal Settings")]
    public class PortalSettings : ScriptableObject
    {
        [Header("Budget - D-140 (D-097, D-100)")]
        [Tooltip("Portal render-texture size as a fraction of the screen. 1 = full resolution, 0.5 = half (cheaper, softer).")]
        [Range(0.5f, 1f)] public float resolutionScale = 1f;
        [Tooltip("At most this many portals render a live view per frame (the nearest visible ones). " +
                 "Others that are visible show the flat white fallback. 0 = test the fallback (no live views).")]
        [Min(0)] public int maxRenderedPortals = 2;
        [Tooltip("Portal-in-portal depth. Fixed at 0 for the slice: a portal seen inside a portal view is flat white. " +
                 "Higher values are not supported yet.")]
        [Range(0, 0)] public int recursionDepth = 0;

        [Header("Crossing without pops - D-142")]
        [Tooltip("When the player camera is closer than this to a portal (on its front side), the flat screen becomes " +
                 "a box that reaches behind the portal, so the camera's near plane can never cut a hole in it.")]
        [Min(0.05f)] public float nearModeDistance = 0.3f;
        [Tooltip("Depth of that box behind the portal plane (m).")]
        [Min(0.05f)] public float nearBoxDepth = 0.25f;
        [Tooltip("Thickness of the flat screen in normal mode (m). Tiny, just so it is a closed box.")]
        [Min(0.0005f)] public float flatThickness = 0.002f;
        [Tooltip("If the portal camera is closer than this to the far portal's plane, the oblique (slanted) near plane " +
                 "is skipped for that frame, because it produces artefacts that close.")]
        [Min(0f)] public float obliqueMinDistance = 0.05f;
        [Tooltip("The oblique near plane is pulled this far towards the portal camera, so floors that touch the " +
                 "portal plane are never cut with a visible seam.")]
        [Min(0f)] public float clipPlaneOffset = 0.02f;

        [Header("Rendering - D-139")]
        [Tooltip("The dedicated 'Portal' Universal Renderer (no SSAO, no Render Objects, no depth/opaque texture). " +
                 "Must also be in the active URP asset's renderer list. Assigned by TWR > Build Test Scene.")]
        public ScriptableRendererData portalRenderer;
        [Tooltip("Depth buffer bits of the portal render texture.")]
        public int depthBufferBits = 24;

        void OnValidate()
        {
            recursionDepth = 0; // see tooltip: the slice never draws a portal inside a portal (D-140)
            resolutionScale = Mathf.Clamp(resolutionScale, 0.5f, 1f);
            if (depthBufferBits != 16 && depthBufferBits != 24 && depthBufferBits != 32) depthBufferBits = 24;
        }
    }
}
