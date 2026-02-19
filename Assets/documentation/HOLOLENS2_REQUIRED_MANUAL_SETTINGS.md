# HoloLens 2 Required Manual Settings

This file lists **CRITICAL** settings that **MUST** be configured manually in Unity Editor before building for HoloLens 2. The app will **NOT work** without these!

---

## ⚠️ CRITICAL Setting 1: Shader Inclusion

**Why needed:** UWP/IL2CPP build strips unused shaders. Without this, buildings have **NO COLORS**.

**Steps:**
1. Open **Edit → Project Settings → Graphics**
2. Scroll down to **Always Included Shaders**
3. Increase the **Size** by 1
4. Drag `Assets/VertexColoredBuilding.shader` into the new empty slot
   - OR click the small circle picker → search "VertexColoredBuilding" → select it
5. Click anywhere else to save
6. **Verification:** You should see `Cesium/VertexColoredBuilding` in the list

**Status:** ❌ Not automated (Unity API limitation)

---

## ⚠️ CRITICAL Setting 2: Graphics API

**Why needed:** Direct3D12 can cause rendering issues and crashes on HoloLens 2. Direct3D11 is stable and recommended.

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

**Status:** ❌ Not automated (Unity API limitation)

---

## ✅ Setting 3: Main Camera XR Tracking (AUTOMATED)

**Why needed:** Without TrackedPoseDriver, the camera won't follow your head movement on HoloLens — the view will be completely static.

**Current state:** ✅ **Automated** via `Assets/Editor/HoloLensXRCameraSetup.cs`
- Runs automatically when scene loads in Editor
- Adds `TrackedPoseDriver` component to Main Camera
- Configures for `GenericXRDevice` with position + rotation tracking
- Can also run manually: **Tools → HoloLens → Setup Main Camera for XR**

**No action needed** (happens automatically when you open the scene in Unity Editor).

---

## ✅ Setting 4: Cesium Physics Colliders (AUTOMATED)

**Why needed:** Without physics colliders, raycasts can't hit buildings — tapping will do nothing.

**Current state:** ✅ **Automated** via `Assets/Editor/CesiumColliderSetup.cs`
- Runs automatically when scene loads in Editor
- Enables `createPhysicsMeshes` on all Cesium3DTileset objects
- Can also run manually: **Tools → HoloLens → Enable Cesium Colliders**

**No action needed** (happens automatically when you open the scene in Unity Editor).

**IMPORTANT:** After running, **save the scene** (Ctrl+S) to persist the changes!

---

## ✅ Setting 5: XR Auto-Initialization (ALREADY SET)

**Why needed:** OpenXR subsystem must auto-start on HoloLens launch.

**Current state:** ✅ **Already configured** via `Assets/XR/XRGeneralSettingsPerBuildTarget.asset`
- `m_AutomaticLoading: 1`
- `m_AutomaticRunning: 1`

**No action needed.**

---

## ✅ Setting 4: IL2CPP Stripping Prevention (ALREADY SET)

**Why needed:** Prevents code stripping that breaks runtime reflection, coroutines, and FindObjectOfType.

**Current state:** ✅ **Already configured** via `Assets/link.xml`

**No action needed.**

---

## ✅ Setting 5: App Icon (ALREADY AUTOMATED)

**Why needed:** Shows your logo on HoloLens 2 Start menu and taskbar.

**Current state:** ✅ **Automated** via `Assets/Editor/SetHoloLensAppIcon.cs`
- Runs automatically before every UWP build via `IPreprocessBuildWithReport`
- Sets `Assets/app_logo.png` as all UWP tile sizes and splash screen
- Can also run manually: **Tools → Set HoloLens App Icon**

**No action needed.**

---

## ✅ Setting 6: Input Mode (ALREADY SET)

**Current state:** ✅ **Already configured**
- `isXRDevice = true` by default in all 4 scripts (CesiumMetadataReader, BuildingAttributesForm, BuildingCountDisplay, CameraController)
- XR hand ray + head gaze input enabled
- OpenXR hand interaction profile enabled

**No action needed.**

---

## Build Checklist

Before every HoloLens 2 build, verify:

- [x] ✅ `Assets/link.xml` exists
- [x] ✅ XR auto-initialization enabled (check `XRGeneralSettingsPerBuildTarget.asset`)
- [ ] ⚠️ **Shader `Cesium/VertexColoredBuilding` in Always Included Shaders** (Project Settings → Graphics)
- [ ] ⚠️ **Auto Graphics API UNCHECKED, only Direct3D11 in list** (Project Settings → Player → UWP → Other Settings)
- [x] ✅ App icon will be set automatically on build

**Once settings 1 and 2 are done, you never have to do them again** (they persist in project files).

---

## Troubleshooting

### "Buildings have no colors on HoloLens"
→ **Solution:** Add shader to Always Included Shaders (Setting 1 above)

### "App crashes or renders incorrectly on HoloLens"
→ **Solution:** Uncheck Auto Graphics API, use only Direct3D11 (Setting 2 above)

### "Can't tap buildings or see UI panels"
→ Check console for errors. If no errors, the code is already correct — just make sure you've waited for tiles to load fully (can take 30-60 seconds after app launch).

### "Only 3 buildings visible"
→ Wait 60-90 seconds for Cesium to stream in all tiles. The tileset loads progressively. You can also:
1. Select `Cesium3DTileset` GameObject in Hierarchy
2. In Inspector, increase **Maximum Cached Bytes** (default is often too low for large datasets)
3. Increase **Loading Descent Hierarchy** to 10 or higher

---

## Summary

**2 manual steps required:**
1. Add shader to Always Included Shaders
2. Set Graphics API to Direct3D11 only

**Everything else is automated or already configured in the project files.**

After completing the 2 manual steps, you can build directly to HoloLens 2 with full functionality:
- Colored buildings based on energy data
- Tap to view building information panel
- Hold-tap to edit building attributes
- API authentication and data caching
