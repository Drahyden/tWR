namespace TWR.Audio
{
    /// <summary>
    /// The adaptive music states (D-075, D-213, D-230, D-233), lowest to highest priority after None:
    /// Ending > Boss > Combat > Puzzle > Exploration > Lobby (D-233). MainMenu is the Lobby track with only its menu stem
    /// (D-230); it only exists in the MainMenu scene, so it never competes with the run states.
    /// None = no music (nothing loaded yet, or a silent room, D-168).
    /// </summary>
    public enum MusicStateKind
    {
        None = 0,
        MainMenu = 1,
        Lobby = 2,
        Exploration = 3,
        Puzzle = 4,
        Combat = 5,
        Boss = 6,
        Ending = 7,
    }
}
