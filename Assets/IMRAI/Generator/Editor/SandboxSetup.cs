// One-time sandbox setup (Getting Started sheet): make URP the active render pipeline, and add the
// Obstacle tag and the VirtualObjects layer. Safe to run again.
// Batch: Unity -batchmode -nographics -projectPath unity/IMRAISandbox -executeMethod SandboxSetup.Run
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SandboxSetup
{
    [MenuItem("IMRAI/Sandbox setup (URP, tag, layer)")]
    public static void Run()
    {
        const string dir = "Assets/IMRAI/Settings";
        Directory.CreateDirectory(dir);
        var path = dir + "/URP.asset";
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
        if (urp == null)
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, dir + "/URP_Renderer.asset");
            urp = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(urp, path);
        }
        GraphicsSettings.renderPipelineAsset = urp;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = tagManager.FindProperty("tags");
        bool hasTag = false;
        for (int i = 0; i < tags.arraySize; i++) hasTag |= tags.GetArrayElementAtIndex(i).stringValue == "Obstacle";
        if (!hasTag) { tags.InsertArrayElementAtIndex(tags.arraySize); tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "Obstacle"; }
        var layers = tagManager.FindProperty("layers");
        int layer = -1;
        for (int i = 8; i < layers.arraySize; i++) if (layers.GetArrayElementAtIndex(i).stringValue == "VirtualObjects") layer = i;
        if (layer < 0)
            for (int i = 8; i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { layers.GetArrayElementAtIndex(i).stringValue = "VirtualObjects"; layer = i; break; }
        tagManager.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log($"SETUP ok: render pipeline {GraphicsSettings.renderPipelineAsset?.name}, tag Obstacle, layer VirtualObjects = {layer}");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
