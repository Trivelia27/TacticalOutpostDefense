using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Scores every <see cref="Targetable"/> for one enemy and picks the best.
    /// score = (wDist*proximity + wVis*visibility + wThreat*threat + wVuln*vulnerability + wObj*objective)
    ///         * rolePreference * crowdingPenalty * hysteresis
    /// </summary>
    public static class TargetSelector
    {
        public const float WDistance = 0.30f;
        public const float WVisibility = 0.15f;
        public const float WThreat = 0.25f;
        public const float WVulnerability = 0.10f;
        public const float WObjective = 0.20f;

        public const float SenseRange = 70f;
        public const float SwitchCooldown = 1.5f;
        public const float StickyBonus = 1.2f;

        public static void Select(EnemyBrain b)
        {
            bool currentValid = b.HasTarget;
            if (currentValid && Time.time - b.LastTargetSwitch < SwitchCooldown && b.TargetScores.Count > 0)
                return; // don't flip-flop

            b.TargetScores.Clear();
            Targetable best = null;
            float bestScore = 0f;

            var all = Targetable.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (!t.IsValid) continue;

                float dist = Vector3.Distance(b.Position, t.transform.position);
                if (dist > SenseRange && t.kind != TargetKind.Reactor) continue;

                float proximity = 1f - Mathf.Clamp01(dist / SenseRange);
                bool los = !Physics.Linecast(b.Eye, t.AimPoint, GameLayers.ObstacleMask);
                float visibility = los ? 1f : 0.35f;

                float threat = t.threat;
                if (b.Health.LastAttacker == t.gameObject && Time.time - b.Health.LastDamageTime < 4f)
                    threat += 0.5f; // revenge: whoever just hurt me is more interesting
                threat = Mathf.Clamp01(threat);

                float vulnerability = 1f - t.Health.Fraction;
                float objective = t.strategicValue;

                float s = WDistance * proximity + WVisibility * visibility + WThreat * threat
                          + WVulnerability * vulnerability + WObjective * objective;

                s *= b.Profile.TargetPref(t.kind);

                // Spread fire: avoid everybody piling onto the same non-objective target.
                if (t.kind != TargetKind.Reactor)
                {
                    int crowd = SquadDirector.CountTargeting(t, b);
                    if (crowd >= 3) s *= Mathf.Pow(0.75f, crowd - 2);
                }

                if (t == b.Target) s *= StickyBonus;

                b.TargetScores.Add(new KeyValuePair<string, float>(Label(t), s));
                if (s > bestScore) { bestScore = s; best = t; }
            }

            if (best != b.Target)
            {
                b.Target = best;
                b.LastTargetSwitch = Time.time;
            }
            else if (best == null)
            {
                b.Target = null;
            }
        }

        static string Label(Targetable t)
        {
            switch (t.kind)
            {
                case TargetKind.Reactor: return "Reactor";
                case TargetKind.Player: return "Player";
                default: return "Turret";
            }
        }
    }
}
