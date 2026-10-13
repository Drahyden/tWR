using UnityEngine;

namespace TWR.Core
{
    /// <summary>
    /// One place for the project's physics layer names (D-118, D-123, D-131/D-134), so scripts
    /// don't each spell "Enemy" or "Player" themselves. ActorMask = layers that are bodies or helpers, never
    /// walls: Player, Enemy, Weapon and Ignore Raycast. Noise muffling and enemy sight remove these from
    /// their "what blocks me" masks, so a ray can never be stopped by the player's own capsule or an
    /// enemy's own body. Layer numbers are looked up once and cached (D-100).
    /// </summary>
    public static class GameLayers
    {
        public const string Player = "Player";
        public const string Enemy = "Enemy";
        public const string Weapon = "Weapon";
        public const string Interactable = "Interactable";
        public const string IgnoreRaycast = "Ignore Raycast";

        static bool cached;
        static int enemyLayer = -1;
        static int playerLayer = -1;
        static int actorMask;

        /// <summary>Index of the Enemy layer, or -1 if it doesn't exist yet (run TWR > Setup Project).</summary>
        public static int EnemyLayer { get { Cache(); return enemyLayer; } }

        /// <summary>Index of the Player layer, or -1 if it doesn't exist yet.</summary>
        public static int PlayerLayer { get { Cache(); return playerLayer; } }

        /// <summary>Bit mask of Player + Enemy + Weapon + Ignore Raycast.</summary>
        public static int ActorMask { get { Cache(); return actorMask; } }

        /// <summary>The given mask with every actor/helper layer removed: what is left are real obstacles.</summary>
        public static int WithoutActors(int mask) => mask & ~ActorMask;

        static void Cache()
        {
            if (cached) return;
            cached = true;
            playerLayer = LayerMask.NameToLayer(Player);
            enemyLayer = LayerMask.NameToLayer(Enemy);
            actorMask = Bit(playerLayer) | Bit(enemyLayer) | Bit(LayerMask.NameToLayer(Weapon)) | Bit(LayerMask.NameToLayer(IgnoreRaycast));
        }

        static int Bit(int layer) => layer >= 0 ? 1 << layer : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cached = false;
    }
}
