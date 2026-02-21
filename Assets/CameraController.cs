using UnityEngine;
using UnityEngine.EventSystems; // ADD THIS - Required for UI blocking
using UnityEngine.XR;
using CesiumForUnity;

/// <summary>
/// Configures camera controls for desktop and HoloLens 2
/// 
/// DESKTOP: 
/// - WASD, arrow keys for movement
/// - E/R for up, C/Z for down
/// - Right-click + drag to look around
/// - Scroll to zoom
/// 
/// HOLOLENS 2:
/// - Camera follows head position (automatic)
/// - Hand gestures for movement (optional)
/// - This script disables itself on XR devices
/// </summary>
public class CameraController : MonoBehaviour
{
    public float moveSpeed = 100f;
    public float fastMoveSpeed = 500f;
    public float rotationSpeed = 2f;
    
    [Header("HoloLens 2 Settings")]
    [Tooltip("Disable camera control on XR devices (HoloLens 2 uses head tracking)")]
    public bool disableOnXR = true;
    
    [Header("Desktop Settings")]
    [Tooltip("Desktop camera controls - ALWAYS ENABLED for dynamic camera")]
    public bool enableDesktopControls = true;
    
    private bool rightMouseDown = false;
    private bool isXRDevice = true;
    
    void Start()
    {
        // Detect if actually running on an XR device (HoloLens 2).
        // Previously hardcoded to true, which disabled the controller in the Editor.
        isXRDevice = XRSettings.isDeviceActive;
        
        Debug.Log($"CameraController: isXRDevice = {isXRDevice}");

        Debug.Log($"<color=cyan>========== CAMERA CONTROLLER START ==========</color>");
        Debug.Log($"<color=cyan>XR Device Detected: {isXRDevice}</color>");
        Debug.Log($"<color=cyan>Disable On XR: {disableOnXR}</color>");
        Debug.Log($"<color=cyan>Enable Desktop Controls: {enableDesktopControls}</color>");
        Debug.Log($"<color=cyan>Camera Position: {transform.position}</color>");
        Debug.Log($"<color=cyan>Move Speed: {moveSpeed}</color>");
        
        if (isXRDevice && disableOnXR)
        {
            Debug.Log("<color=red>CameraController: XR device detected - DISABLING desktop camera controls</color>");
            this.enabled = false;
        }
        else
        {
            // Ensure cursor is unlocked and visible at startup
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("<color=green>✅ CameraController: ENABLED - Desktop controls active!</color>");
            Debug.Log("<color=green>   WASD / Arrow Keys = Move</color>");
            Debug.Log("<color=green>   E/R = Up, C/Z = Down</color>");
            Debug.Log("<color=green>   Right-Click + Drag = Look Around</color>");
            Debug.Log("<color=green>   Shift = Fast Move</color>");
        }
    }
    
    void Update()
    {
        // DEBUG: Check if any movement keys are pressed
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D))
        {
            Debug.Log($"<color=yellow>🎮 KEY PRESSED! Camera position: {transform.position}</color>");
        }
        
        // Check for right mouse button for rotation
        if (Input.GetMouseButtonDown(1))
        {
            rightMouseDown = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        
        if (Input.GetMouseButtonUp(1))
        {
            rightMouseDown = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        
        // Emergency unlock: Press Escape to unlock cursor if it gets stuck
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            rightMouseDown = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        
        // Mouse rotation only when right mouse button is held
        if (rightMouseDown)
        {
            float mouseX = Input.GetAxis("Mouse X") * rotationSpeed;
            float mouseY = Input.GetAxis("Mouse Y") * rotationSpeed;
            
            transform.Rotate(Vector3.up, mouseX, Space.World);
            transform.Rotate(Vector3.right, -mouseY, Space.Self);
        }
        
        // Keyboard movement (WASD + CWERZ)
        float speed = Input.GetKey(KeyCode.LeftShift) ? fastMoveSpeed : moveSpeed;
        float deltaTime = Time.deltaTime * speed;
        
        bool moved = false;
        Vector3 startPos = transform.position;
        
        // Forward/Backward: W/S
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            transform.Translate(Vector3.forward * deltaTime, Space.Self);
            moved = true;
        }
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            transform.Translate(Vector3.back * deltaTime, Space.Self);
            moved = true;
        }
        
        // Left/Right: A/D
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            transform.Translate(Vector3.left * deltaTime, Space.Self);
            moved = true;
        }
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            transform.Translate(Vector3.right * deltaTime, Space.Self);
            moved = true;
        }
        
        // Up/Down: E/C (or Q/Z for compatibility)
        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.R))
        {
            transform.Translate(Vector3.up * deltaTime, Space.World);
            moved = true;
        }
        if (Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.Z))
        {
            transform.Translate(Vector3.down * deltaTime, Space.World);
            moved = true;
        }
        
        // DEBUG: Log actual movement
        if (moved)
        {
            Vector3 endPos = transform.position;
            Debug.Log($"<color=green>📍 MOVED! From: {startPos} To: {endPos} | Delta: {endPos - startPos}</color>");
        }
        
        // Mouse scroll for zoom (increased sensitivity)
        // IMPORTANT: Only zoom if mouse is NOT over UI (prevents zooming when scrolling in forms)
        if (EventSystem.current != null && !EventSystem.current.IsPointerOverGameObject())
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
            {
                transform.Translate(Vector3.forward * scroll * speed * 50f, Space.Self);
            }
        }
    }
}
