using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// World-space AI debug drawing for the enemy under the crosshair (AIDebug.Selected): field of view, detection radius,
    /// raycast to the target, NavMesh path and destination, last known position, cover point and flank position.
    /// Uses pooled LineRenderers, so it shows up in the Game view and in builds (Gizmos only draw in the editor).
    /// <see cref="Gizmos"/> draws the same data in the Scene view.
    /// </summary>
    public sealed class AIDebugDraw : MonoBehaviour
    {
        static AIDebugDraw instance;
        readonly List<LineRenderer> pool = new List<LineRenderer>();
        int used;
        Material material;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create() => Ensure();

        /// <summary>Makes sure the renderer exists (it is created on demand and survives scene loads).</summary>
        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("AIDebugDraw");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<AIDebugDraw>();
        }

        void LateUpdate()
        {
            used = 0;
            var b = AIDebug.Selected;
            if (AIDebug.WorldOn && b != null && !b.IsDead && b.Senses != null) Draw(b);
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // ------------------------------------------------------------------ drawing

        static readonly Color FovColor = new Color(0.4f, 0.9f, 1f, 0.9f);
        static readonly Color RangeColor = new Color(0.4f, 0.9f, 1f, 0.35f);
        static readonly Color ClearColor = new Color(0.3f, 1f, 0.4f, 0.95f);
        static readonly Color BlockedColor = new Color(1f, 0.3f, 0.3f, 0.95f);
        static readonly Color PathColor = Color.white;
        static readonly Color MemoryColor = new Color(1f, 0.55f, 0.1f, 1f);

        void Draw(EnemyBrain b)
        {
            Vector3 eye = b.Eye;
            float y = b.Position.y + 0.15f;

            // Field of view + detection radius
            float sight = b.Profile.SightRange;
            float half = b.Profile.FovDeg * 0.5f;
            Vector3 fwd = b.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            var arc = new List<Vector3> { Flat(b.Position, y) };
            for (int a = -(int)half; a <= (int)half; a += 10) arc.Add(Flat(b.Position, y) + Quaternion.Euler(0f, a, 0f) * fwd * sight);
            arc.Add(Flat(b.Position, y) + Quaternion.Euler(0f, half, 0f) * fwd * sight);
            arc.Add(Flat(b.Position, y));
            Line(arc, FovColor, 0.07f);
            Line(Circle(Flat(b.Position, y), EnemySenses.CloseSense), RangeColor, 0.05f);          // 360 degree close-sense radius
            Line(Circle(Flat(b.Position, y), b.Profile.HearingRange), new Color(0.8f, 0.8f, 0.2f, 0.18f), 0.04f);  // hearing

            // Raycast to the target
            if (b.HasTarget)
            {
                Vector3 aim = b.Target.AimPoint;
                bool clear = !Physics.Linecast(eye, aim, out RaycastHit hit, GameLayers.ObstacleMask);
                Line(new List<Vector3> { eye, clear ? aim : hit.point }, clear ? ClearColor : BlockedColor, 0.09f);
                if (!clear) Marker(hit.point, BlockedColor, 0.35f);
            }

            // Last known position (memory)
            var mem = b.TargetMem;
            if (mem != null && b.Target != null && b.Target.kind != TargetKind.Reactor)
            {
                Marker(mem.Position, MemoryColor, 0.7f);
                Line(new List<Vector3> { mem.Position, mem.Position + Vector3.up * 3f }, MemoryColor, 0.1f);
                Line(Circle(Flat(mem.Position, y), 1.2f), MemoryColor, 0.06f);
            }

            // Navigation: destination and path
            if (b.Agent != null && b.Agent.isOnNavMesh && b.Agent.hasPath)
            {
                var corners = b.Agent.path.corners;
                if (corners.Length > 1)
                {
                    var pts = new List<Vector3>(corners.Length);
                    for (int i = 0; i < corners.Length; i++) pts.Add(corners[i] + Vector3.up * 0.2f);
                    Line(pts, PathColor, 0.08f);
                    Marker(b.Agent.destination, PathColor, 0.5f);
                }
            }

            // Tactical positions
            if (b.BestCover.Valid)
            {
                Line(Circle(Flat(b.BestCover.Point.Position, y), 0.8f), new Color(1f, 0.9f, 0.2f), 0.08f);
                Line(new List<Vector3> { b.BestCover.Point.Position, b.BestCover.Point.Position + Vector3.up * 2f }, new Color(1f, 0.9f, 0.2f), 0.06f);
            }
            if (b.Flank.Valid)
            {
                var mag = new Color(1f, 0.2f, 0.9f);
                Line(Circle(Flat(b.Flank.Position, y), 0.9f), mag, 0.08f);
                var pts = new List<Vector3> { Flat(b.Position, y) };
                if (b.Flank.HasWaypoint) pts.Add(Flat(b.Flank.Waypoint, y));
                pts.Add(Flat(b.Flank.Position, y));
                Line(pts, mag, 0.06f);
            }
        }

        static Vector3 Flat(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        static List<Vector3> Circle(Vector3 center, float radius)
        {
            var pts = new List<Vector3>(41);
            for (int i = 0; i <= 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                pts.Add(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
            }
            return pts;
        }

        void Marker(Vector3 p, Color c, float size)
        {
            Line(new List<Vector3> { p + new Vector3(-size, 0.1f, 0f), p + new Vector3(size, 0.1f, 0f) }, c, 0.08f);
            Line(new List<Vector3> { p + new Vector3(0f, 0.1f, -size), p + new Vector3(0f, 0.1f, size) }, c, 0.08f);
        }

        void Line(List<Vector3> pts, Color color, float width)
        {
            if (pts.Count < 2) return;
            LineRenderer lr;
            if (used < pool.Count) lr = pool[used];
            else
            {
                var go = new GameObject("DebugLine" + pool.Count);
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                if (material == null) material = Visuals.Unlit(Color.white);
                lr.sharedMaterial = material;
                pool.Add(lr);
            }
            used++;
            lr.enabled = true;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.startWidth = lr.endWidth = width;
            lr.startColor = lr.endColor = color;
        }

        // ------------------------------------------------------------------ Scene view

        public static void Gizmos(EnemyBrain b)
        {
            if (b == null || b.Senses == null) return;
            UnityEngine.Gizmos.color = new Color(0.4f, 0.9f, 1f);
            UnityEngine.Gizmos.DrawWireSphere(b.Position, EnemySenses.CloseSense);
            Vector3 fwd = b.transform.forward;
            float half = b.Profile.FovDeg * 0.5f;
            UnityEngine.Gizmos.DrawLine(b.Eye, b.Eye + Quaternion.Euler(0f, half, 0f) * fwd * b.Profile.SightRange);
            UnityEngine.Gizmos.DrawLine(b.Eye, b.Eye + Quaternion.Euler(0f, -half, 0f) * fwd * b.Profile.SightRange);

            if (b.HasTarget)
            {
                UnityEngine.Gizmos.color = b.HasLos ? Color.red : Color.gray;
                UnityEngine.Gizmos.DrawLine(b.Eye, b.Target.AimPoint);
            }
            if (b.TargetMem != null && b.Target != null && b.Target.kind != TargetKind.Reactor)
            {
                UnityEngine.Gizmos.color = new Color(1f, 0.55f, 0.1f);
                UnityEngine.Gizmos.DrawWireSphere(b.TargetMem.Position, 0.7f);
            }
            if (b.BestCover.Valid)
            {
                UnityEngine.Gizmos.color = Color.yellow;
                UnityEngine.Gizmos.DrawWireSphere(b.BestCover.Point.Position, 0.5f);
            }
            if (b.Flank.Valid)
            {
                UnityEngine.Gizmos.color = Color.magenta;
                UnityEngine.Gizmos.DrawWireSphere(b.Flank.Position, 0.6f);
                if (b.Flank.HasWaypoint) UnityEngine.Gizmos.DrawLine(b.Flank.Waypoint, b.Flank.Position);
            }
            if (b.Agent != null && b.Agent.isOnNavMesh && b.Agent.hasPath)
            {
                UnityEngine.Gizmos.color = Color.white;
                var c = b.Agent.path.corners;
                for (int i = 1; i < c.Length; i++) UnityEngine.Gizmos.DrawLine(c[i - 1], c[i]);
            }
        }
    }
}
