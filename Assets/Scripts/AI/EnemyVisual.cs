using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    /// <summary>Wraps the humanoid rig for an enemy: locomotion animation, crouch collider, hit flash, death fall.</summary>
    public class EnemyVisual : MonoBehaviour
    {
        public CharacterRig Rig { get; private set; }

        CapsuleCollider body;
        NavMeshAgent agent;

        float fullHeight = 1.8f;
        bool crouched;
        bool aiming = true;
        float flashUntil;
        bool dying;
        float deathT;
        bool animatedDeath;
        Quaternion deathTarget;
        float aimHold;
        bool wasFlashing;

        public const float CrouchHeightFactor = 0.55f;

        public void Build(RoleProfile p, CapsuleCollider collider)
        {
            body = collider;
            agent = GetComponent<NavMeshAgent>();

            var style = CharacterStyles.ForRole(p);
            fullHeight = 1.85f * style.Scale.y;
            Rig = CharacterRig.Create(transform, style, CharacterCatalog.ForRole(p, fullHeight));

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
            if (body != null) body.enabled = false;
            Rig.SetFlash(false);
            if (Rig.PlayDeath()) { animatedDeath = true; return; }       // the model plays its own death animation
            pushDirection.y = 0f;
            if (pushDirection.sqrMagnitude < 0.01f) pushDirection = -transform.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, pushDirection.normalized);
            deathTarget = Quaternion.AngleAxis(88f, axis) * transform.rotation;
        }

        void Update()
        {
            if (Rig == null) return;

            if (dying && animatedDeath)
            {
                Rig.Tick(Time.deltaTime, 0f, 0f, 0f);
                return;
            }

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
            // direction of travel relative to where the body faces: lets the rig play strafe / backwards clips
            float moveAngle = 0f;
            if (speed > 0.3f && agent != null && agent.enabled && agent.isOnNavMesh)
                moveAngle = Vector3.SignedAngle(transform.forward, agent.velocity, Vector3.up);
            Rig.Tick(Time.deltaTime, speed, crouched ? 1f : 0f, aiming ? 1f : 0.35f, moveAngle);

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
                Rig.SetFlash(flash);
            }
        }
    }
}
