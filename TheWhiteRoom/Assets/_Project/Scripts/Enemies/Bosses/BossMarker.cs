using UnityEngine;

namespace TWR.Enemies
{
    /// <summary>
    /// Put this on a boss's root object (the Bull and the Preacher, D-148). RunStats ignores enemies with it, so bosses
    /// count neither as spawned nor as killed in the performance score (D-158, D-185). BossEncounter requires it.
    /// </summary>
    [DisallowMultipleComponent]
    public class BossMarker : MonoBehaviour
    {
    }
}
