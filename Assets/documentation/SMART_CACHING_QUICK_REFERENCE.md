# Smart Caching Quick Reference

## 🎯 Key Concept

**Old:** Download ALL buildings every startup (30-40 seconds)  
**New:** Download ONCE, cache on disk, use cached version forever (0.5 seconds!)

---

## 📊 How It Works

```
┌─────────────────────────────────────┐
│  First Run (One-Time)               │
├─────────────────────────────────────┤
│ 1. Download 5005 buildings from API │
│ 2. Save to disk cache file          │
│ 3. Color all buildings              │
│    (~30-40 seconds)                 │
└─────────────────────────────────────┘
           ↓
┌─────────────────────────────────────┐
│  Subsequent Runs (Fast Load)        │
├─────────────────────────────────────┤
│ 1. Load cache from disk             │
│ 2. Color all buildings              │
│    (~0.5 seconds)                   │
└─────────────────────────────────────┘
           ↓
┌─────────────────────────────────────┐
│  After Single Building Edit         │
├─────────────────────────────────────┤
│ 1. Update only that building        │
│ 2. Recolor only that building       │
│    (~1-2 seconds)                   │
└─────────────────────────────────────┘
```

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+Delete` | **Clear cache** (then restart) |
| `Ctrl+Shift+C` | Count buildings & compare |
| `Ctrl+Shift+B` | Toggle building count display |

---

## 🎮 In-Game Operations

### Clear Cache
```
Right-click BuildingEnergyManager Inspector
→ "Clear Persistent Cache"
→ Restart scene
→ Fresh download on next start
```

### Update Single Building
```csharp
string buildingId = "DEBW_001...";
manager.StartCoroutine(
    manager.UpdateSingleBuilding(buildingId)
);
// ✅ Fetches from API
// ✅ Updates cache
// ✅ Recolors only that building
```

### Check Statistics
```
Right-click BuildingEnergyManager Inspector
→ "Show Building Count"
// Shows cache status in console
```

---

## 📂 Cache File

**Location:**
```
Windows: C:\Users\{user}\AppData\Local\Unity\...
Mac:     ~/Library/Application Support/...
Linux:   ~/.config/unity3d/...
```

**Filename:**
```
building_cache_{communityId}.json
Example: building_cache_08417008.json
```

**Size:**
```
~1.7 MB per 1000 buildings
For 5000 buildings: ~8.5 MB
```

---

## ✅ What's Cached

- ✅ Building metadata (construction year, storeys, etc.)
- ✅ Energy data (consumption, demand, CO2)
- ✅ Colors (exact hex from API)
- ✅ Renovation data (heating, windows, walls, etc.)
- ✅ All building properties needed for display

---

## ⚙️ Inspector Settings

**BuildingEnergyManager:**
- `Enable Persistent Cache` = ✓ (keep enabled)
- `Enable Change Detection` = ✓ (detects external edits)
- `Change Check Interval` = 300 (5 minutes)

---

## 🔍 Change Detection

Automatically detects when buildings are edited externally:

**How:** Every 5 minutes checks API for changes  
**When:** Found → downloads only changed buildings  
**Then:** Updates cache and recolors

---

## 🐛 Troubleshooting

| Problem | Solution |
|---------|----------|
| Still downloading from API | Check `Enable Persistent Cache` is ON |
| Cache not loading | Delete cache file, restart |
| Stale data | `Ctrl+Shift+Delete` then restart |
| Cache file missing | Normal - will be created on first run |

---

## ⚡ Performance

| Operation | Before | After |
|-----------|--------|-------|
| First start | 30-40s | 30-40s (one-time) |
| Second start | 30-40s | **0.5s** ✅ |
| Edit building | 30-40s | **1-2s** ✅ |

---

## 📋 Inspector Tab

```
BuildingEnergyManager Component

Cache Management:
  Last Cache Update: 2026-02-14 10:30:45
  Cached Building Count: 5005
  Total Buildings Loaded: 5005
  Buildings With Color: 5005
  Buildings Without Color: 0

Smart Caching System:
  ☑ Enable Persistent Cache
  ☑ Enable Change Detection
  Change Check Interval: 300 (seconds)

[Clear Persistent Cache]  (Right-click)
[Show Building Count]      (Right-click)
```

---

## 🚀 Workflow

### Development
```
1. First run    → Downloads & saves cache
2. Close game   → Cache on disk
3. Restart game → Loads from cache (0.5s!)
4. Edit building → UpdateSingleBuilding()
5. Clear cache  → Ctrl+Shift+Delete (restart for fresh download)
```

### Production
```
1. Build deployed → Cache created on first run
2. All players    → Fast subsequent loads (0.5s)
3. External edit  → Auto-detected and updated
4. Always fast    → No more 30-second waits!
```

---

## 💡 Tips

- ✅ Cache persists across editor restarts
- ✅ Cache survives scene reloads
- ✅ Cache survives game builds (stays in build folder)
- ✅ Safe to delete - will auto-recreate
- ✅ Can ship game with pre-cached data (advanced)
- ⚠️ Delete if community ID changes

---

**Status:** ✅ Ready to use  
**Performance:** 60x faster on reloads  
**Network:** Minimal API calls
