# HoloLens 2 Build Fixes — Diagnosis & Solutions

## Why the App Didn't Work on HoloLens 2

When a Unity project runs perfectly in the Editor but fails on HoloLens 2, it's almost never a single issue — it's a combination of three independent systems all breaking at once. In our case, the app appeared "stuck on one view" with no interactivity and no colors. This document explains **why** each problem occurred and **how** each fix addresses it.

---

## 1. Code Stripping Was Silently Removing Runtime Logic

### The Problem

Unity uses IL2CPP (Intermediate Language to C++) to compile C# code for UWP/HoloLens builds. As part of this process, it runs a **managed code linker** that analyzes the compiled assemblies and strips out any code it believes is unreachable. This is an optimization meant to reduce the final binary size — but it has a critical blind spot.

The linker performs **static analysis only**. It traces method calls starting from known entry points and removes anything it can't find a path to. The problem is that Unity and Cesium rely heavily on patterns that are invisible to static analysis:

- **`GetComponent<T>()`** — Unity resolves these at runtime by type name. The linker sees a generic method call but cannot determine which concrete types will be requested, so it may strip the target MonoBehaviour entirely.
- **`FindObjectOfType<T>()`** — Same issue. Our scripts use this 6+ times to discover `BuildingEnergyManager`, `CesiumFeatureColorizer`, `BuildingAttributesForm`, and `Canvas` components at startup.
- **`StartCoroutine()`** — Coroutine methods are stored as `IEnumerator` state machines. If the linker doesn't see a direct call chain to the method that *creates* the coroutine, it may strip the generated state machine class.
- **Cesium runtime tile loading** — Cesium for Unity loads 3D Tiles at runtime and attaches `CesiumPrimitiveFeatures`, `CesiumModelMetadata`, and `CesiumPropertyTable` components dynamically. The linker has no way to know these types are needed because they don't appear in any scene file.
- **Callback delegates / `Invoke()`** — Our `BuildingEnergyManager.FetchBasicAttributes()` uses `Action<T>` callbacks (`onSuccess?.Invoke(attributesData)`). If the linker determines the delegate is never assigned, it strips the target method.

The result is that on HoloLens, methods exist in the source code but are simply **absent from the binary**. No error is thrown — the call silently does nothing, or `GetComponent` returns null where it shouldn't.

### The Fix: `Assets/link.xml`

We created a `link.xml` file in the Assets folder. This is a Unity-recognized configuration file that instructs the IL2CPP linker to **preserve specific assemblies regardless of static analysis**. Unity automatically discovers any `link.xml` files in the Assets directory during the build process — no manual configuration is required.

The file preserves:

| Assembly | Why It Needs Protection |
|----------|----------------------|
| `Assembly-CSharp` | All project scripts — prevents stripping of MonoBehaviours found via `GetComponent`/`FindObjectOfType` |
| `CesiumForUnity` / `CesiumForUnityNative` | Runtime tile loading, metadata reading, feature ID extraction — all dynamically instantiated |
| `UnityEngine.PhysicsModule` | Raycasting for building selection (`Physics.Raycast`) |
| `UnityEngine.UI` | All UI elements created programmatically (Canvas, Text, Image, Button, ScrollRect) |
| `UnityEngine.InputLegacyModule` | `Input.GetMouseButtonDown()` and related calls on desktop fallback path |
| `UnityEngine.UnityWebRequestModule` | HTTP requests to the energy data API |
| `UnityEngine.XRModule` | XR device detection and input device enumeration |
| `Unity.XR.OpenXR` / `Unity.XR.Management` | OpenXR runtime for HoloLens 2 hand tracking |
| `Newtonsoft.Json` | JSON parsing used by both Cesium and our API communication |
| `System` / `System.Core` / `mscorlib` | Reflection, LINQ, delegates, async patterns |

The `preserve="all"` attribute tells the linker to keep every type and method in the assembly, not just the ones it can statically trace. This is a deliberate trade-off: the binary will be slightly larger, but nothing will be silently broken.

**Important**: The only `link.xml` that previously existed was inside `Library/PackageCache/com.unity.nuget.newtonsoft-json/`, which only protected Newtonsoft.Json. Our project scripts and Cesium assemblies had zero protection.

---

## 2. Input System Was Entirely Desktop-Only

### The Problem

This was the most impactful issue. Every single interactive behavior in the project was written using Unity's legacy desktop input API:

- **`Input.GetMouseButtonDown(0)`** — Used in `CesiumMetadataReader` for building selection, in `BuildingAttributesForm` for closing the form by clicking outside it
- **`Input.mousePosition`** — Used to create the raycast ray for building clicks and to check if clicks land on UI elements
- **`Input.GetKey(KeyCode.LeftControl)`** — Used to distinguish between "view data" (left click) and "open edit form" (Ctrl+click)
- **`Input.GetKey(KeyCode.W/A/S/D)`** — Camera movement in `CameraController`
- **`Input.GetAxis("Mouse X/Y")`** — Camera rotation via mouse drag
- **`Input.GetKey(KeyCode.LeftControl + LeftShift + ...)` combinations** — Keyboard shortcuts for cache clearing, building counting, and display toggling

**None of these inputs exist on HoloLens 2.** The device has no mouse, no keyboard, and no physical buttons. HoloLens 2 input comes from:

1. **Hand tracking** — The device tracks the user's hands in 3D space
2. **Air tap / Pinch gesture** — The primary "click" equivalent, mapped as a button press through OpenXR
3. **Head gaze** — The direction the user is looking, represented by the camera's forward vector
4. **Hand ray** — A ray projecting from the user's hand toward what they're pointing at

The fundamental mismatch is that `Input.GetMouseButtonDown(0)` will **never return true** on HoloLens 2 because there is no mouse device. Similarly, `Input.mousePosition` returns an undefined value because there is no screen cursor. The app wasn't "broken" on HoloLens — it was faithfully executing the input checks every frame, and every check was returning false.

### The Fix: XR-Aware Input in Each Script

#### CesiumMetadataReader.cs — The Core Interaction Script

This script handles all building selection. The rewrite addresses two separate problems:

**Problem A: Detecting the "click" gesture.**
The old `HandleXRInput()` method attempted to use `UnityEngine.InputSystem.Mouse.current` — which is the New Input System's mouse device. On HoloLens 2 with OpenXR, there is no mouse device, so `Mouse.current` is null and the null-conditional operator (`?.`) causes every check to return false.

The new implementation uses `UnityEngine.XR.InputDevices.GetDevices()` to enumerate all connected XR input devices and reads their button states directly:

```csharp
// HoloLens 2 air tap / hand pinch → primaryButton
device.TryGetFeatureValue(CommonUsages.primaryButton, out value)

// Fallback: trigger button
device.TryGetFeatureValue(CommonUsages.triggerButton, out value)

// Fallback: analog trigger axis > 0.5
device.TryGetFeatureValue(CommonUsages.trigger, out triggerAxis)
```

On HoloLens 2, the OpenXR runtime maps the air tap and pinch gestures to `primaryButton`. The trigger fallbacks ensure compatibility with other XR controllers. Edge detection (tracking `wasXRSelectPressed`) ensures we only fire once per gesture, not every frame the button is held.

**Problem B: Creating the raycast ray.**
The old code always used `mainCamera.ScreenPointToRay(Input.mousePosition)`. On HoloLens, `Input.mousePosition` is meaningless. The new code creates a ray from the camera's world position along its forward direction — this is the **head gaze ray**, which represents where the user is looking:

```csharp
ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
```

This means on HoloLens 2, the user looks at a building (centering it in their view) and air taps to select it. This is the standard interaction pattern for HoloLens applications.

**Interaction model on HoloLens 2:**
- Quick air tap → shows energy data panel (equivalent to left click on desktop)
- Air tap and hold for 0.5+ seconds → opens edit form (equivalent to Ctrl+click on desktop)

The hold duration is configurable via the `holdDuration` field in the Inspector (default: 0.5 seconds). The hold-to-edit pattern replaces the keyboard modifier (Ctrl) which doesn't exist on HoloLens.

#### BuildingAttributesForm.cs — Form Close-on-Outside-Click

The form previously closed when the user clicked outside it by checking `Input.GetMouseButtonDown(0)` and comparing `Input.mousePosition` against the form's `RectTransform`. On HoloLens:

- The click detection now uses the same `XR.InputDevices` pattern to detect air taps
- The "pointer position" uses screen center (`Screen.width/2, Screen.height/2`) instead of mouse position, since the gaze cursor is always at the center of the HoloLens display
- The F5 keyboard shortcut for form refresh is wrapped in a platform check — it only compiles for desktop builds

#### CameraController.cs — No Changes Needed

This script already had proper XR detection built in. On HoloLens 2, it detects `UNITY_WSA` at startup and calls `this.enabled = false`, completely disabling desktop camera controls. On HoloLens, the camera is controlled by the head tracking system (the camera follows the user's head position and rotation automatically).

#### Keyboard Shortcut Scripts (CesiumFeatureColorizer, BuildingEnergyManager, BuildingCountDisplay)

These three scripts each had a keyboard shortcut in their `Update()` method:

| Script | Shortcut | Action |
|--------|----------|--------|
| CesiumFeatureColorizer | Ctrl+Shift+C | Count buildings and show statistics |
| BuildingEnergyManager | Ctrl+Shift+Delete | Clear the persistent data cache |
| BuildingCountDisplay | Ctrl+Shift+B | Toggle building count overlay |

Since HoloLens 2 has no keyboard, these shortcuts are wrapped in `#if !UNITY_WSA && !WINDOWS_UWP` preprocessor directives. This means:

- On **desktop** (Unity Editor, standalone builds): shortcuts work exactly as before
- On **UWP** (HoloLens 2): the keyboard input code is completely excluded from compilation, avoiding unnecessary `Input.GetKey()` calls that would never return true

The underlying public methods (`CountBuildingsAndShowStats()`, `ClearPersistentCache()`, `ToggleDisplay()`) remain accessible. If you add UI buttons in the future (which is the standard approach for HoloLens actions), they can call these methods directly.

---

## 3. Remaining Manual Steps in Unity Editor

The following fixes cannot be applied from code and must be configured in the Unity Editor before rebuilding:

### Graphics API (Player Settings > Other Settings)

**What to change:** Uncheck "Auto Graphics API" and ensure only **Direct3D11** is in the list. Remove Direct3D12 if present.

**Why:** HoloLens 2 runs Windows Holographic, which supports Direct3D11 for rendering. When "Auto" is checked, Unity may select Direct3D12 for the UWP build, which is not fully supported on HoloLens 2 and can cause rendering failures — blank screens, missing geometry, or incorrect shading.

### Always Included Shaders (Project Settings > Graphics)

**What to change:** Add the `Cesium/VertexColoredBuilding` shader to the "Always Included Shaders" list. Also add the `Standard` shader as a safety net.

**Why:** The `VertexColoredBuilding` shader is what gives buildings their energy-based colors. It applies vertex colors written by `CesiumFeatureColorizer` at runtime. The UWP/IL2CPP build pipeline aggressively strips shaders it considers unused. Because Cesium creates meshes at runtime and assigns materials dynamically, Unity's build analyzer never sees a direct reference to this shader in any scene or prefab — so it strips it. The result is either pink/magenta buildings (Unity's "missing shader" color) or completely invisible geometry.

The `Assets/Resources/` folder, which could serve as an alternative location for shader preservation, is currently empty.

### Managed Stripping Level (Player Settings > Other Settings)

**Current setting:** Low (value = 1). This is acceptable. For maximum safety, you can change it to **Minimal**, which performs the least aggressive stripping.

---

## Summary of All Changes

| Fix | Type | File(s) | Impact |
|-----|------|---------|--------|
| Prevent code stripping | New file | `Assets/link.xml` | Preserves all project/Cesium/Unity assemblies during IL2CPP build |
| XR building selection | Code rewrite | `Assets/CesiumMetadataReader.cs` | Air tap selects buildings via head gaze ray; hold opens edit form |
| XR form close handling | Code rewrite | `Assets/BuildingAttributesForm.cs` | Air tap outside form closes it; uses gaze point instead of mouse |
| Guard keyboard shortcuts | Code edit | `Assets/CesiumFeatureColorizer.cs` | Ctrl+Shift+C compiles out on UWP |
| Guard keyboard shortcuts | Code edit | `Assets/BuildingEnergyManager.cs` | Ctrl+Shift+Delete compiles out on UWP |
| Guard keyboard shortcuts | Code edit | `Assets/BuildingCountDisplay.cs` | Ctrl+Shift+B compiles out on UWP |
| Graphics API | Manual setting | Unity Editor | Set Direct3D11 only for UWP |
| Shader inclusion | Manual setting | Unity Editor | Add VertexColoredBuilding to Always Included Shaders |

### Desktop Compatibility

All changes are backward-compatible. The scripts detect the platform at startup and choose the correct input path:
- **Desktop / Unity Editor**: Mouse and keyboard input works identically to before
- **HoloLens 2 / UWP**: XR input (air tap, gaze) is used instead

The `forceDesktopInput` checkbox in `CesiumMetadataReader`'s Inspector allows you to force desktop input mode even when XR is detected — useful for testing in the Editor with XR Preview mode.
