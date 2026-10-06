using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>The objective. If its integrity reaches zero the outpost falls.</summary>
    [RequireComponent(typeof(Health), typeof(Targetable))]
    public class Reactor : MonoBehaviour
    {
        public static Reactor Instance { get; private set; }

        public float maxIntegrity = 3000f;
        public Renderer[] coreRenderers;
        public Light coreLight;

        public Health Health { get; private set; }
        public Targetable Targetable { get; private set; }

        static readonly Color Healthy = new Color(0.2f, 0.9f, 1f);
        static readonly Color Critical = new Color(1f, 0.2f, 0.1f);
        float lastHit = -99f;

        void Awake()
        {
            Instance = this;
            Health = GetComponent<Health>();
            Health.Setup(maxIntegrity, Team.Defender, true);
            Targetable = GetComponent<Targetable>();
            Targetable.kind = TargetKind.Reactor;
            Targetable.aimHeight = 2.2f;
            Targetable.strategicValue = 1f;
            Targetable.threat = 0.05f;
            Health.Damaged += (h, amt, src) => lastHit = Time.time;
            Health.Died += OnDied;

            var hum = gameObject.AddComponent<AudioSource>();
            hum.clip = SfxLibrary.Get(SfxKind.Hum);
            hum.loop = true;
            hum.spatialBlend = 1f;
            hum.minDistance = 6f;
            hum.maxDistance = 60f;
            hum.rolloffMode = AudioRolloffMode.Linear;
            hum.volume = 0.3f;
            hum.Play();

            ParticleFactory.Motes(transform, new Vector3(0f, 0.6f, 0f), new Color(0.3f, 0.9f, 1f, 0.8f));
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float f = Health.Fraction;
            Color c = Color.Lerp(Critical, Healthy, Mathf.Clamp01((f - 0.15f) / 0.6f));
            float pulse = 1.2f + Mathf.Sin(Time.time * (2f + (1f - f) * 8f)) * 0.5f;
            if (Time.time - lastHit < 0.12f) pulse += 1.5f;

            if (coreRenderers != null)
            {
                foreach (var r in coreRenderers)
                {
                    if (r == null) continue;
                    var m = r.material;
                    m.SetColor("_EmissionColor", c * pulse);
                    m.color = Color.Lerp(m.color, c * 0.5f, 0.1f);
                }
            }
            if (coreLight != null)
            {
                coreLight.color = c;
                coreLight.intensity = 2.5f * pulse * Mathf.Lerp(0.4f, 1f, f);
            }
        }

        void OnDied(Health h, GameObject src)
        {
            Fx.Burst(transform.position + Vector3.up * 2f, new Color(1f, 0.6f, 0.2f), 14f, 1.4f);
            Fx.Sound(SfxKind.Explosion, transform.position, 1f);
            GameManager.Instance?.Lose("The reactor was destroyed.");
        }

        public void Repair(float hp) => Health.Heal(hp);
    }
}
