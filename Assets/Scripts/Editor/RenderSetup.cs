using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TacticalOutpost.Editor
{
    /// <summary>Creates and assigns the URP pipeline asset, renderer (with SSAO) and the post-processing profile.</summary>
    public static class RenderSetup
    {
        const string Dir = "Assets/Settings";
        public const string VolumeProfilePath = Dir + "/OutpostPostFX.asset";

        [MenuItem("Tools/Tactical Outpost/Setup Render Pipeline")]
        public static void Setup()
        {
            Directory.CreateDirectory(Dir);

            var rendererPath = Dir + "/Outpost_Renderer.asset";
            var pipelinePath = Dir + "/Outpost_URP.asset";

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
                AddSsao(renderer);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            ConfigurePipeline(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.antiAliasing = 0; // handled by SMAA on the camera
            }
            QualitySettings.SetQualityLevel(current, false);

            CreateVolumeProfile();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log("[RenderSetup] URP pipeline assigned (SSAO + post-processing profile ready).");
        }

        static void SetInt(SerializedObject so, string prop, int v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.intValue = v; else Debug.LogWarning("[RenderSetup] missing URP property " + prop);
        }

        static void SetFloat(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.floatValue = v; else Debug.LogWarning("[RenderSetup] missing URP property " + prop);
        }

        static void SetBool(SerializedObject so, string prop, bool v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.boolValue = v; else Debug.LogWarning("[RenderSetup] missing URP property " + prop);
        }

        static void ConfigurePipeline(UniversalRenderPipelineAsset asset)
        {
            var so = new SerializedObject(asset);
            SetBool(so, "m_SupportsHDR", true);
            SetInt(so, "m_MSAA", 4);
            SetFloat(so, "m_ShadowDistance", 90f);
            SetInt(so, "m_ShadowCascadeCount", 3);
            SetInt(so, "m_MainLightShadowmapResolution", 4096);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_AdditionalLightsRenderingMode", 1);          // per vertex is cheap; per pixel = 2
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 4);
            SetBool(so, "m_RequireDepthTexture", true);
            SetBool(so, "m_RequireOpaqueTexture", false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddSsao(UniversalRendererData renderer)
        {
            try
            {
                var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                AssetDatabase.SaveAssets();
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId))
                    throw new Exception("cannot resolve SSAO file id");

                var so = new SerializedObject(renderer);
                var feats = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                int n = feats.arraySize;
                feats.arraySize = n + 1;
                feats.GetArrayElementAtIndex(n).objectReferenceValue = ssao;
                map.arraySize = n + 1;
                map.GetArrayElementAtIndex(n).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderer);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RenderSetup] SSAO could not be added: " + e.Message);
            }
        }

        static void CreateVolumeProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (existing != null) AssetDatabase.DeleteAsset(VolumeProfilePath);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(0.32f);
            bloom.scatter.Override(0.68f);
            bloom.tint.Override(new Color(1f, 0.95f, 0.9f));

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.3f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(new Color(0.03f, 0.02f, 0.02f));

            var adj = profile.Add<ColorAdjustments>(true);
            adj.postExposure.Override(0.2f);
            adj.contrast.Override(20f);
            adj.saturation.Override(14f);
            adj.colorFilter.Override(new Color(1f, 0.97f, 0.92f));

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(0f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.08f);

            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }
    }
}
