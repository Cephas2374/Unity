using UnityEngine;

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
/// CRITICAL FOR MRC: The main instance (on Main Camera) dynamically adds this component to
/// ALL other cameras, including the "Photo Video Camera" that HoloLens creates for MRC
/// recording. Without this, terrain was visible live but invisible in MRC video because the
/// MRC camera didn't get the alpha fix. Buildings were visible because VertexColoredBuilding
/// shader writes alpha=1 natively.
///
/// The shader is stereo-aware (supports single-pass instanced rendering on HoloLens 2).
///
/// Attach to the Main Camera (auto-attached by HoloLensXRCameraSetup).
/// Auto-propagates to any additional cameras (MRC, scene capture, etc.).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ForceOpaqueAlpha : MonoBehaviour
{
    private Material forceAlphaMat;

    /// <summary>
    /// True for instances auto-added to non-Main cameras (e.g., MRC Photo Video Camera).
    /// Only the original (Main Camera) instance monitors for new cameras.
    /// </summary>
    [HideInInspector]
    public bool isAutoAdded = false;

    void Start()
    {
        Shader shader = Shader.Find("Hidden/ForceAlphaOnly");
        if (shader != null && shader.isSupported)
        {
            forceAlphaMat = new Material(shader);
            forceAlphaMat.hideFlags = HideFlags.HideAndDontSave;

            if (isAutoAdded)
            {
                Debug.Log($"<color=green>[ForceOpaqueAlpha] ✅ Alpha fix active on '{gameObject.name}' (MRC camera)</color>");
            }
        }
        else
        {
            Debug.LogWarning("ForceOpaqueAlpha: Hidden/ForceAlphaOnly shader not found. " +
                             "MRC alpha fix disabled. Ensure ForceAlphaOnly.shader is in Assets/.");
            enabled = false;
        }
    }

    /// <summary>
    /// Single-pass blit: copies RGB from the rendered frame and sets alpha=1.
    /// Runs on EVERY camera this component is attached to — including MRC cameras.
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
    /// Monitor for new cameras (e.g., MRC "Photo Video Camera") and add ForceOpaqueAlpha.
    /// Only the original Main Camera instance performs this check.
    /// HoloLens creates the MRC camera dynamically when recording starts.
    /// </summary>
    void LateUpdate()
    {
        if (isAutoAdded) return;

        Camera[] allCams = Camera.allCameras;
        for (int i = 0; i < allCams.Length; i++)
        {
            if (allCams[i].GetComponent<ForceOpaqueAlpha>() == null)
            {
                ForceOpaqueAlpha added = allCams[i].gameObject.AddComponent<ForceOpaqueAlpha>();
                added.isAutoAdded = true;
                Debug.Log($"<color=green>[ForceOpaqueAlpha] Added to camera '{allCams[i].name}' for MRC compatibility</color>");
            }
        }
    }

    void OnDestroy()
    {
        if (forceAlphaMat != null)
        {
            Destroy(forceAlphaMat);
            forceAlphaMat = null;
        }
    }
}
