using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// An action the Utility AI can choose. Its desirability is the product of all its
    /// considerations (with the standard "compensation factor" so adding considerations
    /// does not unfairly punish an action) multiplied by the role weight.
    /// </summary>
    public abstract class UtilityAction
    {
        public abstract string Name { get; }
        public virtual Color DebugColor => Color.white;
        public float LastScore { get; private set; }
        public readonly List<Consideration> Considerations = new List<Consideration>();

        protected abstract float RoleWeight(RoleProfile profile);

        /// <summary>Hard preconditions (e.g. a Medic action is only valid for medics).</summary>
        public virtual bool IsAvailable(EnemyBrain brain) => true;

        public float Evaluate(EnemyBrain brain)
        {
            if (!IsAvailable(brain))
            {
                for (int i = 0; i < Considerations.Count; i++) Considerations[i].Score(brain);
                return LastScore = 0f;
            }

            float score = 1f;
            for (int i = 0; i < Considerations.Count; i++)
                score *= Considerations[i].Score(brain);

            int n = Considerations.Count;
            if (n > 1)
            {
                float modification = 1f - 1f / n;
                score += (1f - score) * modification * score;
            }

            return LastScore = score * RoleWeight(brain.Profile);
        }

        public virtual void OnEnter(EnemyBrain brain) { }
        public abstract void Tick(EnemyBrain brain, float dt);
        public virtual void OnExit(EnemyBrain brain) { }

        /// <summary>True when the action has completed and the brain may switch immediately.</summary>
        public virtual bool IsFinished(EnemyBrain brain) => false;

        protected void Add(string name, System.Func<EnemyBrain, float> input, ResponseCurve curve)
            => Considerations.Add(new Consideration(name, input, curve));
    }
}
