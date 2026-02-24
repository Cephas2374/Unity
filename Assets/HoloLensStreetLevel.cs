using UnityEngine;
using UnityEngine.XR;
using CesiumForUnity;
using System.Collections;

/// <summary>
/// Adjusts CesiumGeoreference origin height at runtime on HoloLens 2 so that
/// the XR tracking floor (Y=0) corresponds to street level next to buildings.
///
/// PROBLEM:
/// In the desktop editor, the camera is manually positioned at localPosition.y ≈ -38
/// to achieve street-level viewing of the Cesium 3D Tiles buildings.
/// On HoloLens 2, TrackedPoseDriver overrides the camera's localPosition with head
/// tracking data (Y ≈ 0 at floor, Y ≈ 1.6 at head). This places the user ~38m
/// ABOVE the buildings, resulting in a top-down bird's-eye view and visible
/// parallax jitter from head-tracking micro-movements.
///
/// SOLUTION:
/// Lower the CesiumGeoreference origin height by the desktop camera's Y offset (~38m).
/// This shifts all georeferenced content (buildings, terrain) upward in Unity's local
/// coordinate system so that building bases align with the XR floor plane (Y ≈ 0).
/// The user then sees buildings at natural eye level (Y ≈ 1.6m above ground).
///
/// This only runs on XR devices; desktop view is unaffected.
/// </summary>
public class HoloLensStreetLevel : MonoBehaviour
{
    [Header("Street Level Configuration")]
    [Tooltip("Meters to lower the georeference origin. Should match the absolute value " +
             "of the desktop camera's Y position within the CesiumGeoreference hierarchy. " +
             "Default 38.0 comes from DynamicCamera.localPosition.y ≈ -37.99 in the scene.")]
    public double streetLevelOffset = 38.0;

    [Tooltip("Only apply on XR devices. Disable for testing in Editor.")]
    public bool xrOnly = true;

    [Tooltip("Maximum frames to wait for XR system initialization before giving up.")]
    public int maxXRWaitFrames = 120;

    private CesiumGeoreference geoRef;
    #pragma warning disable CS0414
    private bool applied = false;
    #pragma warning restore CS0414

    void Start()
    {
        StartCoroutine(ApplyStreetLevelAdjustment());
    }

    IEnumerator ApplyStreetLevelAdjustment()
    {
        // Wait for XR subsystem to fully initialize.
        // On HoloLens 2, XRSettings.isDeviceActive may not be true on the very first frame.
        bool xrActive = false;
        for (int i = 0; i < maxXRWaitFrames; i++)
        {
            if (XRSettings.isDeviceActive)
            {
                xrActive = true;
                Debug.Log($"<color=cyan>HoloLensStreetLevel: XR detected on frame {i}</color>");
                break;
            }
            yield return null;
        }

        if (xrOnly && !xrActive)
        {
            Debug.Log("<color=grey>HoloLensStreetLevel: Not an XR device — skipping height adjustment</color>");
            yield break;
        }

        // Find the CesiumGeoreference in the scene
        geoRef = FindObjectOfType<CesiumGeoreference>();
        if (geoRef == null)
        {
            Debug.LogError("<color=red>HoloLensStreetLevel: CesiumGeoreference not found!</color>");
            yield break;
        }

        // Lower the georeference origin so buildings appear at floor level
        double originalHeight = geoRef.height;
        double newHeight = originalHeight - streetLevelOffset;
        geoRef.height = newHeight;
        applied = true;

        Debug.Log($"<color=green>✅ HoloLensStreetLevel: Adjusted georeference height</color>");
        Debug.Log($"<color=green>   Original: {originalHeight:F2}m → New: {newHeight:F2}m (lowered by {streetLevelOffset}m)</color>");
        Debug.Log($"<color=green>   Buildings now at floor level. User eye height ≈ 1.6m above ground.</color>");
    }

    /// <summary>
    /// For runtime fine-tuning via UI or voice commands.
    /// Positive values lower the camera (raise buildings), negative raises the camera.
    /// </summary>
    public void AdjustOffset(double additionalMeters)
    {
        if (geoRef == null)
        {
            geoRef = FindObjectOfType<CesiumGeoreference>();
            if (geoRef == null) return;
        }

        geoRef.height -= additionalMeters;
        streetLevelOffset += additionalMeters;
        Debug.Log($"<color=yellow>HoloLensStreetLevel: Fine-tuned by {additionalMeters:F1}m. New height: {geoRef.height:F2}m</color>");
    }
}
