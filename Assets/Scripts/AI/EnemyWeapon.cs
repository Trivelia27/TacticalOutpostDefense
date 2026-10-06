using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Role-driven hitscan weapon: bursts for rifles/SMGs/miniguns and a telegraphed
    /// (laser-sight) charge for snipers.
    /// </summary>
    public class EnemyWeapon : MonoBehaviour
    {
        EnemyBrain brain;
        RoleProfile p;
        float nextShot;
        int burstLeft;
        float chargeStart = -1f;
        float lastRequest;
        LineRenderer laser;

        public bool IsCharging => chargeStart >= 0f;

        public void Init(EnemyBrain owner)
        {
            brain = owner;
            p = owner.Profile;
            burstLeft = p.Burst;
            nextShot = Time.time + Random.Range(0.2f, 0.8f);

            if (p.ChargeTime > 0f)
            {
                var go = new GameObject("Laser");
                go.transform.SetParent(transform, false);
                laser = go.AddComponent<LineRenderer>();
                laser.positionCount = 2;
                laser.useWorldSpace = true;
                laser.startWidth = 0.035f;
                laser.endWidth = 0.02f;
                laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                laser.receiveShadows = false;
                laser.sharedMaterial = Visuals.Unlit(Color.white);
                laser.startColor = laser.endColor = new Color(1f, 0.15f, 0.15f, 0.9f);
                laser.enabled = false;
            }
        }

        void Update()
        {
            // Aim request lapsed (lost sight / changed action): drop the charge.
            if (IsCharging && Time.time - lastRequest > 0.25f) CancelCharge();
        }

        public void CancelCharge()
        {
            chargeStart = -1f;
            if (laser != null) laser.enabled = false;
        }

        /// <summary>Attempts to shoot. Returns true if a bullet was actually fired this call.</summary>
        public bool TryShoot(Targetable t, float spreadMultiplier = 1f)
        {
            if (t == null || !t.IsValid) { CancelCharge(); return false; }
            lastRequest = Time.time;
            if (Time.time < nextShot) return false;
            if (!brain.IsAimedAt(t, 14f)) return false;

            if (p.ChargeTime > 0f)
            {
                if (chargeStart < 0f) chargeStart = Time.time;
                if (laser != null)
                {
                    laser.enabled = true;
                    laser.SetPosition(0, brain.Muzzle);
                    laser.SetPosition(1, t.AimPoint);
                }
                if (Time.time - chargeStart < p.ChargeTime) return false;

                CancelCharge();
                FireOne(t, spreadMultiplier);
                nextShot = Time.time + p.FireInterval;
                return true;
            }

            FireOne(t, spreadMultiplier);
            burstLeft--;
            if (burstLeft <= 0)
            {
                burstLeft = p.Burst;
                nextShot = Time.time + p.BurstPause * Random.Range(0.8f, 1.25f);
            }
            else
            {
                nextShot = Time.time + p.FireInterval;
            }
            return true;
        }

        void FireOne(Targetable t, float spreadMultiplier)
        {
            float spread = p.SpreadDeg * spreadMultiplier;
            if (brain.Agent.velocity.sqrMagnitude > 0.5f) spread *= 1.4f;

            Ballistics.Fire(brain.Muzzle, t.AimPoint, spread, p.Range * 1.25f, GameLayers.EnemyBulletMask,
                            p.Damage, brain.gameObject, Team.Attacker, p.Tracer, p.StructureMult);
            brain.Visual.Recoil();
            brain.Visual.SetAiming(true);
            Fx.MuzzleFlash(brain.Muzzle);
            Fx.Sound(p.Sfx, brain.Muzzle, p.ChargeTime > 0f ? 1f : 0.55f, Random.Range(0.92f, 1.08f));
        }
    }
}
