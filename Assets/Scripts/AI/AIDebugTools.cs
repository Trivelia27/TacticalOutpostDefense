using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    /// <summary>
    /// "AI test lab" helpers behind the debug keys: spawn a single enemy in front of the player (F5) to demonstrate perception,
    /// memory and search on demand, and clear them again (F6). Only available while AI Debug Mode is on.
    /// </summary>
    public static class AIDebugTools
    {
        static readonly EnemyRole[] Cycle = { EnemyRole.Assault, EnemyRole.Flanker, EnemyRole.Sniper, EnemyRole.Heavy, EnemyRole.Medic };
        static int next;

        public static EnemyBrain SpawnTestEnemy(Transform viewer, float distance = 22f)
        {
            var player = PlayerController.Instance;
            if (player == null || viewer == null) return null;

            Vector3 forward = viewer.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = player.transform.forward;
            forward.Normalize();

            // In front of the player first, then fan out to find a free spot on the NavMesh.
            float[] angles = { 0f, 25f, -25f, 50f, -50f, 80f, -80f, 120f, -120f, 180f };
            foreach (float a in angles)
            {
                Vector3 pos = player.transform.position + Quaternion.Euler(0f, a, 0f) * forward * distance;
                if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                Vector3 toPlayer = player.transform.position - hit.position;
                toPlayer.y = 0f;
                var role = Cycle[next++ % Cycle.Length];
                return EnemyFactory.Create(role, hit.position, Quaternion.LookRotation(toPlayer.sqrMagnitude > 0.01f ? toPlayer : Vector3.forward));
            }
            return null;
        }

        public static int ClearEnemies()
        {
            var list = SquadDirector.Enemies.ToArray();
            for (int i = 0; i < list.Length; i++)
                if (list[i] != null) Object.Destroy(list[i].gameObject);
            return list.Length;
        }
    }
}
