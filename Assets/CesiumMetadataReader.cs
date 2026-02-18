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
    private bool isXRDevice = false;
    private bool wasXRSelectPressed = false;

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
    }
    
    void DetectXRDevice()
    {
#if UNITY_WSA || WINDOWS_UWP
        isXRDevice = true;
        Debug.Log("<color=cyan>Running on HoloLens 2 / UWP platform</color>");
#elif ENABLE_INPUT_SYSTEM && UNITY_XR
        isXRDevice = UnityEngine.XR.XRSettings.isDeviceActive;
        Debug.Log($"<color=cyan>XR Device Active: {isXRDevice}</color>");
#else
        isXRDevice = UnityEngine.XR.XRSettings.isDeviceActive;
        Debug.Log($"<color=cyan>XR Device Active: {isXRDevice}</color>");
#endif

        // Check for manual override
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
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // Below the form
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        
        // Create Panel
        metadataPanel = new GameObject("MetadataPanel");
        metadataPanel.transform.SetParent(canvasObj.transform, false);
        
        Image panelImage = metadataPanel.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.85f);
        
        RectTransform panelRect = metadataPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.01f, 0.5f);
        panelRect.anchorMax = new Vector2(0.3f, 0.99f);
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
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10);
        textRect.offsetMax = new Vector2(-10, -10);
        
        metadataPanel.SetActive(false);
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
        // Quick tap = view building data, Hold (0.5s+) = open edit form
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
        
        // Track hold duration
        if (isHoldingGesture)
        {
            holdTimer += Time.deltaTime;
            
            // Check if hold duration reached and not yet processed
            if (holdTimer >= holdDuration && !holdProcessed)
            {
                // Hold gesture detected - open edit form
                Debug.Log("<color=magenta>\U0001f590️ XR HOLD gesture detected → Opening edit form</color>");
                HandleBuildingClick(true);
                holdProcessed = true;
                isHoldingGesture = false;
            }
        }
        
        // Handle gesture release
        if (selectReleased)
        {
            if (isHoldingGesture && !holdProcessed)
            {
                // Quick tap - view data
                Debug.Log("<color=cyan>\U0001f446 XR TAP gesture detected → Viewing building data</color>");
                HandleBuildingClick(false);
            }
            isHoldingGesture = false;
            holdTimer = 0f;
        }
    }
    
    /// <summary>
    /// Reads the select/trigger state from all XR input devices.
    /// On HoloLens 2, air tap and pinch gestures map to primaryButton or triggerButton via OpenXR.
    /// </summary>
    bool GetXRSelectState()
    {
        var inputDevices = new List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevices(inputDevices);
        
        foreach (var device in inputDevices)
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
            // HoloLens 2: Use head gaze direction (camera forward)
            ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
            Debug.Log($"<color=cyan>\U0001f3af XR Gaze Ray: origin={mainCamera.transform.position}, dir={mainCamera.transform.forward}</color>");
        }
        else
        {
            // Desktop: Use mouse screen position
            ray = mainCamera.ScreenPointToRay(Input.mousePosition);
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
                // Silently ignore non-Cesium objects (terrain, sky, etc.)
                return;
            }
            
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
                                    StartCoroutine(energyManager.FetchBasicAttributes(
                                        gmlIdBasic,
                                        (attributesData) => 
                                        {
                                            try
                                            {
                                                attributesForm.ShowBuildingForm(gmlId, cachedBuilding);
                                            }
                                            catch (System.Exception ex)
                                            {
                                                Debug.LogError($"<color=red>❌ ShowBuildingForm exception: {ex.Message}\n{ex.StackTrace}</color>");
                                            }
                                        },
                                        (error) =>
                                        {
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
                // Fetch basic attributes and open form
                yield return energyManager.FetchBasicAttributes(
                    gmlIdBasic,
                    (attributesData) => 
                    {
                        try
                        {
                            attributesForm.ShowBuildingForm(gmlId, cachedBuilding);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"<color=red>❌ Exception in ShowBuildingForm(): {ex.Message}\n{ex.StackTrace}</color>");
                        }
                    },
                    (error) =>
                    {
                        Debug.LogWarning($"<color=yellow>⚠️ Failed to fetch attributes for '{gmlIdBasic}': {error}</color>");
                    }
                );
            }
            else
            {
                Debug.LogWarning($"<color=yellow>⚠️ Building '{gmlId}' has no gml_id in API response</color>");
            }
        }
        else
        {
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

    void OnDestroy()
    {
        // Stop all coroutines to prevent GC handle issues on domain reload
        StopAllCoroutines();
    }
}
