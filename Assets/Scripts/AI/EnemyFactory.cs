using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    /// <summary>Builds a fully wired enemy (agent, collider, health, weapon, visuals, brain) at runtime.</summary>
    public static class EnemyFactory
    {
        public static EnemyBrain Create(EnemyRole role, Vector3 position, Quaternion rotation)
        {
            var p = RoleProfile.Get(role);

            var go = new GameObject("Enemy_" + p.Name);
            GameLayers.SetLayerRecursive(go, GameLayers.Enemy);
            go.transform.SetPositionAndRotation(position, rotation);

            var col = go.AddComponent<CapsuleCollider>();
            col.height = 1.8f;
            col.center = new Vector3(0f, 0.9f, 0f);
            col.radius = p.Radius * 0.9f;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = p.Radius;
            agent.height = 1.9f;
            agent.speed = p.RunSpeed;
            agent.acceleration = 16f;
            agent.angularSpeed = 0f;
            agent.autoBraking = true;
            agent.stoppingDistance = 0.3f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            agent.avoidancePriority = p.Role == EnemyRole.Heavy ? 20 : Random.Range(35, 75);

            var health = go.AddComponent<Health>();
            health.Setup(p.MaxHealth, Team.Attacker, false, p.DamageTaken);

            var weapon = go.AddComponent<EnemyWeapon>();
            var visual = go.AddComponent<EnemyVisual>();
            visual.Build(p, col);

            var brain = go.AddComponent<EnemyBrain>();
            brain.Init(p, agent, health, weapon, visual);
            return brain;
        }
    }
}
