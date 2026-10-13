using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Anything that can hear noises (Seeker enemies later, D-043; test listener now).
    /// Register with NoiseSystem.Register in OnEnable and Unregister in OnDisable.
    /// </summary>
    public interface INoiseListener
    {
        /// <summary>World position of the "ears".</summary>
        Vector3 HearingPosition { get; }

        /// <summary>Called when a noise is emitted within its radius of HearingPosition.</summary>
        void OnNoiseHeard(NoiseEvent noise);
    }
}
