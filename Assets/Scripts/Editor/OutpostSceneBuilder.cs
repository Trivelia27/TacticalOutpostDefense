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
        // Imported models (CC0 low-poly assets by Quaternius, see Assets/Art/CREDITS.txt)
        // =====================================================================

        const string PropDir = "Assets/Art/Props";
        const string ModelMatDir = "Assets/Art/Materials";

        static GameObject LoadModel(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/{name}.glb");

        /// <summary>URP Lit copy of a glTF material, saved as an asset so the scene can reference it.</summary>
        static Material ModelMat(Material src)
        {
            string tex = "";
            Color color = Color.white;
            if (src.HasProperty("baseColorTexture")) { var t = src.GetTexture("baseColorTexture"); if (t != null) tex = t.name; }
            if (src.HasProperty("baseColorFactor")) color = src.GetColor("baseColorFactor");
            string key = (src.name + "_" + tex + "_" + ColorUtility.ToHtmlStringRGB(color)).Replace(' ', '_').Replace('.', '_').Replace('|', '_');
            string path = $"{ModelMatDir}/{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Directory.CreateDirectory(ModelMatDir);
            m = new Material(ModelMaterials.ToLit(src, 0.4f)) { name = key };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void ApplyModelMaterials(GameObject go, Material overrideMat = null)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = overrideMat != null ? overrideMat : (mats[i] != null ? ModelMat(mats[i]) : null);
                r.sharedMaterials = mats;
            }
        }

        static Bounds RenderBounds(GameObject go)
        {
            var b = new Bounds();
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>
        /// Instantiates a model, scales it (<paramref name="size"/> exact box when <paramref name="stretch"/>, else uniformly so the
        /// given axis matches) and stands it on the ground at <paramref name="pos"/> (bottom centre), turned by <paramref name="rotY"/>.
        /// </summary>
        static GameObject PlaceModel(string asset, string name, Transform parent, Vector3 pos, float rotY, Vector3 size, bool stretch,
                                     int axis = 1, Material overrideMat = null, bool isStatic = true)
        {
            var prefab = LoadModel(asset);
            var holder = new GameObject(name);                         // stays in the scene root while it is measured at the origin
            if (prefab == null)
            {
                Debug.LogWarning("[OutpostSceneBuilder] missing model " + asset);
                holder.transform.SetParent(parent, false);
                return holder;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            var raw = RenderBounds(inst).size;
            Vector3 scale;
            if (stretch) scale = new Vector3(size.x / Mathf.Max(0.001f, raw.x), size.y / Mathf.Max(0.001f, raw.y), size.z / Mathf.Max(0.001f, raw.z));
            else { float k = size[axis] / Mathf.Max(0.001f, raw[axis]); scale = Vector3.one * k; }
            inst.transform.localScale = scale;

            var b = RenderBounds(inst);                                  // holder is still at the origin, unrotated
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            ApplyModelMaterials(inst, overrideMat);
            holder.transform.position = pos;
            holder.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            holder.transform.SetParent(parent, true);                  // keeps the world pose
            if (isStatic) foreach (var t in holder.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return holder;
        }

        static GameObject PlaceBox(string asset, string name, Transform parent, Vector3 pos, float rotY, Vector3 size, Material mat = null)
            => PlaceModel(asset, name, parent, pos, rotY, size, true, 1, mat);

        static GameObject PlaceHeight(string asset, string name, Transform parent, Vector3 pos, float rotY, float height, Material mat = null)
            => PlaceModel(asset, name, parent, pos, rotY, new Vector3(0f, height, 0f), false, 1, mat);

        static GameObject PlaceLength(string asset, string name, Transform parent, Vector3 pos, float rotY, float length, int axis, Material mat = null)
        {
            var size = Vector3.zero;
            size[axis] = length;
            return PlaceModel(asset, name, parent, pos, rotY, size, false, axis, mat);
        }

        /// <summary>A solid prop: invisible box collider (obstacle layer, NavMesh blocker, optionally cover) with a model as its visual.</summary>
        static GameObject SolidModel(string asset, string name, Transform parent, float x, float z, Vector3 colliderSize, float rotY, Vector3 modelSize, bool stretch, bool asCover)
        {
            var go = Obstacle(name, parent, x, z, colliderSize, rotY, M.Concrete, asCover);
            go.GetComponent<MeshRenderer>().enabled = false;
            PlaceModel(asset, name + "_Model", parent, new Vector3(x, 0f, z), rotY, modelSize, stretch);
            return go;
        }

        static bool IsFree(Vector3 p, float radius)
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
                    var tower = Obstacle("Tower" + ti, walls, sx * 15f, sz * 15f, size, 45f, M.Concrete, false);
                    tower.GetComponent<MeshRenderer>().enabled = false;
                    PlaceHeight("guard-tower", "Tower" + ti + "_Model", walls, new Vector3(sx * 15f, 0f, sz * 15f), ti * 90f + 45f, 5.6f);
                    if (ti == 3)
                    {
                        var radar = PlaceHeight("radar", "Radar", walls, new Vector3(sx * 15f, 5.6f, sz * 15f), 0f, 2.0f);
                        foreach (var t in radar.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = false;
                        radar.AddComponent<Spinner>().degreesPerSecond = new Vector3(0f, 55f, 0f);
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

        /// <summary>Collision box (hidden renderer) with a sandbag wall model as its visual.</summary>
        static void Sandbags(string name, Transform parent, float x, float z, float rot)
        {
            SolidModel("sandbag-long", name, parent, x, z, SandbagSize, rot, SandbagSize, true, true);
        }

        static void CrateStack(string name, Transform parent, float x, float z, float rot)
        {
            string model = ((int)(Mathf.Abs(x) + Mathf.Abs(z))) % 2 == 0 ? "crate-d" : "crate-a";
            SolidModel(model, name, parent, x, z, CrateSize, rot, CrateSize, true, true);
        }

        static void Barrier(string name, Transform parent, float x, float z, float rot)
        {
            var go = Obstacle(name, parent, x, z, BarrierSize, rot, M.Concrete, true);
            go.GetComponent<MeshRenderer>().enabled = false;

            // two heavy traffic barriers side by side
            var v = VisualRoot(name + "_Visual", parent, new Vector3(x, 0f, z), rot);
            foreach (float dx in new[] { -0.76f, 0.76f })
            {
                var piece = PlaceBox("barrier-traffic-b", "Barrier", v, Vector3.zero, 0f, new Vector3(1.55f, BarrierSize.y, BarrierSize.z * 0.95f));
                piece.transform.SetParent(v, false);
                piece.transform.localPosition = new Vector3(dx, 0f, 0f);
                piece.transform.localRotation = Quaternion.identity;
            }
        }

        // ---- buildings --------------------------------------------------------

        static void BuildBuildings(Transform env)
        {
            var specs = new[]
            {
                new Vector4(-34f, 22f, 7f, 6f), new Vector4(34f, 24f, 6f, 7f),
                new Vector4(30f, -34f, 7f, 6f), new Vector4(-36f, -28f, 6f, 7f),
            };
            string[,] colours = { { "container-green", "container-red" }, { "container-red", "container-green" }, { "container-green", "container-green" }, { "container-red", "container-red" } };
            int i = 0;
            foreach (var s in specs)
            {
                float w = s.z, d = s.w, h = 3.6f;
                var size = new Vector3(w, h, d);
                var obstacle = Obstacle($"Building_{i}", env, s.x, s.y, size, 0f, M.Building, false);
                obstacle.GetComponent<MeshRenderer>().enabled = false;

                // A barracks block made of two shipping containers side by side with a third one on top.
                bool wide = w >= d;
                float rot = wide ? 0f : 90f;
                var container = new Vector3(5.7f, 2.6f, 2.55f);
                float gap = 0.2f;
                float off = (container.z + gap) * 0.5f;
                var holder = VisualRoot($"Building_{i}_Model", env, new Vector3(s.x, 0f, s.y), rot);
                PlaceBox(colours[i, 0], "ContainerA", holder, holder.position + holder.TransformDirection(new Vector3(0f, 0f, -off)), rot, container).transform.SetParent(holder, true);
                PlaceBox(colours[i, 1], "ContainerB", holder, holder.position + holder.TransformDirection(new Vector3(0f, 0f, off)), rot, container).transform.SetParent(holder, true);
                PlaceBox(colours[(i + 1) % 4, 0], "ContainerTop", holder, holder.position + new Vector3(0f, container.y, 0f), rot, new Vector3(5.2f, 2.4f, 2.4f)).transform.SetParent(holder, true);
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
            string[] drumModels = { "barrel-blue", "barrel-brown", "barrel-blue", "barrel-wood", "barrel-brown" };
            for (int i = 0; i < drums.Length; i++)
            {
                var d = Drum(props, drums[i].x, drums[i].y, drumModels[i]);
                if (i == 0 || i == 3) d.AddComponent<FireEffect>();
            }

            // Supply crates, jerry cans and a dumpster next to the tents.
            AmmoBox(props, -13.3f, 14f, 0f, "crate-b");
            AmmoBox(props, -9.6f, 14.1f, 12f, "crate-c");
            AmmoBox(props, 13.3f, -14.1f, 90f, "crate-b");
            AmmoBox(props, 9.4f, -14.2f, 0f, "crate-c");
            PlaceHeight("gas-can", "GasCan", props, new Vector3(-8.6f, 0f, 13.8f), 20f, 0.7f);
            PlaceHeight("gas-can", "GasCan", props, new Vector3(8.4f, 0f, -13.9f), -30f, 0.7f);
            PlaceBox("dumpster", "Dumpster", props, new Vector3(13.6f, 0f, 12.6f), 90f, new Vector3(1.9f, 1.4f, 1.1f));

            // Street lights.
            Vector2[] poles = { new Vector2(-12.5f, -12.5f), new Vector2(12.5f, 12.5f), new Vector2(12.5f, -12.5f), new Vector2(-12.5f, 12.5f) };
            foreach (var p in poles)
            {
                float face = Mathf.Atan2(-p.x, -p.y) * Mathf.Rad2Deg;
                PlaceHeight("street-light-c", "StreetLight", props, new Vector3(p.x, 0f, p.y), face, 6.2f);
            }

            // Flag in front of the north gate.
            PlaceHeight("flag", "Flag", props, new Vector3(4.5f, 0f, 19f), 0f, 7.5f);

            // Outpost equipment: antenna mast, water tower and two wrecks parked outside the walls.
            PlaceHeight("antenna-mast", "AntennaMast", props, new Vector3(-12.2f, 0f, -2.5f), 0f, 9f);
            Vehicle(props, "water-tower", new Vector3(-20.5f, 0f, 6.5f), 0f, new Vector3(2.8f, 9.4f, 2.8f), 9.4f, 1, true);
            Vehicle(props, "tank", new Vector3(24f, 0f, -38f), 25f, new Vector3(6.4f, 2.4f, 3.4f), 6.4f, 0, true);
            Vehicle(props, "pickup-armored", new Vector3(34f, 0f, 38f), -35f, new Vector3(2.4f, 2.0f, 5.0f), 5.0f, 2, true);
        }

        /// <summary>A large prop with a box collider (solid obstacle, usable as cover) and a model on top.</summary>
        static void Vehicle(Transform parent, string asset, Vector3 pos, float rotY, Vector3 colliderSize, float fit, int axis, bool asCover)
        {
            if (!IsFree(pos, colliderSize.magnitude * 0.5f)) { Debug.LogWarning("[OutpostSceneBuilder] no room for " + asset); return; }
            var go = Obstacle(asset + "_Solid", parent, pos.x, pos.z, colliderSize, rotY, M.Concrete, asCover);
            go.GetComponent<MeshRenderer>().enabled = false;
            PlaceLength(asset, asset, parent, pos, rotY, fit, axis);
        }

        static GameObject Drum(Transform parent, float x, float z, string model)
        {
            var root = new GameObject("Drum");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, 0f, z);
            var m = PlaceBox(model, "Model", root.transform, root.transform.position, x * 37f, new Vector3(0.7f, 1.1f, 0.7f));
            m.transform.SetParent(root.transform, true);
            return root;
        }

        static void AmmoBox(Transform parent, float x, float z, float rot, string model)
        {
            PlaceBox(model, "AmmoBox", parent, new Vector3(x, 0f, z), rot, new Vector3(0.8f, 0.8f, 0.8f));
            PlaceBox(model, "AmmoBox2", parent, new Vector3(x + 0.1f, 0.8f, z + 0.02f), rot + 14f, new Vector3(0.6f, 0.6f, 0.6f));
        }

        static void Tent(Transform parent, float x, float z)
        {
            var root = VisualRoot("Tent", parent, new Vector3(x, 0f, z), 0f);
            PlaceLength("tent", "TentModel", root, new Vector3(x, 0f, z), 0f, 4.6f, 2).transform.SetParent(root, true);

            var blocker = new GameObject("TentBlocker");
            blocker.transform.SetParent(root, false);
            blocker.transform.localPosition = new Vector3(0f, 1f, 0f);
            blocker.layer = GameLayers.Obstacle;
            var bc = blocker.AddComponent<BoxCollider>();
            bc.size = new Vector3(3.2f, 2f, 4.4f);
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
                PlaceHeight("rock-a", "Pebble", scatter, new Vector3(p.x, 0f, p.z), (float)rng.NextDouble() * 360f, s * 0.9f);
                pebbles++;
            }

            // Rocks and cacti
            int rocks = 0;
            string[] plants = { "cactus-a", "cactus-b" };
            for (int tries = 0; rocks < 34 && tries < 1000; tries++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 19f + (float)rng.NextDouble() * 36f;
                var p = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                if (!Free(p, 1.8f)) continue;
                if (Mathf.Abs(p.x) < 4f && p.z > 15f) continue;   // keep the north road clear
                bool rock = rng.NextDouble() < 0.55;
                float rot = (float)rng.NextDouble() * 360f;
                if (rock) PlaceHeight("rock-a", "Rock", scatter, p, rot, 0.5f + (float)rng.NextDouble() * 0.9f);
                else PlaceHeight(plants[rocks % plants.Length], "Cactus", scatter, p, rot, 0.9f + (float)rng.NextDouble() * 0.9f);
                rocks++;
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

                // Active turret: concrete plinth, rotating head carrying the turret model
                var active = new GameObject("Active").transform;
                active.SetParent(root.transform, false);
                Prim(PrimitiveType.Cylinder, "Plinth", active, pos + new Vector3(0f, 0.15f, 0f), new Vector3(1.9f, 0.15f, 1.9f), M.ConcreteDark);
                var head = new GameObject("Head").transform;
                head.SetParent(active, false);
                head.position = pos + new Vector3(0f, 0.3f, 0f);
                var model = PlaceModel("turret-gun-a", "TurretModel", head, head.position, 0f, new Vector3(2.1f, 0f, 0f), false, 0, null, false);
                model.transform.SetParent(head, true);
                var hit = new GameObject("HitBox");
                hit.transform.SetParent(head, false);
                hit.transform.position = head.position + new Vector3(0f, 0.55f, 0f);
                hit.layer = GameLayers.Structure;
                hit.AddComponent<BoxCollider>().size = new Vector3(1.5f, 1.1f, 1.5f);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(head, false);
                muzzle.position = head.position + new Vector3(0f, 0.75f, 1.4f);

                var ghost = new GameObject("Ghost").transform;
                ghost.SetParent(root.transform, false);
                Prim(PrimitiveType.Cylinder, "Disc", ghost, pos + new Vector3(0f, 0.09f, 0f), new Vector3(2.2f, 0.01f, 2.2f), M.Ghost);
                Prim(PrimitiveType.Cylinder, "Ring", ghost, pos + new Vector3(0f, 0.085f, 0f), new Vector3(2.6f, 0.01f, 2.6f), M.PadMark);

                var wreck = new GameObject("Wreck").transform;
                wreck.SetParent(root.transform, false);
                Prim(PrimitiveType.Cylinder, "Plinth", wreck, pos + new Vector3(0f, 0.15f, 0f), new Vector3(1.9f, 0.15f, 1.9f), M.Wreck);
                var burnt = PlaceModel("turret-gun-b", "BurntTurret", wreck, pos + new Vector3(0.1f, 0.3f, 0f), 37f, new Vector3(1.9f, 0f, 0f), false, 0, M.Wreck);
                burnt.transform.rotation = Quaternion.Euler(14f, 37f, 18f);
                burnt.transform.SetParent(wreck, true);

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
