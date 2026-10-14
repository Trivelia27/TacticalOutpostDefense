using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>
    /// Imported glTF models ship with the glTF shader graph, whose metallic / reflection response renders almost black in this
    /// scene's lighting. This keeps each model's base colour and texture but re-creates the material on the URP Lit shader
    /// (matte, slightly glossy), the same shader the rest of the level uses. Converted materials are cached.
    /// </summary>
    public static class ModelMaterials
    {
        static readonly Dictionary<Material, Material> cache = new Dictionary<Material, Material>();
        static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
        static readonly int BaseColorTexture = Shader.PropertyToID("baseColorTexture");
        static readonly int EmissiveFactor = Shader.PropertyToID("emissiveFactor");
        static readonly int RoughnessFactor = Shader.PropertyToID("roughnessFactor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cache.Clear();

        public static Material ToLit(Material src, float smoothnessScale = 0.35f)
        {
            if (src == null) return null;
            if (src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline")) return src;
            if (cache.TryGetValue(src, out var done) && done != null) return done;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return src;

            var m = new Material(shader) { name = src.name + "_Lit" };
            Color color = src.HasProperty(BaseColorFactor) ? src.GetColor(BaseColorFactor) : Color.white;
            m.SetColor("_BaseColor", color);
            if (src.HasProperty(BaseColorTexture))
            {
                var tex = src.GetTexture(BaseColorTexture);
                if (tex != null) m.SetTexture("_BaseMap", tex);
            }
            float rough = src.HasProperty(RoughnessFactor) ? src.GetFloat(RoughnessFactor) : 0.7f;
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", Mathf.Clamp01((1f - rough) * smoothnessScale));
            if (src.HasProperty(EmissiveFactor))
            {
                var e = src.GetColor(EmissiveFactor);
                if (e.maxColorComponent > 0.01f)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", e);
                }
            }
            cache[src] = m;
            return m;
        }

        /// <summary>Replaces every material under <paramref name="root"/> with its URP Lit equivalent.</summary>
        public static void Apply(GameObject root, float smoothnessScale = 0.35f)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var lit = ToLit(mats[i], smoothnessScale);
                    if (lit != mats[i]) { mats[i] = lit; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
