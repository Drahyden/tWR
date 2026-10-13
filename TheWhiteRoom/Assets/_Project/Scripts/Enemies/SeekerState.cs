namespace TWR.Enemies
{
    /// <summary>The Seeker's behaviour states (D-133). Colours per state: SeekerVisuals.</summary>
    public enum SeekerState
    {
        /// <summary>Walks its waypoint route at 1.6 m/s, pausing 1-2 s at each point.</summary>
        Patrol,
        /// <summary>Heard a small noise: stops and turns toward it for 1 s.</summary>
        Suspicious,
        /// <summary>Walks to a noise at 2.5 m/s and looks around for 4 s.</summary>
        Investigate,
        /// <summary>Runs at the player at 4.5 m/s.</summary>
        Chase,
        /// <summary>Melee wind-up and strike.</summary>
        Attack,
        /// <summary>Lost the player: checks the last known position and random points around it.</summary>
        Search,
        /// <summary>Walks back to the nearest waypoint, then patrols again.</summary>
        Return,
        Dead
    }
}
