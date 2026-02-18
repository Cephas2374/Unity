# Quick Start Guide - Per-Feature Building Coloring

## 🚀 Fastest Setup (2 Steps)

### Step 1: Run Setup Menu
```
Unity Menu Bar → Tools → Cesium → Setup Feature Colorizer
```

### Step 2: Enter Play Mode
Press **Play** and wait ~10 seconds for buildings to load and color automatically.

---

## ✅ Success Indicators

Watch Console for these messages:

### 1. Data Loaded
```
✅ Successfully loaded 5005 buildings into cache
✅ Successfully loaded 5005 building colors into cache
```

### 2. Colorizer Ready
```
CesiumFeatureColorizer: Subscribed to tile creation events.
  - Cached colors: 5005
  - Custom shader: Cesium/VertexColoredBuilding
```

### 3. Buildings Coloring (as tiles load)
```
✓ Colored 8234/8234 vertices for 35 buildings in mesh Building_LOD2_part_042
✓ Colored 12450/12450 vertices for 47 buildings in mesh Building_LOD2_part_123
```

### 4. Visual Confirmation
- **Green buildings**: Low energy (efficient)
- **Yellow buildings**: Medium energy
- **Red buildings**: High energy (inefficient)
- **Gray buildings**: No data available

---

## 🎯 What This Does

**Problem**: Cesium batches 20-50 buildings into single meshes. Material coloring affects entire batches.

**Solution**: Writes unique colors to individual vertices based on API data.

**Result**: Each building shows its own color, even within batched meshes.

---

## 🛠️ Manual Setup (If Automatic Fails)

1. **Select** `bisingen` GameObject (Cesium3DTileset)
2. **Add Component** → `CesiumFeatureColorizer`
3. **Drag** `BuildingEnergyManager` to `Energy Manager` field
4. **Drag** `VertexColoredBuilding.shader` to `Custom Shader` field
5. **Set** `Vertex Color Strength` = 1.0
6. **In BuildingEnergyManager**: Uncheck `Enable Continuous Coloring`

---

## 🔧 Runtime Commands

### Recolor All Loaded Tiles
```
Unity Menu → Tools → Cesium → Recolor All Tiles (Runtime Only)
```
Use this if tiles loaded before colorizer was ready.

### Show Statistics
```
Unity Menu → Tools → Cesium → Show Colorizer Statistics (Runtime Only)
```
Displays how many buildings/vertices have been colored.

---

## ❓ Troubleshooting

### Buildings Stay Gray
**Cause**: API data not loaded yet  
**Fix**: Wait 10-15 seconds. Check console for "✅ Successfully loaded..."

### Buildings Stay White
**Cause**: Shader not applied  
**Fix**: Check Material shows "Cesium/VertexColoredBuilding" shader

### Still Batch Coloring (clusters same color)
**Cause**: Component not active  
**Fix**: 
1. Check `CesiumFeatureColorizer` is on Cesium3DTileset GameObject
2. Enable `Debug Mode` checkbox
3. Check console for warnings

### Black Buildings
**Cause**: No lighting  
**Fix**: Add **Directional Light** to scene

---

## 📊 Expected Results

| Feature | Behavior |
|---------|----------|
| **Building Colors** | Green/Yellow/Red/Gray based on API |
| **Click Detection** | Single building highlights (not cluster) |
| **UI Panel** | Shows correct energy data with matching color |
| **Performance** | 60 FPS, ~4MB extra memory |
| **Accuracy** | 5005 buildings with individual colors |

---

## 📁 Files Created

- `Assets/CesiumFeatureColorizer.cs` - Main coloring logic
- `Assets/VertexColoredBuilding.shader` - Vertex color shader
- `Assets/Editor/CesiumFeatureColorizerSetup.cs` - Setup menu
- `Assets/PerFeatureColoringGuide.md` - Full documentation
- `Assets/PerFeatureColoringSolution_Summary.md` - Technical details

---

## 💡 Key Concepts

**Vertex Colors**: Instead of coloring materials (affects entire mesh), we color individual vertices.

**Feature IDs**: Cesium stores which building each vertex belongs to.

**Per-Vertex Coloring**: Each vertex gets its building's API color → GPU interpolates → Individual buildings show unique colors.

---

## 🎨 Color Legend

```
🟢 Green (#66b032)  → Low Energy Demand (Efficient)
🟡 Yellow (#ffff00) → Medium Energy Demand
🔴 Red (#ff0000)    → High Energy Demand (Inefficient)
⚪ Gray (#808080)   → No Data Available
```

---

## 📞 Need Help?

1. **Check Console**: Enable `Debug Mode` in CesiumFeatureColorizer
2. **Read Full Guide**: `Assets/PerFeatureColoringGuide.md`
3. **Technical Details**: `Assets/PerFeatureColoringSolution_Summary.md`

---

## 🎓 How It Works (Simple)

```
1. API loads → 5005 buildings with colors cached
2. Cesium loads tile → Event fires
3. Colorizer processes tile:
   - For each vertex: Get building ID → Get color → Set vertex color
4. GPU renders → Each building shows its color
```

---

**Version**: 1.0  
**Unity**: 2022.3+  
**Cesium for Unity**: v1.22.0+
