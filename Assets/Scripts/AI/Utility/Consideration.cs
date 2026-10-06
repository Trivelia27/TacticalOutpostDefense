using System;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// One input of the Utility AI: reads a value from the agent, normalises it to 0..1
    /// and shapes it through a response curve. Last values are kept for the debug inspector.
    /// </summary>
    public sealed class Consideration
    {
        public readonly string Name;
        readonly Func<EnemyBrain, float> input;
        readonly ResponseCurve curve;

        public float LastInput { get; private set; }
        public float LastScore { get; private set; }

        public Consideration(string name, Func<EnemyBrain, float> input, ResponseCurve curve)
        {
            Name = name;
            this.input = input;
            this.curve = curve;
        }

        public float Score(EnemyBrain brain)
        {
            LastInput = Mathf.Clamp01(input(brain));
            LastScore = curve.Evaluate(LastInput);
            return LastScore;
        }
    }
}
