using UnityEngine;
using UnityEditor;
using UnityEngine.SpatialTracking;
using UnityEngine.Rendering;

/// <summary>
/// CRITICAL: Auto-configures Main Camera for HoloLens 2 XR tracking.
/// Runs automatically when scene loads in Editor.
/// 
/// Fixes applied:
/// 1. TrackedPoseDriver for 6DOF head tracking
/// 2. ClearFlags → SolidColor with transparent black (required for AR see-through)
/// 3. ForceOpaqueAlpha for MRC video capture
/// 4. HoloLensNavigationUI for AR navigation buttons
/// 5. XRInteractionFeedback for cursor, ring, audio & haptics
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

        // === CRITICAL: Camera clear flags for HoloLens 2 AR ===
        // HoloLens 2 is a see-through AR device. ClearFlags MUST be SolidColor
        // with transparent black (0,0,0,0) so the real world shows through.
        // Skybox mode renders an opaque background that occludes/hides terrain tiles.
        if (mainCam.clearFlags != CameraClearFlags.SolidColor)
        {
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            Debug.Log($"<color=green>✅ Set {mainCam.name} clearFlags=SolidColor, bg=transparent for HoloLens 2 AR</color>");
            EditorUtility.SetDirty(mainCam.gameObject);
        }
        else if (mainCam.backgroundColor.a > 0.01f)
        {
            mainCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            Debug.Log($"<color=green>✅ Set {mainCam.name} background to transparent black for AR</color>");
            EditorUtility.SetDirty(mainCam.gameObject);
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

        // Auto-add ForceOpaqueAlpha for MRC video recording compatibility
        ForceOpaqueAlpha forceAlpha = mainCam.GetComponent<ForceOpaqueAlpha>();
        if (forceAlpha == null)
        {
            mainCam.gameObject.AddComponent<ForceOpaqueAlpha>();
            Debug.Log($"<color=green>✅ Added ForceOpaqueAlpha to {mainCam.name} for MRC video capture</color>");
            EditorUtility.SetDirty(mainCam.gameObject);
        }

        // Auto-add HoloLensNavigationUI for AR navigation buttons
        HoloLensNavigationUI navUI = mainCam.GetComponent<HoloLensNavigationUI>();
        if (navUI == null)
        {
            navUI = mainCam.gameObject.AddComponent<HoloLensNavigationUI>();
            Debug.Log($"<color=green>✅ Added HoloLensNavigationUI to {mainCam.name} for AR navigation</color>");
            EditorUtility.SetDirty(mainCam.gameObject);
        }

        // Auto-add XRInteractionFeedback for gaze cursor, progress ring, audio & haptics
        XRInteractionFeedback xrFeedback = mainCam.GetComponent<XRInteractionFeedback>();
        if (xrFeedback == null)
        {
            mainCam.gameObject.AddComponent<XRInteractionFeedback>();
            Debug.Log($"<color=green>✅ Added XRInteractionFeedback to {mainCam.name} for interaction feedback</color>");
            EditorUtility.SetDirty(mainCam.gameObject);
        }
    }

    [MenuItem("Tools/HoloLens/Setup Main Camera for XR")]
    public static void ManualSetup()
    {
        SetupMainCameraForXR();
    }
}
