# HoloLens 2 — Bug Fixes & Features (Session 2026-02-21)

All changes committed to `main` branch, commits `3b1b838` through `1189aee`.

---

## 1. MRC Video Recording — Flashing & Crash Fix

**Commit:** `3b1b838`  
**Files Changed:**
- `Assets/ForceAlphaOnly.shader` (NEW)
- `Assets/ForceOpaqueAlpha.cs` (REWRITTEN)
- `Assets/Editor/CesiumShaderProtection.cs` (UPDATED)

**Problem:**  
When starting Mixed Reality Capture (MRC) video recording on HoloLens 2, the app would flash violently and then crash, killing both the app and the recording.

**Root Cause (3 bugs in old `ForceOpaqueAlpha.cs`):**

| Bug | Effect |
|-----|--------|
| `GL.Color(0,0,0,1)` drew a **black quad** over the entire screen | Overwrote RGB channels (not just alpha) — screen goes black |
| MRC detection toggled on/off every 1 second via `Camera.allCameras.Length` | Black quad flashed on and off repeatedly |
| Double render pass (`Graphics.Blit` + GL immediate mode quad) | GPU overload on HoloLens 2's Adreno 630 → app crash |

**Fix:**
- Created `ForceAlphaOnly.shader` — a stereo-aware shader that copies RGB untouched and only sets alpha=1.0 in a single pass. Supports HoloLens 2 single-pass instanced rendering.
- Rewrote `ForceOpaqueAlpha.cs` — now uses a single `Graphics.Blit(src, dest, material)` call, always active. Safe because HoloLens 2's additive see-through display ignores the alpha channel entirely — only MRC uses it for compositing.
- No more MRC detection toggling, GL immediate mode, or double rendering.
- Added `Hidden/ForceAlphaOnly` to `CesiumShaderProtection.cs` to survive IL2CPP shader stripping.

---

## 2. Volumetric Cloud Sky — Black Sky Fix & Cloud Implementation

**Commits:** `c3f3a50`, `3947795`  
**Files Changed:**
- `Assets/CesiumSkyWithClouds.shader` (NEW, then REWRITTEN)
- `Assets/Editor/CesiumSkySetup.cs` (NEW)
- `Assets/Editor/HoloLensXRCameraSetup.cs` (UPDATED)
- `Assets/Editor/CesiumShaderProtection.cs` (UPDATED)

**Problem:**  
The sky was completely black after setting camera ClearFlags to SolidColor with transparent black (a previous fix for AR see-through on HoloLens 2). The user wanted a realistic volumetric cloud sky similar to UE5's VolumetricCloudComponent.

**Fix — Phase 1 (`c3f3a50`):**
- Changed camera ClearFlags back to `Skybox` mode in `HoloLensXRCameraSetup.cs`
- Created initial `CesiumSkyWithClouds.shader` with procedural sky and clouds
- Created `CesiumSkySetup.cs` editor script to auto-create material and assign as skybox

**Fix — Phase 2 (`3947795`) — Complete shader rewrite for realistic cumulus clouds:**

The initial shader produced flat, tiled, uniform cloud patterns. Rewrote with:

| Old Shader | New Shader |
|---|---|
| Hash-based value noise (blocky, grid-visible) | **Gradient (Perlin) noise** with quintic interpolation |
| 3 octaves only | **5-octave FBM** for shape + **3-octave FBM** for detail |
| No cell structure | **Worley (cellular) noise** for puffy cumulus bubbles |
| Uniform coverage everywhere | **Domain warping** for natural cloud clusters |
| No self-shadowing | **Beer's law shadow sampling** — darker bases, brighter tops |
| Simple ambient lighting | 3-color lighting: direct sun + ambient + silver-lining edge glow |
| Speed 0.02 | **Speed 0.006** — slow, natural drift |

Additional features:
- 3-stop sky gradient (zenith → mid → horizon) for richer blue
- Atmospheric haze near horizon
- Cloud ambient color for realistic mid-tone shading
- Top brightness parameter — cloud tops catch more light
- Smoothstep opacity for natural cloud buildup

---

## 3. Magenta/Pink Screen Fix

**Commit:** `91e4ebb`  
**Files Changed:**
- `Assets/ForceAlphaOnly.shader` (FIXED)

**Problem:**  
Entire screen turned magenta/pink when pressing Play in Unity Editor.

**Root Cause:**  
Typo in `ForceAlphaOnly.shader` vertex function: referenced `v.texcoord` but the `appdata` struct field was named `uv`. This caused a shader compilation error, and since `ForceOpaqueAlpha.cs` blits every frame through this shader via `OnRenderImage`, the compile failure turned the entire screen magenta (Unity's "broken shader" indicator).

**Fix:**  
One-character fix: `v.texcoord` → `v.uv` (line 56).

---

## 4. Clouds Blocking Terrain View

**Commits:** `7240e28`, `0a69652`  
**Files Changed:**
- `Assets/CesiumSkyWithClouds.shader` (UPDATED)
- `Assets/Editor/CesiumSkySetup.cs` (UPDATED)

**Problem:**  
Clouds rendered too low in the skybox, filling the entire forward view near the horizon. Navigation moved through clouds instead of through the city. Buildings and terrain were obscured.

**Root Cause:**  
Cloud altitude and minimum elevation angle were too low. The old material also cached stale property values because the setup script skipped recreation if the material already existed.

**Fix (two iterations):**

| Parameter | Original | Final | Effect |
|---|---|---|---|
| `_CloudAltitude` | 0.12 | **1.5** | Cloud plane pushed very high in the sky |
| `_CloudMinElev` | 0.003 (hardcoded) | **0.35** (new property) | Clouds only render when looking 20+ degrees above horizon |
| `_CloudCoverage` | 0.42 | **0.30** | Less cloud, more blue sky gaps |
| `_CloudScale` | 8 | **6** | Larger individual clouds, fewer of them |
| `_CloudOpacity` | 0.92 | **0.85** | Slightly more transparent |

- `CesiumSkySetup.cs` now **always recreates** the material to ensure shader default values are applied correctly (no more stale cached materials).

---

## 5. CameraController Always Disabling in Editor

**Commit:** `b81f307`  
**Files Changed:**
- `Assets/CameraController.cs` (FIXED)

**Problem:**  
The Camera Controller component always unchecked itself (disabled) when pressing Play in the Unity Editor, preventing WASD keyboard navigation.

**Root Cause:**  
```csharp
private bool isXRDevice = true;  // hardcoded, NEVER changes
```
`isXRDevice` was hardcoded to `true` with no actual XR detection. Combined with `disableOnXR = true`, the controller disabled itself in `Start()` even on desktop.

**Fix:**  
Added `using UnityEngine.XR;` and replaced hardcoded value with actual detection:
```csharp
isXRDevice = XRSettings.isDeviceActive;
```
- **Desktop/Editor:** `false` → controller stays enabled, WASD works
- **HoloLens 2:** `true` → controller disables itself (head tracking takes over)

---

## 6. UWP Build Failure — Hidden/Internal-Colored Shader

**Commit:** `4556150`  
**Files Changed:**
- `Assets/Editor/CesiumShaderProtection.cs` (FIXED)

**Problem:**  
Unity build failed with:
```
An asset is marked with HideFlags.DontSave but is included in the build:
Asset name: Hidden/Internal-Colored
Building - Failed to write file: Library/PlayerDataCache/WindowsStoreApps/Data/Resources/unity_builtin_extra
```

**Root Cause:**  
`CesiumShaderProtection.cs` added `Hidden/Internal-Colored` to `AlwaysIncludedShaders`, but this is an internal Unity shader with `HideFlags.DontSave` that cannot be serialized into UWP builds. It was originally added for the old `ForceOpaqueAlpha` GL immediate mode code, which had already been replaced by the custom `Hidden/ForceAlphaOnly` shader.

**Fix:**
- Removed `Hidden/Internal-Colored` from the required shaders list
- Added cleanup code that actively scans `AlwaysIncludedShaders` and removes any forbidden `Hidden/Internal-*` entries from previous runs

---

## 7. UWP Build Failure — Icon Dimension Errors

**Commit:** `1189aee`  
**Files Changed:**
- `Assets/UWPIcons/*.png` (35 files REGENERATED)
- Build output icons regenerated in-place

**Problem:**  
APPX packaging failed with 7 `APPX1619`/`APPX3207` errors — icon PNGs had wrong pixel dimensions.

**Root Cause:**  
Icon generation used `int()` truncation instead of `math.ceil()` for the 125% scale factor. Example: `71 × 1.25 = 88.75` was truncated to 88px, but UWP requires 89px (ceiling). Also, `Square310x310Logo.scale-400.png` was 232KB, exceeding the 204.8KB APPX size limit.

| Icon | Had | Required |
|---|---|---|
| Square71x71Logo.scale-125 | 88×88 | **89×89** |
| Square71x71Logo.scale-150 | 106×106 | **107×107** |
| Square150x150Logo.scale-125 | 187×187 | **188×188** |
| Wide310x150Logo.scale-125 | 387×187 | **388×188** |
| Square310x310Logo.scale-125 | 387×387 | **388×388** |
| Square310x310Logo.scale-400 | 232KB | **<204.8KB** |
| StoreLogo.scale-125 | 62×62 | **63×63** |

**Fix:**  
Regenerated all 35 icon PNGs using `math.ceil(base × scale / 100)` for correct dimensions. Compressed large icons using palette optimization to stay under the 204.8KB limit.

---

## Summary

| # | Fix | Commit | Impact |
|---|---|---|---|
| 1 | MRC flashing & crash | `3b1b838` | Video recording works without flashing or crashing |
| 2 | Volumetric cloud sky | `c3f3a50`, `3947795` | Realistic UE5-style cumulus clouds replace black sky |
| 3 | Magenta screen | `91e4ebb` | Shader typo fix — app renders correctly |
| 4 | Clouds blocking view | `7240e28`, `0a69652` | Clouds only appear high in sky, terrain/buildings visible |
| 5 | Camera controller disabling | `b81f307` | WASD navigation works in Unity Editor |
| 6 | Hidden shader build error | `4556150` | UWP build no longer fails on shader serialization |
| 7 | Icon dimension errors | `1189aee` | APPX packaging succeeds with correct icon sizes |

**Final build output:**  
`D:\3D_Unity_2022\Builds_2022_V2\AppPackages\3D_Unity_2022\3D_Unity_2022_1.0.0.0_ARM64_Test\3D_Unity_2022_1.0.0.0_ARM64.appx`
