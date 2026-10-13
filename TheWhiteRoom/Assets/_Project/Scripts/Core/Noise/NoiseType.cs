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
        Interact
    }
}
