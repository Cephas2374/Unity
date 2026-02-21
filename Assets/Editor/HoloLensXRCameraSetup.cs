using UnityEngine;
using UnityEditor;
using UnityEngine.SpatialTracking;

/// <summary>
/// CRITICAL: Auto-configures Main Camera for HoloLens 2 XR tracking.
/// Runs automatically when scene loads in Editor.
/// 
/// Fixes applied:
/// 1. TrackedPoseDriver for 6DOF head tracking
/// 2. ClearFlags → Skybox (renders CesiumSkyWithClouds procedural sky + clouds)
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

        // === Camera clear flags: Skybox for immersive 3D scene ===
        // Using Skybox mode so the CesiumSkyWithClouds procedural skybox renders
        // behind the Cesium terrain. This gives a natural sky with volumetric clouds.
        //
        // NOTE: If you want AR see-through mode (real world visible behind holograms),
        // change to CameraClearFlags.SolidColor with backgroundColor = (0,0,0,0).
        // ForceOpaqueAlpha.cs handles MRC alpha in both modes.
        if (mainCam.clearFlags != CameraClearFlags.Skybox)
        {
            mainCam.clearFlags = CameraClearFlags.Skybox;
            Debug.Log($"<color=green>✅ Set {mainCam.name} clearFlags=Skybox for CesiumSkyWithClouds</color>");
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
