using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Close the distance: push towards the current target / the reactor, or follow the squad (medics).</summary>
    public sealed class AdvanceAction : UtilityAction
    {
        public override string Name => "Advance";
        public override Color DebugColor => new Color(0.3f, 0.6f, 1f);
        protected override float RoleWeight(RoleProfile p) => p.WAdvance;

        public AdvanceAction()
        {
            Add("ObjectiveDistance", b => b.DistToObjective01, ResponseCurve.Linear(0.75f, 0.25f));
            Add("ShotAvailable", b => b.ShotReady ? 1f : 0f, ResponseCurve.Linear(-0.65f, 1f));
            Add("Healthy", b => b.Health.Fraction, ResponseCurve.Linear(0.6f, 0.4f));
            Add("NotSuppressed", b => b.Suppression, ResponseCurve.Linear(-0.7f, 1f));
        }

        public override void OnEnter(EnemyBrain b) => b.SetCrouched(false);

        public override void Tick(EnemyBrain b, float dt)
        {
            var p = b.Profile;
            Vector3 goal;
            float stop;

            if (p.Role == EnemyRole.Medic)
            {
                var buddy = SquadDirector.NearestAlly(b, e => e.Profile.Role != EnemyRole.Medic);
                if (buddy != null) { goal = buddy.Position; stop = 4f; }
                else { goal = ObjectivePosition(b); stop = 8f; }
            }
            else if (b.HasTarget)
            {
                goal = b.Target.transform.position;
                stop = Mathf.Max(1f, p.PreferredRange * 0.8f);
            }
            else
            {
                goal = ObjectivePosition(b);
                stop = Mathf.Max(1f, p.PreferredRange * 0.8f);
            }

            b.MoveTo(goal, true, stop);

            // Walking fire for aggressive roles.
            if (p.MoveFire && b.LineOfFire() && b.DistToTarget <= p.Range * 0.9f)
            {
                b.FaceTarget = true;
                b.Weapon.TryShoot(b.Target, 1.8f);
            }
        }

        static Vector3 ObjectivePosition(EnemyBrain b)
            => Reactor.Instance != null ? Reactor.Instance.transform.position : b.Position;
    }
}
