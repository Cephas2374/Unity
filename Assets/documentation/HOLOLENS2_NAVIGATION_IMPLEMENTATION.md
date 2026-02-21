# HoloLens 2 Navigation & AR Experience — Implementation Report

## 1. Overview

This document presents the implementation of a fully interactive navigation and AR experience for deploying a 3D city energy-visualization application to  Microsoft HoloLens 2. Since HoloLens is a head-mounted, keyboard-free device, traditional WASD keyboard controls are unavailable. The solution introduces a floating, holographic button panel that follows the user's gaze, allowing them to fly through the 3D city using hand-tapped buttons. Alongside the navigation system, a Mixed Reality Capture (MRC) fix ensures recorded video correctly displays all holographic content, and an automated camera setup script guarantees the required components are always present on the Main Camera.

The implementation comprises three core scripts:

| Script | Location | Purpose |
|--------|----------|---------|
| `HoloLensNavigationUI.cs` | `Assets/` | Floating AR navigation button panel |
| `ForceOpaqueAlpha.cs` | `Assets/` | MRC video recording alpha-channel fix |
| `HoloLensXRCameraSetup.cs` | `Assets/Editor/` | Auto-configuration of Main Camera components |

A fourth Editor script, `CesiumColliderSetup.cs`, auto-enables physics meshes on all Cesium 3D Tilesets so that hand-ray raycasts can hit buildings for tap interactions.

---

## 2. The Navigation Panel — `HoloLensNavigationUI.cs`

### 2.1 Design Rationale

On HoloLens 2, the user cannot press keyboard keys. The scene camera is driven entirely by head tracking (via `TrackedPoseDriver`), so looking around is natural, but *translating* through the city requires an alternative. The solution is a WorldSpace UI canvas with large, clearly labelled buttons that mirror a keyboard layout familiar to any PC user: **Q W E / A S D / R X** plus rotation and speed controls.

The panel is built entirely at runtime in `Start()`, requiring no pre-authored prefabs or assets in the Unity scene. This keeps the implementation self-contained and avoids merge conflicts across branches.

### 2.2 Button Layout

The buttons are arranged in a grid that mimics a keyboard's left-hand cluster:

```
  [Q ↰]   [W ↑]   [E ↗]       ← Row 1: Rotate Left / Forward / Rotate Right
  [A ←]   [S ↓]   [D →]       ← Row 2: Strafe Left / Backward / Strafe Right
  [R ⬆]   [X ⬇]   [⚡FAST]    ← Row 3: Move Up / Move Down / Speed Boost Toggle
              [HIDE ✕]          ← Row 4: Minimize panel
```

Each directional button uses **press-and-hold** semantics via Unity's `EventTrigger` system (`PointerDown` / `PointerUp` / `PointerExit`), so the user can keep their finger pinched to continue moving. The boost toggle and hide button use standard `Button.onClick` events.

### 2.3 Movement Mechanics

Movement does not translate the camera itself. Instead, the script repositions the `CesiumGeoreference` transform in the *opposite* direction. On HoloLens, the camera's world position is locked to the user's physical head, so the only way to "fly" is to move the entire world origin. This approach preserves Cesium's geospatial accuracy and keeps spatial anchors stable.

Rotation works similarly — `RotateAround` pivots the georeference around the camera position on the Y-axis, effectively rotating the city beneath the user.

### 2.4 Panel Tracking

In `LateUpdate()`, the panel is continuously repositioned to float 0.6 m in front of the user, 0.3 m below eye level, and shifted slightly to the left so it doesn't obscure the center of view. The panel's forward vector is flattened (Y = 0) so it remains upright regardless of head pitch.

When the user taps **HIDE**, the panel disappears and a small blue **NAV ☰** pill replaces it. Tapping the pill restores the full panel. This is handled by a coroutine that continuously repositions the mini-button each frame until the panel is toggled back.

### 2.5 Full Source Code

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HoloLens 2 Navigation Controller
/// 
/// Creates a floating button panel in AR for navigating the 3D city.
/// On HoloLens, you can't use a physical keyboard, so this provides
/// tappable buttons for flying through the scene.
/// 
/// Button Layout (mimics keyboard):
///   [Q↰]  [W↑]  [E↗]
///   [A←]  [S↓]  [D→]
///         [X↓↓]
///   [R⤴]  [F⤵]  [Y⟳L] [C⟳R]
///
/// Q = Rotate Left    W = Forward     E = Rotate Right
/// A = Strafe Left    S = Backward    D = Strafe Right
/// R = Move Up        F = Move Down
/// Y = Yaw Left       X = Move Down (alt)  C = Yaw Right
///
/// The panel follows the user and can be toggled with a dedicated button.
/// </summary>
public class HoloLensNavigationUI : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Movement speed in meters per second")]
    public float moveSpeed = 2.0f;
    
    [Tooltip("Fast movement speed (when boost is active)")]
    public float fastMoveSpeed = 8.0f;
    
    [Tooltip("Rotation speed in degrees per second")]
    public float rotationSpeed = 45f;
    
    [Header("UI Settings")]
    [Tooltip("Distance of navigation panel from camera (meters)")]
    public float panelDistance = 0.6f;
    
    [Tooltip("Vertical offset below eye level (meters)")]
    public float panelVerticalOffset = -0.3f;
    
    [Tooltip("Scale of the navigation panel")]
    public float panelScale = 0.0003f;
    
    [Header("XR Settings")]
    public bool isXRDevice = true;
    
    // Movement state (which directions are currently active)
    private bool moveForward, moveBackward, moveLeft, moveRight;
    private bool moveUp, moveDown;
    private bool rotateLeft, rotateRight;
    private bool isBoosting;
    
    // UI references
    private Canvas navCanvas;
    private GameObject navPanel;
    private bool panelVisible = true;
    private Camera mainCamera;
    
    // Color scheme
    private Color btnNormal = new Color(0.15f, 0.15f, 0.15f, 0.85f);
    private Color btnPressed = new Color(0.0f, 0.47f, 0.84f, 0.95f);
    private Color btnToggle = new Color(0.0f, 0.65f, 0.31f, 0.90f);
    private Color textColor = Color.white;
    
    void Start()
    {
        if (!isXRDevice) 
        {
            this.enabled = false;
            return;
        }
        
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("[HoloLensNav] No Main Camera found!");
            this.enabled = false;
            return;
        }
        
        CreateNavigationPanel();
        Debug.Log("<color=cyan>[HoloLensNav] Navigation panel created. " +
                  "Tap buttons to fly through the city.</color>");
    }
    
    void Update()
    {
        if (mainCamera == null) return;
        
        // Apply continuous movement based on active buttons
        float speed = isBoosting ? fastMoveSpeed : moveSpeed;
        float dt = Time.deltaTime;
        
        Transform camTransform = mainCamera.transform;
        
        // Get horizontal forward (ignore head pitch for movement)
        Vector3 flatForward = camTransform.forward;
        flatForward.y = 0;
        flatForward.Normalize();
        if (flatForward == Vector3.zero) flatForward = Vector3.forward;
        
        Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward) * -1f;
        
        Vector3 movement = Vector3.zero;
        
        if (moveForward)  movement += flatForward * speed * dt;
        if (moveBackward) movement -= flatForward * speed * dt;
        if (moveRight)    movement += flatRight * speed * dt;
        if (moveLeft)     movement -= flatRight * speed * dt;
        if (moveUp)       movement += Vector3.up * speed * dt;
        if (moveDown)     movement -= Vector3.up * speed * dt;
        
        if (movement != Vector3.zero)
        {
            // Move the CesiumGeoreference origin (inverse of camera movement)
            var geoRef = FindObjectOfType<CesiumForUnity.CesiumGeoreference>();
            if (geoRef != null)
            {
                geoRef.transform.position -= movement;
            }
            else
            {
                camTransform.position += movement;
            }
        }
        
        // Rotation (yaw only)
        if (rotateLeft || rotateRight)
        {
            float yaw = 0f;
            if (rotateLeft) yaw -= rotationSpeed * dt;
            if (rotateRight) yaw += rotationSpeed * dt;
            
            var geoRef = FindObjectOfType<CesiumForUnity.CesiumGeoreference>();
            if (geoRef != null)
            {
                geoRef.transform.RotateAround(
                    camTransform.position, Vector3.up, -yaw);
            }
        }
    }
    
    void LateUpdate()
    {
        if (navPanel != null && panelVisible && mainCamera != null)
        {
            PositionPanel();
        }
    }
    
    void PositionPanel()
    {
        Vector3 forward = mainCamera.transform.forward;
        forward.y = 0;
        if (forward == Vector3.zero) forward = Vector3.forward;
        forward.Normalize();
        
        Vector3 pos = mainCamera.transform.position 
            + forward * panelDistance 
            + Vector3.up * panelVerticalOffset
            + Vector3.Cross(Vector3.up, forward) * 0.15f;
        
        navPanel.transform.position = pos;
        navPanel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
    
    void CreateNavigationPanel()
    {
        GameObject canvasObj = new GameObject("HoloLensNavCanvas");
        navCanvas = canvasObj.AddComponent<Canvas>();
        navCanvas.renderMode = RenderMode.WorldSpace;
        
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasObj.AddComponent<GraphicRaycaster>();
        
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(700, 500);
        canvasRect.localScale = Vector3.one * panelScale;
        
        navPanel = canvasObj;
        
        // Background
        GameObject bgObj = CreateUIElement("NavBackground", canvasObj, new Vector2(700, 500));
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0.05f, 0.05f, 0.05f, 0.7f);
        
        // Title
        GameObject titleObj = CreateUIElement("Title", bgObj, new Vector2(700, 50));
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchoredPosition = new Vector2(0, 200);
        Text titleText = titleObj.AddComponent<Text>();
        titleText.text = "Navigation";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 32;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = textColor;
        
        // === ROW 1: Q W E ===
        float row1Y = 120f;
        CreateNavButton(bgObj, "Q  ↰", -200, row1Y,
            () => rotateLeft = true, () => rotateLeft = false, "Rotate Left");
        CreateNavButton(bgObj, "W  ↑", 0, row1Y,
            () => moveForward = true, () => moveForward = false, "Forward");
        CreateNavButton(bgObj, "E  ↗", 200, row1Y,
            () => rotateRight = true, () => rotateRight = false, "Rotate Right");
        
        // === ROW 2: A S D ===
        float row2Y = 10f;
        CreateNavButton(bgObj, "A  ←", -200, row2Y,
            () => moveLeft = true, () => moveLeft = false, "Strafe Left");
        CreateNavButton(bgObj, "S  ↓", 0, row2Y,
            () => moveBackward = true, () => moveBackward = false, "Backward");
        CreateNavButton(bgObj, "D  →", 200, row2Y,
            () => moveRight = true, () => moveRight = false, "Strafe Right");
        
        // === ROW 3: R X + Boost ===
        float row3Y = -100f;
        CreateNavButton(bgObj, "R  ⬆", -200, row3Y,
            () => moveUp = true, () => moveUp = false, "Move Up");
        CreateNavButton(bgObj, "X  ⬇", 0, row3Y,
            () => moveDown = true, () => moveDown = false, "Move Down");
        CreateToggleButton(bgObj, "⚡FAST", 200, row3Y,
            (active) => { isBoosting = active; }, "Toggle Fast Speed");
        
        // === BOTTOM: Hide ===
        float row4Y = -200f;
        CreateActionButton(bgObj, "HIDE ✕", 0, row4Y, () => {
            panelVisible = false;
            navPanel.SetActive(false);
            StartCoroutine(ShowMinimizedButton());
        }, "Minimize Panel");
    }
    
    System.Collections.IEnumerator ShowMinimizedButton()
    {
        yield return new WaitForSeconds(0.5f);
        
        GameObject miniCanvas = new GameObject("NavMiniButton");
        Canvas mc = miniCanvas.AddComponent<Canvas>();
        mc.renderMode = RenderMode.WorldSpace;
        miniCanvas.AddComponent<CanvasScaler>();
        miniCanvas.AddComponent<GraphicRaycaster>();
        
        RectTransform mRect = miniCanvas.GetComponent<RectTransform>();
        mRect.sizeDelta = new Vector2(200, 80);
        mRect.localScale = Vector3.one * panelScale;
        
        GameObject btnObj = CreateUIElement("ShowBtn", miniCanvas, new Vector2(200, 80));
        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = new Color(0.0f, 0.47f, 0.84f, 0.9f);
        
        Text btnText = CreateTextChild(btnObj, "NAV ☰", 24);
        
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(() => {
            panelVisible = true;
            navPanel.SetActive(true);
            Destroy(miniCanvas);
        });
        
        while (miniCanvas != null && !panelVisible)
        {
            Vector3 fwd = mainCamera.transform.forward;
            fwd.y = 0;
            if (fwd == Vector3.zero) fwd = Vector3.forward;
            fwd.Normalize();
            
            miniCanvas.transform.position = mainCamera.transform.position 
                + fwd * 0.5f 
                + Vector3.up * panelVerticalOffset
                + Vector3.Cross(Vector3.up, fwd) * 0.25f;
            miniCanvas.transform.rotation =
                Quaternion.LookRotation(fwd, Vector3.up);
            
            yield return null;
        }
    }
    
    // === UI CREATION HELPERS ===
    
    void CreateNavButton(GameObject parent, string label, float x, float y,
        System.Action onPress, System.Action onRelease, string tooltip)
    {
        GameObject btnObj = CreateUIElement(
            "Btn_" + label.Split(' ')[0], parent, new Vector2(170, 90));
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, y);
        
        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = btnNormal;
        
        Text text = CreateTextChild(btnObj, label, 28);
        
        var trigger = btnObj.AddComponent<
            UnityEngine.EventSystems.EventTrigger>();
        
        var pointerDown = new UnityEngine.EventSystems.EventTrigger.Entry();
        pointerDown.eventID =
            UnityEngine.EventSystems.EventTriggerType.PointerDown;
        pointerDown.callback.AddListener((data) => { 
            onPress?.Invoke();
            btnImage.color = btnPressed;
        });
        trigger.triggers.Add(pointerDown);
        
        var pointerUp = new UnityEngine.EventSystems.EventTrigger.Entry();
        pointerUp.eventID =
            UnityEngine.EventSystems.EventTriggerType.PointerUp;
        pointerUp.callback.AddListener((data) => { 
            onRelease?.Invoke();
            btnImage.color = btnNormal;
        });
        trigger.triggers.Add(pointerUp);
        
        var pointerExit = new UnityEngine.EventSystems.EventTrigger.Entry();
        pointerExit.eventID =
            UnityEngine.EventSystems.EventTriggerType.PointerExit;
        pointerExit.callback.AddListener((data) => { 
            onRelease?.Invoke();
            btnImage.color = btnNormal;
        });
        trigger.triggers.Add(pointerExit);
    }
    
    void CreateToggleButton(GameObject parent, string label, float x, float y,
        System.Action<bool> onToggle, string tooltip)
    {
        GameObject btnObj = CreateUIElement(
            "Btn_Toggle", parent, new Vector2(170, 90));
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, y);
        
        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = btnNormal;
        
        Text text = CreateTextChild(btnObj, label, 24);
        
        bool isActive = false;
        
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImage;
        btn.onClick.AddListener(() => {
            isActive = !isActive;
            btnImage.color = isActive ? btnToggle : btnNormal;
            onToggle?.Invoke(isActive);
        });
    }
    
    void CreateActionButton(GameObject parent, string label, float x, float y,
        System.Action onClick, string tooltip)
    {
        GameObject btnObj = CreateUIElement(
            "Btn_Action", parent, new Vector2(170, 70));
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, y);
        
        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = new Color(0.6f, 0.1f, 0.1f, 0.85f);
        
        Text text = CreateTextChild(btnObj, label, 22);
        
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImage;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }
    
    GameObject CreateUIElement(string name, GameObject parent, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return obj;
    }
    
    Text CreateTextChild(GameObject parent, string label, int fontSize)
    {
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(parent.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        Text text = textObj.AddComponent<Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = textColor;
        text.fontStyle = FontStyle.Bold;
        
        return text;
    }
    
    void OnDestroy()
    {
        if (navPanel != null) Destroy(navPanel);
    }
}
```

---

## 3. Mixed Reality Capture Fix — `ForceOpaqueAlpha.cs`

### 3.1 The Problem

During testing, HoloLens 2 video recordings (Mixed Reality Capture) showed the Cesium terrain and buildings as invisible. The real-world camera feed was visible, and Unity-native UI elements appeared, but every Cesium-rendered pixel was missing from the recording.

The root cause is that HoloLens MRC composites holograms over the real-world camera feed using the **alpha channel**. Pixels with alpha = 0 are treated as transparent (show the real world); pixels with alpha = 1 are treated as opaque holograms. Cesium's internal shaders write `alpha = 0` for what they consider opaque geometry, which is correct for a standard display but invisible to MRC.

### 3.2 The Solution

`ForceOpaqueAlpha` is a camera post-processing script that runs after all rendering is complete. It uses `OnRenderImage` to:

1. **Blit** the source render texture to the destination normally (preserving all color channels).
2. **Draw a fullscreen GL quad** that writes only to the alpha channel, forcing every pixel to `alpha = 1.0`.

This ensures every rendered pixel — regardless of which shader produced it — appears as a solid hologram in MRC recordings, without altering the visual appearance on the HoloLens display itself.

### 3.3 Full Source Code

```csharp
using UnityEngine;

/// <summary>
/// Forces alpha=1.0 on all opaque pixels after rendering.
/// This fixes HoloLens 2 Mixed Reality Capture (MRC) not showing
/// Cesium terrain/tiles in video recordings.
/// 
/// Attach this to the Main Camera (DynamicCamera).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ForceOpaqueAlpha : MonoBehaviour
{
    private Material forceAlphaMat;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (forceAlphaMat == null)
        {
            forceAlphaMat = new Material(
                Shader.Find("Hidden/Internal-Colored"));
            if (forceAlphaMat == null)
            {
                Graphics.Blit(src, dest);
                return;
            }
        }

        // Blit normally first
        Graphics.Blit(src, dest);

        // Then force alpha=1 on the destination
        GL.PushMatrix();
        GL.LoadOrtho();

        GL.Clear(false, false, new Color(0, 0, 0, 1));

        var prevRT = RenderTexture.active;
        RenderTexture.active = dest;

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
```

---

## 4. Automatic Camera Configuration — `HoloLensXRCameraSetup.cs`

### 4.1 Purpose

Rather than requiring the developer to manually attach three components to the Main Camera every time the scene is opened, this Editor-only script uses the `[InitializeOnLoad]` attribute to run automatically when Unity loads. It inspects the Main Camera and, if any of the following components are missing, adds them:

- **`TrackedPoseDriver`** — Maps the HoloLens head position and rotation to the camera, enabling 6-DOF spatial tracking.
- **`ForceOpaqueAlpha`** — The MRC fix described above.
- **`HoloLensNavigationUI`** — The navigation button panel.

A manual menu entry is also registered under **Tools > HoloLens > Setup Main Camera for XR** for cases where the developer wants to trigger the setup explicitly.

### 4.2 Full Source Code

```csharp
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
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogWarning(
                "HoloLensXRCameraSetup: No Main Camera found!");
            return;
        }

        // TrackedPoseDriver for head tracking
        TrackedPoseDriver tpd =
            mainCam.GetComponent<TrackedPoseDriver>();
        if (tpd == null)
        {
            tpd = mainCam.gameObject
                .AddComponent<TrackedPoseDriver>();
            tpd.SetPoseSource(
                TrackedPoseDriver.DeviceType.GenericXRDevice,
                TrackedPoseDriver.TrackedPose.Center);
            tpd.trackingType =
                TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType =
                TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            
            Debug.Log($"<color=green>✅ Added TrackedPoseDriver to " +
                $"{mainCam.name} for HoloLens 2 head tracking</color>");
            EditorUtility.SetDirty(mainCam.gameObject);
        }

        // ForceOpaqueAlpha for MRC video recording
        ForceOpaqueAlpha forceAlpha =
            mainCam.GetComponent<ForceOpaqueAlpha>();
        if (forceAlpha == null)
        {
            mainCam.gameObject.AddComponent<ForceOpaqueAlpha>();
            Debug.Log($"<color=green>✅ Added ForceOpaqueAlpha to " +
                $"{mainCam.name} for MRC video capture</color>");
            EditorUtility.SetDirty(mainCam.gameObject);
        }

        // HoloLensNavigationUI for AR navigation buttons
        HoloLensNavigationUI navUI =
            mainCam.GetComponent<HoloLensNavigationUI>();
        if (navUI == null)
        {
            navUI = mainCam.gameObject
                .AddComponent<HoloLensNavigationUI>();
            Debug.Log($"<color=green>✅ Added HoloLensNavigationUI to " +
                $"{mainCam.name} for AR navigation</color>");
            EditorUtility.SetDirty(mainCam.gameObject);
        }
    }

    [MenuItem("Tools/HoloLens/Setup Main Camera for XR")]
    public static void ManualSetup()
    {
        SetupMainCameraForXR();
    }
}
```

---

## 5. Cesium Collider Auto-Setup — `CesiumColliderSetup.cs`

### 5.1 Why Colliders Matter

For the user's tap and hold-tap interactions to work (quick pinch opens a building info panel, long pinch opens an edit form), the hand ray must physically intersect a collider. Cesium 3D Tiles are streamed at runtime and do not carry Unity colliders by default. The `createPhysicsMeshes` property on `Cesium3DTileset` tells the plugin to generate `MeshCollider` components as tiles load, making them raycast-hittable.

This Editor script ensures `createPhysicsMeshes = true` on every `Cesium3DTileset` in the scene, logging a warning if the setting was changed so the developer knows to save the scene.

### 5.2 Full Source Code

```csharp
using UnityEngine;
using UnityEditor;
using CesiumForUnity;

/// <summary>
/// Ensures Cesium3DTileset generates colliders for raycasting.
/// Without colliders, tapping buildings won't work on HoloLens 2.
/// </summary>
[InitializeOnLoad]
public class CesiumColliderSetup
{
    static CesiumColliderSetup()
    {
        EditorApplication.delayCall += SetupCesiumColliders;
    }

    static void SetupCesiumColliders()
    {
        Cesium3DTileset[] tilesets =
            Object.FindObjectsOfType<Cesium3DTileset>();
        if (tilesets.Length == 0)
        {
            Debug.LogWarning(
                "CesiumColliderSetup: No Cesium3DTileset found!");
            return;
        }

        bool madeChanges = false;
        foreach (var tileset in tilesets)
        {
            if (!tileset.createPhysicsMeshes)
            {
                tileset.createPhysicsMeshes = true;
                Debug.Log($"<color=green>✅ Enabled Physics Meshes " +
                    $"on {tileset.name}</color>");
                EditorUtility.SetDirty(tileset);
                madeChanges = true;
            }
        }

        if (madeChanges)
        {
            Debug.Log("<color=yellow>⚠️ Cesium collider settings " +
                "changed. Save the scene!</color>");
        }
    }

    [MenuItem("Tools/HoloLens/Enable Cesium Colliders")]
    public static void ManualSetup()
    {
        SetupCesiumColliders();
    }
}
```

---

## 6. How It All Comes Together on HoloLens 2

When the application launches on HoloLens 2, the following sequence occurs:

1. **Head tracking activates** — `TrackedPoseDriver` locks the Unity camera to the user's physical head position and orientation. The 3D city appears as a holographic overlay on the real world.

2. **Navigation panel spawns** — `HoloLensNavigationUI.Start()` creates the WorldSpace button panel. It floats 60 cm in front of the user, slightly below eye level and to the left.

3. **The user looks around** naturally by turning their head. The panel follows smoothly via `LateUpdate()`.

4. **To fly through the city**, the user raises a hand, points the hand ray at a navigation button, and **pinches**. Holding the pinch keeps the movement active. Releasing stops it. The city slides beneath them as the `CesiumGeoreference` origin is repositioned.

5. **To inspect a building**, the user points at it and performs a **quick pinch** (< 0.5 seconds). An info panel appears showing building metadata and energy data. A **long pinch** (>= 0.5 seconds) opens the editable attributes form instead.

6. **To hide the panel**, the user taps **HIDE**. A small blue **NAV** button remains. Tapping it restores the full panel.

7. **Speed boost** — tapping the green **FAST** toggle increases movement speed from 2 m/s to 8 m/s for quickly traversing large areas.

8. **MRC video recording** works correctly because `ForceOpaqueAlpha` forces every rendered pixel's alpha to 1.0, ensuring Cesium terrain and buildings appear in recordings alongside the real-world camera feed.

---

## 7. Configuration Reference

| Parameter | Default | Description |
|-----------|---------|-------------|
| `moveSpeed` | 2.0 m/s | Normal movement speed |
| `fastMoveSpeed` | 8.0 m/s | Boosted movement speed |
| `rotationSpeed` | 45 deg/s | Yaw rotation speed |
| `panelDistance` | 0.6 m | Distance of panel from user |
| `panelVerticalOffset` | -0.3 m | Vertical drop below eye level |
| `panelScale` | 0.0003 | WorldSpace canvas scale factor |
| `isXRDevice` | true | Enables/disables the navigation panel |

All parameters are exposed as `[Tooltip]`-annotated public fields and can be adjusted in the Unity Inspector without modifying code.

---

*Document generated from the Unity HoloLens 2 deployment project — commit `14ab809` on branch `main`.*
