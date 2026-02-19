using UnityEngine;
using UnityEditor;
using UnityEngine.SpatialTracking;

/// <summary>
/// CRITICAL: Auto-configures Main Camera for HoloLens 2 XR tracking.
/// Runs automatically when scene loads in Editor.
/// </summary>
[InitializeOnLoad]
public class HoloLensXRCameraSetup
{
    static HoloLensXRCameraSetup()
    {
        EditorApplication.delayCall += SetupMainCameraForXR;
    }

    static void SetupMainCameraForXR()
    {
        // Find Main Camera
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogWarning("HoloLensXRCameraSetup: No Main Camera found!");
            return;
        }

        // Check if TrackedPoseDriver exists
        TrackedPoseDriver tpd = mainCam.GetComponent<TrackedPoseDriver>();
        if (tpd == null)
        {
            tpd = mainCam.gameObject.AddComponent<TrackedPoseDriver>();
            tpd.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            
            Debug.Log($"<color=green>✅ Added TrackedPoseDriver to {mainCam.name} for HoloLens 2 head tracking</color>");
            EditorUtility.SetDirty(mainCam.gameObject);
        }
    }

    [MenuItem("Tools/HoloLens/Setup Main Camera for XR")]
    public static void ManualSetup()
    {
        SetupMainCameraForXR();
    }
}
