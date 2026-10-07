using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Burning oil drum: flames, smoke and a flickering warm light.</summary>
    public class FireEffect : MonoBehaviour
    {
        Light fireLight;
        float seed;

        void Start()
        {
            seed = Random.value * 100f;
            ParticleFactory.Fire(transform, Vector3.up * 1.05f);
            var go = new GameObject("FireLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1.6f;
            fireLight = go.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.6f, 0.25f);
            fireLight.range = 7f;
            fireLight.shadows = LightShadows.None;
        }

        void Update()
        {
            if (fireLight == null) return;
            float n = Mathf.PerlinNoise(Time.time * 6f, seed);
            fireLight.intensity = 1.6f + n * 1.8f;
        }
    }
}
