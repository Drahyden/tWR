namespace TWR.Combat
{
    /// <summary>
    /// Anything with HP that guns (and later enemies) can hurt: the player (D-034), enemies, test dummies
    /// (D-118). Bullets find it on the hit collider or any of its parents.
    /// When HP reaches 0 the implementer must call CombatEvents.RaiseKilled so kills are counted (D-029).
    /// </summary>
    public interface IDamageable
    {
        /// <summary>False once dead: shots still hit it but deal no damage and show no hit marker.</summary>
        bool IsAlive { get; }

        void TakeDamage(in DamageInfo damage);
    }
}
