using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Maps a normalised input (0..1) to a normalised utility score (0..1).
    /// Linear:    y = m*x + b
    /// Quadratic: y = m*(x-c)^k + b
    /// Logistic:  y = 1 / (1 + e^(-k*(x-c)))   (optionally inverted)
    /// </summary>
    public readonly struct ResponseCurve
    {
        enum Kind { Linear, Polynomial, Logistic }

        readonly Kind kind;
        readonly float m, k, b, c;
        readonly bool inverted;

        ResponseCurve(Kind kind, float m, float k, float b, float c, bool inverted)
        {
            this.kind = kind; this.m = m; this.k = k; this.b = b; this.c = c; this.inverted = inverted;
        }

        public static ResponseCurve Linear(float slope = 1f, float intercept = 0f)
            => new ResponseCurve(Kind.Linear, slope, 1f, intercept, 0f, false);

        public static ResponseCurve Polynomial(float slope, float exponent, float intercept = 0f, float shift = 0f)
            => new ResponseCurve(Kind.Polynomial, slope, exponent, intercept, shift, false);

        public static ResponseCurve Logistic(float steepness, float midpoint, bool inverted = false)
            => new ResponseCurve(Kind.Logistic, 1f, steepness, 0f, midpoint, inverted);

        public float Evaluate(float x)
        {
            float y;
            switch (kind)
            {
                case Kind.Linear:
                    y = m * x + b;
                    break;
                case Kind.Polynomial:
                    y = m * Mathf.Pow(Mathf.Max(0f, x - c), k) + b;
                    break;
                default:
                    y = 1f / (1f + Mathf.Exp(-k * (x - c)));
                    if (inverted) y = 1f - y;
                    break;
            }
            return Mathf.Clamp01(y);
        }
    }
}
