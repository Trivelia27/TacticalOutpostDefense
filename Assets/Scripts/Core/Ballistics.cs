using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Hitscan shot resolution shared by the player, turrets and enemies.</summary>
    public static class Ballistics
    {
        static readonly Color Dust = new Color(0.85f, 0.75f, 0.55f);
        static readonly Color Blood = new Color(1f, 0.35f, 0.2f);
        static readonly Color Metal = new Color(1f, 0.9f, 0.5f);

        public static Vector3 ApplySpread(Vector3 dir, float spreadDeg)
        {
            if (spreadDeg <= 0.001f) return dir;
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(spreadDeg * Mathf.Deg2Rad);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right);
            return (dir + right * r.x + up * r.y).normalized;
        }

        /// <summary>Fires one ray. Returns the damaged Health (or null).</summary>
        public static Health Fire(Vector3 origin, Vector3 aimPoint, float spreadDeg, float range, int mask,
                                  float damage, GameObject source, Team team, Color tracer,
                                  float structureMultiplier = 1f)
        {
            Vector3 dir = aimPoint - origin;
            if (dir.sqrMagnitude < 0.0001f) dir = source != null ? source.transform.forward : Vector3.forward;
            dir = ApplySpread(dir.normalized, spreadDeg);

            Vector3 end;
            Health damaged = null;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, range, mask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var h = hit.collider.GetComponentInParent<Health>();
                if (h != null && !h.IsDead && h.team != team)
                {
                    float dmg = h.isStructure ? damage * structureMultiplier : damage;
                    h.TakeDamage(dmg, source);
                    damaged = h;
                    Fx.Spark(end, h.isStructure ? Metal : Blood, 0.3f, 0.12f);
                    Fx.Sound(SfxKind.Hit, end, 0.5f);
                }
                else
                {
                    Fx.Spark(end, Dust, 0.2f, 0.18f);
                }
            }
            else
            {
                end = origin + dir * range;
            }

            Fx.Tracer(origin, end, tracer);
            if (team == Team.Defender) SuppressNearby(origin, end, damaged);
            return damaged;
        }

        /// <summary>Defender fire near an enemy makes it feel pinned down (feeds Utility AI "Suppression").</summary>
        static void SuppressNearby(Vector3 a, Vector3 b, Health hitHealth)
        {
            var list = SquadDirector.Enemies;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.01f) return;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.Health == hitHealth) continue;
                Vector3 p = e.transform.position + Vector3.up;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
                if ((a + ab * t - p).sqrMagnitude < 2.6f * 2.6f)
                    e.AddSuppression(0.12f);
            }
        }
    }
}
