using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    /// <summary>Wraps the humanoid rig for an enemy: locomotion animation, crouch collider, hit flash, death fall.</summary>
    public class EnemyVisual : MonoBehaviour
    {
        public HumanoidRig Rig { get; private set; }

        CapsuleCollider body;
        NavMeshAgent agent;
        MaterialPropertyBlock block;

        float fullHeight = 1.8f;
        bool crouched;
        bool aiming = true;
        float flashUntil;
        bool dying;
        float deathT;
        Quaternion deathTarget;
        float aimHold;
        bool wasFlashing;

        public const float CrouchHeightFactor = 0.55f;

        public void Build(RoleProfile p, CapsuleCollider collider)
        {
            body = collider;
            agent = GetComponent<NavMeshAgent>();

            var style = CharacterStyles.ForRole(p);
            Rig = HumanoidRig.Build(transform, style);
            fullHeight = 1.85f * style.Scale.y;

            if (body != null)
            {
                body.height = fullHeight;
                body.center = new Vector3(0f, fullHeight * 0.5f, 0f);
                body.radius = Mathf.Max(0.35f, p.Radius * 0.9f);
            }
        }

        public void SetCrouched(bool value) => crouched = value;

        /// <summary>Raises the weapon (shouldered pose) for a moment; the brain calls this while fighting.</summary>
        public void SetAiming(bool value)
        {
            if (value) aimHold = Time.time + 0.6f;
        }

        public void Flash() => flashUntil = Time.time + 0.08f;
        public void Recoil() => Rig?.Kick();

        public void PlayDeath(Vector3 pushDirection)
        {
            dying = true;
            pushDirection.y = 0f;
            if (pushDirection.sqrMagnitude < 0.01f) pushDirection = -transform.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, pushDirection.normalized);
            deathTarget = Quaternion.AngleAxis(88f, axis) * transform.rotation;
            if (body != null) body.enabled = false;
            foreach (var r in Rig.bodyRenderers) if (r != null) r.SetPropertyBlock(null);
        }

        void Update()
        {
            if (Rig == null) return;

            if (dying)
            {
                deathT += Time.deltaTime * 2.2f;
                transform.rotation = Quaternion.Slerp(transform.rotation, deathTarget, Mathf.Clamp01(deathT));
                // Limbs go slack.
                Rig.Tick(Time.deltaTime, 0f, 0f, 0f);
                return;
            }

            float speed = agent != null && agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0f;
            aiming = Time.time < aimHold;
            Rig.Tick(Time.deltaTime, speed, crouched ? 1f : 0f, aiming ? 1f : 0.35f);

            if (body != null)
            {
                float f = Mathf.Lerp(1f, CrouchHeightFactor, Rig.CrouchBlend);
                body.height = fullHeight * f;
                body.center = new Vector3(0f, body.height * 0.5f, 0f);
            }

            bool flash = Time.time < flashUntil;
            if (flash != wasFlashing)
            {
                wasFlashing = flash;
                if (flash)
                {
                    if (block == null) block = new MaterialPropertyBlock();
                    block.SetColor(Visuals.BaseColorId, Color.white);
                }
                foreach (var r in Rig.bodyRenderers)
                    if (r != null) r.SetPropertyBlock(flash ? block : null);
            }
        }
    }
}
