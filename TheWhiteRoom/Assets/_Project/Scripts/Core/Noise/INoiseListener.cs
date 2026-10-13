using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Anything that can hear noises (the Seeker, D-043; NoiseTestListener).
    /// Register with NoiseSystem.Register in OnEnable and Unregister in OnDisable.
    /// </summary>
    public interface INoiseListener
    {
        /// <summary>World position of the "ears".</summary>
        Vector3 HearingPosition { get; }

        /// <summary>
        /// Called when a noise reaches HearingPosition after wall muffling (D-131, D-116).
        /// heard.WallCount / heard.EffectiveRadius tell how muffled it was.
        /// </summary>
        void OnNoiseHeard(in HeardNoise heard);
    }
}
