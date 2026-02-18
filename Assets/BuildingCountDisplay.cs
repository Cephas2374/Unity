using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays building count statistics on screen
/// Auto-updates when building data is loaded
/// </summary>
public class BuildingCountDisplay : MonoBehaviour
{
    [Header("References")]
    public BuildingEnergyManager energyManager;
    public Text displayText;
    
    [Header("Settings")]
    public bool autoUpdate = true;
    public float updateInterval = 1.0f; // Update every second
    
    [Header("Display Options")]
    public bool showTotal = true;
    public bool showWithColor = true;
    public bool showWithoutColor = false;
    public bool showLastUpdate = true;
    public bool showComparison = true; // Show API vs Tileset comparison
    
    [Header("Comparison Settings")]
    public CesiumFeatureColorizer featureColorizer;
    
    private float updateTimer = 0f;
    private GameObject textPanel;
    private int lastTilesetCount = 0;
    private float lastComparisonUpdate = 0f;
    private float comparisonUpdateInterval = 5f; // Update comparison every 5 seconds
    
    void Start()
    {
        if (energyManager == null)
        {
            energyManager = FindObjectOfType<BuildingEnergyManager>();
            if (energyManager == null)
            {
                Debug.LogError("BuildingCountDisplay: No BuildingEnergyManager found!");
                return;
            }
        }
        
        if (featureColorizer == null && showComparison)
        {
            featureColorizer = FindObjectOfType<CesiumFeatureColorizer>();
            if (featureColorizer == null)
            {
                Debug.LogWarning("BuildingCountDisplay: CesiumFeatureColorizer not found. Comparison display disabled.");
                showComparison = false;
            }
        }
        
        // Create UI if not assigned
        if (displayText == null)
        {
            CreateDisplayUI();
        }
        
        // Initial update
        UpdateDisplay();
    }
    
    void Update()
    {
        if (!autoUpdate || energyManager == null || displayText == null)
            return;
            
        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            UpdateDisplay();
        }
        
        // Keyboard shortcut: Ctrl+Shift+B = Toggle Building Count Display
        // On HoloLens 2 this won't fire (no keyboard) - call ToggleDisplay() from UI instead
#if !UNITY_WSA && !WINDOWS_UWP
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.B))
        {
            ToggleDisplay();
            Debug.Log($"<color=cyan>Building count display toggled: {(textPanel != null && textPanel.activeSelf ? "ON" : "OFF")}</color>");
        }
#endif
    }
    
    void CreateDisplayUI()
    {
        // Find or create canvas
        Canvas canvas = null;
        GameObject canvasObj = GameObject.Find("BuildingCountCanvas");
        
        if (canvasObj != null)
        {
            canvas = canvasObj.GetComponent<Canvas>();
        }
        
        if (canvas == null)
        {
            canvasObj = new GameObject("BuildingCountCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // Display on top
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        
        // Create panel background
        textPanel = new GameObject("BuildingCountPanel");
        textPanel.transform.SetParent(canvas.transform, false);
        
        RectTransform panelRect = textPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1); // Top left
        panelRect.anchorMax = new Vector2(0, 1);
        panelRect.pivot = new Vector2(0, 1);
        panelRect.sizeDelta = new Vector2(350, 150); // Larger to fit comparison
        panelRect.anchoredPosition = new Vector2(10, -10); // 10px from top-left
        
        Image panelBg = textPanel.AddComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.7f); // Semi-transparent black
        
        // Add subtle border
        Outline panelOutline = textPanel.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.12f, 0.53f, 0.70f, 1f); // Blue border
        panelOutline.effectDistance = new Vector2(2, -2);
        
        // Create text
        GameObject textObj = new GameObject("CountText");
        textObj.transform.SetParent(textPanel.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10); // Padding
        textRect.offsetMax = new Vector2(-10, -10);
        
        displayText = textObj.AddComponent<Text>();
        displayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        displayText.fontSize = 14;
        displayText.color = Color.white;
        displayText.alignment = TextAnchor.UpperLeft;
        displayText.text = "Loading building data...";
        
        Debug.Log("✅ Building count display UI created");
    }
    
    public void UpdateDisplay()
    {
        if (energyManager == null || displayText == null)
            return;
            
        var stats = energyManager.GetBuildingStatistics();
        
        string displayString = "<b><color=#1D85B8>Building Statistics</color></b>\n";
        
        if (showTotal)
        {
            displayString += $"<color=#4CAF50>📦 API Total: {stats["total"]}</color>\n";
        }
        
        if (showWithColor)
        {
            displayString += $"<color=#FFEB3B>🎨 Colored: {stats["withColor"]}</color>\n";
        }
        
        if (showWithoutColor && stats["withoutColor"] > 0)
        {
            displayString += $"<color=#FF9800>⚪ No Color: {stats["withoutColor"]}</color>\n";
        }
        
        // Add comparison with tileset
        if (showComparison && featureColorizer != null)
        {
            // Update tileset count periodically (expensive operation)
            if (Time.time - lastComparisonUpdate > comparisonUpdateInterval)
            {
                lastComparisonUpdate = Time.time;
                lastTilesetCount = featureColorizer.GetTilesetBuildingCount();
            }
            
            if (lastTilesetCount > 0)
            {
                displayString += $"<color=#03A9F4>🏢 Tileset: {lastTilesetCount}</color>\n";
                
                // Show actual match count (tileset buildings that have API energy data)
                int matchedCount = featureColorizer.GetMatchedBuildingCount();
                if (lastTilesetCount > 0)
                {
                    float matchPercent = matchedCount * 100f / lastTilesetCount;
                    string statusColor = matchPercent > 85f ? "#4CAF50" : matchPercent > 50f ? "#FF9800" : "#F44336";
                    displayString += $"<color={statusColor}>📊 Colored: {matchedCount}/{lastTilesetCount} ({matchPercent:F0}%)</color>\n";
                }
            }
        }
        
        if (showLastUpdate)
        {
            displayString += $"<color=#9E9E9E>🕐 {energyManager.lastCacheUpdate}</color>";
        }
        
        displayText.text = displayString;
    }
    
    /// <summary>
    /// Toggle the display on/off
    /// </summary>
    public void ToggleDisplay()
    {
        if (textPanel != null)
        {
            textPanel.SetActive(!textPanel.activeSelf);
        }
    }
    
    /// <summary>
    /// Show the display
    /// </summary>
    public void Show()
    {
        if (textPanel != null)
        {
            textPanel.SetActive(true);
            UpdateDisplay();
        }
    }
    
    /// <summary>
    /// Hide the display
    /// </summary>
    public void Hide()
    {
        if (textPanel != null)
        {
            textPanel.SetActive(false);
        }
    }
    
    void OnDestroy()
    {
        // Clean up created UI
        if (textPanel != null)
        {
            Destroy(textPanel);
        }
    }
}
