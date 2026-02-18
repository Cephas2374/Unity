# Per-Feature Coloring Implementation Guide

## Problem Solved
Cesium batches buildings into single meshes (20-50 buildings per mesh) for performance. Material-based coloring affects entire batches. This solution colors individual buildings within batched meshes using vertex colors.

## Solution Overview
**Vertex-Based Per-Feature Coloring**: Writes unique colors directly to mesh vertices based on feature IDs, allowing individual buildings to be colored within the same batched mesh.

## Components Created

### 1. CesiumFeatureColorizer.cs
**Location**: `Assets/CesiumFeatureColorizer.cs`

**Purpose**: Applies per-feature colors to Cesium tiles by writing vertex colors

**How It Works**:
1. Subscribes to `Cesium3DTileset.OnTileGameObjectCreated` event
2. When a tile loads, processes all meshes in that tile
3. For each vertex in the mesh:
   - Gets the feature ID (building ID) from `CesiumFeatureIdTexture` or `CesiumFeatureIdAttribute`
   - Looks up the building's gml:id from `CesiumPropertyTable`
   - Retrieves the cached color from `BuildingEnergyManager`
   - Writes the color to the vertex
4. Clones the mesh with vertex colors applied
5. Sets shader property to use vertex colors

**Key Methods**:
- `OnTileCreated()`: Entry point when new tiles load
- `ColorizeMesh()`: Main logic for per-feature coloring
- `GetColorForFeature()`: Maps feature ID → gml:id → color
- `RecolorAllTiles()`: Public method to reapply colors to all loaded tiles

### 2. VertexColoredBuilding.shader
**Location**: `Assets/VertexColoredBuilding.shader`

**Purpose**: Custom shader that renders vertex colors on Cesium buildings

**Features**:
- **Dual Render Pipeline Support**: Works with both URP and Built-In
- **Vertex Color Support**: Reads mesh vertex colors and applies them
- **_UseVertexColor Property**: Toggle between texture and vertex colors (0-1 blend)
- **Standard Lighting**: Includes main light, shadows, and fog
- **Shadow Casting**: Proper shadow pass for depth

**Properties**:
- `_BaseColor`: Base tint color
- `_BaseColorMap`: Texture (if any)
- `_UseVertexColor` (0-1): Mix factor (1 = full vertex color, 0 = texture/base color)
- `_Smoothness`, `_Metallic`: Surface properties

## Implementation Steps

### Step 1: Setup Components

1. **Attach CesiumFeatureColorizer to Cesium3DTileset**:
   - Select your "bisingen" Cesium3DTileset GameObject
   - Add Component → CesiumFeatureColorizer
   - The script will auto-find BuildingEnergyManager
   - Enable `Debug Mode` to see detailed logs

2. **No Code Changes Needed**:
   - BuildingEnergyManager already caches colors correctly
   - `FindColorForBuilding()` method is now public for the colorizer

### Step 2: Apply Custom Shader (Option A: Manual)

**For New Tiles Loading**:
Cesium will create materials automatically, but we need to replace them:

1. Let the scene run and tiles load
2. In the Hierarchy, expand the Cesium3DTileset object
3. Select a child mesh renderer
4. In Inspector, find the Material
5. Change Shader dropdown: `Shader > Cesium > VertexColoredBuilding`
6. Repeat for other materials

**Problem**: This is tedious and materials reset on reload.

### Step 2: Apply Custom Shader (Option B: Automated)

Modify `CesiumFeatureColorizer.cs` to auto-apply the shader:

```csharp
// Add at the top with other fields
[Header("Shader Settings")]
public Shader customShader;

// In ColorizeMesh() method, after setting vertex colors, add:
if (customShader != null)
{
    Material newMaterial = new Material(customShader);
    newMaterial.SetFloat("_UseVertexColor", 1.0f);
    renderer.material = newMaterial;
}
```

Then in Unity:
1. Select CesiumFeatureColorizer component
2. Drag `VertexColoredBuilding.shader` to the `Custom Shader` field

### Step 3: Test and Verify

1. **Run the Scene**
2. Wait for authentication and data loading (5005 buildings cached)
3. Watch console for: `"CesiumFeatureColorizer: Colored X/Y vertices for Z buildings in mesh..."`
4. Buildings should now show individual colors (green/yellow/red/gray)
5. Click a building - should highlight only that building (if using modified highlight)

### Step 4: Disable Old Coloring System

In BuildingEnergyManager Inspector:
- **Uncheck** `Enable Continuous Coloring` (old material-based approach)
- Keep data caching enabled

## Expected Results

**Before (Material-Based)**:
- 5005 buildings → ~180 meshes
- Clicking one building highlights 20-50 buildings (entire batch)
- Only one color per mesh

**After (Vertex-Based)**:
- Each building within a batch has its own color
- UI text colors match 3D colors perfectly
- Individual building selection possible

## Debugging

### Check 1: Verify Color Cache
Console should show: `"✅ Successfully loaded 5005 buildings into cache"`

### Check 2: Verify Feature IDs
Enable Debug Mode on CesiumFeatureColorizer, check for:
```
CesiumFeatureColorizer: Colored 12450/12450 vertices for 47 buildings in mesh Building_LOD2_part_123
```

If you see "No CesiumPrimitiveFeatures" or "No feature ID sets":
- Cesium version may not support feature IDs
- Tileset may not have feature metadata

### Check 3: Verify Shader Application
Select a colored building mesh:
- Material shader should be "Cesium/VertexColoredBuilding"
- `_UseVertexColor` property should be 1.0

### Check 4: Visual Inspection
- Buildings should have multiple colors (not all same color per group)
- Colors should match the legend: green (efficient), yellow (medium), red (inefficient), gray (no data)

## Advanced: Custom Shader Variants

### For Unlit Buildings (Performance)
Remove lighting calculations from shader (faster)

### For Outline/Selection
Add a selection color property:
```hlsl
float _Selected;
float4 _SelectionColor;

// In fragment shader:
finalColor = lerp(finalColor, _SelectionColor, _Selected);
```

Set from script when building is clicked.

## Troubleshooting

### Issue: All Buildings Gray
- **Cause**: Color cache is empty
- **Fix**: Wait for API data to load (check console for "✅ Successfully loaded...")

### Issue: Buildings Have Random Colors
- **Cause**: Feature ID → gml:id mapping is broken
- **Fix**: Check that property table has "gml:id" property (not "gml_id" or other variant)

### Issue: Entire Batches Still Same Color
- **Cause**: Shader not applied or vertex colors not written
- **Fix**: 
  1. Check material uses VertexColoredBuilding shader
  2. Check mesh has colors array (select mesh in inspector)
  3. Enable debug mode and check logs

### Issue: Buildings Turn Black
- **Cause**: No main light in scene or normals are inverted
- **Fix**: Add Directional Light to scene

### Issue: Performance Drop
- **Cause**: Cloning meshes and creating materials per-tile
- **Fix**: 
  1. Reduce `debugMode` logging
  2. Only process changed tiles (tracking with `processedTiles` HashSet)
  3. Consider mesh pooling

## Performance Considerations

**Memory**:
- Each mesh clone adds ~10-20KB
- 180 meshes = ~3.6MB additional memory
- Acceptable for modern systems

**CPU**:
- Processing happens only when tiles load (not every frame)
- One-time cost per tile

**GPU**:
- Vertex colors add no GPU cost (already in vertex data)
- Custom shader is comparable to standard shader

## Alternative Approaches (Not Implemented)

### 1. Shader Feature ID Lookup
Instead of vertex colors, pass feature IDs and look up colors in a texture/buffer.
**Pros**: Less memory
**Cons**: More complex, requires shader modifications

### 2. Decals/Overlay
Project colored quads on top of buildings.
**Pros**: No mesh modification
**Cons**: Z-fighting, doesn't work at all angles

### 3. Mesh Splitting
Un-batch Cesium meshes into individual buildings.
**Pros**: Full control per building
**Cons**: Destroys Cesium's performance optimizations (5005 draw calls)

## Future Enhancements

1. **Highlight Shader**: Separate shader for selected buildings with outline/glow
2. **LOD Color Persistence**: Ensure colors persist across LOD changes
3. **Dynamic Updates**: Re-color buildings when data updates from API
4. **Color Interpolation**: Smooth transitions when energy values change
5. **Shader Graph Version**: Visual shader editing for artists
