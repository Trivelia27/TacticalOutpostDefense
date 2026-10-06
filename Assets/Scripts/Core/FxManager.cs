using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Static facade so gameplay code never has to null-check the manager.</summary>
    public static class Fx
    {
        public static void Tracer(Vector3 a, Vector3 b, Color c, float width = 0.04f, float life = 0.07f)
            => FxManager.Get()?.AddTracer(a, b, c, width, life);

        public static void Spark(Vector3 p, Color c, float size = 0.25f, float life = 0.12f)
            => FxManager.Get()?.AddBlob(p, c, size, life, false);

        public static void Burst(Vector3 p, Color c, float size = 3f, float life = 0.6f)
            => FxManager.Get()?.AddBlob(p, c, size, life, true);

        public static void Sound(SfxKind kind, Vector3 p, float volume = 1f, float pitch = 1f)
            => FxManager.Get()?.PlaySound(kind, p, volume, pitch);

        public static void MuzzleFlash(Vector3 p)
        {
            var m = FxManager.Get();
            if (m == null) return;
            m.AddBlob(p, new Color(1f, 0.85f, 0.45f), 0.2f, 0.05f, false);
            m.FlashLight(p, new Color(1f, 0.75f, 0.35f), 2.0f, 5f, 0.05f);
        }
    }

    /// <summary>Pooled tracers, sparks, explosion blobs and positional one-shot audio.</summary>
    public class FxManager : MonoBehaviour
    {
        class TracerItem { public LineRenderer line; public float age, life; public Color color; }
        class BlobItem
        {
            public Transform t; public Renderer r; public float age, life, size; public bool grow; public Color color;
            public MaterialPropertyBlock block = new MaterialPropertyBlock();
        }

        static FxManager instance;
        static bool quitting;

        readonly List<TracerItem> tracers = new List<TracerItem>();
        readonly List<BlobItem> blobs = new List<BlobItem>();
        AudioSource[] sources;
        int nextSource;

        class LightItem { public Light light; public float age, life, peak; }
        readonly List<LightItem> lights = new List<LightItem>();
        const int MaxLights = 5;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public static FxManager Get()
        {
            if (instance != null) return instance;
            if (quitting) return null;
            var go = new GameObject("[FX]");
            instance = go.AddComponent<FxManager>();
            return instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            quitting = false;
        }

        void Awake()
        {
            instance = this;
            sources = new AudioSource[14];
            for (int i = 0; i < sources.Length; i++)
            {
                var child = new GameObject("Sfx" + i);
                child.transform.SetParent(transform, false);
                var s = child.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 8f;
                s.maxDistance = 90f;
                sources[i] = s;
            }

            var amb = new GameObject("Ambience").AddComponent<AudioSource>();
            amb.transform.SetParent(transform, false);
            amb.clip = SfxLibrary.Get(SfxKind.Wind);
            amb.loop = true;
            amb.spatialBlend = 0f;
            amb.volume = 0.22f;
            amb.Play();
        }

        public void FlashLight(Vector3 pos, Color color, float intensity, float range, float life)
        {
            LightItem item = null;
            for (int i = 0; i < lights.Count; i++)
                if (!lights[i].light.enabled) { item = lights[i]; break; }
            if (item == null)
            {
                if (lights.Count >= MaxLights) return;
                var go = new GameObject("FlashLight");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                item = new LightItem { light = l };
                lights.Add(item);
            }
            item.age = 0f;
            item.life = life;
            item.peak = intensity;
            item.light.color = color;
            item.light.range = range;
            item.light.intensity = intensity;
            item.light.transform.position = pos;
            item.light.enabled = true;
        }

        void OnApplicationQuit() => quitting = true;

        public void PlaySound(SfxKind kind, Vector3 pos, float volume, float pitch)
        {
            if (sources == null) return;
            var s = sources[nextSource];
            nextSource = (nextSource + 1) % sources.Length;
            s.transform.position = pos;
            s.clip = SfxLibrary.Get(kind);
            s.volume = Mathf.Clamp01(volume);
            s.pitch = pitch;
            s.Play();
        }

        public void AddTracer(Vector3 a, Vector3 b, Color c, float width, float life)
        {
            TracerItem item = null;
            for (int i = 0; i < tracers.Count; i++)
            {
                if (!tracers[i].line.enabled) { item = tracers[i]; break; }
            }
            if (item == null)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = Visuals.Unlit(Color.white);
                item = new TracerItem { line = lr };
                tracers.Add(item);
            }
            item.age = 0f;
            item.life = Mathf.Max(0.01f, life);
            item.color = c;
            item.line.startWidth = width;
            item.line.endWidth = width * 0.6f;
            item.line.SetPosition(0, a);
            item.line.SetPosition(1, b);
            item.line.startColor = c;
            item.line.endColor = c;
            item.line.enabled = true;
        }

        public void AddBlob(Vector3 pos, Color c, float size, float life, bool grow)
        {
            BlobItem item = null;
            for (int i = 0; i < blobs.Count; i++)
            {
                if (!blobs[i].r.enabled) { item = blobs[i]; break; }
            }
            if (item == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Blob";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = Visuals.Unlit(Color.white);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                item = new BlobItem { t = go.transform, r = r };
                blobs.Add(item);
            }
            item.age = 0f;
            item.life = Mathf.Max(0.02f, life);
            item.size = size;
            item.grow = grow;
            item.color = c;
            item.t.position = pos;
            item.t.localScale = Vector3.one * (grow ? size * 0.3f : size);
            item.r.enabled = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < tracers.Count; i++)
            {
                var t = tracers[i];
                if (!t.line.enabled) continue;
                t.age += dt;
                if (t.age >= t.life) { t.line.enabled = false; continue; }
                var c = t.color;
                c.a = 1f - t.age / t.life;
                t.line.startColor = c;
                t.line.endColor = c;
            }

            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                if (!l.light.enabled) continue;
                l.age += dt;
                if (l.age >= l.life) { l.light.enabled = false; continue; }
                l.light.intensity = l.peak * (1f - l.age / l.life);
            }

            for (int i = 0; i < blobs.Count; i++)
            {
                var b = blobs[i];
                if (!b.r.enabled) continue;
                b.age += dt;
                if (b.age >= b.life) { b.r.enabled = false; continue; }
                float k = b.age / b.life;
                b.t.localScale = Vector3.one * (b.grow ? b.size * (0.3f + 0.7f * k) : b.size * (1f - k));
                var c = b.color;
                c.a = (1f - k) * (b.grow ? 0.7f : 1f);
                b.block.SetColor(ColorId, c);
                b.r.SetPropertyBlock(b.block);
            }
        }
    }
}
