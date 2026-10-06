using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    public enum EnemyRole { Assault, Flanker, Sniper, Heavy, Medic }

    /// <summary>
    /// Data-driven description of an enemy role: stats, weapon and – most importantly –
    /// the weights that bias the Utility AI (what the role "wants" to do) and the
    /// target selector (what the role "wants" to shoot).
    /// </summary>
    public sealed class RoleProfile
    {
        public EnemyRole Role;
        public string Name;
        public string Letter;
        public Color Color;

        // Body
        public float MaxHealth;
        public float WalkSpeed;
        public float RunSpeed;
        public float Radius = 0.45f;
        public float BodyScale = 1f;
        public float DamageTaken = 1f;
        public int ScrapReward = 10;

        // Weapon
        public float Damage;
        public float FireInterval;     // seconds between shots inside a burst (or cooldown for charged weapons)
        public int Burst = 3;
        public float BurstPause;
        public float SpreadDeg;
        public float Range;
        public float PreferredRange;
        public float MinRange;
        public float ChargeTime;       // > 0 => telegraphed sniper shot
        public float StructureMult = 1f;
        public bool MoveFire;          // may shoot while advancing / flanking
        public SfxKind Sfx = SfxKind.Rifle;
        public Color Tracer = new Color(1f, 0.8f, 0.3f);

        // Utility weights (multiplier on the final action score)
        public float WAdvance = 1f;
        public float WAttack = 1f;
        public float WCover = 1f;
        public float WFlank = 0.3f;
        public float WRetreat = 1f;
        public float WSupport = 0f;
        /// <summary>0..1 – how much the role likes fighting from cover even without being shot at.</summary>
        public float CoverAffinity = 0.3f;

        // Target preference multipliers
        public float PrefPlayer = 1f;
        public float PrefReactor = 1f;
        public float PrefTurret = 1f;

        public float TargetPref(TargetKind kind)
        {
            switch (kind)
            {
                case TargetKind.Player: return PrefPlayer;
                case TargetKind.Reactor: return PrefReactor;
                default: return PrefTurret;
            }
        }

        static readonly Dictionary<EnemyRole, RoleProfile> map = new Dictionary<EnemyRole, RoleProfile>
        {
            {
                EnemyRole.Assault, new RoleProfile
                {
                    Role = EnemyRole.Assault, Name = "Assault", Letter = "A", Color = new Color(0.85f, 0.25f, 0.2f),
                    MaxHealth = 100f, WalkSpeed = 3.2f, RunSpeed = 5.0f, Radius = 0.45f, ScrapReward = 10,
                    Damage = 7f, FireInterval = 0.17f, Burst = 3, BurstPause = 0.85f, SpreadDeg = 2.6f,
                    Range = 28f, PreferredRange = 16f, MinRange = 6f, MoveFire = true,
                    Sfx = SfxKind.Rifle, Tracer = new Color(1f, 0.75f, 0.3f),
                    WAdvance = 1.0f, WAttack = 1.0f, WCover = 0.85f, WFlank = 0.4f, WRetreat = 0.8f, WSupport = 0f,
                    CoverAffinity = 0.35f, PrefPlayer = 1.0f, PrefReactor = 1.0f, PrefTurret = 0.9f
                }
            },
            {
                EnemyRole.Flanker, new RoleProfile
                {
                    Role = EnemyRole.Flanker, Name = "Flanker", Letter = "F", Color = new Color(0.95f, 0.65f, 0.1f),
                    MaxHealth = 70f, WalkSpeed = 4.4f, RunSpeed = 7.2f, Radius = 0.4f, BodyScale = 0.92f, ScrapReward = 15,
                    Damage = 5f, FireInterval = 0.1f, Burst = 5, BurstPause = 0.7f, SpreadDeg = 4.5f,
                    Range = 20f, PreferredRange = 11f, MinRange = 3f, MoveFire = true,
                    Sfx = SfxKind.Smg, Tracer = new Color(1f, 0.9f, 0.3f),
                    WAdvance = 0.7f, WAttack = 0.9f, WCover = 0.5f, WFlank = 1.5f, WRetreat = 0.6f, WSupport = 0f,
                    CoverAffinity = 0.15f, PrefPlayer = 1.25f, PrefReactor = 0.8f, PrefTurret = 0.7f
                }
            },
            {
                EnemyRole.Sniper, new RoleProfile
                {
                    Role = EnemyRole.Sniper, Name = "Sniper", Letter = "S", Color = new Color(0.55f, 0.35f, 0.85f),
                    MaxHealth = 60f, WalkSpeed = 2.8f, RunSpeed = 4.2f, Radius = 0.4f, BodyScale = 1.05f, ScrapReward = 25,
                    Damage = 38f, FireInterval = 2.2f, Burst = 1, BurstPause = 2.2f, SpreadDeg = 0.4f,
                    Range = 46f, PreferredRange = 30f, MinRange = 12f, ChargeTime = 1.3f, MoveFire = false,
                    Sfx = SfxKind.Sniper, Tracer = new Color(1f, 0.3f, 0.9f),
                    WAdvance = 0.35f, WAttack = 0.3f, WCover = 1.5f, WFlank = 0.1f, WRetreat = 1.0f, WSupport = 0f,
                    CoverAffinity = 0.85f, PrefPlayer = 1.3f, PrefReactor = 0.5f, PrefTurret = 1.2f
                }
            },
            {
                EnemyRole.Heavy, new RoleProfile
                {
                    Role = EnemyRole.Heavy, Name = "Heavy", Letter = "H", Color = new Color(0.35f, 0.4f, 0.45f),
                    MaxHealth = 380f, WalkSpeed = 2.0f, RunSpeed = 3.0f, Radius = 0.62f, BodyScale = 1.35f, DamageTaken = 0.75f,
                    ScrapReward = 40,
                    Damage = 6f, FireInterval = 0.09f, Burst = 12, BurstPause = 1.2f, SpreadDeg = 5.5f,
                    Range = 22f, PreferredRange = 13f, MinRange = 0f, StructureMult = 2.2f, MoveFire = true,
                    Sfx = SfxKind.Heavy, Tracer = new Color(1f, 0.5f, 0.2f),
                    WAdvance = 1.4f, WAttack = 1.3f, WCover = 0.25f, WFlank = 0f, WRetreat = 0.1f, WSupport = 0f,
                    CoverAffinity = 0.05f, PrefPlayer = 0.7f, PrefReactor = 1.25f, PrefTurret = 1.6f
                }
            },
            {
                EnemyRole.Medic, new RoleProfile
                {
                    Role = EnemyRole.Medic, Name = "Medic", Letter = "M", Color = new Color(0.25f, 0.8f, 0.45f),
                    MaxHealth = 80f, WalkSpeed = 3.6f, RunSpeed = 5.4f, Radius = 0.42f, ScrapReward = 20,
                    Damage = 4f, FireInterval = 0.3f, Burst = 2, BurstPause = 1.5f, SpreadDeg = 4f,
                    Range = 18f, PreferredRange = 14f, MinRange = 4f, MoveFire = false,
                    Sfx = SfxKind.Pistol, Tracer = new Color(0.6f, 1f, 0.6f),
                    WAdvance = 0.6f, WAttack = 0.4f, WCover = 1.3f, WFlank = 0f, WRetreat = 1.0f, WSupport = 1.7f,
                    CoverAffinity = 0.7f, PrefPlayer = 1f, PrefReactor = 1f, PrefTurret = 1f
                }
            },
        };

        public static RoleProfile Get(EnemyRole role) => map[role];
    }
}
