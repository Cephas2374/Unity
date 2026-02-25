using UnityEngine;
using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // For LINQ queries in color validation

/// <summary>
/// Applies per-feature colors to Cesium 3D Tiles by writing colors directly to mesh vertices.
/// This solves the batched mesh problem by coloring individual buildings within the same mesh.
/// </summary>
public class CesiumFeatureColorizer : MonoBehaviour
{
    [Tooltip("Reference to the BuildingEnergyManager with cached colors")]
    public BuildingEnergyManager energyManager;

    [Tooltip("Reference to the Cesium3DTileset to color")]
    public Cesium3DTileset tileset;

    [Header("Shader Settings")]
    [Tooltip("Custom shader that supports vertex colors (leave null to use existing materials)")]
    public Shader customShader;

    [Tooltip("Force use of vertex colors (1.0) vs textures (0.0)")]
    [Range(0f, 1f)]
    public float vertexColorStrength = 1.0f;

    [Header("Performance")]
    [Tooltip("Skip recoloring existing tiles on data load (tiles will color as they stream in)")]
    public bool skipInitialRecolor = true;
    
    [Header("Debug")]
    [Tooltip("Enable debug logging")]
    public bool debugMode = true; // Enable by default for troubleshooting

    private HashSet<GameObject> processedTiles = new HashSet<GameObject>();
    private int totalBuildingsColored = 0;
    private int totalVerticesColored = 0;
    private HashSet<string> uniqueBuildingsInTileset = new HashSet<string>(); // Track unique gml:ids
    private Dictionary<string, string> tilesetToApiMatches = new Dictionary<string, string>(); // tileset gml:id -> API modified_gml_id

    void Start()
    {
        StartCoroutine(DelayedStart());
    }
    
    void Update()
    {
        // Keyboard shortcut: Ctrl+Shift+C = Count Buildings
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("<color=cyan>🔢 Keyboard shortcut: Ctrl+Shift+C - Count Buildings</color>");
            StartCoroutine(CountBuildingsAndShowStats());
        }
    }
    
    IEnumerator DelayedStart()
    {
        // Minimal delay - just wait for end of frame
        yield return new WaitForEndOfFrame();
        
        if (energyManager == null)
        {
            energyManager = FindObjectOfType<BuildingEnergyManager>();
            if (energyManager == null)
            {
                Debug.LogError("<color=red>❌ CesiumFeatureColorizer: BuildingEnergyManager not found!</color>");
                yield break;
            }
            else
            {
                Debug.Log($"<color=green>✅ CesiumFeatureColorizer: Found BuildingEnergyManager on '{energyManager.gameObject.name}'</color>");
            }
        }

        // ⚠️ CRITICAL: Auto-load vertex color shader if not assigned
        if (customShader == null)
        {
            customShader = Shader.Find("Cesium/VertexColoredBuilding");
            if (customShader == null)
            {
                Debug.LogError("<color=red>❌ SHADER NOT FOUND! 'Cesium/VertexColoredBuilding' shader is missing. Vertex colors will NOT display!</color>");
            }
            else
            {
                Debug.Log("<color=green>✅ Auto-loaded VertexColoredBuilding shader (was not assigned in Inspector)</color>");
            }
        }
        else
        {
            Debug.Log($"<color=green>✅ Using custom shader: {customShader.name}</color>");
        }

        if (tileset == null)
        {
            tileset = GetComponent<Cesium3DTileset>();
            if (tileset == null)
            {
                Debug.LogError("<color=red>❌ CesiumFeatureColorizer: Cesium3DTileset component not found!</color>");
                yield break;
            }
            else
            {
                Debug.Log($"<color=green>✅ CesiumFeatureColorizer: Found tileset '{tileset.name}'</color>");
            }
        }
        
        // Check if this tileset is likely terrain (skip if so)
        string tilesetName = tileset.gameObject.name.ToLower();
        if (tilesetName.Contains("terrain") || tilesetName.Contains("world") || tilesetName.Contains("imagery"))
        {
            Debug.Log($"<color=yellow>CesiumFeatureColorizer: Skipping terrain tileset '{tileset.name}' - colorizer is for buildings only</color>");
            this.enabled = false;
            yield break;
        }

        try
        {
            // Subscribe to tile creation event
            tileset.OnTileGameObjectCreated += OnTileCreated;
            
            Debug.Log($"<color=cyan>CesiumFeatureColorizer initialized on '{tileset.name}' - Cache: {energyManager.buildingColorCache.Count} colors</color>");
            
            // If cache is empty, wait for data (non-blocking)
            if (energyManager.buildingColorCache.Count == 0)
            {
                // Only show warning once per colorizer, not repeatedly
                if (debugMode)
                    Debug.Log($"<color=yellow>Color cache empty - will apply colors when data loads</color>");
                StartCoroutine(WaitForDataAndRecolor());
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"CesiumFeatureColorizer initialization error: {e.Message}");
        }
    }
    
    private IEnumerator WaitForDataAndRecolor()
    {
        // Wait up to 30 seconds for data to load (reduced from 60)
        float elapsed = 0f;
        float maxWait = 30f;
        
        while (energyManager.buildingColorCache.Count == 0 && elapsed < maxWait)
        {
            yield return new WaitForSeconds(1f); // Check every 1 second instead of 2
            elapsed += 1f;
            
            if (elapsed % 10f == 0f && debugMode)
            {
                Debug.Log($"<color=yellow>Waiting for data... {elapsed}s elapsed. Cache: {energyManager.buildingColorCache.Count}</color>");
            }
        }
        
        if (energyManager.buildingColorCache.Count > 0)
        {
            Debug.Log($"<color=green>✅ Color cache loaded! {energyManager.buildingColorCache.Count} colors available for buildings.</color>");
            
            if (!skipInitialRecolor)
            {
                Debug.Log($"<color=cyan>🎨 Starting initial recoloring of all existing tiles...</color>");
                RecolorAllTiles();
            }
            else
            {
                Debug.Log($"<color=cyan>⏸️ Initial recolor skipped - tiles will color as they stream in</color>");
            }
        }
        else
        {
            Debug.LogWarning("<color=yellow>⚠️ Timeout: BuildingEnergyManager failed to load data after 30 seconds. Buildings will show default gray colors.</color>");
        }
    }

    void OnDestroy()
    {
        // Stop all coroutines to prevent GC handle issues on domain reload
        StopAllCoroutines();
        
        if (tileset != null)
        {
            tileset.OnTileGameObjectCreated -= OnTileCreated;
        }
    }

    /// <summary>
    /// Called when a new tile GameObject is created by Cesium
    /// </summary>
    private void OnTileCreated(GameObject tileGameObject)
    {
        if (processedTiles.Contains(tileGameObject))
            return;

        processedTiles.Add(tileGameObject);

        if (debugMode)
            Debug.Log($"CesiumFeatureColorizer: Processing new tile: {tileGameObject.name}");

        // Process all mesh renderers in this tile
        bool wasColored = ColorizeAllMeshesInTile(tileGameObject);
        
        if (debugMode && wasColored)
            Debug.Log($"🎨 Successfully colored new tile: {tileGameObject.name}");
    }

    /// <summary>
    /// Colorizes all meshes in a tile GameObject
    /// </summary>
    private bool ColorizeAllMeshesInTile(GameObject tileGameObject)
    {
        bool anyColored = false;
        MeshRenderer[] renderers = tileGameObject.GetComponentsInChildren<MeshRenderer>();

        foreach (MeshRenderer renderer in renderers)
        {
            ColorizeMesh(renderer);
            anyColored = true; // Assume coloring attempt was made
        }
        
        return anyColored;
    }

    /// <summary>
    /// Applies per-feature colors to a single mesh by writing vertex colors
    /// Made public to allow BuildingEnergyManager to progressively recolor meshes
    /// </summary>
    public void ColorizeMesh(MeshRenderer renderer)
    {
        if (debugMode && totalBuildingsColored < 5) // Only log first few to avoid spam
        {
            Debug.Log($"<color=cyan>🔧 ColorizeMesh called for: {renderer.name}</color>");
        }
        
        // Get the mesh filter first - skip if no mesh
        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            if (debugMode && totalBuildingsColored < 5)
            {
                Debug.LogWarning($"<color=orange>⚠️ No mesh found on {renderer.name}</color>");
            }
            return;
        }

        Mesh mesh = meshFilter.sharedMesh;
        int vertexCount = mesh.vertexCount;
        
        if (debugMode && totalBuildingsColored < 5)
        {
            Debug.Log($"<color=yellow>📐 Mesh has {vertexCount} vertices</color>");
        }

        // Get the CesiumPrimitiveFeatures component
        CesiumPrimitiveFeatures primitiveFeatures = renderer.GetComponent<CesiumPrimitiveFeatures>();
        if (primitiveFeatures == null)
        {
            if (debugMode && totalBuildingsColored < 5)
            {
                Debug.LogWarning($"<color=orange>⚠️ No CesiumPrimitiveFeatures on {renderer.name}, using default coloring</color>");
            }
            // Apply default gray coloring even without feature metadata
            // This ensures building meshes have proper coloring applied
            ApplyDefaultColoring(renderer, meshFilter, mesh, vertexCount);
            return;
        }
        
        if (debugMode && totalBuildingsColored < 5)
        {
            Debug.Log($"<color=green>✅ CesiumPrimitiveFeatures found on {renderer.name}</color>");
        }

        // Continue with normal feature-based coloring

        // Get feature ID sets
        var featureIdSets = primitiveFeatures.featureIdSets;
        if (featureIdSets == null || featureIdSets.Length == 0)
        {
            // Silently skip tiles without feature IDs
            return;
        }

        // Use the first feature ID set
        CesiumFeatureIdSet featureIdSet = featureIdSets[0];

        // Get the CesiumModelMetadata to access property tables
        CesiumModelMetadata modelMetadata = renderer.GetComponentInParent<CesiumModelMetadata>();
        if (modelMetadata == null)
        {
            // Silently skip tiles without metadata
            return;
        }

        // Get property tables
        var propertyTables = modelMetadata.propertyTables;
        if (propertyTables == null || propertyTables.Length == 0)
        {
            // Silently skip tiles without property tables
            return;
        }

        CesiumPropertyTable propertyTable = propertyTables[0];

        // Create vertex color array
        Color[] colors = new Color[vertexCount];
        Color defaultColor = Color.white; // Default for buildings without data (keeps original texture)

        int coloredVertices = 0;
        int totalBuildings = 0;
        
        // 🔎 DEBUG: Log mesh info
        if (debugMode && totalBuildingsColored < 5)
        {
            Debug.Log($"<color=cyan>📊 Coloring mesh '{renderer.name}' with {vertexCount} vertices</color>");
        }

        // 🎨 Track unique colors in this mesh for validation
        HashSet<Color> uniqueColorsInMesh = new HashSet<Color>();

        // Try to cast to specific feature ID types
        CesiumFeatureIdAttribute featureIdAttribute = featureIdSet as CesiumFeatureIdAttribute;
        CesiumFeatureIdTexture featureIdTexture = featureIdSet as CesiumFeatureIdTexture;

        if (featureIdAttribute != null && featureIdAttribute.status == CesiumFeatureIdAttributeStatus.Valid)
        {
            // Use attribute-based feature IDs (per-vertex)
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                long featureId = featureIdAttribute.GetFeatureIdForVertex(vertexIndex);
                Color color = GetColorForFeature(propertyTable, featureId, ref totalBuildings);
                
                // ✅ Ensure alpha is 1.0 for proper color application
                color.a = 1.0f;
                colors[vertexIndex] = color;
                
                if (color != defaultColor && color != Color.white)
                {
                    coloredVertices++;
                    uniqueColorsInMesh.Add(color);
                }
            }
            
            if (debugMode && totalBuildingsColored < 3)
            {
                Debug.Log($"<color=cyan>📊 Attribute-based coloring: {coloredVertices}/{vertexCount} vertices colored, {totalBuildings} buildings found</color>");
            }
        }
        else if (featureIdTexture != null && featureIdTexture.status == CesiumFeatureIdTextureStatus.Valid)
        {
            // Use texture-based feature IDs (per-vertex via UV)
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                long featureId = featureIdTexture.GetFeatureIdForVertex(vertexIndex);
                Color color = GetColorForFeature(propertyTable, featureId, ref totalBuildings);
                
                // ✅ Ensure alpha is 1.0 for proper color application
                color.a = 1.0f;
                colors[vertexIndex] = color;
                
                if (color != defaultColor && color != Color.white)
                {
                    coloredVertices++;
                    uniqueColorsInMesh.Add(color);
                }
            }
            
            if (debugMode && totalBuildingsColored < 3)
            {
                Debug.Log($"<color=cyan>📊 Texture-based coloring: {coloredVertices}/{vertexCount} vertices colored, {totalBuildings} buildings found</color>");
            }
        }
        else
        {
            // Silently skip - no feature ID mapping available
            return;
        }

        // Clone the mesh and set vertex colors
        Mesh newMesh = Object.Instantiate(mesh);
        newMesh.colors = colors;
        meshFilter.mesh = newMesh;

        totalBuildingsColored += totalBuildings;
        totalVerticesColored += coloredVertices;
        
        // 📊 Enhanced statistics logging
        if (debugMode || totalBuildings > 0)
        {
            Debug.Log($"<color=green>✅ Mesh '{renderer.gameObject.name}' colored:</color>");
            Debug.Log($"<color=yellow>   • {totalBuildings} buildings found</color>");
            Debug.Log($"<color=yellow>   • {uniqueColorsInMesh.Count} unique colors from API</color>");
            Debug.Log($"<color=yellow>   • {coloredVertices}/{vertexCount} vertices colored ({(coloredVertices*100f/vertexCount):F1}%)</color>");
            Debug.Log($"<color=yellow>   • {vertexCount - coloredVertices} vertices kept original (white = no API data)</color>");
            
            // Show sample colors for validation
            if (uniqueColorsInMesh.Count > 0 && uniqueColorsInMesh.Count <= 5)
            {
                string colorList = string.Join(", ", uniqueColorsInMesh.Select(c => "#" + ColorUtility.ToHtmlStringRGB(c)));
                Debug.Log($"<color=cyan>   Colors in mesh: {colorList}</color>");
            }
        }

        // ⚠️ CRITICAL: Apply vertex color shader (REQUIRED for colors to display)
        if (customShader == null)
        {
            Debug.LogError($"<color=red>❌ Cannot apply colors to '{renderer.gameObject.name}' - customShader is null! Vertex colors will NOT display!</color>");
            return; // Don't continue without shader
        }

        Material newMaterial = new Material(customShader);
        
        // Copy properties from old material if it had a texture
        if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColorMap"))
        {
            Texture tex = renderer.sharedMaterial.GetTexture("_BaseColorMap");
            if (tex != null && newMaterial.HasProperty("_MainTex"))
            {
                newMaterial.SetTexture("_MainTex", tex);
            }
        }
        
        // ✅ CRITICAL: Set vertex color strength to 1.0 (full vertex colors, no texture blending)
        if (newMaterial.HasProperty("_UseVertexColor"))
        {
            newMaterial.SetFloat("_UseVertexColor", 1.0f);
        }
        else
        {
            Debug.LogWarning($"<color=orange>⚠️ Shader '{customShader.name}' doesn't have '_UseVertexColor' property!</color>");
        }
        
        renderer.material = newMaterial;
        
        if (debugMode && totalBuildings > 0)
        {
            Debug.Log($"<color=green>✅ Applied shader '{customShader.name}' to '{renderer.gameObject.name}'</color>");
        }

        if (debugMode || totalBuildings > 0)
        {
            if (debugMode)
                Debug.Log($"<color=green>✓ CesiumFeatureColorizer: Colored {coloredVertices}/{vertexCount} vertices " +
                      $"for {totalBuildings} buildings in mesh {renderer.gameObject.name}</color>");
        }
    }

    /// <summary>
    /// Applies default gray coloring to meshes that don't have CesiumPrimitiveFeatures
    /// This ensures building meshes at least get our custom shader and a base color
    /// </summary>
    private void ApplyDefaultColoring(MeshRenderer renderer, MeshFilter meshFilter, Mesh mesh, int vertexCount)
    {
        // Skip very large meshes (likely terrain)
        if (vertexCount > 50000)
        {
            return;
        }
        
        // Skip terrain/ground meshes (they tend to be very large and flat)
        Bounds bounds = mesh.bounds;
        float aspectRatio = bounds.size.y / Mathf.Max(bounds.size.x, bounds.size.z, 0.01f);
        
        // Buildings typically have some height, terrain is flat
        if (aspectRatio < 0.05f && bounds.size.y < 5f)
        {
            // Likely terrain, skip
            return;
        }
        
        // Skip if this looks like a satellite imagery tile (very wide)
        if (bounds.size.x > 1000f || bounds.size.z > 1000f)
        {
            return;
        }

        // Create vertex colors - all gray (no data)
        Color[] colors = new Color[vertexCount];
        Color defaultColor = Color.white; // White = no change to original texture

        for (int i = 0; i < vertexCount; i++)
        {
            colors[i] = defaultColor;
        }

        // Clone mesh and apply colors
        Mesh newMesh = Object.Instantiate(mesh);
        newMesh.colors = colors;
        meshFilter.mesh = newMesh;

        // Apply custom shader if provided
        if (customShader != null)
        {
            Material newMaterial = new Material(customShader);
            
            // Copy textures from original material if available
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColorMap"))
            {
                Texture tex = renderer.sharedMaterial.GetTexture("_BaseColorMap");
                if (tex != null)
                {
                    newMaterial.SetTexture("_BaseColorMap", tex);
                }
            }
            
            if (newMaterial.HasProperty("_UseVertexColor"))
            {
                newMaterial.SetFloat("_UseVertexColor", vertexColorStrength);
            }
            
            renderer.material = newMaterial;
        }

        if (debugMode)
        {
            Debug.Log($"<color=yellow>⚠ Applied white (no change) to {renderer.gameObject.name} " +
                     $"(no CesiumPrimitiveFeatures - tileset may lack per-feature metadata)</color>");
        }
    }

    /// <summary>
    /// Gets the color for a specific feature from the property table
    /// Returns EXACT color from API cache - NO custom colors or defaults
    /// </summary>
    private Color GetColorForFeature(CesiumPropertyTable propertyTable, long featureId, ref int buildingCount)
    {
        if (featureId < 0)
        {
            if (debugMode && buildingCount <= 3)
                Debug.Log($"<color=red>❌ Invalid featureId {featureId}</color>");
            return Color.white; // Invalid feature, keep original color
        }

        // Get the gml:id property
        CesiumPropertyTableProperty gmlIdProperty = null;
        if (!propertyTable.properties.TryGetValue("gml:id", out gmlIdProperty))
        {
            // Try alternative names
            if (!propertyTable.properties.TryGetValue("gml_id", out gmlIdProperty) &&
                !propertyTable.properties.TryGetValue("gmlId", out gmlIdProperty))
            {
                if (debugMode && buildingCount <= 3)
                    Debug.LogWarning($"<color=orange>⚠️ No gml:id property found in tileset</color>");
                return Color.white; // No gml:id property
            }
        }

        // Get the gml:id value for this feature
        CesiumMetadataValue gmlIdValue = gmlIdProperty.GetValue(featureId);
        if (gmlIdValue == null)
        {
            if (debugMode && buildingCount <= 3)
                Debug.LogWarning($"<color=orange>⚠️ gml:id value is null for feature {featureId}</color>");
            return Color.white;
        }

        string gmlId = gmlIdValue.GetString(string.Empty);
        if (string.IsNullOrEmpty(gmlId))
        {
            if (debugMode && buildingCount <= 3)
                Debug.LogWarning($"<color=orange>⚠️ gml:id is empty for feature {featureId}</color>");
            return Color.white;
        }

        buildingCount++;
        
        // Track unique buildings found in tileset
        uniqueBuildingsInTileset.Add(gmlId);
        
        // Look up EXACT color from API cache
        string normalizedGmlId = NormalizeGmlId(gmlId);
        
        // Try direct match first
        if (energyManager.buildingColorCache.TryGetValue(gmlId, out Color cachedColor))
        {
            // ✅ Only log first 3 buildings per mesh to avoid spam
            if (buildingCount <= 3)
                Debug.Log($"<color=green>✅ Color found for '{gmlId}': #{ColorUtility.ToHtmlStringRGB(cachedColor)}</color>");
            return cachedColor;
        }
        
        // Try normalized match
        foreach (var kvp in energyManager.buildingColorCache)
        {
            if (NormalizeGmlId(kvp.Key) == normalizedGmlId)
            {
                if (buildingCount <= 3)
                    Debug.Log($"<color=green>✅ Normalized match for '{gmlId}' via '{kvp.Key}': #{ColorUtility.ToHtmlStringRGB(kvp.Value)}</color>");
                // Cache this match for faster lookup next time
                energyManager.buildingColorCache[gmlId] = kvp.Value;
                return kvp.Value;
            }
        }

        // Try robust ID matching (fallback)
        Color? foundColor = energyManager.FindColorForBuilding(gmlId, featureId);
        if (foundColor.HasValue && foundColor.Value != Color.clear)
        {
            if (buildingCount <= 3)
                Debug.Log($"<color=orange>✅ Fallback match for '{gmlId}': #{ColorUtility.ToHtmlStringRGB(foundColor.Value)}</color>");
            // Cache for next time
            energyManager.buildingColorCache[gmlId] = foundColor.Value;
            return foundColor.Value;
        }

        // ❌ No match found - show cache keys for debugging
        if (buildingCount <= 10 || gmlId.Contains("DEBW_0010008wid6"))
        {
            Debug.LogWarning($"<color=red>❌ NO MATCH for '{gmlId}'! Cache has {energyManager.buildingColorCache.Count} entries</color>");
            Debug.LogWarning($"<color=yellow>Sample cache keys (first 5):</color>");
            int count = 0;
            foreach (var key in energyManager.buildingColorCache.Keys)
            {
                Debug.LogWarning($"<color=yellow>  [{count}] '{key}'</color>");
                if (++count >= 5) break;
            }
        }
        
        return Color.white; // No API color = keep original tileset color
    }

    /// <summary>
    /// Public method to trigger re-coloring of all existing tiles (batched to avoid freezing)
    /// </summary>
    public void RecolorAllTiles()
    {
        StartCoroutine(RecolorAllTilesWithLogging());
    }
    
    /// <summary>
    /// Count buildings in tileset and API, show matching statistics
    /// </summary>
    [ContextMenu("Count Buildings (Tileset vs API)")]
    public void CountBuildings()
    {
        StartCoroutine(CountBuildingsAndShowStats());
    }
    
    private IEnumerator CountBuildingsAndShowStats()
    {
        Debug.Log("<color=cyan>========================================</color>");
        Debug.Log("<color=cyan>📊 BUILDING COUNT ANALYSIS</color>");
        Debug.Log("<color=cyan>========================================</color>");
        
        // Reset counters
        uniqueBuildingsInTileset.Clear();
        tilesetToApiMatches.Clear();
        
        // 1. Count buildings in API cache
        int apiCount = energyManager.buildingColorCache.Count;
        Debug.Log($"<color=yellow>\n🌐 API DATA:</color>");
        Debug.Log($"<color=yellow>   • Total buildings in cache: {apiCount}</color>");
        Debug.Log($"<color=yellow>   • Last cache update: {energyManager.lastCacheUpdate}</color>");
        Debug.Log($"<color=yellow>   • Cache field used: 'modified_gml_id'</color>");
        
        if (apiCount == 0)
        {
            Debug.LogError("<color=red>❌ API cache is EMPTY! No buildings to compare.</color>");
            yield break;
        }
        
        // Show sample API keys
        Debug.Log($"<color=cyan>   Sample API keys (first 5):</color>");
        int count = 0;
        foreach (var key in energyManager.buildingColorCache.Keys)
        {
            Debug.Log($"<color=cyan>      [{count}] '{key}'</color>");
            if (++count >= 5) break;
        }
        
        // 2. Scan tileset for buildings
        Debug.Log($"<color=yellow>\n🏢 TILESET DATA:</color>");
        MeshRenderer[] allRenderers = tileset.GetComponentsInChildren<MeshRenderer>();
        Debug.Log($"<color=yellow>   • Meshes in tileset: {allRenderers.Length}</color>");
        
        if (allRenderers.Length == 0)
        {
            Debug.LogWarning("<color=orange>⚠️ No meshes found in tileset! Tiles may not be loaded yet.</color>");
            yield break;
        }
        
        // Scan all meshes to find unique gml:ids
        int meshesProcessed = 0;
        Debug.Log($"<color=yellow>   • Scanning meshes for gml:id values...</color>");
        
        foreach (MeshRenderer renderer in allRenderers)
        {
            ScanMeshForGmlIds(renderer);
            meshesProcessed++;
            
            // Yield every 50 meshes to prevent freezing
            if (meshesProcessed % 50 == 0)
            {
                Debug.Log($"<color=gray>      Progress: {meshesProcessed}/{allRenderers.Length} meshes scanned...</color>");
                yield return null;
            }
        }
        
        int tilesetCount = uniqueBuildingsInTileset.Count;
        Debug.Log($"<color=yellow>   • Unique buildings found: {tilesetCount}</color>");
        Debug.Log($"<color=yellow>   • Tileset field used: 'gml:id'</color>");
        
        // Show sample tileset keys
        Debug.Log($"<color=cyan>   Sample tileset gml:ids (first 5):</color>");
        count = 0;
        foreach (var key in uniqueBuildingsInTileset)
        {
            Debug.Log($"<color=cyan>      [{count}] '{key}'</color>");
            if (++count >= 5) break;
        }
        
        // 3. Match tileset gml:id with API modified_gml_id
        Debug.Log($"<color=yellow>\n🔍 MATCHING ANALYSIS:</color>");
        int exactMatches = 0;
        int normalizedMatches = 0;
        int noMatches = 0;
        
        foreach (var tilesetGmlId in uniqueBuildingsInTileset)
        {
            // Try exact match
            if (energyManager.buildingColorCache.ContainsKey(tilesetGmlId))
            {
                exactMatches++;
                tilesetToApiMatches[tilesetGmlId] = tilesetGmlId;
                continue;
            }
            
            // Try normalized match
            string normalizedTilesetId = NormalizeGmlId(tilesetGmlId);
            bool foundMatch = false;
            
            foreach (var apiKey in energyManager.buildingColorCache.Keys)
            {
                if (NormalizeGmlId(apiKey) == normalizedTilesetId)
                {
                    normalizedMatches++;
                    tilesetToApiMatches[tilesetGmlId] = apiKey;
                    foundMatch = true;
                    break;
                }
            }
            
            if (!foundMatch)
            {
                noMatches++;
                // Log first 5 unmatched buildings
                if (noMatches <= 5)
                {
                    Debug.LogWarning($"<color=orange>   ⚠️ No API match for tileset gml:id: '{tilesetGmlId}'</color>");
                }
            }
        }
        
        int totalMatches = exactMatches + normalizedMatches;
        float matchPercent = tilesetCount > 0 ? (totalMatches * 100f / tilesetCount) : 0f;
        
        Debug.Log($"<color=green>   ✅ Exact matches: {exactMatches}</color>");
        Debug.Log($"<color=green>   ✅ Normalized matches: {normalizedMatches}</color>");
        Debug.Log($"<color=green>   ✅ Total matched: {totalMatches} ({matchPercent:F1}%)</color>");
        Debug.Log($"<color=red>   ❌ Unmatched: {noMatches}</color>");
        
        // 4. Final Summary
        Debug.Log($"<color=cyan>\n========================================</color>");
        Debug.Log($"<color=cyan>📊 SUMMARY:</color>");
        Debug.Log($"<color=cyan>========================================</color>");
        Debug.Log($"<color=yellow>API Buildings: {apiCount}</color>");
        Debug.Log($"<color=yellow>Tileset Buildings: {tilesetCount}</color>");
        Debug.Log($"<color=yellow>Matching Buildings: {totalMatches} ({matchPercent:F1}%)</color>");
        
        // Provide recommendations
        if (tilesetCount < apiCount * 0.5f)
        {
            Debug.LogError($"<color=red>\n🚨 CRITICAL: Tileset has {tilesetCount} buildings but API has {apiCount}!</color>");
            Debug.LogError($"<color=red>This suggests your tileset has been UPDATED but cache is OLD.</color>");
            Debug.LogError($"<color=yellow>🔧 SOLUTION: Right-click 'BuildingEnergyManager' → 'Hard Refresh Cache'</color>");
            Debug.LogError($"<color=yellow>Or press: Ctrl+Shift+R to clear cache and reload from API</color>");
        }
        else if (matchPercent < 90f)
        {
            Debug.LogWarning($"<color=orange>\n⚠️ WARNING: Only {matchPercent:F1}% of buildings have matching IDs!</color>");
            Debug.LogWarning($"<color=yellow>Possible causes:</color>");
            Debug.LogWarning($"<color=yellow>   • GML ID formatting differences (whitespace, case, prefixes)</color>");
            Debug.LogWarning($"<color=yellow>   • API and tileset are from different data sources</color>");
            Debug.LogWarning($"<color=yellow>   • Buildings renamed in one dataset but not the other</color>");
        }
        else
        {
            Debug.Log($"<color=green>\n✅ GOOD: {matchPercent:F1}% match rate indicates data is synchronized!</color>");
        }
        
        Debug.Log($"<color=cyan>========================================</color>");
    }
    
    /// <summary>
    /// Get the current count of unique buildings in the tileset
    /// </summary>
    public int GetTilesetBuildingCount()
    {
        return uniqueBuildingsInTileset.Count;
    }
    
    /// <summary>
    /// Get a copy of all unique building IDs found in the tileset
    /// </summary>
    public HashSet<string> GetUniqueTilesetBuildingIds()
    {
        return new HashSet<string>(uniqueBuildingsInTileset);
    }
    
    /// <summary>
    /// Scan a single mesh for all unique gml:id values
    /// </summary>
    private void ScanMeshForGmlIds(MeshRenderer renderer)
    {
        CesiumPrimitiveFeatures primitiveFeatures = renderer.GetComponent<CesiumPrimitiveFeatures>();
        if (primitiveFeatures == null)
            return;
            
        CesiumFeatureIdSet[] featureIdSets = primitiveFeatures.featureIdSets;
        if (featureIdSets == null || featureIdSets.Length == 0)
            return;
            
        CesiumFeatureIdSet featureIdSet = featureIdSets[0];
        
        // Get the CesiumModelMetadata to access property tables
        CesiumModelMetadata modelMetadata = renderer.GetComponentInParent<CesiumModelMetadata>();
        if (modelMetadata == null)
            return;
            
        // Get property tables
        var propertyTables = modelMetadata.propertyTables;
        if (propertyTables == null || propertyTables.Length == 0)
            return;
            
        CesiumPropertyTable propertyTable = propertyTables[0];
        
        // Get the gml:id property
        CesiumPropertyTableProperty gmlIdProperty = null;
        if (!propertyTable.properties.TryGetValue("gml:id", out gmlIdProperty))
        {
            if (!propertyTable.properties.TryGetValue("gml_id", out gmlIdProperty) &&
                !propertyTable.properties.TryGetValue("gmlId", out gmlIdProperty))
                return;
        }
        
        // Cast to correct feature ID type
        CesiumFeatureIdAttribute featureIdAttribute = featureIdSet as CesiumFeatureIdAttribute;
        CesiumFeatureIdTexture featureIdTexture = featureIdSet as CesiumFeatureIdTexture;
        
        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;
            
        int vertexCount = meshFilter.sharedMesh.vertexCount;
        HashSet<long> seenFeatureIds = new HashSet<long>();
        
        // Scan all vertices to find unique feature IDs
        if (featureIdAttribute != null && featureIdAttribute.status == CesiumFeatureIdAttributeStatus.Valid)
        {
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                long featureId = featureIdAttribute.GetFeatureIdForVertex(vertexIndex);
                if (featureId >= 0 && !seenFeatureIds.Contains(featureId))
                {
                    seenFeatureIds.Add(featureId);
                    
                    // Get gml:id for this feature
                    CesiumMetadataValue gmlIdValue = gmlIdProperty.GetValue(featureId);
                    if (gmlIdValue != null)
                    {
                        string gmlId = gmlIdValue.GetString(string.Empty);
                        if (!string.IsNullOrEmpty(gmlId))
                        {
                            uniqueBuildingsInTileset.Add(gmlId);
                        }
                    }
                }
            }
        }
        else if (featureIdTexture != null && featureIdTexture.status == CesiumFeatureIdTextureStatus.Valid)
        {
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                long featureId = featureIdTexture.GetFeatureIdForVertex(vertexIndex);
                if (featureId >= 0 && !seenFeatureIds.Contains(featureId))
                {
                    seenFeatureIds.Add(featureId);
                    
                    // Get gml:id for this feature
                    CesiumMetadataValue gmlIdValue = gmlIdProperty.GetValue(featureId);
                    if (gmlIdValue != null)
                    {
                        string gmlId = gmlIdValue.GetString(string.Empty);
                        if (!string.IsNullOrEmpty(gmlId))
                        {
                            uniqueBuildingsInTileset.Add(gmlId);
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Recolor all tiles with detailed logging for debugging
    /// </summary>
    public IEnumerator RecolorAllTilesWithLogging()
    {
        processedTiles.Clear();
        totalBuildingsColored = 0;
        totalVerticesColored = 0;

        // Find all mesh renderers under the tileset
        MeshRenderer[] allRenderers = tileset.GetComponentsInChildren<MeshRenderer>();
        
        Debug.Log($"<color=cyan>🎨 RecolorAllTilesWithLogging started: {allRenderers.Length} meshes found</color>");
        Debug.Log($"<color=yellow>📋 API buildings in cache: {energyManager.buildingColorCache.Count}</color>");
        
        // Reset tileset building counter
        uniqueBuildingsInTileset.Clear();
        
        if (allRenderers.Length == 0)
        {
            Debug.LogWarning("<color=orange>⚠️ No meshes found! Tiles may not be loaded yet or tileset is empty.</color>");
            yield break;
        }
        
        if (energyManager.buildingColorCache.Count == 0)
        {
            Debug.LogError("<color=red>❌ No colors in cache! BuildingEnergyManager may not have loaded data yet.</color>");
            yield break;
        }
        
        if (energyManager.buildingColorCache.Count == 0)
        {
            Debug.LogError("<color=red>❌ No colors in cache! BuildingEnergyManager may not have loaded data yet.</color>");
            yield break;
        }

        int batchSize = 100; // Process 100 meshes per frame
        int processed = 0;
        
        foreach (MeshRenderer renderer in allRenderers)
        {
            ColorizeMesh(renderer);
            processed++;
            
            // Yield every batch to prevent freezing
            if (processed % batchSize == 0)
            {
                Debug.Log($"<color=yellow>Progress: {processed}/{allRenderers.Length} meshes recolored...</color>");
                yield return null; // Yield to next frame
            }
        }
        
        // 📊 Final statistics: API vs Tileset comparison
        Debug.Log($"<color=green>✅ Recoloring complete!</color>");
        Debug.Log($"<color=cyan>📊 BUILDING COUNT COMPARISON:</color>");
        Debug.Log($"<color=yellow>   • API: {energyManager.buildingColorCache.Count} buildings with energy data</color>");
        Debug.Log($"<color=yellow>   • Tileset: {uniqueBuildingsInTileset.Count} unique buildings (gml:ids) found</color>");
        Debug.Log($"<color=yellow>   • Match rate: {(uniqueBuildingsInTileset.Count * 100f / energyManager.buildingColorCache.Count):F1}%</color>");
        
        if (uniqueBuildingsInTileset.Count < energyManager.buildingColorCache.Count)
        {
            int missing = energyManager.buildingColorCache.Count - uniqueBuildingsInTileset.Count;
            Debug.LogWarning($"<color=orange>⚠️ {missing} buildings from API are NOT in the 3D tileset</color>");
            
            // 🚨 CRITICAL: Check if cache is likely stale
            float mismatchPercent = (missing * 100f) / energyManager.buildingColorCache.Count;
            if (mismatchPercent > 50f)
            {
                Debug.LogError($"<color=red>🚨 CRITICAL: {mismatchPercent:F0}% of cached buildings are missing from tileset!</color>");
                Debug.LogError($"<color=red>This suggests your 3D tileset has been UPDATED but the cache contains OLD data.</color>");
                Debug.LogError($"<color=yellow>🔧 FIX: Right-click on 'BuildingEnergyManager' in Hierarchy</color>");
                Debug.LogError($"<color=yellow>        → Select 'Hard Refresh Cache (Clear & Reload)'</color>");
                Debug.LogError($"<color=yellow>⏳ This will clear the old cache and download fresh data from API</color>");
            }
        }
        else if (uniqueBuildingsInTileset.Count > energyManager.buildingColorCache.Count)
        {
            int extra = uniqueBuildingsInTileset.Count - energyManager.buildingColorCache.Count;
            Debug.LogWarning($"<color=orange>⚠️ {extra} buildings in tileset have NO energy data from API</color>");
        }
        else
        {
            Debug.Log($"<color=green>✅ Perfect match! All tileset buildings have API data</color>");
        }
        
        Debug.Log($"<color=yellow>   • {totalVerticesColored} vertices colored across {allRenderers.Length} meshes</color>");
    }

    /// <summary>
    /// Recolor a single building by gmlId (efficient update after data change)
    /// </summary>
    public void RecolorSingleBuilding(string gmlId, Color newColor)
    {
        Debug.Log($"<color=cyan>🎨 RecolorSingleBuilding: {gmlId} to RGB({newColor.r:F2},{newColor.g:F2},{newColor.b:F2})</color>");
        
        if (tileset == null)
        {
            Debug.LogError("<color=red>❌ Tileset is null!</color>");
            return;
        }
        
        // Update cache first (ensure it's available for lookups)
        if (energyManager != null)
        {
            energyManager.buildingColorCache[gmlId] = newColor;
        }
        
        // Find all mesh renderers under the tileset
        MeshRenderer[] allRenderers = tileset.GetComponentsInChildren<MeshRenderer>();
        
        int recoloredMeshes = 0;
        
        foreach (MeshRenderer renderer in allRenderers)
        {
            // Check if this mesh contains the target building
            if (MeshContainsBuilding(renderer, gmlId, out int featureIndex))
            {
                // Recolor this specific mesh
                ColorizeMesh(renderer);
                recoloredMeshes++;
                
                Debug.Log($"<color=green>✅ Recolored mesh {renderer.name} containing building {gmlId}</color>");
            }
        }
        
        if (recoloredMeshes == 0)
        {
            Debug.LogWarning($"<color=orange>⚠️ Building {gmlId} not found in any loaded meshes (may not be visible/loaded yet)</color>");
        }
        else
        {
            Debug.Log($"<color=green>✅ Successfully recolored {recoloredMeshes} mesh(es) containing building {gmlId}</color>");
        }
    }
    
    /// <summary>
    /// Check if a mesh contains a specific building by gmlId
    /// </summary>
    private bool MeshContainsBuilding(MeshRenderer renderer, string targetGmlId, out int featureIndex)
    {
        featureIndex = -1;
        
        CesiumPrimitiveFeatures primitiveFeatures = renderer.GetComponent<CesiumPrimitiveFeatures>();
        if (primitiveFeatures == null || primitiveFeatures.featureIdSets == null || primitiveFeatures.featureIdSets.Length == 0)
            return false;
        
        CesiumModelMetadata modelMetadata = renderer.GetComponentInParent<CesiumModelMetadata>();
        if (modelMetadata == null || modelMetadata.propertyTables == null || modelMetadata.propertyTables.Length == 0)
            return false;
        
        CesiumPropertyTable propertyTable = modelMetadata.propertyTables[0];
        
        // Get gml:id property
        CesiumPropertyTableProperty gmlIdProperty = null;
        if (!propertyTable.properties.TryGetValue("gml:id", out gmlIdProperty))
        {
            if (!propertyTable.properties.TryGetValue("gml_id", out gmlIdProperty) &&
                !propertyTable.properties.TryGetValue("gmlId", out gmlIdProperty))
            {
                return false;
            }
        }
        
        // Check all features in this mesh
        for (long i = 0; i < propertyTable.count; i++)
        {
            CesiumMetadataValue gmlIdValue = gmlIdProperty.GetValue(i);
            if (gmlIdValue != null)
            {
                string gmlId = gmlIdValue.GetString(string.Empty);
                if (NormalizeGmlId(gmlId) == NormalizeGmlId(targetGmlId))
                {
                    featureIndex = (int)i;
                    return true;
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Normalize GML ID for matching
    /// </summary>
    private string NormalizeGmlId(string gmlId)
    {
        if (string.IsNullOrEmpty(gmlId)) return "";
        return gmlId.Trim().Replace(" ", "").Replace("\t", "").Replace("\n", "");
    }
    
    /// <summary>
    /// Get statistics about coloring process
    /// </summary>
    public string GetStatistics()
    {
        return $"Buildings colored: {totalBuildingsColored}, Vertices colored: {totalVerticesColored}, Tiles processed: {processedTiles.Count}";
    }
}
