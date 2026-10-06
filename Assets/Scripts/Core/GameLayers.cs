using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Fixed physics layers used by the project. The editor builder registers the
    /// names in TagManager so they show up nicely in the Inspector.
    /// </summary>
    public static class GameLayers
    {
        public const int Obstacle = 8;   // walls, cover, buildings (block LOS, bullets and NavMesh)
        public const int Ground = 9;
        public const int Player = 10;
        public const int Enemy = 11;
        public const int Structure = 12; // reactor and turrets (hit-boxes)

        public static readonly string[] Names = { "Obstacle", "Ground", "Player", "Enemy", "Structure" };

        public const int ObstacleMask = 1 << Obstacle;
        public const int GroundMask = 1 << Ground;
        public const int PlayerMask = 1 << Player;
        public const int EnemyMask = 1 << Enemy;
        public const int StructureMask = 1 << Structure;

        /// <summary>What attackers' bullets can hit (they never hit their own team).</summary>
        public const int EnemyBulletMask = ObstacleMask | PlayerMask | StructureMask;

        /// <summary>What the defenders' bullets (player, turrets) can hit.</summary>
        public const int DefenderBulletMask = ObstacleMask | EnemyMask;

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
