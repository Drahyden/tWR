using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TWR.Portals
{
    /// <summary>
    /// The camera that films the view through one portal (proposed D-139, D-141). It is a standalone Base
    /// camera (never an overlay or camera stack, D-123) that renders into a runtime HDR RenderTexture before the
    /// main camera (lower depth). Every frame it is placed where the player camera WOULD be on the other side
    /// (same FOV, aspect, near/far and full rotation including lean roll) and gets an oblique near plane on the
    /// destination portal, so nothing between it and that portal is drawn. No post-processing here: bloom and
    /// tonemapping are applied once by the main camera. It renders only while PortalRenderManager says so;
    /// otherwise the Camera component is disabled and costs nothing (D-100).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PortalCamera : MonoBehaviour
    {
        [Tooltip("The portal whose screen shows this camera's picture (the camera itself sits behind the destination).")]
        [SerializeField] Portal portal;

        Camera cam;
        UniversalAdditionalCameraData cameraData;
        RenderTexture texture;
        int rendererIndex = int.MinValue;

        public Camera Camera => cam;
        public Portal Portal => portal;
        public RenderTexture Texture => texture;
        public bool IsRendering => cam != null && cam.enabled;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.enabled = false;        // never draws until the manager asks
            cam.targetTexture = null;   // texture is made at runtime only, never saved (D-139)
            cam.allowHDR = true;        // HDR in, the main camera does bloom + tonemapping once (D-141)
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.allowDynamicResolution = false;

            // Same settings the scene builder saves; repeated here so a hand-made portal camera is still right.
            cameraData = cam.GetUniversalAdditionalCameraData();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderPostProcessing = false;
            cameraData.requiresDepthOption = CameraOverrideOption.Off;
            cameraData.requiresColorOption = CameraOverrideOption.Off;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.stopNaN = false;
            cameraData.dithering = false;
        }

        void OnDestroy()
        {
            ReleaseTexture();
        }

        /// <summary>
        /// Place the camera for this frame and make sure it renders. Called from PortalRenderManager's
        /// late LateUpdate, after look, lean, head bob and the crossing check (proposed D-139).
        /// </summary>
        public void Render(Camera player, PortalSettings settings, int width, int height, int urpRendererIndex)
        {
            Portal destination = portal != null ? portal.Destination : null;
            if (player == null || settings == null || destination == null)
            {
                Stop();
                return;
            }

            EnsureTexture(width, height, settings.depthBufferBits);
            if (urpRendererIndex != rendererIndex)
            {
                cameraData.SetRenderer(urpRendererIndex); // -1 = pipeline default renderer
                rendererIndex = urpRendererIndex;
            }

            // Pose: the player camera (leaned, rolled) carried through the portal pair (D-141).
            Transform eye = player.transform;
            transform.SetPositionAndRotation(portal.ToDestinationPoint(eye.position), portal.ToDestinationRotation(eye.rotation));

            cam.fieldOfView = player.fieldOfView;
            cam.aspect = player.aspect;
            cam.nearClipPlane = player.nearClipPlane;
            cam.farClipPlane = player.farClipPlane;
            cam.clearFlags = player.clearFlags;
            cam.backgroundColor = player.backgroundColor;

            // Oblique near plane = the destination portal's plane, so walls behind it don't block the view.
            Matrix4x4 baseProjection = player.projectionMatrix;
            Matrix4x4 worldToCamera = cam.worldToCameraMatrix;
            Transform clip = destination.transform;
            Vector3 planePoint = worldToCamera.MultiplyPoint(clip.position);
            Vector3 planeNormal = worldToCamera.MultiplyVector(clip.forward); // points away from us, into the visible space
            float planeDistance = -Vector3.Dot(planePoint, planeNormal);     // our signed distance to it (negative = behind)

            if (-planeDistance > settings.obliqueMinDistance)
            {
                var plane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, planeDistance + settings.clipPlaneOffset);
                cam.projectionMatrix = player.CalculateObliqueMatrix(plane);
            }
            else
            {
                // Too close to the plane: an oblique matrix this close gives artefacts (proposed D-142).
                cam.projectionMatrix = baseProjection;
            }
            // Cull with the normal frustum; the oblique one has a distorted far plane.
            cam.cullingMatrix = baseProjection * worldToCamera;

            if (!cam.enabled) cam.enabled = true;
        }

        /// <summary>Stop rendering (portal not visible, closed, or over budget). Keeps the texture for next time.</summary>
        public void Stop()
        {
            if (cam != null && cam.enabled) cam.enabled = false;
        }

        void EnsureTexture(int width, int height, int depthBits)
        {
            if (texture != null && texture.width == width && texture.height == height) return;

            ReleaseTexture();
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.DefaultHDR, depthBits)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false,
            };
            texture = new RenderTexture(descriptor)
            {
                name = "Portal View " + (portal != null ? portal.PortalId : name),
                hideFlags = HideFlags.DontSave, // runtime only, never written into the scene (D-139)
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();
            cam.targetTexture = texture;
            if (portal != null) portal.SetLiveTexture(texture);
        }

        void ReleaseTexture()
        {
            if (texture == null) return;
            if (cam != null) cam.targetTexture = null;
            if (portal != null) portal.SetLiveTexture(null);
            texture.Release();
            Destroy(texture);
            texture = null;
        }
    }
}
