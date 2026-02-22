using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using System.Collections.Generic;

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
/// XR Interaction (HoloLens 2):
///   Standard GraphicRaycaster does NOT work with XR hand rays, so this script
///   implements its own gaze/hand-ray + air-tap interaction:
///   - Each button has a BoxCollider for Physics.Raycast
///   - Hand ray (or head gaze fallback) identifies hovered button
///   - Air tap (pinch) presses the button
///   - Hold pinch for continuous navigation movement
///   - Release to stop
///   - EventTrigger kept as fallback for Editor/Desktop mouse interaction
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
    public float panelVerticalOffset = -0.05f;
    
    [Tooltip("Scale of the navigation panel")]
    public float panelScale = 0.0003f;
    
    [Header("XR Settings")]
    public bool isXRDevice = true;
    
    /// <summary>
    /// Static flag: true when an XR navigation button is actively being held.
    /// CesiumMetadataReader checks this to avoid conflicting XR input.
    /// </summary>
    public static bool IsXRButtonActive { get; private set; }
    
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
    private Color btnHover = new Color(0.25f, 0.35f, 0.55f, 0.90f);
    private Color btnPressed = new Color(0.0f, 0.47f, 0.84f, 0.95f);
    private Color btnToggle = new Color(0.0f, 0.65f, 0.31f, 0.90f);
    private Color textColor = Color.white;
    
    // === XR Button Interaction ===
    
    /// <summary>Tracks a UI button for direct XR raycast interaction.</summary>
    private class XRButtonData
    {
        public GameObject gameObject;
        public BoxCollider collider;
        public Image image;
        public System.Action onPress;     // Called on pinch start (hold buttons)
        public System.Action onRelease;   // Called on pinch release
        public bool isHoldButton;         // true = press-and-hold (nav), false = tap (toggle/action)
        public bool isPressed;
        public Color normalColor;         // Rest color (changes for toggle buttons)
    }
    
    private List<XRButtonData> xrButtons = new List<XRButtonData>();
    private XRButtonData activeHoldButton = null;   // Currently held nav button
    private int hoveredButtonIndex = -1;
    private bool wasXRPinching = false;

    // Visible hand ray (LineRenderer)
    private LineRenderer handRayLine;
    private const float RAY_MAX_DISTANCE = 5f;

    // OpenXR aim/pointer pose — this is the far-field pointing ray on HoloLens 2
    // (devicePosition/deviceRotation = grip pose at wrist, NOT the aim ray)
    private static readonly InputFeatureUsage<Vector3> pointerPosition = new InputFeatureUsage<Vector3>("PointerPosition");
    private static readonly InputFeatureUsage<Quaternion> pointerRotation = new InputFeatureUsage<Quaternion>("PointerRotation");
    private bool loggedPoseSource = false; // One-time diagnostic log

    // Cached references to avoid per-frame allocations (GC pressure crashes HoloLens 2)
    private CesiumForUnity.CesiumGeoreference cachedGeoRef;
    private readonly List<InputDevice> cachedDevices = new List<InputDevice>();
    private readonly List<InputDevice> cachedSelectDevices = new List<InputDevice>();
    
    // UI layer for efficient Physics.Raycast (avoids checking thousands of Cesium colliders)
    private const int UI_LAYER = 5;
    private int uiLayerMask;
    
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
        
        // Cache CesiumGeoreference to avoid FindObjectOfType every frame
        cachedGeoRef = FindObjectOfType<CesiumForUnity.CesiumGeoreference>();
        uiLayerMask = 1 << UI_LAYER;
        
        CreateNavigationPanel();
        CreateHandRay();
        Debug.Log("<color=cyan>[HoloLensNav] Navigation panel created with XR interaction.</color>");
        Debug.Log("<color=cyan>[HoloLensNav] Point your hand at buttons and air-tap/pinch to navigate.</color>");
    }
    
    void Update()
    {
        if (mainCamera == null) return;
        
        // XR button interaction: hand ray + pinch
        if (isXRDevice)
        {
            HandleXRButtonInteraction();
            UpdateHandRayVisual();
        }
        
        // Apply continuous movement based on active buttons
        ApplyMovement();
    }
    
    void LateUpdate()
    {
        // Keep panel positioned relative to user
        if (navPanel != null && panelVisible && mainCamera != null)
        {
            PositionPanel();
        }
    }
    
    // === MOVEMENT ===
    
    void ApplyMovement()
    {
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
            // Move the CesiumGeoreference origin (cached — no FindObjectOfType per frame)
            if (cachedGeoRef != null)
            {
                // Move the georeference opposite to desired camera movement
                // This effectively "moves" the user through the city
                cachedGeoRef.transform.position -= movement;
            }
            else
            {
                // Fallback: move camera directly
                camTransform.position += movement;
            }
        }
        
        // Rotation (yaw only)
        if (rotateLeft || rotateRight)
        {
            float yaw = 0f;
            if (rotateLeft) yaw -= rotationSpeed * dt;
            if (rotateRight) yaw += rotationSpeed * dt;
            
            if (cachedGeoRef != null)
            {
                cachedGeoRef.transform.RotateAround(camTransform.position, Vector3.up, -yaw);
            }
        }
    }
    
    // === XR BUTTON INTERACTION ===
    
    /// <summary>
    /// Handles XR hand-ray / gaze + air-tap interaction with navigation buttons.
    /// Uses Physics.Raycast against BoxColliders on buttons, bypassing EventSystem
    /// which doesn't support XR hand rays with standard GraphicRaycaster.
    /// </summary>
    void HandleXRButtonInteraction()
    {
        bool currentPinch = GetXRSelectState();
        
        // If a hold-button is actively held, keep it until pinch is released
        // (user can look away while holding — button stays active)
        if (activeHoldButton != null)
        {
            if (!currentPinch)
            {
                // Released pinch → release the held button
                activeHoldButton.onRelease?.Invoke();
                activeHoldButton.image.color = activeHoldButton.normalColor;
                activeHoldButton.isPressed = false;
                activeHoldButton = null;
                IsXRButtonActive = false;
            }
            wasXRPinching = currentPinch;
            return; // Don't process other interactions while holding a nav button
        }
        
        // Cast ray to find which button (if any) the user is pointing at
        // Uses UI layer mask to avoid hitting thousands of Cesium building colliders
        Ray ray = GetXRPointingRay();
        RaycastHit hit;
        int hitIndex = -1;
        
        if (Physics.Raycast(ray, out hit, 3f, uiLayerMask))
        {
            for (int i = 0; i < xrButtons.Count; i++)
            {
                if (xrButtons[i].collider != null && xrButtons[i].collider == hit.collider)
                {
                    hitIndex = i;
                    break;
                }
            }
        }
        
        // Update hover highlight
        if (hitIndex != hoveredButtonIndex)
        {
            // Unhighlight previous
            if (hoveredButtonIndex >= 0 && hoveredButtonIndex < xrButtons.Count
                && !xrButtons[hoveredButtonIndex].isPressed)
            {
                xrButtons[hoveredButtonIndex].image.color = xrButtons[hoveredButtonIndex].normalColor;
            }
            // Highlight new
            if (hitIndex >= 0 && !xrButtons[hitIndex].isPressed)
            {
                xrButtons[hitIndex].image.color = btnHover;
            }
            hoveredButtonIndex = hitIndex;
        }
        
        // Handle pinch start on a button
        if (currentPinch && !wasXRPinching && hitIndex >= 0)
        {
            var btn = xrButtons[hitIndex];
            
            if (btn.isHoldButton)
            {
                // Hold button (navigation): start continuous action
                btn.onPress?.Invoke();
                btn.image.color = btnPressed;
                btn.isPressed = true;
                activeHoldButton = btn;
                IsXRButtonActive = true;
            }
            else
            {
                // Tap button (toggle/action): visual press feedback
                btn.image.color = btnPressed;
                btn.isPressed = true;
            }
        }
        
        // Handle pinch release for tap buttons
        if (!currentPinch && wasXRPinching)
        {
            for (int i = 0; i < xrButtons.Count; i++)
            {
                if (xrButtons[i].isPressed && !xrButtons[i].isHoldButton)
                {
                    // Fire action only if still pointing at the same button
                    if (i == hitIndex)
                    {
                        xrButtons[i].onRelease?.Invoke();
                    }
                    xrButtons[i].image.color = xrButtons[i].normalColor;
                    xrButtons[i].isPressed = false;
                }
            }
        }
        
        wasXRPinching = currentPinch;
    }
    
    /// <summary>
    /// Gets the pointing ray from XR hand tracking, or falls back to head gaze.
    /// On HoloLens 2, this returns the hand aim ray (extends from hand toward target).
    /// </summary>
    Ray GetXRPointingRay()
    {
        // Reuse cached list to avoid per-frame allocation (GC pressure)
        cachedDevices.Clear();
        
        // Try right hand first (most users are right-handed)
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, cachedDevices);
        if (cachedDevices.Count == 0)
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, cachedDevices);
        if (cachedDevices.Count == 0)
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HandTracking, cachedDevices);
        
        foreach (var device in cachedDevices)
        {
            Vector3 pos;
            Quaternion rot;
            
            // Try aim/pointer pose first — this is the far-field pointing ray on HoloLens 2
            bool hasPos = device.TryGetFeatureValue(pointerPosition, out pos);
            bool hasRot = device.TryGetFeatureValue(pointerRotation, out rot);
            
            if (hasPos && hasRot && pos != Vector3.zero)
            {
                if (!loggedPoseSource) { Debug.Log("<color=cyan>[HoloLensNav] Using AIM/Pointer pose from: " + device.name + "</color>"); loggedPoseSource = true; }
                return new Ray(pos, rot * Vector3.forward);
            }
            
            // Fall back to grip/device pose (wrist position)
            hasPos = device.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
            hasRot = device.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
            
            if (hasPos && hasRot && pos != Vector3.zero)
            {
                if (!loggedPoseSource) { Debug.Log("<color=yellow>[HoloLensNav] AIM pose unavailable, using GRIP pose from: " + device.name + "</color>"); loggedPoseSource = true; }
                return new Ray(pos, rot * Vector3.forward);
            }
        }
        
        // Fallback: head gaze (camera forward)
        return new Ray(mainCamera.transform.position, mainCamera.transform.forward);
    }
    
    /// <summary>
    /// Reads XR air-tap / pinch state from all input devices.
    /// On HoloLens 2, pinch gesture maps to triggerButton / trigger axis via OpenXR.
    /// </summary>
    bool GetXRSelectState()
    {
        // Reuse cached list to avoid per-frame allocation (GC pressure)
        cachedSelectDevices.Clear();
        InputDevices.GetDevices(cachedSelectDevices);
        
        foreach (var device in cachedSelectDevices)
        {
            bool value;
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out value) && value)
                return true;
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out value) && value)
                return true;
            float axis;
            if (device.TryGetFeatureValue(CommonUsages.trigger, out axis) && axis > 0.5f)
                return true;
        }
        return false;
    }
    
    // === HAND RAY VISUAL ===
    
    /// <summary>
    /// Creates a visible ray line from hand to target, so the user can see where they're pointing.
    /// Uses LineRenderer for a thin, bright line with a gradient fade.
    /// </summary>
    void CreateHandRay()
    {
        GameObject rayObj = new GameObject("HandRayPointer");
        handRayLine = rayObj.AddComponent<LineRenderer>();
        handRayLine.positionCount = 2;
        handRayLine.startWidth = 0.002f;
        handRayLine.endWidth = 0.001f;
        handRayLine.useWorldSpace = true;
        handRayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        handRayLine.receiveShadows = false;
        
        // Simple unlit material — visible in XR
        Material rayMat = new Material(Shader.Find("Sprites/Default"));
        rayMat.renderQueue = 4000; // Render on top
        handRayLine.material = rayMat;
        
        // Gradient: bright cyan at hand, fading to transparent at end
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0f, 0.8f, 1f), 0f),
                new GradientColorKey(new Color(0f, 0.8f, 1f), 0.7f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0.5f, 0.7f),
                new GradientAlphaKey(0.0f, 1f)
            }
        );
        handRayLine.colorGradient = gradient;
        handRayLine.enabled = false;
    }
    
    /// <summary>
    /// Updates the visible ray each frame: origin at hand, end at hit point or max distance.
    /// Turns brighter when pointing at a button, even brighter when pinching.
    /// </summary>
    void UpdateHandRayVisual()
    {
        if (handRayLine == null) return;
        
        Ray ray = GetXRPointingRay();
        
        // Check if this is from actual hand (not head gaze fallback)
        // Head gaze fallback starts at camera position — don't show ray from face
        bool isHandRay = Vector3.Distance(ray.origin, mainCamera.transform.position) > 0.05f;
        
        if (!isHandRay)
        {
            handRayLine.enabled = false;
            return;
        }
        
        handRayLine.enabled = true;
        
        // Determine end point: hit surface or max distance
        RaycastHit hit;
        Vector3 endPoint;
        if (Physics.Raycast(ray, out hit, RAY_MAX_DISTANCE))
        {
            endPoint = hit.point;
        }
        else
        {
            endPoint = ray.origin + ray.direction * RAY_MAX_DISTANCE;
        }
        
        handRayLine.SetPosition(0, ray.origin);
        handRayLine.SetPosition(1, endPoint);
        
        // Change ray appearance based on state
        bool isPinching = GetXRSelectState();
        if (isPinching)
        {
            handRayLine.startWidth = 0.004f;
            handRayLine.endWidth = 0.002f;
        }
        else
        {
            handRayLine.startWidth = 0.002f;
            handRayLine.endWidth = 0.001f;
        }
    }
    
    // === PANEL POSITIONING ===
    
    void PositionPanel()
    {
        Vector3 forward = mainCamera.transform.forward;
        forward.y = 0;
        if (forward == Vector3.zero) forward = Vector3.forward;
        forward.Normalize();
        
        // Position: in front of user, slightly below eye level, centered
        Vector3 pos = mainCamera.transform.position 
            + forward * panelDistance 
            + Vector3.up * panelVerticalOffset;
        
        navPanel.transform.position = pos;
        navPanel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
    
    // === UI CREATION ===
    
    void CreateNavigationPanel()
    {
        // Create canvas on UI layer for efficient XR raycast
        GameObject canvasObj = new GameObject("HoloLensNavCanvas");
        canvasObj.layer = UI_LAYER;
        navCanvas = canvasObj.AddComponent<Canvas>();
        navCanvas.renderMode = RenderMode.WorldSpace;
        
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasObj.AddComponent<GraphicRaycaster>(); // Kept for Desktop/Editor fallback
        
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(700, 500);
        canvasRect.localScale = Vector3.one * panelScale;
        
        navPanel = canvasObj;
        
        // Background panel
        GameObject bgObj = CreateUIElement("NavBackground", canvasObj, new Vector2(700, 500));
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0.05f, 0.05f, 0.05f, 0.7f);
        
        // Title with interaction hint
        GameObject titleObj = CreateUIElement("Title", bgObj, new Vector2(700, 50));
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchoredPosition = new Vector2(0, 200);
        Text titleText = titleObj.AddComponent<Text>();
        titleText.text = "Navigation — Point & Pinch";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 28;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = textColor;
        
        // === ROW 1: Q W E ===
        float row1Y = 120f;
        CreateNavButton(bgObj, "Q  ↰", -200, row1Y, () => rotateLeft = true, () => rotateLeft = false);
        CreateNavButton(bgObj, "W  ↑", 0, row1Y, () => moveForward = true, () => moveForward = false);
        CreateNavButton(bgObj, "E  ↗", 200, row1Y, () => rotateRight = true, () => rotateRight = false);
        
        // === ROW 2: A S D ===
        float row2Y = 10f;
        CreateNavButton(bgObj, "A  ←", -200, row2Y, () => moveLeft = true, () => moveLeft = false);
        CreateNavButton(bgObj, "S  ↓", 0, row2Y, () => moveBackward = true, () => moveBackward = false);
        CreateNavButton(bgObj, "D  →", 200, row2Y, () => moveRight = true, () => moveRight = false);
        
        // === ROW 3: R (up) X (down) + boost ===
        float row3Y = -100f;
        CreateNavButton(bgObj, "R  ⬆", -200, row3Y, () => moveUp = true, () => moveUp = false);
        CreateNavButton(bgObj, "X  ⬇", 0, row3Y, () => moveDown = true, () => moveDown = false);
        
        // Boost toggle button
        CreateToggleButton(bgObj, "⚡FAST", 200, row3Y);
        
        // === BOTTOM: Hide button ===
        float row4Y = -200f;
        CreateActionButton(bgObj, "HIDE ✕", 0, row4Y, () => {
            panelVisible = false;
            navPanel.SetActive(false);
            // Create a small "show" button that stays in fixed position
            StartCoroutine(ShowMinimizedButton());
        });
    }
    
    System.Collections.IEnumerator ShowMinimizedButton()
    {
        // Wait a moment, then create a small floating button to reopen
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
        Color miniColor = new Color(0.0f, 0.47f, 0.84f, 0.9f);
        btnImg.color = miniColor;
        
        CreateTextChild(btnObj, "NAV ☰", 24);
        
        // BoxCollider for XR interaction
        BoxCollider col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(210, 90, 15);
        col.center = Vector3.zero;
        
        // Track for XR interaction (tap-to-show)
        xrButtons.Add(new XRButtonData
        {
            gameObject = btnObj,
            collider = col,
            image = btnImg,
            onPress = null,
            onRelease = () =>
            {
                panelVisible = true;
                navPanel.SetActive(true);
                xrButtons.RemoveAll(b => b.gameObject == btnObj);
                Destroy(miniCanvas);
            },
            isHoldButton = false,
            isPressed = false,
            normalColor = miniColor
        });
        
        // Also add Button for Desktop/Editor fallback
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(() =>
        {
            panelVisible = true;
            navPanel.SetActive(true);
            xrButtons.RemoveAll(b => b.gameObject == btnObj);
            Destroy(miniCanvas);
        });
        
        // Position it
        while (miniCanvas != null && !panelVisible)
        {
            Vector3 fwd = mainCamera.transform.forward;
            fwd.y = 0;
            if (fwd == Vector3.zero) fwd = Vector3.forward;
            fwd.Normalize();
            
            miniCanvas.transform.position = mainCamera.transform.position 
                + fwd * 0.5f 
                + Vector3.up * panelVerticalOffset;
            miniCanvas.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            
            yield return null;
        }
    }
    
    // === BUTTON CREATION HELPERS ===
    
    /// <summary>
    /// Creates a press-and-hold navigation button with BoxCollider for XR raycast.
    /// </summary>
    void CreateNavButton(GameObject parent, string label, float x, float y,
        System.Action onPress, System.Action onRelease)
    {
        GameObject btnObj = CreateUIElement("Btn_" + label.Split(' ')[0], parent, new Vector2(170, 90));
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, y);
        
        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = btnNormal;
        
        CreateTextChild(btnObj, label, 28);
        
        // BoxCollider for XR gaze/hand-ray raycast (slightly larger than visual for easier targeting)
        BoxCollider col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(180, 100, 15);
        col.center = Vector3.zero;
        
        // Track for XR interaction (press-and-hold)
        xrButtons.Add(new XRButtonData
        {
            gameObject = btnObj,
            collider = col,
            image = btnImage,
            onPress = onPress,
            onRelease = onRelease,
            isHoldButton = true,
            isPressed = false,
            normalColor = btnNormal
        });
        
        // EventTrigger for Desktop/Editor fallback (pointer events)
        UnityEngine.EventSystems.EventTrigger trigger = btnObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        
        // PointerDown → start movement
        var pointerDown = new UnityEngine.EventSystems.EventTrigger.Entry();
        pointerDown.eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown;
        pointerDown.callback.AddListener((data) => { 
            onPress?.Invoke();
            btnImage.color = btnPressed;
        });
        trigger.triggers.Add(pointerDown);
        
        // PointerUp → stop movement
        var pointerUp = new UnityEngine.EventSystems.EventTrigger.Entry();
        pointerUp.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
        pointerUp.callback.AddListener((data) => { 
            onRelease?.Invoke();
            btnImage.color = btnNormal;
        });
        trigger.triggers.Add(pointerUp);
        
        // PointerExit → also stop (finger leaves button)
        var pointerExit = new UnityEngine.EventSystems.EventTrigger.Entry();
        pointerExit.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
        pointerExit.callback.AddListener((data) => { 
            onRelease?.Invoke();
            btnImage.color = btnNormal;
        });
        trigger.triggers.Add(pointerExit);
    }
    
    /// <summary>
    /// Creates a toggle button (tap to toggle on/off) with BoxCollider for XR.
    /// </summary>
    void CreateToggleButton(GameObject parent, string label, float x, float y)
    {
        GameObject btnObj = CreateUIElement("Btn_Toggle", parent, new Vector2(170, 90));
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, y);
        
        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = btnNormal;
        
        CreateTextChild(btnObj, label, 24);
        
        bool isActive = false;
        
        // BoxCollider for XR
        BoxCollider col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(180, 100, 15);
        col.center = Vector3.zero;
        
        // Track for XR interaction (tap-to-toggle)
        var btnData = new XRButtonData
        {
            gameObject = btnObj,
            collider = col,
            image = btnImage,
            onPress = null,
            onRelease = () =>
            {
                isActive = !isActive;
                isBoosting = isActive;
                Color newNormal = isActive ? btnToggle : btnNormal;
                btnImage.color = newNormal;
                // Update the tracked normal color so hover/unhover uses correct color
                var tracked = xrButtons.Find(b => b.collider == col);
                if (tracked != null) tracked.normalColor = newNormal;
            },
            isHoldButton = false,
            isPressed = false,
            normalColor = btnNormal
        };
        xrButtons.Add(btnData);
        
        // Button for Desktop/Editor fallback
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImage;
        btn.onClick.AddListener(() =>
        {
            isActive = !isActive;
            isBoosting = isActive;
            btnImage.color = isActive ? btnToggle : btnNormal;
        });
    }
    
    /// <summary>
    /// Creates a one-shot action button (tap to execute) with BoxCollider for XR.
    /// </summary>
    void CreateActionButton(GameObject parent, string label, float x, float y, System.Action onClick)
    {
        GameObject btnObj = CreateUIElement("Btn_Action", parent, new Vector2(170, 70));
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, y);
        
        Image btnImage = btnObj.AddComponent<Image>();
        Color actionColor = new Color(0.6f, 0.1f, 0.1f, 0.85f);
        btnImage.color = actionColor;
        
        CreateTextChild(btnObj, label, 22);
        
        // BoxCollider for XR
        BoxCollider col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(180, 80, 15);
        col.center = Vector3.zero;
        
        // Track for XR interaction (tap-to-execute)
        xrButtons.Add(new XRButtonData
        {
            gameObject = btnObj,
            collider = col,
            image = btnImage,
            onPress = null,
            onRelease = onClick,
            isHoldButton = false,
            isPressed = false,
            normalColor = actionColor
        });
        
        // Button for Desktop/Editor fallback
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImage;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }
    
    // === UI ELEMENT HELPERS ===
    
    GameObject CreateUIElement(string name, GameObject parent, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.layer = UI_LAYER; // UI layer for efficient XR raycast with layer mask
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
        IsXRButtonActive = false;
        if (handRayLine != null) Destroy(handRayLine.gameObject);
        if (navPanel != null) Destroy(navPanel);
    }
}
