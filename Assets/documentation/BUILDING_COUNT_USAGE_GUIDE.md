# Building Count & API vs Tileset Comparison Guide

This guide explains how to count buildings, compare API data with the Cesium tileset, and troubleshoot mismatches.

---

## 🎯 Quick Start: View Building Counts

### Method 1: On-Screen Display (Easiest)

**Setup:**
1. Select any GameObject in Hierarchy (or create a new one: `GameObject` → `Create Empty`)
2. Name it "BuildingStats" (optional)
3. Add Component → `BuildingCountDisplay`
4. **Play** the scene

**What you'll see:**
A panel in the top-left corner showing:
```
Building Statistics
📦 API Total: 5005
🎨 Colored: 5005
🏢 Tileset: 5005
📊 Match: 100%
🕐 2026-02-14 10:30:45
```

**Keyboard Shortcuts:**
- `Ctrl+Shift+B` - Toggle display on/off

---

### Method 2: Console Logging (Detailed Analysis)

#### Option A: Quick Count (BuildingEnergyManager)
1. Select `BuildingEnergyManager` GameObject in Hierarchy
2. Right-click script component in Inspector
3. Select **"Show Building Count"**

**Console Output:**
```
=== BUILDING COUNT SUMMARY ===
📊 Total Buildings Loaded: 5005
📦 Buildings in Data Cache: 5005
🎨 Buildings with Color: 5005
⚪ Buildings without Color: 0
📅 Last Updated: 2026-02-14 10:30:45
================================
```

#### Option B: Detailed Comparison (CesiumFeatureColorizer)
1. Select the `bisingen` GameObject (or your Cesium3DTileset)
2. Find the `CesiumFeatureColorizer` component
3. Right-click → **"Count Buildings (Tileset vs API)"**

**Or use keyboard shortcut:** `Ctrl+Shift+C`

**Detailed Console Output:**
```
========================================
📊 BUILDING COUNT ANALYSIS
========================================

🌐 API DATA:
   • Total buildings in cache: 5005
   • Last cache update: 2026-02-14 10:30:45
   • Cache field used: 'modified_gml_id'
   Sample API keys (first 5):
      [0] 'DEBW_0010008wid658_part_1'
      [1] 'DEBW_0010008wid658_part_2'
      ...

🏢 TILESET DATA:
   • Meshes in tileset: 247
   • Scanning meshes for gml:id values...
   • Unique buildings found: 5005
   • Tileset field used: 'gml:id'
   Sample tileset gml:ids (first 5):
      [0] 'DEBW_0010008wid658_part_1'
      [1] 'DEBW_0010008wid658_part_2'
      ...

🔍 MATCHING ANALYSIS:
   ✅ Exact matches: 4950
   ✅ Normalized matches: 55
   ✅ Total matched: 5005 (100.0%)
   ❌ Unmatched: 0

========================================
📊 SUMMARY:
========================================
API Buildings: 5005
Tileset Buildings: 5005
Matching Buildings: 5005 (100.0%)

✅ GOOD: 100.0% match rate indicates data is synchronized!
========================================
```

---

## 📊 Understanding the Numbers

### API Data (from Backend)
- **Source:** Database via REST API
- **Field:** `modified_gml_id`
- **When loaded:** On scene start or manual refresh
- **Shows:** Buildings that SHOULD have colors

### Tileset Data (from Cesium)
- **Source:** 3D Tiles streaming from server
- **Field:** `gml:id` (metadata in tiles)
- **When loaded:** As tiles stream in (progressive)
- **Shows:** Buildings that ARE rendered in scene

### Match Percentage
The system tries to match IDs using:
1. **Exact match** - IDs are identical
2. **Normalized match** - IDs match after removing prefixes/suffixes
3. **Robust matching** - Fallback pattern matching

**Good:** >90% match rate  
**Warning:** 70-90% match rate  
**Critical:** <70% match rate

---

## 🔧 Common Scenarios & Solutions

### Scenario 1: Perfect Match ✅
```
API Buildings: 5005
Tileset Buildings: 5005
Match: 100%
```
**Status:** Everything is working correctly!  
**Action:** None needed

---

### Scenario 2: API > Tileset ⚠️
```
API Buildings: 5005
Tileset Buildings: 2500
Match: 50%
```

**Cause:** Tiles haven't fully loaded yet

**Solutions:**
1. **Wait for tiles to load** - Zoom in/out to force tile loading
2. **Check camera view** - Move camera to see all areas
3. **Run count again** - After a few minutes, repeat the count
4. **Check tileset URL** - Verify Cesium3DTileset has correct URL

---

### Scenario 3: Tileset > API 🚨
```
API Buildings: 500
Tileset Buildings: 5005
Match: 10%
```

**Cause:** Tileset was updated but API cache is old

**Solutions:**
1. **Hard Refresh Cache:**
   - Select `BuildingEnergyManager`
   - Right-click → "Hard Refresh Cache (Clear & Reload)"
   
2. **Or use keyboard shortcut:**
   - Press `Ctrl+Shift+R` (anywhere in scene)

3. **Check API endpoint:**
   - Verify `community_id` is correct
   - Check access token is valid

---

### Scenario 4: Low Match Rate ⚠️
```
API Buildings: 5005
Tileset Buildings: 5005
Match: 65%
```

**Cause:** ID format mismatch between API and tileset

**Solutions:**
1. **Check ID examples:**
   - Look at "Sample API keys" vs "Sample tileset gml:ids" in console
   - Compare formatting (underscores, prefixes, case)

2. **API field check:**
   - API should use `modified_gml_id` field
   - Tileset should have `gml:id` metadata

3. **Contact data team:**
   - IDs may need to be standardized
   - Share console output for analysis

---

## 🎨 Customizing the Display

### Change Display Position

Edit `BuildingCountDisplay.CreateDisplayUI()`:

```csharp
// Top-right corner
panelRect.anchorMin = new Vector2(1, 1);
panelRect.anchorMax = new Vector2(1, 1);
panelRect.pivot = new Vector2(1, 1);
panelRect.anchoredPosition = new Vector2(-10, -10);

// Bottom-left
panelRect.anchorMin = new Vector2(0, 0);
panelRect.anchorMax = new Vector2(0, 0);
panelRect.pivot = new Vector2(0, 0);
panelRect.anchoredPosition = new Vector2(10, 10);

// Bottom-right
panelRect.anchorMin = new Vector2(1, 0);
panelRect.anchorMax = new Vector2(1, 0);
panelRect.pivot = new Vector2(1, 0);
panelRect.anchoredPosition = new Vector2(-10, 10);
```

### Toggle Display Options

In Inspector → `BuildingCountDisplay` component:

**References:**
- ✓ `Auto Update` - Updates automatically
- ✓ `Update Interval` - How often to update (seconds)

**Display Options:**
- ✓ `Show Total` - Show total from API
- ✓ `Show With Color` - Show buildings with colors
- ☐ `Show Without Color` - Show buildings missing colors
- ✓ `Show Last Update` - Show cache timestamp
- ✓ `Show Comparison` - Show API vs Tileset comparison

---

## 💻 Programmatic Access

### Get Building Counts in Code

```csharp
using UnityEngine;

public class YourScript : MonoBehaviour
{
    void Start()
    {
        // Get managers
        BuildingEnergyManager energyManager = FindObjectOfType<BuildingEnergyManager>();
        CesiumFeatureColorizer colorizer = FindObjectOfType<CesiumFeatureColorizer>();
        
        // Get API counts
        var stats = energyManager.GetBuildingStatistics();
        int apiTotal = stats["total"];
        int withColor = stats["withColor"];
        int withoutColor = stats["withoutColor"];
        
        Debug.Log($"API Buildings: {apiTotal}");
        Debug.Log($"With Color: {withColor}");
        Debug.Log($"Without Color: {withoutColor}");
        
        // Get tileset count
        int tilesetCount = colorizer.GetTilesetBuildingCount();
        Debug.Log($"Tileset Buildings: {tilesetCount}");
        
        // Calculate match
        float matchPercent = (Mathf.Min(apiTotal, tilesetCount) * 100f) / Mathf.Max(apiTotal, tilesetCount);
        Debug.Log($"Match Rate: {matchPercent:F1}%");
    }
}
```

### Get Building IDs

```csharp
// Get all API building IDs
Dictionary<string, Color> apiBuildings = energyManager.buildingColorCache;
Debug.Log($"API Building IDs: {string.Join(", ", apiBuildings.Keys)}");

// Get all tileset building IDs
HashSet<string> tilesetBuildings = colorizer.GetUniqueTilesetBuildingIds();
Debug.Log($"Tileset Building IDs: {string.Join(", ", tilesetBuildings)}");

// Find missing buildings
var missingInTileset = apiBuildings.Keys.Where(id => !tilesetBuildings.Contains(id));
var missingInAPI = tilesetBuildings.Where(id => !apiBuildings.ContainsKey(id));

Debug.Log($"Missing in tileset: {string.Join(", ", missingInTileset)}");
Debug.Log($"Missing in API: {string.Join(", ", missingInAPI)}");
```

---

## ⌨️ Keyboard Shortcuts Summary

| Shortcut | Action | Component |
|----------|--------|-----------|
| `Ctrl+Shift+C` | Count buildings & show comparison | CesiumFeatureColorizer |
| `Ctrl+Shift+R` | Hard refresh API cache | BuildingEnergyManager |
| `Ctrl+Shift+B` | Toggle on-screen display | BuildingCountDisplay |

---

## 🔍 Troubleshooting

### "API cache is EMPTY"
**Cause:** Data hasn't loaded from API yet  
**Solution:** 
1. Check Console for API errors
2. Verify `accessToken` in BuildingEnergyManager
3. Run "Hard Refresh Cache"

### "No meshes found in tileset"
**Cause:** Tiles haven't loaded yet  
**Solution:**
1. Wait a few seconds
2. Ensure Cesium3DTileset is active
3. Check tileset URL is correct

### Display shows "Loading..."
**Cause:** Waiting for data  
**Solution:**
1. Check Console for errors
2. Verify BuildingEnergyManager is configured
3. Wait for API to load (5-10 seconds)

### Tileset count is 0
**Cause:** Tiles need to be scanned  
**Solution:**
1. Run "Count Buildings (Tileset vs API)" manually
2. Move camera to load tiles
3. Wait for tiles to stream in

---

## 📋 Best Practices

### For Production
1. ✅ Enable `BuildingCountDisplay` with `showComparison = true`
2. ✅ Set `updateInterval = 2.0` (update every 2 seconds)
3. ✅ Disable `showWithoutColor` (cleaner display)
4. ✅ Run comparison on scene start to verify data sync

### For Debugging
1. 🔍 Enable detailed logging in CesiumFeatureColorizer
2. 🔍 Run "Count Buildings" context menu for full analysis
3. 🔍 Compare sample IDs to identify format differences
4. 🔍 Export building lists for data team analysis

### For Performance
1. ⚡ Increase `comparisonUpdateInterval` to 10+ seconds
2. ⚡ Disable `autoUpdate` if not actively monitoring
3. ⚡ Use context menu counts instead of continuous display

---

## 📊 Expected Results by Phase

### Phase 1: Initial Load (0-5 seconds)
```
API Total: 0
Tileset: 0
Match: N/A
Last Update: Loading...
```

### Phase 2: API Loaded (5-10 seconds)
```
API Total: 5005
Tileset: 0-500 (streaming)
Match: <50%
Last Update: 2026-02-14 10:30:45
```

### Phase 3: Full Sync (30-60 seconds)
```
API Total: 5005
Tileset: 5005
Match: 95-100%
Last Update: 2026-02-14 10:30:45
```

---

## 📚 Related Documentation

- [GC_HANDLE_FIX_AND_BUILDING_COUNT.md](GC_HANDLE_FIX_AND_BUILDING_COUNT.md) - Building count feature overview
- [API_CONFIGURATION_GUIDE.md](API_CONFIGURATION_GUIDE.md) - API setup
- [SETUP_CHECKLIST.md](SETUP_CHECKLIST.md) - Complete setup guide
- [PerFeatureColoringGuide.md](PerFeatureColoringGuide.md) - How coloring works

---

## ✅ Quick Test Checklist

After setup, verify everything works:

- [ ] On-screen display appears in Play mode
- [ ] API count > 0 after 5-10 seconds
- [ ] Tileset count increases as tiles load
- [ ] Match percentage > 90%
- [ ] `Ctrl+Shift+C` shows detailed analysis
- [ ] `Ctrl+Shift+B` toggles display
- [ ] Console shows no errors
- [ ] Buildings are colored correctly

---

**Last Updated:** February 14, 2026  
**Version:** 2.0  
**Status:** ✅ Fully Functional
