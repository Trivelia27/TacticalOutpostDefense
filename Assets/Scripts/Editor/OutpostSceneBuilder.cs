using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TacticalOutpost.Editor
{
    /// <summary>
    /// Generates the whole playable level (Desert Military Outpost) as a normal Unity scene so every
    /// object can still be inspected / edited by hand afterwards.
    /// Menu: Tools ▸ Tactical Outpost ▸ Build Outpost Scene
    /// </summary>
    public static class OutpostSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Outpost_Desert.unity";
        const string MatDir = "Assets/Materials";
        const string GenDir = "Assets/Materials/Generated";

        static readonly Vector3 SandbagSize = new Vector3(3.4f, 1.3f, 1.0f);
        static readonly Vector3 CrateSize = new Vector3(1.7f, 1.3f, 1.7f);
        static readonly Vector3 BarrierSize = new Vector3(3.0f, 1.3f, 0.9f);

        static readonly List<Bounds> placed = new List<Bounds>();
        static readonly Dictionary<string, Material> tiledCache = new Dictionary<string, Material>();
        static Mats M;

        [MenuItem("Tools/Tactical Outpost/Build Outpost Scene")]
        public static void BuildFromMenu() => Build();

        /// <summary>Entry point for -executeMethod in batch mode.</summary>
        public static void BuildBatch()
        {
            try { Build(); }
            catch (Exception e)
            {
                Debug.LogError("[OutpostSceneBuilder] " + e);
                EditorApplication.Exit(1);
            }
        }

        // =====================================================================

        static void Build()
        {
            LayerSetup.EnsureLayers();
            RenderSetup.Setup();
            ConfigureProject();
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(GenDir);
            Directory.CreateDirectory("Assets/Scenes");

            var tex = ProceduralTextures.Generate();
            M = new Mats(tex);
            placed.Clear();
            tiledCache.Clear();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            var env = new GameObject("Environment").transform;
            var cover = new GameObject("Cover").transform;
            var props = new GameObject("Props").transform;

            BuildGround(env);
            BuildBorder(env);
            BuildCompound(env);
            BuildCoverField(cover);
            BuildBuildings(env);
            BuildDecor(props);
            BuildScatter(props);

            var reactor = BuildReactor();
            var posts = BuildTurretPosts();
            var player = BuildPlayer();
            var spawns = BuildSpawnPoints();
            BuildCamera(player);
            BuildPostProcessing();
            BuildManagers(spawns);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[OutpostSceneBuilder] Scene saved to {ScenePath} (reactor={reactor.name}, turret posts={posts.Length}, spawn points={spawns.Length}).");
        }

        static void ConfigureProject()
        {
            PlayerSettings.companyName = "GameCerdas";
            PlayerSettings.productName = "Tactical Outpost Defense";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
        }

        // =====================================================================
        // Materials (URP/Lit)
        // =====================================================================

        sealed class Mats
        {
            public Material Sand, SandDark, Asphalt, Concrete, ConcreteDark, Sandbag, SandbagB, Crate, Olive, Canvas, Metal, MetalDark,
                            Rust, Building, Hazard, Defender, Wreck, Glass, Pebble, Bush, Core, Strip, Lamp, Ghost, PadMark, Stain, Red, Green;

            public Mats(ProceduralTextures.Set t)
            {
                Sand = Make("Sand", new Color(0.8f, 0.67f, 0.46f), 0.04f, t.Sand, t.SandNormal);
                SandDark = Make("SandDark", new Color(0.7f, 0.58f, 0.4f), 0.04f, t.Sand, t.SandNormal);
                Asphalt = Make("Asphalt", new Color(0.3f, 0.3f, 0.32f), 0.12f, t.Asphalt, t.AsphaltNormal);
                Concrete = Make("Concrete", new Color(0.72f, 0.7f, 0.66f), 0.08f, t.Concrete, t.ConcreteNormal);
                ConcreteDark = Make("ConcreteDark", new Color(0.5f, 0.5f, 0.49f), 0.08f, t.Concrete, t.ConcreteNormal);
                Sandbag = Make("Sandbag", new Color(0.66f, 0.58f, 0.42f), 0.03f, t.Cloth, t.ClothNormal);
                SandbagB = Make("SandbagB", new Color(0.56f, 0.5f, 0.37f), 0.03f, t.Cloth, t.ClothNormal);
                Crate = Make("Crate", new Color(0.62f, 0.45f, 0.27f), 0.1f, t.Wood, t.WoodNormal);
                Olive = Make("Olive", new Color(0.33f, 0.38f, 0.24f), 0.15f, t.Cloth, t.ClothNormal);
                Canvas = Make("Canvas", new Color(0.82f, 0.77f, 0.6f), 0.04f, t.Cloth, t.ClothNormal);
                Metal = Make("Metal", new Color(0.3f, 0.32f, 0.35f), 0.55f, null, null, 0.7f);
                MetalDark = Make("MetalDark", new Color(0.13f, 0.14f, 0.15f), 0.4f, null, null, 0.5f);
                Rust = Make("Rust", new Color(0.5f, 0.26f, 0.14f), 0.25f, null, null, 0.4f);
                Building = Make("Building", new Color(0.78f, 0.66f, 0.5f), 0.05f, t.Concrete, t.ConcreteNormal);
                Hazard = Make("Hazard", new Color(0.92f, 0.74f, 0.08f), 0.2f);
                Defender = Make("Defender", new Color(0.18f, 0.45f, 0.85f), 0.45f, null, null, 0.3f);
                Wreck = Make("Wreck", new Color(0.09f, 0.09f, 0.09f), 0.1f);
                Glass = Make("Glass", new Color(0.12f, 0.2f, 0.28f), 0.92f, null, null, 0.2f);
                Pebble = Make("Pebble", new Color(0.62f, 0.56f, 0.46f), 0.1f, t.Concrete, t.ConcreteNormal);
                Bush = Make("DryBush", new Color(0.36f, 0.31f, 0.17f), 0.05f, t.Cloth, t.ClothNormal);
                Red = Make("RedPaint", new Color(0.7f, 0.1f, 0.08f), 0.3f);
                Green = Make("MilGreen", new Color(0.26f, 0.32f, 0.2f), 0.25f);
                Core = MakeEmissive("ReactorCore", new Color(0.2f, 0.9f, 1f), 1.8f);
                Strip = MakeEmissive("LightStrip", new Color(0.3f, 0.9f, 1f), 2.4f);
                Lamp = MakeEmissive("Lamp", new Color(1f, 0.9f, 0.7f), 2.2f);
                Ghost = MakeUnlit("TurretGhost", new Color(0.3f, 0.85f, 1f, 0.35f));
                PadMark = MakeUnlit("PadMark", new Color(0.95f, 0.8f, 0.2f, 0.55f));
                Stain = MakeUnlit("OilStain", new Color(0.12f, 0.09f, 0.06f, 0.22f));
            }
        }

        static Material MakeSky()
        {
            var m = LoadOrCreate($"{MatDir}/DesertSky.mat", Shader.Find("Skybox/Procedural"));
            m.SetFloat("_SunSize", 0.06f);
            m.SetFloat("_SunSizeConvergence", 5f);
            m.SetFloat("_AtmosphereThickness", 0.95f);
            m.SetColor("_SkyTint", new Color(0.5f, 0.56f, 0.68f));
            m.SetColor("_GroundColor", new Color(0.5f, 0.47f, 0.44f));
            m.SetFloat("_Exposure", 1.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Shader UrpLit => Shader.Find("Universal Render Pipeline/Lit");

        static Material LoadOrCreate(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.shader != shader) m.shader = shader;
            return m;
        }

        static Material Make(string name, Color color, float smoothness, Texture2D albedo = null, Texture2D normal = null, float metallic = 0f)
        {
            var m = LoadOrCreate($"{MatDir}/{name}.mat", UrpLit);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_BumpMap", normal);
            if (normal != null) { m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            else m.DisableKeyword("_NORMALMAP");
            m.SetTextureScale("_BaseMap", Vector2.one);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material MakeEmissive(string name, Color color, float intensity)
        {
            var m = Make(name, color * 0.5f, 0.35f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", color * intensity);
            return m;
        }

        static Material MakeUnlit(string name, Color color)
        {
            var m = LoadOrCreate($"{MatDir}/{name}.mat", Shader.Find("Sprites/Default"));
            m.color = color;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A copy of <paramref name="baseMat"/> with a different UV tiling (kept as an asset so the scene can reference it).</summary>
        static Material Tiled(Material baseMat, float tx, float ty)
        {
            tx = Mathf.Max(0.25f, Mathf.Round(tx * 2f) / 2f);
            ty = Mathf.Max(0.25f, Mathf.Round(ty * 2f) / 2f);
            string key = $"{baseMat.name}_{tx}x{ty}";
            if (tiledCache.TryGetValue(key, out var cached)) return cached;
            string path = $"{GenDir}/{key.Replace('.', '_')}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(baseMat);
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(baseMat);
            m.SetTextureScale("_BaseMap", new Vector2(tx, ty));
            m.SetTextureScale("_BumpMap", new Vector2(tx, ty));
            EditorUtility.SetDirty(m);
            tiledCache[key] = m;
            return m;
        }

        // =====================================================================
        // Primitive helpers
        // =====================================================================

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale,
                               Material mat, int layer = 0, bool collider = false, float rotY = 0f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            Vector3 ps = parent != null ? parent.lossyScale : Vector3.one;
            go.transform.localScale = new Vector3(scale.x / ps.x, scale.y / ps.y, scale.z / ps.z);
            go.layer = layer;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            go.isStatic = true;
            return go;
        }

        /// <summary>Primitive placed in the local space of an UNSCALED parent.</summary>
        static GameObject LP(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale,
                             Material mat, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            var c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
            go.isStatic = true;
            return go;
        }

        static Transform VisualRoot(string name, Transform parent, Vector3 worldPos, float rotY)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = worldPos;
            t.rotation = Quaternion.Euler(0f, rotY, 0f);
            return t;
        }

        static void MakeNavBlocker(GameObject go)
        {
            var mod = go.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = 1; // Not Walkable
            go.isStatic = true;
        }

        static GameObject Obstacle(string name, Transform parent, float x, float z, Vector3 size, float rotY,
                                   Material mat, bool asCover)
        {
            var go = Prim(PrimitiveType.Cube, name, parent, new Vector3(x, size.y * 0.5f, z), size, mat,
                          GameLayers.Obstacle, true, rotY);
            MakeNavBlocker(go);
            if (asCover) go.AddComponent<CoverObject>();
            placed.Add(go.GetComponent<Collider>().bounds);
            return go;
        }

        static Material ConcreteFor(Material baseMat, Vector3 size)
            => Tiled(baseMat, Mathf.Max(size.x, size.z) / 3f, size.y / 3f);

        // =====================================================================
        // Environment
        // =====================================================================

        static void BuildLighting()
        {
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.92f, 0.78f);
            light.intensity = 1.75f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.9f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.4f;
            lightGo.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
            RenderSettings.sun = light;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.64f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.46f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.27f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0048f;
            RenderSettings.fogColor = new Color(0.78f, 0.74f, 0.66f);
            RenderSettings.skybox = MakeSky();
        }

        static void BuildGround(Transform env)
        {
            var g = Prim(PrimitiveType.Cube, "Ground", env, new Vector3(0f, -0.5f, 0f), new Vector3(124f, 1f, 124f),
                         Tiled(M.Sand, 31f, 31f), GameLayers.Ground, true);

            Prim(PrimitiveType.Cube, "Pad", env, new Vector3(0f, 0.03f, 0f), new Vector3(29f, 0.06f, 29f), Tiled(M.Concrete, 7f, 7f));
            RoadStrip(env, "Road_N", 0f, 38f, 6f, 46f);
            RoadStrip(env, "Road_E", 38f, 6.5f, 46f, 5f);
            RoadStrip(env, "Road_W", -38f, -0.5f, 46f, 7f);
            RoadStrip(env, "Road_S", -9f, -38f, 6f, 46f);

            for (int i = 0; i < 16; i++)
            {
                float a = i * 25.7f * Mathf.Deg2Rad;
                float r = 20f + (i * 7) % 33;
                Prim(PrimitiveType.Cylinder, "Patch" + i, env, new Vector3(Mathf.Sin(a) * r, 0.012f, Mathf.Cos(a) * r),
                     new Vector3(7f + i % 4 * 2.5f, 0.01f, 7f + i % 3 * 2.5f), Tiled(M.SandDark, 2f, 2f));
            }
            _ = g;
        }

        static void RoadStrip(Transform env, string name, float x, float z, float sx, float sz)
        {
            Prim(PrimitiveType.Cube, name, env, new Vector3(x, 0.02f, z), new Vector3(sx, 0.04f, sz), Tiled(M.Asphalt, sx / 4f, sz / 4f));
            // dashed centre line
            bool alongZ = sz > sx;
            float len = alongZ ? sz : sx;
            for (float t = -len / 2f + 2f; t < len / 2f - 1f; t += 4f)
            {
                Vector3 p = alongZ ? new Vector3(x, 0.05f, z + t) : new Vector3(x + t, 0.05f, z);
                Vector3 s = alongZ ? new Vector3(0.18f, 0.01f, 1.6f) : new Vector3(1.6f, 0.01f, 0.18f);
                Prim(PrimitiveType.Cube, "Dash", env, p, s, M.Hazard);
            }
        }

        static void BuildBorder(Transform env)
        {
            const float d = 58f, len = 120f;
            BorderWall(env, "Border_N", 0f, d, new Vector3(len, 5f, 2f));
            BorderWall(env, "Border_S", 0f, -d, new Vector3(len, 5f, 2f));
            BorderWall(env, "Border_E", d, 0f, new Vector3(2f, 5f, len));
            BorderWall(env, "Border_W", -d, 0f, new Vector3(2f, 5f, len));
        }

        static void BorderWall(Transform env, string name, float x, float z, Vector3 size)
        {
            Obstacle(name, env, x, z, size, 0f, ConcreteFor(M.ConcreteDark, size), false);
            var v = VisualRoot(name + "_Detail", env, new Vector3(x, 0f, z), 0f);
            LP(PrimitiveType.Cube, "Coping", v, new Vector3(0f, size.y + 0.1f, 0f), new Vector3(size.x + 0.2f, 0.2f, size.z + 0.3f), M.Concrete);
        }

        // ---- compound walls --------------------------------------------------

        static void WallX(Transform p, string name, float z, float x0, float x1) => Wall(p, name, (x0 + x1) * 0.5f, z, x1 - x0, 1f, true);
        static void WallZ(Transform p, string name, float x, float z0, float z1) => Wall(p, name, x, (z0 + z1) * 0.5f, 1f, z1 - z0, false);

        static void Wall(Transform parent, string name, float cx, float cz, float sx, float sz, bool alongX)
        {
            var size = new Vector3(sx, 3.2f, sz);
            Obstacle(name, parent, cx, cz, size, 0f, ConcreteFor(M.ConcreteDark, size), true);

            float len = alongX ? sx : sz;
            var v = VisualRoot(name + "_Detail", parent, new Vector3(cx, 0f, cz), 0f);
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            Vector3 coping = alongX ? new Vector3(len + 0.1f, 0.22f, 1.35f) : new Vector3(1.35f, 0.22f, len + 0.1f);
            LP(PrimitiveType.Cube, "Coping", v, new Vector3(0f, 3.31f, 0f), coping, M.Concrete);
            Vector3 skirt = alongX ? new Vector3(len + 0.06f, 0.45f, 1.12f) : new Vector3(1.12f, 0.45f, len + 0.06f);
            LP(PrimitiveType.Cube, "Skirt", v, new Vector3(0f, 0.225f, 0f), skirt, M.ConcreteDark);

            // panel seams
            for (float t = -len / 2f + 3f; t < len / 2f - 1f; t += 3.5f)
            {
                Vector3 s = alongX ? new Vector3(0.05f, 3.0f, 1.04f) : new Vector3(1.04f, 3.0f, 0.05f);
                LP(PrimitiveType.Cube, "Seam", v, along * t + Vector3.up * 1.7f, s, M.MetalDark);
            }

            // barbed wire on top: posts + two strands
            for (float t = -len / 2f + 0.8f; t < len / 2f; t += 2.2f)
                LP(PrimitiveType.Cube, "WirePost", v, along * t + Vector3.up * 3.75f, new Vector3(0.06f, 0.55f, 0.06f), M.MetalDark);
            Vector3 wireEuler = alongX ? new Vector3(0f, 0f, 90f) : new Vector3(90f, 0f, 0f);
            foreach (float y in new[] { 3.62f, 3.9f })
                LP(PrimitiveType.Cylinder, "Wire", v, Vector3.up * y, new Vector3(0.025f, len * 0.5f, 0.025f), M.MetalDark, wireEuler);

            // wall lamps every ~9 m
            for (float t = -len / 2f + 4f; t < len / 2f - 1f; t += 9f)
                LP(PrimitiveType.Cube, "Lamp", v, along * t + Vector3.up * 2.9f + (alongX ? Vector3.forward : Vector3.right) * 0.62f,
                   new Vector3(0.35f, 0.14f, 0.2f), M.Lamp);
        }

        static void BuildCompound(Transform env)
        {
            var walls = new GameObject("CompoundWalls").transform;
            walls.SetParent(env, false);

            WallX(walls, "Wall_N_L", 15f, -15.5f, -3.5f);
            WallX(walls, "Wall_N_R", 15f, 3.5f, 15.5f);
            WallX(walls, "Wall_S_L", -15f, -15.5f, -12f);
            WallX(walls, "Wall_S_R", -15f, -6f, 15.5f);
            WallZ(walls, "Wall_E_L", 15f, -15.5f, 4f);
            WallZ(walls, "Wall_E_R", 15f, 9f, 15.5f);
            WallZ(walls, "Wall_W_L", -15f, -15.5f, -4f);
            WallZ(walls, "Wall_W_R", -15f, 3f, 15.5f);

            // Corner towers with roofs, hazard bands and a radar on the NE tower.
            int ti = 0;
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                {
                    var size = new Vector3(3.4f, 5.4f, 3.4f);
                    Obstacle("Tower" + ti, walls, sx * 15f, sz * 15f, size, 45f, ConcreteFor(M.Concrete, size), false);
                    var v = VisualRoot("Tower" + ti + "_Detail", walls, new Vector3(sx * 15f, 0f, sz * 15f), 45f);
                    LP(PrimitiveType.Cube, "Roof", v, Vector3.up * 5.65f, new Vector3(4.2f, 0.4f, 4.2f), M.Green);
                    LP(PrimitiveType.Cube, "Band", v, Vector3.up * 4.3f, new Vector3(3.5f, 0.35f, 3.5f), M.Hazard);
                    LP(PrimitiveType.Cube, "Band2", v, Vector3.up * 1.0f, new Vector3(3.5f, 0.35f, 3.5f), M.Hazard);
                    LP(PrimitiveType.Cube, "Window", v, new Vector3(0f, 3.0f, 1.71f), new Vector3(1.8f, 0.7f, 0.06f), M.Glass);
                    LP(PrimitiveType.Cube, "Window2", v, new Vector3(1.71f, 3.0f, 0f), new Vector3(0.06f, 0.7f, 1.8f), M.Glass);
                    LP(PrimitiveType.Cylinder, "Mast", v, Vector3.up * 6.6f, new Vector3(0.1f, 1.0f, 0.1f), M.Metal);
                    if (ti == 3)
                    {
                        var dish = LP(PrimitiveType.Cube, "Radar", v, Vector3.up * 7.7f, new Vector3(2.4f, 0.12f, 0.35f), M.Metal);
                        dish.isStatic = false;
                        dish.AddComponent<Spinner>().degreesPerSecond = new Vector3(0f, 55f, 0f);
                    }
                    ti++;
                }

            Prim(PrimitiveType.Cube, "GateMark_N", env, new Vector3(0f, 0.08f, 15f), new Vector3(7f, 0.02f, 0.4f), M.Hazard);
            Prim(PrimitiveType.Cube, "GateMark_S", env, new Vector3(-9f, 0.08f, -15f), new Vector3(6f, 0.02f, 0.4f), M.Hazard);
            Prim(PrimitiveType.Cube, "GateMark_E", env, new Vector3(15f, 0.08f, 6.5f), new Vector3(0.4f, 0.02f, 5f), M.Hazard);
            Prim(PrimitiveType.Cube, "GateMark_W", env, new Vector3(-15f, 0.08f, -0.5f), new Vector3(0.4f, 0.02f, 7f), M.Hazard);
        }

        // ---- cover field ----------------------------------------------------

        struct CoverSpec
        {
            public char Kind; public float X, Z, Rot;
            public CoverSpec(char kind, float x, float z, float rot = 0f) { Kind = kind; X = x; Z = z; Rot = rot; }
        }

        static void BuildCoverField(Transform parent)
        {
            var list = new[]
            {
                new CoverSpec('B', -5.5f, 11f), new CoverSpec('B', 5.5f, 11f),
                new CoverSpec('S', 11.5f, 2.5f, 90f), new CoverSpec('S', 11.5f, 10.5f, 90f),
                new CoverSpec('S', -11.5f, -4.8f, 90f), new CoverSpec('S', -11.5f, 3.8f, 90f),
                new CoverSpec('B', -12.5f, -11.5f), new CoverSpec('B', -5f, -11.5f),
                new CoverSpec('C', 6.5f, -6.5f, 20f), new CoverSpec('C', -6.5f, 6.5f, 20f),
                new CoverSpec('C', 5f, 5f, 35f), new CoverSpec('C', -5f, -5f, 35f),

                new CoverSpec('S', -9f, 24f), new CoverSpec('S', 9f, 25f), new CoverSpec('C', -1.3f, 29f), new CoverSpec('C', 1.3f, 29.4f, 15f),
                new CoverSpec('B', -16f, 32f, 25f), new CoverSpec('B', 16f, 31f, -25f), new CoverSpec('S', 0f, 40f),
                new CoverSpec('C', -21f, 38f), new CoverSpec('C', 21f, 39f), new CoverSpec('S', -27f, 26f, 80f), new CoverSpec('S', 27f, 27f, 100f),
                new CoverSpec('B', -8f, 46f), new CoverSpec('B', 9f, 47f),

                new CoverSpec('S', 26f, 12f, 90f), new CoverSpec('S', 26f, 1f, 90f), new CoverSpec('C', 33f, 6.5f), new CoverSpec('B', 24f, 19f, 60f),
                new CoverSpec('B', 25f, -10f, 120f), new CoverSpec('S', 40f, 14f, 90f), new CoverSpec('S', 41f, -6f, 90f),
                new CoverSpec('C', 34f, -13f), new CoverSpec('C', 34f, 18f), new CoverSpec('B', 46f, -1f, 90f), new CoverSpec('C', 44f, 20f),

                new CoverSpec('S', -14f, -24f), new CoverSpec('S', -2f, -26f), new CoverSpec('B', 8f, -24f, 20f), new CoverSpec('C', -26f, -30f),
                new CoverSpec('C', 0f, -34f), new CoverSpec('S', 14f, -34f), new CoverSpec('S', -9f, -42f), new CoverSpec('B', 22f, -26f, -30f),
                new CoverSpec('C', -20f, -44f), new CoverSpec('B', 4f, -48f),

                new CoverSpec('S', -26f, -6f, 90f), new CoverSpec('C', -26f, 6f), new CoverSpec('C', -27f, -13f), new CoverSpec('B', -24f, 16f, 120f),
                new CoverSpec('B', -24f, -16f, 60f), new CoverSpec('S', -40f, 8f, 90f), new CoverSpec('S', -40f, -10f, 90f),
                new CoverSpec('C', -34f, -1f), new CoverSpec('B', -46f, -2f, 90f), new CoverSpec('C', -44f, 18f),

                new CoverSpec('C', 22f, 22f, 20f), new CoverSpec('S', 29f, 30f, 45f), new CoverSpec('B', 17f, 31f, 45f),
                new CoverSpec('S', -22f, 22f, -45f), new CoverSpec('C', -28f, 32f), new CoverSpec('B', 22f, -22f, 45f),
                new CoverSpec('S', -22f, -22f, 45f), new CoverSpec('C', 28f, -26f, 30f), new CoverSpec('C', -33f, -18f, 10f),
            };

            int i = 0;
            foreach (var c in list)
            {
                switch (c.Kind)
                {
                    case 'S': Sandbags($"Sandbags_{i++}", parent, c.X, c.Z, c.Rot); break;
                    case 'C': CrateStack($"Crate_{i++}", parent, c.X, c.Z, c.Rot); break;
                    default: Barrier($"Barrier_{i++}", parent, c.X, c.Z, c.Rot); break;
                }
            }
        }

        /// <summary>Collision box (hidden renderer) + a stack of lumpy sandbags as visuals.</summary>
        static void Sandbags(string name, Transform parent, float x, float z, float rot)
        {
            var go = Obstacle(name, parent, x, z, SandbagSize, rot, M.Sandbag, true);
            go.GetComponent<MeshRenderer>().enabled = false;

            var v = VisualRoot(name + "_Visual", parent, new Vector3(x, 0f, z), rot);
            var rng = new System.Random((int)(x * 31f + z * 17f));
            const int courses = 4;
            const int perCourse = 4;
            float bagLen = SandbagSize.x / perCourse;
            for (int c = 0; c < courses; c++)
            {
                bool shifted = (c % 2 == 1);
                float offset = shifted ? bagLen * 0.5f : 0f;
                int n = shifted ? perCourse - 1 : perCourse;
                for (int b = 0; b < n; b++)
                {
                    float lx = -SandbagSize.x * 0.5f + bagLen * 0.5f + offset + b * bagLen;
                    float ly = 0.17f + c * 0.3f;
                    float jitter = (float)(rng.NextDouble() - 0.5) * 0.06f;
                    var bag = LP(PrimitiveType.Sphere, "Bag", v, new Vector3(lx, ly, jitter),
                                 new Vector3(bagLen * 1.12f, 0.36f, SandbagSize.z * 0.9f), (b + c) % 2 == 0 ? M.Sandbag : M.SandbagB,
                                 new Vector3((float)(rng.NextDouble() - 0.5) * 5f, (float)(rng.NextDouble() - 0.5) * 7f, (float)(rng.NextDouble() - 0.5) * 4f));
                    bag.isStatic = true;
                }
            }
        }

        static void CrateStack(string name, Transform parent, float x, float z, float rot)
        {
            var size = CrateSize;
            var wood = Tiled(M.Crate, 1f, 1f);
            var go = Obstacle(name, parent, x, z, size, rot, wood, true);

            var v = VisualRoot(name + "_Detail", parent, new Vector3(x, 0f, z), rot);
            float h = size.y;
            LP(PrimitiveType.Cube, "FrameBottom", v, new Vector3(0f, 0.04f, 0f), new Vector3(size.x + 0.04f, 0.08f, size.z + 0.04f), M.MetalDark);
            foreach (var sx in new[] { -1f, 1f })
            {
                LP(PrimitiveType.Cube, "RimX", v, new Vector3(sx * (size.x * 0.5f), h - 0.04f, 0f), new Vector3(0.1f, 0.08f, size.z + 0.05f), M.MetalDark);
                LP(PrimitiveType.Cube, "RimZ", v, new Vector3(0f, h - 0.04f, sx * (size.z * 0.5f)), new Vector3(size.x + 0.05f, 0.08f, 0.1f), M.MetalDark);
            }
            LP(PrimitiveType.Cube, "Brace", v, new Vector3(0f, h - 0.03f, 0f), new Vector3(0.12f, 0.02f, size.z - 0.1f), M.MetalDark);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    LP(PrimitiveType.Cube, "Post", v, new Vector3(sx * (size.x * 0.5f), h * 0.5f, sz * (size.z * 0.5f)), new Vector3(0.1f, h, 0.1f), M.MetalDark);
            LP(PrimitiveType.Cube, "Stencil", v, new Vector3(0f, h * 0.55f, size.z * 0.5f + 0.012f), new Vector3(0.7f, 0.28f, 0.01f), M.Canvas);
            LP(PrimitiveType.Cube, "Stencil2", v, new Vector3(size.x * 0.5f + 0.012f, h * 0.55f, 0f), new Vector3(0.01f, 0.28f, 0.7f), M.Hazard);
        }

        static void Barrier(string name, Transform parent, float x, float z, float rot)
        {
            var go = Obstacle(name, parent, x, z, BarrierSize, rot, M.Concrete, true);
            go.GetComponent<MeshRenderer>().enabled = false;

            var v = VisualRoot(name + "_Visual", parent, new Vector3(x, 0f, z), rot);
            var mat = Tiled(M.Concrete, 1f, 0.5f);
            float w = BarrierSize.x;
            LP(PrimitiveType.Cube, "Base", v, new Vector3(0f, 0.25f, 0f), new Vector3(w, 0.5f, 0.9f), mat);
            LP(PrimitiveType.Cube, "Mid", v, new Vector3(0f, 0.72f, 0f), new Vector3(w, 0.46f, 0.64f), mat);
            LP(PrimitiveType.Cube, "Top", v, new Vector3(0f, 1.1f, 0f), new Vector3(w, 0.4f, 0.4f), mat);
            LP(PrimitiveType.Cube, "TopCap", v, new Vector3(0f, 1.31f, 0f), new Vector3(w + 0.02f, 0.05f, 0.44f), M.ConcreteDark);
            LP(PrimitiveType.Cube, "StripeA", v, new Vector3(-0.8f, 0.72f, 0.33f), new Vector3(0.5f, 0.3f, 0.02f), M.Hazard, new Vector3(0f, 0f, 25f));
            LP(PrimitiveType.Cube, "StripeB", v, new Vector3(0.8f, 0.72f, 0.33f), new Vector3(0.5f, 0.3f, 0.02f), M.Hazard, new Vector3(0f, 0f, 25f));
            LP(PrimitiveType.Cube, "Reflector", v, new Vector3(0f, 0.72f, 0.335f), new Vector3(0.14f, 0.1f, 0.015f), M.Red);
        }

        // ---- buildings --------------------------------------------------------

        static void BuildBuildings(Transform env)
        {
            var specs = new[]
            {
                new Vector4(-34f, 22f, 7f, 6f), new Vector4(34f, 24f, 6f, 7f),
                new Vector4(30f, -34f, 7f, 6f), new Vector4(-36f, -28f, 6f, 7f),
            };
            int i = 0;
            foreach (var s in specs)
            {
                float w = s.z, d = s.w, h = 3.6f;
                var size = new Vector3(w, h, d);
                Obstacle($"Building_{i}", env, s.x, s.y, size, 0f, ConcreteFor(M.Building, size), false);
                var v = VisualRoot($"Building_{i}_Detail", env, new Vector3(s.x, 0f, s.y), 0f);

                LP(PrimitiveType.Cube, "Roof", v, new Vector3(0f, h + 0.15f, 0f), new Vector3(w + 0.5f, 0.3f, d + 0.5f), M.ConcreteDark);
                LP(PrimitiveType.Cube, "ParapetN", v, new Vector3(0f, h + 0.55f, d * 0.5f + 0.1f), new Vector3(w + 0.5f, 0.5f, 0.18f), M.Building);
                LP(PrimitiveType.Cube, "ParapetS", v, new Vector3(0f, h + 0.55f, -d * 0.5f - 0.1f), new Vector3(w + 0.5f, 0.5f, 0.18f), M.Building);
                LP(PrimitiveType.Cube, "ParapetE", v, new Vector3(w * 0.5f + 0.1f, h + 0.55f, 0f), new Vector3(0.18f, 0.5f, d + 0.5f), M.Building);
                LP(PrimitiveType.Cube, "ParapetW", v, new Vector3(-w * 0.5f - 0.1f, h + 0.55f, 0f), new Vector3(0.18f, 0.5f, d + 0.5f), M.Building);
                LP(PrimitiveType.Cube, "Plinth", v, new Vector3(0f, 0.2f, 0f), new Vector3(w + 0.2f, 0.4f, d + 0.2f), M.ConcreteDark);

                // door + windows (front = +Z), side windows on +/-X
                LP(PrimitiveType.Cube, "Door", v, new Vector3(0f, 1.1f, d * 0.5f + 0.04f), new Vector3(1.3f, 2.2f, 0.1f), M.MetalDark);
                LP(PrimitiveType.Cube, "DoorLight", v, new Vector3(0.9f, 2.5f, d * 0.5f + 0.1f), new Vector3(0.2f, 0.12f, 0.1f), M.Lamp);
                foreach (var wx in new[] { -w * 0.3f, w * 0.3f })
                {
                    LP(PrimitiveType.Cube, "Window", v, new Vector3(wx, 2.3f, d * 0.5f + 0.03f), new Vector3(1.1f, 0.9f, 0.08f), M.Glass);
                    LP(PrimitiveType.Cube, "Sill", v, new Vector3(wx, 1.8f, d * 0.5f + 0.08f), new Vector3(1.3f, 0.08f, 0.16f), M.Concrete);
                }
                foreach (var sx in new[] { -1f, 1f })
                {
                    LP(PrimitiveType.Cube, "SideWindow", v, new Vector3(sx * (w * 0.5f + 0.03f), 2.3f, 0f), new Vector3(0.08f, 0.9f, 1.1f), M.Glass);
                    LP(PrimitiveType.Cube, "SideSill", v, new Vector3(sx * (w * 0.5f + 0.08f), 1.8f, 0f), new Vector3(0.16f, 0.08f, 1.3f), M.Concrete);
                }

                // roof equipment
                LP(PrimitiveType.Cube, "AC", v, new Vector3(w * 0.22f, h + 0.7f, -d * 0.15f), new Vector3(1.3f, 0.7f, 0.9f), M.Metal);
                LP(PrimitiveType.Cylinder, "Fan", v, new Vector3(w * 0.22f, h + 1.07f, -d * 0.15f), new Vector3(0.6f, 0.03f, 0.6f), M.MetalDark);
                LP(PrimitiveType.Cylinder, "Antenna", v, new Vector3(-w * 0.3f, h + 1.6f, d * 0.1f), new Vector3(0.05f, 1.3f, 0.05f), M.Metal);
                LP(PrimitiveType.Cube, "AntennaBar", v, new Vector3(-w * 0.3f, h + 2.3f, d * 0.1f), new Vector3(0.9f, 0.04f, 0.04f), M.Metal);
                LP(PrimitiveType.Cylinder, "Tank", v, new Vector3(-w * 0.2f, h + 0.9f, -d * 0.25f), new Vector3(1.1f, 0.6f, 1.1f), M.Rust);
                i++;
            }
        }

        // ---- props, scatter ---------------------------------------------------

        static void BuildDecor(Transform props)
        {
            Tent(props, -11.5f, 12f);
            Tent(props, 11.5f, -12f);

            // Oil drums, two of them burning.
            Vector2[] drums = { new Vector2(-12.8f, 8.2f), new Vector2(-13.2f, 9.3f), new Vector2(12.9f, -9.4f), new Vector2(13.2f, -8.3f), new Vector2(-9.2f, 13.4f) };
            for (int i = 0; i < drums.Length; i++)
            {
                var d = Drum(props, drums[i].x, drums[i].y, i % 2 == 0 ? M.Rust : M.Green);
                if (i == 0 || i == 3) d.AddComponent<FireEffect>();
            }

            // Supply crates and tarps next to the tents.
            AmmoBox(props, -13.3f, 14f, 0f);
            AmmoBox(props, -9.6f, 14.1f, 12f);
            AmmoBox(props, 13.3f, -14.1f, 90f);
            AmmoBox(props, 9.4f, -14.2f, 0f);

            // Light poles.
            Vector2[] poles = { new Vector2(-12.5f, -12.5f), new Vector2(12.5f, 12.5f), new Vector2(12.5f, -12.5f), new Vector2(-12.5f, 12.5f) };
            foreach (var p in poles)
            {
                Prim(PrimitiveType.Cylinder, "Pole", props, new Vector3(p.x, 3f, p.y), new Vector3(0.18f, 3f, 0.18f), M.Metal);
                Prim(PrimitiveType.Cube, "Arm", props, new Vector3(p.x, 6.05f, p.y), new Vector3(0.9f, 0.08f, 0.14f), M.Metal);
                Prim(PrimitiveType.Cube, "Lamp", props, new Vector3(p.x, 5.95f, p.y), new Vector3(0.7f, 0.12f, 0.35f), M.Lamp);
            }

            // Flag pole in front of the north gate.
            Prim(PrimitiveType.Cylinder, "FlagPole", props, new Vector3(4.5f, 4f, 19f), new Vector3(0.1f, 4f, 0.1f), M.Metal);
            Prim(PrimitiveType.Cube, "Flag", props, new Vector3(5.4f, 7.2f, 19f), new Vector3(1.7f, 0.95f, 0.04f), M.Defender);
        }

        static GameObject Drum(Transform parent, float x, float z, Material mat)
        {
            var root = new GameObject("Drum");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, 0f, z);
            var body = LP(PrimitiveType.Cylinder, "Body", root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.62f, 0.55f, 0.62f), mat);
            LP(PrimitiveType.Cylinder, "RingA", root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.65f, 0.03f, 0.65f), M.MetalDark);
            LP(PrimitiveType.Cylinder, "RingB", root.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.65f, 0.03f, 0.65f), M.MetalDark);
            LP(PrimitiveType.Cylinder, "Lid", root.transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.58f, 0.015f, 0.58f), M.MetalDark);
            body.isStatic = true;
            return root;
        }

        static void AmmoBox(Transform parent, float x, float z, float rot)
        {
            var v = VisualRoot("AmmoBox", parent, new Vector3(x, 0f, z), rot);
            LP(PrimitiveType.Cube, "Box", v, new Vector3(0f, 0.22f, 0f), new Vector3(0.9f, 0.44f, 0.5f), M.Green);
            LP(PrimitiveType.Cube, "Lid", v, new Vector3(0f, 0.46f, 0f), new Vector3(0.94f, 0.05f, 0.54f), M.MetalDark);
            LP(PrimitiveType.Cube, "Stencil", v, new Vector3(0f, 0.25f, 0.255f), new Vector3(0.4f, 0.1f, 0.01f), M.Canvas);
            LP(PrimitiveType.Cube, "Box2", v, new Vector3(0.1f, 0.66f, 0.02f), new Vector3(0.7f, 0.36f, 0.45f), M.Green, new Vector3(0f, 14f, 0f));
        }

        static void Tent(Transform parent, float x, float z)
        {
            var root = VisualRoot("Tent", parent, new Vector3(x, 0f, z), 0f);
            var canvas = Tiled(M.Canvas, 2f, 1f);
            LP(PrimitiveType.Cube, "Body", root, new Vector3(0f, 0.9f, 0f), new Vector3(3.6f, 1.8f, 4.4f), canvas);
            LP(PrimitiveType.Cube, "RoofL", root, new Vector3(-0.95f, 2.05f, 0f), new Vector3(2.2f, 0.12f, 4.6f), M.Olive, new Vector3(0f, 0f, 35f));
            LP(PrimitiveType.Cube, "RoofR", root, new Vector3(0.95f, 2.05f, 0f), new Vector3(2.2f, 0.12f, 4.6f), M.Olive, new Vector3(0f, 0f, -35f));
            LP(PrimitiveType.Cube, "Ridge", root, new Vector3(0f, 2.55f, 0f), new Vector3(0.12f, 0.1f, 4.7f), M.MetalDark);
            LP(PrimitiveType.Cube, "Door", root, new Vector3(0f, 0.8f, 2.22f), new Vector3(1.2f, 1.6f, 0.05f), M.Olive);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    LP(PrimitiveType.Cylinder, "Pole", root, new Vector3(sx * 1.85f, 1.2f, sz * 2.3f), new Vector3(0.08f, 1.2f, 0.08f), M.MetalDark);

            var blocker = new GameObject("TentBlocker");
            blocker.transform.SetParent(root, false);
            blocker.transform.localPosition = new Vector3(0f, 1f, 0f);
            blocker.layer = GameLayers.Obstacle;
            var bc = blocker.AddComponent<BoxCollider>();
            bc.size = new Vector3(3.6f, 2f, 4.4f);
            MakeNavBlocker(blocker);
            placed.Add(bc.bounds);
        }

        static void BuildScatter(Transform props)
        {
            var rng = new System.Random(42);
            var scatter = new GameObject("Scatter").transform;
            scatter.SetParent(props, false);

            bool Free(Vector3 p, float radius)
            {
                foreach (var b in placed)
                {
                    var e = b.extents; e.y = 0f;
                    var c = b.center; c.y = 0f;
                    var d = new Vector3(Mathf.Max(0f, Mathf.Abs(p.x - c.x) - e.x), 0f, Mathf.Max(0f, Mathf.Abs(p.z - c.z) - e.z));
                    if (d.magnitude < radius) return false;
                }
                return true;
            }

            // Pebbles
            int pebbles = 0;
            for (int tries = 0; pebbles < 170 && tries < 1000; tries++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 17f + (float)rng.NextDouble() * 38f;
                var p = new Vector3(Mathf.Sin(a) * r, 0.04f, Mathf.Cos(a) * r);
                if (!Free(p, 0.3f)) continue;
                float s = 0.12f + (float)rng.NextDouble() * 0.28f;
                Prim(PrimitiveType.Sphere, "Pebble", scatter, p, new Vector3(s * 1.3f, s * 0.55f, s), M.Pebble, 0, false, (float)rng.NextDouble() * 360f);
                pebbles++;
            }

            // Dry bushes
            int bushes = 0;
            for (int tries = 0; bushes < 34 && tries < 1000; tries++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 19f + (float)rng.NextDouble() * 36f;
                var p = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                if (!Free(p, 1.6f)) continue;
                if (Mathf.Abs(p.x) < 4f && p.z > 15f) continue;   // keep the north road clear
                float s = 0.55f + (float)rng.NextDouble() * 0.6f;
                for (int k = 0; k < 4; k++)
                {
                    var off = new Vector3((float)(rng.NextDouble() - 0.5) * s, 0f, (float)(rng.NextDouble() - 0.5) * s);
                    float ks = s * (0.55f + (float)rng.NextDouble() * 0.45f);
                    Prim(PrimitiveType.Sphere, "Bush", scatter, p + off + Vector3.up * ks * 0.22f, new Vector3(ks, ks * 0.5f, ks), M.Bush);
                }
                bushes++;
            }

            // Oil stains and tyre-dark patches near the gates.
            Vector2[] stains = { new Vector2(2f, 19f), new Vector2(-3f, 24f), new Vector2(19f, 6f), new Vector2(25f, 8f), new Vector2(-20f, -2f), new Vector2(-9f, -20f), new Vector2(6f, 9f), new Vector2(-4f, -9f) };
            foreach (var s in stains)
            {
                float sc = 1.2f + (Mathf.Abs(s.x * 13.37f) % 1.3f);
                Prim(PrimitiveType.Cylinder, "Stain", scatter, new Vector3(s.x, 0.07f, s.y), new Vector3(sc * 1.4f, 0.005f, sc), M.Stain);
            }
        }

        // =====================================================================
        // Objective, defenders, player
        // =====================================================================

        static GameObject BuildReactor()
        {
            var root = new GameObject("Reactor");
            root.layer = GameLayers.Structure;
            root.AddComponent<Health>();
            root.AddComponent<Targetable>();
            var reactor = root.AddComponent<Reactor>();
            var t = root.transform;

            // Platform with hazard ring and steps
            Prim(PrimitiveType.Cylinder, "Platform", t, new Vector3(0f, 0.12f, 0f), new Vector3(9.4f, 0.12f, 9.4f), Tiled(M.ConcreteDark, 3f, 3f));
            Prim(PrimitiveType.Cylinder, "PlatformTop", t, new Vector3(0f, 0.26f, 0f), new Vector3(8.4f, 0.03f, 8.4f), M.Concrete);
            Prim(PrimitiveType.Cylinder, "HazardRing", t, new Vector3(0f, 0.285f, 0f), new Vector3(7.6f, 0.01f, 7.6f), M.Hazard);
            Prim(PrimitiveType.Cylinder, "HazardInner", t, new Vector3(0f, 0.295f, 0f), new Vector3(7.2f, 0.01f, 7.2f), M.Asphalt);

            var baseGo = Prim(PrimitiveType.Cylinder, "Base", t, new Vector3(0f, 0.7f, 0f), new Vector3(6.4f, 0.7f, 6.4f), M.MetalDark, GameLayers.Obstacle, true);
            UnityEngine.Object.DestroyImmediate(baseGo.GetComponent<CapsuleCollider>());
            var mc = baseGo.AddComponent<MeshCollider>();
            mc.convex = true;
            MakeNavBlocker(baseGo);
            Prim(PrimitiveType.Cylinder, "BaseTrim", t, new Vector3(0f, 1.45f, 0f), new Vector3(6.0f, 0.06f, 6.0f), M.Metal);
            Prim(PrimitiveType.Cylinder, "BaseGlow", t, new Vector3(0f, 1.25f, 0f), new Vector3(6.45f, 0.04f, 6.45f), M.Strip);

            // Cooling fins around the base
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3.28f;
                var fin = Prim(PrimitiveType.Cube, "Fin", t, p + Vector3.up * 0.85f, new Vector3(0.18f, 1.0f, 0.5f), M.Metal);
                fin.transform.rotation = Quaternion.Euler(0f, i * 30f, 0f);
            }

            var tower = Prim(PrimitiveType.Cylinder, "Tower", t, new Vector3(0f, 2.9f, 0f), new Vector3(2.2f, 1.6f, 2.2f), M.Metal, GameLayers.Structure, true);
            var core = Prim(PrimitiveType.Cylinder, "Core", t, new Vector3(0f, 3.3f, 0f), new Vector3(1.6f, 1.9f, 1.6f), M.Core);
            var top = Prim(PrimitiveType.Sphere, "CoreTop", t, new Vector3(0f, 5.75f, 0f), Vector3.one * 1.7f, M.Core);
            Prim(PrimitiveType.Cylinder, "TowerCapBottom", t, new Vector3(0f, 1.6f, 0f), new Vector3(2.5f, 0.12f, 2.5f), M.MetalDark);
            Prim(PrimitiveType.Cylinder, "TowerCapTop", t, new Vector3(0f, 5.05f, 0f), new Vector3(2.5f, 0.1f, 2.5f), M.MetalDark);

            // Spinning containment rings
            var ringA = Prim(PrimitiveType.Cylinder, "RingA", t, new Vector3(0f, 3.0f, 0f), new Vector3(3.4f, 0.04f, 3.4f), M.Strip);
            var ringB = Prim(PrimitiveType.Cylinder, "RingB", t, new Vector3(0f, 4.1f, 0f), new Vector3(2.8f, 0.04f, 2.8f), M.Strip);
            ringA.isStatic = false; ringB.isStatic = false;
            ringA.AddComponent<Spinner>().degreesPerSecond = new Vector3(8f, 40f, 0f);
            ringB.AddComponent<Spinner>().degreesPerSecond = new Vector3(-10f, -55f, 4f);

            // Pylons, pipes, light strips
            foreach (var a in new[] { 45f, 135f, 225f, 315f })
            {
                float rad = a * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Prim(PrimitiveType.Cube, "Pylon", t, dir * 2.7f + Vector3.up * 2.1f, new Vector3(0.55f, 3.6f, 0.55f), M.Metal, 0, false, a);
                Prim(PrimitiveType.Cube, "PylonStrip", t, dir * 2.97f + Vector3.up * 2.1f, new Vector3(0.14f, 3.0f, 0.14f), M.Strip, 0, false, a);
                var pipe = Prim(PrimitiveType.Cylinder, "Pipe", t, dir * 1.9f + Vector3.up * 3.9f, new Vector3(0.22f, 0.9f, 0.22f), M.MetalDark);
                pipe.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
                Prim(PrimitiveType.Sphere, "Vent", t, dir * 2.7f + Vector3.up * 3.95f, Vector3.one * 0.55f, M.Metal);
            }

            var lightGo = new GameObject("CoreLight");
            lightGo.transform.SetParent(t, false);
            lightGo.transform.localPosition = new Vector3(0f, 5.5f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 22f;
            light.color = new Color(0.2f, 0.9f, 1f);
            light.intensity = 3.2f;
            light.shadows = LightShadows.None;

            reactor.coreRenderers = new[] { core.GetComponent<Renderer>(), top.GetComponent<Renderer>() };
            reactor.coreLight = light;
            _ = tower;
            return root;
        }

        static TurretPost[] BuildTurretPosts()
        {
            var parent = new GameObject("TurretPosts").transform;
            var spec = new[]
            {
                (new Vector3(8f, 0f, 8f), TurretPost.State.Active, "NE"),
                (new Vector3(-8f, 0f, -8f), TurretPost.State.Active, "SW"),
                (new Vector3(-8f, 0f, 8f), TurretPost.State.Empty, "NW"),
                (new Vector3(8f, 0f, -8f), TurretPost.State.Empty, "SE"),
            };

            var result = new TurretPost[spec.Length];
            for (int i = 0; i < spec.Length; i++)
            {
                var (pos, state, tag) = spec[i];
                var root = new GameObject("TurretPost_" + tag);
                root.transform.SetParent(parent, false);
                root.transform.position = pos;
                root.layer = GameLayers.Structure;
                root.AddComponent<Health>();
                root.AddComponent<Targetable>();
                var post = root.AddComponent<TurretPost>();
                post.startState = state;

                var blocker = new GameObject("Pedestal");
                blocker.transform.SetParent(root.transform, false);
                blocker.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                blocker.layer = GameLayers.Obstacle;
                var bc = blocker.AddComponent<BoxCollider>();
                bc.size = new Vector3(1.4f, 1f, 1.4f);
                MakeNavBlocker(blocker);

                // Active turret: sandbag ring, pedestal, rotating head with twin barrels
                var active = new GameObject("Active").transform;
                active.SetParent(root.transform, false);
                Prim(PrimitiveType.Cylinder, "Plinth", active, pos + new Vector3(0f, 0.12f, 0f), new Vector3(1.9f, 0.12f, 1.9f), M.ConcreteDark);
                Prim(PrimitiveType.Cylinder, "Base", active, pos + new Vector3(0f, 0.65f, 0f), new Vector3(1.3f, 0.45f, 1.3f), M.Defender);
                Prim(PrimitiveType.Cylinder, "BaseBand", active, pos + new Vector3(0f, 0.95f, 0f), new Vector3(1.38f, 0.04f, 1.38f), M.Strip);
                var head = new GameObject("Head").transform;
                head.SetParent(active, false);
                head.position = pos + new Vector3(0f, 1.4f, 0f);
                Prim(PrimitiveType.Cube, "Body", head, head.position, new Vector3(0.85f, 0.55f, 0.95f), M.Metal, GameLayers.Structure, true);
                Prim(PrimitiveType.Sphere, "Dome", head, head.position + new Vector3(0f, 0.28f, -0.05f), new Vector3(0.75f, 0.4f, 0.8f), M.Defender);
                Prim(PrimitiveType.Cube, "BarrelL", head, head.position + new Vector3(-0.17f, 0.02f, 0.9f), new Vector3(0.1f, 0.1f, 1.1f), M.MetalDark);
                Prim(PrimitiveType.Cube, "BarrelR", head, head.position + new Vector3(0.17f, 0.02f, 0.9f), new Vector3(0.1f, 0.1f, 1.1f), M.MetalDark);
                Prim(PrimitiveType.Cube, "Sensor", head, head.position + new Vector3(0f, 0.1f, 0.5f), new Vector3(0.4f, 0.1f, 0.05f), M.Strip);
                Prim(PrimitiveType.Cube, "AmmoBox", head, head.position + new Vector3(0f, -0.05f, -0.55f), new Vector3(0.5f, 0.3f, 0.3f), M.Green);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(head, false);
                muzzle.position = head.position + new Vector3(0f, 0.02f, 1.5f);

                var ghost = new GameObject("Ghost").transform;
                ghost.SetParent(root.transform, false);
                Prim(PrimitiveType.Cylinder, "Disc", ghost, pos + new Vector3(0f, 0.09f, 0f), new Vector3(2.2f, 0.01f, 2.2f), M.Ghost);
                Prim(PrimitiveType.Cylinder, "Ring", ghost, pos + new Vector3(0f, 0.085f, 0f), new Vector3(2.6f, 0.01f, 2.6f), M.PadMark);

                var wreck = new GameObject("Wreck").transform;
                wreck.SetParent(root.transform, false);
                Prim(PrimitiveType.Cylinder, "Base", wreck, pos + new Vector3(0f, 0.3f, 0f), new Vector3(1.4f, 0.3f, 1.4f), M.Wreck);
                var chunk = Prim(PrimitiveType.Cube, "Chunk", wreck, pos + new Vector3(0.1f, 0.8f, 0f), new Vector3(0.7f, 0.4f, 0.6f), M.Wreck);
                chunk.transform.rotation = Quaternion.Euler(20f, 30f, 25f);
                Prim(PrimitiveType.Cube, "Chunk2", wreck, pos + new Vector3(-0.4f, 0.55f, 0.3f), new Vector3(0.3f, 0.2f, 0.5f), M.Rust, 0, false, 40f);

                post.head = head;
                post.muzzle = muzzle;
                post.activeVisual = active.gameObject;
                post.ghostVisual = ghost.gameObject;
                post.wreckVisual = wreck.gameObject;
                result[i] = post;
            }
            return result;
        }

        static GameObject BuildPlayer()
        {
            var root = new GameObject("Player");
            root.layer = GameLayers.Player;
            root.transform.position = new Vector3(0f, 0.05f, -6f);
            root.AddComponent<CharacterController>();
            root.AddComponent<Health>();
            root.AddComponent<Targetable>();
            root.AddComponent<PlayerController>();   // builds its humanoid model at runtime
            return root;
        }

        static Transform[] BuildSpawnPoints()
        {
            var parent = new GameObject("SpawnPoints").transform;
            var pts = new[]
            {
                ("Spawn_North", new Vector3(0f, 0f, 50f)),
                ("Spawn_East", new Vector3(50f, 0f, 2f)),
                ("Spawn_SouthWest", new Vector3(-34f, 0f, -50f)),
                ("Spawn_West", new Vector3(-50f, 0f, 12f)),
            };
            var result = new Transform[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var go = new GameObject(pts[i].Item1);
                go.transform.SetParent(parent, false);
                go.transform.position = pts[i].Item2;
                result[i] = go.transform;
            }
            return result;
        }

        static void BuildCamera(GameObject player)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 250f;
            cam.allowHDR = true;

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            go.AddComponent<AudioListener>();
            go.AddComponent<AmbientDust>();
            var rig = go.AddComponent<CameraRig>();
            go.transform.position = player.transform.position + rig.offset;
            go.transform.rotation = Quaternion.LookRotation(-rig.offset.normalized, Vector3.up);
        }

        static void BuildPostProcessing()
        {
            var go = new GameObject("PostFX");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(RenderSetup.VolumeProfilePath);
        }

        static void BuildManagers(Transform[] spawns)
        {
            var go = new GameObject("_GameSystems");
            go.AddComponent<GameManager>();
            go.AddComponent<HUD>();
            var wm = go.AddComponent<WaveManager>();
            wm.spawnPoints = spawns;

            var nav = new GameObject("NavMesh");
            var surface = nav.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = GameLayers.ObstacleMask | GameLayers.GroundMask;
            var level = nav.AddComponent<OutpostLevel>();
            level.surface = surface;
            level.spawnPoints = spawns;
        }
    }
}
