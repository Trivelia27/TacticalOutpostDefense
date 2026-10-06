using System;
using UnityEngine;

namespace TacticalOutpost
{
    public enum Team { Defender, Attacker }

    /// <summary>Shared hit-point container for the player, structures and enemies.</summary>
    public class Health : MonoBehaviour
    {
        public float maxHealth = 100f;
        public Team team = Team.Defender;
        public bool isStructure;
        public float damageTakenMultiplier = 1f;

        bool initialised;

        public float Current { get; private set; }
        public float Fraction => maxHealth > 0f ? Mathf.Clamp01(Current / maxHealth) : 0f;
        public bool IsDead => Current <= 0f;
        public float LastDamageTime { get; private set; } = -999f;
        public GameObject LastAttacker { get; private set; }

        /// <summary>(victim, damageApplied, attacker)</summary>
        public event Action<Health, float, GameObject> Damaged;
        public event Action<Health, GameObject> Died;

        void Awake()
        {
            if (!initialised)
            {
                Current = maxHealth;
                initialised = true;
            }
        }

        public void Setup(float max, Team owner, bool structure = false, float damageMultiplier = 1f)
        {
            maxHealth = max;
            team = owner;
            isStructure = structure;
            damageTakenMultiplier = damageMultiplier;
            Current = max;
            initialised = true;
        }

        public void ResetFull()
        {
            Current = maxHealth;
            initialised = true;
        }

        public void TakeDamage(float amount, GameObject source)
        {
            if (IsDead || amount <= 0f) return;
            float applied = amount * damageTakenMultiplier;
            Current = Mathf.Max(0f, Current - applied);
            LastDamageTime = Time.time;
            LastAttacker = source;
            Damaged?.Invoke(this, applied, source);
            if (Current <= 0f) Died?.Invoke(this, source);
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Current = Mathf.Min(maxHealth, Current + amount);
        }
    }
}
