using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Lightweight blackboard shared by every attacker. It lets individual Utility AIs
    /// react to what the rest of the squad is doing (pinning fire, flank claims, wounded allies).
    /// </summary>
    public static class SquadDirector
    {
        sealed class FlankClaim
        {
            public Vector3 Position;
            public float Expires;
            public EnemyBrain Owner;
        }

        public static readonly List<EnemyBrain> Enemies = new List<EnemyBrain>();
        static readonly List<FlankClaim> claims = new List<FlankClaim>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Enemies.Clear();
            claims.Clear();
        }

        public static void Register(EnemyBrain e)
        {
            if (!Enemies.Contains(e)) Enemies.Add(e);
        }

        public static void Unregister(EnemyBrain e)
        {
            Enemies.Remove(e);
            ReleaseFlank(e);
        }

        /// <summary>Allies (except <paramref name="except"/>) that currently have a clear shot at the target.</summary>
        public static int CountEngaging(Targetable t, EnemyBrain except)
        {
            int n = 0;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (e == except || e.Target != t || !e.HasLos) continue;
                if (e.DistToTarget <= e.Profile.Range * 1.3f) n++;
            }
            return n;
        }

        public static int CountTargeting(Targetable t, EnemyBrain except)
        {
            int n = 0;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (e != except && e.Target == t) n++;
            }
            return n;
        }

        /// <summary>
        /// Average (flat) direction from the target towards the allies that are already engaging it –
        /// i.e. the "front" of the attack. Zero when nobody is engaging yet.
        /// </summary>
        public static Vector3 FrontDirection(Targetable t, EnemyBrain except)
        {
            Vector3 sum = Vector3.zero;
            Vector3 tp = t.transform.position;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (e == except || e.Target != t || !e.HasLos) continue;
                Vector3 d = e.Position - tp;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) sum += d.normalized;
            }
            sum.y = 0f;
            return sum.sqrMagnitude > 0.05f ? sum.normalized : Vector3.zero;
        }

        public static EnemyBrain FindWoundedAlly(EnemyBrain medic, float maxDist, out float need)
        {
            EnemyBrain best = null;
            float bestScore = 0f;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (e == medic || e.IsDead) continue;
                float frac = e.Health.Fraction;
                if (frac > 0.85f) continue;
                float d = Vector3.Distance(medic.Position, e.Position);
                if (d > maxDist) continue;
                // Prefer the most wounded, then the closest.
                float score = (1f - frac) + (1f - d / maxDist) * 0.35f;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            need = best != null ? 1f - best.Health.Fraction : 0f;
            return best;
        }

        public static bool MedicNear(Vector3 p, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (e.Profile.Role == EnemyRole.Medic && (e.Position - p).sqrMagnitude < r2) return true;
            }
            return false;
        }

        public static EnemyBrain NearestAlly(EnemyBrain from, System.Predicate<EnemyBrain> filter)
        {
            EnemyBrain best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (e == from || e.IsDead || !filter(e)) continue;
                float d = (e.Position - from.Position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // ---- Flank claims: stop two flankers choosing the same firing spot ----

        public static void ClaimFlank(Vector3 pos, EnemyBrain owner)
        {
            ReleaseFlank(owner);
            claims.Add(new FlankClaim { Position = pos, Owner = owner, Expires = Time.time + 20f });
        }

        public static void ReleaseFlank(EnemyBrain owner)
        {
            for (int i = claims.Count - 1; i >= 0; i--)
                if (claims[i].Owner == owner) claims.RemoveAt(i);
        }

        public static float NearestFlankClaim(Vector3 pos, EnemyBrain except)
        {
            float best = float.MaxValue;
            for (int i = claims.Count - 1; i >= 0; i--)
            {
                var c = claims[i];
                if (c.Expires < Time.time || c.Owner == null) { claims.RemoveAt(i); continue; }
                if (c.Owner == except) continue;
                best = Mathf.Min(best, Vector3.Distance(pos, c.Position));
            }
            return best;
        }
    }
}
