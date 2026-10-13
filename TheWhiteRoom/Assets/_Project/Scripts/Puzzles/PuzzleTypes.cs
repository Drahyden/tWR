namespace TWR.Puzzles
{
    /// <summary>
    /// How a switch puzzle counts as solved (D-124).
    /// AllActive = every switch is on, in any order. Sequence = switches must be turned on in the order
    /// they are listed on the PuzzleController; a wrong switch resets them all (raises PuzzleEvents.Reset).
    /// </summary>
    public enum PuzzleSolveMode
    {
        AllActive,
        Sequence
    }

    /// <summary>
    /// What a switch does when it is triggered again (D-124, D-125).
    /// Latch = turns on and stays on. Toggle = flips between on and off on every hit.
    /// </summary>
    public enum SwitchBehaviour
    {
        Latch,
        Toggle
    }
}
