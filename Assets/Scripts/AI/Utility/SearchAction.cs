using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    /// <summary>
    /// The target (player or turret) was known but is out of sight: go to its last known position, look around a few
    /// spots nearby, and give up when nothing is found (the memory is then forgotten and the enemy goes back to the objective).
    /// Seeing the target again ends the search at once (the Attack/Cover scores take over).
    /// </summary>
    public sealed class SearchAction : UtilityAction
    {
        enum Stage { Go, Look, Sweep, Done }

        public override string Name => "Search";
        public override Color DebugColor => new Color(1f, 0.55f, 0.1f);
        protected override float RoleWeight(RoleProfile p) => p.WSearch;

        const int SweepPoints = 3;
        const float SweepRadius = 7f;

        Stage stage;
        Vector3 origin;
        Vector3 spot;
        int visited;
        float lookUntil;
        float lookStart;
        Vector3 lookDir;

        public SearchAction()
        {
            Add("TargetLost", b => b.TargetMem != null && !b.TargetVisible ? 1f : 0f, ResponseCurve.Linear(1f, 0f));
            Add("MemoryFresh", b => b.TargetMem == null ? 0f : 1f - b.TargetMem.Age / EnemySenses.MemorySeconds, ResponseCurve.Linear(0.8f, 0.2f));
            Add("NotSuppressed", b => b.Suppression, ResponseCurve.Linear(-0.6f, 1f));
            Add("Healthy", b => b.Health.Fraction, ResponseCurve.Linear(0.5f, 0.5f));
        }

        public override bool IsAvailable(EnemyBrain b)
            => b.HasTarget && b.Target.kind != TargetKind.Reactor && b.TargetMem != null
               && !b.TargetVisible && b.TargetMem.Age < EnemySenses.MemorySeconds;

        public override void OnEnter(EnemyBrain b)
        {
            b.SetCrouched(false);
            Begin(b);
        }

        void Begin(EnemyBrain b)
        {
            origin = b.TargetMem != null ? b.TargetMem.Position : b.Position;
            stage = Stage.Go;
            visited = 0;
        }

        public override void Tick(EnemyBrain b, float dt)
        {
            var m = b.TargetMem;
            if (m == null || !b.HasTarget) return;

            // New information somewhere else (a fresh sound or report): restart from there.
            if (stage != Stage.Go && m.Age < 0.6f && (m.Position - origin).sqrMagnitude > 6f * 6f) Begin(b);

            switch (stage)
            {
                case Stage.Go:
                    b.MoveTo(origin, true, 1.5f);
                    if (Flat(b.Position, origin) < 2.5f || b.HasArrived(1f))
                    {
                        b.Halt();
                        m.Searched = true;
                        StartLook(b);
                        stage = Stage.Look;
                    }
                    break;

                case Stage.Look:
                    LookAround(b);
                    if (Time.time >= lookUntil)
                    {
                        if (visited >= SweepPoints) { stage = Stage.Done; break; }
                        if (PickSweepSpot()) { visited++; stage = Stage.Sweep; }
                        else stage = Stage.Done;
                    }
                    break;

                case Stage.Sweep:
                    b.MoveTo(spot, false, 0.5f);
                    if (Flat(b.Position, spot) < 1.2f || b.HasArrived(0.6f))
                    {
                        b.Halt();
                        StartLook(b);
                        stage = Stage.Look;
                    }
                    break;

                case Stage.Done:
                    // Nothing found: forget the target so the enemy returns to the objective.
                    b.Senses.Forget(b.Target);
                    break;
            }
        }

        public override bool IsFinished(EnemyBrain b) => stage == Stage.Done;

        void StartLook(EnemyBrain b)
        {
            lookStart = Time.time;
            lookUntil = Time.time + 1.6f;
            lookDir = b.transform.forward;
        }

        void LookAround(EnemyBrain b)
        {
            // Sweep the view left and right: the field of view is what finds a hidden target.
            float angle = Mathf.Sin((Time.time - lookStart) * 3.4f) * 80f;
            b.FaceTowards(b.Position + Quaternion.Euler(0f, angle, 0f) * lookDir * 5f);
        }

        bool PickSweepSpot()
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 r = Random.insideUnitCircle * SweepRadius;
                Vector3 candidate = origin + new Vector3(r.x, 0f, r.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.5f, NavMesh.AllAreas) && Flat(hit.position, origin) > 2.5f)
                {
                    spot = hit.position;
                    return true;
                }
            }
            return false;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
