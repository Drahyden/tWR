using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Put this on a boss's root object (the bull and the preacher, D-148). RunStats ignores enemies with it,
    /// so bosses count neither as spawned nor as killed in the performance score (D-158). No bosses exist yet;
    /// this is only the hook.
    /// </summary>
    [DisallowMultipleComponent]
    public class BossMarker : MonoBehaviour
    {
    }
}
