namespace TWR.Run
{
    /// <summary>The section templates we have for now (D-155). Fork and BossArena are special slots in the plan.</summary>
    public enum SectionType
    {
        Puzzle,
        Combat,
        Stealth,
        ImpossibleGeometry,
        Silent,
        /// <summary>The fork room where the player picks a path door (D-149). A connector, not a section (D-156).</summary>
        Fork,
        /// <summary>One boss per path (D-106, D-148). Must be path-tagged (D-156).</summary>
        BossArena
    }

    /// <summary>The two paths of the central dilemma (D-023, D-060). None = usable on any path / not chosen yet.</summary>
    public enum RunPath
    {
        None,
        Escape,
        Discover
    }

    /// <summary>Why a run ended (D-157). Completed = past the boss arena (stub until the end screen, D-092).</summary>
    public enum RunEndReason
    {
        Death,
        Quit,
        Debug,
        Completed
    }
}
