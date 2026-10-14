using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Scores every target an enemy knows about and picks the best. A target is "known" when the enemy has a memory of it
    /// (see <see cref="EnemySenses"/>): the reactor is always known, the player and turrets only after they were seen, heard,
    /// reported by a squad mate or fired back, and until the memory expires.
    /// score = (wDist*proximity + wVis*visibility + wThreat*threat + wVuln*vulnerability + wObj*objective)
    ///         * rolePreference * crowdingPenalty * hysteresis
    /// Distances use the remembered (last known) position, so an enemy does not "see through walls".
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
            bool currentValid = b.HasTarget && b.Senses.TryGet(b.Target, out _);
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
                if (!b.Senses.TryGet(t, out TargetMemory mem)) continue;       // unknown to this enemy

                float dist = Vector3.Distance(b.Position, mem.Position);
                if (dist > SenseRange && t.kind != TargetKind.Reactor) continue;

                float proximity = 1f - Mathf.Clamp01(dist / SenseRange);
                float visibility = mem.Visible ? 1f : 0.35f;

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
