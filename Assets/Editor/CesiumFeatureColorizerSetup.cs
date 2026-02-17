using UnityEngine;
using UnityEditor;
using CesiumForUnity;

/// <summary>
/// Editor utility to automatically setup CesiumFeatureColorizer on the Cesium3DTileset
/// Menu: Tools > Cesium > Setup Feature Colorizer
/// </summary>
public class CesiumFeatureColorizerSetup : MonoBehaviour
{
#if UNITY_EDITOR
    [MenuItem("Tools/Cesium/Setup Feature Colorizer")]
    static void SetupColorizer()
    {
        // Find BuildingEnergyManager
        BuildingEnergyManager energyManager = FindObjectOfType<BuildingEnergyManager>();
        if (energyManager == null)
        {
            Debug.LogError("❌ BuildingEnergyManager not found in scene! Please add it first.");
            return;
        }

        // Find Cesium3DTileset
        Cesium3DTileset tileset = FindObjectOfType<Cesium3DTileset>();
        if (tileset == null)
        {
            Debug.LogError("❌ Cesium3DTileset not found in scene!");
            return;
        }

        // Check if CesiumFeatureColorizer already exists
        CesiumFeatureColorizer existingColorizer = tileset.GetComponent<CesiumFeatureColorizer>();
        if (existingColorizer != null)
        {
            Debug.LogWarning("⚠️ CesiumFeatureColorizer already exists on " + tileset.name);
            Selection.activeGameObject = tileset.gameObject;
            return;
        }

        // Add CesiumFeatureColorizer component
        CesiumFeatureColorizer colorizer = tileset.gameObject.AddComponent<CesiumFeatureColorizer>();
        
        // Auto-configure
        colorizer.energyManager = energyManager;
        colorizer.tileset = tileset;
        colorizer.debugMode = true;
        colorizer.vertexColorStrength = 1.0f;

        // Try to find the custom shader
        Shader customShader = Shader.Find("Cesium/VertexColoredBuilding");
        if (customShader != null)
        {
            colorizer.customShader = customShader;
            Debug.Log("✅ Found and assigned custom shader: Cesium/VertexColoredBuilding");
        }
        else
        {
            Debug.LogWarning("⚠️ Custom shader 'Cesium/VertexColoredBuilding' not found. Will use existing materials.");
            Debug.LogWarning("   Make sure VertexColoredBuilding.shader is in the Assets folder.");
        }

        // OLD: Disable old coloring system - field no longer exists (removed)
        // energyManager.enableContinuousColoring = false;

        // Mark scene as dirty
        EditorUtility.SetDirty(tileset.gameObject);
        EditorUtility.SetDirty(energyManager.gameObject);

        // Select the tileset to show the component
        Selection.activeGameObject = tileset.gameObject;

        Debug.Log("<color=green>✅ CesiumFeatureColorizer setup complete!</color>");
        Debug.Log($"  - Attached to: {tileset.name}");
        Debug.Log($"  - Energy Manager: {energyManager.name}");
        Debug.Log($"  - Shader: {(customShader != null ? customShader.name : "None")}");
        Debug.Log($"  - Disabled old continuous coloring system");
        Debug.Log("\n<color=cyan>NEXT STEPS:</color>");
        Debug.Log("1. Enter Play Mode");
        Debug.Log("2. Wait for building data to load (5005 buildings)");
        Debug.Log("3. Buildings should automatically color as tiles load");
        Debug.Log("4. Check Console for: 'CesiumFeatureColorizer: Colored X vertices...'");
    }

    [MenuItem("Tools/Cesium/Recolor All Tiles (Runtime Only)", false, 1)]
    static void RecolorTiles()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("⚠️ This function only works in Play Mode!");
            return;
        }

        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        if (colorizer == null)
        {
            Debug.LogError("❌ CesiumFeatureColorizer not found! Run 'Setup Feature Colorizer' first.");
            return;
        }

        Debug.Log("<color=cyan>Triggering manual recolor of all loaded tiles...</color>");
        colorizer.RecolorAllTiles();
    }

    [MenuItem("Tools/Cesium/Show Colorizer Statistics (Runtime Only)", false, 2)]
    static void ShowStatistics()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("⚠️ This function only works in Play Mode!");
            return;
        }

        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        if (colorizer == null)
        {
            Debug.LogError("❌ CesiumFeatureColorizer not found!");
            return;
        }

        Debug.Log("<color=cyan>=== CesiumFeatureColorizer Statistics ===</color>");
        Debug.Log(colorizer.GetStatistics());
        
        BuildingEnergyManager energyManager = colorizer.energyManager;
        if (energyManager != null)
        {
            Debug.Log($"Cached building data: {energyManager.buildingDataCache.Count}");
            Debug.Log($"Cached colors: {energyManager.buildingColorCache.Count}");
        }
    }
#endif
}
