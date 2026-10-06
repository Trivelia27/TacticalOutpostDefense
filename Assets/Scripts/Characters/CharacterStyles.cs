using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Outfit presets: attackers wear dark tactical gear with role-coloured accents; defenders wear tan + blue.</summary>
    public static class CharacterStyles
    {
        public static HumanoidStyle ForPlayer() => new HumanoidStyle
        {
            Uniform = new Color(0.4f, 0.37f, 0.27f),
            Vest = new Color(0.13f, 0.21f, 0.34f),
            Helmet = new Color(0.2f, 0.27f, 0.38f),
            Accent = new Color(0.3f, 0.85f, 1f),
            Gear = new Color(0.1f, 0.1f, 0.11f),
            Head = HeadGear.Helmet,
            Weapon = WeaponKind.Rifle,
            ShoulderPads = true,
            Backpack = true,
            Scale = new Vector3(1f, 1f, 1f),
        };

        public static HumanoidStyle ForRole(RoleProfile p)
        {
            float v = Random.Range(-0.02f, 0.02f);
            var s = new HumanoidStyle
            {
                Uniform = new Color(0.2f + v, 0.22f + v, 0.26f + v),
                Vest = Color.Lerp(new Color(0.1f, 0.1f, 0.11f), p.Color, 0.42f),
                Helmet = Color.Lerp(new Color(0.15f, 0.16f, 0.18f), p.Color, 0.14f),
                Accent = p.Color,
                Gear = new Color(0.08f, 0.08f, 0.09f),
                Skin = Color.Lerp(new Color(0.76f, 0.6f, 0.5f), new Color(0.4f, 0.29f, 0.22f), Random.value),
            };

            switch (p.Role)
            {
                case EnemyRole.Assault:
                    s.Head = HeadGear.Helmet;
                    s.Weapon = WeaponKind.Rifle;
                    s.ShoulderPads = Random.value < 0.4f;
                    s.Scale = new Vector3(1f, 1f, 1f);
                    break;
                case EnemyRole.Flanker:
                    s.Uniform = new Color(0.15f, 0.17f, 0.2f);
                    s.Head = HeadGear.Cap;
                    s.Weapon = WeaponKind.Smg;
                    s.Balaclava = true;
                    s.Scarf = true;
                    s.Backpack = false;
                    s.Scale = new Vector3(0.93f, 0.99f, 0.93f);
                    break;
                case EnemyRole.Sniper:
                    s.Uniform = new Color(0.22f, 0.22f, 0.15f);
                    s.Helmet = new Color(0.21f, 0.21f, 0.15f);
                    s.Vest = new Color(0.17f, 0.17f, 0.12f);
                    s.Head = HeadGear.Hood;
                    s.Weapon = WeaponKind.Sniper;
                    s.Cape = true;
                    s.Balaclava = true;
                    s.Scale = new Vector3(0.98f, 1.0f, 0.98f);
                    break;
                case EnemyRole.Heavy:
                    s.Uniform = new Color(0.22f, 0.24f, 0.27f);
                    s.Vest = new Color(0.2f, 0.22f, 0.25f);
                    s.Helmet = new Color(0.24f, 0.26f, 0.28f);
                    s.Head = HeadGear.HeavyHelmet;
                    s.Weapon = WeaponKind.Minigun;
                    s.ShoulderPads = true;
                    s.BigPack = true;
                    s.Scale = new Vector3(1.3f, 1.0f, 1.28f);
                    break;
                case EnemyRole.Medic:
                    s.Uniform = new Color(0.24f, 0.26f, 0.24f);
                    s.Vest = new Color(0.78f, 0.8f, 0.78f);
                    s.Head = HeadGear.MedicHelmet;
                    s.Weapon = WeaponKind.Pistol;
                    s.Cross = true;
                    s.Scale = new Vector3(0.98f, 1f, 0.98f);
                    break;
            }
            return s;
        }
    }
}
