namespace TWR.Audio
{
    /// <summary>
    /// Every sound ID the AudioLibrary knows (D-227, D-228). Use these constants instead of typing strings, so a typo is a
    /// compile error. Enemy and boss sounds are NOT here: they stay in their per-type sound set assets (D-138); guns
    /// keep their clips in GunData. Music IDs come in A2 (D-228). To add a sound: add a constant here, add it to All
    /// AND to exactly one of Required / Fallback / Reserved, run TWR > Audio > Build Audio (A1) (it adds the empty
    /// entry), then drop the clip into the AudioLibrary asset. TWR > Audio > Validate Audio (A1) checks all of this.
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
        };

        /// <summary>Played straight from the library by code: must have a clip ([TWR] error if empty, D-228).
        /// Placeholders exist for all of them.</summary>
        public static readonly string[] Required = { AmbienceHum, OfficePhone, OfficePrinter, OfficeKeyboard, UiClick, UiBack };

        /// <summary>Played by code only when the object's own clip slot is empty (DoorData, PlayerMovementSounds), so they
        /// may stay empty until A3 ([TWR] warning if empty).</summary>
        public static readonly string[] Fallback = { DoorOpen, DoorLocked, DoorUnlock, PlayerFootstep, PlayerJump, PlayerLand };

        /// <summary>Registered for later; nothing plays them yet (the stinger is played from A5, D-081, D-228). Never an error.</summary>
        public static readonly string[] Reserved = { StingerViolin };

        /// <summary>IDs whose clips are loops (looping itself is set on the AudioSource; TWR > Audio > Check Loops, D-228).</summary>
        public static readonly string[] Loops = { AmbienceHum };
    }
}
