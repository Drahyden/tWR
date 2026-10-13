using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Debug tool: draws every recent noise as a coloured wire sphere (its radius) in the Scene view,
    /// fading out over a few seconds. Lets you SEE the noise system working (D-043, D-079).
    /// Put one in a test scene; it costs nothing in builds because gizmos only draw in the Editor.
    /// </summary>
    public class NoiseDebugDrawer : MonoBehaviour
    {
        [Tooltip("How long each noise stays visible, in seconds.")]
        [SerializeField] float displaySeconds = 2f;
        [Tooltip("Also draw a ground circle with Debug.DrawLine so noises show in the Game view when Gizmos are on.")]
        [SerializeField] bool drawGroundCircleWithDebugLines = true;

        const int Capacity = 32;
        const int CircleSegments = 24;
        readonly NoiseEvent[] recent = new NoiseEvent[Capacity];
        int nextIndex;
        int count;

        void OnEnable() => NoiseSystem.NoiseEmitted += HandleNoise;
        void OnDisable() => NoiseSystem.NoiseEmitted -= HandleNoise;

        void HandleNoise(NoiseEvent noise)
        {
            recent[nextIndex] = noise;
            nextIndex = (nextIndex + 1) % Capacity;
            if (count < Capacity) count++;

            if (drawGroundCircleWithDebugLines)
                DrawGroundCircle(noise.Position, noise.Radius, ColorFor(noise.Type), displaySeconds);
        }

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
        }
    }
}
