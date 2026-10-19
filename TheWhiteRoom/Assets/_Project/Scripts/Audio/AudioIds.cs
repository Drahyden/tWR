namespace TWR.Audio
{
    /// <summary>
    /// Every sound ID the AudioLibrary knows (D-227, D-228). Use these constants instead of typing strings, so a typo is a
    /// compile error. Enemy and boss sounds are NOT here: they stay in their per-type sound set assets (D-138); guns
    /// keep their clips in GunData. Music IDs (A2, D-235) are registered here too (Music), one per track, filled by
    /// TWR > Audio > Build Music (A2). To add a sound: add a constant here, add it to All AND to exactly one of
    /// Required / Fallback / Reserved / Music, run TWR > Audio > Build Audio (A1) (it adds the empty entry), then drop the
    /// clip into the AudioLibrary asset. TWR > Audio > Validate Audio (A1) checks all of this.
    /// </summary>
    public static class AudioIds
    {
        // Ambience (D-077, D-223).
        public const string AmbienceHum = "ambience.hum";
        public const string OfficePhone = "ambience.office.phone";
        public const string OfficePrinter = "ambience.office.printer";
        public const string OfficeKeyboard = "ambience.office.keyboard";

        // UI (menus, end-of-run screen).
        public const string UiClick = "ui.click";
        public const string UiBack = "ui.back";

        // Mechanisms (doors; used when the DoorData slot is empty).
        public const string DoorOpen = "mechanism.door.open";
        public const string DoorLocked = "mechanism.door.locked";
        public const string DoorUnlock = "mechanism.door.unlock";

        // Player (D-079; used when the PlayerMovementSounds clip lists are empty).
        public const string PlayerFootstep = "player.footstep";
        public const string PlayerJump = "player.jump";
        public const string PlayerLand = "player.land";

        // Stingers (D-081). Registered now, played from A5 (D-228).
        public const string StingerViolin = "sfx.stinger.violin";

        // Music (A2, D-228, D-235). Registered so every music file has an ID; the MusicDirector plays them through
        // MusicData / EndingData, never through the library. Each entry holds all stems of its track.
        public const string MusicLobby = "music.lobby";
        public const string MusicExplore = "music.explore";
        public const string MusicPuzzle = "music.puzzle";
        public const string MusicCombat = "music.combat";
        public const string MusicBossBull = "music.boss.bull";
        public const string MusicBossPreacher = "music.boss.preacher";
        public const string MusicEndClockOut = "music.end.clockout";
        public const string MusicEndOvertime = "music.end.overtime"; // registered, never played (D-221, D-234)
        public const string MusicEndFloorPlan = "music.end.floorplan";
        public const string MusicEndAbsorbed = "music.end.absorbed";

        /// <summary>The distant office one-shots the ambient bed picks from (D-223).</summary>
        public static readonly string[] OfficeSounds = { OfficePhone, OfficePrinter, OfficeKeyboard };

        /// <summary>Every ID above, for the builder and the [TWR] validation.</summary>
        public static readonly string[] All =
        {
            AmbienceHum, OfficePhone, OfficePrinter, OfficeKeyboard,
            UiClick, UiBack,
            DoorOpen, DoorLocked, DoorUnlock,
            PlayerFootstep, PlayerJump, PlayerLand,
            StingerViolin,
            MusicLobby, MusicExplore, MusicPuzzle, MusicCombat, MusicBossBull, MusicBossPreacher,
            MusicEndClockOut, MusicEndOvertime, MusicEndFloorPlan, MusicEndAbsorbed,
        };

        /// <summary>Played straight from the library by code: must have a clip ([TWR] error if empty, D-228).
        /// Placeholders exist for all of them.</summary>
        public static readonly string[] Required = { AmbienceHum, OfficePhone, OfficePrinter, OfficeKeyboard, UiClick, UiBack };

        /// <summary>Played by code only when the object's own clip slot is empty (DoorData, PlayerMovementSounds), so they
        /// may stay empty until A3 ([TWR] warning if empty).</summary>
        public static readonly string[] Fallback = { DoorOpen, DoorLocked, DoorUnlock, PlayerFootstep, PlayerJump, PlayerLand };

        /// <summary>Registered for later; nothing plays them yet (the stinger is played from A5, D-081, D-228). Never an error.</summary>
        public static readonly string[] Reserved = { StingerViolin };

        /// <summary>Music tracks (A2, D-235): filled by TWR > Audio > Build Music (A2) from Audio/Music; played by the MusicDirector
        /// via MusicData / EndingData. Empty = [TWR] warning (the music validation reports the missing files).</summary>
        public static readonly string[] Music =
        {
            MusicLobby, MusicExplore, MusicPuzzle, MusicCombat, MusicBossBull, MusicBossPreacher,
            MusicEndClockOut, MusicEndOvertime, MusicEndFloorPlan, MusicEndAbsorbed,
        };

        /// <summary>IDs whose clips are loops (looping itself is set on the AudioSource; TWR > Audio > Check Loops, D-228).</summary>
        public static readonly string[] Loops = { AmbienceHum };
    }
}
