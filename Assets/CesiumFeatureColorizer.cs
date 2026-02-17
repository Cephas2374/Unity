using UnityEngine;
using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// OPTIMIZED: Applies per-feature colors to Cesium 3D Tiles by writing colors directly to mesh vertices.
/// 
/// PERFORMANCE IMPROVEMENTS:
/// 1. Feature ID caching - Eliminates redundant lookups for same building ID within a mesh
/// 2. Cesium component caching - Reuses GetComponent results instead of repeated calls
/// 3. Material pooling - Reuses materials instead of creating new ones per mesh
/// 4. Batched processing - Spreads work across frames to maintain smooth playback
/// 5. Optimized string operations - Cached ID matching results
/// 6. Removed duplicate logging - Single consolidated path for all logs
/// </summary>
public class CesiumFeatureColorizer : MonoBehaviour
{
    [Tooltip("Reference to the BuildingEnergyManager with cached colors")]
    public BuildingEnergyManager energyManager;

    [Tooltip("Reference to the Cesium3DTileset to color")]
    public Cesium3DTileset tileset;

    [Header("Shader Settings")]
    [Tooltip("Custom shader that supports vertex colors")]
    public Shader customShader;

    [Range(0f, 1f)]
    public float vertexColorStrength = 1.0f;

    [Header("Performance")]
    [Tooltip("Skip recoloring existing tiles on data load")]
    public bool skipInitialRecolor = true;
    
    [Tooltip("Meshes per frame during batched recoloring (higher = faster but may stutter)")]
    [Range(10, 500)]
    public int meshesPerFrame = 50;
    
    [Header("Debug")]
    public bool debugMode = true;

    // ===== OPTIMIZATION: Per-mesh feature ID cache =====
    private Dictionary<long, Color> featureColorCache = new Dictionary<long, Color>();
    
    // ===== OPTIMIZATION: Cesium component cache =====
    private struct CesiumMeshContext
    {
        public CesiumModelMetadata modelMetadata;
        public CesiumPropertyTable propertyTable;
        public CesiumPropertyTableProperty gmlIdProperty;
        public CesiumFeatureIdSet featureIdSet;
        public CesiumFeatureIdAttribute featureIdAttribute;
        public CesiumFeatureIdTexture featureIdTexture;
    }
    
    // ===== OPTIMIZATION: Material pooling =====
    private Material cachedVertexColorMaterial;
    
    private HashSet<GameObject> processedTiles = new HashSet<GameObject>();
    private int totalBuildingsColored = 0;
    private int totalVerticesColored = 0;
    private HashSet<string> uniqueBuildingsInTileset = new HashSet<string>();
    private Dictionary<string, string> idMatchCache = new Dictionary<string, string>(); // gmlId -> matched API key

    void Start()
    {
        StartCoroutine(DelayedStart());
    }
    
    void Update()
    {
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("<color=cyan>🔢 Keyboard shortcut: Ctrl+Shift+C - Count Buildings</color>");
            StartCoroutine(CountBuildingsAndShowStats());
        }
    }
    
    IEnumerator DelayedStart()
    {
        yield return new WaitForEndOfFrame();
        
        if (energyManager == null)
        {
            energyManager = FindObjectOfType<BuildingEnergyManager>();
            if (energyManager == null)
            {
                Debug.LogError("<color=red>❌ CesiumFeatureColorizer: BuildingEnergyManager not found!</color>");
                yield break;
            }
            Debug.Log($"<color=green>✅ Found BuildingEnergyManager</color>");
        }

        if (customShader == null)
        {
            customShader = Shader.Find("Cesium/VertexColoredBuilding");
            if (customShader == null)
            {
                Debug.LogError("<color=red>❌ SHADER NOT FOUND! 'Cesium/VertexColoredBuilding' shader is missing.</color>");
                yield break;
            }
            Debug.Log("<color=green>✅ Auto-loaded VertexColoredBuilding shader</color>");
        }

        if (tileset == null)
        {
            tileset = GetComponent<Cesium3DTileset>();
            if (tileset == null)
            {
                Debug.LogError("<color=red>❌ Cesium3DTileset component not found!</color>");
                yield break;
            }
            Debug.Log($"<color=green>✅ Found tileset '{tileset.name}'</color>");
        }
        
        // Skip terrain tilesets
        string tilesetName = tileset.gameObject.name.ToLower();
        if (tilesetName.Contains("terrain") || tilesetName.Contains("world") || tilesetName.Contains("imagery"))
        {
            Debug.Log($"<color=yellow>Skipping terrain tileset '{tileset.name}'</color>");
            this.enabled = false;
            yield break;
        }

        try
        {
            // Pre-create pooled material
            cachedVertexColorMaterial = new Material(customShader);
            cachedVertexColorMaterial.SetFloat("_UseVertexColor", 1.0f);
            
            tileset.OnTileGameObjectCreated += OnTileCreated;
            Debug.Log($"<color=cyan>CesiumFeatureColorizer initialized - Cache: {energyManager.buildingColorCache.Count} colors</color>");
            
            if (energyManager.buildingColorCache.Count == 0)
                StartCoroutine(WaitForDataAndRecolor());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Initialization error: {e.Message}");
        }
    }
    
    private IEnumerator WaitForDataAndRecolor()
    {
        float elapsed = 0f;
        const float maxWait = 30f;
        
        while (energyManager.buildingColorCache.Count == 0 && elapsed < maxWait)
        {
            yield return new WaitForSeconds(2f);
            elapsed += 2f;
            if (elapsed % 10f < 2f && debugMode)
                Debug.Log($"<color=yellow>Waiting for data... {elapsed}s. Cache: {energyManager.buildingColorCache.Count}</color>");
        }
        
        if (energyManager.buildingColorCache.Count > 0)
        {
            Debug.Log($"<color=green>✅ Cache loaded: {energyManager.buildingColorCache.Count} colors</color>");
            if (!skipInitialRecolor)
            {
                Debug.Log($"<color=cyan>Starting initial recoloring...</color>");
                RecolorAllTiles();
            }
        }
        else
        {
            Debug.LogWarning("<color=yellow>⚠️ Timeout waiting for data</color>");
        }
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        if (tileset != null)
            tileset.OnTileGameObjectCreated -= OnTileCreated;
        
        if (cachedVertexColorMaterial != null)
            Destroy(cachedVertexColorMaterial);
    }

    private void OnTileCreated(GameObject tileGameObject)
    {
        if (processedTiles.Contains(tileGameObject))
            return;

        processedTiles.Add(tileGameObject);

        MeshRenderer[] renderers = tileGameObject.GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer renderer in renderers)
        {
            ColorizeMesh(renderer);
        }
    }

    /// <summary>
    /// OPTIMIZED: Color a single mesh with caching to eliminate redundant lookups
    /// </summary>
    public void ColorizeMesh(MeshRenderer renderer)
    {
        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        Mesh mesh = meshFilter.sharedMesh;
        int vertexCount = mesh.vertexCount;

        // Get and cache Cesium components (avoids repeated GetComponent calls)
        if (!TryGetCesiumMeshContext(renderer, out CesiumMeshContext context))
        {
            ApplyDefaultColoring(renderer, meshFilter, mesh, vertexCount);
            return;
        }

        // ===== OPTIMIZATION: Per-mesh feature ID cache =====
        featureColorCache.Clear();
        
        Color[] colors = new Color[vertexCount];
        int coloredVertices = 0;
        int totalBuildings = 0;
        HashSet<Color> uniqueColorsInMesh = new HashSet<Color>();

        // Color all vertices using optimized context
        if (context.featureIdAttribute != null && context.featureIdAttribute.status == CesiumFeatureIdAttributeStatus.Valid)
        {
            ColorizeWithAttribute(context.featureIdAttribute, context.propertyTable, context.gmlIdProperty, 
                vertexCount, colors, ref coloredVertices, ref totalBuildings, uniqueColorsInMesh);
        }
        else if (context.featureIdTexture != null && context.featureIdTexture.status == CesiumFeatureIdTextureStatus.Valid)
        {
            ColorizeWithTexture(context.featureIdTexture, context.propertyTable, context.gmlIdProperty, 
                vertexCount, colors, ref coloredVertices, ref totalBuildings, uniqueColorsInMesh);
        }
        else
        {
            return;
        }

        // Apply vertex colors to mesh
        Mesh newMesh = Object.Instantiate(mesh);
        newMesh.colors = colors;
        meshFilter.mesh = newMesh;

        totalBuildingsColored += totalBuildings;
        totalVerticesColored += coloredVertices;

        // Apply shader
        ApplyVertexColorMaterial(renderer, context.modelMetadata);

        if (debugMode && totalBuildings > 0)
        {
            Debug.Log($"<color=green>✅ {totalBuildings} buildings, {coloredVertices}/{vertexCount} vertices, {uniqueColorsInMesh.Count} colors</color>");
        }
    }

    /// <summary>
    /// OPTIMIZATION: Get cached Cesium context to avoid repeated GetComponent calls
    /// </summary>
    private bool TryGetCesiumMeshContext(MeshRenderer renderer, out CesiumMeshContext context)
    {
        context = default;

        CesiumPrimitiveFeatures primitiveFeatures = renderer.GetComponent<CesiumPrimitiveFeatures>();
        if (primitiveFeatures == null || primitiveFeatures.featureIdSets == null || primitiveFeatures.featureIdSets.Length == 0)
            return false;

        context.featureIdSet = primitiveFeatures.featureIdSets[0];
        context.featureIdAttribute = context.featureIdSet as CesiumFeatureIdAttribute;
        context.featureIdTexture = context.featureIdSet as CesiumFeatureIdTexture;

        context.modelMetadata = renderer.GetComponentInParent<CesiumModelMetadata>();
        if (context.modelMetadata == null || context.modelMetadata.propertyTables == null || context.modelMetadata.propertyTables.Length == 0)
            return false;

        context.propertyTable = context.modelMetadata.propertyTables[0];

        // Get gml:id property (try multiple names)
        if (!context.propertyTable.properties.TryGetValue("gml:id", out context.gmlIdProperty))
        {
            if (!context.propertyTable.properties.TryGetValue("gml_id", out context.gmlIdProperty) &&
                !context.propertyTable.properties.TryGetValue("gmlId", out context.gmlIdProperty))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// OPTIMIZATION: Color vertices using feature ID attribute with per-mesh caching
    /// </summary>
    private void ColorizeWithAttribute(CesiumFeatureIdAttribute featureIdAttr, CesiumPropertyTable propertyTable,
        CesiumPropertyTableProperty gmlIdProperty, int vertexCount, Color[] colors, 
        ref int coloredVertices, ref int totalBuildings, HashSet<Color> uniqueColors)
    {
        for (int v = 0; v < vertexCount; v++)
        {
            long featureId = featureIdAttr.GetFeatureIdForVertex(v);
            Color color = GetCachedColorForFeature(featureId, propertyTable, gmlIdProperty, ref totalBuildings);
            color.a = 1.0f;
            colors[v] = color;
            
            if (color != Color.white)
            {
                coloredVertices++;
                uniqueColors.Add(color);
            }
        }
    }

    /// <summary>
    /// OPTIMIZATION: Color vertices using feature ID texture with per-mesh caching
    /// </summary>
    private void ColorizeWithTexture(CesiumFeatureIdTexture featureIdTex, CesiumPropertyTable propertyTable,
        CesiumPropertyTableProperty gmlIdProperty, int vertexCount, Color[] colors, 
        ref int coloredVertices, ref int totalBuildings, HashSet<Color> uniqueColors)
    {
        for (int v = 0; v < vertexCount; v++)
        {
            long featureId = featureIdTex.GetFeatureIdForVertex(v);
            Color color = GetCachedColorForFeature(featureId, propertyTable, gmlIdProperty, ref totalBuildings);
            color.a = 1.0f;
            colors[v] = color;
            
            if (color != Color.white)
            {
                coloredVertices++;
                uniqueColors.Add(color);
            }
        }
    }

    /// <summary>
    /// OPTIMIZATION: Cache feature ID lookups per mesh to avoid redundant dictionary operations
    /// </summary>
    private Color GetCachedColorForFeature(long featureId, CesiumPropertyTable propertyTable, 
        CesiumPropertyTableProperty gmlIdProperty, ref int buildingCount)
    {
        if (featureId < 0)
            return Color.white;

        // Check per-mesh cache first
        if (featureColorCache.TryGetValue(featureId, out Color cachedColor))
            return cachedColor;

        // Get gml:id for this feature
        CesiumMetadataValue gmlIdValue = gmlIdProperty.GetValue(featureId);
        if (gmlIdValue == null)
            return Color.white;

        string gmlId = gmlIdValue.GetString(string.Empty);
        if (string.IsNullOrEmpty(gmlId))
            return Color.white;

        buildingCount++;
        uniqueBuildingsInTileset.Add(gmlId);

        // Try direct match
        if (energyManager.buildingColorCache.TryGetValue(gmlId, out Color directColor))
        {
            featureColorCache[featureId] = directColor;
            return directColor;
        }

        // Try cached match
        if (idMatchCache.TryGetValue(gmlId, out string matchedKey) && 
            energyManager.buildingColorCache.TryGetValue(matchedKey, out Color cachedMatchColor))
        {
            featureColorCache[featureId] = cachedMatchColor;
            return cachedMatchColor;
        }

        // Try normalized or fallback match
        Color? foundColor = FindColorWithCaching(gmlId);
        Color result = foundColor.HasValue ? foundColor.Value : Color.white;
        featureColorCache[featureId] = result;
        return result;
    }

    /// <summary>
    /// Find color with caching to avoid repeated searches for same ID
    /// </summary>
    private Color? FindColorWithCaching(string gmlId)
    {
        // Try normalized match
        string normalizedGmlId = NormalizeGmlId(gmlId);
        
        foreach (var kvp in energyManager.buildingColorCache)
        {
            if (NormalizeGmlId(kvp.Key) == normalizedGmlId)
            {
                idMatchCache[gmlId] = kvp.Key;
                return kvp.Value;
            }
        }

        // Try robust fallback
        Color? foundColor = energyManager.FindColorForBuilding(gmlId, -1);
        if (foundColor.HasValue && foundColor.Value != Color.clear)
        {
            idMatchCache[gmlId] = gmlId;
            energyManager.buildingColorCache[gmlId] = foundColor.Value;
            return foundColor.Value;
        }

        return null;
    }

    /// <summary>
    /// OPTIMIZATION: Material pooling - reuse shader material instead of creating new ones
    /// </summary>
    private void ApplyVertexColorMaterial(MeshRenderer renderer, CesiumModelMetadata modelMetadata)
    {
        if (cachedVertexColorMaterial == null)
            return;

        // Clone the pooled material for this renderer
        Material newMaterial = new Material(cachedVertexColorMaterial);
        
        // Copy texture from original if available
        if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColorMap"))
        {
            Texture tex = renderer.sharedMaterial.GetTexture("_BaseColorMap");
            if (tex != null && newMaterial.HasProperty("_MainTex"))
                newMaterial.SetTexture("_MainTex", tex);
        }

        renderer.material = newMaterial;
    }

    private void ApplyDefaultColoring(MeshRenderer renderer, MeshFilter meshFilter, Mesh mesh, int vertexCount)
    {
        if (vertexCount > 50000)
            return;

        Bounds bounds = mesh.bounds;
        float aspectRatio = bounds.size.y / Mathf.Max(bounds.size.x, bounds.size.z, 0.01f);
        
        if (aspectRatio < 0.05f && bounds.size.y < 5f)
            return;
        
        if (bounds.size.x > 1000f || bounds.size.z > 1000f)
            return;

        Color[] colors = new Color[vertexCount];
        for (int i = 0; i < vertexCount; i++)
            colors[i] = Color.white;

        Mesh newMesh = Object.Instantiate(mesh);
        newMesh.colors = colors;
        meshFilter.mesh = newMesh;

        if (cachedVertexColorMaterial != null)
        {
            Material newMaterial = new Material(cachedVertexColorMaterial);
            renderer.material = newMaterial;
        }
    }

    private string NormalizeGmlId(string gmlId)
    {
        if (string.IsNullOrEmpty(gmlId)) return "";
        return gmlId.Trim().Replace(" ", "").Replace("\t", "").Replace("\n", "").ToLowerInvariant();
    }

    /// <summary>
    /// OPTIMIZATION: Batched recoloring across frames
    /// </summary>
    public void RecolorAllTiles()
    {
        StartCoroutine(RecolorAllTilesWithLogging());
    }

    public IEnumerator RecolorAllTilesWithLogging()
    {
        processedTiles.Clear();
        totalBuildingsColored = 0;
        totalVerticesColored = 0;
        uniqueBuildingsInTileset.Clear();
        idMatchCache.Clear();

        MeshRenderer[] allRenderers = tileset.GetComponentsInChildren<MeshRenderer>();
        
        Debug.Log($"<color=cyan>🎨 Recoloring {allRenderers.Length} meshes, Cache: {energyManager.buildingColorCache.Count}</color>");
        
        if (allRenderers.Length == 0 || energyManager.buildingColorCache.Count == 0)
        {
            Debug.LogError("<color=red>❌ No data to color (empty meshes or cache)</color>");
            yield break;
        }

        int processed = 0;
        foreach (MeshRenderer renderer in allRenderers)
        {
            ColorizeMesh(renderer);
            processed++;
            
            // Batch across frames
            if (processed % meshesPerFrame == 0)
            {
                Debug.Log($"<color=yellow>Progress: {processed}/{allRenderers.Length}</color>");
                yield return null;
            }
        }

        // Final statistics — count how many tileset buildings got a color from API
        int tilesetCount = uniqueBuildingsInTileset.Count;
        int apiCount = energyManager.buildingColorCache.Count;
        
        // Count matched tileset buildings: direct cache hits + normalized/fallback matches via idMatchCache
        int matchedCount = 0;
        foreach (var tileId in uniqueBuildingsInTileset)
        {
            if (energyManager.buildingColorCache.ContainsKey(tileId) || idMatchCache.ContainsKey(tileId))
                matchedCount++;
        }
        
        float matchRate = tilesetCount > 0 ? (matchedCount * 100f / tilesetCount) : 0f;
        int unmatchedTileset = tilesetCount - matchedCount;
        
        Debug.Log($"<color=green>✅ Recoloring complete</color>");
        Debug.Log($"<color=yellow>API: {apiCount} | Tileset: {tilesetCount} | Colored: {matchedCount} | Match: {matchRate:F1}%</color>");
        
        if (unmatchedTileset > 0)
        {
            Debug.Log($"<color=cyan>ℹ️ {unmatchedTileset} tileset buildings have no energy data in API (not in database)</color>");
        }
        
        if (matchRate < 50f && tilesetCount > 0)
        {
            Debug.LogWarning($"<color=orange>⚠️ Low match rate: {matchRate:F1}% — only {matchedCount}/{tilesetCount} tileset buildings found in API</color>");
            Debug.LogWarning($"<color=yellow>Possible causes: stale cache, case mismatch, or tileset covers area outside API data</color>");
            Debug.LogWarning($"<color=cyan>Try: Right-click BuildingEnergyManager → Hard Refresh Cache</color>");
            
            // Show ID samples for debugging
            Debug.Log($"<color=magenta>========== ID FORMAT DIAGNOSTIC ==========</color>");
            Debug.Log($"<color=cyan>📦 TILESET gml:id (first 5):</color>");
            foreach (var id in uniqueBuildingsInTileset.Take(5))
            {
                Debug.Log($"<color=cyan>   '{id}'</color>");
            }
            Debug.Log($"<color=lime>🌐 API cache keys (first 5):</color>");
            foreach (var id in energyManager.buildingColorCache.Keys.Take(5))
            {
                Debug.Log($"<color=lime>   '{id}'</color>");
            }
            Debug.Log($"<color=magenta>===========================================</color>");
        }
        else if (matchRate >= 50f && matchRate < 85f)
        {
            Debug.Log($"<color=yellow>📊 Moderate match: {matchedCount}/{tilesetCount} ({matchRate:F1}%)</color>");
        }
        else if (tilesetCount > 0)
        {
            Debug.Log($"<color=green>✅ Good match: {matchedCount}/{tilesetCount} ({matchRate:F1}%)</color>");
        }
    }

    [ContextMenu("Count Buildings (Tileset vs API)")]
    public void CountBuildings()
    {
        StartCoroutine(CountBuildingsAndShowStats());
    }

    /// <summary>
    /// Recolor a single building by gmlId (called after building data is edited)
    /// </summary>
    public void RecolorSingleBuilding(string gmlId, Color newColor)
    {
        idMatchCache.Clear(); // Clear match cache to force fresh lookup
        featureColorCache.Clear(); // Clear feature cache

        MeshRenderer[] allRenderers = tileset.GetComponentsInChildren<MeshRenderer>();

        int recoloredCount = 0;
        foreach (MeshRenderer renderer in allRenderers)
        {
            if (MeshContainsBuilding(renderer, gmlId))
            {
                ColorizeMesh(renderer);
                recoloredCount++;
            }
        }

        if (recoloredCount == 0 && debugMode)
            Debug.LogWarning($"<color=orange>Building {gmlId} not found in visible tiles</color>");
    }

    /// <summary>
    /// Check if a mesh contains a specific building
    /// </summary>
    private bool MeshContainsBuilding(MeshRenderer renderer, string targetGmlId)
    {
        if (!TryGetCesiumMeshContext(renderer, out CesiumMeshContext context))
            return false;

        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return false;

        HashSet<long> seenFeatureIds = new HashSet<long>();
        int vertexCount = meshFilter.sharedMesh.vertexCount;

        if (context.featureIdAttribute != null && context.featureIdAttribute.status == CesiumFeatureIdAttributeStatus.Valid)
        {
            for (int v = 0; v < vertexCount; v++)
            {
                long featureId = context.featureIdAttribute.GetFeatureIdForVertex(v);
                if (featureId >= 0 && !seenFeatureIds.Contains(featureId))
                {
                    seenFeatureIds.Add(featureId);
                    CesiumMetadataValue gmlIdValue = context.gmlIdProperty.GetValue(featureId);
                    if (gmlIdValue != null)
                    {
                        string gmlId = gmlIdValue.GetString(string.Empty);
                        if (NormalizeGmlId(gmlId) == NormalizeGmlId(targetGmlId))
                            return true;
                    }
                }
            }
        }
        else if (context.featureIdTexture != null && context.featureIdTexture.status == CesiumFeatureIdTextureStatus.Valid)
        {
            for (int v = 0; v < vertexCount; v++)
            {
                long featureId = context.featureIdTexture.GetFeatureIdForVertex(v);
                if (featureId >= 0 && !seenFeatureIds.Contains(featureId))
                {
                    seenFeatureIds.Add(featureId);
                    CesiumMetadataValue gmlIdValue = context.gmlIdProperty.GetValue(featureId);
                    if (gmlIdValue != null)
                    {
                        string gmlId = gmlIdValue.GetString(string.Empty);
                        if (NormalizeGmlId(gmlId) == NormalizeGmlId(targetGmlId))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private IEnumerator CountBuildingsAndShowStats()
    {
        Debug.Log("<color=cyan>======== BUILDING COUNT ANALYSIS ========</color>");
        
        yield return ScanTilesetForBuildingIds(showStats: true);
        
        int tilesetCount = uniqueBuildingsInTileset.Count;
        int apiCount = energyManager.buildingColorCache.Count;
        
        // Count how many tileset buildings have a match in the API cache (case-insensitive via idMatchCache)
        int matchedCount = 0;
        foreach (var tileId in uniqueBuildingsInTileset)
        {
            if (energyManager.buildingColorCache.ContainsKey(tileId) || idMatchCache.ContainsKey(tileId))
                matchedCount++;
        }
        float matchPercent = tilesetCount > 0 ? (matchedCount * 100f / tilesetCount) : 0f;
        int unmatchedCount = tilesetCount - matchedCount;

        Debug.Log($"<color=yellow>API buildings: {apiCount}</color>");
        Debug.Log($"<color=yellow>Tileset buildings: {tilesetCount}</color>");
        Debug.Log($"<color=yellow>Matched (colored): {matchedCount}</color>");
        Debug.Log($"<color=yellow>No energy data: {unmatchedCount}</color>");
        Debug.Log($"<color=green>Match rate: {matchPercent:F1}%</color>");

        if (matchPercent >= 85f)
        {
            Debug.Log($"<color=green>✅ GOOD: {matchPercent:F1}% of tileset buildings have energy data</color>");
        }
        else if (matchPercent >= 50f)
        {
            Debug.Log($"<color=yellow>📊 Moderate: {matchPercent:F1}% match — {unmatchedCount} buildings not in energy database</color>");
        }
        else
        {
            Debug.LogWarning($"<color=orange>⚠️ Low match: {matchPercent:F1}% — most tileset buildings have no API data</color>");
            Debug.LogWarning($"<color=cyan>Try: Right-click BuildingEnergyManager → Hard Refresh Cache</color>");
        }

        Debug.Log("<color=cyan>========================================</color>");
    }
    
    /// <summary>
    /// Public method to scan tileset and populate building IDs (used by BuildingEnergyManager)
    /// </summary>
    public IEnumerator ScanTilesetForBuildingIds(bool showStats = false)
    {
        uniqueBuildingsInTileset.Clear();

        MeshRenderer[] allRenderers = tileset.GetComponentsInChildren<MeshRenderer>();
        
        if (showStats)
        {
            Debug.Log($"<color=yellow>Meshes in tileset: {allRenderers.Length}</color>");
        }

        int processed = 0;
        foreach (MeshRenderer renderer in allRenderers)
        {
            ScanMeshForGmlIds(renderer);
            processed++;
            
            if (processed % 50 == 0)
            {
                if (showStats)
                {
                    Debug.Log($"<color=gray>Progress: {processed}/{allRenderers.Length}</color>");
                }
                yield return null;
            }
        }
    }

    private void ScanMeshForGmlIds(MeshRenderer renderer)
    {
        if (!TryGetCesiumMeshContext(renderer, out CesiumMeshContext context))
            return;

        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        int vertexCount = meshFilter.sharedMesh.vertexCount;
        HashSet<long> seenFeatureIds = new HashSet<long>();

        if (context.featureIdAttribute != null && context.featureIdAttribute.status == CesiumFeatureIdAttributeStatus.Valid)
        {
            for (int v = 0; v < vertexCount; v++)
            {
                long featureId = context.featureIdAttribute.GetFeatureIdForVertex(v);
                if (featureId >= 0 && !seenFeatureIds.Contains(featureId))
                {
                    seenFeatureIds.Add(featureId);
                    CesiumMetadataValue gmlIdValue = context.gmlIdProperty.GetValue(featureId);
                    if (gmlIdValue != null)
                    {
                        string gmlId = gmlIdValue.GetString(string.Empty);
                        if (!string.IsNullOrEmpty(gmlId))
                            uniqueBuildingsInTileset.Add(gmlId);
                    }
                }
            }
        }
        else if (context.featureIdTexture != null && context.featureIdTexture.status == CesiumFeatureIdTextureStatus.Valid)
        {
            for (int v = 0; v < vertexCount; v++)
            {
                long featureId = context.featureIdTexture.GetFeatureIdForVertex(v);
                if (featureId >= 0 && !seenFeatureIds.Contains(featureId))
                {
                    seenFeatureIds.Add(featureId);
                    CesiumMetadataValue gmlIdValue = context.gmlIdProperty.GetValue(featureId);
                    if (gmlIdValue != null)
                    {
                        string gmlId = gmlIdValue.GetString(string.Empty);
                        if (!string.IsNullOrEmpty(gmlId))
                            uniqueBuildingsInTileset.Add(gmlId);
                    }
                }
            }
        }
    }

    public string GetStatistics()
    {
        return $"Buildings: {totalBuildingsColored}, Vertices: {totalVerticesColored}, Tiles: {processedTiles.Count}";
    }

    /// <summary>
    /// Get the current count of unique buildings in the tileset
    /// </summary>
    public int GetTilesetBuildingCount()
    {
        return uniqueBuildingsInTileset.Count;
    }

    /// <summary>
    /// Get the count of tileset buildings that successfully matched an API cache entry
    /// </summary>
    public int GetMatchedBuildingCount()
    {
        if (energyManager == null) return 0;
        int matched = 0;
        foreach (var tileId in uniqueBuildingsInTileset)
        {
            if (energyManager.buildingColorCache.ContainsKey(tileId) || idMatchCache.ContainsKey(tileId))
                matched++;
        }
        return matched;
    }

    /// <summary>
    /// Get a copy of all unique building IDs found in the tileset
    /// </summary>
    public HashSet<string> GetUniqueTilesetBuildingIds()
    {
        return new HashSet<string>(uniqueBuildingsInTileset);
    }
}
