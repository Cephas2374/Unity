# Building Energy System - Quick Setup Checklist

## ✅ Setup Steps

### 1. API Configuration (CRITICAL - Required for coloring to work)

- [ ] **Get API Credentials**
  - [ ] Obtain Bearer Token from backend administrator
  - [ ] Get your Community ID
  - [ ] Test API access with Postman/curl (optional but recommended)

- [ ] **Configure BuildingEnergyManager**
  - [ ] Open Unity scene with Cesium tileset
  - [ ] Create empty GameObject named "BuildingEnergyManager"
  - [ ] Add `BuildingEnergyManager` script component
  - [ ] Fill in Inspector fields:
    - API Base Url: `https://backend.gisworld-tech.com`
    - Access Token: `[YOUR_TOKEN_HERE]`
    - Community Id: `[YOUR_ID_HERE]`
    - Buildings Tileset Name: `bisingen`

### 2. Test API Connection

- [ ] **Force Reload Data**
  - [ ] Select BuildingEnergyManager GameObject
  - [ ] Right-click script component → "Force Reload Data"
  - [ ] Check Console for: "Loaded data for X buildings"
  - [ ] If error: verify token and community ID

### 3. Test Building Coloring

- [ ] **Apply Colors**
  - [ ] Right-click BuildingEnergyManager → "Test Color System"
  - [ ] Observe buildings in Game view
  - [ ] Expected: Green (low), Yellow (medium), Red (high energy)
  - [ ] If no color change: check tileset name, verify tiles loaded

### 4. Setup CesiumMetadataReader (Optional but Recommended)

- [ ] **Verify Existing Setup**
  - [ ] Find GameObject with CesiumMetadataReader script
  - [ ] In Inspector, assign BuildingEnergyManager reference
  - [ ] Test: Click building in Game view → see metadata panel

### 5. Create UI Panels (Future Work)

- [ ] **BuildingInfoPanel**
  - [ ] Create Canvas GameObject
  - [ ] Add BuildingInfoPanel script
  - [ ] Create Text and Button UI elements
  - [ ] Connect to BuildingEnergyManager

- [ ] **BuildingAttributesForm** (Renovation Form)
  - [ ] Create form panel with dropdowns
  - [ ] Implement save/close functionality
  - [ ] Connect to API PUT endpoint

---

## 🔍 Verification Checklist

### API Working?
- [ ] Console shows: "API Request URL: https://backend.gisworld-tech.com/..."
- [ ] Console shows: "API Response received: X characters"
- [ ] Console shows: "Loaded data for X buildings" (X > 0)
- [ ] No "Failed to preload building data" errors

### Colors Working?
- [ ] Console shows: "Applied Cesium styling with X building colors"
- [ ] Buildings visible in Game view
- [ ] Buildings change color when "Test Color System" is run
- [ ] Colors match energy values: Green < 50, Yellow 50-150, Red > 150

### Metadata Reading Working?
- [ ] Click building → Console shows "Building Metadata"
- [ ] Console shows "GML ID: DENW19AL..."
- [ ] Metadata panel appears on screen (if UI setup)

---

## 🚨 Common Issues & Quick Fixes

| Issue | Quick Fix |
|-------|----------|
| "No access token available" | Fill in Access Token field in Inspector |
| "Failed to preload building data" | Check token validity, verify community ID |
| "Buildings tileset not found" | Verify tileset GameObject name matches "bisingen" |
| Buildings not changing color | Ensure tileset is loaded (tiles visible), try "Apply Test Colors" |
| "Response Code: 401" | Token expired or invalid - get new token |
| "Response Code: 403" | Community ID doesn't match your permissions |
| Click detection not working | Ensure Camera reference is set in CesiumMetadataReader |

---

## 📊 Expected Results

### Console Output (Success)
```
API Request URL: https://backend.gisworld-tech.com/geospatial/buildings-energy/?community_id=123&field_type=basic
API Response received: 15234 characters
Loaded data for 47 buildings
Found buildings tileset: bisingen
Applied Cesium styling with 47 building colors
```

### Game View
- Buildings visible in 3D
- Buildings colored based on energy:
  - **Green**: Low energy (0-50 kWh/year)
  - **Yellow**: Medium energy (50-150 kWh/year)
  - **Red**: High energy (>150 kWh/year)
  - **Gray**: No data or default

### Click Interaction
1. Enter Play mode
2. Click any building
3. Console shows: "Building Metadata" + "GML ID: ..."
4. (Future) Info panel appears with building details

---

## 🎯 Priority Order

### Phase 1: API & Colors (CURRENT)
1. ✅ Get API credentials
2. ✅ Configure BuildingEnergyManager
3. ✅ Test data loading
4. ✅ Test building coloring

### Phase 2: UI Panels
5. Create BuildingInfoPanel UI
6. Test click → display info workflow

### Phase 3: Renovation Form
7. Create BuildingAttributesForm with dropdowns
8. Implement API save functionality
9. Add real-time sync (optional)

---

## 📝 Configuration Values Reference

### Required (Must Fill)
```
Access Token: [Get from backend admin]
Community Id: [Your community identifier]
```

### Default (Pre-configured)
```
API Base Url: https://backend.gisworld-tech.com
Buildings Tileset Name: bisingen
Enable Cesium Styling: ✓
Low Energy Color: Green (0, 255, 0)
Medium Energy Color: Yellow (255, 255, 0)
High Energy Color: Red (255, 0, 0)
```

### Optional (Advanced)
```
Enable Real Time Sync: ☐ (enable for 2-second polling)
Sync Interval: 2.0 seconds
```

---

## 📖 Documentation Files

- [API_CONFIGURATION_GUIDE.md](API_CONFIGURATION_GUIDE.md) - Complete API reference
- [BuildingEnergyManager.cs](BuildingEnergyManager.cs) - Main script
- [CesiumMetadataReader.cs](CesiumMetadataReader.cs) - Click detection script

---

## 🔄 Next Session Goals

After completing API setup and coloring:
1. Test with actual API credentials
2. Verify buildings are colored correctly
3. Document any API response discrepancies
4. Create BuildingInfoPanel UI in Unity Editor
5. Begin BuildingAttributesForm implementation

---

**Status:** Ready for API credentials → Test → Deploy UI panels
