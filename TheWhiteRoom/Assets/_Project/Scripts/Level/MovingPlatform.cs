using UnityEngine;

namespace TWR.Level
{
    /// <summary>
    /// A platform that slides back and forth between point A (where it is built) and A + travel (D-241): horizontal
    /// movers and vertical lifts. It speeds up and slows down gently, waits at each end, and waits while a player or
    /// enemy that isn't riding it is in its way, so it never pushes or crushes anyone. It runs before the player in
    /// the frame (execution order), and publishes how far it moved this frame (FrameDelta) so PlayerLocomotion can
    /// carry a rider by exactly that much: no jitter, no sliding off. Time-based from section load, no randomness
    /// (D-246); never emits noise events. The BoxCollider on this object is its walkable top (ArenaPlatform marker).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public class MovingPlatform : MonoBehaviour
    {
        [SerializeField] MovingPlatformData data;
        [Tooltip("World offset from the start point A (where it is placed) to the end point B.")]
        [SerializeField] Vector3 travel = new Vector3(10f, 0f, 0f);

        BoxCollider box;
        readonly Collider[] hits = new Collider[4];
        Vector3 pointA, direction;
        float length, along, speed, pauseLeft;
        int heading = 1;

        /// <summary>How far it moved this frame (world, metres). Zero while waiting.</summary>
        public Vector3 FrameDelta { get; private set; }
        /// <summary>Its velocity this frame (m/s).</summary>
        public Vector3 Velocity { get; private set; }
        public Vector3 Travel => travel;
        public MovingPlatformData Data => data;
        public bool IsLift => Mathf.Abs(travel.y) > Mathf.Abs(travel.x) + Mathf.Abs(travel.z);
        /// <summary>Start point A: where the builder placed it (edit mode) or where it started (play mode).</summary>
        public Vector3 PointA => Application.isPlaying ? pointA : transform.position;

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            pointA = transform.position;
            length = travel.magnitude;
            direction = length > 0.001f ? travel / length : Vector3.zero;
            pauseLeft = data != null ? data.pauseAtA : 0f;
            if (data == null) Debug.LogError($"[MovingPlatform] '{name}' has no MovingPlatformData. Rebuild the run scenes.", this);
        }

        void OnDisable()
        {
            FrameDelta = Vector3.zero;
            Velocity = Vector3.zero;
        }

        void Update()
        {
            FrameDelta = Vector3.zero;
            Velocity = Vector3.zero;
            float dt = Time.deltaTime;
            if (data == null || length < 0.01f || dt <= 0f) return;
            if (pauseLeft > 0f)
            {
                pauseLeft -= dt;
                return;
            }

            float remaining = heading > 0 ? length - along : along;
            float lookahead = speed * speed / (2f * data.brakeDeceleration) + data.clearanceMargin;
            bool blocked = PathBlocked(direction * heading, lookahead);
            float target = blocked ? 0f : Mathf.Max(data.minSpeed, Mathf.Min(data.speed, Mathf.Sqrt(2f * data.acceleration * remaining)));
            speed = Mathf.MoveTowards(speed, target, (blocked ? data.brakeDeceleration : data.acceleration) * dt);

            float step = Mathf.Min(speed * dt, remaining);
            along += step * heading;
            if (remaining - step <= 0.0005f)
            {
                along = heading > 0 ? length : 0f;
                speed = 0f;
                heading = -heading;
                pauseLeft = heading > 0 ? data.pauseAtA : data.pauseAtB;
            }

            Vector3 next = pointA + direction * along;
            FrameDelta = next - transform.position;
            Velocity = FrameDelta / dt;
            transform.position = next;
            Physics.SyncTransforms(); // the player's CharacterController must see the new position this frame
        }

        /// <summary>
        /// True if a non-riding player or enemy is in the strip just ahead of the platform. Moving sideways the strip
        /// covers the slab's own height (not the space above its top, where riders stand); moving down it is the space
        /// under the slab; moving up nothing is checked (riders are above; headroom is validated, D-242).
        /// </summary>
        bool PathBlocked(Vector3 moveDir, float lookahead)
        {
            if (data.blockers.value == 0 || box == null) return false;
            Bounds b = box.bounds;
            Vector3 centre, half;
            if (moveDir.y > 0.5f) return false;
            if (moveDir.y < -0.5f)
            {
                half = new Vector3(b.extents.x - 0.02f, lookahead * 0.5f, b.extents.z - 0.02f);
                centre = new Vector3(b.center.x, b.min.y - lookahead * 0.5f, b.center.z);
            }
            else
            {
                bool alongX = Mathf.Abs(moveDir.x) > Mathf.Abs(moveDir.z);
                float bottom = b.min.y - 0.1f, top = b.max.y - 0.05f;
                half = new Vector3(alongX ? lookahead * 0.5f : b.extents.x - 0.02f, (top - bottom) * 0.5f,
                                   alongX ? b.extents.z - 0.02f : lookahead * 0.5f);
                Vector3 offset = alongX ? new Vector3(Mathf.Sign(moveDir.x) * (b.extents.x + lookahead * 0.5f), 0f, 0f)
                                        : new Vector3(0f, 0f, Mathf.Sign(moveDir.z) * (b.extents.z + lookahead * 0.5f));
                centre = new Vector3(b.center.x, (top + bottom) * 0.5f, b.center.z) + offset;
            }
            return Physics.OverlapBoxNonAlloc(centre, half, hits, Quaternion.identity, data.blockers, QueryTriggerInteraction.Ignore) > 0;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 a = PointA;
            Gizmos.color = new Color(0.2f, 0.8f, 1f);
            Gizmos.DrawLine(a, a + travel);
            var col = GetComponent<BoxCollider>();
            if (col != null) Gizmos.DrawWireCube(a + travel + col.center, col.size);
        }
    }
}
