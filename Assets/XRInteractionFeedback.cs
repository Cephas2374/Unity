using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using System.Collections.Generic;

/// <summary>
/// Provides visual, audio, and haptic feedback for XR interactions on HoloLens 2.
/// 
/// Features:
/// - Gaze cursor (small dot on surfaces showing where the hand ray points)
/// - Hold-progress ring (radial fill that shows hold duration approaching threshold)
/// - Audio feedback (tap click, hold-complete sound, error buzz)
/// - Haptic pulse on hand controllers
/// 
/// Attach to the same GameObject as CesiumMetadataReader.
/// </summary>
public class XRInteractionFeedback : MonoBehaviour
{
    [Header("Cursor Settings")]
    [Tooltip("Size of the reticle dot in world units")]
    public float cursorSize = 0.015f;
    
    [Tooltip("Color of the reticle when idle (pointing at nothing)")]
    public Color cursorIdleColor = new Color(1f, 1f, 1f, 0.6f);
    
    [Tooltip("Color of the reticle when pointing at a building")]
    public Color cursorHoverColor = new Color(0f, 0.75f, 1f, 0.9f);
    
    [Tooltip("Color of the reticle during hold gesture")]
    public Color cursorHoldColor = new Color(1f, 0.6f, 0f, 0.95f);
    
    [Header("Progress Ring")]
    [Tooltip("Outer radius of the hold-progress ring")]
    public float ringRadius = 0.025f;
    
    [Tooltip("Color of the progress ring as it fills")]
    public Color ringColor = new Color(1f, 0.6f, 0f, 0.9f);
    
    [Tooltip("Color when ring completes (hold threshold reached)")]
    public Color ringCompleteColor = new Color(0f, 1f, 0.4f, 0.95f);
    
    [Header("Audio")]
    [Tooltip("Volume for feedback sounds (0-1)")]
    [Range(0f, 1f)]
    public float audioVolume = 0.3f;
    
    [Header("Haptics")]
    [Tooltip("Haptic pulse amplitude for tap (0-1)")]
    [Range(0f, 1f)]
    public float tapHapticAmplitude = 0.4f;
    
    [Tooltip("Haptic pulse amplitude for hold-complete (0-1)")]
    [Range(0f, 1f)]
    public float holdHapticAmplitude = 0.8f;
    
    // Internal state
    private GameObject cursorDot;
    private MeshRenderer cursorRenderer;
    private Material cursorMaterial;
    
    private GameObject progressRingObj;
    private MeshRenderer ringRenderer;
    private Material ringMaterial;
    private Mesh ringMesh;
    
    private AudioSource audioSource;
    private Camera mainCamera;
    
    // Current state
    private bool isHovering = false;
    private float holdProgress = 0f; // 0 to 1
    private bool holdCompleted = false;
    private Vector3 lastHitPoint;
    private Vector3 lastHitNormal;
    private bool hasHit = false;
    
    // Ring mesh segments
    private const int RING_SEGMENTS = 36;
    
    // Cached XR device lists — reused to avoid per-frame GC allocation
    private readonly List<InputDevice> cachedRayDevices = new List<InputDevice>();
    private readonly List<InputDevice> cachedHapticDevices = new List<InputDevice>();
    
    void Start()
    {
        mainCamera = Camera.main;
        
        CreateCursorDot();
        CreateProgressRing();
        CreateAudioSource();
        
        Debug.Log("<color=cyan>[XRFeedback] Interaction feedback system initialized.</color>");
    }
    
    void CreateCursorDot()
    {
        cursorDot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        cursorDot.name = "XR_GazeCursor";
        cursorDot.transform.localScale = Vector3.one * cursorSize;
        
        // Remove collider so it doesn't interfere with raycasts
        Collider col = cursorDot.GetComponent<Collider>();
        if (col != null) Destroy(col);
        
        cursorRenderer = cursorDot.GetComponent<MeshRenderer>();
        cursorMaterial = new Material(Shader.Find("Sprites/Default"));
        cursorMaterial.color = cursorIdleColor;
        cursorMaterial.renderQueue = 4000; // Render on top
        cursorRenderer.material = cursorMaterial;
        cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cursorRenderer.receiveShadows = false;
        
        cursorDot.SetActive(false);
    }
    
    void CreateProgressRing()
    {
        progressRingObj = new GameObject("XR_ProgressRing");
        MeshFilter mf = progressRingObj.AddComponent<MeshFilter>();
        ringRenderer = progressRingObj.AddComponent<MeshRenderer>();
        
        ringMaterial = new Material(Shader.Find("Sprites/Default"));
        ringMaterial.color = ringColor;
        ringMaterial.renderQueue = 4001; // On top of cursor
        ringRenderer.material = ringMaterial;
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;
        
        ringMesh = new Mesh();
        mf.mesh = ringMesh;
        
        progressRingObj.SetActive(false);
    }
    
    void CreateAudioSource()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // 2D sound (always audible)
        audioSource.volume = audioVolume;
    }
    
    void Update()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            return;
        }
        
        UpdateCursorPosition();
    }
    
    /// <summary>
    /// Continuously cast a ray and move the cursor dot to the hit point.
    /// Called every frame in Update.
    /// </summary>
    void UpdateCursorPosition()
    {
        Ray ray = GetCurrentRay();
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, 100f))
        {
            hasHit = true;
            lastHitPoint = hit.point;
            lastHitNormal = hit.normal;
            
            // Check if hovering over a Cesium building
            bool isCesiumBuilding = hit.collider.GetComponentInParent<CesiumForUnity.Cesium3DTileset>() != null;
            isHovering = isCesiumBuilding;
            
            // Position and show cursor
            cursorDot.SetActive(true);
            cursorDot.transform.position = hit.point + hit.normal * 0.002f; // Slight offset to avoid z-fighting
            cursorDot.transform.rotation = Quaternion.LookRotation(-hit.normal);
            
            // Scale cursor based on distance (keep consistent visual size)
            float distance = Vector3.Distance(mainCamera.transform.position, hit.point);
            float scale = cursorSize * Mathf.Max(1f, distance * 0.5f);
            cursorDot.transform.localScale = Vector3.one * scale;
            
            // Set color based on state
            if (holdProgress > 0f && !holdCompleted)
            {
                cursorMaterial.color = cursorHoldColor;
            }
            else if (isHovering)
            {
                cursorMaterial.color = cursorHoverColor;
            }
            else
            {
                cursorMaterial.color = cursorIdleColor;
            }
            
            // Update progress ring
            if (holdProgress > 0f)
            {
                UpdateProgressRing(hit.point, hit.normal, distance);
                progressRingObj.SetActive(true);
            }
            else
            {
                progressRingObj.SetActive(false);
            }
        }
        else
        {
            hasHit = false;
            isHovering = false;
            cursorDot.SetActive(false);
            progressRingObj.SetActive(false);
        }
    }
    
    /// <summary>
    /// Builds and positions the ring mesh showing hold progress.
    /// </summary>
    void UpdateProgressRing(Vector3 position, Vector3 normal, float distance)
    {
        progressRingObj.transform.position = position + normal * 0.003f;
        progressRingObj.transform.rotation = Quaternion.LookRotation(-normal);
        
        float outerRadius = ringRadius * Mathf.Max(1f, distance * 0.5f);
        float innerRadius = outerRadius * 0.65f;
        
        // How many segments to fill based on progress
        int filledSegments = Mathf.CeilToInt(holdProgress * RING_SEGMENTS);
        if (filledSegments < 1) filledSegments = 1;
        
        // Build ring mesh
        Vector3[] vertices = new Vector3[filledSegments * 4]; // 2 tris per segment = 4 verts
        int[] triangles = new int[filledSegments * 6];
        
        for (int i = 0; i < filledSegments; i++)
        {
            float angle0 = (float)i / RING_SEGMENTS * Mathf.PI * 2f - Mathf.PI / 2f; // Start at top
            float angle1 = (float)(i + 1) / RING_SEGMENTS * Mathf.PI * 2f - Mathf.PI / 2f;
            
            int vi = i * 4;
            vertices[vi + 0] = new Vector3(Mathf.Cos(angle0) * innerRadius, Mathf.Sin(angle0) * innerRadius, 0);
            vertices[vi + 1] = new Vector3(Mathf.Cos(angle0) * outerRadius, Mathf.Sin(angle0) * outerRadius, 0);
            vertices[vi + 2] = new Vector3(Mathf.Cos(angle1) * outerRadius, Mathf.Sin(angle1) * outerRadius, 0);
            vertices[vi + 3] = new Vector3(Mathf.Cos(angle1) * innerRadius, Mathf.Sin(angle1) * innerRadius, 0);
            
            int ti = i * 6;
            triangles[ti + 0] = vi + 0;
            triangles[ti + 1] = vi + 1;
            triangles[ti + 2] = vi + 2;
            triangles[ti + 3] = vi + 0;
            triangles[ti + 4] = vi + 2;
            triangles[ti + 5] = vi + 3;
        }
        
        ringMesh.Clear();
        ringMesh.vertices = vertices;
        ringMesh.triangles = triangles;
        ringMesh.RecalculateNormals();
        
        // Color: orange while filling, green when complete
        ringMaterial.color = holdCompleted ? ringCompleteColor : ringColor;
    }
    
    // === PUBLIC API (called by CesiumMetadataReader) ===
    
    /// <summary>
    /// Update the hold progress indicator. Called each frame during a hold gesture.
    /// </summary>
    /// <param name="progress">0.0 to 1.0 representing progress toward hold threshold</param>
    public void SetHoldProgress(float progress)
    {
        holdProgress = Mathf.Clamp01(progress);
        holdCompleted = false;
    }
    
    /// <summary>
    /// Signal that the hold threshold was reached.
    /// Triggers completion visual + audio + haptic.
    /// </summary>
    public void OnHoldComplete()
    {
        holdProgress = 1f;
        holdCompleted = true;
        PlayHoldCompleteSound();
        SendHapticPulse(holdHapticAmplitude, 0.15f);
    }
    
    /// <summary>
    /// Signal that a quick tap was registered.
    /// Triggers tap audio + light haptic.
    /// </summary>
    public void OnTap()
    {
        PlayTapSound();
        SendHapticPulse(tapHapticAmplitude, 0.05f);
    }
    
    /// <summary>
    /// Reset all visual feedback (called on gesture release).
    /// </summary>
    public void ResetFeedback()
    {
        holdProgress = 0f;
        holdCompleted = false;
        progressRingObj.SetActive(false);
    }
    
    /// <summary>
    /// Play an error/negative feedback sound (e.g., ray didn't hit a building).
    /// </summary>
    public void OnMiss()
    {
        PlayMissSound();
    }
    
    /// <summary>
    /// Whether the cursor is currently hovering over a Cesium building.
    /// </summary>
    public bool IsHoveringBuilding()
    {
        return isHovering && hasHit;
    }
    
    // === AUDIO ===
    
    void PlayTapSound()
    {
        // Generate a short click sound procedurally (no asset files needed)
        AudioClip clip = GenerateTone(800f, 0.06f, 0.5f);
        audioSource.PlayOneShot(clip, audioVolume);
    }
    
    void PlayHoldCompleteSound()
    {
        // Two-tone rising chime for hold completion
        AudioClip clip = GenerateTwoTone(600f, 900f, 0.12f, 0.7f);
        audioSource.PlayOneShot(clip, audioVolume);
    }
    
    void PlayMissSound()
    {
        // Low short buzz for miss
        AudioClip clip = GenerateTone(200f, 0.08f, 0.3f);
        audioSource.PlayOneShot(clip, audioVolume);
    }
    
    /// <summary>
    /// Generate a simple sine wave tone as an AudioClip (no audio files needed).
    /// </summary>
    AudioClip GenerateTone(float frequency, float duration, float volume)
    {
        int sampleRate = 44100;
        int numSamples = (int)(sampleRate * duration);
        float[] samples = new float[numSamples];
        
        for (int i = 0; i < numSamples; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = 1f - (t / duration); // Linear fade-out
            envelope *= envelope; // Quadratic fade for snappier decay
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * volume * envelope;
        }
        
        AudioClip clip = AudioClip.Create("Tone", numSamples, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
    
    /// <summary>
    /// Generate a two-tone rising chime.
    /// </summary>
    AudioClip GenerateTwoTone(float freq1, float freq2, float duration, float volume)
    {
        int sampleRate = 44100;
        int numSamples = (int)(sampleRate * duration);
        int halfSamples = numSamples / 2;
        float[] samples = new float[numSamples];
        
        for (int i = 0; i < numSamples; i++)
        {
            float t = (float)i / sampleRate;
            float freq = (i < halfSamples) ? freq1 : freq2;
            float envelope = 1f - (t / duration);
            envelope *= envelope;
            samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * volume * envelope;
        }
        
        AudioClip clip = AudioClip.Create("TwoTone", numSamples, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
    
    // === HAPTICS ===
    
    /// <summary>
    /// Send a haptic pulse to XR hand controllers (HoloLens 2 supports this via OpenXR).
    /// </summary>
    void SendHapticPulse(float amplitude, float durationSeconds)
    {
        // Reuse cached list to avoid GC allocation
        cachedHapticDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, cachedHapticDevices);
        
        if (cachedHapticDevices.Count == 0)
        {
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, cachedHapticDevices);
        }
        
        foreach (var device in cachedHapticDevices)
        {
            HapticCapabilities caps;
            if (device.TryGetHapticCapabilities(out caps) && caps.supportsImpulse)
            {
                device.SendHapticImpulse(0, amplitude, durationSeconds);
            }
        }
    }
    
    // === RAY HELPER ===
    
    /// <summary>
    /// Gets the current XR ray (mirrors CesiumMetadataReader.GetXRRay logic).
    /// </summary>
    Ray GetCurrentRay()
    {
        // Reuse cached list to avoid per-frame GC allocation
        cachedRayDevices.Clear();
        
        // Right hand first
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, cachedRayDevices);
        
        if (cachedRayDevices.Count == 0)
        {
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, cachedRayDevices);
        }
        
        if (cachedRayDevices.Count == 0)
        {
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HandTracking, cachedRayDevices);
        }
        
        foreach (var device in cachedRayDevices)
        {
            Vector3 pos;
            Quaternion rot;
            bool hasPos = device.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
            bool hasRot = device.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
            
            if (hasPos && hasRot && pos != Vector3.zero)
            {
                return new Ray(pos, rot * Vector3.forward);
            }
        }
        
        // Fallback: head gaze
        if (mainCamera != null)
        {
            return new Ray(mainCamera.transform.position, mainCamera.transform.forward);
        }
        return new Ray(Vector3.zero, Vector3.forward);
    }
    
    void OnDestroy()
    {
        if (cursorDot != null) Destroy(cursorDot);
        if (progressRingObj != null) Destroy(progressRingObj);
        if (cursorMaterial != null) Destroy(cursorMaterial);
        if (ringMaterial != null) Destroy(ringMaterial);
        if (ringMesh != null) Destroy(ringMesh);
    }
}
