using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// A defensive turret position. Starts Active, Empty (build it for scrap) or Destroyed (rebuild).
    /// Active turrets auto-target the nearest visible enemy and are valid enemy targets themselves.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(Targetable))]
    public class TurretPost : MonoBehaviour
    {
        public enum State { Empty, Active, Destroyed }

        public static readonly List<TurretPost> All = new List<TurretPost>();

        public State startState = State.Active;
        public float range = 26f;
        public float damage = 9f;
        public float fireRate = 5f;
        public float maxIntegrity = 220f;
        public int buildCost = 75;

        [Header("Visuals (assigned by the scene builder)")]
        public Transform head;
        public Transform muzzle;
        public GameObject activeVisual;
        public GameObject ghostVisual;
        public GameObject wreckVisual;

        public State Current { get; private set; }
        public Health Health { get; private set; }
        public Targetable Targetable { get; private set; }
        public bool IsActive => Current == State.Active;
        public int Cost => Current == State.Destroyed ? buildCost / 2 : buildCost;

        float nextShot;
        float nextScan;
        Transform currentTarget;

        void Awake()
        {
            Health = GetComponent<Health>();
            Targetable = GetComponent<Targetable>();
            Targetable.kind = TargetKind.Turret;
            Targetable.aimHeight = 1.4f;
            Targetable.strategicValue = 0.45f;
            Targetable.threat = 0.7f;
            Health.Setup(maxIntegrity, Team.Defender, true);
            Health.Died += OnDied;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start() => Apply(startState);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void Apply(State s)
        {
            Current = s;
            if (activeVisual != null) activeVisual.SetActive(s == State.Active);
            if (ghostVisual != null) ghostVisual.SetActive(s == State.Empty);
            if (wreckVisual != null) wreckVisual.SetActive(s == State.Destroyed);
            Targetable.enabled = s == State.Active;
        }

        public bool Build()
        {
            if (Current == State.Active) return false;
            Health.Setup(maxIntegrity, Team.Defender, true);
            Apply(State.Active);
            Fx.Sound(SfxKind.Build, transform.position, 0.8f);
            Fx.Burst(transform.position + Vector3.up * 0.8f, new Color(0.4f, 0.8f, 1f), 3f, 0.4f);
            return true;
        }

        void OnDied(Health h, GameObject src)
        {
            Apply(State.Destroyed);
            Fx.Burst(transform.position + Vector3.up, new Color(1f, 0.55f, 0.2f), 5f, 0.6f);
            Fx.Sound(SfxKind.Explosion, transform.position, 0.8f, 1.3f);
        }

        public void Repair(float hp) => Health.Heal(hp);

        void Update()
        {
            if (Current != State.Active) return;
            var gm = GameManager.Instance;
            if (gm != null && !gm.IsPlaying) return;

            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.2f;
                currentTarget = FindTarget();
            }

            if (currentTarget == null) return;

            Vector3 aim = currentTarget.position + Vector3.up * 1.0f;
            Vector3 flat = aim - head.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
            {
                var want = Quaternion.LookRotation(flat);
                head.rotation = Quaternion.RotateTowards(head.rotation, want, 540f * Time.deltaTime);
                if (Quaternion.Angle(head.rotation, want) < 8f && Time.time >= nextShot)
                {
                    nextShot = Time.time + 1f / fireRate;
                    Ballistics.Fire(muzzle.position, aim, 1.5f, range * 1.2f, GameLayers.DefenderBulletMask,
                                    damage, gameObject, Team.Defender, new Color(0.4f, 0.9f, 1f));
                    Fx.Spark(muzzle.position, new Color(0.6f, 0.95f, 1f), 0.25f, 0.05f);
                    Fx.Sound(SfxKind.Turret, muzzle.position, 0.45f, Random.Range(0.95f, 1.1f));
                }
            }
        }

        Transform FindTarget()
        {
            Transform best = null;
            float bestD = range * range;
            var list = SquadDirector.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.IsDead) continue;
                float d = (e.Position - transform.position).sqrMagnitude;
                if (d >= bestD) continue;
                if (Physics.Linecast(muzzle.position, e.Position + Vector3.up * 1.0f, GameLayers.ObstacleMask)) continue;
                bestD = d;
                best = e.transform;
            }
            return best;
        }
    }
}
