using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Medic only: run to the most wounded ally and channel a healing beam.</summary>
    public sealed class SupportAction : UtilityAction
    {
        public override string Name => "Support";
        public override Color DebugColor => new Color(0.2f, 1f, 1f);
        protected override float RoleWeight(RoleProfile p) => p.WSupport;

        public const float HealRange = 10f;
        public const float HealPerSecond = 16f;

        float nextBeam;
        float nextSound;

        public SupportAction()
        {
            Add("AllyNeed", b => b.WoundedAllyNeed, ResponseCurve.Linear(1f, 0f));
            Add("AllyNear", b => b.WoundedAlly != null ? 1f - Mathf.Clamp01(Vector3.Distance(b.Position, b.WoundedAlly.Position) / 40f) : 0f,
                ResponseCurve.Linear(0.8f, 0.2f));
            Add("Safety", b => b.Suppression, ResponseCurve.Linear(-0.7f, 1f));
            Add("SelfHealthy", b => b.Health.Fraction, ResponseCurve.Linear(0.5f, 0.5f));
        }

        public override bool IsAvailable(EnemyBrain b)
            => b.Profile.WSupport > 0f && b.WoundedAlly != null && !b.WoundedAlly.IsDead;

        public override void OnEnter(EnemyBrain b) => b.SetCrouched(false);

        public override void Tick(EnemyBrain b, float dt)
        {
            var ally = b.WoundedAlly;
            if (ally == null || ally.IsDead) return;

            Vector3 allyPos = ally.Position;
            float d = Vector3.Distance(b.Position, allyPos);
            bool los = !Physics.Linecast(b.Eye, allyPos + Vector3.up * 1.2f, GameLayers.ObstacleMask);

            if (d > HealRange * 0.85f || !los)
            {
                b.MoveTo(allyPos, true, HealRange * 0.5f);
                return;
            }

            b.Halt();
            b.FaceTowards(allyPos);
            ally.Health.Heal(HealPerSecond * dt);

            if (Time.time >= nextBeam)
            {
                nextBeam = Time.time + 0.1f;
                Fx.Tracer(b.Muzzle, allyPos + Vector3.up * 1.2f, new Color(0.3f, 1f, 0.5f), 0.07f, 0.12f);
            }
            if (Time.time >= nextSound)
            {
                nextSound = Time.time + 0.7f;
                Fx.Sound(SfxKind.Heal, b.Position, 0.5f);
                Fx.Spark(allyPos + Vector3.up * 1.8f, new Color(0.3f, 1f, 0.5f), 0.3f, 0.4f);
            }
        }

        public override bool IsFinished(EnemyBrain b) => b.WoundedAlly == null || b.WoundedAlly.IsDead;
    }
}
