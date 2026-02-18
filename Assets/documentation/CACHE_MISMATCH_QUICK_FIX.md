# 🚨 Cache Mismatch Quick Fix

## Error You See
```
🚨 CRITICAL: 68% of cached buildings are missing from tileset!
This suggests your 3D tileset has been UPDATED but the cache contains OLD data.
```

---

## ✅ One-Click Fix (Windows/Mac/Linux)

```
1. Look at Hierarchy (left panel)
2. Find "BuildingEnergyManager"
3. Right-click on it
4. Select: "Hard Refresh Cache (Clear & Reload)"
5. Wait 30-40 seconds
6. DONE! Buildings should now be colored
```

---

## 🔍 Alternative Diagnostics

### Option 1: Detailed Diagnosis
```
Right-click BuildingEnergyManager
→ "Diagnose API vs Tileset Mismatch"

Shows:
• Exactly how many buildings are missing
• Which buildings (first 10 examples)
• Whether hard refresh is needed
```

### Option 2: Check Cache Health
```
Right-click BuildingEnergyManager  
→ "Validate Cache Integrity"

Shows:
• Data and color mismatches in cache
• Duplicate colors (normal)
• Cache corruption status
```

---

## ⏱️ Timing

| Action | Time |
|--------|------|
| Hard Refresh Cache | 30-40 seconds |
| Clear Cache only | <1 second |
| Subsequent runs | 0.5 seconds |

---

## 🎯 When to Use Each

| Situation | Action |
|-----------|--------|
| See 68% error | Hard Refresh Cache (Clear & Reload) |
| Just checking | Diagnose API vs Tileset Mismatch |
| Cache suspicious | Validate Cache Integrity |
| Want fresh data | Hard Refresh Cache (Clear & Reload) |
| Paranoid | Hard Refresh Cache (Clear & Reload) |

---

## 📍 Where to Find "BuildingEnergyManager"

In Unity Editor:
```
Left Panel → Scene Hierarchy
├── Cesium World Terrain
├── Cesium3DTiles
├── CesiumCamera
└─► BuildingEnergyManager  ← RIGHT-CLICK HERE
```

---

## ✨ What Hard Refresh Does

```
✓ Deletes old cache file from disk
✓ Clears memory
✓ Contacts API for fresh building list
✓ Downloads ~5000 buildings (30-40 sec)
✓ Colors all buildings
✓ Saves to disk for next run
✓ Scene is ready to use

Result: 100% of buildings properly colored
```

---

## 💡 Pro Tips

- **First time?** Hard refresh takes 30-40 sec. Next time will be 0.5 sec (uses disk cache)
- **Tileset updated?** Hard refresh automatically detects and reloads
- **No internet?** Can't hard refresh - needs API connection
- **Still broken?** Check console for error messages or contact support

---

**Don't overthink it:** Just right-click BuildingEnergyManager → Hard Refresh Cache! 🎉
