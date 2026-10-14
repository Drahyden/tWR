using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TWR.Portals
{
    /// <summary>
    /// Decides every frame which portals render a live view (D-140; D-097, D-100). A portal renders only
    /// while it is open, linked, the player camera is in FRONT of it and its screen is inside the camera frustum
    /// (GeometryUtility.TestPlanesAABB). Of those, only the nearest maxRenderedPortals render live; the rest show
    /// flat white. It also turns a screen into a short box when the camera is about to step through (D-142).
    /// Runs in a very late LateUpdate, so look, lean, head bob and the crossing teleport have already happened
    /// this frame and the portal cameras copy the final pose (D-139). One per scene.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class PortalRenderManager : MonoBehaviour
    {
        [SerializeField] PortalSettings settings;
        [Tooltip("The player's first-person camera (PlayerController.PlayerCamera).")]
        [SerializeField] Camera playerCamera;

        readonly Plane[] frustum = new Plane[6];
        Portal[] candidates = new Portal[8];
        float[] candidateDistances = new float[8];
        bool[] candidateNear = new bool[8];

        RenderPipelineAsset resolvedPipeline;
        int rendererIndex = -1;
        bool pipelineResolved;
        bool warnedRenderer;

        /// <summary>How many portals rendered a live view last frame (debug / Stats check).</summary>
        public int LiveCount => liveCount;

        [Header("Debug (read-only, updated in Play mode)")]
        [Tooltip("Portals that rendered a live view last frame. Should drop to 0 when you look away (D-140).")]
        [SerializeField] int liveCount;
        public PortalSettings Settings => settings;

        void Awake()
        {
            if (settings == null || playerCamera == null)
                Debug.LogError("[PortalRenderManager] Needs PortalSettings and the player camera. Run TWR > Build Test Scene.", this);
        }

        // In the run, this manager lives in the persistent Core scene and portals arrive and leave with the section
        // scenes (D-161): re-run the pair check whenever a section is loaded additively. Portals register themselves
        // in Portal.Active (OnEnable/OnDisable) and free their render textures when unloaded (PortalCamera.OnDestroy).
        void OnEnable() => SceneManager.sceneLoaded += HandleSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

        void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) CheckPairs();
        }

        void Start() => CheckPairs();

        void CheckPairs()
        {
            // Sanity check (D-142): the maths assumes every pair differs only by position + yaw.
            IReadOnlyList<Portal> portals = Portal.Active;
            for (int i = 0; i < portals.Count; i++)
            {
                Portal portal = portals[i];
                if (portal.IsLinked && !Portal.DiffersOnlyByYaw(portal, portal.Destination, out string problem))
                    Debug.LogError($"[PortalRenderManager] Portal '{portal.PortalId}': {problem}. Pairs may differ only by position and yaw.", portal);
            }
        }

        void LateUpdate()
        {
            if (settings == null || playerCamera == null) return;
            ResolveRendererIndex();

            float scale = Mathf.Clamp(settings.resolutionScale, 0.5f, 1f);
            int width = Mathf.Max(16, Mathf.RoundToInt(playerCamera.pixelWidth * scale));
            int height = Mathf.Max(16, Mathf.RoundToInt(playerCamera.pixelHeight * scale));

            GeometryUtility.CalculateFrustumPlanes(playerCamera, frustum);
            Vector3 eye = playerCamera.transform.position;

            IReadOnlyList<Portal> portals = Portal.Active;
            EnsureCapacity(portals.Count);
            int count = 0;

            for (int i = 0; i < portals.Count; i++)
            {
                Portal portal = portals[i];
                bool open = portal.IsLinked && portal.IsOpen;
                portal.SetFallbackVisible(open); // what OTHER portal cameras see here: flat white (D-140)

                float side = portal.SignedDistance(eye);
                if (!open || side <= 0f)
                {
                    Hide(portal); // closed, or we are behind it: one-sided, see straight through the frame (D-146)
                    continue;
                }

                bool near = side < settings.nearModeDistance && portal.IsInsideOpening(eye, settings.nearModeDistance);
                Bounds bounds = portal.GetScreenBounds(near ? settings.nearBoxDepth : settings.flatThickness);
                if (!GeometryUtility.TestPlanesAABB(frustum, bounds))
                {
                    Hide(portal);
                    continue;
                }

                // Insert sorted by distance (nearest first). Tiny list, so a simple insertion sort, no garbage.
                float distance = (bounds.ClosestPoint(eye) - eye).sqrMagnitude;
                int at = count;
                while (at > 0 && candidateDistances[at - 1] > distance)
                {
                    candidates[at] = candidates[at - 1];
                    candidateDistances[at] = candidateDistances[at - 1];
                    candidateNear[at] = candidateNear[at - 1];
                    at--;
                }
                candidates[at] = portal;
                candidateDistances[at] = distance;
                candidateNear[at] = near;
                count++;
            }

            int budget = Mathf.Max(0, settings.maxRenderedPortals);
            int live = 0;
            for (int i = 0; i < count; i++)
            {
                Portal portal = candidates[i];
                bool near = candidateNear[i];
                float depth = near ? settings.nearBoxDepth : settings.flatThickness;
                if (i < budget && portal.View != null)
                {
                    portal.View.Render(playerCamera, settings, width, height, rendererIndex);
                    portal.SetSurface(PortalSurfaceMode.Live, near, depth);
                    live++;
                }
                else
                {
                    if (portal.View != null) portal.View.Stop();
                    portal.SetSurface(PortalSurfaceMode.Fallback, near, depth);
                }
                candidates[i] = null;
            }
            liveCount = live;
        }

        void Hide(Portal portal)
        {
            if (portal.View != null) portal.View.Stop();
            portal.SetSurface(PortalSurfaceMode.Hidden, false, settings.flatThickness);
        }

        void EnsureCapacity(int needed)
        {
            if (candidates.Length >= needed) return;
            int size = Mathf.NextPowerOfTwo(needed); // grows only when more portals exist than ever before
            candidates = new Portal[size];
            candidateDistances = new float[size];
            candidateNear = new bool[size];
        }

        /// The portal cameras use the dedicated "Portal" renderer: find its index in the ACTIVE URP asset
        /// (it can differ per quality level). Re-checked only when the active pipeline asset changes.
        void ResolveRendererIndex()
        {
            RenderPipelineAsset current = GraphicsSettings.currentRenderPipeline;
            if (pipelineResolved && current == resolvedPipeline) return;
            pipelineResolved = true;
            resolvedPipeline = current;
            rendererIndex = -1;

            if (current is UniversalRenderPipelineAsset urp && settings.portalRenderer != null)
            {
                System.ReadOnlySpan<ScriptableRendererData> list = urp.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] == settings.portalRenderer) { rendererIndex = i; break; }
                }
            }
            if (rendererIndex < 0 && !warnedRenderer)
            {
                warnedRenderer = true;
                Debug.LogWarning("[PortalRenderManager] The 'Portal' renderer is not in the active URP asset's renderer list; " +
                                 "portal cameras use the default renderer (works, but slower). Run TWR > Build Test Scene.", this);
            }
        }
    }
}
