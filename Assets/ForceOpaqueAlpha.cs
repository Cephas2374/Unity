using UnityEngine;

/// <summary>
/// Forces alpha=1.0 on rendered pixels for HoloLens 2 Mixed Reality Capture (MRC).
///
/// On HoloLens 2, MRC composites holograms over the real-world camera using the alpha channel.
/// Cesium shaders write alpha=0 for opaque geometry, making terrain invisible in recordings.
///
/// IMPORTANT: This must NOT run for the live holographic display — forcing alpha=1 globally
/// would make the transparent background (real-world see-through) appear as solid black.
/// This script only activates when MRC is recording, detected by checking if Unity has added
/// the photo/video camera (MRC uses an additional camera).
///
/// Attach to the Main Camera (auto-attached by HoloLensXRCameraSetup).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ForceOpaqueAlpha : MonoBehaviour
{
    private Material forceAlphaMat;
    private bool isMRCActive = false;
    private float mrcCheckTimer = 0f;
    private const float MRC_CHECK_INTERVAL = 1.0f; // Check every second

    void Update()
    {
        // Periodically check if MRC is recording by counting cameras.
        // When MRC starts, Unity adds a "Photo Video Camera" to the scene.
        mrcCheckTimer += Time.deltaTime;
        if (mrcCheckTimer >= MRC_CHECK_INTERVAL)
        {
            mrcCheckTimer = 0f;
            Camera[] allCameras = Camera.allCameras;
            isMRCActive = allCameras.Length > 1; // More than just Main Camera = MRC is active
        }
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        // Always blit the image through
        Graphics.Blit(src, dest);
        
        // Only force alpha=1 when MRC is recording
        if (!isMRCActive)
        {
            return;
        }

        if (forceAlphaMat == null)
        {
            forceAlphaMat = new Material(Shader.Find("Hidden/Internal-Colored"));
            if (forceAlphaMat == null)
            {
                return;
            }
        }

        // Force alpha=1 on the destination so MRC sees holograms as opaque
        GL.PushMatrix();
        GL.LoadOrtho();

        var prevRT = RenderTexture.active;
        RenderTexture.active = dest;

        // Draw fullscreen quad writing only alpha=1
        GL.Begin(GL.QUADS);
        GL.Color(new Color(0, 0, 0, 1));
        GL.Vertex3(0, 0, 0);
        GL.Vertex3(1, 0, 0);
        GL.Vertex3(1, 1, 0);
        GL.Vertex3(0, 1, 0);
        GL.End();

        RenderTexture.active = prevRT;
        GL.PopMatrix();
    }

    void OnDestroy()
    {
        if (forceAlphaMat != null)
            Destroy(forceAlphaMat);
    }
}
