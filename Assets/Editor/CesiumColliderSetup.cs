using UnityEngine;
using UnityEditor;
using CesiumForUnity;

/// <summary>
/// Ensures Cesium3DTileset generates colliders for raycasting.
/// Without colliders, tapping buildings won't work on HoloLens 2.
/// </summary>
[InitializeOnLoad]
public class CesiumColliderSetup
{
    static CesiumColliderSetup()
    {
        EditorApplication.delayCall += SetupCesiumColliders;
    }

    static void SetupCesiumColliders()
    {
        Cesium3DTileset[] tilesets = Object.FindObjectsOfType<Cesium3DTileset>();
        if (tilesets.Length == 0)
        {
            Debug.LogWarning("CesiumColliderSetup: No Cesium3DTileset found in scene!");
            return;
        }

        bool madeChanges = false;
        foreach (var tileset in tilesets)
        {
            // Check if createPhysicsMeshes is enabled
            if (!tileset.createPhysicsMeshes)
            {
                tileset.createPhysicsMeshes = true;
                Debug.Log($"<color=green>✅ Enabled Physics Meshes on {tileset.name}</color>");
                EditorUtility.SetDirty(tileset);
                madeChanges = true;
            }
        }

        if (madeChanges)
        {
            Debug.Log("<color=yellow>⚠️ Cesium collider settings changed. Save the scene for changes to persist!</color>");
        }
    }

    [MenuItem("Tools/HoloLens/Enable Cesium Colliders")]
    public static void ManualSetup()
    {
        SetupCesiumColliders();
    }
}
