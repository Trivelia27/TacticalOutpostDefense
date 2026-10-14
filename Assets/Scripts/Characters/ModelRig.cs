using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TacticalOutpost
{
    /// <summary>
    /// Drives an imported animated character (generic rig, clips embedded in the model).
    ///  - Locomotion (idle / walk / run / strafe / backpedal) on the base layer. The run-family clips share ONE phase and are played at
    ///    the exact speed that makes the planted foot move at the character's real ground speed, so feet do not slide.
    ///  - Weapon pose and recoil on an upper-body layer, so the legs can run in any direction while the rifle stays up.
    ///  - Death on a full-body layer.
    ///  - Crouch as a procedural pose on top of the animation, and a pelvis "ground lock" that keeps the lowest foot on the ground
    ///    (no floating or sinking whatever the clip, scale or crouch depth).
    ///  - Support hand IK onto the weapon's foregrip.
    /// </summary>
    public sealed class ModelRig : CharacterRig
    {
        // Ground speed (m/s, at model scale 1) of the planted foot in the source clips, measured from the clip data.
        public const float RunGroundSpeed = 3.2f;
        public const float WalkGroundSpeed = 1.23f;

        sealed class Slot
        {
            public AnimationClipPlayable Playable;
            public AnimationClip Clip;
            public float Length => Clip != null ? Clip.length : 1f;
            public void SetNormalized(float t01) { if (Clip != null) Playable.SetTime(Mathf.Repeat(t01, 1f) * Clip.length); }
        }

        ModelSpec spec;
        Animator animator;
        PlayableGraph graph;
        AnimationLayerMixerPlayable layers;
        AnimationMixerPlayable loco, upperPose;
        readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>();
        readonly Dictionary<string, int> locoIndex = new Dictionary<string, int>();
        Slot shoot, death;
        int shootLayer = -1, deathLayer = -1;

        Transform body, chest, upperLegL, upperLegR, lowerLegL, lowerLegR, footEndL, footEndR;
        Transform upperArmL, lowerArmL, wristL, wristR, abdomen;
        Transform footBoneL, footBoneR;               // skin joints that the shoes are weighted to; they hang off the root, not off the legs
        Vector3 footPosL, footPosR;
        Quaternion footRotL, footRotR;
        Transform weaponT;
        Vector3 weaponGripLocal, weaponForeLocal;
        float weaponMuzzleSign = 1f;
        float modelScale = 1f;
        float restAnkleHeight;                       // height of the ankle above the ground in the standing pose

        float crouchBlend, aimBlend, aimPitch, shootT, deathBlend, deathT;
        bool dead;
        float runPhase, walkPhase, idlePhase;
        float locoSpeed, locoAngle, legYaw, legYawTarget, walkDir = 1f;
        int sector;                                   // 0 = forward, 1 = side, 2 = backwards (with hysteresis)
        float[] locoWeights, locoTarget;
        MaterialPropertyBlock flashBlock;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public override float CrouchBlend => crouchBlend;
        public override void Kick() => shootT = 0.28f;

        /// <summary>Measured-from-the-model numbers used by the tests: how fast the model's planted foot should move.</summary>
        public float StanceFootSpeedAtRun => RunGroundSpeed * modelScale * spec.StrideScale;
        public float StandingAnkleHeight => restAnkleHeight;
        public Transform LeftFoot => footEndL;
        public Transform RightFoot => footEndR;

        // ================================================================== construction

        public static ModelRig Build(Transform parent, ModelSpec spec)
        {
            var prefab = Resources.Load<GameObject>(spec.Character);
            if (prefab == null) return null;
            var clips = Resources.LoadAll<AnimationClip>(spec.Character);
            if (clips == null || clips.Length == 0) return null;

            var root = new GameObject("ModelRig");
            root.transform.SetParent(parent, false);
            var rig = root.AddComponent<ModelRig>();
            rig.spec = spec;
            rig.Setup(root.transform, prefab, clips);
            return rig;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        void Setup(Transform rigRoot, GameObject prefab, AnimationClip[] clipList)
        {
            var model = Instantiate(prefab, rigRoot);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // ---- size: scale the model so it stands spec.Height tall, feet on the rig origin ------------------
            var b = RenderBounds(model);
            modelScale = spec.Height / Mathf.Max(0.5f, b.size.y);
            model.transform.localScale = Vector3.one * modelScale;
            model.transform.localPosition = new Vector3(0f, -(RenderBounds(model).min.y - rigRoot.position.y), 0f);

            // ---- bones ---------------------------------------------------------------------------
            body = Find(model.transform, "Body");
            abdomen = Find(model.transform, "Abdomen");
            chest = Find(model.transform, "Chest");
            upperLegL = Find(model.transform, "UpperLeg.L");
            upperLegR = Find(model.transform, "UpperLeg.R");
            lowerLegL = Find(model.transform, "LowerLeg.L");
            lowerLegR = Find(model.transform, "LowerLeg.R");
            footEndL = Find(model.transform, "LowerLeg.L_end") ?? lowerLegL;
            footEndR = Find(model.transform, "LowerLeg.R_end") ?? lowerLegR;
            footBoneL = Find(model.transform, "Foot.L");
            footBoneR = Find(model.transform, "Foot.R");
            upperArmL = Find(model.transform, "UpperArm.L");
            lowerArmL = Find(model.transform, "LowerArm.L");
            wristL = Find(model.transform, "Wrist.L");
            wristR = Find(model.transform, "Wrist.R");

            // ---- weapon -------------------------------------------------------------------------------
            if (!string.IsNullOrEmpty(spec.Weapon) && wristR != null)
            {
                var wp = Resources.Load<GameObject>(spec.Weapon);
                if (wp != null)
                {
                    var w = Instantiate(wp);                        // measured before it is parented, so no bone scale leaks in
                    w.name = "Weapon";
                    var wb = RenderBounds(w);
                    float rawLen = Mathf.Max(0.1f, wb.size.x);
                    weaponMuzzleSign = MuzzleSign(w, wb);
                    float rearX = weaponMuzzleSign > 0f ? wb.min.x : wb.max.x;
                    weaponGripLocal = new Vector3(rearX + weaponMuzzleSign * wb.size.x * spec.GripFraction, wb.center.y - wb.size.y * 0.12f, wb.center.z);
                    weaponForeLocal = new Vector3(rearX + weaponMuzzleSign * wb.size.x * spec.ForeFraction, wb.center.y - wb.size.y * 0.05f, wb.center.z);
                    w.transform.SetParent(wristR, false);
                    w.transform.localScale = Vector3.one * (spec.WeaponLength / rawLen / Mathf.Max(0.0001f, wristR.lossyScale.x));
                    weaponT = w.transform;
                }
            }

            ModelMaterials.Apply(model);
            if (spec.Tint != Color.white) TintUniform(model);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                bodyRenderers.Add(r);
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;      // bounds follow the crouch / death pose
            }

            BuildAnimation(model, clipList);
        }

        static Bounds RenderBounds(GameObject go)
        {
            var b = new Bounds();
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>Colours the clothing of this one character (skin, hair and black gear keep their colour).</summary>
        void TintUniform(GameObject model)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (weaponT != null && r.transform.IsChildOf(weaponT)) continue;
                var mats = r.sharedMaterials;
                var copy = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                    bool keep = n.Contains("skin") || n.Contains("hair") || n.Contains("black") || n.Contains("eye") || n.Contains("shoe") || n.Contains("glass");
                    if (keep || mats[i] == null) { copy[i] = mats[i]; continue; }
                    copy[i] = new Material(mats[i]);
                    copy[i].SetColor("_BaseColor", mats[i].GetColor("_BaseColor") * spec.Tint);
                }
                r.sharedMaterials = copy;
            }
        }

        // ------------------------------------------------------------------ weapon alignment

        /// <summary>The muzzle is the slimmer end of the weapon: compare the height of the mesh at both ends.</summary>
        static float MuzzleSign(GameObject weapon, Bounds b)
        {
            float loMin = float.MaxValue, loMax = float.MinValue, hiMin = float.MaxValue, hiMax = float.MinValue;
            float len = b.size.x;
            foreach (var mf in weapon.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 p = m.MultiplyPoint3x4(v);
                    if (p.x < b.min.x + len * 0.2f) { loMin = Mathf.Min(loMin, p.y); loMax = Mathf.Max(loMax, p.y); }
                    if (p.x > b.max.x - len * 0.2f) { hiMin = Mathf.Min(hiMin, p.y); hiMax = Mathf.Max(hiMax, p.y); }
                }
            }
            return (loMax - loMin) < (hiMax - hiMin) ? -1f : 1f;      // slimmer end at -X => the muzzle points to -X
        }

        /// <summary>Poses the model aimed once: orients the weapon so the muzzle points where the character faces and measures the standing ankle height.</summary>
        void AlignPoseOnce()
        {
            upperPose.SetInputWeight(0, 0f);
            upperPose.SetInputWeight(1, 1f);
            if (locoIndex.TryGetValue("idle", out int idle)) loco.SetInputWeight(idle, 1f);
            slots["gunAim"].Playable.SetTime(0f);
            graph.Evaluate(0f);

            if (weaponT != null && wristR != null)
            {
                Vector3 fwd = transform.forward, up = transform.up;
                Quaternion rot = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(0f, weaponMuzzleSign > 0f ? -90f : 90f, 0f);
                float k = weaponT.lossyScale.x;
                weaponT.SetPositionAndRotation(wristR.position - rot * (weaponGripLocal * k) + transform.TransformVector(spec.WeaponOffset), rot);
            }
            if (footEndL != null && footEndR != null)
                restAnkleHeight = Mathf.Min(footEndL.position.y, footEndR.position.y) - transform.position.y;
        }

        // ------------------------------------------------------------------ animation graph

        static AnimationClip FindClip(AnimationClip[] list, params string[] names)
        {
            foreach (var n in names)
                foreach (var c in list)
                {
                    string id = c.name;
                    int bar = id.LastIndexOf('|');
                    if (bar >= 0) id = id.Substring(bar + 1);
                    if (id == n) return c;
                }
            return null;
        }

        Slot MakeSlot(AnimationClip[] list, string key, params string[] names)
        {
            var clip = FindClip(list, names);
            if (clip == null) return null;
            var s = new Slot { Clip = clip, Playable = AnimationClipPlayable.Create(graph, clip) };
            s.Playable.SetSpeed(0f);                       // time is set by hand every frame
            s.Playable.SetApplyFootIK(false);
            slots[key] = s;
            return s;
        }

        AvatarMask UpperBodyMask(Transform model)
        {
            var all = model.GetComponentsInChildren<Transform>(true);
            var mask = new AvatarMask { transformCount = all.Length };
            for (int i = 0; i < all.Length; i++)
            {
                mask.SetTransformPath(i, PathOf(all[i], model));
                mask.SetTransformActive(i, abdomen != null && (all[i] == abdomen || all[i].IsChildOf(abdomen)));
            }
            return mask;
        }

        static string PathOf(Transform t, Transform root)
        {
            if (t == root) return "";
            string p = t.name;
            for (var c = t.parent; c != null && c != root; c = c.parent) p = c.name + "/" + p;
            return p;
        }

        void BuildAnimation(GameObject model, AnimationClip[] list)
        {
            animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;

            graph = PlayableGraph.Create("ModelRig_" + spec.Character);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);

            var keys = new List<(string key, Slot slot)>
            {
                ("idle", MakeSlot(list, "idle", "Idle")),
                ("walk", MakeSlot(list, "walk", "Walk", "Run")),
                ("run", MakeSlot(list, "run", "Run")),
                ("back", MakeSlot(list, "back", "Run_Back", "Run")),
                ("left", MakeSlot(list, "left", "Run_Left", "Run")),
                ("right", MakeSlot(list, "right", "Run_Right", "Run")),
            };
            var present = keys.FindAll(c => c.slot != null);
            loco = AnimationMixerPlayable.Create(graph, present.Count);
            for (int i = 0; i < present.Count; i++)
            {
                graph.Connect(present[i].slot.Playable, 0, loco, i);
                locoIndex[present[i].key] = i;
            }
            locoWeights = new float[present.Count];
            locoTarget = new float[present.Count];
            if (locoIndex.TryGetValue("idle", out int idle0)) locoWeights[idle0] = 1f;

            // layers: 0 base locomotion, 1 upper-body weapon pose, 2 recoil, 3 death
            layers = AnimationLayerMixerPlayable.Create(graph, 4);
            int li = 0;
            graph.Connect(loco, 0, layers, li);
            layers.SetInputWeight(li, 1f);
            li++;

            var low = MakeSlot(list, "gunLow", "Idle_Gun", "Idle");
            var aim = MakeSlot(list, "gunAim", "Idle_Gun_Pointing", "Idle_Gun", "Idle");
            upperPose = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(low.Playable, 0, upperPose, 0);
            graph.Connect(aim.Playable, 0, upperPose, 1);
            var mask = UpperBodyMask(model.transform);
            graph.Connect(upperPose, 0, layers, li);
            layers.SetInputWeight(li, 1f);
            layers.SetLayerMaskFromAvatarMask((uint)li, mask);
            li++;

            shoot = MakeSlot(list, "shoot", "Idle_Gun_Shoot", "Gun_Shoot");
            if (shoot != null)
            {
                graph.Connect(shoot.Playable, 0, layers, li);
                layers.SetInputWeight(li, 0f);
                layers.SetLayerMaskFromAvatarMask((uint)li, mask);
                shootLayer = li;
            }
            li++;

            death = MakeSlot(list, "death", "Death");
            if (death != null)
            {
                graph.Connect(death.Playable, 0, layers, li);
                layers.SetInputWeight(li, 0f);
                deathLayer = li;
            }

            output.SetSourcePlayable(layers);
            graph.Play();
            AlignPoseOnce();
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        // ================================================================== per frame

        public override bool PlayDeath()
        {
            if (death == null) return false;
            dead = true;
            death.Playable.SetTime(0f);
            deathT = 0f;
            return true;
        }

        public override void Tick(float dt, float speed, float crouchTarget, float aimTarget, float moveAngle = 0f, float pitch = 0f)
        {
            if (!graph.IsValid()) return;
            crouchBlend = Mathf.MoveTowards(crouchBlend, dead ? 0f : crouchTarget, dt * 6f);
            aimBlend = Mathf.MoveTowards(aimBlend, aimTarget, dt * 5f);
            aimPitch = pitch;
            if (shootT > 0f) shootT -= dt;

            locoSpeed = Mathf.Lerp(locoSpeed, speed, 1f - Mathf.Exp(-14f * dt));
            locoAngle = Mathf.MoveTowardsAngle(locoAngle, moveAngle, 540f * dt);      // swing round to the new direction quickly but not instantly

            if (dead)
            {
                deathBlend = Mathf.MoveTowards(deathBlend, 1f, dt * 8f);
                layers.SetInputWeight(deathLayer, deathBlend);
                deathT = Mathf.Min(deathT + dt, death.Length);
                death.Playable.SetTime(deathT);
                return;
            }

            UpdateLocomotion(dt);

            upperPose.SetInputWeight(0, 1f - Mathf.Clamp01(aimBlend));
            upperPose.SetInputWeight(1, Mathf.Clamp01(aimBlend));
            slots["gunLow"].SetNormalized(Time.time * 0.4f);
            slots["gunAim"].SetNormalized(Time.time * 0.4f);
            if (shoot != null)
            {
                layers.SetInputWeight(shootLayer, shootT > 0f ? Mathf.Clamp01(shootT / 0.12f) : 0f);
                shoot.Playable.SetTime(Mathf.Clamp(0.28f - shootT, 0f, shoot.Length));
            }
        }

        void UpdateLocomotion(float dt)
        {
            for (int i = 0; i < locoTarget.Length; i++) locoTarget[i] = 0f;

            float move = Mathf.Clamp01((locoSpeed - 0.15f) / 0.6f);
            float a = Mathf.DeltaAngle(0f, locoAngle);                     // -180..180, 0 = forward, 90 = right
            float abs = Mathf.Abs(a), sign = a >= 0f ? 1f : -1f;
            bool forwardish = abs < 35f;
            bool useWalk = locoSpeed < 2.1f && forwardish;

            // One clip per direction sector (never a mix of clips that move the feet along different axes: that crosses the legs on
            // the diagonals). Forward run for the front sector, a strafe clip for the sides, the backpedal clip behind. The legs are then
            // turned about the vertical so the planted foot travels exactly along the movement direction while the torso keeps facing the
            // aim. The sector has hysteresis, and the change itself is a short cross-fade in time (see the weight smoothing below).
            if (sector == 0 && abs > 60f) sector = 1;
            else if (sector == 1) { if (abs < 50f) sector = 0; else if (abs > 130f) sector = 2; }
            else if (sector == 2 && abs < 120f) sector = 1;
            float sF = sector == 0 ? 1f : 0f, sS = sector == 1 ? 1f : 0f, sB = sector == 2 ? 1f : 0f;
            float yawF = a;
            float yawS = Mathf.DeltaAngle(0f, a - sign * 90f);
            float yawB = Mathf.DeltaAngle(0f, a - sign * 180f);
            float runYaw = sector == 0 ? yawF : sector == 1 ? yawS : yawB;

            // crouched: the walk cycle in every direction (backwards = the cycle played in reverse)
            float cr = Mathf.Clamp01(crouchBlend * 1.5f);
            bool back = abs > 100f;
            float walkYaw = back ? Mathf.DeltaAngle(0f, a - sign * 180f) : a;
            walkDir = Mathf.MoveTowards(walkDir, cr > 0.1f && back ? -1f : 1f, dt * 8f);

            Set("idle", 1f - move);
            if (useWalk) Set("walk", move);
            else
            {
                Set("walk", move * cr);
                float free = 1f - cr;
                Set("run", move * sF * free);
                Set(sign > 0f ? "right" : "left", move * sS * free);
                Set("back", move * sB * free);
            }
            legYawTarget = Mathf.Lerp(useWalk ? a : runYaw, walkYaw, cr) * move;

            // smooth the weights (a short cross-fade, never a pop) and push them to the mixer
            float total = 0f;
            for (int i = 0; i < locoWeights.Length; i++)
            {
                locoWeights[i] = Mathf.MoveTowards(locoWeights[i], locoTarget[i], dt * 7f);
                total += locoWeights[i];
            }
            if (total < 0.0001f) { locoWeights[0] = 1f; total = 1f; }
            for (int i = 0; i < locoWeights.Length; i++) loco.SetInputWeight(i, locoWeights[i] / total);

            // Phase: the run family shares ONE phase advancing so that the planted foot travels at the real ground speed.
            float runLen = slots["run"].Length, walkLen = slots["walk"].Length;
            float stride = modelScale * spec.StrideScale;
            float runCycles = locoSpeed / (RunGroundSpeed * stride) / runLen;          // cycles per second
            float walkCycles = locoSpeed / (WalkGroundSpeed * stride) / walkLen;
            runPhase = Mathf.Repeat(runPhase + dt * Mathf.Min(runCycles, 2.6f / runLen), 1f);
            walkPhase = Mathf.Repeat(walkPhase + dt * walkDir * Mathf.Min(walkCycles, 2.4f / walkLen), 1f);
            idlePhase = Mathf.Repeat(idlePhase + dt / Mathf.Max(0.5f, slots["idle"].Length), 1f);

            foreach (var key in new[] { "run", "right", "back", "left" })
                if (slots.TryGetValue(key, out var s)) s.SetNormalized(runPhase);
            slots["walk"].SetNormalized(walkPhase);
            slots["idle"].SetNormalized(idlePhase);
        }

        static float Smooth(float x, float a, float b) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        void Set(string key, float w)
        {
            if (locoIndex.TryGetValue(key, out int i)) locoTarget[i] += w;
        }

        // ================================================================== crouch / aim / ground lock after the animation

        void LateUpdate()
        {
            if (dead) return;
            Vector3 right = transform.right;

            // The foot bones are skin joints but not children of the legs: remember where the animation put them relative to the shins,
            // so they can be put back on the shins after the pose below has turned / bent the legs (otherwise the shoes get stretched).
            CaptureFoot(footBoneL, lowerLegL, out footPosL, out footRotL);
            CaptureFoot(footBoneR, lowerLegR, out footPosR, out footRotR);

            float c = crouchBlend;
            if (c > 0.001f && body != null)
            {
                body.position += Vector3.down * (spec.CrouchDrop * modelScale * c);
                if (upperLegL != null) upperLegL.rotation = Quaternion.AngleAxis(-spec.CrouchThigh * c, right) * upperLegL.rotation;
                if (upperLegR != null) upperLegR.rotation = Quaternion.AngleAxis(-spec.CrouchThigh * c, right) * upperLegR.rotation;
                if (lowerLegL != null) lowerLegL.rotation = Quaternion.AngleAxis(spec.CrouchShin * c, right) * lowerLegL.rotation;
                if (lowerLegR != null) lowerLegR.rotation = Quaternion.AngleAxis(spec.CrouchShin * c, right) * lowerLegR.rotation;
                if (chest != null) chest.rotation = Quaternion.AngleAxis(spec.CrouchLean * c, right) * chest.rotation;
            }

            // Turn the legs towards the movement direction (the pelvis and torso keep facing the aim).
            legYaw = Mathf.MoveTowardsAngle(legYaw, legYawTarget, 720f * Time.deltaTime);
            if (Mathf.Abs(legYaw) > 0.5f)
            {
                var yaw = Quaternion.AngleAxis(legYaw, Vector3.up);
                if (upperLegL != null) upperLegL.rotation = yaw * upperLegL.rotation;
                if (upperLegR != null) upperLegR.rotation = yaw * upperLegR.rotation;
            }

            // Ground lock: the lowest foot rests on the ground. Sinking is corrected fully, floating only partly (keeps a little run bounce).
            if (body != null && footEndL != null && footEndR != null)
            {
                float lowest = Mathf.Min(footEndL.position.y, footEndR.position.y) - transform.position.y - restAnkleHeight;
                float fix = lowest < 0f ? -lowest : -lowest * 0.7f;
                body.position += Vector3.up * Mathf.Clamp(fix, -0.4f, 0.4f);
            }

            ApplyFoot(footBoneL, lowerLegL, footPosL, footRotL);
            ApplyFoot(footBoneR, lowerLegR, footPosR, footRotR);

            if (chest != null && Mathf.Abs(aimPitch) > 0.1f)
                chest.rotation = Quaternion.AngleAxis(Mathf.Clamp(aimPitch, -50f, 50f) * 0.6f, right) * chest.rotation;    // look up / down with the weapon

            // Support hand on the foregrip.
            if (weaponT != null && upperArmL != null && lowerArmL != null && wristL != null && aimBlend > 0.05f)
            {
                Vector3 target = weaponT.TransformPoint(weaponForeLocal);
                Vector3 pole = -transform.up * 0.7f - transform.right * 0.6f - transform.forward * 0.1f;
                SolveArm(upperArmL, lowerArmL, wristL, Vector3.Lerp(wristL.position, target, Mathf.Clamp01(aimBlend)), pole);
            }
        }

        static void CaptureFoot(Transform foot, Transform shin, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (foot == null || shin == null) return;
            pos = shin.InverseTransformPoint(foot.position);
            rot = Quaternion.Inverse(shin.rotation) * foot.rotation;
        }

        static void ApplyFoot(Transform foot, Transform shin, Vector3 pos, Quaternion rot)
        {
            if (foot == null || shin == null) return;
            foot.position = shin.TransformPoint(pos);
            foot.rotation = shin.rotation * rot;
        }

        /// <summary>Two-bone arm IK: puts the support hand on the weapon's foregrip.</summary>
        static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            Vector3 S = upper.position, E = lower.position, H = hand.position;
            float a = (E - S).magnitude, b = (H - E).magnitude;
            Vector3 toT = target - S;
            float d = Mathf.Clamp(toT.magnitude, 0.05f, (a + b) * 0.995f);
            Vector3 dir = toT.normalized;
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float ang = Mathf.Acos(cosA);
            Vector3 perp = Vector3.ProjectOnPlane(pole, dir);
            perp = perp.sqrMagnitude < 1e-4f ? Vector3.down : perp.normalized;
            Vector3 E2 = S + (dir * Mathf.Cos(ang) + perp * Mathf.Sin(ang)) * a;
            upper.rotation = Quaternion.FromToRotation(E - S, E2 - S) * upper.rotation;
            E = lower.position; H = hand.position;
            lower.rotation = Quaternion.FromToRotation(H - E, S + dir * d - E) * lower.rotation;
        }

        // ================================================================== hit flash

        public override void SetFlash(bool on)
        {
            if (on)
            {
                if (flashBlock == null) flashBlock = new MaterialPropertyBlock();
                flashBlock.SetColor(EmissionId, new Color(0.8f, 0.8f, 0.8f));
            }
            foreach (var r in bodyRenderers)
                if (r != null) r.SetPropertyBlock(on ? flashBlock : null);
        }
    }
}
