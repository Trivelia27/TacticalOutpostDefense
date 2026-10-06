using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    public enum TargetKind { Player, Reactor, Turret }

    /// <summary>
    /// Marks something the attackers may choose to shoot at. Enemy target selection
    /// iterates <see cref="All"/> and scores every entry (see TargetSelector).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Targetable : MonoBehaviour
    {
        public static readonly List<Targetable> All = new List<Targetable>();

        public TargetKind kind = TargetKind.Player;
        [Tooltip("Height above the pivot that bullets aim at / line of sight is tested to.")]
        public float aimHeight = 1.1f;
        [Tooltip("How important this is for the attackers' objective (0..1).")]
        public float strategicValue = 0.5f;
        [Tooltip("How dangerous this is to attackers (0..1).")]
        public float threat = 0.5f;

        public Health Health { get; private set; }
        public Vector3 AimPoint => transform.position + Vector3.up * aimHeight;
        public bool IsValid => isActiveAndEnabled && Health != null && !Health.IsDead;

        void Awake() => Health = GetComponent<Health>();

        void OnEnable()
        {
            if (Health == null) Health = GetComponent<Health>();
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable() => All.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
    }
}
