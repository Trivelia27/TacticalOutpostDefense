using System.IO;
using UnityEditor;
using UnityEngine;

namespace TacticalOutpost.Editor
{
    /// <summary>
    /// Generates seamless tiling albedo + normal-map textures (sand, concrete, wood, asphalt, cloth)
    /// with periodic value noise, so the level has real surface detail without external assets.
    /// </summary>
    public static class ProceduralTextures
    {
        const string Dir = "Assets/Textures";
        const int Size = 512;

        public sealed class Set
        {
            public Texture2D Sand, SandNormal, Concrete, ConcreteNormal, Wood, WoodNormal, Asphalt, AsphaltNormal, Cloth, ClothNormal;
        }

        // ---- periodic value noise (wraps every 'freq' lattice cells so textures tile) ----

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        static float Noise(float u, float v, int freq, int seed)
        {
            float x = u * freq, y = v * freq;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = ((x0 % freq) + freq) % freq, xb = (xa + 1) % freq;
            int ya = ((y0 % freq) + freq) % freq, yb = (ya + 1) % freq;
            float a = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx);
            float b = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        static float Fbm(float u, float v, int baseFreq, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int f = baseFreq;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(u, v, f, seed + i * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                f *= 2;
            }
            return sum / norm;
        }

        delegate float HeightFn(float u, float v);

        static void Bake(string name, HeightFn height, System.Func<float, float, float, Color> albedo, float normalStrength, Texture2D[] outputs)
        {
            var h = new float[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    h[y * Size + x] = height(x / (float)Size, y / (float)Size);

            var col = new Color[Size * Size];
            var nrm = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float hv = h[y * Size + x];
                    col[y * Size + x] = albedo(u, v, hv);

                    float hl = h[y * Size + (x + Size - 1) % Size], hr = h[y * Size + (x + 1) % Size];
                    float hd = h[((y + Size - 1) % Size) * Size + x], hu = h[((y + 1) % Size) * Size + x];
                    var n = new Vector3((hl - hr) * normalStrength, (hd - hu) * normalStrength, 1f).normalized;
                    nrm[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }

            outputs[0] = Save(name, col, false);
            outputs[1] = Save(name + "_N", nrm, true);
        }

        static Texture2D Save(string name, Color[] pixels, bool normal)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, normal);
            tex.SetPixels(pixels);
            tex.Apply();
            string path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 8;
            imp.filterMode = FilterMode.Trilinear;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Set Generate()
        {
            Directory.CreateDirectory(Dir);
            var set = new Set();
            var o = new Texture2D[2];

            // Sand: soft dunes + ripples + grain
            Bake("T_Sand", (u, v) =>
            {
                float ripple = Mathf.Sin((v + Fbm(u, v, 4, 3, 5) * 0.35f) * Mathf.PI * 2f * 24f) * 0.5f + 0.5f;
                return Fbm(u, v, 6, 4, 11) * 0.55f + ripple * 0.12f + Noise(u, v, 256, 3) * 0.12f;
            }, (u, v, h) =>
            {
                float g = Mathf.Lerp(0.72f, 1.0f, h);
                return new Color(g, g * 0.985f, g * 0.95f, 1f);
            }, 3.2f, o);
            set.Sand = o[0]; set.SandNormal = o[1];

            // Concrete: mottled with pits and fine speckle
            Bake("T_Concrete", (u, v) =>
            {
                float pits = Noise(u, v, 128, 9) > 0.9f ? -0.25f : 0f;
                return Fbm(u, v, 8, 5, 21) * 0.8f + Noise(u, v, 256, 4) * 0.15f + pits;
            }, (u, v, h) =>
            {
                float stain = Fbm(u, v, 3, 3, 31);
                float g = Mathf.Lerp(0.66f, 1.0f, Mathf.Clamp01(h)) * Mathf.Lerp(0.88f, 1f, stain);
                return new Color(g, g, g * 0.985f, 1f);
            }, 2.2f, o);
            set.Concrete = o[0]; set.ConcreteNormal = o[1];

            // Wood planks: vertical boards with grain and seams
            Bake("T_Wood", (u, v) =>
            {
                float plank = Mathf.Floor(u * 5f);
                float seam = Mathf.Abs(Mathf.Repeat(u * 5f, 1f) - 0.5f) > 0.475f ? -0.4f : 0f;
                float grain = Mathf.Sin((v * 3f + Noise(u * 5f % 1f, v, 4, (int)plank * 7) * 0.5f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                return grain * 0.12f + Fbm(u, v, 16, 3, (int)plank + 3) * 0.35f + seam + 0.35f;
            }, (u, v, h) =>
            {
                float plank = Mathf.Floor(u * 5f);
                float tone = Mathf.Lerp(0.75f, 1.05f, Hash((int)plank, 0, 77));
                float g = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(h)) * tone;
                return new Color(g, g * 0.95f, g * 0.88f, 1f);
            }, 1.0f, o);
            set.Wood = o[0]; set.WoodNormal = o[1];

            // Asphalt: dark with aggregate speckle and cracks
            Bake("T_Asphalt", (u, v) =>
            {
                float crack = Mathf.Abs(Fbm(u, v, 4, 3, 41) - 0.5f) < 0.012f ? -0.5f : 0f;
                return Noise(u, v, 256, 6) * 0.5f + Fbm(u, v, 16, 3, 51) * 0.4f + crack;
            }, (u, v, h) =>
            {
                float g = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(h));
                return new Color(g, g, g, 1f);
            }, 2.8f, o);
            set.Asphalt = o[0]; set.AsphaltNormal = o[1];

            // Cloth / burlap weave for sandbags and canvas
            Bake("T_Cloth", (u, v) =>
            {
                float wx = Mathf.Sin(u * Mathf.PI * 2f * 16f) * 0.5f + 0.5f;
                float wy = Mathf.Sin(v * Mathf.PI * 2f * 16f) * 0.5f + 0.5f;
                return wx * wy * 0.5f + Fbm(u, v, 8, 3, 61) * 0.45f;
            }, (u, v, h) =>
            {
                float g = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(h));
                return new Color(g, g, g * 0.97f, 1f);
            }, 1.8f, o);
            set.Cloth = o[0]; set.ClothNormal = o[1];

            AssetDatabase.SaveAssets();
            return set;
        }
    }
}
