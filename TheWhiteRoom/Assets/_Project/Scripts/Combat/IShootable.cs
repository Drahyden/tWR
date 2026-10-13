namespace TWR.Combat
{
    /// <summary>
    /// A puzzle object that reacts to being shot (D-118; D-036, D-105): switches, glass,
    /// knock-over props. Bullets find it on the hit collider or any of its parents. Any gun reaches it;
    /// the object itself decides whether that gun counts, usually with a GunRequirement field.
    /// </summary>
    public interface IShootable
    {
        /// <summary>
        /// Called for every bullet that hits it. Return true if the shot DID something (shows a hit
        /// marker, D-091); return false if it was ignored (e.g. wrong gun for a gun-specific switch).
        /// </summary>
        bool OnShot(in ShotHitInfo hit);
    }
}
