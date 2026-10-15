namespace TWR.Enemies
{
    /// <summary>
    /// What BossEncounter needs from a boss's behaviour script (BullBrain, PreacherBrain; D-181, D-184): start fighting,
    /// react to a phase change, stop everything on defeat, and describe itself for the F1 debug overlay (D-185).
    /// </summary>
    public interface IBossBrain
    {
        /// <summary>The fight began (BossEvents.Started was raised).</summary>
        void BeginFight();

        /// <summary>The boss reached a new phase (2 at 50% HP, D-184).</summary>
        void HandlePhaseChanged(int phase);

        /// <summary>The boss died: stop moving and attacking at once.</summary>
        void HandleDefeated();

        /// <summary>One or two lines for the debug overlay (state, counters, targets). Debug only; may allocate.</summary>
        string DebugState { get; }
    }
}
