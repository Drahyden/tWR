using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// One noise that happened: where, how far it carries, what kind, and who made it.
    /// A struct so emitting noises never allocates garbage (D-100). D-043, D-079.
    /// </summary>
    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly NoiseType Type;
        public readonly GameObject Source;
        public readonly float Time;

        public NoiseEvent(Vector3 position, float radius, NoiseType type, GameObject source, float time)
        {
            Position = position;
            Radius = radius;
            Type = type;
            Source = source;
            Time = time;
        }
    }
}
