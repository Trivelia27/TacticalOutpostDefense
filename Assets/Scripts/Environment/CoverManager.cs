using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace TacticalOutpost
{
    public sealed class CoverPoint
    {
        public Vector3 Position;
        public Vector3 Normal;      // pointing away from the obstacle
        public CoverObject Owner;
        public EnemyBrain Occupant;
    }

    public enum CoverMode { Fight, Retreat }

    public readonly struct CoverQuery
    {
        public readonly CoverPoint Point;
        public readonly float Quality;
        public bool Valid => Point != null;
        public CoverQuery(CoverPoint p, float q) { Point = p; Quality = q; }
        public static CoverQuery None => new CoverQuery(null, 0f);
    }

    /// <summary>
    /// Generates cover points around every <see cref="CoverObject"/> and answers
    /// "where is the best cover against that target?" queries for the Utility AI.
    /// </summary>
    public static class CoverManager
    {
        public static readonly List<CoverPoint> Points = new List<CoverPoint>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Points.Clear();

        public static void Build(IEnumerable<CoverObject> objects)
        {
            Points.Clear();
            foreach (var obj in objects)
            {
                var box = obj.GetComponent<BoxCollider>();
                if (box == null) continue;

                Vector3 s = Vector3.Scale(box.size, obj.transform.lossyScale);
                Vector3 center = obj.transform.TransformPoint(box.center);
                center.y = 0f;
                Vector3 right = obj.transform.right; right.y = 0f; right.Normalize();
                Vector3 fwd = obj.transform.forward; fwd.y = 0f; fwd.Normalize();
                float halfX = s.x * 0.5f, halfZ = s.z * 0.5f;

                // Faces along +-right (their length runs along forward) and +-forward (length along right).
                AddFace(obj, center, right, fwd, halfX, halfZ);
                AddFace(obj, center, -right, fwd, halfX, halfZ);
                AddFace(obj, center, fwd, right, halfZ, halfX);
                AddFace(obj, center, -fwd, right, halfZ, halfX);
            }
        }

        static void AddFace(CoverObject obj, Vector3 center, Vector3 normal, Vector3 along, float halfDepth, float halfLength)
        {
            float length = halfLength * 2f;
            int count = Mathf.Max(1, Mathf.FloorToInt(length / obj.pointSpacing));
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : Mathf.Lerp(-halfLength + obj.pointSpacing * 0.5f, halfLength - obj.pointSpacing * 0.5f, i / (float)(count - 1));
                Vector3 p = center + normal * (halfDepth + obj.standOff) + along * t;

                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 0.6f, NavMesh.AllAreas)) continue;
                if (Physics.CheckSphere(hit.position + Vector3.up * 0.9f, 0.4f, GameLayers.ObstacleMask)) continue;

                Points.Add(new CoverPoint { Position = hit.position, Normal = normal, Owner = obj });
            }
        }

        public static void Release(EnemyBrain who)
        {
            for (int i = 0; i < Points.Count; i++)
                if (Points[i].Occupant == who) Points[i].Occupant = null;
        }

        public static bool Claim(CoverPoint p, EnemyBrain who)
        {
            if (p.Occupant != null && p.Occupant != who) return false;
            Release(who);
            p.Occupant = who;
            return true;
        }

        /// <summary>
        /// 1 when a crouched unit standing at <paramref name="pos"/> cannot be seen from
        /// <paramref name="threatAim"/>, 0.5 when only partially hidden, 0 when exposed.
        /// </summary>
        public static float Protection(Vector3 pos, Vector3 threatAim)
        {
            float v = 0f;
            if (Physics.Linecast(threatAim, pos + Vector3.up * 0.4f, GameLayers.ObstacleMask)) v += 0.5f;
            if (Physics.Linecast(threatAim, pos + Vector3.up * 0.85f, GameLayers.ObstacleMask)) v += 0.5f;
            return v;
        }

        public static CoverQuery FindBest(EnemyBrain b, Targetable threat, CoverMode mode, float maxDist = 24f)
        {
            CoverPoint best = null;
            float bestQ = 0f;

            Vector3 me = b.Position;
            Vector3 threatPos = threat.transform.position;
            Vector3 threatAim = threat.AimPoint;
            float range = b.Profile.Range;
            float pref = Mathf.Max(1f, b.Profile.PreferredRange);
            float maxSqr = maxDist * maxDist;

            for (int i = 0; i < Points.Count; i++)
            {
                var p = Points[i];
                if (p.Occupant != null && p.Occupant != b) continue;

                float dMeSqr = (p.Position - me).sqrMagnitude;
                if (dMeSqr > maxSqr) continue;

                float dThreat = Vector3.Distance(p.Position, threatPos);
                if (mode == CoverMode.Fight && dThreat < b.Profile.MinRange) continue;

                if (Protection(p.Position, threatAim) < 0.99f) continue;

                float distScore = 1f - Mathf.Sqrt(dMeSqr) / maxDist;
                float q;
                if (mode == CoverMode.Fight)
                {
                    float rangeFit = 1f - Mathf.Clamp01(Mathf.Abs(dThreat - pref) / pref);
                    bool peek = dThreat <= range &&
                                !Physics.Linecast(p.Position + Vector3.up * 1.6f, threatAim, GameLayers.ObstacleMask);
                    q = 0.3f * distScore + 0.35f * rangeFit + (peek ? 0.35f : 0f);
                }
                else
                {
                    float away = Mathf.Clamp01((dThreat - 8f) / 20f);
                    float medic = SquadDirector.MedicNear(p.Position, 9f) ? 0.25f : 0f;
                    q = 0.35f * distScore + 0.4f * away + medic;
                }

                if (q > bestQ) { bestQ = q; best = p; }
            }

            return best != null ? new CoverQuery(best, Mathf.Clamp01(bestQ)) : CoverQuery.None;
        }
    }
}
