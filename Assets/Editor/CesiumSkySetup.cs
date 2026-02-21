using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Auto-creates a CesiumSkyWithClouds material and assigns it as the scene's skybox.
/// Runs at Editor startup and as a menu item.
///
/// The material is saved at Assets/CesiumSkyWithClouds.mat so it persists across sessions.
/// If the material already exists and is already assigned, this script does nothing.
/// </summary>
[InitializeOnLoad]
public class CesiumSkySetup
{
    private const string MAT_PATH = "Assets/CesiumSkyWithClouds.mat";
    private const string SHADER_NAME = "Skybox/CesiumSkyWithClouds";

    static CesiumSkySetup()
    {
        EditorApplication.delayCall += SetupSky;
    }

    static void SetupSky()
    {
        // Already using our sky material?
        if (RenderSettings.skybox != null &&
            RenderSettings.skybox.shader != null &&
            RenderSettings.skybox.shader.name == SHADER_NAME)
        {
            return;
        }

        Shader skyShader = Shader.Find(SHADER_NAME);
        if (skyShader == null)
        {
            Debug.LogWarning("CesiumSkySetup: Shader '" + SHADER_NAME + "' not found. " +
                             "Make sure CesiumSkyWithClouds.shader is in Assets/.");
            return;
        }

        // Load existing material or create a new one
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
        if (mat == null)
        {
            mat = new Material(skyShader);
            AssetDatabase.CreateAsset(mat, MAT_PATH);
            AssetDatabase.SaveAssets();
            Debug.Log("<color=green>✅ Created CesiumSkyWithClouds material at " + MAT_PATH + "</color>");
        }
        else if (mat.shader != skyShader)
        {
            mat.shader = skyShader;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }

        // Assign as the scene's skybox
        RenderSettings.skybox = mat;

        // Set environment lighting source to Skybox so GI picks up the sky colours
        RenderSettings.ambientMode = AmbientMode.Skybox;

        // Update GI / reflection probes
        DynamicGI.UpdateEnvironment();

        Debug.Log("<color=green>✅ Skybox set to CesiumSkyWithClouds — volumetric clouds enabled</color>");
    }

    [MenuItem("Tools/HoloLens/Setup Cesium Sky + Clouds")]
    public static void ManualSetup()
    {
        SetupSky();
    }
}
