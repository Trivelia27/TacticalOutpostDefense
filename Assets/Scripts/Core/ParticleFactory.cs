using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Builds ambient particle systems (reactor motes, fire, smoke, dust) at runtime – no particle assets needed.</summary>
    public static class ParticleFactory
    {
        static Texture2D soft;
        static Material material;

        static Texture2D SoftTexture()
        {
            if (soft != null) return soft;
            const int n = 48;
            soft = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "SoftCircle" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(n * 0.5f - 0.5f, n * 0.5f - 0.5f)) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            soft.SetPixels(px);
            soft.Apply();
            return soft;
        }

        static Material Mat()
        {
            if (material != null) return material;
            material = new Material(Shader.Find("Sprites/Default")) { name = "RT_Particles" };
            material.mainTexture = SoftTexture();
            return material;
        }

        static ParticleSystem Create(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void FadeOut(ParticleSystem ps, float peakAlpha = 1f)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        /// <summary>Glowing motes drifting upward (reactor).</summary>
        public static ParticleSystem Motes(Transform parent, Vector3 pos, Color color)
        {
            var ps = Create("Motes", parent, pos);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.6f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var em = ps.emission; em.rateOverTime = 22f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 2.4f; sh.rotation = new Vector3(-90f, 0f, 0f);
            FadeOut(ps, 0.9f);
            ps.Play();
            return ps;
        }

        /// <summary>Flames + embers for a burning drum.</summary>
        public static void Fire(Transform parent, Vector3 pos)
        {
            var fire = Create("Flames", parent, pos);
            var main = fire.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.1f, 0.9f), new Color(1f, 0.85f, 0.3f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.15f;
            var em = fire.emission; em.rateOverTime = 28f;
            var sh = fire.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.22f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var sz = fire.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
            FadeOut(fire, 0.85f);
            fire.Play();

            var smoke = Create("Smoke", parent, pos + Vector3.up * 0.5f);
            var sm = smoke.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.15f, 0.15f, 0.55f), new Color(0.3f, 0.28f, 0.26f, 0.45f));
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.gravityModifier = -0.05f;
            var sem = smoke.emission; sem.rateOverTime = 7f;
            var ssh = smoke.shape; ssh.shapeType = ParticleSystemShapeType.Circle; ssh.radius = 0.2f; ssh.rotation = new Vector3(-90f, 0f, 0f);
            var ssz = smoke.sizeOverLifetime; ssz.enabled = true;
            ssz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.6f)));
            FadeOut(smoke, 0.5f);
            smoke.Play();
        }

        /// <summary>Wind-blown dust that follows the camera.</summary>
        public static ParticleSystem Dust(Transform parent)
        {
            var ps = Create("Dust", parent, new Vector3(0f, 0f, 14f));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.87f, 0.7f, 0.45f), new Color(1f, 0.95f, 0.85f, 0.3f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            var em = ps.emission; em.rateOverTime = 36f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(34f, 8f, 26f);
            var vel = ps.velocityOverLifetime; vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.1f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.4f);
            FadeOut(ps, 0.8f);
            ps.Play();
            return ps;
        }
    }
}
