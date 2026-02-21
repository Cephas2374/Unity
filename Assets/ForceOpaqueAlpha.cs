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
/// The shader is stereo-aware (supports single-pass instanced rendering on HoloLens 2).
///
/// Previous version caused FLASHING AND CRASHES because:
///  1. GL.Color(0,0,0,1) drew a BLACK quad — overwrote RGB, not just alpha
///  2. MRC detection toggled on/off every second — visual flicker
///  3. Double render pass (Blit + GL immediate mode) — overloaded HoloLens GPU
///
/// Attach to the Main Camera (auto-attached by HoloLensXRCameraSetup).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ForceOpaqueAlpha : MonoBehaviour
{
    private Material forceAlphaMat;

    void Start()
    {
        Shader shader = Shader.Find("Hidden/ForceAlphaOnly");
        if (shader != null && shader.isSupported)
        {
            forceAlphaMat = new Material(shader);
            forceAlphaMat.hideFlags = HideFlags.HideAndDontSave;
        }
        else
        {
            Debug.LogWarning("ForceOpaqueAlpha: Hidden/ForceAlphaOnly shader not found. " +
                             "MRC alpha fix disabled. Ensure ForceAlphaOnly.shader is in Assets/.");
            // Disable this component so OnRenderImage is never called — avoids
            // pointless render-to-texture overhead on HoloLens 2.
            enabled = false;
        }
    }

    /// <summary>
    /// Single-pass blit: copies RGB from the rendered frame and sets alpha=1.
    /// No GL immediate mode, no toggling, no double blit.
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

    void OnDestroy()
    {
        if (forceAlphaMat != null)
        {
            Destroy(forceAlphaMat);
            forceAlphaMat = null;
        }
    }
}
