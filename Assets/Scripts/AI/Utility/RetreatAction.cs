using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    /// <summary>Wounded units fall back to safe cover far from the threat (ideally near a medic) and recover.</summary>
    public sealed class RetreatAction : UtilityAction
    {
        public override string Name => "Retreat";
        public override Color DebugColor => new Color(0.3f, 1f, 0.4f);
        protected override float RoleWeight(RoleProfile p) => p.WRetreat;

        const float MaxTime = 10f;
        const float RecoverHealth = 0.65f;

        CoverPoint point;
        Vector3 spot;
        float enteredAt;
        bool arrived;

        public RetreatAction()
        {
            Add("LowHealth", b => 1f - b.Health.Fraction, ResponseCurve.Logistic(10f, 0.45f));
            Add("Threatened",
                b => Mathf.Max(b.Suppression, b.HasTarget && b.DistToTarget < b.Profile.Range * 1.3f ? 0.8f : 0.25f),
                ResponseCurve.Linear(0.8f, 0.2f));
            Add("SafeSpotKnown", b => Mathf.Max(0.3f, b.RetreatCover.Quality), ResponseCurve.Linear(0.7f, 0.3f));
        }

        public override bool IsAvailable(EnemyBrain b)
        {
            if (b.Current == this) return true;
            if (b.Health.Fraction >= 0.6f) return false;
            return Time.time >= b.RetreatCooldownUntil || b.Health.Fraction < 0.25f;
        }

        public override void OnEnter(EnemyBrain b)
        {
            enteredAt = Time.time;
            arrived = false;
            b.SetCrouched(false);

            point = b.RetreatCover.Point;
            if (point != null)
            {
                CoverManager.Claim(point, b);
                spot = point.Position;
            }
            else
            {
                // No cover known: just back away from the target along the way we came.
                Vector3 away = b.HasTarget ? b.Position - b.Target.transform.position : -b.transform.forward;
                away.y = 0f;
                Vector3 p = b.Position + away.normalized * 18f;
                spot = NavMesh.SamplePosition(p, out NavMeshHit hit, 8f, NavMesh.AllAreas) ? hit.position : b.Position;
            }
        }

        public override void Tick(EnemyBrain b, float dt)
        {
            if (!arrived)
            {
                b.MoveTo(spot, true, 0.2f);
                if (b.HasArrived(0.6f)) arrived = true;
            }
            else
            {
                b.Halt();
                b.SetCrouched(true);
                if (b.HasTarget) b.FaceTowards(b.Target.transform.position);
            }
        }

        public override bool IsFinished(EnemyBrain b)
            => b.Health.Fraction > RecoverHealth || Time.time - enteredAt > MaxTime;

        public override void OnExit(EnemyBrain b)
        {
            CoverManager.Release(b);
            b.SetCrouched(false);
            b.RetreatCooldownUntil = Time.time + 12f;
            point = null;
        }
    }
}
