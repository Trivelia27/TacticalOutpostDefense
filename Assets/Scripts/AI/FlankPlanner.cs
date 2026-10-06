using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    public struct FlankPlan
    {
        public bool Valid;
        public bool HasWaypoint;
        public Vector3 Waypoint;
        public Vector3 Position;
        public float Quality;
        public Vector3 TargetRef;   // where the target was when the plan was made
        public float Angle;
    }

    /// <summary>
    /// Finds a firing position on the side/rear of the target relative to where the rest of the
    /// squad is attacking from, plus an optional arc waypoint so the route does not run through
    /// the target's line of fire.
    /// </summary>
    public static class FlankPlanner
    {
        static readonly float[] Angles = { 55f, -55f, 80f, -80f, 105f, -105f, 130f, -130f };
        static readonly NavMeshPath path = new NavMeshPath();

        struct Candidate
        {
            public Vector3 Pos;
            public float Angle;
            public float Quality;
            public float Exposure;
        }

        public static FlankPlan Plan(EnemyBrain b)
        {
            var plan = new FlankPlan();
            var target = b.Target;
            if (target == null) return plan;

            Vector3 T = target.transform.position;
            Vector3 me = b.Position;

            // "Front" of the attack = where allies are already shooting from; flank around it.
            Vector3 axis = SquadDirector.FrontDirection(target, b);
            if (axis == Vector3.zero)
            {
                axis = me - T;
                axis.y = 0f;
                axis = axis.sqrMagnitude > 0.01f ? axis.normalized : Vector3.forward;
            }

            float r = Mathf.Clamp(b.Profile.PreferredRange, 6f, b.Profile.Range * 0.85f);
            var cands = new List<Candidate>(Angles.Length);

            for (int i = 0; i < Angles.Length; i++)
            {
                float ang = Angles[i];
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * axis;
                Vector3 p = T + dir * r;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                p = hit.position;

                // The flank spot has to actually see the target.
                if (Physics.Linecast(p + Vector3.up * 1.6f, target.AimPoint, GameLayers.ObstacleMask)) continue;

                float spacing = SquadDirector.NearestFlankClaim(p, b);
                float spacingScore = Mathf.Clamp01(spacing / 8f);

                float closest = DistancePointToSegment(T, me, p);
                float exposure = Mathf.Clamp01(closest / r);       // 1 = route stays far from the target
                float angleScore = Mathf.Abs(ang) / 130f;
                float lenScore = 1f - Mathf.Clamp01(Vector3.Distance(me, p) / 70f);

                float q = 0.35f * angleScore + 0.25f * lenScore + 0.2f * exposure + 0.2f * spacingScore;
                cands.Add(new Candidate { Pos = p, Angle = ang, Quality = q, Exposure = exposure });
            }

            cands.Sort((x, y) => y.Quality.CompareTo(x.Quality));

            int checkedCount = 0;
            for (int i = 0; i < cands.Count && checkedCount < 3; i++, checkedCount++)
            {
                var c = cands[i];
                if (!NavMesh.CalculatePath(me, c.Pos, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    continue;

                plan.Valid = true;
                plan.Position = c.Pos;
                plan.Angle = c.Angle;
                plan.Quality = c.Quality;
                plan.TargetRef = T;

                if (c.Exposure < 0.7f)
                {
                    // Route would pass close to the target: swing wide first.
                    Vector3 wdir = Quaternion.AngleAxis(c.Angle * 0.55f, Vector3.up) * axis;
                    Vector3 wp = T + wdir * (r * 1.35f);
                    if (NavMesh.SamplePosition(wp, out NavMeshHit wh, 6f, NavMesh.AllAreas) &&
                        NavMesh.CalculatePath(me, wh.position, NavMesh.AllAreas, path) &&
                        path.status == NavMeshPathStatus.PathComplete)
                    {
                        plan.HasWaypoint = true;
                        plan.Waypoint = wh.position;
                    }
                }
                return plan;
            }

            return plan;
        }

        static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0f;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
