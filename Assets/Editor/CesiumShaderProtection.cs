using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ensures Cesium's runtime-generated shaders survive IL2CPP shader stripping
/// by adding critical shaders to GraphicsSettings.m_AlwaysIncludedShaders.
///
/// PROBLEM: Cesium for Unity creates materials at runtime for terrain and imagery tiles.
/// The shaders these materials use (CesiumUnlitTexture, Standard, etc.) have no direct
/// asset reference in the project. Without an asset reference, Unity's shader stripping
/// removes them from IL2CPP UWP builds, causing terrain to be invisible on HoloLens 2.
/// Buildings are unaffected because CesiumFeatureColorizer replaces their materials with
/// VertexColoredBuilding.shader, which IS in AlwaysIncludedShaders.
///
/// FIX: This script adds the required shaders both at Editor startup and as a pre-build
/// step, guaranteeing they're never stripped.
/// </summary>
[InitializeOnLoad]
public class CesiumShaderProtection : IPreprocessBuildWithReport
{
    public int callbackOrder => -10; // Run early, before other build steps

    static CesiumShaderProtection()
    {
        EditorApplication.delayCall += EnsureShadersIncluded;
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        EnsureShadersIncluded();
    }

    [MenuItem("Tools/HoloLens/Protect Cesium Shaders")]
    public static void EnsureShadersIncluded()
    {
        // Shaders that Cesium for Unity uses at runtime for terrain/imagery tiles.
        // These must survive shader stripping for terrain to be visible.
        string[] requiredShaderNames = new string[]
        {
            "Standard",                          // Unity Standard — Cesium default opaque
            "Unlit/Texture",                     // Cesium uses for some imagery overlays
            "Unlit/Color",                       // Fallback for untextured tiles
            "Hidden/Internal-Colored",           // Unity built-in, used by various systems
            "Hidden/ForceAlphaOnly",             // ForceOpaqueAlpha MRC alpha fix shader
            "Sprites/Default",                   // Used by XRInteractionFeedback cursor/ring
            "Legacy Shaders/Diffuse",            // Cesium fallback
        };

        SerializedObject graphicsSettings = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        SerializedProperty alwaysIncluded = graphicsSettings.FindProperty("m_AlwaysIncludedShaders");

        bool changed = false;

        foreach (string shaderName in requiredShaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"CesiumShaderProtection: Shader '{shaderName}' not found — skipping");
                continue;
            }

            // Check if already in the list
            bool alreadyIncluded = false;
            for (int i = 0; i < alwaysIncluded.arraySize; i++)
            {
                SerializedProperty element = alwaysIncluded.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue == shader)
                {
                    alreadyIncluded = true;
                    break;
                }
            }

            if (!alreadyIncluded)
            {
                int newIndex = alwaysIncluded.arraySize;
                alwaysIncluded.InsertArrayElementAtIndex(newIndex);
                alwaysIncluded.GetArrayElementAtIndex(newIndex).objectReferenceValue = shader;
                Debug.Log($"<color=green>✅ Added '{shaderName}' to Always Included Shaders</color>");
                changed = true;
            }
        }

        if (changed)
        {
            graphicsSettings.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("<color=cyan>🔒 Cesium terrain shaders protected from IL2CPP stripping.</color>");
        }
        else
        {
            Debug.Log("<color=cyan>✓ All required shaders already in Always Included Shaders.</color>");
        }
    }
}
