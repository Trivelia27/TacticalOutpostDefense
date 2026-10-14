using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Which imported model, weapon, colour and size a character uses.</summary>
    public sealed class ModelSpec
    {
        public string Character;               // Resources path, e.g. "Art/Characters/swat"
        public string Weapon;                  // weapon model (Resources path), attached to the right wrist
        public float Height = 1.85f;           // standing height in metres
        public Color Tint = Color.white;       // multiplies the clothing colour (skin, hair and black gear are left alone)
        public float StrideScale = 1f;         // > 1 for bulky characters whose legs cover more ground per cycle
        public float WeaponLength = 0.95f;     // world length of the weapon
        public Vector3 WeaponOffset;           // extra offset of the weapon in character space (metres)
        public float GripFraction = 0.40f;     // where the trigger hand holds the weapon, 0 = rear end, 1 = muzzle
        public float ForeFraction = 0.74f;     // where the support hand holds it
        public float CrouchDrop = 0.52f;       // crouch pose: pelvis drop (m), thigh / shin / chest angles (deg); the ground lock fine-tunes it
        public float CrouchThigh = 76f;
        public float CrouchShin = 116f;
        public float CrouchLean = 30f;
    }

    /// <summary>
    /// Role to model assignment. Everyone belongs to the same family of tactical low-poly characters (same skeleton and clips) so the squad
    /// looks consistent; roles differ by outfit colour, build, weapon and behaviour. Credits and licences: Assets/Art/CREDITS.txt.
    /// </summary>
    public static class CharacterCatalog
    {
        const string Chars = "Art/Characters/";
        const string Weapons = "Art/Weapons/";

        public static ModelSpec ForPlayer() => new ModelSpec { Character = Chars + "swat", Weapon = Weapons + "rifle-m4", Height = 1.8f };

        public static ModelSpec ForRole(RoleProfile p, float height)
        {
            switch (p.Role)
            {
                case EnemyRole.Assault:
                    return new ModelSpec
                    {
                        Character = Chars + (Random.value < 0.5f ? "soldier-tactical" : "operator-black"),
                        Weapon = Weapons + "rifle-ak", Height = height, Tint = new Color(1.5f, 1.25f, 0.9f)          // desert tan
                    };
                case EnemyRole.Flanker:
                    return new ModelSpec { Character = Chars + "swat", Weapon = Weapons + "smg", Height = height * 0.98f, WeaponLength = 0.7f, Tint = new Color(1.25f, 0.95f, 0.75f) };   // dark brown
                case EnemyRole.Sniper:
                    return new ModelSpec { Character = Chars + "operator-black", Weapon = Weapons + "sniper-rifle", Height = height, WeaponLength = 1.3f, Tint = new Color(0.75f, 0.95f, 0.7f) };   // olive drab
                case EnemyRole.Heavy:
                    return new ModelSpec
                    {
                        Character = Chars + "swat", Weapon = Weapons + "minigun", Height = height * 1.07f, WeaponLength = 0.95f, StrideScale = 1.05f, CrouchThigh = 92f, CrouchShin = 132f, CrouchLean = 38f,
                        GripFraction = 0.45f, ForeFraction = 0.78f, Tint = new Color(0.75f, 0.85f, 0.8f), CrouchDrop = 0.58f
                    };
                default:
                    return new ModelSpec { Character = Chars + "soldier-tactical", Weapon = Weapons + "smg", Height = height * 0.97f, WeaponLength = 0.65f, Tint = new Color(1.6f, 1.7f, 1.6f) };   // light grey-green field-medic
            }
        }
    }
}
