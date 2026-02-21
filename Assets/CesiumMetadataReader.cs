using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR;
using CesiumForUnity;
using System;
using System.Collections;using System.Linq;using System.Collections.Generic;
using System.Text;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Handles building interaction and displays metadata/energy data.
/// 
/// DESKTOP CONTROLS:
/// - Left Click: View building energy data
/// - Ctrl + Left Click: Open building attributes edit form
/// - Right Click: Camera rotation (handled by CameraController)
/// 
/// HOLOLENS 2 CONTROLS:
/// - Air Tap / Pinch (Quick): View building energy data
/// - Hold Gesture (0.5+ seconds): Open building attributes edit form
/// - Gaze + Hand Ray: Target buildings
/// </summary>
public class CesiumMetadataReader : MonoBehaviour
{
    public Camera mainCamera;
    public float displayDuration = 10f;
    
    [Header("Building Energy Integration")]
    public BuildingEnergyManager energyManager;
    public BuildingAttributesForm attributesForm;
    
    [Header("HoloLens 2 Settings")]
    [Tooltip("Time in seconds to trigger edit mode (hold gesture)")]
    public float holdDuration = 0.5f;
    
    [Tooltip("Use XR input for HoloLens 2 (auto-detected)")]
    public bool useXRInput = true;
    
    [Tooltip("OVERRIDE: Force desktop input even if XR detected (check this for desktop testing)")]
    public bool forceDesktopInput = false;
    
    private GameObject metadataPanel;
    private Text metadataText;
    private float hideTimer = 0f;
    private bool isDisplaying = false;
    
    // Currently displayed building info (for refresh after save)
    private string currentDisplayedGmlId;
    private string currentDisplayedObjectName;
    private Int64 currentDisplayedFeatureId;

    // Building selection highlight
    private GameObject currentSelectedBuilding;
    private Material originalMaterial;
    private Material highlightMaterial;
    
    // HoloLens 2 / XR interaction tracking
    private bool isHoldingGesture = false;
    private float holdTimer = 0f;
    private bool holdProcessed = false;
    private bool isXRDevice = true;
    private bool wasXRSelectPressed = false;
    
    // Cached XR device lists — reused every frame to avoid GC allocations
    // (new List every frame caused periodic GC pauses → flashing + crash on HoloLens 2)
    private readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> cachedSelectDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
    private readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> cachedRayDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
    
    // Interaction feedback (cursor, ring, audio, haptics)
    private XRInteractionFeedback xrFeedback;
    
    // Debounce: prevent rapid successive taps
    private float lastTapTime = -1f;
    private const float TAP_DEBOUNCE_INTERVAL = 0.4f;
    
    // Loading spinner overlay
    private GameObject loadingOverlay;
    private Text loadingText;
    
    // Close button on info panel
    private GameObject closeButton;

    void Start()
    {
        StartCoroutine(DelayedStart());
    }
    
    IEnumerator DelayedStart()
    {
        // Minimal delay - just wait for end of frame
        yield return new WaitForEndOfFrame();
        
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        Debug.Log("<color=cyan>========== CesiumMetadataReader INITIALIZATION ==========</color>");
        
        // Check for duplicate instances
        CesiumMetadataReader[] allReaders = FindObjectsOfType<CesiumMetadataReader>();
        Debug.Log($"<color=cyan>📊 CesiumMetadataReader instances in scene: {allReaders.Length}</color>");
        if (allReaders.Length > 1)
        {
            Debug.LogError($"<color=red>⚠️⚠️⚠️ MULTIPLE CesiumMetadataReader INSTANCES DETECTED! ({allReaders.Length})</color>");
            Debug.LogError($"<color=red>This will cause BOTH XR and Desktop handlers to run simultaneously!</color>");
            foreach (var reader in allReaders)
            {
                Debug.LogError($"<color=red>   - Instance: {reader.gameObject.name} (isXRDevice: {reader.isXRDevice}, useXRInput: {reader.useXRInput}, forceDesktopInput: {reader.forceDesktopInput})</color>");
            }
            Debug.LogError($"<color=yellow>💡 SOLUTION: Remove duplicate CesiumMetadataReader components or GameObjects</color>");
        }
        
        Debug.Log($"<color=cyan>mainCamera: {(mainCamera != null ? "✓ Found" : "✗ NULL")}</color>");
        Debug.Log($"<color=cyan>energyManager: {(energyManager != null ? "✓ Found" : "✗ NULL - Ctrl+Click won't work!")}</color>");
        Debug.Log($"<color=cyan>attributesForm: {(attributesForm != null ? "✓ Found" : "✗ NULL - Ctrl+Click won't work!")}</color>");
        
        if (energyManager == null)
        {
            Debug.LogError("<color=red>❌ BuildingEnergyManager is NOT assigned! Please assign it in the Inspector.</color>");
        }
        
        if (attributesForm == null)
        {
            Debug.LogError("<color=red>❌ BuildingAttributesForm is NOT assigned! Please assign it in the Inspector.</color>");
            Debug.Log("<color=yellow>💡 Select the CesiumMetadataReader GameObject and drag BuildingAttributesForm into the Inspector field</color>");
        }
        
        Debug.Log("<color=cyan>===================================================</color>");
        
        if (attributesForm == null)
        {
            // FindObjectOfType with includeInactive=true to find even if GameObject is disabled
            attributesForm = FindObjectOfType<BuildingAttributesForm>(true);
            if (attributesForm != null)
            {
                Debug.Log($"<color=green>✅ BuildingAttributesForm found: {attributesForm.gameObject.name} (active: {attributesForm.gameObject.activeSelf})</color>");
                
                // Enable it if disabled - it needs to be active to receive method calls
                if (!attributesForm.gameObject.activeSelf)
                {
                    Debug.Log("<color=yellow>⚠️ BuildingAttributesForm was disabled - enabling it now</color>");
                    attributesForm.gameObject.SetActive(true);
                }
            }
            else
            {
                Debug.LogWarning("<color=yellow>⚠️ BuildingAttributesForm not found in scene!</color>");
            }
        }
        
        // Detect if running on XR device (HoloLens 2)
        DetectXRDevice();
        
        CreateMetadataUI();
        
        // Initialize XR interaction feedback (cursor, progress ring, audio)
        if (isXRDevice && useXRInput)
        {
            xrFeedback = GetComponent<XRInteractionFeedback>();
            if (xrFeedback == null)
            {
                xrFeedback = gameObject.AddComponent<XRInteractionFeedback>();
                Debug.Log("<color=green>✅ Added XRInteractionFeedback for cursor, ring, audio & haptics</color>");
            }
        }
    }
    
    void DetectXRDevice()
    {
        // Default: isXRDevice = true (set at field declaration) so HoloLens builds work immediately.
        // Override to false only if forceDesktopInput is checked in the Inspector.
        Debug.Log($"<color=cyan>XR Device default: {isXRDevice} (true = HoloLens mode)</color>");

        // Check for manual override (Inspector toggle for desktop testing)
        if (forceDesktopInput)
        {
            Debug.Log("<color=yellow>⚠️ FORCE DESKTOP INPUT ENABLED - Overriding XR detection</color>");
            isXRDevice = false;
            useXRInput = false;
        }
        
        Debug.Log($"<color=magenta>========== INPUT MODE SELECTION ==========</color>");
        Debug.Log($"<color=magenta>isXRDevice: {isXRDevice}</color>");
        Debug.Log($"<color=magenta>useXRInput: {useXRInput}</color>");
        Debug.Log($"<color=magenta>forceDesktopInput: {forceDesktopInput}</color>");
        Debug.Log($"<color=magenta>ACTIVE INPUT HANDLER: {(isXRDevice && useXRInput ? "HandleXRInput()" : "HandleDesktopInput()")}</color>");
        Debug.Log($"<color=magenta>=====================================</color>");
    }

    void CreateMetadataUI()
    {
        // Find or create Canvas (reuse existing if possible)
        Canvas canvas = null;
        GameObject canvasObj = GameObject.Find("MetadataCanvas");
        
        if (canvasObj != null)
        {
            canvas = canvasObj.GetComponent<Canvas>();
        }
        
        if (canvas == null)
        {
            canvasObj = new GameObject("MetadataCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            
            if (isXRDevice)
            {
                // HoloLens 2: WorldSpace canvas (ScreenSpaceOverlay is invisible on AR/VR)
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 50;
                RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(1920, 1080);
                canvasObj.transform.localScale = Vector3.one * 0.0004f; // ~0.77m wide at arm's length
                PositionCanvasInFrontOfCamera(canvasObj);
            }
            else
            {
                // Desktop: ScreenSpaceOverlay
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 50;
            }
            
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        
        // Create Panel
        metadataPanel = new GameObject("MetadataPanel");
        metadataPanel.transform.SetParent(canvasObj.transform, false);
        
        Image panelImage = metadataPanel.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.85f);
        
        RectTransform panelRect = metadataPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.01f, 0.3f);
        panelRect.anchorMax = new Vector2(0.35f, 0.99f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        // Create Text
        GameObject textObj = new GameObject("MetadataText");
        textObj.transform.SetParent(metadataPanel.transform, false);
        
        metadataText = textObj.AddComponent<Text>();
        metadataText.font = Font.CreateDynamicFontFromOSFont("Arial", 32);
        metadataText.fontSize = 32;
        metadataText.color = Color.white;
        metadataText.alignment = TextAnchor.UpperLeft;
        metadataText.fontStyle = FontStyle.Bold;
        metadataText.supportRichText = true;
        metadataText.verticalOverflow = VerticalWrapMode.Overflow;
        metadataText.horizontalOverflow = HorizontalWrapMode.Wrap;
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10);
        textRect.offsetMax = new Vector2(-10, -10);
        
        metadataPanel.SetActive(false);
        
        // === Close button (X) — top-right corner of the info panel ===
        closeButton = new GameObject("CloseButton");
        closeButton.transform.SetParent(metadataPanel.transform, false);
        
        Image closeBtnImage = closeButton.AddComponent<Image>();
        closeBtnImage.color = new Color(0.7f, 0.15f, 0.15f, 0.9f);
        
        RectTransform closeBtnRect = closeButton.GetComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(1f, 1f);
        closeBtnRect.anchorMax = new Vector2(1f, 1f);
        closeBtnRect.pivot = new Vector2(1f, 1f);
        closeBtnRect.sizeDelta = new Vector2(60, 60);
        closeBtnRect.anchoredPosition = new Vector2(-5, -5);
        
        GameObject closeTxtObj = new GameObject("CloseText");
        closeTxtObj.transform.SetParent(closeButton.transform, false);
        Text closeTxt = closeTxtObj.AddComponent<Text>();
        closeTxt.text = "✕";
        closeTxt.font = Font.CreateDynamicFontFromOSFont("Arial", 36);
        closeTxt.fontSize = 36;
        closeTxt.color = Color.white;
        closeTxt.alignment = TextAnchor.MiddleCenter;
        closeTxt.fontStyle = FontStyle.Bold;
        RectTransform closeTxtRect = closeTxtObj.GetComponent<RectTransform>();
        closeTxtRect.anchorMin = Vector2.zero;
        closeTxtRect.anchorMax = Vector2.one;
        closeTxtRect.offsetMin = Vector2.zero;
        closeTxtRect.offsetMax = Vector2.zero;
        
        Button closeBtnComponent = closeButton.AddComponent<Button>();
        closeBtnComponent.targetGraphic = closeBtnImage;
        closeBtnComponent.onClick.AddListener(() => { HideMetadata(); });
        
        // === Loading spinner overlay ===
        loadingOverlay = new GameObject("LoadingOverlay");
        loadingOverlay.transform.SetParent(canvasObj.transform, false);
        
        Image loadBgImage = loadingOverlay.AddComponent<Image>();
        loadBgImage.color = new Color(0, 0, 0, 0.75f);
        
        RectTransform loadRect = loadingOverlay.GetComponent<RectTransform>();
        loadRect.anchorMin = new Vector2(0.3f, 0.45f);
        loadRect.anchorMax = new Vector2(0.7f, 0.55f);
        loadRect.offsetMin = Vector2.zero;
        loadRect.offsetMax = Vector2.zero;
        
        GameObject loadTxtObj = new GameObject("LoadingText");
        loadTxtObj.transform.SetParent(loadingOverlay.transform, false);
        loadingText = loadTxtObj.AddComponent<Text>();
        loadingText.text = "Loading...";
        loadingText.font = Font.CreateDynamicFontFromOSFont("Arial", 28);
        loadingText.fontSize = 28;
        loadingText.color = Color.white;
        loadingText.alignment = TextAnchor.MiddleCenter;
        loadingText.fontStyle = FontStyle.Bold;
        RectTransform loadTxtRect = loadTxtObj.GetComponent<RectTransform>();
        loadTxtRect.anchorMin = Vector2.zero;
        loadTxtRect.anchorMax = Vector2.one;
        loadTxtRect.offsetMin = Vector2.zero;
        loadTxtRect.offsetMax = Vector2.zero;
        
        loadingOverlay.SetActive(false);
    }

    void Update()
    {
        // Handle hide timer
        if (isDisplaying)
        {
            hideTimer -= Time.deltaTime;
            if (hideTimer <= 0f)
            {
                HideMetadata();
            }
        }
        
        // Handle input based on platform
        // DEBUG: Log which input handler is being used on first frame
        if (Time.frameCount == 1)
        {
            Debug.Log($"<color=magenta>🎮 INPUT HANDLER ACTIVE: {(isXRDevice && useXRInput ? "XR (HoloLens 2)" : "DESKTOP (Mouse/Keyboard)")}</color>");
            Debug.Log($"<color=yellow>💡 If wrong, set 'Force Desktop Input' = TRUE in Inspector</color>");
        }
        
        // DEBUG: Show which path is taken every frame when clicking
        if (Input.GetMouseButtonDown(0))
        {
            Debug.Log($"<color=red>⚠️ UPDATE() BRANCH CHECK:</color>");
            Debug.Log($"<color=red>   GameObject: {gameObject.name}</color>");
            Debug.Log($"<color=red>   isXRDevice: {isXRDevice}</color>");
            Debug.Log($"<color=red>   useXRInput: {useXRInput}</color>");
            Debug.Log($"<color=red>   forceDesktopInput: {forceDesktopInput}</color>");
            Debug.Log($"<color=red>   Condition (isXRDevice && useXRInput): {isXRDevice && useXRInput}</color>");
            Debug.Log($"<color=red>   Will call: {(isXRDevice && useXRInput ? "HandleXRInput()" : "HandleDesktopInput()")}</color>");
        }
        
        if (isXRDevice && useXRInput)
        {
            HandleXRInput();
        }
        else
        {
            HandleDesktopInput();
        }
    }
    
    void HandleDesktopInput()
    {
        // Debug: Show all clicks with comprehensive info
        if (Input.GetMouseButtonDown(0))
        {
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool uiBlocked = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            
            Debug.Log($"<color=yellow>========== MOUSE CLICK DEBUG ==========</color>");
            Debug.Log($"<color=yellow>🖱️ Mouse Click - Ctrl: {ctrlHeld}, UI Blocked: {uiBlocked}</color>");
            Debug.Log($"<color=yellow>Script Enabled: {enabled}, GameObject Active: {gameObject.activeSelf}</color>");
            Debug.Log($"<color=yellow>EventSystem Exists: {(EventSystem.current != null)}</color>");
            Debug.Log($"<color=yellow>=================================</color>");
            
            if (uiBlocked)
            {
                Debug.LogWarning("<color=orange>⚠️ Click blocked by UI - clicking on a UI element</color>");
                return;  // Don't process building clicks when clicking UI
            }
        }
        
        // Desktop: Check Ctrl+Click FIRST (higher priority)
        if (Input.GetMouseButtonDown(0))
        {
            bool ctrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            
            if (ctrlPressed)
            {
                Debug.Log("<color=magenta>🖱️ CTRL+CLICK detected! Calling HandleBuildingClick(isRightClick=true)</color>");
                HandleBuildingClick(true);  // true = open form
            }
            else
            {
                Debug.Log("<color=cyan>🖱️ LEFT CLICK detected (no Ctrl)</color>");
                HandleBuildingClick(false);  // false = show panel only
            }
        }
    }
    
    void HandleXRInput()
    {
        // HoloLens 2: Detect air tap / pinch via OpenXR input devices
        // Quick tap (< holdDuration) = view building data
        // Hold (>= holdDuration) then RELEASE = open edit form
        
        // Skip building interaction when a navigation button is actively held
        // (HoloLensNavigationUI sets this flag during press-and-hold nav buttons)
        if (HoloLensNavigationUI.IsXRButtonActive)
        {
            // Reset any in-progress gesture to avoid stale state
            if (isHoldingGesture)
            {
                isHoldingGesture = false;
                holdTimer = 0f;
                if (xrFeedback != null) xrFeedback.ResetFeedback();
            }
            wasXRSelectPressed = false;
            return;
        }
        
        bool currentSelectState = GetXRSelectState();
        
        bool selectPressed = currentSelectState && !wasXRSelectPressed;
        bool selectReleased = !currentSelectState && wasXRSelectPressed;
        wasXRSelectPressed = currentSelectState;
        
        // Handle gesture start
        if (selectPressed)
        {
            isHoldingGesture = true;
            holdTimer = 0f;
            holdProcessed = false;
        }
        
        // Track hold duration and update progress ring
        if (isHoldingGesture && !holdProcessed)
        {
            holdTimer += Time.deltaTime;
            
            // Update visual progress ring via feedback system
            if (xrFeedback != null)
            {
                float progress = Mathf.Clamp01(holdTimer / holdDuration);
                xrFeedback.SetHoldProgress(progress);
            }
            
            // Mark as hold-ready when threshold reached (but DON'T fire yet — wait for release)
            if (holdTimer >= holdDuration && !holdProcessed)
            {
                holdProcessed = true;
                // Visual + audio confirmation that hold threshold reached
                if (xrFeedback != null) xrFeedback.OnHoldComplete();
                Debug.Log("<color=magenta>🖐️ XR HOLD threshold reached — release to open edit form</color>");
            }
        }
        
        // Handle gesture release — this is where we decide tap vs hold
        if (selectReleased && isHoldingGesture)
        {
            // Debounce check
            if (Time.time - lastTapTime < TAP_DEBOUNCE_INTERVAL)
            {
                Debug.Log("<color=yellow>⚡ Debounced — ignoring rapid tap</color>");
                isHoldingGesture = false;
                holdTimer = 0f;
                if (xrFeedback != null) xrFeedback.ResetFeedback();
                return;
            }
            lastTapTime = Time.time;
            
            if (holdProcessed)
            {
                // Hold gesture completed → open edit form ON RELEASE (not while still pinching)
                Debug.Log("<color=magenta>🖐️ XR HOLD released → Opening edit form</color>");
                HandleBuildingClick(true);
            }
            else
            {
                // Quick tap — view data
                Debug.Log("<color=cyan>👆 XR TAP gesture detected → Viewing building data</color>");
                if (xrFeedback != null) xrFeedback.OnTap();
                HandleBuildingClick(false);
            }
            
            isHoldingGesture = false;
            holdTimer = 0f;
            if (xrFeedback != null) xrFeedback.ResetFeedback();
        }
        
        // Handle case where gesture was abandoned (released without hitting threshold and without quick tap)
        if (selectReleased && !isHoldingGesture)
        {
            if (xrFeedback != null) xrFeedback.ResetFeedback();
        }
    }
    
    /// <summary>
    /// Reads the select/trigger state from all XR input devices.
    /// On HoloLens 2, air tap and pinch gestures map to primaryButton or triggerButton via OpenXR.
    /// </summary>
    bool GetXRSelectState()
    {
        // Reuse cached list to avoid per-frame GC allocation
        cachedSelectDevices.Clear();
        UnityEngine.XR.InputDevices.GetDevices(cachedSelectDevices);
        
        foreach (var device in cachedSelectDevices)
        {
            bool value;
            
            // HoloLens 2 air tap / hand pinch → primaryButton
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out value) && value)
                return true;
            
            // Fallback: trigger button (some controller configurations)
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out value) && value)
                return true;
            
            // Fallback: analog trigger axis > 0.5
            float triggerAxis;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out triggerAxis) && triggerAxis > 0.5f)
                return true;
        }
        
        return false;
    }
    
    void HandleBuildingClick(bool isRightClick)
    {
        Debug.Log($"<color=magenta>======== HandleBuildingClick CALLED ======== isRightClick={isRightClick}</color>");
        
        // Create ray based on input mode
        Ray ray;
        if (isXRDevice && useXRInput)
        {
            // HoloLens 2: Try XR hand ray first, fall back to head gaze
            ray = GetXRRay();
            Debug.Log($"<color=cyan>XR Ray: origin={ray.origin}, dir={ray.direction}</color>");
        }
        else
        {
            // Desktop: Use mouse screen position
            ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        }
        
        // Reposition UI canvas in front of user on HoloLens
        if (isXRDevice)
        {
            GameObject metadataCanvas = GameObject.Find("MetadataCanvas");
            if (metadataCanvas != null) PositionCanvasInFrontOfCamera(metadataCanvas);
        }
        
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            Debug.Log($"<color=cyan>✅ Raycast HIT: {hit.collider.gameObject.name}</color>");
            GameObject clickedObject = hit.collider.gameObject;
            
            // Check if this is a Cesium object before processing
            bool isCesiumObject = clickedObject.GetComponentInParent<Cesium3DTileset>() != null ||
                                  clickedObject.GetComponent<CesiumPrimitiveFeatures>() != null ||
                                  clickedObject.GetComponent<CesiumModelMetadata>() != null ||
                                  clickedObject.GetComponentInParent<CesiumModelMetadata>() != null;
            
            if (!isCesiumObject)
            {
                // Not a building — play miss feedback on XR
                if (xrFeedback != null) xrFeedback.OnMiss();
                return;
            }
            
            // Highlight the selected building visually
            Color highlightColor = isRightClick 
                ? new Color(1f, 0.6f, 0f, 1f)   // Orange for edit mode
                : new Color(0f, 0.75f, 1f, 1f);  // Cyan for info mode
            AddBuildingHighlight(clickedObject, highlightColor);
            
            // Get the CesiumPrimitiveFeatures component (on the mesh primitive)
            CesiumPrimitiveFeatures primitiveFeatures = clickedObject.GetComponent<CesiumPrimitiveFeatures>();
            
            if (primitiveFeatures != null)
            {
                // Get the feature ID from the raycast hit
                Int64 featureId = primitiveFeatures.GetFeatureIdFromRaycastHit(hit, 0);
                
                if (featureId >= 0)
                {
                    // Now find the CesiumModelMetadata component (usually on parent game object)
                    CesiumModelMetadata modelMetadata = clickedObject.GetComponentInParent<CesiumModelMetadata>();
                    
                    if (modelMetadata != null && modelMetadata.propertyTables != null && modelMetadata.propertyTables.Length > 0)
                    {
                        // Try to find gml:id from tileset metadata
                        string gmlId = ExtractGmlId(featureId, modelMetadata);
                        if (gmlId != null) gmlId = gmlId.Trim();
                        string objectName = clickedObject.name;
                        
                        Debug.Log($"<color=cyan>🏢 Building clicked: gml:id from tileset = '{gmlId}'</color>");
                        
                        // If energy manager is available and we have a gml_id
                        if (energyManager != null && !string.IsNullOrEmpty(gmlId))
                        {
                            if (isRightClick)
                            {
                                // Ctrl+Click - Fetch fresh attributes from field_type=basic API
                                Debug.Log($"<color=magenta>========== CTRL+CLICK FORM FLOW START ==========</color>");
                                Debug.Log($"<color=cyan>📝 Step 1: Ctrl+Click detected for gml:id '{gmlId}'</color>");
                                
                                if (attributesForm == null)
                                {
                                    Debug.LogError($"<color=red>❌ BLOCKED: attributesForm is null!</color>");
                                    return;
                                }
                                Debug.Log($"<color=green>✅ Step 2: attributesForm exists</color>");
                                
                                if (energyManager == null)
                                {
                                    Debug.LogError($"<color=red>❌ BLOCKED: energyManager is null!</color>");
                                    return;
                                }
                                Debug.Log($"<color=green>✅ Step 3: energyManager exists</color>");
                                
                                if (energyManager.buildingDataCache == null)
                                {
                                    Debug.LogError($"<color=red>❌ BLOCKED: buildingDataCache is null!</color>");
                                    return;
                                }
                                Debug.Log($"<color=green>✅ Step 4: buildingDataCache exists with {energyManager.buildingDataCache.Count} buildings</color>");
                                
                                Debug.Log($"<color=cyan>🔍 Step 5: Searching for building in cache...</color>");
                                    // First, find the building in cache (case-insensitive)
                                    string cacheKey;
                                    BuildingData cachedBuilding = FindBuildingInCache(gmlId, out cacheKey);
                                    
                                    if (cachedBuilding == null)
                                    {
                                        // Building not in data cache - fetch by modified_gml_id from API
                                        Debug.LogWarning($"<color=yellow>⚠️ Building '{gmlId}' not in cache - fetching from API...</color>");
                                        StartCoroutine(FetchThenOpenForm(gmlId));
                                        return;
                                    }
                                    
                                    // Get gml_id from GmlIdCache (the correct API identifier)
                                    // Use the matched cache key (correct casing) for lookup
                                    string gmlIdBasic = null;
                                    string lookupKey = cacheKey ?? gmlId;
                                    if (energyManager.gmlIdCache.ContainsKey(lookupKey))
                                    {
                                        gmlIdBasic = energyManager.gmlIdCache[lookupKey];
                                    }
                                    else
                                    {
                                        gmlIdBasic = cachedBuilding.gmlIdBasic;
                                    }
                                    
                                    Debug.Log($"<color=cyan>📋 modified_gml_id: '{gmlId}' → gml_id: '{gmlIdBasic}'</color>");
                                    
                                    if (string.IsNullOrEmpty(gmlIdBasic))
                                    {
                                        Debug.LogError($"<color=red>❌ No gml_id mapping for '{gmlId}' - run 'Hard Refresh Cache'</color>");
                                        return;
                                    }
                                    
                                    // Fetch basic attributes using gml_id and open form
                                    ShowLoadingOverlay("Loading attributes...");
                                    StartCoroutine(energyManager.FetchBasicAttributes(
                                        gmlIdBasic,
                                        (attributesData) => 
                                        {
                                            try
                                            {
                                                HideLoadingOverlay();
                                                attributesForm.ShowBuildingForm(gmlId, cachedBuilding);
                                            }
                                            catch (System.Exception ex)
                                            {
                                                HideLoadingOverlay();
                                                Debug.LogError($"<color=red>❌ ShowBuildingForm exception: {ex.Message}\n{ex.StackTrace}</color>");
                                            }
                                        },
                                        (error) =>
                                        {
                                            HideLoadingOverlay();
                                            Debug.LogError($"<color=red>❌ Failed to fetch attributes for '{gmlIdBasic}': {error}</color>");
                                        }
                                    ));
                            }
                            else
                            {
                                // Left click - Show data panel from cache
                                DisplayBuildingDataFromCache(gmlId, objectName, featureId);
                            }
                        }
                        else if (!isRightClick)
                        {
                            // Fallback to showing basic metadata - only on left click
                            DisplayMetadata(clickedObject.name, featureId, modelMetadata);
                        }
                    }
                    // ModelMetadata missing is normal for some tiles - silently ignore
                }
                // Invalid feature IDs are common for terrain and base tiles - silently ignore
            }
            // CesiumPrimitiveFeatures missing is normal for terrain/base tiles - silently ignore
        }
        else
        {
            // Raycast missed everything — feedback
            if (xrFeedback != null) xrFeedback.OnMiss();
        }
    }
    
    void DisplayMetadata(string objectName, Int64 featureId, CesiumModelMetadata modelMetadata)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<b><size=24>Building Metadata</size></b>");
        sb.AppendLine($"<b>Object:</b> {objectName}");
        sb.AppendLine($"<b>Feature ID:</b> {featureId}");
        sb.AppendLine();
        
        // Loop through property tables
        foreach (CesiumPropertyTable propertyTable in modelMetadata.propertyTables)
        {
            if (featureId < propertyTable.count)
            {
                // Get all property values for this feature
                var metadataValues = propertyTable.GetMetadataValuesForFeature(featureId);
                
                if (metadataValues.Count > 0)
                {
                    sb.AppendLine($"<b>Properties:</b>");
                    sb.AppendLine(new string('-', 40));
                    
                    foreach (var kvp in metadataValues)
                    {
                        string propertyName = kvp.Key;
                        CesiumMetadataValue value = kvp.Value;
                        
                        // Try to get the value as a string
                        string valueStr = value.GetString("");
                        if (string.IsNullOrEmpty(valueStr))
                        {
                            // Try as double
                            double doubleVal = value.GetDouble(double.NaN);
                            if (!double.IsNaN(doubleVal))
                            {
                                valueStr = doubleVal.ToString("F2");
                            }
                            else
                            {
                                // Try as integer
                                Int64 intVal = value.GetInt64(0);
                                valueStr = intVal.ToString();
                            }
                        }
                        
                        sb.AppendLine($"<b>{propertyName}:</b> {valueStr}");
                    }
                }
            }
        }
        
        metadataText.text = sb.ToString();
        metadataPanel.SetActive(true);
        isDisplaying = true;
        hideTimer = displayDuration;
    }
    
    void HideMetadata()
    {
        metadataPanel.SetActive(false);
        isDisplaying = false;
        RemoveBuildingHighlight();
    }
    
    void AddBuildingHighlight(GameObject building, Color highlightColor)
    {
        // Remove previous highlight
        RemoveBuildingHighlight();
        
        currentSelectedBuilding = building;
        MeshRenderer renderer = building.GetComponent<MeshRenderer>();
        
        if (renderer != null)
        {
            // Create highlight material with emission glow
            highlightMaterial = new Material(renderer.material);
            highlightMaterial.EnableKeyword("_EMISSION");
            highlightMaterial.SetColor("_EmissionColor", highlightColor * 2f); // Bright glow
            
            // Also set base color to highlight color
            if (highlightMaterial.HasProperty("_baseColorFactor"))
            {
                highlightMaterial.SetColor("_baseColorFactor", highlightColor);
            }
            if (highlightMaterial.HasProperty("_Color"))
            {
                highlightMaterial.color = highlightColor;
            }
            
            // Apply highlight material
            renderer.material = highlightMaterial;
        }
    }
    
    void RemoveBuildingHighlight()
    {
        if (currentSelectedBuilding != null)
        {
            MeshRenderer renderer = currentSelectedBuilding.GetComponent<MeshRenderer>();
            if (renderer != null && originalMaterial != null)
            {
                renderer.material = originalMaterial;
            }
            currentSelectedBuilding = null;
        }
    }
    
    /// <summary>
    /// Fetch building by modified_gml_id from the batch API to populate cache,
    /// then open the form with the correct gml_id from the API response.
    /// </summary>
    IEnumerator FetchThenOpenForm(string gmlId)
    {
        // Show loading overlay
        ShowLoadingOverlay("Fetching building data...");
        
        // Use RefreshSingleBuilding to fetch by modified_gml_id and populate cache
        yield return energyManager.RefreshSingleBuilding(gmlId);
        
        // Check if it's now in cache (case-insensitive)
        string fetchedKey = null;
        BuildingData cachedBuilding = FindBuildingInCache(gmlId, out fetchedKey);
        if (cachedBuilding != null)
        {
            Debug.Log($"<color=green>✅ Building fetched and cached. gml_id = '{cachedBuilding.gmlIdBasic}'</color>");
            
            string gmlIdBasic = cachedBuilding.gmlIdBasic;
            if (!string.IsNullOrEmpty(gmlIdBasic))
            {
                ShowLoadingOverlay("Loading attributes...");
                
                // Fetch basic attributes and open form
                yield return energyManager.FetchBasicAttributes(
                    gmlIdBasic,
                    (attributesData) => 
                    {
                        try
                        {
                            HideLoadingOverlay();
                            attributesForm.ShowBuildingForm(gmlId, cachedBuilding);
                        }
                        catch (System.Exception ex)
                        {
                            HideLoadingOverlay();
                            Debug.LogError($"<color=red>❌ Exception in ShowBuildingForm(): {ex.Message}\n{ex.StackTrace}</color>");
                        }
                    },
                    (error) =>
                    {
                        HideLoadingOverlay();
                        Debug.LogWarning($"<color=yellow>⚠️ Failed to fetch attributes for '{gmlIdBasic}': {error}</color>");
                    }
                );
            }
            else
            {
                HideLoadingOverlay();
                Debug.LogWarning($"<color=yellow>⚠️ Building '{gmlId}' has no gml_id in API response</color>");
            }
        }
        else
        {
            HideLoadingOverlay();
            Debug.Log($"<color=yellow>ℹ️ Building '{gmlId}' has no energy data in the database.</color>");
        }
    }

    string ExtractGmlId(Int64 featureId, CesiumModelMetadata modelMetadata)
    {
        // Try common property names for gml_id - note: Cesium often uses "gml:id" with colon
        string[] candidateKeys = { "gml:id", "gml_id", "gmlId", "id", "building_id", "buildingId" };
        
        foreach (CesiumPropertyTable propertyTable in modelMetadata.propertyTables)
        {
            if (featureId < propertyTable.count)
            {
                var metadataValues = propertyTable.GetMetadataValuesForFeature(featureId);
                
                // Always log all property keys and values for clicked building
                Debug.Log($"<color=magenta>🔍 Feature {featureId} has {metadataValues.Count} properties: [{string.Join(", ", metadataValues.Keys)}]</color>");
                foreach (var kvp in metadataValues)
                {
                    // Try multiple extraction methods since value might not be stored as string
                    string val = kvp.Value.GetString("");
                    if (string.IsNullOrEmpty(val))
                    {
                        // Try as other types
                        double dVal = kvp.Value.GetDouble(double.NaN);
                        if (!double.IsNaN(dVal))
                            val = $"(double){dVal}";
                        else
                        {
                            Int64 iVal = kvp.Value.GetInt64(Int64.MinValue);
                            if (iVal != Int64.MinValue)
                                val = $"(int64){iVal}";
                            else
                            {
                                bool bVal = kvp.Value.GetBoolean(false);
                                val = $"(other/empty) bool={bVal}";
                            }
                        }
                    }
                    Debug.Log($"<color=magenta>   {kvp.Key} = '{val}'</color>");
                }
                
                foreach (string key in candidateKeys)
                {
                    if (metadataValues.ContainsKey(key))
                    {
                        CesiumMetadataValue metaVal = metadataValues[key];
                        
                        // Try GetString first
                        string value = metaVal.GetString("");
                        if (!string.IsNullOrEmpty(value))
                        {
                            Debug.Log($"<color=cyan>Found gml_id from property '{key}' (string): '{value}'</color>");
                            return value;
                        }
                        
                        // If GetString returned empty, try GetObjectAsString or ToString
                        // CesiumMetadataValue might store it in a non-string format
                        try
                        {
                            string objStr = metaVal.ToString();
                            if (!string.IsNullOrEmpty(objStr) && objStr != "CesiumForUnity.CesiumMetadataValue")
                            {
                                Debug.Log($"<color=cyan>Found gml_id from property '{key}' (ToString): '{objStr}'</color>");
                                return objStr;
                            }
                        }
                        catch (System.Exception) { }
                    }
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Case-insensitive lookup in buildingDataCache.
    /// Tileset metadata may use different casing than the API (e.g., 'wid6' vs 'wiD6').
    /// </summary>
    private BuildingData FindBuildingInCache(string gmlId, out string matchedKey)
    {
        matchedKey = null;
        if (string.IsNullOrEmpty(gmlId)) return null;
        
        // Try exact match first (fast)
        if (energyManager.buildingDataCache.ContainsKey(gmlId))
        {
            matchedKey = gmlId;
            return energyManager.buildingDataCache[gmlId];
        }
        
        // Case-insensitive match (tileset vs API casing mismatch)
        foreach (var kvp in energyManager.buildingDataCache)
        {
            if (string.Equals(kvp.Key, gmlId, System.StringComparison.OrdinalIgnoreCase))
            {
                matchedKey = kvp.Key;
                Debug.Log($"<color=green>✅ Case-insensitive match: tileset '{gmlId}' → cache '{kvp.Key}'</color>");
                return kvp.Value;
            }
        }
        
        return null;
    }

    /// <summary>
    /// Display building data directly from cache (no API call, no authentication)
    /// </summary>
    void DisplayBuildingDataFromCache(string gmlId, string objectName, Int64 featureId)
    {
        Debug.Log($"<color=cyan>🔍 Looking up building from cache: '{gmlId}'</color>");
        Debug.Log($"<color=cyan>📦 Cache contains {energyManager.buildingDataCache.Count} buildings</color>");
        
        metadataPanel.SetActive(true);
        isDisplaying = true;
        
        // Case-insensitive lookup (tileset may use different casing than API)
        string matchedKey;
        BuildingData data = FindBuildingInCache(gmlId, out matchedKey);
        
        if (data != null)
        {
            DisplayBuildingData(data, objectName, featureId);
        }
        else
        {
            // Building not in the energy database - show clean minimal info
            Debug.Log($"<color=yellow>⚠️ No energy data for gml:id '{gmlId}' (cache has {energyManager.buildingDataCache.Count} buildings)</color>");
            
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"<b><size=40>Building Information</size></b>");
            sb.AppendLine();
            sb.AppendLine($"<size=26><b>Object:</b> {objectName}</size>");
            sb.AppendLine($"<size=26><b>Feature ID:</b> {featureId}</size>");
            sb.AppendLine();
            sb.AppendLine($"<size=26><b>GML ID:</b> {gmlId}</size>");
            sb.AppendLine();
            sb.AppendLine("<size=26><color=#AAAAAA>No energy data available for this building.</color></size>");
            
            metadataText.text = sb.ToString();
        }
        
        hideTimer = displayDuration;
    }
    
    /// <summary>
    /// [DEPRECATED - Only kept for backward compatibility]
    /// Old method that fetched from API - now replaced by DisplayBuildingDataFromCache
    /// </summary>
    System.Collections.IEnumerator FetchAndDisplayBuildingData(string gmlId, string objectName, Int64 featureId)
    {
        Debug.LogWarning($"<color=yellow>⚠️ FetchAndDisplayBuildingData is deprecated - use DisplayBuildingDataFromCache instead</color>");
        
        // Show loading message
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<b><size=40>Building Information</size></b>");
        sb.AppendLine();
        sb.AppendLine($"<b><size=28>GML ID:</b> {gmlId}</size>");
        sb.AppendLine();
        sb.AppendLine("<color=yellow>Loading data from API...</color>");
        
        metadataText.text = sb.ToString();
        metadataPanel.SetActive(true);
        isDisplaying = true;
        
        // Fetch data from energy manager
        yield return energyManager.FetchBuildingData(gmlId);
        
        // Check if exact match exists
        bool exactMatch = energyManager.buildingDataCache.ContainsKey(gmlId);
        
        // Display the fetched data
        if (exactMatch)
        {
            BuildingData data = energyManager.buildingDataCache[gmlId];
            DisplayBuildingData(data, objectName, featureId);
        }
        else
        {
            // Show error message
            sb.Clear();
            sb.AppendLine($"<b><size=40>Building Information</size></b>");
            sb.AppendLine();
            sb.AppendLine($"<size=26><b>GML ID:</b> {gmlId}</size>");
            sb.AppendLine();
            sb.AppendLine("<size=26><color=#AAAAAA>No energy data available for this building.</color></size>");
            
            metadataText.text = sb.ToString();
        }
        
        hideTimer = displayDuration;
    }
    
    void DisplayBuildingData(BuildingData data, string objectName, Int64 featureId)
    {
        // Remember what's displayed so we can refresh after save
        currentDisplayedGmlId = data.gmlId;
        currentDisplayedObjectName = objectName;
        currentDisplayedFeatureId = featureId;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<b><size=40>Building Information</size></b>");
        sb.AppendLine();
        sb.AppendLine($"<size=26><b>Object:</b> {objectName}</size>");
        sb.AppendLine($"<size=26><b>Feature ID:</b> {featureId}</size>");
        sb.AppendLine();
        
        // Energy color indicator from cache - try both modified_gml_id and gml_id
        Color energyColor = Color.gray;
        if (energyManager.buildingColorCache.ContainsKey(data.gmlId))
        {
            energyColor = energyManager.buildingColorCache[data.gmlId];
        }
        else if (!string.IsNullOrEmpty(data.gmlIdBasic) && energyManager.buildingColorCache.ContainsKey(data.gmlIdBasic))
        {
            energyColor = energyManager.buildingColorCache[data.gmlIdBasic];
        }
        string colorHex = ColorUtility.ToHtmlStringRGB(energyColor);
        
        sb.AppendLine($"<b><size=34><color=#{colorHex}>■</color> Building Details</size></b>");
        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"<b>GML ID:</b> {data.gmlId}");
        
        if (!string.IsNullOrEmpty(data.constructionYear))
            sb.AppendLine($"<b>Construction Year:</b> {data.constructionYear}");
        
        if (data.numberOfStorey > 0)
            sb.AppendLine($"<b>Number of Storeys:</b> {data.numberOfStorey}");
        
        sb.AppendLine();
        sb.AppendLine($"<b><size=32>CO2 Emissions [t CO2/a]</size></b>");
        sb.AppendLine($"<b>Before Renovation:</b> {data.co2Before:F3} t CO2/a");
        sb.AppendLine($"<b>After Renovation:</b> {data.co2After:F3} t CO2/a");
        
        sb.AppendLine();
        sb.AppendLine($"<b><size=32>Energy Demand Specific [kWh/m²a]</size></b>");
        sb.AppendLine($"<b>Before Renovation:</b> <color=#{colorHex}>{data.energyDemandBefore} kWh/m²a</color>");
        sb.AppendLine($"<b>After Renovation:</b> <color=#{colorHex}>{data.energyDemandAfter} kWh/m²a</color>");
        
        if (!string.IsNullOrEmpty(data.heatingSystemBefore))
        {
            sb.AppendLine();
            sb.AppendLine($"<b>Heating System (Before):</b> {data.heatingSystemBefore}");
            if (!string.IsNullOrEmpty(data.heatingSystemAfter))
                sb.AppendLine($"<b>Heating System (After):</b> {data.heatingSystemAfter}");
        }
        
        if (!string.IsNullOrEmpty(data.windowBefore))
        {
            sb.AppendLine();
            sb.AppendLine($"<b>Windows (Before):</b> {data.windowBefore}");
            if (!string.IsNullOrEmpty(data.windowAfter))
                sb.AppendLine($"<b>Windows (After):</b> {data.windowAfter}");
        }
        
        if (!string.IsNullOrEmpty(data.wallBefore))
        {
            sb.AppendLine();
            sb.AppendLine($"<b>Walls (Before):</b> {data.wallBefore}");
            if (!string.IsNullOrEmpty(data.wallAfter))
                sb.AppendLine($"<b>Walls (After):</b> {data.wallAfter}");
        }
        
        if (!string.IsNullOrEmpty(data.roofBefore))
        {
            sb.AppendLine();
            sb.AppendLine($"<b>Roof (Before):</b> {data.roofBefore}");
            if (!string.IsNullOrEmpty(data.roofAfter))
                sb.AppendLine($"<b>Roof (After):</b> {data.roofAfter}");
        }
        
        metadataText.text = sb.ToString();
        metadataPanel.SetActive(true);
        isDisplaying = true;
    }
    
    /// <summary>
    /// Public method to refresh the Building Information panel after data changes (e.g. after PUT save).
    /// Re-reads from the updated cache and re-renders the info text.
    /// </summary>
    public void RefreshDisplayedBuildingInfo()
    {
        if (string.IsNullOrEmpty(currentDisplayedGmlId))
        {
            Debug.Log("<color=yellow>⚠️ RefreshDisplayedBuildingInfo: No building currently displayed</color>");
            return;
        }
        
        Debug.Log($"<color=cyan>🔄 Refreshing Building Information panel for: {currentDisplayedGmlId}</color>");
        DisplayBuildingDataFromCache(currentDisplayedGmlId, currentDisplayedObjectName, currentDisplayedFeatureId);
    }

    // === LOADING OVERLAY ===
    
    void ShowLoadingOverlay(string message = "Loading...")
    {
        if (loadingOverlay != null)
        {
            loadingOverlay.SetActive(true);
            if (loadingText != null) loadingText.text = message;
            
            // reposition canvas in front of user on HoloLens
            if (isXRDevice)
            {
                GameObject metadataCanvas = GameObject.Find("MetadataCanvas");
                if (metadataCanvas != null) PositionCanvasInFrontOfCamera(metadataCanvas);
            }
        }
    }
    
    void HideLoadingOverlay()
    {
        if (loadingOverlay != null) loadingOverlay.SetActive(false);
    }

    void OnDestroy()
    {
        // Stop all coroutines to prevent GC handle issues on domain reload
        StopAllCoroutines();
    }
    
    /// <summary>
    /// Gets a ray for XR interaction. Tries XR hand ray controllers first (aim pose),
    /// then falls back to head gaze (camera forward).
    /// </summary>
    Ray GetXRRay()
    {
        // Reuse cached list to avoid GC allocation
        cachedRayDevices.Clear();
        
        // Check right hand first (most users are right-handed)
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller,
            cachedRayDevices);
        
        // Also check left hand
        if (cachedRayDevices.Count == 0)
        {
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller,
                cachedRayDevices);
        }
        
        // Also check hand tracking devices directly
        if (cachedRayDevices.Count == 0)
        {
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.HandTracking,
                cachedRayDevices);
        }
        
        foreach (var device in cachedRayDevices)
        {
            Vector3 aimPosition;
            Quaternion aimRotation;
            
            bool hasPos = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out aimPosition);
            bool hasRot = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out aimRotation);
            
            if (hasPos && hasRot && aimPosition != Vector3.zero)
            {
                Debug.Log($"<color=green>Using XR hand ray from device: {device.name}</color>");
                return new Ray(aimPosition, aimRotation * Vector3.forward);
            }
        }
        
        // Fallback: Head gaze (camera forward direction)
        Debug.Log("<color=yellow>Fallback: Using head gaze ray</color>");
        return new Ray(mainCamera.transform.position, mainCamera.transform.forward);
    }
    
    /// <summary>
    /// Positions a WorldSpace canvas 1.5m in front of the camera, facing the user.
    /// Used on HoloLens 2 where ScreenSpaceOverlay canvases are invisible.
    /// </summary>
    void PositionCanvasInFrontOfCamera(GameObject canvasObj)
    {
        if (mainCamera == null) return;
        
        Vector3 forward = mainCamera.transform.forward;
        forward.y = 0; // Keep canvas upright (don't tilt with head pitch)
        if (forward == Vector3.zero) forward = Vector3.forward;
        forward.Normalize();
        
        canvasObj.transform.position = mainCamera.transform.position + forward * 1.5f;
        canvasObj.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
}
