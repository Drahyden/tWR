using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Debug tool: draws every recent noise as a coloured wire sphere (its radius) in the Scene view,
    /// fading out over a few seconds. Lets you SEE the noise system working (D-043, D-079).
    /// Wall muffling (D-131, D-116): for every listener inside the raw radius it also draws the check
    /// line from the noise to the listener: GREEN = heard with no wall, YELLOW = heard but muffled,
    /// RED = muffled out (not heard). In the Scene view a label at the listener shows walls and effective radius.
    /// Put one in a test scene; gizmos and Debug lines only draw in the Editor, so it costs nothing in builds.
    /// </summary>
    public class NoiseDebugDrawer : MonoBehaviour
    {
        [Tooltip("How long each noise stays visible, in seconds.")]
        [SerializeField] float displaySeconds = 2f;
        [Tooltip("Also draw a ground circle with Debug.DrawLine so noises show in the Game view when Gizmos are on.")]
        [SerializeField] bool drawGroundCircleWithDebugLines = true;
        [Tooltip("Draw the noise -> listener muffling check lines (green clear, yellow muffled, red not heard).")]
        [SerializeField] bool drawListenerChecks = true;

        const int Capacity = 32;
        const int CircleSegments = 24;
        readonly NoiseEvent[] recent = new NoiseEvent[Capacity];
        int nextIndex;
        int count;

        readonly HeardNoise[] recentChecks = new HeardNoise[Capacity];
        int nextCheckIndex;
        int checkCount;

        static readonly Color ClearColour = new Color(0.2f, 0.9f, 0.3f);
        static readonly Color MuffledColour = new Color(1f, 0.85f, 0.1f);
        static readonly Color BlockedColour = new Color(1f, 0.15f, 0.1f);

        NoiseSystem.ListenerCheckedHandler onChecked;

        void Awake() => onChecked = HandleChecked; // cached delegate: no garbage per subscribe

        void OnEnable()
        {
            NoiseSystem.NoiseEmitted += HandleNoise;
            NoiseSystem.ListenerChecked += onChecked;
        }

        void OnDisable()
        {
            NoiseSystem.NoiseEmitted -= HandleNoise;
            NoiseSystem.ListenerChecked -= onChecked;
        }

        void HandleNoise(NoiseEvent noise)
        {
            recent[nextIndex] = noise;
            nextIndex = (nextIndex + 1) % Capacity;
            if (count < Capacity) count++;

            if (drawGroundCircleWithDebugLines)
                DrawGroundCircle(noise.Position, noise.Radius, ColorFor(noise.Type), displaySeconds);
        }

        void HandleChecked(in HeardNoise result)
        {
            recentChecks[nextCheckIndex] = result;
            nextCheckIndex = (nextCheckIndex + 1) % Capacity;
            if (checkCount < Capacity) checkCount++;

            if (drawListenerChecks)
                Debug.DrawLine(result.RayStart, result.ListenerPosition, CheckColour(result), displaySeconds);
        }

        static Color CheckColour(in HeardNoise result) =>
            !result.IsHeard ? BlockedColour : result.IsMuffled ? MuffledColour : ClearColour;

        public static Color ColorFor(NoiseType type)
        {
            switch (type)
            {
                case NoiseType.Footstep: return new Color(0.2f, 0.6f, 1f);
                case NoiseType.Jump:     return new Color(1f, 0.85f, 0.1f);
                case NoiseType.Land:     return new Color(1f, 0.5f, 0f);
                case NoiseType.Gunshot:  return new Color(1f, 0.1f, 0.1f);
                case NoiseType.Impact:   return new Color(0.8f, 0.2f, 1f);
                case NoiseType.Interact: return new Color(0.1f, 1f, 0.4f);
                case NoiseType.Pickup:   return new Color(0.1f, 0.9f, 0.9f);
                case NoiseType.Mechanism: return new Color(1f, 0.35f, 0.75f); // pink (D-128)
                default:                 return Color.white;
            }
        }

        static void DrawGroundCircle(Vector3 centre, float radius, Color colour, float duration)
        {
            float step = Mathf.PI * 2f / CircleSegments;
            Vector3 previous = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= CircleSegments; i++)
            {
                float a = step * i;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Debug.DrawLine(previous, next, colour, duration);
                previous = next;
            }
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            float now = Time.time;
            for (int i = 0; i < count; i++)
            {
                NoiseEvent noise = recent[i];
                float age = now - noise.Time;
                if (age > displaySeconds) continue;

                Color c = ColorFor(noise.Type);
                c.a = 1f - age / displaySeconds;
                Gizmos.color = c;
                Gizmos.DrawWireSphere(noise.Position, noise.Radius);
                Gizmos.DrawSphere(noise.Position, 0.1f);
#if UNITY_EDITOR
                UnityEditor.Handles.color = c;
                UnityEditor.Handles.Label(noise.Position + Vector3.up * 0.3f, noise.Type + " " + noise.Radius.ToString("0.0") + "m");
#endif
            }

#if UNITY_EDITOR
            for (int i = 0; i < checkCount; i++)
            {
                HeardNoise check = recentChecks[i];
                float age = now - check.Noise.Time;
                if (age > displaySeconds || check.WallCount == 0) continue;

                Color c = CheckColour(check);
                c.a = 1f - age / displaySeconds;
                UnityEditor.Handles.color = c;
                UnityEditor.Handles.Label(check.ListenerPosition + Vector3.up * 0.5f,
                    $"{check.WallCount} wall(s): {check.EffectiveRadius:0.0}/{check.Noise.Radius:0.0}m {(check.IsHeard ? "heard" : "NOT heard")}");
            }
#endif
        }
    }
}
