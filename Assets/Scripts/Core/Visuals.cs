using System.Collections.Generic;
using UnityEngine;

namespace TacticalOutpost
{
    /// <summary>Runtime material helper (characters and effects are built from primitives). Targets URP/Lit.</summary>
    public static class Visuals
    {
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Shader litShader;
        static Shader unlitShader;

        static Shader LitShader()
        {
            if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");
            return litShader;
        }

        static Shader UnlitShader()
        {
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
            return unlitShader;
        }

        public static void SetBaseColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetSmoothness(Material m, float s)
        {
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", s);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", s);
        }

        public static Material Lit(Color color, float emission = 0f, float smoothness = 0.25f, float metallic = 0f)
        {
            string key = $"L{color.r:F2}{color.g:F2}{color.b:F2}{color.a:F2}|{emission:F1}|{smoothness:F2}|{metallic:F2}";
            if (cache.TryGetValue(key, out var existing) && existing != null) return existing;

            var m = new Material(LitShader()) { name = "RT_Lit_" + key };
            SetBaseColor(m, color);
            SetSmoothness(m, smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor(EmissionId, color * emission);
            }
            cache[key] = m;
            return m;
        }

        public static Material Unlit(Color color)
        {
            string key = $"U{color.r:F2}{color.g:F2}{color.b:F2}{color.a:F2}";
            if (cache.TryGetValue(key, out var existing) && existing != null) return existing;

            var m = new Material(UnlitShader()) { name = "RT_Unlit_" + key, color = color };
            cache[key] = m;
            return m;
        }

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos,
                                           Vector3 localScale, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (!keepCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (mat != null) r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return go;
        }
    }
}
