using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Fight from cover. Move to a cover point that hides a crouched unit from the target, then
    /// loop: hide (crouched) -> peek (stand up and shoot over the low wall) -> hide.
    /// Longer hides when suppressed; the point is abandoned once the target can see it (flanked).
    /// </summary>
    public sealed class CoverAction : UtilityAction
    {
        enum Stage { Move, Hidden, Peek }

        public override string Name => "Cover";
        public override Color DebugColor => new Color(1f, 0.9f, 0.2f);
        protected override float RoleWeight(RoleProfile p) => p.WCover;

        const float MaxTime = 14f;

        CoverPoint point;
        Stage stage;
        float stageUntil;
        float enteredAt;
        float nextValidate;
        float hiddenSince;
        bool compromised;

        public CoverAction()
        {
            Add("CoverNeed", b => Mathf.Max(b.Suppression, b.Profile.CoverAffinity * (b.HasTarget ? 1f : 0f)), ResponseCurve.Linear(1f, 0f));
            Add("CoverQuality", b => b.BestCover.Quality, ResponseCurve.Linear(1f, 0f));
            Add("Vulnerability", b => 1f - b.Health.Fraction, ResponseCurve.Linear(0.5f, 0.5f));
            Add("TargetNear", b => 1f - Mathf.Clamp01(b.DistToTarget / (b.Profile.Range * 1.6f)), ResponseCurve.Linear(0.85f, 0.15f));
        }

        public override bool IsAvailable(EnemyBrain b) => b.HasTarget && (b.BestCover.Valid || b.Current == this);

        public override void OnEnter(EnemyBrain b)
        {
            point = b.BestCover.Point;
            compromised = false;
            enteredAt = Time.time;
            stage = Stage.Move;
            b.SetCrouched(false);
            if (point != null) CoverManager.Claim(point, b);
        }

        public override void Tick(EnemyBrain b, float dt)
        {
            if (point == null || !b.HasTarget) return;
            var p = b.Profile;

            switch (stage)
            {
                case Stage.Move:
                    b.MoveTo(point.Position, true, 0.15f);
                    if ((b.Position - point.Position).sqrMagnitude < 0.6f * 0.6f || b.HasArrived(0.5f))
                    {
                        b.Halt();
                        stage = Stage.Hidden;
                        hiddenSince = Time.time;
                        stageUntil = Time.time + HideTime(b);
                        b.SetCrouched(true);
                    }
                    break;

                case Stage.Hidden:
                    b.Halt();
                    b.SetCrouched(true);
                    b.FaceTowards(b.Target.transform.position);

                    if (Time.time >= nextValidate)
                    {
                        nextValidate = Time.time + 0.5f;
                        if (CoverManager.Protection(point.Position, b.Target.AimPoint) < 0.99f) compromised = true;
                    }

                    if (Time.time >= stageUntil && CanPeek(b))
                    {
                        stage = Stage.Peek;
                        stageUntil = Time.time + PeekTime(p);
                        b.SetCrouched(false);
                    }
                    break;

                case Stage.Peek:
                    b.Halt();
                    b.SetCrouched(false);
                    b.FaceTarget = true;
                    if (b.LineOfFire()) b.Weapon.TryShoot(b.Target, 1f);

                    bool pinned = b.Suppression > 0.8f && b.Health.Fraction < 0.5f;
                    if (Time.time >= stageUntil || pinned)
                    {
                        stage = Stage.Hidden;
                        hiddenSince = Time.time;
                        stageUntil = Time.time + HideTime(b);
                        b.Weapon.CancelCharge();
                        b.SetCrouched(true);
                    }
                    break;
            }
        }

        public override bool IsFinished(EnemyBrain b)
        {
            if (point == null || !b.HasTarget || compromised) return true;
            if (Time.time - enteredAt > MaxTime) return true;
            // Hiding with no shot available: give up once things calm down.
            if (stage == Stage.Hidden && Time.time - hiddenSince > 5f && !CanPeek(b)) return true;
            return false;
        }

        public override void OnExit(EnemyBrain b)
        {
            CoverManager.Release(b);
            b.SetCrouched(false);
            point = null;
        }

        static float HideTime(EnemyBrain b)
            => Random.Range(0.9f, 1.8f) * (1f + b.Suppression * 1.5f);

        static float PeekTime(RoleProfile p)
            => p.ChargeTime > 0f ? p.ChargeTime + 0.9f : p.Burst * p.FireInterval + 0.7f;

        bool CanPeek(EnemyBrain b)
        {
            if (b.Suppression > 0.75f && b.Health.Fraction < 0.5f) return false;
            if (b.DistToTarget > b.Profile.Range) return false;
            return !Physics.Linecast(point.Position + Vector3.up * 1.6f, b.Target.AimPoint, GameLayers.ObstacleMask);
        }
    }
}
