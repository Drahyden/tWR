namespace TWR.Core
{
    /// <summary>
    /// Kinds of noise the world can make. Enemies (e.g. the Seeker, D-043) can react
    /// differently per type. D-043, D-052, D-079.
    /// </summary>
    public enum NoiseType
    {
        Footstep,
        Jump,
        Land,
        Gunshot,
        Impact,
        Interact,
        /// <summary>Walking over an item pickup (D-121). Its own type so a Seeker can treat it differently from F-interactions.</summary>
        Pickup,
        /// <summary>Puzzle machinery: a switch latching, a door unlocking or opening (D-128).</summary>
        Mechanism
    }
}
