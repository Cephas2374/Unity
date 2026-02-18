using UnityEngine;
using UnityEngine.Networking;
using CesiumForUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>
/// Manages building energy data from API - handles authentication, data caching, and color generation.
/// 
/// ARCHITECTURE:
/// - BuildingEnergyManager: Fetches data from API, caches building data and colors (this script)
/// - CesiumFeatureColorizer: Applies per-vertex colors to individual buildings in batched meshes
/// - CesiumMetadataReader: Handles building clicks and displays UI panels
/// 
/// NOTE: Old batch coloring methods (ApplyColorsToMeshes, ApplyColorToRenderer) are deprecated.
/// Use CesiumFeatureColorizer component for per-building coloring instead.
/// </summary>
public class BuildingEnergyManager : MonoBehaviour
{
    [Header("API Configuration")]
    [Tooltip("Base API URL for building energy data")]
    public string apiBaseUrl = "https://backend.gisworld-tech.com";
    
    [Tooltip("Community ID parameter for filtering buildings")]
    public string communityId = "08417008";
    
    [Header("Authentication (Auto-configured)")]
    [Tooltip("Access token obtained from authentication - auto-filled on Start")]
    public string accessToken = "";
    
    [Header("Initialization Settings")]
    [Tooltip("Disable auto-initialization on Start (useful for debugging domain reload issues)")]
    public bool disableAutoInit = false;
    
    [Tooltip("Delay before initialization (seconds) - increase if domain reload issues persist")]
    [Range(0.1f, 5f)]
    public float initDelay = 0.1f;
    
    [Tooltip("⚠️ WARNING: Auto-refresh scans tileset at startup. Only enable AFTER tiles have loaded at least once! For first run, use manual 'Hard Refresh Cache'.")]
    public bool autoRefreshOnStartup = false;
    
    private bool isAuthenticating = false;
    private bool isInitialized = false;
    
    [Header("Cesium Configuration")]
    [Tooltip("Name of the Cesium3DTileset GameObject to apply colors to")]
    public string buildingsTilesetName = "bisingen";
    
    [Tooltip("Use Cesium styling JSON (recommended) or direct material manipulation")]
    public bool enableCesiumStyling = true;
    
    [Header("Color Settings")]
    [Tooltip("Fallback color for buildings without data - NOT used if API provides colors")]
    public Color defaultColor = Color.gray;
    
    [Header("UI References")]
    public BuildingInfoPanel buildingInfoPanel;
    
    [Header("Smart Caching System")]
    [Tooltip("Enable persistent disk cache (survives editor restarts)")]
    public bool enablePersistentCache = true;
    
    [Tooltip("Cache file path (auto-generated)")]
    [SerializeField] private string cacheFilePath = "";
    
    [Header("Real-Time Updates")]
    [Tooltip("REAL-TIME MODE: Disable persistent cache, always fetch fresh data, poll for updates every interval")]
    public bool realTimeMode = false;
    
    [Tooltip("Enable change detection for external edits (polling) - ALWAYS ENABLED")]
    public bool enableChangeDetection = true;
    
    [Tooltip("How often to check for external changes (seconds) - 5 seconds for real-time responsiveness")]
    [Range(5f, 3600f)]
    public float changeCheckInterval = 5f; // 5 seconds for real-time updates
    
    // Public for CesiumMetadataReader and CesiumFeatureColorizer access
    public Dictionary<string, BuildingData> buildingDataCache = new Dictionary<string, BuildingData>();
    public Dictionary<string, Color> buildingColorCache = new Dictionary<string, Color>();
    
    /// <summary>
    /// Maps modified_gml_id → gml_id (e.g., "DEBW_0010008wrid6" → "DEBWL0010008wrid6")
    /// Same as UE5's GmlIdCache - needed because gml_id cannot be derived by string manipulation
    /// </summary>
    public Dictionary<string, string> gmlIdCache = new Dictionary<string, string>();
    
    private Cesium3DTileset buildingsTileset;
    private float changeCheckTimer = 0f;
    private HashSet<string> modifiedBuildingIds = new HashSet<string>(); // Track buildings modified in this session
    private Dictionary<string, DateTime> buildingLastUpdated = new Dictionary<string, DateTime>(); // Track update timestamps
    private bool isPollingForUpdates = false; // Prevent concurrent polling
    
    [Header("Cache Management")]
    [Tooltip("Last cache update timestamp")]
    public string lastCacheUpdate = "Never";
    
    [Tooltip("Number of buildings currently in cache")]
    public int cachedBuildingCount = 0;
    
    [Header("Building Statistics")]
    [Tooltip("Total buildings loaded from API")]
    public int totalBuildingsLoaded = 0;
    
    [Tooltip("Buildings with color data")]
    public int buildingsWithColor = 0;
    
    [Tooltip("Buildings without color data")]
    public int buildingsWithoutColor = 0;
    
    void Start()
    {
        // Delay initialization to avoid domain reload conflicts
        if (!disableAutoInit)
        {
            StartCoroutine(DelayedStart());
        }
        else
        {
            Debug.LogWarning("BuildingEnergyManager: Auto-initialization is DISABLED. Call InitializeManager() manually.");
        }
        
        // Auto-refresh if enabled
        if (autoRefreshOnStartup)
        {
            Debug.Log("<color=cyan>🔄 Auto-Refresh enabled - will refresh cache after initialization</color>");
        }
    }
    
    IEnumerator DelayedStart()
    {
        // Wait for Unity to fully initialize and domain reload to complete
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(initDelay);
        
        // Double-check domain is not reloading
        if (Application.isPlaying && this != null && gameObject != null)
        {
            // Auto-refresh if enabled (clears cache, token, and forces fresh authentication)
            if (autoRefreshOnStartup)
            {
                Debug.Log("<color=cyan>🔄 Auto-Refresh: Clearing cache, token, and forcing fresh authentication...</color>");
                ClearPersistentCache();
                accessToken = ""; // Clear token to force re-authentication
                isAuthenticating = false; // Reset auth flag
            }
            
            yield return InitializeManager();
        }
    }
    
    IEnumerator InitializeManager()
    {
        if (isInitialized)
        {
            Debug.LogWarning("BuildingEnergyManager already initialized!");
            yield break;
        }
        
        isInitialized = true;
        
        Debug.Log($"<color=cyan>🚀 BuildingEnergyManager: Smart Caching System Initialized</color>");
        Debug.Log($"<color=cyan>   • Community: {communityId}</color>");
        Debug.Log($"<color=cyan>   • Real-Time Mode: {(realTimeMode ? "ENABLED (always fresh data)" : "DISABLED (uses cache)")}</color>");
        Debug.Log($"<color=cyan>   • Persistent Cache: {(enablePersistentCache && !realTimeMode ? "ENABLED" : "DISABLED")}</color>");
        Debug.Log($"<color=cyan>   • Change Detection: {(enableChangeDetection ? $"ENABLED (every {changeCheckInterval}s)" : "DISABLED")}</color>");
        
        FindBuildingsTileset();
        
        // Initialize cache file path
        if (string.IsNullOrEmpty(cacheFilePath))
        {
            cacheFilePath = System.IO.Path.Combine(Application.persistentDataPath, $"building_cache_{communityId}.json");
            Debug.Log($"<color=yellow>📁 Cache file: {cacheFilePath}</color>");
        }
        
        // Try to load RAW JSON from persistent cache first (FAST - no parsing during load)
        // SKIP CACHE if realTimeMode is enabled
        bool cacheLoaded = false;
        if (enablePersistentCache && !realTimeMode)
        {
            string rawCachePath = cacheFilePath.Replace(".json", "_raw.json");
            if (System.IO.File.Exists(rawCachePath))
            {
                Debug.Log($"<color=green>📦 Found raw JSON cache - loading instantly...</color>");
                cacheLoaded = LoadRawJsonFromDisk();
                
                if (cacheLoaded && parsedJsonArray != null)
                {
                    Debug.Log($"<color=green>✅ Raw JSON loaded from disk (INSTANT) - {parsedJsonArray.Count} buildings available</color>");
                    Debug.Log($"<color=yellow>⏳ Parsing buildings now...</color>");
                    
                    // Parse buildings from cached JSON (with yield for responsiveness)
                    yield return ParseBuildingsDataLazy();
                    
                    Debug.Log($"<color=green>✅ Loaded {buildingDataCache.Count} buildings from cache</color>");
                    Debug.Log($"<color=green>   Last update: {lastCacheUpdate}</color>");
                    Debug.Log($"<color=cyan>💡 No API download needed - using cached data!</color>");
                    
                    // Authenticate in background to get fresh token for future API calls
                    Debug.Log($"<color=cyan>🔐 Authenticating in background for future API calls...</color>");
                    yield return Authenticate();
                    
                    // Notify colorizer that data is ready
                    NotifyColorizerDataReady();
                    yield break; // Done! No need to download from API
                }
            }
            
            // Fallback to legacy cache format
            if (!cacheLoaded && System.IO.File.Exists(cacheFilePath))
            {
                Debug.Log($"<color=yellow>📦 Found legacy cache file - loading...</color>");
                cacheLoaded = LoadCacheFromDisk();
                
                if (cacheLoaded)
                {
                    Debug.Log($"<color=green>✅ Cache loaded from disk: {buildingDataCache.Count} buildings</color>");
                    Debug.Log($"<color=green>   Last update: {lastCacheUpdate}</color>");
                    Debug.Log($"<color=cyan>💡 No API download needed - using cached data!</color>");
                    
                    // Authenticate in background to get fresh token for future API calls
                    Debug.Log($"<color=cyan>🔐 Authenticating in background for future API calls...</color>");
                    yield return Authenticate();
                    
                    // Notify colorizer that data is ready
                    NotifyColorizerDataReady();
                    yield break; // Done! No need to download from API
                }
            }
        }
        
        // Cache not found or failed to load - download from API
        if (!cacheLoaded)
        {
            Debug.Log($"<color=yellow>📥 No cache found - downloading from API (first time only)...</color>");
            
            // Auto-authenticate if no token
            if (string.IsNullOrEmpty(accessToken))
            {
                Debug.Log("⚠️ No access token found. Starting authentication...");
                yield return AuthenticateAndLoadData();
            }
            else
            {
                Debug.Log($"✅ Using existing access token (length: {accessToken.Length})");
                yield return DownloadAndCacheAllBuildings();
            }
        }
    }
    
    void Update()
    {
        // Keyboard shortcut for clearing cache (Ctrl+Shift+Delete)
        // On HoloLens 2 this won't fire (no keyboard) - call ClearPersistentCache() from UI instead
#if !UNITY_WSA && !WINDOWS_UWP
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.Delete))
        {
            Debug.Log("<color=cyan>Keyboard shortcut triggered: Ctrl+Shift+Delete - Clear Cache</color>");
            ClearPersistentCache();
        }
#endif
        
        // Periodic check for external changes (if enabled)
        if (enableChangeDetection && buildingDataCache.Count > 0)
        {
            changeCheckTimer += Time.deltaTime;
            if (changeCheckTimer >= changeCheckInterval)
            {
                changeCheckTimer = 0f;
                StartCoroutine(CheckForExternalChanges());
            }
        }
    }
    
    // ========================================
    // PERSISTENT CACHE MANAGEMENT
    // ========================================
    
    /// <summary>
    /// Save RAW JSON cache to disk (FAST - no processing)
    /// </summary>
    public bool SaveRawJsonToDisk(string rawJson)
    {
        // Skip saving in real-time mode (always fetch fresh)
        if (realTimeMode)
            return true;
        
        if (!enablePersistentCache)
            return false;
            
        try
        {
            // Save raw JSON directly without processing
            string rawCachePath = cacheFilePath.Replace(".json", "_raw.json");
            System.IO.File.WriteAllText(rawCachePath, rawJson);
            
            // Save metadata separately
            JObject metadata = new JObject();
            metadata["communityId"] = this.communityId;
            metadata["lastUpdate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            metadata["buildingCount"] = 0; // Will be counted when parsed
            
            string metadataJson = metadata.ToString();
            string metadataPath = cacheFilePath.Replace(".json", "_metadata.json");
            System.IO.File.WriteAllText(metadataPath, metadataJson);
            
            Debug.Log($"<color=green>✅ Raw JSON cached to disk (INSTANT)</color>");
            Debug.Log($"<color=green>   File: {rawCachePath}</color>");
            Debug.Log($"<color=green>   Size: {rawJson.Length / 1024}KB</color>");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>❌ Failed to save raw JSON: {e.Message}</color>");
            return false;
        }
    }
    
    /// <summary>
    /// Save cache to disk for persistent storage (LEGACY - for backward compatibility)
    /// </summary>
    public bool SaveCacheToDisk()
    {
        if (!enablePersistentCache)
            return false;
            
        try
        {
            var cacheData = new CacheContainer
            {
                communityId = this.communityId,
                lastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                buildings = new List<BuildingCacheEntry>()
            };
            
            // Convert dictionary to serializable list
            foreach (var kvp in buildingDataCache)
            {
                var entry = new BuildingCacheEntry
                {
                    gmlId = kvp.Key,
                    data = kvp.Value,
                    color = "#" + ColorUtility.ToHtmlStringRGB(buildingColorCache.ContainsKey(kvp.Key) ? buildingColorCache[kvp.Key] : Color.white)
                };
                cacheData.buildings.Add(entry);
            }
            
            string json = JsonUtility.ToJson(cacheData, true);
            System.IO.File.WriteAllText(cacheFilePath, json);
            
            Debug.Log($"<color=green>✅ Cache saved to disk: {cacheData.buildings.Count} buildings</color>");
            Debug.Log($"<color=green>   File: {cacheFilePath}</color>");
            Debug.Log($"<color=green>   Size: {json.Length / 1024}KB</color>");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>❌ Failed to save cache to disk: {e.Message}</color>");
            return false;
        }
    }
    
    /// <summary>
    /// Load RAW JSON from cache and parse on-demand (FAST)
    /// </summary>
    private string rawJsonCache = null;
    private JArray parsedJsonArray = null;
    
    private bool LoadRawJsonFromDisk()
    {
        if (!enablePersistentCache)
            return false;
            
        string rawCachePath = cacheFilePath.Replace(".json", "_raw.json");
        string metadataPath = cacheFilePath.Replace(".json", "_metadata.json");
        
        if (!System.IO.File.Exists(rawCachePath))
            return false;
            
        try
        {
            // Load raw JSON (FAST - no parsing yet)
            rawJsonCache = System.IO.File.ReadAllText(rawCachePath);
            
            // Load metadata to verify
            if (System.IO.File.Exists(metadataPath))
            {
                string metadataJson = System.IO.File.ReadAllText(metadataPath);
                JObject metadata = JObject.Parse(metadataJson);
                
                if (metadata["communityId"].ToString() != this.communityId)
                {
                    Debug.LogWarning($"<color=yellow>⚠️ Cache is for different community</color>");
                    return false;
                }
                
                Debug.Log($"<color=green>✅ Raw JSON loaded from disk (INSTANT)</color>");
                Debug.Log($"<color=green>   Size: {rawJsonCache.Length / 1024}KB</color>");
                Debug.Log($"<color=green>   Last update: {metadata["lastUpdate"]}</color>");
            }
            
            // Parse JSON array structure only (no building parsing yet)
            parsedJsonArray = JArray.Parse(rawJsonCache);
            Debug.Log($"<color=cyan>✅ JSON structure parsed: {parsedJsonArray.Count} buildings available</color>");
            
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>❌ Failed to load raw JSON: {e.Message}</color>");
            rawJsonCache = null;
            parsedJsonArray = null;
            return false;
        }
    }
    
    /// <summary>
    /// Load cache from disk (LEGACY - for backward compatibility)
    /// </summary>
    private bool LoadCacheFromDisk()
    {
        if (!enablePersistentCache || !System.IO.File.Exists(cacheFilePath))
            return false;
            
        try
        {
            string json = System.IO.File.ReadAllText(cacheFilePath);
            var cacheData = JsonUtility.FromJson<CacheContainer>(json);
            
            if (cacheData == null || cacheData.buildings == null)
            {
                Debug.LogWarning("<color=yellow>⚠️ Cache file is corrupt or empty</color>");
                return false;
            }
            
            // Verify it's for the same community
            if (cacheData.communityId != this.communityId)
            {
                Debug.LogWarning($"<color=yellow>⚠️ Cache is for different community ({cacheData.communityId} vs {this.communityId})</color>");
                return false;
            }
            
            // Load into dictionaries
            buildingDataCache.Clear();
            buildingColorCache.Clear();
            
            foreach (var entry in cacheData.buildings)
            {
                buildingDataCache[entry.gmlId] = entry.data;
                if (ColorUtility.TryParseHtmlString(entry.color, out Color color))
                {
                    buildingColorCache[entry.gmlId] = color;
                }
            }
            
            lastCacheUpdate = cacheData.lastUpdate;
            cachedBuildingCount = buildingDataCache.Count;
            totalBuildingsLoaded = buildingDataCache.Count;
            buildingsWithColor = buildingColorCache.Count;
            buildingsWithoutColor = buildingDataCache.Count - buildingColorCache.Count;
            
            Debug.Log($"<color=green>✅ Cache loaded successfully</color>");
            Debug.Log($"<color=green>   Buildings: {buildingDataCache.Count}</color>");
            Debug.Log($"<color=green>   Colors: {buildingColorCache.Count}</color>");
            Debug.Log($"<color=green>   Last update: {lastCacheUpdate}</color>");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>❌ Failed to load cache from disk: {e.Message}</color>");
            return false;
        }
    }
    
    /// <summary>
    /// Clear persistent cache file from disk
    /// </summary>
    [ContextMenu("Clear Persistent Cache")]
    public void ClearPersistentCache()
    {
        Debug.Log("<color=cyan>=== Clearing Persistent Cache ===</color>");
        
        // Clear memory
        int oldCount = buildingDataCache.Count;
        buildingDataCache.Clear();
        buildingColorCache.Clear();
        modifiedBuildingIds.Clear();
        cachedBuildingCount = 0;
        totalBuildingsLoaded = 0;
        buildingsWithColor = 0;
        buildingsWithoutColor = 0;
        lastCacheUpdate = "Cleared";
        
        // Delete file
        if (System.IO.File.Exists(cacheFilePath))
        {
            try
            {
                System.IO.File.Delete(cacheFilePath);
                Debug.Log($"<color=green>✅ Cache file deleted: {cacheFilePath}</color>");
            }
            catch (Exception e)
            {
                Debug.LogError($"<color=red>❌ Failed to delete cache file: {e.Message}</color>");
            }
        }
        
        Debug.Log($"<color=yellow>🗑️ Cleared {oldCount} buildings from memory</color>");
        Debug.Log($"<color=cyan>💡 Restart the scene to download fresh data from API</color>");
    }
    
    /// <summary>
    /// Open the cache folder in Windows Explorer to view JSON files
    /// </summary>
    [ContextMenu("Open Cache Folder")]
    public void OpenCacheFolder()
    {
        string folderPath = System.IO.Path.GetDirectoryName(cacheFilePath);
        
        if (System.IO.Directory.Exists(folderPath))
        {
            // Open folder in Windows Explorer
            System.Diagnostics.Process.Start("explorer.exe", folderPath);
            Debug.Log($"<color=green>📂 Opening cache folder:</color>");
            Debug.Log($"<color=cyan>   {folderPath}</color>");
        }
        else
        {
            Debug.LogWarning($"<color=yellow>⚠️ Cache folder does not exist yet:</color>");
            Debug.LogWarning($"<color=yellow>   {folderPath}</color>");
            Debug.Log($"<color=cyan>💡 Run 'Hard Refresh Cache' to create it</color>");
        }
    }
    
    /// <summary>
    /// Log the exact path to the cache files
    /// </summary>
    [ContextMenu("Show Cache File Paths")]
    public void ShowCacheFilePaths()
    {
        string rawCachePath = cacheFilePath.Replace(".json", "_raw.json");
        string metadataPath = cacheFilePath.Replace(".json", "_metadata.json");
        
        Debug.Log($"<color=cyan>========== CACHE FILE PATHS ==========</color>");
        Debug.Log($"<color=yellow>📁 Cache folder:</color>");
        Debug.Log($"<color=white>   {System.IO.Path.GetDirectoryName(cacheFilePath)}</color>");
        Debug.Log($"");
        Debug.Log($"<color=yellow>📄 Raw JSON cache:</color>");
        Debug.Log($"<color=white>   {rawCachePath}</color>");
        Debug.Log($"<color=cyan>   Exists: {System.IO.File.Exists(rawCachePath)}</color>");
        if (System.IO.File.Exists(rawCachePath))
        {
            long sizeBytes = new System.IO.FileInfo(rawCachePath).Length;
            Debug.Log($"<color=cyan>   Size: {sizeBytes / 1024}KB ({sizeBytes:N0} bytes)</color>");
        }
        Debug.Log($"");
        Debug.Log($"<color=yellow>📄 Metadata:</color>");
        Debug.Log($"<color=white>   {metadataPath}</color>");
        Debug.Log($"<color=cyan>   Exists: {System.IO.File.Exists(metadataPath)}</color>");
        Debug.Log($"");
        Debug.Log($"<color=yellow>📄 Legacy cache (old format):</color>");
        Debug.Log($"<color=white>   {cacheFilePath}</color>");
        Debug.Log($"<color=cyan>   Exists: {System.IO.File.Exists(cacheFilePath)}</color>");
        Debug.Log($"<color=cyan>======================================</color>");
    }
    
    /// <summary>
    /// Hard refresh: Clear cache AND immediately reload from API (one-click operation)
    /// Use this when tileset or API data changes significantly
    /// </summary>
    [ContextMenu("Hard Refresh Cache (Clear & Reload)")]
    public void HardRefreshCacheAndReload()
    {
        Debug.Log("<color=cyan>🔄 === HARD REFRESH: Clear & Reload ===</color>");
        
        // Clear existing cache
        ClearPersistentCache();
        
        // Clear access token to force fresh authentication
        accessToken = "";
        Debug.Log("<color=yellow>🔐 Cleared access token - will re-authenticate</color>");
        
        // Reset initialization flag so it will reinitialize
        isInitialized = false;
        
        // Force stop any running coroutines
        StopAllCoroutines();
        
        // Reset authentication (will re-authenticate if needed)
        isAuthenticating = false;
        isPollingForUpdates = false;
        
        Debug.Log("<color=cyan>⏳ Starting fresh download from API...</color>");
        
        // Re-initialize immediately
        StartCoroutine(InitializeManager());
    }
    
    /// <summary>
    /// Force an immediate poll for updates (bypasses timer)
    /// </summary>
    [ContextMenu("Force Poll for Updates Now")]
    public void ForcePollNow()
    {
        if (!enableChangeDetection)
        {
            Debug.LogWarning("<color=yellow>⚠️ Change detection is disabled. Enable it in Inspector first.</color>");
            return;
        }
        
        if (string.IsNullOrEmpty(accessToken))
        {
            Debug.LogWarning("<color=yellow>⚠️ Not authenticated. Run 'Hard Refresh Cache' first.</color>");
            return;
        }
        
        Debug.Log("<color=cyan>🔍 Forcing immediate update check...</color>");
        changeCheckTimer = 0f; // Reset timer
        StopCoroutine(CheckForExternalChanges());
        StartCoroutine(CheckForExternalChanges());
    }
    
    /// <summary>
    /// Notify CesiumFeatureColorizer that data is ready for coloring
    /// </summary>
    private void NotifyColorizerDataReady()
    {
        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        if (colorizer != null)
        {
            Debug.Log($"<color=green>✅ Notifying colorizer: {buildingDataCache.Count} buildings, {buildingColorCache.Count} colors ready</color>");
            
            // Wait for tiles to load before coloring
            StartCoroutine(WaitForTilesAndColor(colorizer));
        }
        else
        {
            Debug.LogWarning("<color=yellow>CesiumFeatureColorizer not found - tiles won't be colored</color>");
        }
    }
    
    /// <summary>
    /// Wait for Cesium tiles to load before attempting to color buildings
    /// </summary>
    private IEnumerator WaitForTilesAndColor(CesiumFeatureColorizer colorizer)
    {
        Debug.Log("<color=yellow>⏳ Waiting for Cesium tiles to load before coloring...</color>");
        
        // Quick check if tiles are already loaded (happens in Play mode when cache loads fast)
        if (colorizer.tileset != null)
        {
            CesiumForUnity.CesiumPrimitiveFeatures[] quickCheck = colorizer.tileset.GetComponentsInChildren<CesiumForUnity.CesiumPrimitiveFeatures>();
            if (quickCheck.Length > 0)
            {
                Debug.Log($"<color=green>✅ Tiles already loaded! Found {quickCheck.Length} tiles, coloring immediately...</color>");
                yield return colorizer.RecolorAllTilesWithLogging();
                yield break;
            }
        }
        
        // Wait a few frames for tiles to start loading
        yield return new WaitForSeconds(3f);
        
        // Check if any tiles are loaded
        int attempts = 0;
        int maxAttempts = 60; // 60 seconds max wait (tiles can take time on slow connections)
        
        while (attempts < maxAttempts)
        {
            int cesiumRenderers = 0;
            
            // Check tileset children recursively (tiles might be nested)
            if (colorizer.tileset != null)
            {
                // Check all descendants, not just direct children
                CesiumForUnity.CesiumPrimitiveFeatures[] features = colorizer.tileset.GetComponentsInChildren<CesiumForUnity.CesiumPrimitiveFeatures>();
                cesiumRenderers = features.Length;
                
                if (cesiumRenderers > 0)
                {
                    Debug.Log($"<color=green>✅ Tiles loaded! Found {cesiumRenderers} Cesium tiles in tileset hierarchy</color>");
                    Debug.Log($"<color=cyan>🎨 Starting automatic building coloring...</color>");
                    yield return colorizer.RecolorAllTilesWithLogging();
                    Debug.Log($"<color=green>✅ Automatic coloring complete!</color>");
                    yield break;
                }
            }
            else
            {
                // Fallback: search all mesh renderers in scene
                MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>();
                foreach (var renderer in renderers)
                {
                    if (renderer.GetComponent<CesiumForUnity.CesiumPrimitiveFeatures>() != null)
                    {
                        cesiumRenderers++;
                    }
                }
                
                if (cesiumRenderers > 0)
                {
                    Debug.Log($"<color=green>✅ Tiles loaded! Found {cesiumRenderers} Cesium mesh renderers</color>");
                    Debug.Log($"<color=cyan>🎨 Starting automatic building coloring...</color>");
                    yield return colorizer.RecolorAllTilesWithLogging();
                    Debug.Log($"<color=green>✅ Automatic coloring complete!</color>");
                    yield break;
                }
            }
            
            attempts++;
            yield return new WaitForSeconds(1f);
            
            if (attempts % 10 == 0)
            {
                int childCount = colorizer.tileset != null ? colorizer.tileset.transform.childCount : 0;
                Debug.Log($"<color=yellow>⏳ Still waiting for tiles... ({attempts}s elapsed, tileset has {childCount} direct children)</color>");
            }
        }
        
        Debug.LogWarning("<color=orange>⚠️ Tiles not loaded after 60 seconds - attempting to color anyway</color>");
        Debug.LogWarning("<color=cyan>💡 TIP: Ensure Cesium tileset URL is correct and accessible</color>");
        yield return colorizer.RecolorAllTilesWithLogging();
    }
    
    // ========================================
    // SMART UPDATE - SINGLE BUILDING ONLY
    // ========================================
    
    /// <summary>
    /// Update a single building after editing (efficient - no full reload)
    /// </summary>
    public IEnumerator UpdateSingleBuilding(string gmlId)
    {
        Debug.Log($"<color=cyan>🔄 Updating single building: {gmlId}</color>");
        
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        string url = $"{apiBaseUrl}/geospatial/buildings-energy/?community_id={communityId}&modified_gml_id={UnityWebRequest.EscapeURL(gmlId)}&format=json&include_colors=true&energy_type=total&time_period=annual&classification=co2&color_scheme=co2_classes&_t={timestamp}";
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 10;
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Cache-Control", "no-cache");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    JArray buildings = JArray.Parse(request.downloadHandler.text);
                    
                    foreach (JObject building in buildings)
                    {
                        string buildingId = building["modified_gml_id"]?.ToString();
                        if (buildingId == gmlId)
                        {
                            BuildingData data = ParseSingleBuilding(building);
                            if (data != null)
                            {
                                buildingDataCache[gmlId] = data;
                                modifiedBuildingIds.Add(gmlId);
                                Debug.Log($"<color=green>✅ Building {gmlId} updated in cache</color>");
                                
                                // [DEPRECATED] Save to disk - now handled by UpdateBuildingInRawCache in RefreshSingleBuilding
                                // SaveCacheToDisk();
                                
                                // Update visual for this building only
                                UpdateBuildingVisual(gmlId);
                                yield break;
                            }
                        }
                    }
                    
                    Debug.LogWarning($"<color=yellow>⚠️ Building {gmlId} not found in API response</color>");
                }
                catch (Exception e)
                {
                    Debug.LogError($"<color=red>❌ Failed to parse update: {e.Message}</color>");
                }
            }
            else
            {
                Debug.LogError($"<color=red>❌ Failed to fetch building: {request.error}</color>");
            }
        }
    }
    
    /// <summary>
    /// Update the visual color for a single building (efficient recolor)
    /// </summary>
    private void UpdateBuildingVisual(string gmlId)
    {
        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        if (colorizer != null)
        {
            // Get the color for this building
            if (buildingColorCache.TryGetValue(gmlId, out Color color))
            {
                Debug.Log($"<color=cyan>🎨 Updating visual for building: {gmlId}</color>");
                colorizer.RecolorSingleBuilding(gmlId, color);
            }
        }
    }
    
    /// <summary>
    /// Check for buildings modified by external systems - polls API for changes
    /// Downloads fresh data and compares with cache to detect updates
    /// </summary>
    private IEnumerator CheckForExternalChanges()
    {
        if (!enableChangeDetection || string.IsNullOrEmpty(accessToken) || isPollingForUpdates)
            yield break;
            
        isPollingForUpdates = true;
        
        Debug.Log("<color=cyan>🔍 Polling API for updated building data...</color>");
        
        // Poll API with aggressive cache-busting
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string url = $"{apiBaseUrl}/geospatial/buildings-energy/?community_id={communityId}&format=json&include_colors=true&energy_type=total&time_period=annual&classification=co2&color_scheme=co2_classes&_t={timestamp}";
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 30;
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
            request.SetRequestHeader("Pragma", "no-cache");
            request.SetRequestHeader("Expires", "0");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    JArray freshData = JArray.Parse(request.downloadHandler.text);
                    int updatedCount = 0;
                    int newCount = 0;
                    
                    // Compare with cached data and update changes
                    foreach (JObject building in freshData)
                    {
                        string gmlId = building["modified_gml_id"]?.ToString();
                        if (string.IsNullOrEmpty(gmlId)) continue;
                        
                        // Check if building data has changed
                        bool hasChanged = false;
                        
                        if (buildingDataCache.ContainsKey(gmlId))
                        {
                            // Check if energy data changed (simple comparison)
                            var existingData = buildingDataCache[gmlId];
                            var energyResult = building["energy_result"];
                            
                            if (energyResult != null)
                            {
                                int? newEnergyDemand = energyResult["end"]?["result"]?["energy_demand_specific"]?["value"]?.ToObject<int?>();
                                if (newEnergyDemand.HasValue && newEnergyDemand.Value != existingData.energyDemandAfter)
                                {
                                    hasChanged = true;
                                }
                            }
                        }
                        else
                        {
                            hasChanged = true;
                            newCount++;
                        }
                        
                        if (hasChanged)
                        {
                            // Update building in cache
                            BuildingData updatedData = ParseSingleBuilding(building);
                            if (updatedData != null)
                            {
                                buildingDataCache[gmlId] = updatedData;
                                buildingLastUpdated[gmlId] = DateTime.Now;
                                updatedCount++;
                                
                                // Update visual
                                if (buildingColorCache.ContainsKey(gmlId))
                                {
                                    UpdateBuildingVisual(gmlId);
                                }
                            }
                        }
                    }
                    
                    if (updatedCount > 0 || newCount > 0)
                    {
                        Debug.Log($"<color=green>✅ Detected changes: {updatedCount} updated, {newCount} new buildings</color>");
                        
                        // Update cache file if persistent cache is enabled
                        if (enablePersistentCache && !realTimeMode)
                        {
                            SaveRawJsonToDisk(request.downloadHandler.text);
                        }
                    }
                    else
                    {
                        Debug.Log($"<color=gray>ℹ️ No changes detected since last check</color>");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"<color=red>❌ Failed to check for changes: {e.Message}</color>");
                }
            }
            else if (request.responseCode == 401)
            {
                Debug.LogWarning("<color=yellow>⚠️ Authentication expired, re-authenticating...</color>");
                yield return Authenticate();
            }
        }
        
        isPollingForUpdates = false;
    }
    
    // ========================================
    // DEPRECATED - FULL RELOAD (OLD SYSTEM)
    // ========================================
    
    /// <summary>
    /// DEPRECATED: Use HardRefreshCacheAndReload() instead
    /// This method is kept for backward compatibility only
    /// </summary>
    [ContextMenu("[DEPRECATED] Hard Refresh Cache")]
    public void HardRefreshCache()
    {
        Debug.LogWarning("<color=orange>⚠️ HardRefreshCache is DEPRECATED!</color>");
        Debug.LogWarning("<color=yellow>💡 Use 'Hard Refresh Cache (Clear & Reload)' instead!</color>");
        HardRefreshCacheAndReload();
    }
    
    void FindBuildingsTileset()
    {
        try
        {
            Cesium3DTileset[] tilesets = FindObjectsOfType<Cesium3DTileset>();
            if (tilesets == null || tilesets.Length == 0)
            {
                Debug.LogWarning("No Cesium3DTileset found in scene. Will retry later.");
                return;
            }
            
            foreach (var tileset in tilesets)
            {
                if (tileset.name.Contains(buildingsTilesetName))
                {
                    buildingsTileset = tileset;
                    Debug.Log($"Found buildings tileset: {tileset.name}");
                    break;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Error finding tileset (likely domain reload): {e.Message}");
        }
    }
    
    /// <summary>
    /// Authenticate and get access token (without loading building data)
    /// Use this for refreshing expired tokens during runtime
    /// </summary>
    IEnumerator Authenticate()
    {
        if (isAuthenticating)
        {
            Debug.LogWarning("Authentication already in progress");
            yield break;
        }
        
        isAuthenticating = true;
        Debug.Log("<color=cyan>=== Authenticating to get access token ===</color>");
        
        string authUrl = $"{apiBaseUrl}/api/token/";
        string jsonPayload = "{\"username\": \"hft_api\", \"password\": \"Stegsteg2025\"}";
        
        using (UnityWebRequest request = UnityWebRequest.Post(authUrl, jsonPayload, "application/json"))
        {
            request.timeout = 10;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string response = request.downloadHandler.text;
                
                try
                {
                    JObject json = JObject.Parse(response);
                    accessToken = json["access"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        Debug.Log($"<color=green>✅ Authentication successful! Token length: {accessToken.Length}</color>");
                        Debug.Log($"<color=green>   Token preview: {accessToken.Substring(0, Math.Min(20, accessToken.Length))}...</color>");
                    }
                    else
                    {
                        Debug.LogError("Authentication response missing 'access' token");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to parse auth response: {e.Message}");
                }
            }
            else
            {
                Debug.LogError($"<color=red>❌ Authentication failed: {request.error}</color>");
                Debug.LogError($"Response Code: {request.responseCode}");
                Debug.LogError($"Response: {request.downloadHandler.text}");
            }
        }
        
        isAuthenticating = false;
    }
    
    IEnumerator AuthenticateAndLoadData()
    {
        if (isAuthenticating)
        {
            Debug.LogWarning("Authentication already in progress");
            yield break;
        }
        
        isAuthenticating = true;
        Debug.Log("=== Starting Authentication ===");
        
        string authUrl = $"{apiBaseUrl}/api/token/";
        
        // Create JSON payload with credentials (from UE5 implementation)
        string jsonPayload = "{\"username\": \"hft_api\", \"password\": \"Stegsteg2025\"}";
        
        using (UnityWebRequest request = UnityWebRequest.Post(authUrl, jsonPayload, "application/json"))
        {
            request.timeout = 10; // 10 second timeout for auth
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            
            Debug.Log($"Auth URL: {authUrl}");
            Debug.Log($"Auth Payload: {jsonPayload}");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string response = request.downloadHandler.text;
                Debug.Log($"Auth Response: {response}");
                
                JObject json = null;
                try
                {
                    json = JObject.Parse(response);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to parse auth response: {e.Message}");
                }
                
                if (json != null)
                {
                    accessToken = json["access"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        Debug.Log($"✓ Authentication successful! Token length: {accessToken.Length}");
                        Debug.Log($"Token preview: {accessToken.Substring(0, Math.Min(20, accessToken.Length))}...");
                        
                        // Now load building data
                        yield return DownloadAndCacheAllBuildings();
                    }
                    else
                    {
                        Debug.LogError("Authentication response missing 'access' token");
                    }
                }
            }
            else
            {
                Debug.LogError($"Authentication failed: {request.error}");
                Debug.LogError($"Response Code: {request.responseCode}");
                Debug.LogError($"Response: {request.downloadHandler.text}");
            }
        }
        
        isAuthenticating = false;
    }
    
    /// <summary>
    /// Helper class to track batch download results (coroutines can't use ref parameters)
    /// </summary>
    private class BatchDownloadResult
    {
        public int downloaded = 0;
        public int failed = 0;
    }
    
    /// <summary>
    /// Downloads ONLY buildings that exist in the tileset (smart caching)
    /// Much faster than downloading all 5005 buildings!
    /// NOTE: Only works AFTER tiles have loaded. On first startup, will download all.
    /// </summary>
    public IEnumerator DownloadAndCacheAllBuildings()
    {
        Debug.Log("<color=cyan>=== Smart Caching: Downloading ONLY buildings from tileset ===</color>");
        
        // Step 1: Scan tileset to find which buildings we actually need
        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        if (colorizer == null)
        {
            Debug.LogError("<color=red>❌ CesiumFeatureColorizer not found! Falling back to download all buildings</color>");
            yield return DownloadAllBuildingsFromAPI();
            yield break;
        }
        
        Debug.Log("<color=yellow>📊 Step 1: Scanning tileset to find building IDs...</color>");
        yield return colorizer.ScanTilesetForBuildingIds(showStats: false);
        
        HashSet<string> tilesetBuildingIds = colorizer.GetUniqueTilesetBuildingIds();
        Debug.Log($"<color=green>✅ Scan complete: Found {tilesetBuildingIds.Count} buildings in tileset</color>");
        
        if (tilesetBuildingIds.Count == 0)
        {
            Debug.LogWarning("<color=orange>⚠️ No buildings in tileset yet (tiles not loaded).</color>");
            Debug.LogWarning("<color=orange>💡 RECOMMENDATION: Uncheck 'Auto Refresh On Startup', let tiles load, then use 'Hard Refresh Cache'</color>");
            Debug.LogWarning("<color=orange>📥 Falling back to downloading ALL buildings (this will take 5-10 minutes)...</color>");
            yield return DownloadAllBuildingsFromAPI();
            yield break;
        }
        
        // Check if we're downloading significantly fewer buildings
        if (tilesetBuildingIds.Count < 100)
        {
            Debug.LogWarning($"<color=yellow>⚠️ Only found {tilesetBuildingIds.Count} buildings - this seems low. Tiles may not be fully loaded yet.</color>");
        }
        
        Debug.Log($"<color=cyan>💡 Smart Caching Strategy: Download ALL buildings → Save raw JSON → Parse only {tilesetBuildingIds.Count} needed</color>");
        
        // Step 2: Download ALL buildings as raw JSON (much faster than one-by-one)
        Debug.Log($"<color=yellow>📥 Step 2: Downloading complete building dataset from API...</color>");
        
        string url = $"{apiBaseUrl}/geospatial/buildings-energy/?community_id={communityId}&format=json&include_colors=true&energy_type=total&time_period=annual&classification=co2&color_scheme=co2_classes";
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 120;
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
            request.SetRequestHeader("Pragma", "no-cache");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text;
                Debug.Log($"<color=green>✅ Downloaded {jsonResponse.Length / 1024}KB from API</color>");
                
                // ✅ OPTIMIZATION: Save raw JSON immediately (FAST - no parsing)
                if (enablePersistentCache)
                {
                    SaveRawJsonToDisk(jsonResponse);
                    Debug.Log($"<color=green>💾 Raw JSON cached to disk (INSTANT!)</color>");
                }
                
                // Store raw JSON and parse array structure only
                rawJsonCache = jsonResponse;
                parsedJsonArray = JArray.Parse(jsonResponse);
                Debug.Log($"<color=cyan>✅ JSON structure parsed: {parsedJsonArray.Count} buildings available</color>");
                
                // Step 3: Parse ONLY buildings that are in the tileset
                Debug.Log($"<color=yellow>⏳ Step 3: Parsing only {tilesetBuildingIds.Count} buildings in tileset...</color>");
                yield return ParseBuildingsForTileset(tilesetBuildingIds);
                
                Debug.Log($"<color=green>✅ Smart caching complete: {buildingDataCache.Count} buildings parsed and cached</color>");
                
                // Recolor tiles with the downloaded data
                if (buildingDataCache.Count > 0)
                {
                    Debug.Log($"<color=cyan>🎨 Starting tile recoloring...</color>");
                    yield return colorizer.StartCoroutine(colorizer.RecolorAllTilesWithLogging());
                }
            }
            else
            {
                Debug.LogError($"<color=red>❌ Failed to download buildings: {request.error}</color>");
                Debug.LogError($"<color=yellow>💡 Try 'Hard Refresh Cache' again</color>");
            }
        }
    }
    
    /* === DEPRECATED METHODS (Commented Out - Using Raw JSON Cache Instead) ===
    
    /// <summary>
    /// [DEPRECATED] Download a batch of buildings by their IDs
    /// This is now replaced by: Download ALL → Save raw JSON → Parse only needed buildings
    /// </summary>
    /*
    IEnumerator DownloadBuildingBatch(List<string> buildingIds, BatchDownloadResult result)
    {
        foreach (string gmlId in buildingIds)
        {
            // Check if already cached
            if (buildingDataCache.ContainsKey(gmlId))
            {
                result.downloaded++;
                continue;
            }
            
            string url = $"{apiBaseUrl}/geospatial/buildings-energy/?community_id={communityId}&modified_gml_id={UnityEngine.Networking.UnityWebRequest.EscapeURL(gmlId)}&format=json&include_colors=true&energy_type=total&time_period=annual&classification=co2&color_scheme=co2_classes";
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 10;
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");
                
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        JArray buildings = JArray.Parse(request.downloadHandler.text);
                        if (buildings.Count > 0)
                        {
                            BuildingData data = ParseSingleBuilding(buildings[0] as JObject);
                            if (data != null)
                            {
                                result.downloaded++;
                            }
                            else
                            {
                                result.failed++;
                            }
                        }
                        else
                        {
                            result.failed++;
                        }
                    }
                    catch
                    {
                        result.failed++;
                    }
                }
                else
                {
                    result.failed++;
                }
            }
        }
    }
    
    */
    
    // End of deprecated methods
    // ========================================
    
    /// <summary>
    /// Downloads ALL buildings from API and saves as raw JSON
    /// </summary>
    IEnumerator DownloadAllBuildingsFromAPI()
    {
        Debug.Log("<color=yellow>⚠️ Downloading ALL buildings from API (slower method)...</color>");
        
        string url = $"{apiBaseUrl}/geospatial/buildings-energy/?community_id={communityId}&format=json&include_colors=true&energy_type=total&time_period=annual&classification=co2&color_scheme=co2_classes";
        
        Debug.Log($"<color=yellow>📡 API Request URL: {url}</color>");
        
        if (string.IsNullOrEmpty(accessToken))
        {
            Debug.LogError("<color=red>❌ CRITICAL: Access token is empty! Cannot download buildings.</color>");
            Debug.LogError("<color=yellow>💡 Solution: Check authentication or run 'Hard Refresh Cache'</color>");
            yield break;
        }
        
        Debug.Log($"<color=green>🔑 Access Token length: {accessToken.Length}</color>");
        Debug.Log($"<color=green>🔑 Token preview: {accessToken.Substring(0, Math.Min(30, accessToken.Length))}...</color>");
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 120; // 2 MINUTE timeout for large datasets (5000+ buildings)
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.SetRequestHeader("Content-Type", "application/json");
            
            // Add cache-busting headers like UE5 implementation
            request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
            request.SetRequestHeader("Pragma", "no-cache");
            
            Debug.Log("<color=cyan>⏳ Starting download... (this may take 30-60 seconds for large datasets)</color>");
            
            yield return request.SendWebRequest();
            
            Debug.Log($"<color=cyan>📥 Download completed! Status: {request.result}</color>");
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text;
                Debug.Log($"\u2705 API Response received: {jsonResponse.Length} characters");
                
                if (jsonResponse.Length < 100)
                {
                    Debug.LogError($"<color=red>❌ SUSPICIOUS: Response too short ({jsonResponse.Length} chars)!</color>");
                    Debug.LogError($"<color=orange>Response: {jsonResponse}</color>");
                    Debug.LogError($"<color=yellow>💡 This might indicate API returned an error as text</color>");
                }
                else
                {
                    Debug.Log($"Response preview: {jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length))}...");
                }
                
                // ✅ OPTIMIZATION: Save raw JSON immediately (FAST - no parsing)
                if (enablePersistentCache)
                {
                    SaveRawJsonToDisk(jsonResponse);
                    Debug.Log($"<color=green>💾 Raw JSON cached instantly!</color>");
                }
                
                // Store raw JSON for lazy parsing
                rawJsonCache = jsonResponse;
                parsedJsonArray = JArray.Parse(jsonResponse);
                
                Debug.Log($"<color=cyan>✅ {parsedJsonArray.Count} buildings available</color>");
                Debug.Log($"<color=yellow>⏳ Parsing only buildings needed for tileset...</color>");
                
                // Parse only buildings that match tileset IDs (smart caching already collected these)
                yield return ParseBuildingsDataLazy();
                
                // CRITICAL CHECK: Verify data was actually cached
                if (buildingDataCache.Count == 0)
                {
                    Debug.LogError($"<color=red>🚨 CRITICAL FAILURE: Downloaded data but NOTHING was cached!</color>");
                    Debug.LogError($"<color=red>📊 BuildingDataCache: {buildingDataCache.Count} entries</color>");
                    Debug.LogError($"<color=red>📊 BuildingColorCache: {buildingColorCache.Count} entries</color>");
                    Debug.LogError($"<color=yellow>💡 Possible causes:</color>");
                    Debug.LogError($"<color=yellow>   • JSON parsing failed</color>");
                    Debug.LogError($"<color=yellow>   • API returned different format than expected</color>");
                    Debug.LogError($"<color=yellow>   • All buildings failed to parse individually</color>");
                    yield break; // Don't proceed to coloring
                }
                
                // Now that data is loaded, recolor all existing tiles with the real energy data
                CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
                if (colorizer != null)
                {
                    Debug.Log($"<color=green>✓ Data loaded! Cache: {buildingDataCache.Count} buildings, {buildingColorCache.Count} colors</color>");
                    Debug.Log($"<color=cyan>🎨 Starting tile recoloring (may take 10-30 seconds for large datasets)...</color>");
                    
                    // Start the recoloring and wait for it
                    yield return colorizer.StartCoroutine(colorizer.RecolorAllTilesWithLogging());
                    
                    Debug.Log($"<color=green>✓✓✓ All done! Buildings colored with energy data.</color>");
                }
                else
                {
                    Debug.LogWarning("<color=yellow>CesiumFeatureColorizer not found - tiles won't be colored</color>");
                }
            }
            else
            {
                Debug.LogError($"<color=red>❌ DOWNLOAD FAILED: {request.error}</color>");
                Debug.LogError($"<color=red>📟 Response Code: {request.responseCode}</color>");
                
                if (request.responseCode == 401)
                {
                    Debug.LogError($"<color=red>AUTHENTICATION ERROR - Token invalid or expired!</color>");
                    Debug.Log("<color=cyan>Auto-refreshing token and retrying...</color>");
                    
                    // Auto-refresh: clear token, re-authenticate, and retry
                    accessToken = "";
                    yield return Authenticate();
                    
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        Debug.Log("<color=green>Token refreshed - retrying download...</color>");
                        yield return DownloadAllBuildingsFromAPI();
                        yield break; // Exit this call since we're retrying
                    }
                    else
                    {
                        Debug.LogError("<color=red>Re-authentication failed! Check API credentials.</color>");
                    }
                }
                else if (request.responseCode == 0)
                {
                    Debug.LogError($"<color=red>🌐 NETWORK/TIMEOUT ERROR - No response from server!</color>");
                    Debug.LogError($"<color=yellow>💡 Solutions:</color>");
                    Debug.LogError($"<color=yellow>   • Check internet connection</color>");
                    Debug.LogError($"<color=yellow>   • Try again (server may be busy)</color>");
                    Debug.LogError($"<color=yellow>   • Hard Refresh Cache to retry</color>");
                }
                else if (request.responseCode >= 500)
                {
                    Debug.LogError($"<color=red>🖥️ SERVER ERROR - API backend is having issues!</color>");
                    Debug.LogError($"<color=yellow>💡 Solution: Wait and try 'Hard Refresh Cache' later</color>");
                }
                
                if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                {
                    Debug.LogError($"<color=orange>📄 Response Body: {request.downloadHandler.text}</color>");
                }
                
                Debug.LogError($"<color=red>⚠️ RESULT: No buildings cached - all buildings will stay white!</color>");
                Debug.LogError($"<color=yellow>🔧 TO FIX: Right-click BuildingEnergyManager → 'Hard Refresh Cache (Clear & Reload)'</color>");
            }
        }
    }
    
    /// <summary>
    /// Parse buildings lazily - only extract IDs and metadata (FAST)
    /// Full parsing happens on-demand when building is accessed
    /// </summary>
    IEnumerator ParseBuildingsDataLazy()
    {
        Debug.Log("=== Starting Lazy Parsing (ID extraction only) ===");
        
        if (parsedJsonArray == null || parsedJsonArray.Count == 0)
        {
            Debug.LogError("❌ No buildings found in parsed JSON array");
            yield break;
        }
        
        Debug.Log($"✅ Extracting IDs from {parsedJsonArray.Count} buildings...");
        
        int processedCount = 0;
        int batchSize = 100;
        
        // Extract only IDs and minimal data for matching (FAST)
        foreach (JObject building in parsedJsonArray)
        {
            try
            {
                string gmlId = building["modified_gml_id"]?.ToString();
                if (!string.IsNullOrEmpty(gmlId))
                {
                    // Parse full building data
                    BuildingData data = ParseSingleBuilding(building);
                    if (data != null)
                    {
                        buildingDataCache[data.gmlId] = data;
                        processedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to extract ID: {ex.Message}");
            }
            
            // Yield every batch to avoid blocking
            if (processedCount % batchSize == 0)
            {
                Debug.Log($"   Processed {processedCount}/{parsedJsonArray.Count}...");
                yield return null;
            }
        }
        
        // Update statistics after lazy parsing
        cachedBuildingCount = buildingDataCache.Count;
        totalBuildingsLoaded = buildingDataCache.Count;
        buildingsWithColor = buildingColorCache.Count;
        buildingsWithoutColor = buildingDataCache.Count - buildingColorCache.Count;
        lastCacheUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        int skippedCount = parsedJsonArray.Count - processedCount;
        Debug.Log($"<color=green>✅ Lazy parsing complete: {processedCount} buildings indexed, {skippedCount} skipped (out of {parsedJsonArray.Count} total)</color>");
        Debug.Log($"<color=green>   Colors in cache: {buildingColorCache.Count}</color>");
        if (skippedCount > 0)
        {
            Debug.LogWarning($"<color=orange>⚠️ {skippedCount} buildings failed to parse - they will not appear in cache!</color>");
        }
    }
    
    /// <summary>
    /// Parse ONLY buildings that are in the tileset (smart caching optimization)
    /// </summary>
    IEnumerator ParseBuildingsForTileset(HashSet<string> tilesetBuildingIds)
    {
        if (parsedJsonArray == null || parsedJsonArray.Count == 0)
        {
            Debug.LogError("❌ No parsed JSON array available");
            yield break;
        }
        
        Debug.Log($"=== Parsing {tilesetBuildingIds.Count} buildings from tileset ===");
        
        int processedCount = 0;
        int matchedCount = 0;
        int batchSize = 100;
        
        foreach (JObject building in parsedJsonArray)
        {
            try
            {
                string gmlId = building["modified_gml_id"]?.ToString();
                string gmlIdBasic = building["gml_id"]?.ToString();
                
                // Only parse if this building is in the tileset
                // Check BOTH modified_gml_id AND gml_id since tileset gml:id may use either format
                bool inTileset = false;
                if (!string.IsNullOrEmpty(gmlId) && tilesetBuildingIds.Contains(gmlId))
                    inTileset = true;
                else if (!string.IsNullOrEmpty(gmlIdBasic) && tilesetBuildingIds.Contains(gmlIdBasic))
                    inTileset = true;
                
                if (inTileset)
                {
                    BuildingData data = ParseSingleBuilding(building);
                    if (data != null)
                    {
                        buildingDataCache[data.gmlId] = data;
                        matchedCount++;
                    }
                }
                
                processedCount++;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to parse building: {ex.Message}");
            }
            
            // Yield every batch to avoid blocking
            if (processedCount % batchSize == 0)
            {
                Debug.Log($"   Processed {processedCount}/{parsedJsonArray.Count}, matched {matchedCount}...");
                yield return null;
            }
        }
        
        // Update statistics
        cachedBuildingCount = buildingDataCache.Count;
        totalBuildingsLoaded = buildingDataCache.Count;
        buildingsWithColor = buildingColorCache.Count;
        buildingsWithoutColor = buildingDataCache.Count - buildingColorCache.Count;
        lastCacheUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        Debug.Log($"<color=green>✅ Selective parsing complete:</color>");
        Debug.Log($"<color=green>   Scanned: {processedCount} buildings</color>");
        Debug.Log($"<color=green>   Matched: {matchedCount} buildings in tileset</color>");
        Debug.Log($"<color=green>   Cached: {buildingDataCache.Count} buildings, {buildingColorCache.Count} colors</color>");
    }
    
    void ParseBuildingsData(string jsonResponse)
    {
        Debug.Log("=== Starting ParseBuildingsData (LEGACY) ===");
        try
        {
            // API returns array directly, not object with "buildings" property
            JArray buildingsArray = JArray.Parse(jsonResponse);
            Debug.Log($"✅ JSON parsed successfully as array with {buildingsArray.Count} buildings");
            
            if (buildingsArray == null || buildingsArray.Count == 0)
            {
                Debug.LogError("❌ No buildings found in API response");
                return;
            }
            
            Debug.Log($"✅ Found {buildingsArray.Count} buildings in JSON");
            
            int successCount = 0;
            int failCount = 0;
            
            foreach (JObject building in buildingsArray)
            {
                try
                {
                    BuildingData data = ParseSingleBuilding(building);
                    if (data != null)
                    {
                        buildingDataCache[data.gmlId] = data;
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    Debug.LogWarning($"Failed to parse building: {ex.Message}");
                }
            }
            
            Debug.Log($"✅ Successfully cached {successCount} buildings, {failCount} failed");
            Debug.Log($"✅ Cache totals: {buildingDataCache.Count} buildings, {buildingColorCache.Count} colors");
            
            // 📅 Update cache metadata and statistics
            cachedBuildingCount = buildingDataCache.Count;
            totalBuildingsLoaded = buildingDataCache.Count;
            buildingsWithColor = buildingColorCache.Count;
            buildingsWithoutColor = buildingDataCache.Count - buildingColorCache.Count;
            lastCacheUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            
            Debug.Log($"<color=green>📅 Cache timestamp: {lastCacheUpdate}</color>");
            Debug.Log($"<color=cyan>📊 Building Statistics:</color>");
            Debug.Log($"<color=cyan>   • Total Buildings: {totalBuildingsLoaded}</color>");
            Debug.Log($"<color=cyan>   • With Color Data: {buildingsWithColor}</color>");
            Debug.Log($"<color=cyan>   • Without Color: {buildingsWithoutColor}</color>");
            
            // Sample cache keys for debugging
            Debug.Log($"<color=yellow>=== GML ID MAPPING DEBUG ===</color>");
            Debug.Log($"<color=yellow>API uses field: 'modified_gml_id'</color>");
            Debug.Log($"<color=yellow>Tileset uses field: 'gml:id'</color>");
            Debug.Log($"<color=yellow>These should contain the same values!</color>");
            
            // Show first 10 modified_gml_id values from API for better debugging
            var sampleIds = buildingDataCache.Keys.Take(10).ToList();
            Debug.Log($"<color=cyan>Sample modified_gml_id values from API (first 10):</color>");
            foreach (string id in sampleIds)
            {
                Color color = buildingColorCache.ContainsKey(id) ? buildingColorCache[id] : Color.clear;
                string colorHex = color != Color.clear ? ColorUtility.ToHtmlStringRGB(color) : "NONE";
                Debug.Log($"<color=cyan>  '{id}' → Color: #{colorHex}</color>");
            }
            
            // Enhanced color debugging
            Debug.Log($"<color=yellow>=== COLOR VERIFICATION (EXACT API COLORS) ===</color>");
            Debug.Log($"<color=yellow>Total buildings with colors: {buildingColorCache.Count}</color>");
            
            var colorStats = new Dictionary<string, int>();
            var colorToBuildings = new Dictionary<string, List<string>>(); // Track which buildings share colors
            
            foreach (var kvp in buildingColorCache)
            {
                string colorHex = ColorUtility.ToHtmlStringRGB(kvp.Value);
                
                if (!colorStats.ContainsKey(colorHex)) colorStats[colorHex] = 0;
                colorStats[colorHex]++;
                
                // Track color duplicates (this is NORMAL and expected!)
                if (!colorToBuildings.ContainsKey(colorHex)) colorToBuildings[colorHex] = new List<string>();
                colorToBuildings[colorHex].Add(kvp.Key);
            }
            
            Debug.Log($"<color=cyan>✅ {colorStats.Count} unique colors from API (exact hex values)</color>");
            
            // Show duplicate color info (this is expected - multiple buildings can have same energy rating)
            int colorsWithMultipleBuildings = colorToBuildings.Count(x => x.Value.Count > 1);
            Debug.Log($"<color=cyan>📊 {colorsWithMultipleBuildings} colors shared by multiple buildings (same energy ratings - NORMAL)</color>");
            
            var sampleColors = buildingColorCache.Take(3).ToList();
            Debug.Log($"<color=cyan>Sample exact API colors:</color>");
            foreach (var kvp in sampleColors)
            {
                Debug.Log($"<color=yellow>  {kvp.Key} → EXACT API #{ColorUtility.ToHtmlStringRGB(kvp.Value)}</color>");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing building data: {e.Message}");
            Debug.LogError($"Stack trace: {e.StackTrace}");
        }
    }
    
    
    private BuildingData ParseSingleBuilding(JObject building)
    {
        try
        {
            string gmlId = building["modified_gml_id"]?.ToString();
            if (string.IsNullOrEmpty(gmlId))
            {
                return null;
            }
            
            // Debug: Show what we're extracting from API
            if (buildingDataCache.Count < 10 || gmlId.Contains("DEBW_0010008wid6")) // Enhanced logging for debugging
            {
                Debug.Log($"<color=green>📋 Parsing API building #{buildingDataCache.Count + 1}: modified_gml_id = '{gmlId}'</color>");
            }
            
            BuildingData data = new BuildingData
            {
                gmlId = gmlId,                                              // modified_gml_id (with underscore)
                gmlIdBasic = building["gml_id"]?.ToString(),                // gml_id (no underscore) for basic API
                databaseId = building["id"]?.ToString(),                    // database id for PUT requests
                constructionYear = building["construction_year_class"]?.ToString(),
                numberOfStorey = building["storey"]?.ToObject<int?>() ?? building["number_of_storey"]?.ToObject<int?>() ?? 0,
                energyConsumption = building["energy_consumption"]?.ToObject<float?>() ?? 0f,
                heatingSystemBefore = building["begin_heating_system_type_1"]?.ToString(),
                heatingSystemAfter = building["end_heating_system_type_1"]?.ToString(),
                windowBefore = building["begin_window_type_1"]?.ToString(),
                windowAfter = building["end_window_type_1"]?.ToString(),
                wallBefore = building["begin_wall_type_1"]?.ToString(),
                wallAfter = building["end_wall_type_1"]?.ToString(),
                roofBefore = building["begin_roof_type_1"]?.ToString(),
                roofAfter = building["end_roof_type_1"]?.ToString(),
                ceilingBefore = building["begin_ceiling_type_1"]?.ToString(),
                ceilingAfter = building["end_ceiling_type_1"]?.ToString(),
                rawJson = building
            };
            
            // Extract CO2, Energy Demand Specific, and Color from energy_result (use defaults if null)
            JObject energyResult = building["energy_result"] as JObject;
            
            if (energyResult != null)
            {
                var beginData = energyResult["begin"]?["result"];
                var endData = energyResult["end"]?["result"];
                
                if (beginData != null && endData != null)
                {
                    // CO2 emissions (convert from kg to tonnes, use 0 if null)
                    float? co2BeforeKg = beginData["co2_from_energy_demand"]?["value"]?.ToObject<float?>();
                    float? co2AfterKg = endData["co2_from_energy_demand"]?["value"]?.ToObject<float?>();
                    data.co2Before = (co2BeforeKg ?? 0f) / 1000f;
                    data.co2After = (co2AfterKg ?? 0f) / 1000f;
                    
                    // Energy Demand Specific (use 0 if null)
                    data.energyDemandBefore = beginData["energy_demand_specific"]?["value"]?.ToObject<int?>() ?? 0;
                    data.energyDemandAfter = endData["energy_demand_specific"]?["value"]?.ToObject<int?>() ?? 0;
                }
                
                // ✅ Extract EXACT color from backend - NO conversion, NO defaults
                string hexColor = energyResult["end"]?["color"]?["energy_demand_specific_color"]?.ToString();
                if (!string.IsNullOrEmpty(hexColor) && ColorUtility.TryParseHtmlString(hexColor, out Color parsedColor))
                {
                    // ✅ Store color under BOTH modified_gml_id AND gml_id (like UE5 dual storage)
                    buildingColorCache[gmlId] = parsedColor;
                    if (!string.IsNullOrEmpty(data.gmlIdBasic))
                    {
                        buildingColorCache[data.gmlIdBasic] = parsedColor;
                    }
                    
                    if (buildingDataCache.Count <= 10)
                    {
                        Debug.Log($"<color=green>🎨 Building '{gmlId}':</color>");
                        Debug.Log($"<color=yellow>   • Energy Demand: {data.energyDemandAfter} kWh/m²a</color>");
                        Debug.Log($"<color=yellow>   • EXACT API Hex Color: {hexColor}</color>");
                        Debug.Log($"<color=yellow>   • Dual cached: '{gmlId}' + '{data.gmlIdBasic}'</color>");
                    }
                }
                else
                {
                    // ✅ NO default color fallback - if API has no color, don't color the building
                    if (buildingDataCache.Count <= 10)
                    {
                        Debug.LogWarning($"<color=orange>⚠️ Building '{gmlId}': API provided no color</color>");
                    }
                }
            }
            else
            {
                // No energy_result, still cache the building with default color
                buildingColorCache[gmlId] = defaultColor;
            }
            
            // ✅ Store GML ID mapping (like UE5's GmlIdCache)
            if (!string.IsNullOrEmpty(data.gmlIdBasic))
            {
                gmlIdCache[gmlId] = data.gmlIdBasic;
            }
            
            return data;
        }
        catch (Exception e)
        {
            string tryGmlId = building["modified_gml_id"]?.ToString() ?? "unknown";
            Debug.LogWarning($"<color=orange>⚠️ Failed to parse building '{tryGmlId}': {e.Message}</color>");
            return null;
        }
    }
    
    // ❌ REMOVED: GetColorName() - no longer needed since we use exact API hex colors
    
    // ❌ DEPRECATED: Do NOT use custom color calculation
    // Colors come DIRECTLY from backend API, no conversion needed
    // public Color CalculateEnergyColor(float energyConsumption) { ... }
    
    /// <summary>
    /// Refresh building cache by re-loading data from API
    /// </summary>
    public void RefreshBuildingCache()
    {
        Debug.Log("<color=cyan>🔄 DEPRECATED: RefreshBuildingCache - Use ClearPersistentCache() instead</color>");
        Debug.Log("<color=yellow>💡 Smart caching now loads once and only updates edited buildings</color>");
        ClearPersistentCache();
    }
    
    /// <summary>
    /// Refresh a single building after edit - fetches from API and updates raw JSON cache
    /// </summary>
    public IEnumerator RefreshSingleBuilding(string gmlId, int maxRetries = 1)
    {
        Debug.Log($"<color=cyan>🔄 RefreshSingleBuilding: {gmlId}</color>");
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                float delay = 0.3f;
                Debug.Log($"<color=yellow>⏳ Retry {attempt} after {delay}s delay...</color>");
                yield return new WaitForSeconds(delay);
            }
            
            // Add timestamp for cache-busting
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string url = $"{apiBaseUrl}/geospatial/buildings-energy/?community_id={communityId}&modified_gml_id={UnityWebRequest.EscapeURL(gmlId)}&format=json&include_colors=true&energy_type=total&time_period=annual&classification=co2&color_scheme=co2_classes&_t={timestamp}";
            
            Debug.Log($"<color=cyan>📡 Fetching updated building from API...</color>");
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate, max-age=0");
                request.SetRequestHeader("Pragma", "no-cache");
                request.SetRequestHeader("Expires", "0");
                
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        JArray buildings = JArray.Parse(request.downloadHandler.text);
                        
                        if (buildings.Count > 0)
                        {
                            JObject updatedBuilding = buildings[0] as JObject;
                            
                            // Update in-memory cache
                            BuildingData newData = ParseSingleBuilding(updatedBuilding);
                            if (newData != null)
                            {
                                buildingDataCache[gmlId] = newData;
                                Color newColor = buildingColorCache.ContainsKey(gmlId) ? buildingColorCache[gmlId] : Color.clear;
                                
                                Debug.Log($"<color=green>✅ In-memory cache updated for {gmlId}</color>");
                                
                                // ✅ UPDATE RAW JSON CACHE FILE
                                UpdateBuildingInRawCache(gmlId, updatedBuilding);
                                
                                // Recolor building immediately
                                if (newColor != Color.clear)
                                {
                                    Debug.Log($"<color=green>🎨 Applying color: #{ColorUtility.ToHtmlStringRGB(newColor)}</color>");
                                    CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
                                    if (colorizer != null)
                                    {
                                        colorizer.RecolorSingleBuilding(gmlId, newColor);
                                    }
                                }
                                
                                yield break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"<color=red>❌ Parse error: {e.Message}</color>");
                    }
                }
                else
                {
                    Debug.LogError($"<color=red>❌ Request failed: {request.error}</color>");
                }
            }
        }
    }
    
    /// <summary>
    /// Fetch building attributes using field_type=basic API (for edit form)
    /// Uses gmlIdBasic (no underscore) as per Unreal Engine implementation
    /// </summary>
    public IEnumerator FetchBasicAttributes(string gmlIdBasic, System.Action<JObject> onSuccess, System.Action<string> onError)
    {
        Debug.Log($"<color=cyan>📋 FetchBasicAttributes: {gmlIdBasic}</color>");
        
        // Ensure authenticated before making request
        if (string.IsNullOrEmpty(accessToken))
        {
            Debug.Log("<color=yellow>⚠️ No access token - authenticating first...</color>");
            yield return AuthenticateAndLoadData();
        }
        
        // Construct URL with field_type=basic
        string url = $"{apiBaseUrl}/geospatial/buildings-energy/{UnityWebRequest.EscapeURL(gmlIdBasic)}/?community_id={communityId}&field_type=basic";
        
        Debug.Log($"<color=cyan>📡 Fetching basic attributes from: {url}</color>");
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.SetRequestHeader("Content-Type", "application/json");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    JObject attributesData = JObject.Parse(request.downloadHandler.text);
                    Debug.Log($"<color=green>✅ Basic attributes fetched successfully</color>");
                    Debug.Log($"<color=yellow>   API Response Structure:</color>");
                    Debug.Log($"<color=yellow>      - Top-level keys: {string.Join(", ", attributesData.Properties().Select(p => p.Name))}</color>");
                    
                    // Log nested structure if present
                    if (attributesData["general_info"] != null)
                    {
                        JObject generalInfo = attributesData["general_info"] as JObject;
                        Debug.Log($"<color=yellow>      - general_info keys: {string.Join(", ", generalInfo.Properties().Select(p => p.Name))}</color>");
                        
                        // Check for fields sub-object
                        if (generalInfo["fields"] != null)
                        {
                            JObject generalFields = generalInfo["fields"] as JObject;
                            Debug.Log($"<color=yellow>      - general_info.fields → gml_id: '{generalFields["gml_id"]}'</color>");
                            Debug.Log($"<color=yellow>      - general_info.fields → modified_gml_id: '{generalFields["modified_gml_id"]}'</color>");
                            Debug.Log($"<color=yellow>      - general_info.fields → construction_year_class: '{generalFields["construction_year_class"]}'</color>");
                        }
                    }
                    
                    onSuccess?.Invoke(attributesData);
                }
                catch (Exception e)
                {
                    string error = $"Parse error: {e.Message}";
                    Debug.LogError($"<color=red>❌ {error}</color>");
                    onError?.Invoke(error);
                }
            }
            else
            {
                string error = $"Request failed: {request.error} (Code: {request.responseCode})";
                Debug.LogError($"<color=red>❌ {error}</color>");
                onError?.Invoke(error);
            }
        }
    }
    
    /// <summary>
    /// Update a single building in the raw JSON cache file (efficient partial update)
    /// </summary>
    private void UpdateBuildingInRawCache(string gmlId, JObject updatedBuildingData)
    {
        try
        {
            string rawCachePath = cacheFilePath.Replace(".json", "_raw.json");
            
            if (!System.IO.File.Exists(rawCachePath))
            {
                Debug.LogWarning("<color=yellow>⚠️ Raw cache file not found - skipping update</color>");
                return;
            }
            
            // Load current raw JSON
            string currentJson = System.IO.File.ReadAllText(rawCachePath);
            JArray buildings = JArray.Parse(currentJson);
            
            // Find and replace the building
            bool found = false;
            for (int i = 0; i < buildings.Count; i++)
            {
                JObject building = buildings[i] as JObject;
                string buildingId = building["modified_gml_id"]?.ToString();
                
                if (buildingId == gmlId)
                {
                    buildings[i] = updatedBuildingData;
                    found = true;
                    Debug.Log($"<color=green>✅ Updated building {gmlId} in raw cache (index {i})</color>");
                    break;
                }
            }
            
            if (found)
            {
                // Save updated JSON back to disk
                string updatedJson = buildings.ToString();
                System.IO.File.WriteAllText(rawCachePath, updatedJson);
                
                // Update parsedJsonArray in memory
                parsedJsonArray = buildings;
                rawJsonCache = updatedJson;
                
                Debug.Log($"<color=green>💾 Raw cache file updated successfully</color>");
            }
            else
            {
                Debug.LogWarning($"<color=yellow>⚠️ Building {gmlId} not found in raw cache</color>");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>❌ Failed to update raw cache: {e.Message}</color>");
        }
    }
    
    /// <summary>
    /// Normalize GML ID for robust matching (removes whitespace, handles case)
    /// </summary>
    private string NormalizeGmlId(string gmlId)
    {
        if (string.IsNullOrEmpty(gmlId)) return "";
        return gmlId.Trim().Replace(" ", "").Replace("\t", "").Replace("\n", "");
    }
    
    // OLD BATCH COLORING METHOD - DEPRECATED
    // This has been replaced by CesiumFeatureColorizer which uses per-vertex coloring
    // public void ApplyColorsToBuildings()
    // {
    //     if (buildingsTileset == null)
    //     {
    //         Debug.LogWarning("Cannot apply colors: tileset not found");
    //         return;
    //     }
    //     
    //     Debug.Log($"Applying colors to buildings. Cache has {buildingColorCache.Count} entries");
    //     
    //     // Cesium for Unity doesn't support style property (unlike Cesium JS)
    //     // Use material-based coloring instead
    //     StartCoroutine(ApplyColorsWithDelay());
    // }
    
    // OLD: ApplyColorsWithDelay coroutine - deprecated
    /*
    IEnumerator ApplyColorsWithDelay()
    {
        // Wait for tileset to fully load
        yield return new WaitForSeconds(2f);
        
        Debug.Log("Applying colors after delay...");
        ApplyColorsToMeshes();
        
        // Keep reapplying as new tiles load
        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForSeconds(3f);
            Debug.Log($"Re-applying colors (pass {i+2})...");
            ApplyColorsToMeshes();
        }
    }
    */
    
    // OLD: BuildCesiumStyleJson - deprecated (Cesium for Unity doesn't support runtime styling like Cesium JS)
    /*
    string BuildCesiumStyleJson()
    {
        // Build a Cesium 3D Tiles style expression
        // This uses conditional expressions to color buildings based on their gml_id
        
        StringBuilder colorExpression = new StringBuilder();
        colorExpression.Append("[");
        
        int count = 0;
        foreach (var kvp in buildingColorCache)
        {
            string gmlId = kvp.Key;
            Color color = kvp.Value;
            
            if (count > 0)
            {
                colorExpression.Append(", ");
            }
            
            // Try different property names for gml_id
            string condition = $"${{feature['gml_id']}} === '{gmlId}' || ${{feature['gmlId']}} === '{gmlId}' || ${{feature['id']}} === '{gmlId}'";
            colorExpression.Append($"{condition}, 'rgba({(int)(color.r * 255)}, {(int)(color.g * 255)}, {(int)(color.b * 255)}, {color.a})'");
            count++;
        }
        
        // Default color if no match
        Color defaultCol = defaultColor;
        colorExpression.Append($", 'rgba({(int)(defaultCol.r * 255)}, {(int)(defaultCol.g * 255)}, {(int)(defaultCol.b * 255)}, {defaultCol.a})'");
        colorExpression.Append("]");
        
        // Create the full style JSON
        string styleJson = $@"
        {{
            ""color"": {{
                ""conditions"": {colorExpression}
            }}
        }}";
        
        return styleJson;
    }
    
    // OLD BATCH COLORING METHOD - DEPRECATED
    // This colored entire batched meshes (20-50 buildings) instead of individual buildings
    // Replaced by CesiumFeatureColorizer which writes colors per-vertex for individual building coloring
    /*
    public void ApplyColorsToMeshes()
    {
        if (buildingsTileset == null)
        {
            Debug.LogWarning("⚠️ Buildings tileset not found - cannot apply colors");
            return;
        }
        
        Debug.Log($"🎨 === Applying Colors to Meshes ===");
        Debug.Log($"Building color cache has {buildingColorCache.Count} entries");
        
        // Find all mesh renderers under the tileset
        MeshRenderer[] renderers = buildingsTileset.GetComponentsInChildren<MeshRenderer>(true); // Include inactive
        
        // Only log details occasionally to avoid spam
        bool verboseLogging = coloringTimer < 0.1f; // Only first run
        
        if (verboseLogging)
        {
            Debug.Log($"Found {renderers.Length} mesh renderers in tileset");
        }
        
        int coloredCount = 0;
        int noFeaturesCount = 0;
        int noGmlIdCount = 0;
        int notInCacheCount = 0;
        
        foreach (MeshRenderer renderer in renderers)
        {
            // Try to get the gml_id from the CesiumPrimitiveFeatures component
            CesiumPrimitiveFeatures features = renderer.GetComponent<CesiumPrimitiveFeatures>();
            if (features != null && features.featureIdSets.Length > 0)
            {
                // Get feature ID for the first vertex (representative)
                Int64 featureId = features.GetFeatureIdFromTriangle(0, 0);
                
                if (featureId >= 0)
                {
                    // Try to find gml_id from metadata
                    string gmlId = ExtractGmlIdFromRenderer(renderer);
                    
                    if (!string.IsNullOrEmpty(gmlId))
                    {
                        // Try to find matching color with robust ID matching
                        Color? color = FindColorForBuilding(gmlId, featureId);
                        
                        if (color.HasValue)
                        {
                            ApplyColorToRenderer(renderer, color.Value);
                            coloredCount++;
                            if (verboseLogging && coloredCount <= 10)
                            {
                                Debug.Log($"✓ Applied color {ColorUtility.ToHtmlStringRGB(color.Value)} to building {gmlId}");
                            }
                        }
                        else
                        {
                            // Not in cache - skip coloring, leave Cesium default
                            notInCacheCount++;
                            if (verboseLogging && notInCacheCount <= 3)
                            {
                                Debug.LogWarning($"Building '{gmlId}' (feature {featureId}) not found in cache");
                            }
                        }
                    }
                    else
                    {
                        // No gml:id found - skip coloring
                        noGmlIdCount++;
                    }
                }
            }
            else
            {
                // No features - skip coloring, leave Cesium default
                noFeaturesCount++;
            }
        }
        
        if (verboseLogging)
        {
            Debug.Log($"=== Coloring Complete ===");
            Debug.Log($"✅ Colored {coloredCount} buildings");
            Debug.Log($"⚠️ No features: {noFeaturesCount}, No gml:id: {noGmlIdCount}, Not in cache: {notInCacheCount}");
            Debug.Log($"📊 Total renderers processed: {renderers.Length}");
            Debug.Log($"📦 Cache has {buildingColorCache.Count} colors available");
        }
        else if (coloredCount > 0)
        {
            Debug.Log($"✅ Applied colors to {coloredCount} newly loaded buildings");
        }
    }
    */  // End of deprecated ApplyColorsToMeshes()
    
    public Color? FindColorForBuilding(string extractedId, Int64 featureId)
    {
        if (string.IsNullOrEmpty(extractedId))
            return null;
        
        // Normalize input ID
        string normalizedInput = NormalizeGmlId(extractedId);
        
        // Try multiple matching strategies
        
        // 1. Direct match with extracted ID
        if (buildingColorCache.ContainsKey(extractedId))
        {
            return buildingColorCache[extractedId];
        }
        
        // 2. Normalized exact match
        foreach (var cacheKey in buildingColorCache.Keys)
        {
            if (NormalizeGmlId(cacheKey) == normalizedInput)
            {
                return buildingColorCache[cacheKey];
            }
        }
        
        // 3. Try variations of the ID (case insensitive, with/without prefixes)
        foreach (var cacheKey in buildingColorCache.Keys)
        {
            string normalizedCache = NormalizeGmlId(cacheKey);
            
            // Case-insensitive match
            if (string.Equals(normalizedCache, normalizedInput, StringComparison.OrdinalIgnoreCase))
            {
                return buildingColorCache[cacheKey];
            }
            
            // Match if cache key ends with extracted ID (e.g., modified_gml_id contains gml:id)
            if (normalizedCache.EndsWith(normalizedInput, StringComparison.OrdinalIgnoreCase))
            {
                return buildingColorCache[cacheKey];
            }
            
            // Match if extracted ID ends with cache key
            if (normalizedInput.EndsWith(normalizedCache, StringComparison.OrdinalIgnoreCase))
            {
                return buildingColorCache[cacheKey];
            }
        }
        
        // 4. Try matching without common prefixes (DEBW_, DE_, etc)
        string cleanId = normalizedInput.Replace("DEBW_", "").Replace("debw_", "").Replace("DE_", "").Replace("de_", "");
        foreach (var cacheKey in buildingColorCache.Keys)
        {
            string cleanCacheKey = NormalizeGmlId(cacheKey).Replace("DEBW_", "").Replace("debw_", "").Replace("DE_", "").Replace("de_", "");
            if (string.Equals(cleanId, cleanCacheKey, StringComparison.OrdinalIgnoreCase))
            {
                return buildingColorCache[cacheKey];
            }
        }
        
        // 5. Partial match - if IDs contain each other (last resort)
        foreach (var cacheKey in buildingColorCache.Keys)
        {
            string normalizedCache = NormalizeGmlId(cacheKey);
            if (normalizedInput.Length > 8 && normalizedCache.Length > 8)
            {
                if (normalizedInput.Contains(normalizedCache) || normalizedCache.Contains(normalizedInput))
                {
                    Debug.LogWarning($"<color=orange>⚠️ Partial match: '{extractedId}' matched with '{cacheKey}'</color>");
                    return buildingColorCache[cacheKey];
                }
            }
        }
        
        return null; // No match found
    }
    
    // OLD HELPER METHOD - DEPRECATED (used by batch coloring)
    /*
    string ExtractGmlIdFromRenderer(MeshRenderer renderer)
    {
        CesiumModelMetadata metadata = renderer.GetComponentInParent<CesiumModelMetadata>();
        if (metadata != null && metadata.propertyTables != null && metadata.propertyTables.Length > 0)
        {
            CesiumPrimitiveFeatures features = renderer.GetComponent<CesiumPrimitiveFeatures>();
            if (features != null && features.featureIdSets.Length > 0)
            {
                Int64 featureId = features.GetFeatureIdFromTriangle(0, 0);
                
                foreach (CesiumPropertyTable propertyTable in metadata.propertyTables)
                {
                    if (featureId < propertyTable.count)
                    {
                        var values = propertyTable.GetMetadataValuesForFeature(featureId);
                        
                        // Try common property names - note: Cesium often uses "gml:id" with colon
                        string[] keys = { "gml:id", "gml_id", "gmlId", "id", "building_id", "buildingId" };
                        foreach (string key in keys)
                        {
                            if (values.ContainsKey(key))
                            {
                                string value = values[key].GetString("");
                                if (!string.IsNullOrEmpty(value))
                                {
                                    return value;
                                }
                            }
                        }
                    }
                }
            }
        }
        return null;
    }
    
    void ApplyColorToRenderer(MeshRenderer renderer, Color color)
    {
        // CRITICAL: Create NEW material instances to avoid modifying shared assets
        // Cesium uses shared materials, so we MUST instantiate them
        Material[] materials = renderer.materials; // This creates copies
        bool colorApplied = false;
        
        for (int i = 0; i < materials.Length; i++)
        {
            Material mat = materials[i];
            
            // Set alpha to fully opaque
            Color opaqueColor = new Color(color.r, color.g, color.b, 1.0f);
            
            // CRITICAL: For Cesium shaders, the color must MULTIPLY with texture
            // Set _baseColorFactor (this is the standard glTF property)
            if (mat.HasProperty("_baseColorFactor"))
            {
                mat.SetColor("_baseColorFactor", opaqueColor);
                colorApplied = true;
            }
            
            // Also try standard Unity properties as fallback
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", opaqueColor);
                colorApplied = true;
            }
            if (mat.HasProperty("_Color"))
            {
                mat.color = opaqueColor;
                colorApplied = true;
            }
            
            // CRITICAL: Remove or disable texture to let color show through
            // Cesium may have textures that override the color
            if (mat.HasProperty("_baseColorTexture"))
            {
                mat.SetTexture("_baseColorTexture", null);
            }
            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", null);
            }
            
            // Enable shader keywords that might be needed
            mat.EnableKeyword("_BASECOLORMAP_OFF");
            mat.DisableKeyword("_BASECOLORMAP_ON");
            
            // Ensure opaque rendering
            mat.renderQueue = 2000; // Geometry queue
        }
        
        if (!colorApplied)
        {
            Debug.LogWarning($"Could not apply color - no compatible properties found in materials");
        }
        
        // Re-assign materials array to apply changes
        renderer.materials = materials;
    }
    */  // End of deprecated helper methods
    
    private string currentDisplayedBuildingId = "";
    
    public void DisplayBuildingData(string gmlId)
    {
        currentDisplayedBuildingId = gmlId;
        
        if (buildingDataCache.ContainsKey(gmlId))
        {
            BuildingData data = buildingDataCache[gmlId];
            if (buildingInfoPanel != null)
            {
                buildingInfoPanel.DisplayData(data);
            }
        }
        else
        {
            // Data not cached, fetch from API
            StartCoroutine(FetchBuildingData(gmlId));
        }
    }
    
    public IEnumerator FetchBuildingData(string gmlId)
    {
        // Check if data is already in cache (loaded from bulk API)
        if (buildingDataCache.ContainsKey(gmlId))
        {
            Debug.Log($"✅ Building {gmlId} found in cache");
            yield break; // Data already available
        }
        
        Debug.Log($"⚠️ Building {gmlId} not found in cache. Cache has {buildingDataCache.Count} entries.");
        
        // List available cache keys to help debug
        if (buildingDataCache.Count > 0)
        {
            Debug.Log("Sample cache keys:");
            int count = 0;
            foreach (var key in buildingDataCache.Keys)
            {
                Debug.Log($"  - {key}");
                if (++count >= 5) break; // Only show first 5
            }
        }
        
        // Ensure we have a valid access token
        if (string.IsNullOrEmpty(accessToken))
        {
            Debug.LogWarning("⚠️ Access token is empty! Authenticating...");
            yield return Authenticate();
            
            if (string.IsNullOrEmpty(accessToken))
            {
                Debug.LogError("❌ Authentication failed! Cannot fetch building data.");
                yield break;
            }
        }
        
        // Try fetching - will retry once if 401 occurs
        yield return FetchBuildingDataWithRetry(gmlId, retryOnAuthFailure: true);
    }
    
    /// <summary>
    /// Internal method to fetch building data with optional retry on 401
    /// </summary>
    private IEnumerator FetchBuildingDataWithRetry(string gmlId, bool retryOnAuthFailure)
    {
        Debug.Log($"🔄 Attempting to fetch individual building {gmlId} from API...");
        
        string url = $"{apiBaseUrl}/geospatial/buildings-energy/?community_id={communityId}&modified_gml_id={gmlId}&format=json&include_colors=true&energy_type=total&time_period=annual&classification=co2&color_scheme=co2_classes";
        Debug.Log($"Individual fetch URL: {url}");
        
        if (!string.IsNullOrEmpty(accessToken))
        {
            Debug.Log($"Using access token: {accessToken.Substring(0, Math.Min(20, accessToken.Length))}...");
        }
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.timeout = 20; // Increased timeout from 10 to 20 seconds for individual fetches
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"✅ Individual building fetch successful: {request.downloadHandler.text.Length} characters");
                
                try
                {
                    // API returns array directly (all buildings, not filtered!)
                    JArray buildings = JArray.Parse(request.downloadHandler.text);
                    Debug.Log($"API returned {buildings.Count} buildings total");
                    
                    // Find the specific building with matching modified_gml_id
                    JObject matchingBuilding = null;
                    foreach (JObject building in buildings)
                    {
                        string buildingId = building["modified_gml_id"]?.ToString();
                        if (buildingId == gmlId)
                        {
                            matchingBuilding = building;
                            Debug.Log($"✅ Found matching building: {buildingId}");
                            break;
                        }
                    }
                    
                    if (matchingBuilding != null)
                    {
                        BuildingData data = ParseSingleBuilding(matchingBuilding);
                        
                        if (data != null)
                        {
                            buildingDataCache[gmlId] = data;
                            
                            // Color is already added to buildingColorCache by ParseSingleBuilding
                            if (buildingColorCache.TryGetValue(gmlId, out Color buildingColor))
                            {
                                Debug.Log($"✅ Added {gmlId} to cache from individual fetch (Color: #{ColorUtility.ToHtmlStringRGB(buildingColor)})");
                                
                                // Update the building visual
                                CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
                                if (colorizer != null)
                                {
                                    colorizer.RecolorSingleBuilding(gmlId, buildingColor);
                                }
                            }
                            else
                            {
                                Debug.Log($"✅ Added {gmlId} to cache from individual fetch (no color data)");
                            }
                        }
                    }
                    else
                    {
                        Debug.LogError($"❌ No building with modified_gml_id='{gmlId}' found in {buildings.Count} buildings returned");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"❌ Failed to parse individual building response: {e.Message}");
                }
            }
            else
            {
                Debug.LogError($"❌ Individual fetch failed: {request.error}");
                Debug.LogError($"Response Code: {request.responseCode}");
                
                // Handle specific error types
                if (request.responseCode == 0 && request.error.Contains("timeout"))
                {
                    Debug.LogWarning($"<color=orange>⏰ TIMEOUT ERROR - Request took longer than 20 seconds</color>");
                    Debug.LogWarning($"<color=yellow>💡 Possible causes:</color>");
                    Debug.LogWarning($"<color=yellow>   • Internet connection is slow</color>");
                    Debug.LogWarning($"<color=yellow>   • API server is overloaded</color>");
                    Debug.LogWarning($"<color=yellow>   • Large dataset causing delays</color>");
                    Debug.LogWarning($"<color=cyan>🔧 Solutions:</color>");
                    Debug.LogWarning($"<color=cyan>   • Wait and try clicking the building again</color>");
                    Debug.LogWarning($"<color=cyan>   • Use 'Hard Refresh Cache' to download all at once (more efficient)</color>");
                }
                // Special handling for 401 Unauthorized
                else if (request.responseCode == 401)
                {
                    Debug.LogWarning($"<color=orange>🔐 Token expired or invalid (401)</color>");
                    
                    // Try to re-authenticate ONCE and retry
                    if (retryOnAuthFailure)
                    {
                        Debug.LogWarning($"<color=cyan>♻️ Re-authenticating and retrying...</color>");
                        
                        // Force clear the old token
                        accessToken = "";
                        
                        // Get fresh token
                        yield return Authenticate();
                        
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            Debug.Log($"<color=green>✅ Re-authentication successful! Retrying fetch...</color>");
                            
                            // Retry ONCE with new token (retryOnAuthFailure = false to prevent infinite loop)
                            yield return FetchBuildingDataWithRetry(gmlId, retryOnAuthFailure: false);
                        }
                        else
                        {
                            Debug.LogError($"<color=red>❌ Re-authentication failed!</color>");
                        }
                    }
                    else
                    {
                        // Already retried once, give up
                        Debug.LogError($"<color=red>🔐 AUTHENTICATION ERROR (401 Unauthorized) - Retry failed</color>");
                        Debug.LogError($"<color=yellow>Token is invalid even after re-authentication</color>");
                        Debug.LogError($"<color=yellow>Possible issues:</color>");
                        Debug.LogError($"<color=yellow>  1. API credentials are wrong (username/password)</color>");
                        Debug.LogError($"<color=yellow>  2. API server is rejecting authentication</color>");
                        Debug.LogError($"<color=yellow>  3. Community ID or API endpoint is incorrect</color>");
                        Debug.LogError($"<color=yellow>Solution: Check BuildingEnergyManager inspector settings</color>");
                        Debug.LogError($"Response body: {request.downloadHandler.text}");
                    }
                }
            }
        }
    }
    
    public void OpenAttributesForm(string gmlId)
    {
        // TODO: Implement BuildingAttributesForm in future phase
        Debug.LogWarning("BuildingAttributesForm not yet implemented");
        
        // Placeholder for future implementation:
        // if (buildingAttributesForm != null && buildingDataCache.ContainsKey(gmlId))
        // {
        //     buildingAttributesForm.OpenForm(gmlId, buildingDataCache[gmlId], accessToken);
        // }
    }
    
    /// <summary>
    /// Diagnose why initial download might be failing - shows cache status and connectivity
    /// </summary>
    [ContextMenu("Diagnose Download Issues")]
    public void DiagnoseDownloadIssues()
    {
        Debug.Log("<color=cyan>=== DOWNLOAD DIAGNOSTICS ===</color>");
        
        // Check basic configuration
        Debug.Log($"<color=yellow>📋 Configuration:</color>");
        Debug.Log($"<color=yellow>   API Base URL: {apiBaseUrl}</color>");
        Debug.Log($"<color=yellow>   Community ID: {communityId}</color>");
        Debug.Log($"<color=yellow>   Access Token Length: {(string.IsNullOrEmpty(accessToken) ? 0 : accessToken.Length)}</color>");
        Debug.Log($"<color=yellow>   Token Preview: {(string.IsNullOrEmpty(accessToken) ? "NONE" : accessToken.Substring(0, Math.Min(30, accessToken.Length)) + "...")}</color>");
        
        // Check cache status
        Debug.Log($"<color=yellow>📊 Current Cache Status:</color>");
        Debug.Log($"<color=yellow>   Buildings in Cache: {buildingDataCache.Count}</color>");
        Debug.Log($"<color=yellow>   Colors in Cache: {buildingColorCache.Count}</color>");
        Debug.Log($"<color=yellow>   Cache File Path: {cacheFilePath}</color>");
        Debug.Log($"<color=yellow>   Cache File Exists: {System.IO.File.Exists(cacheFilePath)}</color>");
        Debug.Log($"<color=yellow>   Last Update: {lastCacheUpdate}</color>");
        
        // Check components
        Debug.Log($"<color=yellow>🧩 Component Status:</color>");
        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        Debug.Log($"<color=yellow>   CesiumFeatureColorizer Found: {colorizer != null}</color>");
        Debug.Log($"<color=yellow>   BuildingsTileset Found: {buildingsTileset != null}</color>");
        
        // Show recommended actions
        Debug.Log($"<color=cyan>💡 If buildings are white (not colored):</color>");
        Debug.Log($"<color=cyan>   1. Check console for download errors during startup</color>");
        Debug.Log($"<color=cyan>   2. Try 'Hard Refresh Cache (Clear & Reload)'</color>");
        Debug.Log($"<color=cyan>   3. Check internet connection</color>");
        Debug.Log($"<color=cyan>   4. Verify API credentials are correct</color>");
        
        // Test connectivity
        Debug.Log($"<color=cyan>🌐 Testing API connectivity...</color>");
        StartCoroutine(TestAPIConnectivity());
    }
    
    private IEnumerator TestAPIConnectivity()
    {
        string testUrl = $"{apiBaseUrl}/api/token/";
        Debug.Log($"<color=yellow>Testing: {testUrl}</color>");
        
        using (UnityWebRequest request = UnityWebRequest.Get(testUrl))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success || request.responseCode == 405) // 405 = Method Not Allowed is OK for GET on POST endpoint
            {
                Debug.Log($"<color=green>✅ API server reachable (Status: {request.responseCode})</color>");
            }
            else
            {
                Debug.LogError($"<color=red>❌ API server unreachable: {request.error} (Status: {request.responseCode})</color>");
            }
        }
    }
    
    // Debug and testing methods
    [ContextMenu("Test Color System")]
    public void TestColorSystem()
    {
        Debug.Log("=== Testing Color System ===");
        Debug.Log($"Buildings in cache: {buildingDataCache.Count}");
        Debug.Log($"Colors in cache: {buildingColorCache.Count}");
        Debug.Log($"Tileset found: {buildingsTileset != null}");
        
        if (buildingsTileset != null)
        {
            Debug.Log($"Tileset name: {buildingsTileset.name}");
            Debug.Log($"Tileset children: {buildingsTileset.transform.childCount}");
        }
        
        // OLD: Try to apply colors using both methods - Now use CesiumFeatureColorizer
        // ApplyColorsToBuildings(); // Deprecated
        // ApplyColorsToMeshes(); // Deprecated
        
        Debug.Log("✓ Use CesiumFeatureColorizer component for per-building coloring");
    }
    
    [ContextMenu("[DEPRECATED] Force Reload Data")]
    public void ForceReloadData()
    {
        Debug.LogWarning("<color=orange>⚠️ ForceReloadData is DEPRECATED!</color>");
        Debug.LogWarning("<color=yellow>💡 Smart caching now loads once and caches persistently</color>");
        Debug.LogWarning("<color=yellow>   Use 'Clear Persistent Cache' then restart if needed</color>");
    }
    
    [ContextMenu("Apply Test Colors")]
    public void ApplyTestColors()
    {
        // Add some test data for debugging
        buildingColorCache["test_building_1"] = Color.red;
        buildingColorCache["test_building_2"] = Color.green;
        buildingColorCache["test_building_3"] = Color.blue;
        
        Debug.Log("Added test colors. Use CesiumFeatureColorizer to apply per-building colors.");
        // OLD: ApplyColorsToBuildings(); - Deprecated
        // OLD: ApplyColorsToMeshes(); - Deprecated
    }
    
    [ContextMenu("Validate Cache Integrity")]
    public void ValidateCacheIntegrity()
    {
        Debug.Log("<color=cyan>=== CACHE INTEGRITY VALIDATION ===</color>");
        Debug.Log($"<color=yellow>Building Data Cache: {buildingDataCache.Count} entries</color>");
        Debug.Log($"<color=yellow>Building Color Cache: {buildingColorCache.Count} entries</color>");
        
        // Check for mismatches
        int dataWithoutColor = 0;
        int colorWithoutData = 0;
        
        foreach (var gmlId in buildingDataCache.Keys)
        {
            if (!buildingColorCache.ContainsKey(gmlId))
            {
                dataWithoutColor++;
                if (dataWithoutColor <= 3)
                {
                    Debug.LogWarning($"<color=orange>⚠️ Building {gmlId} has data but no color</color>");
                }
            }
        }
        
        foreach (var gmlId in buildingColorCache.Keys)
        {
            if (!buildingDataCache.ContainsKey(gmlId))
            {
                colorWithoutData++;
                if (colorWithoutData <= 3)
                {
                    Debug.LogWarning($"<color=orange>⚠️ Building {gmlId} has color but no data</color>");
                }
            }
        }
        
        if (dataWithoutColor == 0 && colorWithoutData == 0)
        {
            Debug.Log("<color=green>✅ Cache integrity perfect - all buildings have both data and color</color>");
        }
        else
        {
            Debug.LogWarning($"<color=orange>⚠️ Cache mismatches: {dataWithoutColor} missing colors, {colorWithoutData} missing data</color>");
        }
        
        // Check for duplicate colors (expected behavior)
        var colorGroups = buildingColorCache.GroupBy(x => ColorUtility.ToHtmlStringRGB(x.Value))
                                            .Where(g => g.Count() > 1)
                                            .OrderByDescending(g => g.Count())
                                            .Take(5);
        
        Debug.Log($"<color=cyan>📊 Top 5 most common colors (shared by multiple buildings):</color>");
        foreach (var group in colorGroups)
        {
            Debug.Log($"<color=yellow>  #{group.Key}: {group.Count()} buildings (NORMAL - same energy rating)</color>");
        }
    }
    
    /// <summary>
    /// Diagnose API/Tileset mismatch - shows which buildings are in cache but not in tileset
    /// Triggered automatically if mismatch > 50%, but can be called manually
    /// </summary>
    [ContextMenu("Diagnose API vs Tileset Mismatch")]
    public void DiagnoseTilesetMismatch()
    {
        if (buildingColorCache.Count == 0)
        {
            Debug.LogWarning("<color=orange>⚠️ No buildings in cache - nothing to diagnose</color>");
            return;
        }
        
        Debug.Log("<color=cyan>=== TILESET MISMATCH DIAGNOSIS ===</color>");
        Debug.Log($"<color=yellow>Buildings in API cache: {buildingColorCache.Count}</color>");
        
        // Get buildings from tileset via feature colorizer
        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        if (colorizer == null)
        {
            Debug.LogError("<color=red>❌ CesiumFeatureColorizer not found in scene</color>");
            return;
        }
        
        HashSet<string> tilesetBuildingIds = colorizer.GetUniqueTilesetBuildingIds();
        Debug.Log($"<color=yellow>Buildings in Tileset: {tilesetBuildingIds.Count}</color>");
        
        // Find missing buildings
        var missingBuildings = buildingColorCache.Keys.Where(id => !tilesetBuildingIds.Contains(id)).ToList();
        var extraBuildings = tilesetBuildingIds.Where(id => !buildingColorCache.ContainsKey(id)).ToList();
        
        if (missingBuildings.Count > 0)
        {
            float mismatchPercent = (missingBuildings.Count * 100f) / buildingColorCache.Count;
            Debug.LogError($"<color=red>🚨 {missingBuildings.Count} buildings ({mismatchPercent:F1}%) in cache are NOT in tileset!</color>");
            
            // Show first 10 examples
            Debug.Log($"<color=orange>📋 First 10 missing buildings (examples):</color>");
            for (int i = 0; i < Mathf.Min(10, missingBuildings.Count); i++)
            {
                Debug.Log($"<color=orange>   • {missingBuildings[i]}</color>");
            }
            
            if (mismatchPercent > 50f)
            {
                Debug.LogError($"<color=red>🚨 CRITICAL: {mismatchPercent:F0}% mismatch! Tileset has likely been UPDATED</color>");
                Debug.LogError($"<color=yellow>🔧 SOLUTION: Hard Refresh Cache (Clear & Reload)</color>");
                Debug.LogError($"<color=yellow>   Right-click 'BuildingEnergyManager' → 'Hard Refresh Cache (Clear & Reload)'</color>");
            }
        }
        
        if (extraBuildings.Count > 0)
        {
            Debug.LogWarning($"<color=orange>⚠️ {extraBuildings.Count} buildings in tileset have NO API energy data</color>");
        }
        
        if (missingBuildings.Count == 0 && extraBuildings.Count == 0)
        {
            Debug.Log($"<color=green>✅ PERFECT MATCH: All API buildings are in tileset, and vice versa!</color>");
        }
    }
    
    void OnDestroy()
    {
        // Stop all coroutines to prevent GC handle issues on domain reload
        StopAllCoroutines();
        
        // Additional cleanup for script domain reload
        isAuthenticating = false;
        isInitialized = false;
    }
    
    [ContextMenu("Show Building Count")]
    public void ShowBuildingCount()
    {
        Debug.Log($"<color=cyan>=== BUILDING COUNT SUMMARY ===</color>");
        Debug.Log($"<color=green>📊 Total Buildings Loaded: {totalBuildingsLoaded}</color>");
        Debug.Log($"<color=green>📦 Buildings in Data Cache: {buildingDataCache.Count}</color>");
        Debug.Log($"<color=yellow>🎨 Buildings with Color: {buildingsWithColor}</color>");
        Debug.Log($"<color=orange>⚪ Buildings without Color: {buildingsWithoutColor}</color>");
        Debug.Log($"<color=cyan>📅 Last Updated: {lastCacheUpdate}</color>");
        
        // Also show tileset comparison if available
        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        if (colorizer != null)
        {
            int tilesetCount = colorizer.GetTilesetBuildingCount();
            if (tilesetCount > 0)
            {
                Debug.Log($"<color=magenta>🏢 Tileset Buildings: {tilesetCount}</color>");
                float matchPercent = (Mathf.Min(totalBuildingsLoaded, tilesetCount) * 100f) / Mathf.Max(totalBuildingsLoaded, tilesetCount, 1);
                string matchColor = matchPercent > 90f ? "green" : matchPercent > 70f ? "orange" : "red";
                Debug.Log($"<color={matchColor}>📊 Match Rate: {matchPercent:F1}%</color>");
                
                if (matchPercent < 90f)
                {
                    Debug.LogWarning($"<color=yellow>💡 TIP: For detailed comparison, use Ctrl+Shift+C or right-click CesiumFeatureColorizer → 'Count Buildings (Tileset vs API)'</color>");
                }
            }
            else
            {
                Debug.LogWarning($"<color=yellow>⚠️ Tileset count is 0. Run the full comparison (Ctrl+Shift+C) to scan all tiles.</color>");
            }
        }
        
        Debug.Log($"<color=cyan>================================</color>");
    }
    
    /// <summary>
    /// Get the current building count
    /// </summary>
    /// <returns>Dictionary containing building count statistics</returns>
    public Dictionary<string, int> GetBuildingStatistics()
    {
        return new Dictionary<string, int>
        {
            { "total", totalBuildingsLoaded },
            { "withColor", buildingsWithColor },
            { "withoutColor", buildingsWithoutColor },
            { "inCache", buildingDataCache.Count }
        };
    }

    [ContextMenu("DIAGNOSTIC: Find White Buildings")]
    public void ContextMenu_FindWhiteBuildings()
    {
        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        if (colorizer == null)
        {
            Debug.LogError("CesiumFeatureColorizer not found in scene!");
            return;
        }

        var unmatchedBuildings = colorizer.DiagnosticFindUnmatchedBuildings(30);
        Debug.Log($"\n<color=red>{'='*50} WHITE BUILDINGS DIAGNOSTIC {'='*50}</color>");
        Debug.Log($"<color=yellow>Found {unmatchedBuildings.Count} unmatched buildings in tileset</color>");
        Debug.Log($"<color=cyan>Total API cache entries: {buildingColorCache.Count}</color>\n");

        if (unmatchedBuildings.Count > 0)
        {
            Debug.Log("<color=orange>First 30 unmatched buildings and best cache matches:</color>");
            for (int i = 0; i < unmatchedBuildings.Count && i < 30; i++)
            {
                var (tileId, match, reason) = unmatchedBuildings[i];
                if (match != "NO_CACHE_MATCH")
                    Debug.Log($"  {i+1}. Tile: <color=yellow>{tileId}</color> → Cache: <color=green>{match}</color> ({reason})");
                else
                    Debug.Log($"  {i+1}. Tile: <color=yellow>{tileId}</color> → <color=red>NO MATCH FOUND</color>");
            }
        }
        Debug.Log($"<color=red>{'='*80}</color>\n");
    }

    [ContextMenu("EMERGENCY: Force Color All White Buildings")]
    public void ContextMenu_ForceColorWhiteBuildings()
    {
        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        if (colorizer == null)
        {
            Debug.LogError("CesiumFeatureColorizer not found in scene!");
            return;
        }

        int recolored = colorizer.ForceColorAllUnmatchedBuildings();
        Debug.Log($"<color=green>✅ Force-colored {recolored} previously white buildings using aggressive matching!</color>");
        Debug.Log($"<color=cyan>💡 Check the main console log for detailed matching diagnostics</color>");
    }
}

[System.Serializable]
public class BuildingData
{
    public string gmlId;              // modified_gml_id (with underscore) - matches tileset
    public string gmlIdBasic;         // gml_id (no underscore) - for field_type=basic API
    public string databaseId;         // id field - for PUT requests
    public string constructionYear;
    public int numberOfStorey;
    public float energyConsumption;
    
    // CO2 Emissions in tonnes CO2/year (converted from kg)
    public float co2Before;
    public float co2After;
    
    // Energy Demand Specific in kWh/m²a
    public int energyDemandBefore;
    public int energyDemandAfter;
    
    public string heatingSystemBefore;
    public string heatingSystemAfter;
    public string windowBefore;
    public string windowAfter;
    public string wallBefore;
    public string wallAfter;
    public string roofBefore;
    public string roofAfter;
    public string ceilingBefore;
    public string ceilingAfter;
    public JObject rawJson;
}

// ========================================
// CACHE SERIALIZATION CLASSES
// ========================================

/// <summary>
/// Persistent cache container for disk storage
/// </summary>
[System.Serializable]
public class CacheContainer
{
    public string communityId;
    public string lastUpdate;
    public System.Collections.Generic.List<BuildingCacheEntry> buildings;
}

/// <summary>
/// Individual building cache entry
/// </summary>
[System.Serializable]
public class BuildingCacheEntry
{
    public string gmlId;
    public BuildingData data;
    public string color; // Stored as hex string
}
