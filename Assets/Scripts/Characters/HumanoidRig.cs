using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    public enum HeadGear { Helmet, Cap, Hood, HeavyHelmet, MedicHelmet }
    public enum WeaponKind { Rifle, Smg, Sniper, Minigun, Pistol }

    /// <summary>Appearance description for a soldier. Built into an articulated primitive humanoid.</summary>
    public sealed class HumanoidStyle
    {
        public Color Uniform = new Color(0.2f, 0.21f, 0.22f);
        public Color Vest = new Color(0.12f, 0.12f, 0.13f);
        public Color Helmet = new Color(0.16f, 0.17f, 0.18f);
        public Color Accent = new Color(0.9f, 0.2f, 0.2f);
        public Color Skin = new Color(0.72f, 0.57f, 0.46f);
        public Color Gear = new Color(0.09f, 0.09f, 0.1f);
        public HeadGear Head = HeadGear.Helmet;
        public WeaponKind Weapon = WeaponKind.Rifle;
        public Vector3 Scale = Vector3.one;      // x = width, y = height, z = depth
        public bool Backpack = true;
        public bool BigPack;
        public bool ShoulderPads;
        public bool KneePads = true;
        public bool Balaclava;
        public bool Cross;
        public bool Cape;
        public bool Scarf;
    }

    /// <summary>
    /// Articulated humanoid made of primitives (pelvis, spine, head, arms with elbows, legs with knees,
    /// tactical gear and a role weapon) plus a procedural animator: walk / run cycle, crouch, aim, recoil,
    /// and two-bone IK that keeps both hands on the weapon.
    /// </summary>
    public class HumanoidRig : MonoBehaviour
    {
        public Transform pelvis, spine, head, shoulderL, shoulderR, elbowL, elbowR, hipL, hipR, kneeL, kneeR;
        public Transform weaponRoot, muzzle;
        public Vector3 gripR = new Vector3(0f, -0.07f, 0.02f);
        public Vector3 gripL = new Vector3(0f, -0.05f, 0.38f);
        public readonly List<Renderer> bodyRenderers = new List<Renderer>();

        const float StandPelvisY = 0.95f;
        const float CrouchPelvisY = 0.36f;
        const float UpperArm = 0.3f, ForeArm = 0.3f;

        float phase;
        float crouchBlend;
        float aimBlend;
        float kick;
        float breath;

        public float CrouchBlend => crouchBlend;

        public void Kick() => kick = 1f;

        // ================================================================== animation

        float legYaw;

        /// <param name="moveAngle">Direction of travel relative to the facing direction, degrees (0 = forward, 90 = right, 180 = back).</param>
        /// <param name="aimPitch">Vertical aim angle in degrees (positive = looking down); tilts the weapon and head.</param>
        public void Tick(float dt, float speed, float crouchTarget, float aimTarget, float moveAngle = 0f, float aimPitch = 0f)
        {
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouchTarget, dt * 7f);
            aimBlend = Mathf.MoveTowards(aimBlend, aimTarget, dt * 5f);
            kick = Mathf.MoveTowards(kick, 0f, dt * 9f);
            breath += dt * 1.6f;

            // Legs run along the direction of travel while the upper body keeps facing forward:
            // sideways = strafe, backwards = walk the cycle in reverse.
            float targetLeg = 0f;
            float direction = 1f;
            if (speed > 0.3f)
            {
                targetLeg = moveAngle;
                if (Mathf.Abs(moveAngle) > 100f)
                {
                    targetLeg = moveAngle > 0f ? moveAngle - 180f : moveAngle + 180f;
                    direction = -1f;
                }
            }
            legYaw = Mathf.LerpAngle(legYaw, targetLeg, 1f - Mathf.Exp(-12f * dt));
            pelvis.localRotation = Quaternion.Euler(0f, legYaw, 0f);

            float move = Mathf.Clamp01(speed / 5f);
            phase += dt * speed * 1.9f * direction;
            float sin = Mathf.Sin(phase);
            float cos = Mathf.Cos(phase);
            float c = crouchBlend;
            float a = aimBlend;

            // Legs
            float swing = sin * 38f * move * (1f - c * 0.5f);
            float crouchHip = -75f * c;
            float crouchKnee = 142f * c;
            hipL.localRotation = Quaternion.Euler(-swing + crouchHip, 0f, 0f);
            hipR.localRotation = Quaternion.Euler(swing + crouchHip, 0f, 0f);
            kneeL.localRotation = Quaternion.Euler(crouchKnee + 55f * move * Mathf.Max(0f, cos), 0f, 0f);
            kneeR.localRotation = Quaternion.Euler(crouchKnee + 55f * move * Mathf.Max(0f, -cos), 0f, 0f);

            // Body height / lean
            float bob = 0.03f * move * (1f - Mathf.Abs(cos));
            float y = Mathf.Lerp(StandPelvisY, CrouchPelvisY, c) - bob + Mathf.Sin(breath) * 0.004f;
            pelvis.localPosition = new Vector3(0f, y, 0f);
            float lean = c * 40f + move * 6f + Mathf.Sin(breath) * 0.6f - kick * 3f;
            // spine cancels the pelvis yaw so the torso (and weapon) keep pointing where the player aims
            spine.localRotation = Quaternion.Euler(lean, sin * 3f * move - legYaw, 0f);
            head.localRotation = Quaternion.Euler(-lean * 0.65f + aimPitch * 0.45f, -sin * 2f * move, 0f);

            // Weapon: low-ready near the hip <-> shouldered at chest height (kept level whatever the torso does),
            // then tilted to match where the player is aiming up / down.
            float pitch = Mathf.Lerp(32f, 0f, a) - lean * a * 0.9f - kick * 4f + aimPitch * 0.85f * a;
            weaponRoot.localRotation = Quaternion.Euler(pitch, sin * 2f * move, 0f);
            Vector3 lowPos = new Vector3(0.12f, 0.2f, 0.26f);
            Vector3 highPos = new Vector3(0.1f, 0.4f, 0.3f);
            weaponRoot.localPosition = Vector3.Lerp(lowPos, highPos, a) + new Vector3(0f, Mathf.Abs(sin) * 0.012f * move, -kick * 0.06f);

            // Hands follow the weapon (two-bone IK)
            SolveArm(shoulderR, elbowR, weaponRoot.TransformPoint(gripR), false);
            SolveArm(shoulderL, elbowL, weaponRoot.TransformPoint(gripL), true);
        }

        /// <summary>Analytic two-bone IK in rig space; the elbow bends outwards and down.</summary>
        void SolveArm(Transform sh, Transform el, Vector3 targetWorld, bool left)
        {
            var rt = transform;
            Vector3 S = rt.InverseTransformPoint(sh.position);
            Vector3 T = rt.InverseTransformPoint(targetWorld);
            Vector3 d = T - S;
            if (d.sqrMagnitude < 1e-6f) return;

            float dist = Mathf.Clamp(d.magnitude, 0.1f, (UpperArm + ForeArm) * 0.985f);
            Vector3 dir = d.normalized;

            float a = (UpperArm * UpperArm - ForeArm * ForeArm + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, UpperArm * UpperArm - a * a));
            Vector3 pole = new Vector3(left ? -0.8f : 0.8f, -0.7f, -0.25f);
            Vector3 perp = Vector3.ProjectOnPlane(pole, dir);
            perp = perp.sqrMagnitude < 1e-4f ? Vector3.down : perp.normalized;

            Vector3 E = S + dir * a + perp * h;
            Vector3 hand = S + dir * dist;
            sh.rotation = rt.rotation * Quaternion.FromToRotation(Vector3.down, E - S);
            el.rotation = rt.rotation * Quaternion.FromToRotation(Vector3.down, hand - E);
        }

        // ================================================================== construction

        static GameObject P(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
                            Vector3? euler = null)
        {
            var go = Visuals.Primitive(type, name, parent, pos, scale, mat);
            if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
            return go;
        }

        static Transform Pivot(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        public static HumanoidRig Build(Transform parent, HumanoidStyle s)
        {
            var root = new GameObject("Rig");
            root.transform.SetParent(parent, false);
            root.transform.localScale = s.Scale;
            var rig = root.AddComponent<HumanoidRig>();

            var uniform = Visuals.Lit(s.Uniform, 0f, 0.12f);
            var vest = Visuals.Lit(s.Vest, 0f, 0.25f);
            var helmet = Visuals.Lit(s.Helmet, 0f, 0.4f);
            var skin = Visuals.Lit(s.Skin, 0f, 0.2f);
            var gear = Visuals.Lit(s.Gear, 0f, 0.3f);
            var accent = Visuals.Lit(s.Accent, 0f, 0.3f);
            var glow = Visuals.Lit(s.Accent, 1.1f, 0.5f);
            var white = Visuals.Lit(new Color(0.85f, 0.85f, 0.85f), 0f, 0.3f);
            var red = Visuals.Lit(new Color(0.8f, 0.1f, 0.1f), 0f, 0.3f);

            void Body(GameObject g) { rig.bodyRenderers.Add(g.GetComponent<Renderer>()); }

            // ---- pelvis & legs
            rig.pelvis = Pivot("Pelvis", root.transform, new Vector3(0f, StandPelvisY, 0f));
            Body(P(PrimitiveType.Sphere, "Hips", rig.pelvis, Vector3.zero, new Vector3(0.38f, 0.27f, 0.28f), uniform));
            P(PrimitiveType.Cube, "Belt", rig.pelvis, new Vector3(0f, 0.1f, 0f), new Vector3(0.38f, 0.07f, 0.27f), gear);
            P(PrimitiveType.Cube, "Holster", rig.pelvis, new Vector3(0.21f, -0.06f, 0.02f), new Vector3(0.07f, 0.17f, 0.12f), gear);
            P(PrimitiveType.Cube, "BeltBuckle", rig.pelvis, new Vector3(0f, 0.1f, 0.135f), new Vector3(0.07f, 0.05f, 0.015f),
              Visuals.Lit(new Color(0.5f, 0.5f, 0.45f), 0f, 0.6f, 0.8f));

            BuildLeg(rig, s, true, uniform, gear);
            BuildLeg(rig, s, false, uniform, gear);

            // ---- torso
            rig.spine = Pivot("Spine", rig.pelvis, new Vector3(0f, 0.12f, 0f));
            Body(P(PrimitiveType.Sphere, "Torso", rig.spine, new Vector3(0f, 0.29f, 0f), new Vector3(0.44f, 0.54f, 0.29f), uniform));
            Body(P(PrimitiveType.Sphere, "Trapezius", rig.spine, new Vector3(0f, 0.5f, -0.01f), new Vector3(0.5f, 0.16f, 0.28f), uniform));
            Body(P(PrimitiveType.Sphere, "Vest", rig.spine, new Vector3(0f, 0.26f, 0.0f), new Vector3(0.455f, 0.3f, 0.3f), vest));
            Body(P(PrimitiveType.Sphere, "ChestPlate", rig.spine, new Vector3(0f, 0.37f, 0.105f), new Vector3(0.31f, 0.24f, 0.1f), vest));
            Body(P(PrimitiveType.Sphere, "BackPlate", rig.spine, new Vector3(0f, 0.34f, -0.125f), new Vector3(0.32f, 0.3f, 0.1f), vest));
            for (int i = 0; i < 3; i++)
                P(PrimitiveType.Cube, "Pouch" + i, rig.spine, new Vector3(-0.11f + i * 0.11f, 0.12f, 0.17f), new Vector3(0.08f, 0.1f, 0.055f), gear);
            P(PrimitiveType.Cube, "Patch", rig.spine, new Vector3(-0.09f, 0.45f, 0.17f), new Vector3(0.09f, 0.045f, 0.01f), glow);
            P(PrimitiveType.Cylinder, "Neck", rig.spine, new Vector3(0f, 0.6f, 0f), new Vector3(0.11f, 0.05f, 0.11f), skin);

            if (s.Scarf) Body(P(PrimitiveType.Sphere, "Scarf", rig.spine, new Vector3(0f, 0.58f, 0.015f), new Vector3(0.22f, 0.1f, 0.22f), accent));

            if (s.Backpack)
            {
                float k = s.BigPack ? 1.35f : 1f;
                Body(P(PrimitiveType.Cube, "Pack", rig.spine, new Vector3(0f, 0.28f, -0.22f * k), new Vector3(0.3f * k, 0.36f * k, 0.15f * k), s.Cross ? white : gear));
                P(PrimitiveType.Cylinder, "Roll", rig.spine, new Vector3(0f, 0.07f, -0.22f * k), new Vector3(0.09f, 0.16f * k, 0.09f), uniform, new Vector3(0f, 0f, 90f));
                if (s.Cross)
                {
                    P(PrimitiveType.Cube, "CrossH", rig.spine, new Vector3(0f, 0.3f, -0.3f), new Vector3(0.18f, 0.05f, 0.012f), red);
                    P(PrimitiveType.Cube, "CrossV", rig.spine, new Vector3(0f, 0.3f, -0.3f), new Vector3(0.05f, 0.18f, 0.012f), red);
                }
            }
            if (s.Cape)
            {
                Body(P(PrimitiveType.Sphere, "Cape", rig.spine, new Vector3(0f, 0.28f, -0.18f), new Vector3(0.54f, 0.68f, 0.14f), uniform));
                Body(P(PrimitiveType.Sphere, "CapeTop", rig.spine, new Vector3(0f, 0.52f, -0.04f), new Vector3(0.56f, 0.15f, 0.32f), uniform));
            }

            // ---- head
            rig.head = Pivot("Head", rig.spine, new Vector3(0f, 0.62f, 0f));
            BuildHead(rig, s, skin, helmet, gear, accent, glow, white, red);

            // ---- weapon first (the arms IK onto it)
            rig.weaponRoot = Pivot("WeaponRoot", rig.spine, new Vector3(0.1f, 0.3f, 0.2f));
            BuildWeapon(rig, s.Weapon);

            // ---- arms
            BuildArm(rig, s, true, uniform, gear, vest, accent);
            BuildArm(rig, s, false, uniform, gear, vest, accent);

            rig.Tick(0.016f, 0f, 0f, 1f);
            return rig;
        }

        static void BuildLeg(HumanoidRig rig, HumanoidStyle s, bool left, Material uniform, Material gear)
        {
            float sx = left ? -1f : 1f;
            var hip = Pivot(left ? "HipL" : "HipR", rig.pelvis, new Vector3(0.11f * sx, -0.02f, 0f));
            var thigh = P(PrimitiveType.Capsule, "Thigh", hip, new Vector3(0f, -0.22f, 0f), new Vector3(0.2f, 0.235f, 0.2f), uniform);
            rig.bodyRenderers.Add(thigh.GetComponent<Renderer>());
            var knee = Pivot(left ? "KneeL" : "KneeR", hip, new Vector3(0f, -0.45f, 0f));
            P(PrimitiveType.Sphere, "KneeJoint", knee, Vector3.zero, Vector3.one * 0.165f, uniform);
            var shin = P(PrimitiveType.Capsule, "Shin", knee, new Vector3(0f, -0.21f, 0f), new Vector3(0.15f, 0.22f, 0.15f), uniform);
            rig.bodyRenderers.Add(shin.GetComponent<Renderer>());
            if (s.KneePads) P(PrimitiveType.Sphere, "KneePad", knee, new Vector3(0f, 0.0f, 0.085f), new Vector3(0.16f, 0.14f, 0.07f), gear);
            P(PrimitiveType.Cube, "Boot", knee, new Vector3(0f, -0.4f, 0.05f), new Vector3(0.155f, 0.15f, 0.31f), gear);
            P(PrimitiveType.Cube, "Sole", knee, new Vector3(0f, -0.47f, 0.055f), new Vector3(0.16f, 0.035f, 0.325f), Visuals.Lit(Color.black, 0f, 0.1f));

            if (left) { rig.hipL = hip; rig.kneeL = knee; } else { rig.hipR = hip; rig.kneeR = knee; }
        }

        static void BuildArm(HumanoidRig rig, HumanoidStyle s, bool left, Material uniform, Material gear, Material vest, Material accent)
        {
            float sx = left ? -1f : 1f;
            var sh = Pivot(left ? "ShoulderL" : "ShoulderR", rig.spine, new Vector3(0.27f * sx, 0.49f, 0f));
            rig.bodyRenderers.Add(P(PrimitiveType.Sphere, "Joint", sh, Vector3.zero, Vector3.one * 0.17f, uniform).GetComponent<Renderer>());
            rig.bodyRenderers.Add(P(PrimitiveType.Capsule, "UpperArm", sh, new Vector3(0f, -0.15f, 0f), new Vector3(0.125f, 0.165f, 0.125f), uniform).GetComponent<Renderer>());
            if (s.ShoulderPads)
            {
                P(PrimitiveType.Sphere, "Pad", sh, new Vector3(0.03f * sx, 0.05f, 0f), new Vector3(0.22f, 0.14f, 0.22f), vest);
                P(PrimitiveType.Cube, "PadStripe", sh, new Vector3(0.12f * sx, 0.06f, 0f), new Vector3(0.015f, 0.05f, 0.15f), accent);
            }
            var el = Pivot(left ? "ElbowL" : "ElbowR", sh, new Vector3(0f, -0.3f, 0f));
            P(PrimitiveType.Sphere, "ElbowJoint", el, Vector3.zero, Vector3.one * 0.125f, uniform);
            rig.bodyRenderers.Add(P(PrimitiveType.Capsule, "Forearm", el, new Vector3(0f, -0.14f, 0f), new Vector3(0.11f, 0.155f, 0.11f), uniform).GetComponent<Renderer>());
            P(PrimitiveType.Cube, "ElbowPad", el, new Vector3(0f, 0.01f, -0.055f), new Vector3(0.1f, 0.08f, 0.04f), gear);
            P(PrimitiveType.Sphere, "Glove", el, new Vector3(0f, -0.31f, 0f), new Vector3(0.115f, 0.11f, 0.125f), gear);

            if (left) { rig.shoulderL = sh; rig.elbowL = el; } else { rig.shoulderR = sh; rig.elbowR = el; }
        }

        static void BuildHead(HumanoidRig rig, HumanoidStyle s, Material skin, Material helmet, Material gear,
                              Material accent, Material glow, Material white, Material red)
        {
            var h = rig.head;
            var dark = Visuals.Lit(new Color(0.03f, 0.03f, 0.035f), 0f, 0.7f);

            P(PrimitiveType.Sphere, "Face", h, new Vector3(0f, 0.1f, 0.005f), new Vector3(0.21f, 0.25f, 0.23f), skin);
            P(PrimitiveType.Cube, "Nose", h, new Vector3(0f, 0.085f, 0.115f), new Vector3(0.028f, 0.045f, 0.035f), skin);
            P(PrimitiveType.Sphere, "EarL", h, new Vector3(-0.108f, 0.1f, 0f), new Vector3(0.03f, 0.07f, 0.05f), skin);
            P(PrimitiveType.Sphere, "EarR", h, new Vector3(0.108f, 0.1f, 0f), new Vector3(0.03f, 0.07f, 0.05f), skin);

            if (s.Balaclava)
                rig.bodyRenderers.Add(P(PrimitiveType.Sphere, "Mask", h, new Vector3(0f, 0.035f, 0.015f), new Vector3(0.215f, 0.17f, 0.235f), gear).GetComponent<Renderer>());

            switch (s.Head)
            {
                case HeadGear.Helmet:
                    rig.bodyRenderers.Add(P(PrimitiveType.Sphere, "Helmet", h, new Vector3(0f, 0.15f, -0.015f), new Vector3(0.285f, 0.235f, 0.31f), helmet).GetComponent<Renderer>());
                    P(PrimitiveType.Cube, "Rim", h, new Vector3(0f, 0.105f, 0.04f), new Vector3(0.275f, 0.03f, 0.27f), helmet);
                    P(PrimitiveType.Cube, "Stripe", h, new Vector3(0f, 0.265f, -0.01f), new Vector3(0.05f, 0.012f, 0.27f), accent);
                    P(PrimitiveType.Cube, "GoggleFrame", h, new Vector3(0f, 0.115f, 0.108f), new Vector3(0.2f, 0.06f, 0.04f), dark);
                    P(PrimitiveType.Cube, "GoggleLens", h, new Vector3(0f, 0.115f, 0.13f), new Vector3(0.17f, 0.035f, 0.01f), glow);
                    break;
                case HeadGear.Cap:
                    rig.bodyRenderers.Add(P(PrimitiveType.Sphere, "Cap", h, new Vector3(0f, 0.18f, -0.01f), new Vector3(0.245f, 0.14f, 0.27f), helmet).GetComponent<Renderer>());
                    P(PrimitiveType.Cube, "Peak", h, new Vector3(0f, 0.145f, 0.135f), new Vector3(0.2f, 0.015f, 0.1f), helmet);
                    P(PrimitiveType.Cube, "GoggleFrame", h, new Vector3(0f, 0.105f, 0.108f), new Vector3(0.2f, 0.05f, 0.04f), dark);
                    P(PrimitiveType.Cube, "GoggleLens", h, new Vector3(0f, 0.105f, 0.13f), new Vector3(0.17f, 0.03f, 0.01f), glow);
                    break;
                case HeadGear.Hood:
                    rig.bodyRenderers.Add(P(PrimitiveType.Sphere, "Hood", h, new Vector3(0f, 0.125f, -0.03f), new Vector3(0.29f, 0.31f, 0.32f), helmet).GetComponent<Renderer>());
                    P(PrimitiveType.Cube, "Shade", h, new Vector3(0f, 0.2f, 0.11f), new Vector3(0.21f, 0.03f, 0.1f), helmet);
                    P(PrimitiveType.Cube, "Lens", h, new Vector3(0.05f, 0.108f, 0.112f), new Vector3(0.07f, 0.05f, 0.04f), dark);
                    P(PrimitiveType.Cube, "LensGlow", h, new Vector3(0.05f, 0.108f, 0.133f), new Vector3(0.05f, 0.03f, 0.01f), glow);
                    break;
                case HeadGear.HeavyHelmet:
                    rig.bodyRenderers.Add(P(PrimitiveType.Sphere, "Helmet", h, new Vector3(0f, 0.14f, -0.01f), new Vector3(0.32f, 0.28f, 0.34f), helmet).GetComponent<Renderer>());
                    P(PrimitiveType.Cube, "Faceplate", h, new Vector3(0f, 0.065f, 0.125f), new Vector3(0.22f, 0.14f, 0.07f), gear);
                    P(PrimitiveType.Cube, "VisorSlit", h, new Vector3(0f, 0.115f, 0.165f), new Vector3(0.19f, 0.03f, 0.012f), glow);
                    P(PrimitiveType.Cube, "Crest", h, new Vector3(0f, 0.29f, -0.01f), new Vector3(0.045f, 0.035f, 0.24f), accent);
                    break;
                case HeadGear.MedicHelmet:
                    rig.bodyRenderers.Add(P(PrimitiveType.Sphere, "Helmet", h, new Vector3(0f, 0.15f, -0.015f), new Vector3(0.285f, 0.235f, 0.31f), white).GetComponent<Renderer>());
                    P(PrimitiveType.Cube, "Rim", h, new Vector3(0f, 0.105f, 0.04f), new Vector3(0.275f, 0.03f, 0.27f), white);
                    P(PrimitiveType.Cube, "CrossH", h, new Vector3(0f, 0.22f, 0.145f), new Vector3(0.07f, 0.02f, 0.012f), red);
                    P(PrimitiveType.Cube, "CrossV", h, new Vector3(0f, 0.22f, 0.145f), new Vector3(0.02f, 0.07f, 0.012f), red);
                    P(PrimitiveType.Cube, "GoggleFrame", h, new Vector3(0f, 0.115f, 0.108f), new Vector3(0.2f, 0.05f, 0.04f), dark);
                    P(PrimitiveType.Cube, "GoggleLens", h, new Vector3(0f, 0.115f, 0.13f), new Vector3(0.17f, 0.03f, 0.01f), glow);
                    break;
            }
        }

        static void BuildWeapon(HumanoidRig rig, WeaponKind kind)
        {
            var w = rig.weaponRoot;
            var metal = Visuals.Lit(new Color(0.13f, 0.13f, 0.14f), 0f, 0.55f, 0.6f);
            var poly = Visuals.Lit(new Color(0.07f, 0.07f, 0.075f), 0f, 0.25f);
            var lens = Visuals.Lit(new Color(0.3f, 0.7f, 1f), 1.2f, 0.8f);
            Vector3 zAxis = new Vector3(90f, 0f, 0f); // rotate cylinders so their axis runs along +Z

            float muzzleZ;
            switch (kind)
            {
                case WeaponKind.Smg:
                    P(PrimitiveType.Cube, "Body", w, new Vector3(0f, 0.02f, 0.1f), new Vector3(0.06f, 0.1f, 0.32f), poly);
                    P(PrimitiveType.Cube, "Stock", w, new Vector3(0f, 0f, -0.1f), new Vector3(0.04f, 0.07f, 0.15f), metal);
                    P(PrimitiveType.Cylinder, "Barrel", w, new Vector3(0f, 0.03f, 0.34f), new Vector3(0.03f, 0.1f, 0.03f), metal, zAxis);
                    P(PrimitiveType.Cube, "Mag", w, new Vector3(0f, -0.14f, 0.1f), new Vector3(0.04f, 0.2f, 0.06f), metal);
                    P(PrimitiveType.Cube, "Sight", w, new Vector3(0f, 0.085f, 0.12f), new Vector3(0.03f, 0.03f, 0.07f), metal);
                    rig.gripR = new Vector3(0f, -0.07f, 0.02f);
                    rig.gripL = new Vector3(0f, -0.04f, 0.26f);
                    muzzleZ = 0.46f;
                    break;
                case WeaponKind.Sniper:
                    P(PrimitiveType.Cube, "Body", w, new Vector3(0f, 0.02f, 0.18f), new Vector3(0.06f, 0.1f, 0.55f), poly);
                    P(PrimitiveType.Cube, "Stock", w, new Vector3(0f, -0.01f, -0.22f), new Vector3(0.055f, 0.13f, 0.28f), poly);
                    P(PrimitiveType.Cylinder, "Barrel", w, new Vector3(0f, 0.03f, 0.78f), new Vector3(0.03f, 0.3f, 0.03f), metal, zAxis);
                    P(PrimitiveType.Cylinder, "Scope", w, new Vector3(0f, 0.12f, 0.2f), new Vector3(0.05f, 0.13f, 0.05f), metal, zAxis);
                    P(PrimitiveType.Sphere, "Lens", w, new Vector3(0f, 0.12f, 0.335f), new Vector3(0.045f, 0.045f, 0.02f), lens);
                    P(PrimitiveType.Cube, "BipodL", w, new Vector3(-0.025f, -0.06f, 0.6f), new Vector3(0.012f, 0.14f, 0.012f), metal, new Vector3(0f, 0f, 14f));
                    P(PrimitiveType.Cube, "BipodR", w, new Vector3(0.025f, -0.06f, 0.6f), new Vector3(0.012f, 0.14f, 0.012f), metal, new Vector3(0f, 0f, -14f));
                    P(PrimitiveType.Cube, "Mag", w, new Vector3(0f, -0.1f, 0.12f), new Vector3(0.04f, 0.1f, 0.07f), metal);
                    rig.gripR = new Vector3(0f, -0.07f, 0.03f);
                    rig.gripL = new Vector3(0f, -0.05f, 0.42f);
                    muzzleZ = 1.1f;
                    break;
                case WeaponKind.Minigun:
                    P(PrimitiveType.Cube, "Body", w, new Vector3(0f, 0.02f, 0.12f), new Vector3(0.15f, 0.15f, 0.36f), metal);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 120f * Mathf.Deg2Rad;
                        P(PrimitiveType.Cylinder, "Barrel" + i, w, new Vector3(Mathf.Sin(a) * 0.04f, 0.02f + Mathf.Cos(a) * 0.04f, 0.5f),
                          new Vector3(0.028f, 0.28f, 0.028f), metal, zAxis);
                    }
                    P(PrimitiveType.Cylinder, "Shroud", w, new Vector3(0f, 0.02f, 0.4f), new Vector3(0.14f, 0.03f, 0.14f), poly, zAxis);
                    P(PrimitiveType.Cube, "AmmoBox", w, new Vector3(0.02f, -0.22f, -0.08f), new Vector3(0.22f, 0.2f, 0.17f), Visuals.Lit(new Color(0.25f, 0.3f, 0.2f), 0f, 0.2f));
                    P(PrimitiveType.Cube, "Handle", w, new Vector3(0f, 0.14f, 0.1f), new Vector3(0.03f, 0.05f, 0.3f), poly);
                    rig.gripR = new Vector3(0f, -0.1f, 0.02f);
                    rig.gripL = new Vector3(0f, 0.13f, 0.2f);
                    muzzleZ = 0.82f;
                    break;
                case WeaponKind.Pistol:
                    P(PrimitiveType.Cube, "Slide", w, new Vector3(0f, 0.03f, 0.1f), new Vector3(0.035f, 0.045f, 0.2f), metal);
                    P(PrimitiveType.Cube, "Grip", w, new Vector3(0f, -0.04f, 0.03f), new Vector3(0.035f, 0.1f, 0.05f), poly, new Vector3(-12f, 0f, 0f));
                    rig.gripR = new Vector3(0f, -0.06f, 0.03f);
                    rig.gripL = new Vector3(0.02f, -0.07f, 0.04f);
                    muzzleZ = 0.21f;
                    break;
                default: // Rifle
                    P(PrimitiveType.Cube, "Body", w, new Vector3(0f, 0.02f, 0.12f), new Vector3(0.06f, 0.1f, 0.42f), poly);
                    P(PrimitiveType.Cube, "Stock", w, new Vector3(0f, -0.005f, -0.17f), new Vector3(0.05f, 0.12f, 0.2f), poly);
                    P(PrimitiveType.Cube, "Handguard", w, new Vector3(0f, 0.02f, 0.37f), new Vector3(0.07f, 0.08f, 0.25f), metal);
                    P(PrimitiveType.Cylinder, "Barrel", w, new Vector3(0f, 0.03f, 0.58f), new Vector3(0.03f, 0.13f, 0.03f), metal, zAxis);
                    P(PrimitiveType.Cube, "Mag", w, new Vector3(0f, -0.12f, 0.1f), new Vector3(0.045f, 0.16f, 0.07f), metal, new Vector3(-10f, 0f, 0f));
                    P(PrimitiveType.Cube, "Sight", w, new Vector3(0f, 0.09f, 0.15f), new Vector3(0.03f, 0.04f, 0.08f), metal);
                    P(PrimitiveType.Cube, "Optic", w, new Vector3(0f, 0.1f, 0.28f), new Vector3(0.04f, 0.045f, 0.1f), poly);
                    rig.gripR = new Vector3(0f, -0.07f, 0.02f);
                    rig.gripL = new Vector3(0f, -0.05f, 0.38f);
                    muzzleZ = 0.72f;
                    break;
            }

            rig.muzzle = Pivot("Muzzle", w, new Vector3(0f, 0.03f, muzzleZ));
        }
    }
}
