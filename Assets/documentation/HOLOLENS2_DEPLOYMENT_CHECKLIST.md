# HoloLens 2 Pre-Deployment Checklist ✅

**Last Updated:** 2026-02-19  
**Purpose:** Comprehensive verification of all HoloLens 2 settings before deployment

---

## 🎯 QUICK STATUS OVERVIEW

| Category | Status | Notes |
|----------|--------|-------|
| **XR Defaults** | ✅ **READY** | All scripts default to `isXRDevice = true` |
| **Input System** | ✅ **READY** | XR hand gestures implemented (tap + hold) |
| **UI Canvases** | ✅ **READY** | WorldSpace mode on HoloLens |
| **Camera Tracking** | ✅ **AUTO-FIXED** | TrackedPoseDriver auto-added on scene load |
| **Physics Colliders** | ✅ **AUTO-FIXED** | Cesium colliders auto-enabled |
| **XR Auto-Init** | ✅ **READY** | OpenXR auto-starts on HoloLens |
| **Manual Settings** | ⚠️ **REQUIRED** | 2 settings must be done manually |

---

## 📋 AUTOMATED CONFIGURATIONS (Already Done)

### ✅ 1. XR Device Defaults
**Status:** ✅ **ALL SCRIPTS CONFIGURED**

All scripts default to `isXRDevice = true` for direct HoloLens deployment:

- **[CesiumMetadataReader.cs](../CesiumMetadataReader.cs)** Line 65
  ```csharp
  private bool isXRDevice = true;
  ```
  - ✅ Tap gesture: View building data
  - ✅ Hold gesture (0.5s): Open edit form
  - ✅ Hand ray + head gaze fallback
  - ✅ WorldSpace canvas (1920x1080 @ 0.0004 scale)

- **[BuildingAttributesForm.cs](../BuildingAttributesForm.cs)** Line 70
  ```csharp
  private bool isXRDevice = true;
  ```
  - ✅ WorldSpace form canvas
  - ✅ Dropdown template positioning adjusted
  - ✅ XR-friendly UI controls

- **[CameraController.cs](../CameraController.cs)** Line 34
  ```csharp
  private bool isXRDevice = true;
  ```
  - ✅ Desktop controls **DISABLED** on HoloLens
  - ✅ Head tracking active (via TrackedPoseDriver)

- **[BuildingCountDisplay.cs](../BuildingCountDisplay.cs)** Line 33
  ```csharp
  private bool isXRDevice = true;
  ```
  - ✅ WorldSpace UI positioning

- **[BuildingEnergyManager.cs](../BuildingEnergyManager.cs)**
  - ℹ️ No XR-specific code needed (API data management only)

---

### ✅ 2. Input System - XR Gestures

**Status:** ✅ **FULLY IMPLEMENTED**

**Location:** [CesiumMetadataReader.cs](../CesiumMetadataReader.cs) Lines 318-395

**Implemented Gestures:**
- ✅ **Quick Tap** (Air Tap / Pinch): View building info panel
  - Uses `InputDevices.GetDevices()` with `primaryButton` and `triggerButton`
  - Triggers `HandleBuildingClick(false)`

- ✅ **Hold Gesture** (0.5+ seconds): Open building attributes edit form
  - Hold duration: `public float holdDuration = 0.5f` (Line 38)
  - Triggers `HandleBuildingClick(true)`

**Ray Casting System:**
- ✅ Hand ray detection (right hand → left hand → hand tracking devices)
- ✅ Head gaze fallback if hands not detected
- ✅ Uses `devicePosition` and `deviceRotation` characteristics

**Verification:**
```csharp
// CesiumMetadataReader.cs - HandleXRInput()
bool GetXRSelectState()
{
    var inputDevices = new List<UnityEngine.XR.InputDevice>();
    UnityEngine.XR.InputDevices.GetDevices(inputDevices);
    
    foreach (var device in inputDevices)
    {
        // HoloLens 2 air tap / hand pinch → primaryButton
        if (device.TryGetFeatureValue(CommonUsages.primaryButton, out value) && value)
            return true;
        
        // Trigger button (alternative mapping)
        if (device.TryGetFeatureValue(CommonUsages.triggerButton, out value) && value)
            return true;
    }
    return false;
}
```

---

### ✅ 3. UI Canvas Configuration

**Status:** ✅ **AUTO-CONFIGURED FOR HOLOLENS**

**Info Panel Canvas** ([CesiumMetadataReader.cs](../CesiumMetadataReader.cs) Lines 165-210):
```csharp
if (isXRDevice)
{
    // HoloLens 2: WorldSpace canvas (ScreenSpaceOverlay is invisible on AR/VR)
    canvas.renderMode = RenderMode.WorldSpace;
    
    // Set size and scale for proper visibility
    RectTransform canvasRect = canvas.GetComponent<RectTransform>();
    canvasRect.sizeDelta = new Vector2(1920, 1080);
    canvas.transform.localScale = Vector3.one * 0.0004f;
    
    // Position 1.5m in front of user, upright
    PositionCanvasInFrontOfCamera(metadataPanel);
}
```

**Edit Form Canvas** ([BuildingAttributesForm.cs](../BuildingAttributesForm.cs) Lines 242-249):
```csharp
if (isXRDevice && mainCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
{
    mainCanvas.renderMode = RenderMode.WorldSpace;
    RectTransform canvasRect = mainCanvas.GetComponent<RectTransform>();
    canvasRect.sizeDelta = new Vector2(1200, 900);
    mainCanvas.transform.localScale = Vector3.one * 0.001f;
    PositionCanvasInFrontOfCamera(mainCanvas.gameObject);
}
```

**Why WorldSpace?**
- ScreenSpaceOverlay canvases are **INVISIBLE** on HoloLens 2 AR/VR mode
- WorldSpace canvases render as 3D objects in the scene
- Positioned 1.5m in front of user with `y=0` constraint (upright)

---

### ✅ 4. Main Camera XR Tracking (AUTO-FIXED)

**Status:** ✅ **AUTOMATED VIA EDITOR SCRIPT**

**Auto-Fix Script:** [Assets/Editor/HoloLensXRCameraSetup.cs](../Editor/HoloLensXRCameraSetup.cs)

**What It Does:**
- Runs automatically when Unity Editor loads the scene (`[InitializeOnLoad]`)
- Finds Main Camera (`Camera.main`)
- Adds `TrackedPoseDriver` component if missing
- Configures:
  - Device Type: `GenericXRDevice`
  - Tracked Pose: `Center` (head center)
  - Tracking Type: `RotationAndPosition` (6DOF)
  - Update Type: `UpdateAndBeforeRender`

**Manual Trigger:** `Tools → HoloLens → Setup Main Camera for XR`

**Verification:**
1. Open Unity Editor (script auto-runs)
2. Check Console for: `✅ Added TrackedPoseDriver to Main Camera (DynamicCamera)`
3. Save scene (Ctrl+S)
4. Verify in Inspector: DynamicCamera → TrackedPoseDriver component exists

**Why Critical?**
- **Without TrackedPoseDriver, the camera NEVER moves** - completely static view on HoloLens
- OpenXR writes head position/rotation to TrackedPoseDriver
- This was the root cause of the "stuck view" issue

---

### ✅ 5. Cesium Physics Colliders (AUTO-FIXED)

**Status:** ✅ **AUTOMATED VIA EDITOR SCRIPT**

**Auto-Fix Script:** [Assets/Editor/CesiumColliderSetup.cs](../Editor/CesiumColliderSetup.cs)

**What It Does:**
- Runs automatically when Unity Editor loads the scene
- Finds all `Cesium3DTileset` components
- Enables `createPhysicsMeshes = true` on each

**Manual Trigger:** `Tools → HoloLens → Enable Cesium Colliders`

**Verification:**
1. Open Unity Editor (script auto-runs)
2. Check Console for: `✅ Enabled physics colliders on Cesium3DTileset (bisingen)`
3. Save scene (Ctrl+S)

**Why Critical?**
- **Without physics colliders, raycasts cannot hit buildings**
- Tapping/clicking does nothing because `Physics.Raycast()` misses
- Required for both tap and hold-tap gestures

---

### ✅ 6. XR Auto-Initialization

**Status:** ✅ **ALREADY CONFIGURED**

**Configuration File:** [Assets/XR/XRGeneralSettingsPerBuildTarget.asset](../XR/XRGeneralSettingsPerBuildTarget.asset)

**Metro (UWP) Settings:**
```yaml
Metro Providers:
  m_AutomaticLoading: 1    # ✅ Enabled
  m_AutomaticRunning: 1    # ✅ Enabled
  m_Loaders:
    - OpenXR Loader
```

**What This Does:**
- OpenXR subsystem starts automatically on HoloLens app launch
- No manual initialization code needed
- XR input devices available immediately

---

### ✅ 7. IL2CPP Code Preservation

**Status:** ✅ **ALREADY CONFIGURED**

**Configuration File:** [Assets/link.xml](../link.xml)

**Preserves:**
- ✅ `Assembly-CSharp` (all your scripts)
- ✅ `CesiumForUnity` (3D Tiles runtime)
- ✅ `UnityEngine.XRModule`
- ✅ `Unity.XR.OpenXR`
- ✅ `UnityEngine.SpatialTracking` (TrackedPoseDriver)
- ✅ `Newtonsoft.Json` (API responses)

**Why Critical?**
- IL2CPP scripting backend strips unused code during UWP build
- Without `link.xml`, XR code and JSON parsing would be removed
- Results in crashes or missing functionality on HoloLens

---

### ✅ 8. Authentication & API Integration

**Status:** ✅ **FULLY FUNCTIONAL**

**Script:** [BuildingEnergyManager.cs](../BuildingEnergyManager.cs)

**Features:**
- ✅ JWT token authentication (username: `hft_api`)
- ✅ Auto-retry on 401 errors (token refresh)
- ✅ Auth deadlock fix (commit fa6c0d6) - resets `isAuthenticating` flag before retry
- ✅ Building data caching with persistent disk storage
- ✅ Real-time polling for external edits (5-second interval)

**Default Configuration:**
```csharp
public string apiBaseUrl = "https://backend.gisworld-tech.com";
public string communityId = "08417008";
public float changeCheckInterval = 5f; // 5 seconds for real-time updates
```

---

## ⚠️ MANUAL SETTINGS REQUIRED

These **CANNOT** be automated and **MUST** be done manually in Unity Editor before deployment.

### ❌ 1. Shader Inclusion (CRITICAL for Building Colors)

**Why Needed:** Custom shaders are stripped from builds unless explicitly included.

**Steps:**
1. Open **Edit → Project Settings → Graphics**
2. Scroll to **Always Included Shaders** section
3. Increase **Size** by 1
4. In the new slot, click the circle icon to browse
5. Search for and select: **`Cesium/VertexColoredBuilding`**
6. Close Project Settings (auto-saves)

**Verification:**
- Look for `Cesium/VertexColoredBuilding` in the Always Included Shaders list
- If missing, buildings will be **gray/white** on HoloLens (no colors)

**File Location:** Shader is at [Assets/VertexColoredBuilding.shader](../VertexColoredBuilding.shader)

---

### ❌ 2. Graphics API Configuration (CRITICAL for Stability)

**Why Needed:** Direct3D 12 causes rendering issues and crashes on HoloLens 2. Direct3D 11 is stable.

**Steps:**
1. Open **Edit → Project Settings → Player**
2. Select the **UWP tab** (Windows icon with store bag)
3. Expand **Other Settings** section
4. Find **Auto Graphics API for Windows Store**
5. **UNCHECK** the Auto Graphics API checkbox
6. The **Graphics APIs** list below will become editable
7. If you see `Direct3D12`, select it and click the **`-`** (minus) button to remove it
8. If `Direct3D11` is not in the list, click **`+`** and add it
9. **Final state:** Only `Direct3D11` should be in the list

**Verification:**
- Graphics APIs list should show: `Direct3D11` **only**
- No `Direct3D12` present

---

## 🔍 FINAL VERIFICATION STEPS

Before building for HoloLens 2, verify ALL of these:

### Pre-Build Checklist:

- [ ] **1. Open Unity Editor** - Auto-fix scripts have run (check Console for green checkmarks)
- [ ] **2. Save Scene** - Press Ctrl+S to save auto-added components
- [ ] **3. Verify Main Camera:**
  - [ ] Inspect `DynamicCamera` GameObject
  - [ ] Has `TrackedPoseDriver` component
  - [ ] Device: Generic XR Device, Pose: Center
- [ ] **4. Verify Cesium Tileset:**
  - [ ] Inspect `bisingen` GameObject
  - [ ] `Cesium3DTileset` component has `Create Physics Meshes` checked
- [ ] **5. Manual Setting 1 - Shader:**
  - [ ] Project Settings → Graphics → Always Included Shaders
  - [ ] Contains `Cesium/VertexColoredBuilding`
- [ ] **6. Manual Setting 2 - Graphics API:**
  - [ ] Project Settings → Player → UWP → Other Settings
  - [ ] Auto Graphics API: **UNCHECKED**
  - [ ] Graphics APIs: **Direct3D11 only**
- [ ] **7. XR Settings:**
  - [ ] Project Settings → XR Plug-in Management
  - [ ] UWP tab has **OpenXR** checked
  - [ ] OpenXR → Interaction Profiles → Microsoft Hand Interaction Profile **enabled**
- [ ] **8. Build Settings:**
  - [ ] Platform: **Universal Windows Platform**
  - [ ] Target Device: **HoloLens**
  - [ ] Architecture: **ARM64**
  - [ ] Build Type: **D3D Project**
  - [ ] Scripting Backend: **IL2CPP**
  - [ ] Scenes: `Assets/Scenes/final.unity` included

---

## 🚀 BUILD & DEPLOY

Once all checks pass:

1. **Build UWP Project:**
   - File → Build Settings → Build (or Ctrl+Shift+B)
   - Select output folder (e.g., `Builds/HoloLens2`)

2. **Open in Visual Studio:**
   - Navigate to `Builds/HoloLens2`
   - Open `3D_Unity_2022.sln`

3. **Deploy to HoloLens 2:**
   - Select `ARM64` architecture
   - Select `Release` configuration
   - Select deployment target: `Device` or `Remote Machine`
   - Debug → Start Without Debugging (Ctrl+F5)

---

## 🎮 EXPECTED HOLOLENS 2 FUNCTIONALITY

After deployment, the app should have:

### ✅ Navigation:
- **Head Tracking:** View moves with your head (6DOF position + rotation)
- **Gaze Cursor:** Look around to aim hand ray

### ✅ Building Interaction:
- **Quick Tap (Air Tap / Pinch):** View building info panel
  - Shows: gml_id, construction year, heating system, energy data
  - Panel appears in front of you (WorldSpace)
  - Auto-hides after 10 seconds
  
- **Hold Tap (0.5+ seconds):** Open building attributes edit form
  - Shows: All building properties with dropdown selectors
  - Edit any field
  - Click "Save Changes" to send PUT request to API
  - Form positioned in front of you

### ✅ Visual Features:
- **Building Colors:** Each building colored by energy efficiency
  - API provides RGB hex colors (e.g., `#FF5733`)
  - Applied via vertex colors using custom shader
  
- **Selection Highlight:** Selected building glows with emission

### ✅ API Integration:
- **Auto-Authentication:** JWT token fetched on startup
- **Auto-Retry:** 401 errors trigger token refresh
- **Real-Time Updates:** Polls API every 5 seconds for external edits
- **Persistent Cache:** Building data saved to disk, survives app restarts

---

## 🐛 TROUBLESHOOTING

### Issue: View is completely static, can't move head
**Solution:** TrackedPoseDriver missing or not configured
- Run auto-fix: Tools → HoloLens → Setup Main Camera for XR
- Save scene and rebuild

### Issue: Can't tap buildings, nothing happens
**Solutions:**
1. **Physics colliders missing:**
   - Run auto-fix: Tools → HoloLens → Enable Cesium Colliders
   - Save scene and rebuild

2. **Hand interaction profile not enabled:**
   - Project Settings → XR Plug-in Management → OpenXR
   - OpenXR Settings → Interaction Profiles
   - Enable "Microsoft Hand Interaction Profile"

### Issue: Buildings are gray/white, no colors
**Solutions:**
1. **Shader not included:**
   - Project Settings → Graphics → Always Included Shaders
   - Add `Cesium/VertexColoredBuilding`

2. **Tiles not loaded yet:**
   - Wait 5-10 seconds for Cesium tiles to stream
   - Colors apply when tiles finish loading

### Issue: UI panels invisible
**Solution:** Canvas in wrong render mode
- Canvases should auto-switch to WorldSpace if `isXRDevice = true`
- Check CesiumMetadataReader line 65: `isXRDevice` should be `true`

### Issue: App crashes on startup
**Solutions:**
1. **Direct3D 12 crash:**
   - Project Settings → Player → UWP → Graphics APIs
   - Remove Direct3D12, keep only Direct3D11

2. **Code stripping:**
   - Verify `Assets/link.xml` exists and has all required assemblies

---

## 📝 SUMMARY

**Total Settings:** 10 configurations  
**Automated:** 8 (80%) ✅  
**Manual Required:** 2 (20%) ⚠️

**Manual Settings:**
1. Add shader to Always Included Shaders
2. Set Graphics API to Direct3D11 only

**Auto-Configured (No Action Needed):**
1. XR device defaults (`isXRDevice = true`)
2. XR gesture input (tap, hold-tap)
3. WorldSpace UI canvases
4. TrackedPoseDriver on Main Camera
5. Cesium physics colliders
6. XR auto-initialization
7. IL2CPP code preservation
8. API authentication & caching

---

**Document Version:** 1.0  
**Last Verified:** 2026-02-19  
**Unity Version:** 2022.3.62f3  
**OpenXR Version:** 1.14.3  
**Cesium Version:** 1.22.0
