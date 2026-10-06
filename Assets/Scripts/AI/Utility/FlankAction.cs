using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Swing around to the target's side/rear. Valuable when allies are already pinning the
    /// target from the front. Follows an arc waypoint first so the route avoids the line of fire,
    /// then settles at a firing position with line of sight; the plan is rebuilt if the target moves far.
    /// </summary>
    public sealed class FlankAction : UtilityAction
    {
        public override string Name => "Flank";
        public override Color DebugColor => new Color(1f, 0.2f, 0.9f);
        protected override float RoleWeight(RoleProfile p) => p.WFlank;

        const float MaxTime = 25f;

        FlankPlan plan;
        int stage;          // 0 = heading for the waypoint, 1 = heading for the firing position
        bool done;
        float enteredAt;
        float nextReplan;

        public FlankAction()
        {
            Add("HasTarget", b => b.HasTarget ? 1f : 0f, ResponseCurve.Linear(1f, 0f));
            Add("AlliesPinning", b => b.PinningAllies / 3f, ResponseCurve.Linear(0.65f, 0.35f));
            Add("FlankQuality", b => b.Flank.Valid ? b.Flank.Quality : 0f, ResponseCurve.Linear(1.2f, 0f));
            Add("TargetDistance", b => b.DistToTarget / (b.Profile.Range * 2f), ResponseCurve.Logistic(12f, 0.2f));
            Add("NotRecentlyFlanked", b => (Time.time - b.LastFlankCompleteTime) / 10f, ResponseCurve.Linear(1f, 0f));
            Add("Healthy", b => b.Health.Fraction, ResponseCurve.Linear(0.8f, 0.2f));
        }

        public override bool IsAvailable(EnemyBrain b) => b.HasTarget && (b.Flank.Valid || b.Current == this);

        public override void OnEnter(EnemyBrain b)
        {
            plan = b.Flank;
            stage = plan.HasWaypoint ? 0 : 1;
            done = false;
            enteredAt = Time.time;
            nextReplan = Time.time + 2f;
            b.SetCrouched(false);
            if (plan.Valid) SquadDirector.ClaimFlank(plan.Position, b);
        }

        public override void Tick(EnemyBrain b, float dt)
        {
            if (!b.HasTarget || !plan.Valid) { done = true; return; }
            var p = b.Profile;

            // Target relocated a lot: the flank angle is stale, rebuild it.
            if (Time.time >= nextReplan &&
                Vector3.Distance(b.Target.transform.position, plan.TargetRef) > 9f)
            {
                nextReplan = Time.time + 2f;
                var np = FlankPlanner.Plan(b);
                if (np.Valid)
                {
                    plan = np;
                    stage = np.HasWaypoint ? 0 : 1;
                    SquadDirector.ClaimFlank(np.Position, b);
                }
            }

            Vector3 dest = stage == 0 ? plan.Waypoint : plan.Position;
            b.MoveTo(dest, true, 0.4f);

            if (b.HasArrived(0.9f))
            {
                if (stage == 0) stage = 1;
                else done = true;
            }

            // Quick snap shots on the move.
            if (p.MoveFire && b.LineOfFire() && b.DistToTarget <= p.Range * 0.8f)
            {
                b.FaceTarget = true;
                b.Weapon.TryShoot(b.Target, 1.8f);
            }
        }

        public override bool IsFinished(EnemyBrain b) => done || Time.time - enteredAt > MaxTime;

        public override void OnExit(EnemyBrain b)
        {
            b.LastFlankCompleteTime = Time.time;
            if (!done) SquadDirector.ReleaseFlank(b);
        }
    }
}
