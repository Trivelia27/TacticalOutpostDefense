using UnityEditor;
using UnityEngine;

namespace TacticalOutpost.Editor
{
    /// <summary>Registers the project's physics layers (Obstacle, Ground, Player, Enemy, Structure) in TagManager.</summary>
    [InitializeOnLoad]
    public static class LayerSetup
    {
        static LayerSetup() => EditorApplication.delayCall += EnsureLayers;

        public static void EnsureLayers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");
            if (layers == null) return;

            bool changed = false;
            for (int i = 0; i < GameLayers.Names.Length; i++)
            {
                int index = GameLayers.Obstacle + i;
                var element = layers.GetArrayElementAtIndex(index);
                if (element.stringValue != GameLayers.Names[i])
                {
                    element.stringValue = GameLayers.Names[i];
                    changed = true;
                }
            }

            if (changed)
            {
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log("[LayerSetup] Registered physics layers: " + string.Join(", ", GameLayers.Names));
            }
        }
    }
}
