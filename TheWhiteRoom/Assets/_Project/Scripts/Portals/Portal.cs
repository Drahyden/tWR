using System.Collections.Generic;
using UnityEngine;
using TWR.Puzzles;

namespace TWR.Portals
{
    /// <summary>What the player camera currently sees on a portal's screen.</summary>
    public enum PortalSurfaceMode
    {
        /// <summary>Screen off: closed door, camera behind the portal, or not on screen.</summary>
        Hidden,
        /// <summary>Live view rendered by this portal's PortalCamera.</summary>
        Live,
        /// <summary>Flat emissive white: visible, but over the render budget (D-140).</summary>
        Fallback
    }

    /// <summary>
    /// One side of a see-through portal (D-097, D-054). Its transform sits at the bottom centre of the opening,
    /// local forward (+Z) pointing out of the FRONT side; only the front works (one-sided, proposed D-146).
    /// It knows where it leads (a serialized destination portal + SetDestination for later D-058 / D-053b,
    /// proposed D-147), whether it is open (its optional D-127 Door), and does the "pair maths" that maps
    /// points, directions and rotations from here to the destination with a 180-degree turn. Portals in a pair
    /// may differ only by position and yaw (proposed D-142). It also switches its own screen between live,
    /// fallback-white and hidden; PortalRenderManager decides which.
    /// </summary>
    [DisallowMultipleComponent]
    public class Portal : MonoBehaviour
    {
        [Tooltip("Stable ID for events and voice triggers (D-080), e.g. 'lobby.portal'.")]
        [SerializeField] string portalId = "portal";
        [Tooltip("The portal you come out of (proposed D-147). Change at runtime with SetDestination.")]
        [SerializeField] Portal destination;
        [Tooltip("Optional door (D-127). The portal only renders and lets things through while it is open or opening. " +
                 "Empty = always open (e.g. the return portal in the hall).")]
        [SerializeField] Door door;
        [Tooltip("Inner size of the opening in metres (width, height). Minimum 1.2 x 2.4 (proposed D-142).")]
        [SerializeField] Vector2 openingSize = new Vector2(1.4f, 3f);

        [Header("Screen")]
        [Tooltip("Cube on the 'Portal' layer, no collider, material M_PortalSurface. Only the player camera draws it.")]
        [SerializeField] MeshRenderer surface;
        [Tooltip("Flat white quad on the 'PortalFallback' layer. Only portal cameras draw it (no portal-in-portal, D-140).")]
        [SerializeField] Renderer fallback;
        [Tooltip("Emissive white used on the screen when this portal is over the render budget.")]
        [SerializeField] Material fallbackMaterial;
        [Tooltip("The camera that renders the view through this portal.")]
        [SerializeField] PortalCamera view;

        // How far the screen reaches into the frame on each side (hides hairline gaps).
        const float FrameOverlap = 0.02f;
        static readonly Quaternion HalfTurn = Quaternion.Euler(0f, 180f, 0f);
        static readonly int PortalTexId = Shader.PropertyToID("_PortalTex");
        static readonly List<Portal> active = new List<Portal>(8);

        Material liveMaterial;
        PortalSurfaceMode surfaceMode = PortalSurfaceMode.Live; // forces the first SetSurface to apply
        float appliedDepth = -1f;
        bool fallbackShown = true;
        bool nearMode;

        /// <summary>Every enabled portal in the loaded scenes (no allocation to read).</summary>
        public static IReadOnlyList<Portal> Active => active;

        public string PortalId => portalId;
        public Portal Destination => destination;
        public Door Door => door;
        public Vector2 OpeningSize => openingSize;
        public PortalCamera View => view;
        public PortalSurfaceMode SurfaceMode => surfaceMode;
        public bool IsNearMode => nearMode;

        /// <summary>Has a destination that isn't itself.</summary>
        public bool IsLinked => destination != null && destination != this;
        /// <summary>No door, or the door is open / opening.</summary>
        public bool IsOpen => door == null || door.IsOpenOrOpening;
        /// <summary>Linked, open and enabled: rays, the player and the view go through.</summary>
        public bool CanPass => isActiveAndEnabled && IsLinked && IsOpen;
        /// <summary>Front-side normal (world).</summary>
        public Vector3 Normal => transform.forward;

        void Awake()
        {
            if (surface != null && surface.sharedMaterial != null)
            {
                // Own copy so each portal can show its own render texture. Made once, destroyed with the portal.
                liveMaterial = new Material(surface.sharedMaterial) { name = surface.sharedMaterial.name + " (" + portalId + ")" };
                surface.sharedMaterial = liveMaterial;
            }
            if (surface == null || view == null || fallbackMaterial == null)
                Debug.LogError($"[Portal] '{name}' is missing its surface, view camera or fallback material. Run TWR > Build Test Scene.", this);
            SetSurface(PortalSurfaceMode.Hidden, false, 0.002f);
        }

        void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        void OnDisable()
        {
            active.Remove(this);
            if (view != null) view.Stop();
            SetSurface(PortalSurfaceMode.Hidden, false, appliedDepth > 0f ? appliedDepth : 0.002f);
        }

        void OnDestroy()
        {
            if (liveMaterial != null) Destroy(liveMaterial);
        }

        /// <summary>Point this portal somewhere else (proposed D-147; D-058 / D-053b will call this). Null = leads nowhere.</summary>
        public void SetDestination(Portal newDestination)
        {
            destination = newDestination;
            if (!IsLinked && view != null) view.Stop();
        }

        // ---------- geometry ----------

        /// <summary>Distance from the portal plane: positive in front, negative behind.</summary>
        public float SignedDistance(Vector3 worldPoint) => Vector3.Dot(worldPoint - transform.position, transform.forward);

        /// <summary>True if the point, projected onto the portal plane, lies inside the opening grown by margin (m).</summary>
        public bool IsInsideOpening(Vector3 worldPoint, float margin)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= openingSize.x * 0.5f + margin && local.y >= -margin && local.y <= openingSize.y + margin;
        }

        /// <summary>
        /// True if moving from "from" to "to" goes from the FRONT side to the back side through the opening.
        /// Back to front never counts: the portal is one-sided (proposed D-146).
        /// </summary>
        public bool SegmentCrossesFront(Vector3 from, Vector3 to)
        {
            float a = SignedDistance(from);
            float b = SignedDistance(to);
            if (a <= 0f || b > 0f) return false;
            float t = a / (a - b);
            return IsInsideOpening(Vector3.LerpUnclamped(from, to, t), 0f);
        }

        /// <summary>True if a ray enters the front face inside the opening within maxDistance; distance = where.</summary>
        public bool RayEntersFront(Vector3 origin, Vector3 direction, float maxDistance, out float distance)
        {
            distance = 0f;
            float startSide = SignedDistance(origin);
            float approach = -Vector3.Dot(direction, transform.forward); // > 0: moving towards the back side
            if (startSide < 0f || approach <= 1e-5f) return false;
            distance = startSide / approach;
            if (distance > maxDistance) return false;
            return IsInsideOpening(origin + direction * distance, 0f);
        }

        /// <summary>World-space box around the screen (flat or near-mode box), for the frustum test.</summary>
        public Bounds GetScreenBounds(float depth)
        {
            Vector3 centre = transform.TransformPoint(new Vector3(0f, openingSize.y * 0.5f, -depth * 0.5f));
            Vector3 half = new Vector3(openingSize.x * 0.5f + FrameOverlap, openingSize.y * 0.5f + FrameOverlap, depth * 0.5f + 0.01f);
            Vector3 x = transform.right * half.x, y = transform.up * half.y, z = transform.forward * half.z;
            Vector3 extents = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(centre, extents * 2f);
        }

        // ---------- pair maths (this portal -> destination, turned 180 degrees) ----------

        /// <summary>Where a world point near this portal ends up near the destination.</summary>
        public Vector3 ToDestinationPoint(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return destination.transform.TransformPoint(new Vector3(-local.x, local.y, -local.z));
        }

        /// <summary>A world direction (view, velocity, bullet) turned into the destination's space.</summary>
        public Vector3 ToDestinationDirection(Vector3 worldDirection)
        {
            Vector3 local = transform.InverseTransformDirection(worldDirection);
            return destination.transform.TransformDirection(new Vector3(-local.x, local.y, -local.z));
        }

        /// <summary>A world rotation (e.g. the leaned, rolled camera) turned into the destination's space.</summary>
        public Quaternion ToDestinationRotation(Quaternion worldRotation)
        {
            return destination.transform.rotation * HalfTurn * Quaternion.Inverse(transform.rotation) * worldRotation;
        }

        /// <summary>How much the view turns (degrees) when going through. Valid because pairs differ only by yaw.</summary>
        public float YawDelta => Mathf.DeltaAngle(0f, destination.transform.eulerAngles.y + 180f - transform.eulerAngles.y);

        /// <summary>
        /// Proposed D-142 check: both portals stand upright, unscaled, so the pair differs only by position + yaw.
        /// Used by the scene builder's validation and once at start by PortalRenderManager (not per frame).
        /// </summary>
        public static bool DiffersOnlyByYaw(Portal a, Portal b, out string problem)
        {
            problem = null;
            if (a == null || b == null) { problem = "missing portal"; return false; }
            if (!IsUprightAndUnscaled(a, out problem) || !IsUprightAndUnscaled(b, out problem)) return false;
            if ((a.openingSize - b.openingSize).sqrMagnitude > 1e-4f) { problem = "openings have different sizes"; return false; }
            return true;
        }

        static bool IsUprightAndUnscaled(Portal p, out string problem)
        {
            problem = null;
            if (Vector3.Angle(p.transform.up, Vector3.up) > 0.5f) { problem = $"'{p.name}' is tilted (pitch/roll)"; return false; }
            if ((p.transform.lossyScale - Vector3.one).sqrMagnitude > 1e-4f) { problem = $"'{p.name}' is scaled {p.transform.lossyScale}"; return false; }
            return true;
        }

        // ---------- screen (called by PortalRenderManager) ----------

        /// <summary>Live render texture for this portal's screen (set by PortalCamera when it (re)creates it).</summary>
        public void SetLiveTexture(Texture texture)
        {
            if (liveMaterial != null) liveMaterial.SetTexture(PortalTexId, texture);
        }

        /// <summary>
        /// Show the screen as live / fallback / hidden. near = draw it as a box reaching depth metres behind the
        /// plane (Cull Off), so the camera's near plane can't clip a hole while you step through (proposed D-142).
        /// Only touches the renderer when something changes (no per-frame cost).
        /// </summary>
        public void SetSurface(PortalSurfaceMode mode, bool near, float depth)
        {
            nearMode = near;
            if (surface == null) return;

            if (!Mathf.Approximately(depth, appliedDepth))
            {
                appliedDepth = depth;
                Transform t = surface.transform;
                float height = openingSize.y + FrameOverlap;
                t.localScale = new Vector3(openingSize.x + FrameOverlap * 2f, height, depth);
                t.localPosition = new Vector3(0f, height * 0.5f, -depth * 0.5f); // front face exactly on the plane
                t.localRotation = Quaternion.identity;
            }

            if (mode == surfaceMode) return;
            surfaceMode = mode;
            surface.enabled = mode != PortalSurfaceMode.Hidden;
            if (mode == PortalSurfaceMode.Live && liveMaterial != null) surface.sharedMaterial = liveMaterial;
            else if (mode == PortalSurfaceMode.Fallback && fallbackMaterial != null) surface.sharedMaterial = fallbackMaterial;
        }

        /// <summary>The white stand-in that portal cameras see. Shown while the portal is open.</summary>
        public void SetFallbackVisible(bool visible)
        {
            if (fallback == null || visible == fallbackShown) return;
            fallbackShown = visible;
            fallback.enabled = visible;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => active.Clear();

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = IsLinked ? new Color(0.2f, 0.8f, 1f) : new Color(1f, 0.4f, 0.2f);
            Gizmos.DrawWireCube(new Vector3(0f, openingSize.y * 0.5f, 0f), new Vector3(openingSize.x, openingSize.y, 0.02f));
            Gizmos.DrawLine(new Vector3(0f, openingSize.y * 0.5f, 0f), new Vector3(0f, openingSize.y * 0.5f, 0.6f)); // front
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
