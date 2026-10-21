using System;
using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// Shared static jump-scare event (D-239), like VoiceEvents / PuzzleEvents. Fired(scareId) is raised at the moment of
    /// the hit (stinger), so the future inner voice (D-080) can react and logs can see it. Only the ScareDirector raises it;
    /// F1 debug fires raise it too.
    /// </summary>
    public static class ScareEvents
    {
        /// <summary>(scareId). A jump scare just hit (D-236).</summary>
        public static event Action<string> Fired;

        public static void RaiseFired(string scareId) => Fired?.Invoke(scareId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Fired = null;
    }
}
