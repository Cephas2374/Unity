using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Forces alpha=1.0 on all rendered pixels for HoloLens 2 Mixed Reality Capture (MRC).
///
/// On HoloLens 2, MRC composites holograms over the real-world camera feed using the alpha
/// channel. Cesium terrain and other shaders may write alpha=0 for opaque geometry, making
/// content invisible in MRC recordings.
///
/// This script uses a CommandBuffer at CameraEvent.AfterEverything that writes ONLY to the
/// alpha channel (via ForceAlphaOnly.shader with ColorMask A), setting every pixel's alpha
/// to 1.0 while leaving RGB untouched.
///
/// WHY CommandBuffer INSTEAD OF OnRenderImage:
///   OnRenderImage forces the camera to render to an INTERMEDIATE RenderTexture that is
///   then blitted to the display. HoloLens 2 MRC may read the camera output BEFORE
///   OnRenderImage runs — from the intermediate RT where alpha is still whatever the scene
///   shaders wrote (often 0 for Cesium terrain). This makes terrain invisible in MRC.
///
///   CommandBuffers execute within the GPU command queue at the specified CameraEvent,
///   modifying the camera target DIRECTLY. No intermediate RT is created. MRC captures
///   the final camera target with alpha already set to 1.0.
///
/// COLORMASK A APPROACH:
///   The ForceAlphaOnly shader uses ColorMask A, which writes ONLY to the alpha channel.
///   This means no temporary RT is needed (no source-to-temp-to-dest copy), eliminating
///   stereo texture array issues with single-pass instanced rendering on HoloLens 2.
///   A Blit with a dummy source (Texture2D.whiteTexture) draws a fullscreen quad, and the
///   shader outputs alpha=1 while RGB stays untouched.
///
/// MRC CAMERA SUPPORT:
///   All cameras get the same CommandBuffer treatment. The main camera gets one at Start().
///   A static Camera.onPreRender callback detects any new cameras (MRC "Photo Video Camera",
///   etc.) and adds the CommandBuffer on first sight. A HashSet tracks processed camera IDs
///   for zero per-frame allocation.
///
/// ZERO VISUAL IMPACT on the live HoloLens 2 display — HoloLens 2 is an additive
/// see-through display that ignores the alpha channel entirely. Only MRC uses alpha.
///
/// Attach to the Main Camera (auto-attached by HoloLensXRCameraSetup).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ForceOpaqueAlpha : MonoBehaviour
{
    private Material forceAlphaMat;
    private Camera selfCamera;
    private CommandBuffer mainCB;

    // Track cameras that already have a CommandBuffer (zero per-frame allocation)
    private static readonly HashSet<int> processedCameraIds = new HashSet<int>();

    void Start()
    {
        selfCamera = GetComponent<Camera>();

        Shader shader = Shader.Find("Hidden/ForceAlphaOnly");
        if (shader != null && shader.isSupported)
        {
            forceAlphaMat = new Material(shader);
            forceAlphaMat.hideFlags = HideFlags.HideAndDontSave;
        }
        else
        {
            Debug.LogWarning("[ForceOpaqueAlpha] Hidden/ForceAlphaOnly shader not found. MRC alpha fix disabled.");
            enabled = false;
            return;
        }

        // Add alpha fix to main camera via CommandBuffer (NOT OnRenderImage).
        mainCB = CreateAlphaFixCB("ForceAlphaOnly_Main");
        selfCamera.AddCommandBuffer(CameraEvent.AfterEverything, mainCB);
        processedCameraIds.Add(selfCamera.GetInstanceID());

        // Subscribe to static event that fires before ANY camera renders.
        // Catches the MRC "Photo Video Camera" HoloLens creates during recording.
        Camera.onPreRender += OnAnyCameraPreRender;

        Debug.Log($"<color=green>[ForceOpaqueAlpha] Alpha fix active on '{selfCamera.name}' via CommandBuffer (ColorMask A, no intermediate RT)</color>");
    }

    /// <summary>
    /// Creates a CommandBuffer that draws a fullscreen quad writing only alpha=1.
    /// Uses Blit with a dummy source texture — the ForceAlphaOnly shader has ColorMask A,
    /// so RGB on the camera target is preserved and only alpha is overwritten.
    /// No temporary RenderTexture needed.
    /// </summary>
    private CommandBuffer CreateAlphaFixCB(string cbName)
    {
        CommandBuffer cb = new CommandBuffer();
        cb.name = cbName;

        // Blit with dummy source — shader ignores source (ColorMask A, outputs alpha=1)
        // This draws a fullscreen quad to CameraTarget, writing ONLY alpha=1.
        cb.Blit(Texture2D.whiteTexture, BuiltinRenderTextureType.CameraTarget, forceAlphaMat);

        return cb;
    }

    /// <summary>
    /// Static callback: fires before every camera renders.
    /// Adds a one-time CommandBuffer to any NEW camera (MRC, etc.) to force alpha=1.
    /// Zero per-frame allocation: only does work the first time a camera is seen.
    /// </summary>
    void OnAnyCameraPreRender(Camera cam)
    {
        int camId = cam.GetInstanceID();
        if (processedCameraIds.Contains(camId)) return;
        if (forceAlphaMat == null) return;

        // New camera detected (e.g., MRC Photo Video Camera)
        CommandBuffer cb = CreateAlphaFixCB("ForceAlphaOnly_MRC");
        cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
        processedCameraIds.Add(camId);

        Debug.Log($"<color=green>[ForceOpaqueAlpha] Added alpha fix CommandBuffer to '{cam.name}'</color>");
    }

    void OnDestroy()
    {
        Camera.onPreRender -= OnAnyCameraPreRender;

        if (selfCamera != null && mainCB != null)
        {
            selfCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, mainCB);
            mainCB.Release();
            mainCB = null;
        }

        processedCameraIds.Clear();

        if (forceAlphaMat != null)
        {
            Destroy(forceAlphaMat);
            forceAlphaMat = null;
        }
    }
}
