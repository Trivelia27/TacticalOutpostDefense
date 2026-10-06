using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    public enum SfxKind { Rifle, Smg, Sniper, Heavy, Turret, Pistol, Hit, Explosion, Heal, Reload, Build, Alert, Wind, Hum }

    /// <summary>Generates every sound effect procedurally so the project needs no audio assets.</summary>
    public static class SfxLibrary
    {
        const int SampleRate = 22050;
        static readonly Dictionary<SfxKind, AudioClip> clips = new Dictionary<SfxKind, AudioClip>();
        static System.Random rng = new System.Random(1234);

        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Env(float t, float decay) => Mathf.Exp(-t * decay);

        public static AudioClip Get(SfxKind kind)
        {
            if (clips.TryGetValue(kind, out var c) && c != null) return c;
            c = Create(kind);
            clips[kind] = c;
            return c;
        }

        static AudioClip Create(SfxKind kind)
        {
            switch (kind)
            {
                case SfxKind.Rifle:
                    return Gen("rifle", 0.18f, t => (Noise() * Env(t, 55f) * 0.7f + Sin(140f, t) * Env(t, 30f) * 0.6f) * 0.8f);
                case SfxKind.Smg:
                    return Gen("smg", 0.12f, t => (Noise() * Env(t, 80f) * 0.6f + Sin(220f, t) * Env(t, 50f) * 0.4f) * 0.8f);
                case SfxKind.Pistol:
                    return Gen("pistol", 0.14f, t => (Noise() * Env(t, 70f) * 0.55f + Sin(260f, t) * Env(t, 45f) * 0.35f) * 0.8f);
                case SfxKind.Sniper:
                    return Gen("sniper", 0.6f, t => (Noise() * Env(t, 12f) * 0.8f + Sin(90f - 60f * t, t) * Env(t, 8f) * 0.8f) * 0.9f);
                case SfxKind.Heavy:
                    return Gen("heavy", 0.22f, t => (Noise() * Env(t, 35f) * 0.7f + Sin(80f, t) * Env(t, 18f) * 0.7f) * 0.85f);
                case SfxKind.Turret:
                    return Gen("turret", 0.12f, t => Sin(300f, t) * Env(t, 45f) * 0.5f + Noise() * Env(t, 70f) * 0.4f);
                case SfxKind.Hit:
                    return Gen("hit", 0.06f, t => Noise() * Env(t, 120f) * 0.5f);
                case SfxKind.Explosion:
                    return Gen("explosion", 1.1f, t => (Noise() * Env(t, 4f) * 0.9f + Sin(50f, t) * Env(t, 5f) * 0.8f) * 0.9f);
                case SfxKind.Heal:
                    return Gen("heal", 0.35f, t => Sin(500f + 600f * t, t) * Env(t, 4f) * 0.35f);
                case SfxKind.Reload:
                    return Gen("reload", 0.25f, t => (Noise() * (Env(t, 150f) + (t > 0.12f ? Env(t - 0.12f, 150f) : 0f)) * 0.5f));
                case SfxKind.Build:
                    return Gen("build", 0.4f, t => Sin(300f + 800f * t, t) * Env(t, 5f) * 0.3f);
                case SfxKind.Wind:
                    return GenWind();
                case SfxKind.Hum:
                    return Gen("hum", 2f, t => (Sin(55f, t) * 0.5f + Sin(110f, t) * 0.25f + Sin(165f, t) * 0.1f) * (0.8f + 0.2f * Sin(0.5f, t)));
                default:
                    return Gen("alert", 0.5f, t => (t % 0.25f) < 0.12f ? Sin(880f, t) * 0.3f : 0f);
            }
        }

        /// <summary>Seamless looping wind: low-passed noise with slow gusts.</summary>
        static AudioClip GenWind()
        {
            const float duration = 6f;
            int n = (int)(SampleRate * duration);
            var data = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                lp += (Noise() - lp) * 0.02f;
                lp2 += (lp - lp2) * 0.15f;
                float gust = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * t / duration * 2f);
                data[i] = lp2 * 9f * gust;
            }
            // Crossfade the tail into the head so the loop has no click.
            int fade = SampleRate / 2;
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] = Mathf.Lerp(data[n - fade + i], data[i], k);
            }
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            var clip = AudioClip.Create("wind", n - fade, 1, SampleRate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, n - fade);
            clip.SetData(trimmed, 0);
            return clip;
        }

        static AudioClip Gen(string name, float duration, Func<float, float> sample)
        {
            int n = Mathf.Max(1, (int)(SampleRate * duration));
            var data = new float[n];
            for (int i = 0; i < n; i++)
                data[i] = Mathf.Clamp(sample(i / (float)SampleRate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
