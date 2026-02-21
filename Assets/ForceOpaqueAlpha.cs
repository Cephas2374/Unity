using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Forces alpha=1.0 on all rendered pixels for HoloLens 2 Mixed Reality Capture (MRC).
///
/// On HoloLens 2, MRC composites holograms over the real-world camera feed using the alpha
/// channel. Cesium and other shaders may write alpha=0 for opaque geometry, making content
/// invisible in MRC recordings.
///
/// This script performs a single fullscreen blit using ForceAlphaOnly.shader, which copies
/// RGB from the rendered frame unchanged and sets alpha=1.0. It runs every frame but has
/// ZERO visual impact on the live HoloLens 2 display — HoloLens 2 is an additive see-through
/// display that ignores the alpha channel entirely. Only MRC uses alpha for compositing.
///
/// MRC CAMERA SUPPORT:
///   Main Camera: uses OnRenderImage (blit through ForceAlphaOnly shader).
///   All other cameras (MRC "Photo Video Camera", etc.): uses a CommandBuffer added
///   via Camera.onPreRender static callback. Zero per-frame allocations — the CommandBuffer
///   is created once per camera and reused automatically.
///
/// PREVIOUS FLASHING/CRASH CAUSE (now fixed):
///   The prior version used Camera.allCameras + AddComponent in LateUpdate every frame,
///   allocating a new Camera[] array + GetComponent calls 60x/sec. On HoloLens 2 with
///   limited RAM, this triggered frequent GC pauses (visible as periodic flashing) and
///   eventually crashed from memory pressure.
///
/// The shader is stereo-aware (supports single-pass instanced rendering on HoloLens 2).
///
/// Attach to the Main Camera (auto-attached by HoloLensXRCameraSetup).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ForceOpaqueAlpha : MonoBehaviour
{
    private Material forceAlphaMat;
    private Camera selfCamera;

    // Track cameras that already received a CommandBuffer (zero per-frame allocation)
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

        // Subscribe to static event that fires before ANY camera renders.
        // Catches the MRC "Photo Video Camera" HoloLens creates during recording.
        Camera.onPreRender += OnAnyCameraPreRender;
    }

    /// <summary>
    /// Main Camera: single-pass blit copies RGB and forces alpha=1.
    /// Only fires on the camera this component is attached to.
    /// </summary>
    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (forceAlphaMat != null)
        {
            Graphics.Blit(src, dest, forceAlphaMat);
        }
        else
        {
            Graphics.Blit(src, dest);
        }
    }

    /// <summary>
    /// Static callback: fires before every camera renders.
    /// Adds a one-time CommandBuffer to any NEW camera (MRC, etc.) to force alpha=1.
    /// Zero per-frame allocation: only does work the first time a camera is seen.
    /// </summary>
    void OnAnyCameraPreRender(Camera cam)
    {
        // Skip Main Camera — handled by OnRenderImage
        if (cam == selfCamera) return;

        int camId = cam.GetInstanceID();
        if (processedCameraIds.Contains(camId)) return;

        // New camera detected (e.g., MRC Photo Video Camera)
        if (forceAlphaMat == null) return;

        CommandBuffer cb = new CommandBuffer();
        cb.name = "ForceAlphaOnly_MRC";

        // Blit camera output through ForceAlphaOnly shader (copies RGB, sets alpha=1)
        int tmpId = Shader.PropertyToID("_ForceAlphaTmpMRC");
        cb.GetTemporaryRT(tmpId, -1, -1, 0, FilterMode.Point);
        cb.Blit(BuiltinRenderTextureType.CameraTarget, tmpId);
        cb.Blit(tmpId, BuiltinRenderTextureType.CameraTarget, forceAlphaMat);
        cb.ReleaseTemporaryRT(tmpId);

        cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
        processedCameraIds.Add(camId);

        Debug.Log($"<color=green>[ForceOpaqueAlpha] Added alpha fix CommandBuffer to '{cam.name}'</color>");
    }

    void OnDestroy()
    {
        Camera.onPreRender -= OnAnyCameraPreRender;
        processedCameraIds.Clear();

        if (forceAlphaMat != null)
        {
            Destroy(forceAlphaMat);
            forceAlphaMat = null;
        }
    }
}
