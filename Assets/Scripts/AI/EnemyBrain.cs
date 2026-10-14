using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    /// <summary>Runtime toggles for the on-screen AI debugging aids (F1 master, F2 lines, F3 inspector, F4 perception overlay).</summary>
    public static class AIDebug
    {
        public static bool Master = true;          // F1: AI Debug Mode on/off
        public static bool ShowLabels = true;      // action labels above enemies
        public static bool ShowLines = false;      // F2: line from every enemy to its target
        public static bool ShowInspector = true;   // F3: Utility AI inspector panel
        public static bool ShowWorld = true;       // F4: FOV, detection radius, raycast, path, last known position, cover, flank
        public static EnemyBrain Selected;         // the enemy under the crosshair / cursor
        public static bool Hold;                   // F7: enemies keep perceiving and deciding but stand still and hold fire (perception tests)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Hold = false; Selected = null; }

        public static bool LabelsOn => Master && ShowLabels;
        public static bool LinesOn => Master && ShowLines;
        public static bool InspectorOn => Master && ShowInspector;
        public static bool WorldOn => Master && ShowWorld;
    }

    /// <summary>
    /// The enemy "mind". Every think tick it
    ///   1. selects a target (TargetSelector),
    ///   2. refreshes perception (line of sight, cover and flank plans, wounded allies),
    ///   3. scores all Utility actions and switches to the best one (with commitment hysteresis).
    /// The active action then drives movement / shooting every frame.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyBrain : MonoBehaviour
    {
        const float ThinkInterval = 0.3f;
        const float MinCommitTime = 1.0f;
        const float CommitBonus = 1.15f;
        const float SuppressionDecay = 0.25f;
        const float TurnSpeed = 480f;

        public RoleProfile Profile { get; private set; }
        public NavMeshAgent Agent { get; private set; }
        public Health Health { get; private set; }
        public EnemyWeapon Weapon { get; private set; }
        public EnemyVisual Visual { get; private set; }

        // ---- Perception / blackboard ----
        public Targetable Target { get; set; }
        public bool HasTarget => Target != null && Target.IsValid;
        public bool HasLos { get; private set; }
        public float DistToTarget { get; private set; } = 999f;
        public float Suppression { get; private set; }
        public int PinningAllies { get; private set; }
        public CoverQuery BestCover { get; private set; } = CoverQuery.None;
        public CoverQuery RetreatCover { get; private set; } = CoverQuery.None;
        public FlankPlan Flank { get; private set; }
        public EnemyBrain WoundedAlly { get; private set; }
        public float WoundedAllyNeed { get; private set; }
        public float LastFlankCompleteTime = -99f;
        public float RetreatCooldownUntil;
        public float LastTargetSwitch = -99f;
        // Perception and memory of the current target
        public EnemySenses Senses { get; private set; }
        public TargetMemory TargetMem { get; private set; }
        public bool TargetVisible => HasTarget && TargetMem != null && TargetMem.Visible;
        /// <summary>Where this enemy believes the target is: its real position while in view, otherwise the last known position.</summary>
        public Vector3 TargetPos => !HasTarget ? Position
            : (TargetVisible || Target.kind == TargetKind.Reactor || TargetMem == null) ? Target.transform.position : TargetMem.Position;
        public Vector3 TargetAim => TargetVisible ? Target.AimPoint : TargetPos + Vector3.up * 1.1f;
        public SenseResult TargetSense { get; private set; }
        public string SteeringLabel { get; private set; } = "Idle";
        public readonly List<KeyValuePair<string, float>> TargetScores = new List<KeyValuePair<string, float>>();

        public bool Crouched { get; private set; }
        public bool IsDead => Health == null || Health.IsDead;
        public bool FaceTarget { get; set; }
        Vector3? faceOverride;

        public Vector3 Position => transform.position;
        public Vector3 Eye => transform.position + Vector3.up * (Crouched ? 0.95f : 1.6f);
        public Vector3 Muzzle => transform.position + Vector3.up * (Crouched ? 0.85f : 1.45f) + transform.forward * 0.55f;

        public bool ShotReady => HasLos && DistToTarget <= Profile.Range;
        public float DistToObjective01
        {
            get
            {
                if (Reactor.Instance == null) return 1f;
                return Mathf.Clamp01(Vector3.Distance(Position, Reactor.Instance.transform.position) / 60f);
            }
        }

        // ---- Utility AI ----
        readonly List<UtilityAction> actions = new List<UtilityAction>();
        public IReadOnlyList<UtilityAction> Actions => actions;
        public UtilityAction Current { get; private set; }
        public float TimeInAction => Time.time - actionStart;
        float actionStart;
        float nextThink;

        // ---- Movement cache ----
        Vector3 lastDest = new Vector3(float.MaxValue, 0, 0);
        float lastDestTime;
        float baseSpeed;

        // ---- LOS cache ----
        float losCacheTime = -1f;
        bool losCache;

        LineRenderer debugLine;

        public void Init(RoleProfile profile, NavMeshAgent agent, Health health, EnemyWeapon weapon, EnemyVisual visual)
        {
            Profile = profile;
            Agent = agent;
            Health = health;
            Weapon = weapon;
            Visual = visual;

            Agent.updateRotation = false;
            Senses = new EnemySenses();
            Weapon.Init(this);

            actions.Add(new AdvanceAction());
            actions.Add(new AttackAction());
            actions.Add(new CoverAction());
            actions.Add(new FlankAction());
            actions.Add(new RetreatAction());
            actions.Add(new SupportAction());
            actions.Add(new SearchAction());

            Health.Damaged += OnDamaged;
            Health.Died += OnDied;
            SquadDirector.Register(this);
            nextThink = Time.time + Random.Range(0f, ThinkInterval);
        }

        void OnDestroy() => SquadDirector.Unregister(this);

        // =====================================================================
        // Think / act loop
        // =====================================================================

        void Update()
        {
            if (IsDead) return;

            var gm = GameManager.Instance;
            if (gm != null && !gm.IsPlaying)
            {
                if (Agent.isOnNavMesh) Agent.isStopped = true;
                return;
            }

            float dt = Time.deltaTime;
            FaceTarget = false;
            faceOverride = null;
            Suppression = Mathf.MoveTowards(Suppression, 0f, SuppressionDecay * dt);

            if (Time.time >= nextThink)
            {
                nextThink = Time.time + ThinkInterval * Random.Range(0.85f, 1.2f);
                Think();
            }

            if (AIDebug.Hold)
            {
                Halt();                     // test lab: stand still, hold fire; perception, memory and decisions keep running
                Visual.SetAiming(false);
                UpdateDebugLine();
                return;
            }

            Current?.Tick(this, dt);
            ApplySteering(dt);
            Visual.SetAiming(FaceTarget || faceOverride.HasValue);
            UpdateFacing(dt);
            UpdateDebugLine();
        }

        void Think()
        {
            Senses.Update(this);            // perception + memory
            TargetSelector.Select(this);    // target selection over what is known

            TargetMem = HasTarget ? Senses.Get(Target) : null;
            if (HasTarget && TargetMem != null)
            {
                TargetSense = Senses.Probe(this, Target);
                DistToTarget = Vector3.Distance(Position, TargetPos);
                HasLos = TargetMem.Visible;
                PinningAllies = SquadDirector.CountEngaging(Target, this);

                // Tactical planning needs an actual sighting (or the objective): no planning against an unseen target.
                bool known = TargetMem.Visible || Target.kind == TargetKind.Reactor;
                BestCover = known ? CoverManager.FindBest(this, Target, CoverMode.Fight) : CoverQuery.None;
                RetreatCover = known && Health.Fraction < 0.6f
                    ? CoverManager.FindBest(this, Target, CoverMode.Retreat, 30f)
                    : CoverQuery.None;
                if (known && Profile.WFlank >= 0.3f && !(Current is FlankAction))
                    Flank = FlankPlanner.Plan(this);
                else if (!known)
                    Flank = default;
            }
            else
            {
                TargetSense = default;
                DistToTarget = 999f;
                HasLos = false;
                PinningAllies = 0;
                BestCover = CoverQuery.None;
                RetreatCover = CoverQuery.None;
                Flank = default;
            }

            if (Profile.WSupport > 0f)
                WoundedAlly = SquadDirector.FindWoundedAlly(this, 40f, out float need);
            else
                WoundedAlly = null;
            WoundedAllyNeed = WoundedAlly != null ? 1f - WoundedAlly.Health.Fraction : 0f;

            ChooseAction();
        }

        // =====================================================================
        // Steering (on top of NavMesh path following)
        //   Seek       - follow the NavMesh path towards the destination
        //   Arrive     - slow down smoothly when close to the destination
        //   Separation - push away from nearby squad mates so the group spreads out
        //   Avoidance  - NavMeshAgent local obstacle avoidance
        // =====================================================================

        const float ArriveRadius = 3.5f;
        const float SeparationRadius = 1.8f;

        void ApplySteering(float dt)
        {
            if (!Agent.isOnNavMesh || Agent.isStopped) { SteeringLabel = "Idle"; return; }

            SteeringLabel = "Seek";
            if (!Agent.pathPending && baseSpeed > 0f)
            {
                float rem = Agent.remainingDistance;
                if (rem < ArriveRadius)
                {
                    Agent.speed = baseSpeed * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(rem / ArriveRadius));
                    SteeringLabel = "Arrive";
                }
                else Agent.speed = baseSpeed;
            }

            Vector3 push = Vector3.zero;
            var list = SquadDirector.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == this || e == null || e.IsDead) continue;
                Vector3 d = Position - e.Position;
                d.y = 0f;
                float m = d.magnitude;
                if (m < SeparationRadius && m > 0.01f) push += d / m * (1f - m / SeparationRadius);
            }
            if (push.sqrMagnitude > 0.0004f)
            {
                Agent.Move(Vector3.ClampMagnitude(push, 1f) * 1.4f * dt);
                SteeringLabel += " + Separation";
            }
        }

        void ChooseAction()
        {
            UtilityAction best = null;
            float bestScore = -1f;
            float currentScore = 0f;

            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                float s = a.Evaluate(this);
                if (a == Current) { currentScore = s; s *= CommitBonus; }
                if (s > bestScore) { bestScore = s; best = a; }
            }

            if (best == null || best == Current) return;

            bool canSwitch = Current == null
                             || Time.time - actionStart >= MinCommitTime
                             || Current.IsFinished(this)
                             || bestScore > currentScore * 1.6f;
            if (canSwitch) SwitchTo(best);
        }

        void SwitchTo(UtilityAction next)
        {
            Current?.OnExit(this);
            Current = next;
            actionStart = Time.time;
            lastDest = new Vector3(float.MaxValue, 0, 0);
            Weapon.CancelCharge();
            Current.OnEnter(this);
        }

        // =====================================================================
        // Services used by actions
        // =====================================================================

        public void AddSuppression(float amount) => Suppression = Mathf.Clamp01(Suppression + amount);

        public void MoveTo(Vector3 dest, bool run, float stoppingDistance = 0.3f)
        {
            if (!Agent.isOnNavMesh) return;
            baseSpeed = (run ? Profile.RunSpeed : Profile.WalkSpeed) * (Crouched ? 0.6f : 1f);
            Agent.speed = baseSpeed;
            Agent.stoppingDistance = stoppingDistance;
            Agent.isStopped = false;

            bool changed = (dest - lastDest).sqrMagnitude > 0.6f * 0.6f;
            if (changed || Time.time - lastDestTime > 1.5f)
            {
                if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 6f, NavMesh.AllAreas)) dest = hit.position;
                Agent.SetDestination(dest);
                lastDest = dest;
                lastDestTime = Time.time;
            }
        }

        public void Halt()
        {
            if (Agent.isOnNavMesh && !Agent.isStopped)
            {
                Agent.isStopped = true;
                Agent.velocity = Vector3.zero;
            }
            lastDest = new Vector3(float.MaxValue, 0, 0);
        }

        public bool HasArrived(float tolerance = 0.5f)
        {
            if (!Agent.isOnNavMesh || Agent.pathPending) return false;
            return Agent.remainingDistance <= Mathf.Max(Agent.stoppingDistance, tolerance);
        }

        public void SetCrouched(bool value)
        {
            Crouched = value;
            Visual.SetCrouched(value);
        }

        public void FaceTowards(Vector3 worldPos) => faceOverride = worldPos;

        /// <summary>Cached (0.1 s) live line-of-fire check from the current eye position.</summary>
        public bool LineOfFire()
        {
            if (!HasTarget) return false;
            if (Time.time - losCacheTime > 0.1f)
            {
                losCacheTime = Time.time;
                losCache = TargetVisible && DistToTarget <= Profile.Range * 1.1f &&
                           !Physics.Linecast(Eye, Target.AimPoint, GameLayers.ObstacleMask);
            }
            return losCache;
        }

        public bool IsAimedAt(Targetable t, float toleranceDeg)
        {
            Vector3 d = t.transform.position - Position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return true;
            return Vector3.Angle(transform.forward, d) <= toleranceDeg;
        }

        void UpdateFacing(float dt)
        {
            Vector3 look = Vector3.zero;
            if (faceOverride.HasValue) look = faceOverride.Value - Position;
            else if (FaceTarget && HasTarget) look = TargetPos - Position;
            else if (Agent.isOnNavMesh && Agent.velocity.sqrMagnitude > 0.25f) look = Agent.velocity;

            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), TurnSpeed * dt);
        }

        // =====================================================================
        // Events
        // =====================================================================

        void OnDamaged(Health h, float amount, GameObject source)
        {
            AddSuppression(amount / (0.4f * Health.maxHealth));
            if (source != null && Senses != null)
            {
                var attacker = source.GetComponentInParent<Targetable>();
                if (attacker != null) Senses.Learn(attacker, source.transform.position, InfoSource.Hit);   // I know where that came from
            }
            Visual.Flash();
        }

        void OnDied(Health h, GameObject source)
        {
            Current?.OnExit(this);
            Current = null;
            Weapon.CancelCharge();
            SquadDirector.Unregister(this);
            CoverManager.Release(this);

            if (Agent.isOnNavMesh) Agent.isStopped = true;
            Agent.enabled = false;
            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.detectCollisions = false;

            GameManager.Instance?.RegisterKill(this);

            Vector3 push = source != null ? transform.position - source.transform.position : -transform.forward;
            Visual.PlayDeath(push);
            if (debugLine != null) debugLine.enabled = false;
            Fx.Burst(transform.position + Vector3.up, new Color(1f, 0.6f, 0.2f), 1.6f, 0.35f);
            Fx.Sound(SfxKind.Hit, transform.position, 0.9f, 0.7f);

            enabled = false;
            Destroy(gameObject, 3f);
        }

        // =====================================================================
        // Debug
        // =====================================================================

        void UpdateDebugLine()
        {
            if (!AIDebug.LinesOn || !HasTarget)
            {
                if (debugLine != null && debugLine.enabled) debugLine.enabled = false;
                return;
            }

            if (debugLine == null)
            {
                var go = new GameObject("DebugLine");
                go.transform.SetParent(transform, false);
                debugLine = go.AddComponent<LineRenderer>();
                debugLine.positionCount = 2;
                debugLine.useWorldSpace = true;
                debugLine.startWidth = 0.06f;
                debugLine.endWidth = 0.02f;
                debugLine.sharedMaterial = Visuals.Unlit(Color.white);
                debugLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Color c = Current != null ? Current.DebugColor : Color.white;
            c.a = 0.8f;
            debugLine.startColor = debugLine.endColor = c;
            debugLine.SetPosition(0, Position + Vector3.up * 0.3f);
            debugLine.SetPosition(1, TargetPos + Vector3.up * 0.3f);
            debugLine.enabled = true;
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            AIDebugDraw.Gizmos(this);
        }
    }
}
