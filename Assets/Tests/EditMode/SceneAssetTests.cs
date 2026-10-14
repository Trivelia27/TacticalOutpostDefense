using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacticalOutpost.Tests
{
    /// <summary>
    /// The brief forbids a final game made only of cubes, capsules, spheres, planes and default materials. This opens the scene as
    /// saved (before any batching) and counts where every mesh comes from: an imported model file, a Unity primitive, or our own
    /// generated mesh. It also checks that the imported models use a real shader (no pink / black materials).
    /// </summary>
    public class SceneAssetTests
    {
        const string ScenePath = "Assets/Scenes/Outpost_Desert.unity";
        static readonly HashSet<string> PrimitiveMeshes = new HashSet<string> { "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad" };

        [Test]
        public void Scene_UsesImportedModelsForProps()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // group (top-level object) -> [imported, primitive]; disabled renderers (invisible collision boxes) are ignored
            var groups = new Dictionary<string, int[]>();
            var importedFiles = new HashSet<string>();
            int badMaterials = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
                bool isImported = path.EndsWith(".glb") || path.EndsWith(".fbx");
                bool isPrimitive = !isImported && PrimitiveMeshes.Contains(mf.sharedMesh.name);

                var top = r.transform;
                while (top.parent != null) top = top.parent;
                string group = top.name;
                if (!groups.TryGetValue(group, out var counts)) groups[group] = counts = new int[2];
                if (isImported)
                {
                    counts[0]++;
                    importedFiles.Add(path);
                    foreach (var m in r.sharedMaterials)
                        if (m == null || !m.shader.name.StartsWith("Universal Render Pipeline")) badMaterials++;
                }
                else if (isPrimitive) counts[1]++;
            }

            var sb = new System.Text.StringBuilder($"[TEST] scene assets: {importedFiles.Count} imported model files in use{System.Environment.NewLine}");
            foreach (var kv in groups) sb.AppendLine($"  {kv.Key,-14} imported {kv.Value[0],4}   primitive {kv.Value[1],4}");
            Debug.Log(sb.ToString());

            Assert.GreaterOrEqual(importedFiles.Count, 25, "the level should use many different imported models");
            Assert.AreEqual(0, groups["Cover"][1], "every visible cover object (sandbags, crates, barriers) must be an imported model");
            Assert.Greater(groups["Cover"][0], 60, "the cover field should be made of imported models");
            Assert.Greater(groups["Props"][0], groups["Props"][1], "decor and scatter should be mostly imported models");
            Assert.AreEqual(0, badMaterials, "every imported prop needs a URP material (otherwise it renders pink or black)");
        }

        [Test]
        public void Characters_AreImportedModelsWithAnimations()
        {
            foreach (var path in new[]
            {
                "Art/Characters/swat", "Art/Characters/soldier-tactical", "Art/Characters/operator-black",
            })
            {
                var prefab = Resources.Load<GameObject>(path);
                Assert.IsNotNull(prefab, path + " is missing");
                Assert.Greater(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, 0, path + " has no skinned mesh");
                Assert.GreaterOrEqual(Resources.LoadAll<AnimationClip>(path).Length, 14, path + " has too few animation clips");
            }
        }
    }
}
