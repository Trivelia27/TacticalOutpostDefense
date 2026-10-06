using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    /// <summary>Stand-and-shoot: find a firing position with line of sight, hold ground, strafe and fire.</summary>
    public sealed class AttackAction : UtilityAction
    {
        public override string Name => "Attack";
        public override Color DebugColor => new Color(1f, 0.25f, 0.2f);
        protected override float RoleWeight(RoleProfile p) => p.WAttack;

        float nextStrafe;
        float strafeEnd;
        bool strafing;
        Vector3 strafeDest;
        float nextReposition;
        Vector3 firingPos;
        bool hasFiringPos;

        public AttackAction()
        {
            Add("HasTarget", b => b.HasTarget ? 1f : 0f, ResponseCurve.Linear(1f, 0f));
            Add("InEngageRange", b => b.DistToTarget / (b.Profile.Range * 1.5f), ResponseCurve.Logistic(14f, 0.62f, true));
            Add("Visibility", b => b.HasLos ? 1f : 0f, ResponseCurve.Linear(0.55f, 0.45f));
            Add("Healthy", b => b.Health.Fraction, ResponseCurve.Linear(0.7f, 0.3f));
            Add("NotSuppressed", b => b.Suppression, ResponseCurve.Linear(-0.75f, 1f));
        }

        public override void OnEnter(EnemyBrain b)
        {
            b.SetCrouched(false);
            strafing = false;
            hasFiringPos = false;
            nextStrafe = Time.time + Random.Range(0.5f, 1.5f);
            nextReposition = 0f;
        }

        public override void Tick(EnemyBrain b, float dt)
        {
            if (!b.HasTarget) return;
            var p = b.Profile;
            Vector3 tp = b.Target.transform.position;
            float d = b.DistToTarget;
            bool canShoot = b.LineOfFire();

            if (canShoot && d <= p.Range)
            {
                b.FaceTarget = true;

                if (d < p.MinRange)
                {
                    // Too close for comfort: back away while shooting.
                    Vector3 away = b.Position - tp; away.y = 0f;
                    b.MoveTo(b.Position + away.normalized * 5f, true, 0.3f);
                    b.Weapon.TryShoot(b.Target, 1.5f);
                    return;
                }

                bool approach = p.MoveFire && d > p.PreferredRange * 1.3f;
                if (approach)
                {
                    b.MoveTo(tp, false, p.PreferredRange);
                }
                else if (p.MoveFire)
                {
                    Strafe(b, tp);
                }
                else
                {
                    b.Halt();
                }

                b.Weapon.TryShoot(b.Target, approach || strafing ? 1.4f : 1f);
                return;
            }

            // No shot: either walk towards the target or look for a spot that can see it.
            if (d <= p.Range * 1.15f && !canShoot)
            {
                if (!hasFiringPos || Time.time >= nextReposition)
                {
                    hasFiringPos = FindFiringPosition(b, out firingPos);
                    nextReposition = Time.time + 1.2f;
                }
                if (hasFiringPos) { b.MoveTo(firingPos, true, 0.4f); return; }
            }

            b.MoveTo(tp, true, Mathf.Max(1f, p.PreferredRange * 0.9f));
        }

        void Strafe(EnemyBrain b, Vector3 targetPos)
        {
            if (!strafing && Time.time >= nextStrafe)
            {
                Vector3 toT = targetPos - b.Position; toT.y = 0f;
                Vector3 side = Vector3.Cross(Vector3.up, toT.normalized) * (Random.value < 0.5f ? -1f : 1f);
                strafeDest = b.Position + side * Random.Range(2f, 4f);
                strafing = true;
                strafeEnd = Time.time + 1.6f;
                nextStrafe = Time.time + Random.Range(2.2f, 4f);
            }

            if (strafing)
            {
                b.MoveTo(strafeDest, false, 0.2f);
                if (b.HasArrived(0.4f) || Time.time > strafeEnd) strafing = false;
            }
            else
            {
                b.Halt();
            }
        }

        /// <summary>Samples rings around the target for the nearest navmesh point that can see it.</summary>
        static bool FindFiringPosition(EnemyBrain b, out Vector3 result)
        {
            result = default;
            var p = b.Profile;
            Vector3 T = b.Target.transform.position;
            float baseR = Mathf.Min(p.PreferredRange, p.Range * 0.85f);
            float[] radii = { 0.5f, 0.75f, 1f, 1.3f };
            float bestD = float.MaxValue;
            bool found = false;

            for (int ri = 0; ri < radii.Length; ri++)
            {
                float r = Mathf.Min(baseR * radii[ri], p.Range * 0.9f);
                for (int a = 0; a < 16; a++)
                {
                    float ang = a * 22.5f * Mathf.Deg2Rad;
                    Vector3 pos = T + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * r;
                    if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) continue;
                    if (Physics.Linecast(hit.position + Vector3.up * 1.6f, b.Target.AimPoint, GameLayers.ObstacleMask)) continue;

                    float d = (hit.position - b.Position).sqrMagnitude;
                    if (d < bestD) { bestD = d; result = hit.position; found = true; }
                }
            }
            return found;
        }
    }
}
