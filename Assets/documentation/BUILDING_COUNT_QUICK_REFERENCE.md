# Building Count Quick Reference Card

## 🎯 Three Ways to Count Buildings

### 1️⃣ ON-SCREEN DISPLAY (Real-time)
**Best for:** Continuous monitoring during gameplay

```
Setup:
1. Add Component → BuildingCountDisplay
2. Press Play

Display shows:
┌─────────────────────────┐
│ Building Statistics     │
│ 📦 API Total: 5005     │
│ 🎨 Colored: 5005       │
│ 🏢 Tileset: 5005       │
│ 📊 Match: 100%         │
│ 🕐 2026-02-14 10:30    │
└─────────────────────────┘

Toggle: Ctrl+Shift+B
```

---

### 2️⃣ QUICK CONSOLE COUNT (Fast check)
**Best for:** Quick verification of API data

```
Method:
Select BuildingEnergyManager → Right-click → "Show Building Count"

Console Output:
=== BUILDING COUNT SUMMARY ===
📊 Total Buildings Loaded: 5005
📦 Buildings in Data Cache: 5005
🎨 Buildings with Color: 5005
⚪ Buildings without Color: 0
🏢 Tileset Buildings: 5005
📊 Match Rate: 100.0%
📅 Last Updated: 2026-02-14 10:30:45
================================
```

---

### 3️⃣ DETAILED COMPARISON (Full analysis)
**Best for:** Debugging ID mismatches

```
Method:
Select bisingen GameObject → Right-click CesiumFeatureColorizer → 
"Count Buildings (Tileset vs API)"

Or: Press Ctrl+Shift+C

Console Output:
========================================
📊 BUILDING COUNT ANALYSIS
========================================

🌐 API DATA:
   • Total buildings: 5005
   • Cache field: 'modified_gml_id'
   Sample keys:
      [0] 'DEBW_0010008wid658_part_1'
      [1] 'DEBW_0010008wid658_part_2'
      ...

🏢 TILESET DATA:
   • Meshes scanned: 247
   • Unique buildings: 5005
   • Tileset field: 'gml:id'
   Sample IDs:
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
API Buildings: 5005
Tileset Buildings: 5005
Matching Buildings: 5005 (100.0%)

✅ GOOD: 100.0% match rate!
========================================
```

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action | Where |
|----------|--------|-------|
| **Ctrl+Shift+C** | Detailed building comparison | Anywhere in scene |
| **Ctrl+Shift+R** | Hard refresh API cache | Anywhere in scene |
| **Ctrl+Shift+B** | Toggle count display | Anywhere in scene |

---

## 🚨 Common Issues

### Issue: Tileset count is 0
```
API Total: 5005
Tileset: 0
Match: 0%
```
**Fix:** Wait for tiles to load, then press `Ctrl+Shift+C` to scan

---

### Issue: API count is 0
```
API Total: 0
Tileset: 5005
Match: 0%
```
**Fix:** Check access token, then press `Ctrl+Shift+R` to reload

---

### Issue: Low match rate
```
API Total: 5005
Tileset: 5005
Match: 45%
```
**Fix:** 
1. Check sample IDs in console
2. Verify ID formats match
3. Contact data team if formats differ

---

### Issue: Tileset > API
```
API Total: 500
Tileset: 5005
Match: 10%
```
**Fix:** Tileset updated but cache is old
- Press `Ctrl+Shift+R` to refresh cache

---

## 📊 Interpreting Match Rates

| Match % | Status | Meaning |
|---------|--------|---------|
| **90-100%** | ✅ Excellent | Data is synchronized |
| **70-89%** | ⚠️ Warning | Some ID format issues |
| **50-69%** | 🚨 Problem | Significant mismatch |
| **<50%** | ❌ Critical | Data sources differ |

---

## 🎨 Display Customization

### Component: BuildingCountDisplay

**Inspector Settings:**

**References:**
- `Energy Manager` - Auto-finds BuildingEnergyManager
- `Feature Colorizer` - Auto-finds CesiumFeatureColorizer

**Settings:**
- `Auto Update` ✓ - Updates every second
- `Update Interval` - 1.0 seconds (default)

**Display Options:**
- `Show Total` ✓ - API building count
- `Show With Color` ✓ - Buildings with colors
- `Show Without Color` ☐ - Buildings missing colors
- `Show Last Update` ✓ - Cache timestamp
- `Show Comparison` ✓ - API vs Tileset

**Comparison Settings:**
- `Comparison Update Interval` - 5.0 seconds (default)

---

## 💻 Code Examples

### Get counts in your script:

```csharp
// Get API count
var energyManager = FindObjectOfType<BuildingEnergyManager>();
var stats = energyManager.GetBuildingStatistics();
int apiCount = stats["total"];

// Get tileset count
var colorizer = FindObjectOfType<CesiumFeatureColorizer>();
int tilesetCount = colorizer.GetTilesetBuildingCount();

// Calculate match
float match = (Mathf.Min(apiCount, tilesetCount) * 100f) 
              / Mathf.Max(apiCount, tilesetCount);

Debug.Log($"Match: {match:F1}%");
```

---

## ✅ Quick Test

After setup, verify:

1. Press Play
2. Wait 10 seconds
3. Check counts appear on screen or in console
4. Verify API count > 0
5. Verify Tileset count > 0
6. Verify Match > 90%

If any check fails, see troubleshooting above.

---

## 📚 Full Documentation

See: [BUILDING_COUNT_USAGE_GUIDE.md](BUILDING_COUNT_USAGE_GUIDE.md)

---

**Quick Help:**
- Green = Good ✅
- Yellow = Warning ⚠️
- Red = Problem ❌
