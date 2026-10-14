using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>How an enemy learned the position it currently remembers for a target.</summary>
    public enum InfoSource { None, Objective, Seen, Heard, Alert, Hit }

    /// <summary>What one enemy remembers about one target: last known position, when it was learned and how.</summary>
    public sealed class TargetMemory
    {
        public Targetable Target;
        public Vector3 Position;            // last known position
        public float LastInfoTime;          // any information (seen, heard, alert, hit)
        public float LastSeenTime = -99f;   // last time it was actually in view
        public bool Visible;                // in view right now
        public InfoSource Source;
        public bool Searched;               // the last known position was already checked
        public float NextAlert;             // earliest time this sighting is reported to squad mates again

        public float Age => Time.time - LastInfoTime;
        public float SeenAge => Time.time - LastSeenTime;
    }

    /// <summary>Result of one perception test; every stage is kept so the debug view can show why a target was (not) seen.</summary>
    public struct SenseResult
    {
        public float Distance;
        public float Angle;         // between the facing direction and the target, degrees
        public bool InRange;        // within the sight (detection) radius, or the close-sense radius
        public bool InFov;          // inside the field of view (or already being tracked)
        public bool LineOfSight;    // no obstacle between the eye and the target
        public bool Visible => InRange && InFov && LineOfSight;
    }

    /// <summary>
    /// Perception and memory of a single enemy.
    ///  Perception: sight (distance + field of view + line-of-sight raycast), close sense (a short 360 degree radius), hearing (shots)
    ///              and damage awareness (who just hurt me).
    ///  Memory:     per target last known position, last seen time and source; forgotten after <see cref="MemorySeconds"/>.
    ///  Alert:      what one enemy learns is shared with squad mates nearby (shared alert).
    /// The reactor is the mission objective, so its position is always known.
    /// </summary>
    public sealed class EnemySenses
    {
        public const float MemorySeconds = 10f;
        public const float CloseSense = 6f;
        public const float AlertRadius = 30f;
        const float AlertInterval = 0.8f;
        const float TrackingBonus = 1.1f;

        readonly Dictionary<Targetable, TargetMemory> memories = new Dictionary<Targetable, TargetMemory>();
        readonly List<Targetable> scratch = new List<Targetable>();

        public IEnumerable<TargetMemory> All => memories.Values;
        public int Count => memories.Count;

        public bool TryGet(Targetable t, out TargetMemory memory) => memories.TryGetValue(t, out memory);

        public TargetMemory Get(Targetable t)
        {
            memories.TryGetValue(t, out TargetMemory m);
            return m;
        }

        public void Forget(Targetable t)
        {
            if (t != null && t.kind != TargetKind.Reactor) memories.Remove(t);
        }

        // ------------------------------------------------------------------ perception

        /// <summary>
        /// Pure perception test. <paramref name="tracking"/> means the target is already in view: it is then kept
        /// while it stays within range and line of sight even when it leaves the field of view.
        /// </summary>
        public static SenseResult Evaluate(Vector3 eye, Vector3 facing, Vector3 position, Vector3 targetPos, Vector3 targetAim,
                                           float sightRange, float fovDeg, bool tracking)
        {
            var r = new SenseResult();
            Vector3 flat = targetPos - position;
            flat.y = 0f;
            r.Distance = flat.magnitude;
            facing.y = 0f;
            r.Angle = flat.sqrMagnitude > 0.0001f && facing.sqrMagnitude > 0.0001f ? Vector3.Angle(facing, flat) : 0f;

            r.InRange = r.Distance <= sightRange * (tracking ? TrackingBonus : 1f);
            r.InFov = tracking || r.Distance <= CloseSense || r.Angle <= fovDeg * 0.5f;
            r.LineOfSight = !Physics.Linecast(eye, targetAim, GameLayers.ObstacleMask);
            return r;
        }

        public SenseResult Probe(EnemyBrain b, Targetable t)
        {
            bool tracking = memories.TryGetValue(t, out TargetMemory m) && m.Visible;
            return Evaluate(b.Eye, b.transform.forward, b.Position, t.transform.position, t.AimPoint,
                            b.Profile.SightRange, b.Profile.FovDeg, tracking);
        }

        /// <summary>Refreshes what the enemy can see right now and drops memories that have expired.</summary>
        public void Update(EnemyBrain b)
        {
            float now = Time.time;
            var all = Targetable.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (!t.IsValid) continue;

                memories.TryGetValue(t, out TargetMemory m);
                var s = Probe(b, t);

                if (s.Visible)
                {
                    if (m == null) { m = new TargetMemory { Target = t }; memories[t] = m; }
                    m.Position = t.transform.position;
                    m.LastSeenTime = m.LastInfoTime = now;
                    m.Visible = true;
                    m.Searched = false;
                    if (t.kind != TargetKind.Reactor) m.Source = InfoSource.Seen;
                    if (t.kind != TargetKind.Reactor && now >= m.NextAlert)
                    {
                        m.NextAlert = now + AlertInterval;
                        ShareAlert(b, t, m.Position);
                    }
                }
                else if (m != null)
                {
                    m.Visible = false;
                }

                if (t.kind == TargetKind.Reactor)
                {
                    // The objective is briefed to every attacker: always known, only "visible" when actually in view.
                    if (m == null) { m = new TargetMemory { Target = t, Source = InfoSource.Objective }; memories[t] = m; }
                    m.Position = t.transform.position;
                    m.LastInfoTime = now;
                    if (!m.Visible) m.Source = InfoSource.Objective;
                }
            }

            // Forget stale or destroyed targets.
            scratch.Clear();
            foreach (var kv in memories)
            {
                var t = kv.Key;
                if (t == null || !t.IsValid) { scratch.Add(t); continue; }
                if (t.kind != TargetKind.Reactor && !kv.Value.Visible && kv.Value.Age > MemorySeconds) scratch.Add(t);
            }
            for (int i = 0; i < scratch.Count; i++) memories.Remove(scratch[i]);
        }

        // ------------------------------------------------------------------ hearing, damage, alert

        /// <summary>Learn (or refresh) a position without seeing the target. Fresher visual information is never overwritten.</summary>
        public void Learn(Targetable t, Vector3 position, InfoSource source)
        {
            if (t == null || !t.IsValid || t.kind == TargetKind.Reactor) return;
            if (!memories.TryGetValue(t, out TargetMemory m)) { m = new TargetMemory { Target = t, Source = InfoSource.None, LastInfoTime = -99f }; memories[t] = m; }
            if (m.Visible) return;
            if (source == InfoSource.Alert && m.Age < 1f) return;          // a squad mate's report is weaker than my own recent info

            m.Position = position;
            m.LastInfoTime = Time.time;
            m.Source = source;
            m.Searched = false;
        }

        /// <summary>A shot was fired at <paramref name="origin"/>: every enemy within hearing range learns roughly where.</summary>
        public static void EmitNoise(GameObject source, Vector3 origin)
        {
            if (source == null) return;
            var t = source.GetComponentInParent<Targetable>();
            if (t == null) return;

            var list = SquadDirector.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.IsDead || e.Senses == null) continue;
                float d = Vector3.Distance(e.Position, origin);
                if (d > e.Profile.HearingRange) continue;
                // Sound is imprecise: the error grows with distance.
                Vector2 jitter = Random.insideUnitCircle * Mathf.Lerp(1f, 5f, d / e.Profile.HearingRange);
                e.Senses.Learn(t, origin + new Vector3(jitter.x, 0f, jitter.y), InfoSource.Heard);
            }
        }

        static void ShareAlert(EnemyBrain from, Targetable t, Vector3 position)
        {
            var list = SquadDirector.Enemies;
            float r2 = AlertRadius * AlertRadius;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == from || e == null || e.IsDead || e.Senses == null) continue;
                if ((e.Position - from.Position).sqrMagnitude > r2) continue;
                e.Senses.Learn(t, position, InfoSource.Alert);
            }
        }
    }
}
