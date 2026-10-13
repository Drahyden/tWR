using System;

namespace TWR.Combat
{
    /// <summary>What a bullet achieved. Drives the hit marker (D-091). Flags: a target can be both.</summary>
    [Flags]
    public enum HitResult
    {
        None = 0,
        /// <summary>Hurt an IDamageable that was alive.</summary>
        Damaged = 1,
        /// <summary>That damage killed it.</summary>
        Killed = 2,
        /// <summary>An IShootable accepted the shot (e.g. a switch flipped).</summary>
        Triggered = 4
    }
}
