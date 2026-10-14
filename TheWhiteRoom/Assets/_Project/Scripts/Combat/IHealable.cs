namespace TWR.Combat
{
    /// <summary>
    /// Something that can get HP back, e.g. the player from a health pickup (D-171). Kept next to IDamageable so
    /// item code doesn't need to know about the player scripts.
    /// </summary>
    public interface IHealable
    {
        /// <summary>Alive and below max HP. A health pickup is not used up when this is false (D-171).</summary>
        bool NeedsHealing { get; }

        /// <summary>Add HP, never above max HP.</summary>
        void Heal(float amount);
    }
}
