# Unity Editor Setup Guide - Building Energy System

## Quick Setup Steps

### 1. Connect CesiumMetadataReader to BuildingEnergyManager

**In Unity Editor:**

1. **Find the GameObject with CesiumMetadataReader**
   - Look in the Hierarchy panel (left side)
   - It might be named "EventSystem" or attached to your main camera
   - If you can't find it, create one: `GameObject > Create Empty`, add the script

2. **Select the GameObject**
   - Click on it in the Hierarchy

3. **Look at the Inspector panel** (right side)
   - Find the **CesiumMetadataReader** component
   - You'll see a field called **"Energy Manager"** with a slot that says "None (Building Energy Manager)"

4. **Connect the BuildingEnergyManager**
   - Option A: **Drag and Drop**
     - Find the "BuildingEnergyManager" GameObject in your Hierarchy
     - Drag it into the **Energy Manager** field in the Inspector
   
   - Option B: **Click and Select**
     - Click the small circle icon (⊙) next to the Energy Manager field
     - A window will pop up showing all BuildingEnergyManager objects
     - Double-click "BuildingEnergyManager" to select it

5. **Verify the Connection**
   - The Energy Manager field should now show: "BuildingEnergyManager (Building Energy Manager)"
   - ✅ You're connected!

### 2. Verify BuildingEnergyManager Setup

**Select the BuildingEnergyManager GameObject in Hierarchy**

Check the Inspector shows:
```
API Configuration:
  Api Base Url: https://backend.gisworld-tech.com
  Community Id: 08417008

Authentication (Auto-configured):
  Access Token: (will be filled automatically)

Cesium Configuration:
  Buildings Tileset Name: bisingen
```

**Important:** The Access Token field will be **automatically filled** when you click Play!

---

## What Happens When You Click Play

### Automatic Authentication Flow:

1. **System checks for Access Token**
   - If empty → starts authentication automatically
   - If exists → uses existing token

2. **Authentication Request**
   ```
   POST https://backend.gisworld-tech.com/api/token/
   Body: {"username": "hft_api", "password": "Stegsteg2025"}
   ```

3. **Token Received**
   - System extracts the "access" token from response
   - Stores it in the accessToken field
   - Token is valid for the session

4. **Building Data Loaded**
   - Fetches all building energy data using the token
   - Community ID: 08417008
   - Caches building information and colors

5. **Ready for Clicks!**
   - Click any building to see its energy information

---

## Testing the System

### Step-by-Step Test:

1. **Click Play button** in Unity Editor (top center)

2. **Watch the Console** (bottom panel)
   - You should see:
   ```
   === Starting Authentication ===
   Auth URL: https://backend.gisworld-tech.com/api/token/
   ✓ Authentication successful! Token length: 205
   Token preview: eyJhbGciOiJIUzI1NiIsIn...
   API Request URL: https://backend.gisworld-tech.com/geospatial/buildings-energy/...
   Loaded data for 47 buildings
   ```

3. **Click on any building** in the Game view
   - A black panel appears on the left showing:
     - Building ID
     - Construction year
     - Number of storeys
     - **Energy consumption** (with color indicator)
     - Heating systems (before/after)
     - Windows, walls, roof details

4. **Panel auto-hides after 10 seconds**

---

## Troubleshooting

### Issue: "Authentication failed"
**Check Console for error details:**
- Network connectivity to backend.gisworld-tech.com
- Firewall blocking HTTPS requests

### Issue: "Buildings tileset not found"
**Solution:**
- Verify the Cesium tileset GameObject name contains "bisingen"
- Change `buildingsTilesetName` in Inspector if different

### Issue: Energy Manager field shows "Missing"
**Solution:**
- The GameObject with BuildingEnergyManager was deleted
- Create new GameObject: `GameObject > Create Empty`
- Add Component: BuildingEnergyManager
- Re-connect in CesiumMetadataReader

### Issue: "No CesiumMetadataReader found"
**Solution:**
- Make sure the script is attached to an active GameObject
- If missing, create: `GameObject > Create Empty` → Add CesiumMetadataReader script
- Set Main Camera reference in Inspector

---

## Visual Reference - Inspector Setup

### CesiumMetadataReader (in Inspector):
```
Main Camera: [Main Camera]
Display Duration: 10
Energy Manager: [BuildingEnergyManager] ← MUST BE CONNECTED
```

### BuildingEnergyManager (in Inspector):
```
API Configuration:
  Api Base Url: https://backend.gisworld-tech.com
  Community Id: 08417008

Authentication (Auto-configured):
  Access Token: (auto-filled on Play)

Cesium Configuration:
  Buildings Tileset Name: bisingen
  Enable Cesium Styling: ✓

Color Settings:
  Low Energy Color: Green
  Medium Energy Color: Yellow
  High Energy Color: Red
```

---

## Summary

✅ **Authentication**: Fully automatic - uses hardcoded credentials from UE5
✅ **Community ID**: Set to 08417008 by default
✅ **Token Management**: Auto-fetched on Play, cached during session
✅ **Click Detection**: Works through CesiumMetadataReader → BuildingEnergyManager connection

**All you need to do**: Connect the Energy Manager field in CesiumMetadataReader, then click Play!
