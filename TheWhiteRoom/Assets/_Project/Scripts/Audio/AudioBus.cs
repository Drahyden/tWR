namespace TWR.Audio
{
    /// <summary>
    /// The five mixer buses of TWR_Mixer (D-219). Each bus has a user group (Music, Ambience, SFX, Voice, UI) whose
    /// Volume is the exposed slider parameter (D-225), and one child "state" group (MusicState, ...) that the snapshots
    /// control (D-220). Every AudioSource goes to a STATE group: player/gun/enemy/boss/mechanism/pickups = SFX,
    /// menus/end screen = UI, ambient bed/office one-shots = Ambience, music = Music, inner voice = Voice.
    /// The numbers are used as array indices (keep them 0..4 and in this order).
    /// </summary>
    public enum AudioBus
    {
        Music = 0,
        Ambience = 1,
        SFX = 2,
        Voice = 3,
        UI = 4,
    }

    /// <summary>The four mixer snapshots (D-220), in rising priority: Default, Silent, Ending, Paused.</summary>
    public enum AudioSnapshotKind
    {
        Default = 0,
        Silent = 1,
        Ending = 2,
        Paused = 3,
    }
}
