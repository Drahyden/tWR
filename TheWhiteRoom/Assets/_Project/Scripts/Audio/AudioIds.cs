namespace TWR.Audio
{
    /// <summary>
    /// Every sound ID the AudioLibrary knows (D-227). Use these constants instead of typing strings, so a typo is a
    /// compile error. Enemy and boss sounds are NOT here: they stay in their per-type sound set assets (D-138); guns
    /// keep their clips in GunData. To add a sound: add a constant here, add it to All, run
    /// TWR > Audio > Build Audio (A1) (it adds the empty entry), then drop the clip into the AudioLibrary asset.
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

        /// <summary>The distant office one-shots the ambient bed picks from (D-223).</summary>
        public static readonly string[] OfficeSounds = { OfficePhone, OfficePrinter, OfficeKeyboard };

        /// <summary>Every ID above, for the builder and the [TWR] validation.</summary>
        public static readonly string[] All =
        {
            AmbienceHum, OfficePhone, OfficePrinter, OfficeKeyboard,
            UiClick, UiBack,
            DoorOpen, DoorLocked, DoorUnlock,
            PlayerFootstep, PlayerJump, PlayerLand,
        };

        /// <summary>IDs that must have at least one clip (placeholders exist for them). The rest may stay empty for now.</summary>
        public static readonly string[] Required = { AmbienceHum, OfficePhone, OfficePrinter, OfficeKeyboard, UiClick, UiBack };
    }
}
