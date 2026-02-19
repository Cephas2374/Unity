# HoloLens 2 Build and Deployment — Technical Report

**Project:** 3D Building Energy Visualization with Cesium for Unity  
**Platform:** Universal Windows Platform (UWP) — HoloLens 2 (ARM64)  
**Engine:** Unity 2022.3.62f3 LTS  
**IDE:** Visual Studio 2022 Community (v17.14)  
**Date:** February 19, 2026  

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Build Pipeline Architecture](#2-build-pipeline-architecture)
3. [Error 1: LNK1112 — Module Machine Type Conflict (x86 vs ARM64)](#3-error-1-lnk1112--module-machine-type-conflict-x86-vs-arm64)
4. [Error 2: MSB3774 — WindowsMobile SDK Not Found](#4-error-2-msb3774--windowsmobile-sdk-not-found)
5. [Error 3: APPX1619 — Invalid Icon Dimensions](#5-error-3-appx1619--invalid-icon-dimensions)
6. [Issue: Cesium Terrain Not Visible in Mixed Reality Capture (MRC)](#6-issue-cesium-terrain-not-visible-in-mixed-reality-capture-mrc)
7. [XR Input Architecture: Tap, Hold-Tap, and Spatial Navigation](#7-xr-input-architecture-tap-hold-tap-and-spatial-navigation)
8. [Unity Configuration Requirements](#8-unity-configuration-requirements)
9. [Visual Studio Configuration Requirements](#9-visual-studio-configuration-requirements)
10. [Complete Build Procedure](#10-complete-build-procedure)
11. [Git Commit History](#11-git-commit-history)
12. [Lessons Learned](#12-lessons-learned)

---

## 1. Project Overview

This project is a 3D geospatial building energy visualization application deployed on Microsoft HoloLens 2. It streams real-world 3D city geometry via Cesium 3D Tiles, colors individual buildings according to their energy efficiency class retrieved from a REST API, and allows users to interact with buildings through hand gestures (air tap and pinch) in augmented reality.

The application stack consists of:

- **Cesium for Unity 1.22.0**: Streams 3D Tiles terrain and building geometry at runtime via `Cesium3DTileset` components.
- **OpenXR 1.14.3** with the Microsoft Hand Interaction Profile: Provides hand tracking, articulated hand rays, and gesture recognition on HoloLens 2.
- **IL2CPP Scripting Backend**: Ahead-of-time (AOT) compilation of C# to C++ for UWP ARM64 deployment.
- **Backend API**: A JWT-authenticated REST service at `https://backend.gisworld-tech.com` providing building energy data for community ID `08417008`.

The complete deployment path is: **Unity C# → IL2CPP C++ generation → Visual Studio ARM64 UWP compilation → `.appx` package → HoloLens 2 sideload**.

---

## 2. Build Pipeline Architecture

Understanding the two-stage build pipeline is essential to diagnosing every error encountered in this project.

### Stage 1: Unity Build (C# → C++ via IL2CPP)

When Unity builds for UWP with the IL2CPP scripting backend, it does not produce a final executable. Instead, it generates:

1. **Il2CppOutputProject/**: A Visual Studio Makefile project containing thousands of auto-generated C++ source files. These are the AOT-compiled equivalents of all C# scripts, Unity engine internals, and referenced packages. In our project, this folder contained **2,664 files totaling 929.67 MB**.
2. **3D_Unity_2022/**: A Visual Studio C++ UWP Application project containing the app entry point (`App.cpp`, `Main.cpp`), the UWP app manifest (`Package.appxmanifest`), app icons, and splash screens. This project links against `GameAssembly.lib` produced by the Il2CppOutputProject.
3. **3D_Unity_2022.sln**: A Visual Studio solution file referencing both projects with build dependency ordering.
4. **UnityCommon.props**: Shared MSBuild property sheet defining the Unity editor installation path.

Unity's build time is typically 60–70 seconds for incremental builds, as it only regenerates IL2CPP output for modified scripts.

### Stage 2: Visual Studio Build (C++ → ARM64 `.appx`)

Visual Studio compiles the generated solution in two phases:

1. **Il2CppOutputProject** invokes `il2cpp.exe`, which in turn calls the Bee build system (`bee_backend.exe`). Bee manages a DAG (directed acyclic graph) of compilation nodes, compiling all IL2CPP-generated C++ files using the MSVC ARM64 cross-compiler (`cl.exe`). Output: `GameAssembly.dll` (65 MB) and `GameAssembly.lib`.
2. **3D_Unity_2022** compiles the UWP app entry point files (`App.cpp`, `Main.cpp`, `pch.cpp`, `UnityGenerated.cpp`) and links them against `GameAssembly.lib` and `WindowsApp.lib`. Output: `3D_Unity_2022.exe`, then packaged into `3D_Unity_2022_1.0.0.0_ARM64.appx`.

A full clean build takes approximately 4–5 minutes, dominated by the Il2CppOutputProject's 787 compilation nodes.

---

## 3. Error 1: LNK1112 — Module Machine Type Conflict (x86 vs ARM64)

### Symptom

After Unity's build succeeded and the Visual Studio solution was opened, building for `Release|ARM64` produced:

```
LINK : fatal error LNK1112: module machine type 'x86' conflicts with target machine type 'ARM64'
```

This error appeared consistently across Release, Master, and MasterWithLTCG configurations. Deleting intermediate build folders (`build\`, `.vs\`) and performing clean rebuilds did not resolve the issue.

### Diagnostic Methodology

#### Step 1: Binary Analysis of Object Files

We examined the COFF headers of the compiled object files to verify their actual machine type. For a standard COFF object file, the machine type is encoded in the first two bytes. For "bigobj" format (used with `/bigobj` flag), the magic bytes `00 00 FF FF` appear at offset 0, and the machine type is at offset 6.

Reading the hex bytes of `build\obj\3D_Unity_2022\ARM64\Release\App.obj`:

```
Offset 0-1: 4C 01
```

The value `0x014C` corresponds to `IMAGE_FILE_MACHINE_I386` — **the x86 architecture**. Despite the file residing in an `ARM64\Release\` directory, the object code was compiled for x86. Every object file in the ARM64 output folders exhibited the same problem:

| File | Directory | Actual Machine Type |
|------|-----------|-------------------|
| App.obj | ARM64\Master | x86 (0x014C) |
| Main.obj | ARM64\Master | x86 (0x014C) |
| pch.obj | ARM64\Master | x86 (0x014C) |
| App.obj | ARM64\Release | x86 (0x014C) |
| Main.obj | ARM64\Release | x86 (0x014C) |
| pch.obj | ARM64\Release | x86 (0x014C) |
| App.obj | x64\Debug | x64 (0x8664) |
| Main.obj | x64\Debug | x64 (0x8664) |

Meanwhile, the `GameAssembly.dll` produced by Il2CppOutputProject was verified as correctly ARM64:

```csharp
// PE header analysis
PE signature offset: [from e_lfanew at bytes 60-63]
Machine type at PE+4: 0xAA64 → ARM64 (AArch64) ✓
```

This confirmed the problem was isolated to the **main UWP application project** (`3D_Unity_2022.vcxproj`), not the Il2CppOutputProject.

#### Step 2: MSBuild Diagnostic Logging

We ran MSBuild from the command line with diagnostic verbosity (`/v:diag`) to trace which C++ compiler was being selected:

```powershell
& "MSBuild.exe" "3D_Unity_2022.vcxproj" /p:Configuration=Release /p:Platform=ARM64 /t:ClCompile /v:diag
```

The output revealed:

```
ClCompilerPath = C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.38.33130\bin\HostX86\arm64...
ExecutablePath = C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.38.33130\bin\HostX86\arm64...
```

MSBuild was selecting MSVC toolset version **14.38.33130** for the ARM64 target. This was the critical discovery.

#### Step 3: Toolchain Directory Enumeration

We enumerated the installed MSVC toolset versions:

```
C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\
├── 14.38.33130\
│   └── bin\
│       ├── Hostx64\
│       │   ├── x64\     ← has cl.exe
│       │   └── x86\     ← has cl.exe
│       └── Hostx86\
│           ├── x64\     ← has cl.exe
│           └── x86\     ← has cl.exe
│           (NO arm64 directory!)
│
└── 14.44.35207\
    └── bin\
        └── Hostx64\
            └── arm64\
                └── cl.exe  ← ARM64 cross-compiler EXISTS here
```

**MSVC 14.38.33130 has no ARM64 cross-compiler.** It only contains x86 and x64 host/target combinations. The ARM64 build tools were installed under MSVC **14.44.35207**.

When MSBuild couldn't find `cl.exe` in `14.38.33130\bin\HostX86\arm64\` (because the directory doesn't exist), it silently fell back to the x86 native compiler at `HostX86\x86\cl.exe`, producing x86 object files despite the ARM64 platform selection.

### Root Cause

The vcxproj specifies `<PlatformToolset>v143</PlatformToolset>`. MSBuild resolves the actual MSVC version for `v143` by reading:

```
C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\Microsoft.VCToolsVersion.v143.default.txt
```

This file contained:

```
14.38.33130
```

While the general default (`Microsoft.VCToolsVersion.default.txt`) pointed to `14.44.35207`, the **v143-specific** default locked to the older version. This is a consequence of how Visual Studio manages side-by-side MSVC toolset installations — the v143 toolset family pins to a specific baseline version that predates ARM64 tool availability.

### Resolution

We added an explicit `<VCToolsVersion>` property to the project's Globals PropertyGroup in `3D_Unity_2022.vcxproj`:

```xml
<PropertyGroup Label="Globals">
    <ProjectGuid>{2fabdf39-a3a3-4497-95ba-5aee089ebf0f}</ProjectGuid>
    <!-- ... other properties ... -->
    <VCToolsVersion>14.44.35207</VCToolsVersion>
</PropertyGroup>
```

This forces MSBuild to use the newer MSVC version that includes the ARM64 cross-compiler at `bin\Hostx64\arm64\cl.exe`.

After applying this fix, all object files compiled with the correct machine type:

| File | Machine Type |
|------|-------------|
| App.obj | ARM64 (0xAA64) ✓ |
| Main.obj | ARM64 (0xAA64) ✓ |
| pch.obj | ARM64 (0xAA64) ✓ |
| UnityGenerated.obj | ARM64 (0xAA64) ✓ |

The LNK1112 linker error was permanently resolved.

### Why Il2CppOutputProject Was Unaffected

The Il2CppOutputProject is configured as a **Makefile** project (`<ConfigurationType>Makefile</ConfigurationType>`). It does not invoke `cl.exe` through MSBuild's standard C++ compilation pipeline. Instead, it runs `il2cpp.exe`, which uses the Bee build system with its own compiler resolution logic that correctly selected the ARM64 cross-compiler. This is why `GameAssembly.dll` was always correctly ARM64 while the main project's objects were x86.

---

## 4. Error 2: MSB3774 — WindowsMobile SDK Not Found

### Symptom

```
error MSB3774: Could not find SDK "WindowsMobile, Version=10.0.26100.0"
```

### Root Cause

The Unity-generated vcxproj contained an `<SDKReference>` entry for `WindowsMobile`:

```xml
<SDKReference Include="WindowsMobile, Version=10.0.26100.0" />
```

The Windows 10 SDK version 10.0.26100.0 does not ship a "WindowsMobile" extension SDK. This is a legacy reference that Unity's UWP build template incorrectly includes for certain SDK versions.

### Resolution

The offending `<SDKReference>` line was removed from the vcxproj file. The UWP application only requires `WindowsApp.lib` (referenced in `<AdditionalDependencies>`) and the standard Windows 10 SDK, both of which were already correctly configured.

---

## 5. Error 3: APPX1619 — Invalid Icon Dimensions

### Symptom

After resolving the linker error, the build progressed past compilation and linking but failed during APPX packaging:

```
Package.appxmanifest: error APPX1619: App manifest references the square 71x71 logo image
'Assets\Square71x71Logo.scale-100.png' which does not have valid dimensions. It must be 71x71 pixels.
```

This error repeated for **30 icon files** across all tile sizes (44x44, 71x71, 150x150, 310x310, 310x150 wide, and 620x300 splash screen) at all scale factors (100%, 125%, 150%, 200%, 400%).

### Root Cause

Unity's UWP build exported all icon PNGs as copies of the same source image without resizing them to the required dimensions. Every file was exactly 58,690 bytes regardless of the target size. The UWP APPX packager strictly validates that each image matches its expected pixel dimensions per the manifest's scale factor specifications.

### Resolution

We programmatically generated all 30 PNG files with correct dimensions using `System.Drawing`:

```powershell
Add-Type -AssemblyName System.Drawing
$bgColor = [System.Drawing.Color]::FromArgb(0, 120, 215)  # Microsoft Blue

# For each icon type and scale factor:
$bmp = New-Object System.Drawing.Bitmap($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear($bgColor)
$bmp.Save($filePath, [System.Drawing.Imaging.ImageFormat]::Png)
```

The required dimensions per UWP tile specification:

| Icon Type | 100% | 125% | 150% | 200% | 400% |
|-----------|------|------|------|------|------|
| Square44x44 | 44×44 | 55×55 | 66×66 | 88×88 | 176×176 |
| Square71x71 | 71×71 | 89×89 | 107×107 | 142×142 | 284×284 |
| Square150x150 | 150×150 | 188×188 | 225×225 | 300×300 | 600×600 |
| Square310x310 | 310×310 | 388×388 | 465×465 | 620×620 | 1240×1240 |
| Wide310x150 | 310×150 | 388×188 | 465×225 | 620×300 | 1240×600 |
| SplashScreen | 620×300 | 775×375 | 930×450 | 1240×600 | 2480×1200 |

After regenerating all icons, the APPX packaging completed successfully.

---

## 6. Issue: Cesium Terrain Not Visible in Mixed Reality Capture (MRC)

### Symptom

When recording video on HoloLens 2 using Mixed Reality Capture, the Cesium 3D Tiles terrain and building geometry were **invisible** in the recorded footage, even though they were clearly visible through the HoloLens optical display during live use.

### Technical Background: How MRC Composites Holograms

HoloLens 2's Mixed Reality Capture system works fundamentally differently from the optical display:

- **Optical display**: The HoloLens waveguide projects light directly into the user's eyes. It only uses the **RGB channels** of the rendered frame. Alpha is irrelevant — any non-black pixel is visible as a hologram overlaid on the real world.
- **MRC video recording**: The system captures the PV (Photo/Video) camera feed of the real world and composites holographic content on top. To determine which pixels are "holographic" (should appear in the video) versus "empty" (should show the real world), MRC reads the **alpha channel** of the rendered frame. Pixels with `alpha = 0` are treated as transparent (no hologram), and pixels with `alpha > 0` are composited onto the camera feed proportionally.

### Root Cause Analysis

We examined two shader paths in the rendering pipeline:

**1. Custom Building Shader (`Cesium/VertexColoredBuilding`):**

```hlsl
void surf (Input IN, inout SurfaceOutputStandard o)
{
    fixed4 finalColor = lerp(baseColor, IN.color, _UseVertexColor);
    o.Albedo = finalColor.rgb;
    o.Alpha = finalColor.a;  // Alpha from vertex color
}
```

The `CesiumFeatureColorizer.cs` explicitly sets `color.a = 1.0f` for all vertex colors. Buildings colored by the energy class system should appear in MRC recordings correctly.

**2. Cesium Built-in Shaders (`CesiumDefaultTilesetShader`, `CesiumUnlitTilesetShader`):**

These are shaders provided by the Cesium for Unity package. They render terrain tiles, uncolored buildings, and base map imagery. While they produce correct RGB output for the display, their opaque render pass writes `alpha = 0` to the framebuffer. This is standard behavior for opaque 3D objects in many rendering pipelines — alpha is often unused for opaque geometry. However, on HoloLens, this causes MRC to interpret all Cesium terrain as "no hologram present."

### Resolution: The `ForceOpaqueAlpha` Post-Processing Component

We created a camera post-processing script that forces `alpha = 1.0` across the entire framebuffer after all opaque rendering completes:

```csharp
[RequireComponent(typeof(Camera))]
public class ForceOpaqueAlpha : MonoBehaviour
{
    private Material forceAlphaMat;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        // Pass through RGB unchanged
        Graphics.Blit(src, dest);

        // Force alpha = 1 via a fullscreen quad writing only to the alpha channel
        GL.PushMatrix();
        GL.LoadOrtho();

        var prevRT = RenderTexture.active;
        RenderTexture.active = dest;

        GL.Begin(GL.QUADS);
        GL.Color(new Color(0, 0, 0, 1));  // Only alpha matters
        GL.Vertex3(0, 0, 0);
        GL.Vertex3(1, 0, 0);
        GL.Vertex3(1, 1, 0);
        GL.Vertex3(0, 1, 0);
        GL.End();

        RenderTexture.active = prevRT;
        GL.PopMatrix();
    }
}
```

**How it works:**

1. `OnRenderImage` is a Unity callback invoked after the camera finishes rendering the scene into a `RenderTexture`.
2. `Graphics.Blit(src, dest)` copies the fully rendered frame (RGB + alpha) to the destination buffer without modification.
3. A fullscreen quad is drawn using immediate-mode GL, with `GL.Color(0, 0, 0, 1)`. Since only the alpha channel carries the value `1`, and the quad covers the entire viewport, this overwrites the alpha channel of every pixel to `1.0` while leaving RGB untouched.
4. MRC now sees every rendered pixel as "fully opaque hologram" and composites it into the video recording.

**Caveats:**

- This approach makes **all** rendered pixels opaque to MRC, including the clear color / skybox. If the camera clear color has `alpha = 0` (the default for HoloLens), the skybox will also become opaque in recordings. For AR applications, the camera should use a solid clear color with alpha = 0, and a more selective alpha-writing approach may be needed (e.g., depth-based masking). However, for this building visualization where the entire 3D tileset should be visible, the fullscreen approach is sufficient.

The `ForceOpaqueAlpha` component is auto-attached to the Main Camera via the `HoloLensXRCameraSetup` Editor script (see Section 8).

---

## 7. XR Input Architecture: Tap, Hold-Tap, and Spatial Navigation

### Gesture Detection via OpenXR InputDevices API

The `CesiumMetadataReader.cs` script implements a state-machine-based gesture handler in `HandleXRInput()`:

```
State Machine:
  IDLE → [selectPressed] → HOLDING (timer starts)
  HOLDING → [timer >= 0.5s] → HOLD_PROCESSED (edit form opens)
  HOLDING → [selectReleased before 0.5s] → TAP (info panel opens)
  HOLD_PROCESSED → [selectReleased] → IDLE
```

The select state is sampled every frame from all connected XR input devices:

```csharp
bool GetXRSelectState()
{
    var inputDevices = new List<InputDevice>();
    InputDevices.GetDevices(inputDevices);

    foreach (var device in inputDevices)
    {
        // HoloLens 2 air tap / hand pinch → primaryButton
        if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool value) && value)
            return true;

        // Fallback: triggerButton
        if (device.TryGetFeatureValue(CommonUsages.triggerButton, out value) && value)
            return true;

        // Fallback: analog trigger axis > 0.5
        if (device.TryGetFeatureValue(CommonUsages.trigger, out float axis) && axis > 0.5f)
            return true;
    }
    return false;
}
```

On HoloLens 2 with OpenXR and the Microsoft Hand Interaction Profile, the air tap (pinching thumb and index finger together) maps to `primaryButton`. The three-layer fallback (primaryButton → triggerButton → trigger axis) ensures compatibility with both articulated hand tracking and motion controllers.

### Ray Casting: Hand Ray with Head Gaze Fallback

When a gesture is detected, a ray must be cast into the scene to determine which building the user is pointing at. The `GetXRRay()` method implements a priority-ordered ray source:

1. **Right hand aim ray**: Queries devices matching `InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller` for `devicePosition` and `deviceRotation`. On HoloLens 2, this corresponds to the articulated hand ray projected from the user's right hand.
2. **Left hand aim ray**: Same query for left-handed devices, used if the right hand is not tracked.
3. **Hand tracking devices**: Queries `InputDeviceCharacteristics.HandTracking` directly.
4. **Head gaze fallback**: Uses `Camera.main.transform.position` and `Camera.main.transform.forward` — the user looks at a building and taps.

### Spatial Navigation (Walking)

Head tracking is handled by the `TrackedPoseDriver` component on the Main Camera:

- **Device Type**: `GenericXRDevice`
- **Tracked Pose**: `Center` (head center)
- **Tracking Type**: `RotationAndPosition` (6DOF)
- **Update Type**: `UpdateAndBeforeRender` (low-latency updates)

The `CameraController.cs` desktop script (WASD + mouse) auto-disables itself when XR is detected (`isXRDevice = true` and `disableOnXR = true`), preventing desktop input from conflicting with spatial tracking.

### UI Presentation in AR

Both the info panel and edit form use **WorldSpace** canvases (not ScreenSpaceOverlay, which is invisible on HoloLens). When a building interaction occurs, `PositionCanvasInFrontOfCamera()` places the UI 1.5 meters in front of the user, facing them:

```csharp
void PositionCanvasInFrontOfCamera(GameObject canvasObj)
{
    Vector3 forward = mainCamera.transform.forward;
    forward.y = 0;  // Keep canvas upright
    forward.Normalize();

    canvasObj.transform.position = mainCamera.transform.position + forward * 1.5f;
    canvasObj.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
}
```

---

## 8. Unity Configuration Requirements

The following settings must be configured in Unity before building for HoloLens 2:

### Build Settings
| Setting | Value | Reason |
|---------|-------|--------|
| Target Platform | Universal Windows Platform | HoloLens 2 runs UWP |
| Target Device | HoloLens | Limits to HoloLens build profile |
| Architecture | ARM64 | HoloLens 2 uses Qualcomm Snapdragon 850 (ARM64) |
| Build Type | D3D Project | Direct3D rendering backend |

### Player Settings
| Setting | Value | Reason |
|---------|-------|--------|
| Scripting Backend | IL2CPP | Required for UWP ARM64 |
| API Compatibility | .NET Standard 2.1 | Broadest compatibility |
| Graphics APIs | Direct3D11 only | HoloLens 2 supports D3D11; remove D3D12 and Vulkan |
| Active Input Handling | Both | Required for OpenXR + legacy Input API fallback |
| C++ Compiler Configuration | Release | Optimized builds |
| IL2CPP Code Generation | Faster runtime | Performance optimization |
| Capabilities | InternetClient, WebCam, Microphone, SpatialPerception | API access, MRC, spatial mapping |

### XR Plug-in Management
| Setting | Value |
|---------|-------|
| Universal Windows Platform → OpenXR | Enabled |
| Auto-load XR on Startup | Enabled |
| Microsoft Hand Interaction Profile | Added |

### Automated Editor Scripts

Two `[InitializeOnLoad]` scripts run automatically when the Unity Editor loads:

1. **`HoloLensXRCameraSetup.cs`**: Adds `TrackedPoseDriver` and `ForceOpaqueAlpha` to the Main Camera if missing.
2. **`CesiumColliderSetup.cs`**: Enables `createPhysicsMeshes` on all `Cesium3DTileset` components (required for ray cast hit detection on buildings).

### Code Preservation

`link.xml` prevents IL2CPP from stripping critical assemblies:

```xml
<linker>
    <assembly fullname="Assembly-CSharp" preserve="all"/>
    <assembly fullname="CesiumForUnity" preserve="all"/>
    <assembly fullname="Unity.XR.OpenXR" preserve="all"/>
    <assembly fullname="Newtonsoft.Json" preserve="all"/>
</linker>
```

---

## 9. Visual Studio Configuration Requirements

### Required Visual Studio Components

The following components must be installed via Visual Studio Installer → Modify → Individual Components:

| Component | Required For |
|-----------|-------------|
| MSVC v143 - VS 2022 C++ ARM64 build tools (Latest) | ARM64 cross-compilation |
| C++ Universal Windows Platform support for v143 build tools (ARM64) | UWP ARM64 libraries |
| Windows 10 SDK (10.0.26100.0) | UWP API headers and libraries |
| Universal Windows Platform development workload | UWP project system |

**Critical**: If only the base v143 toolset is installed (MSVC 14.38), the ARM64 cross-compiler will be absent, and MSBuild will silently fall back to x86 compilation (see Section 3).

### vcxproj Modifications

The `VCToolsVersion` property must be explicitly set to the MSVC version containing ARM64 tools:

```xml
<PropertyGroup Label="Globals">
    <VCToolsVersion>14.44.35207</VCToolsVersion>
</PropertyGroup>
```

### Build Configuration

| Setting | Value |
|---------|-------|
| Solution Configuration | Release |
| Solution Platform | ARM64 |
| Signing | Disabled (`/p:AppxPackageSigningEnabled=false`) for development |

---

## 10. Complete Build Procedure

### Step 1: Unity Build

1. Open Unity 2022.3.62f3
2. Verify scene: Main Camera has `TrackedPoseDriver` + `ForceOpaqueAlpha`; `Cesium3DTileset` has `createPhysicsMeshes = true`
3. File → Build Settings → UWP, ARM64, D3D Project
4. Click **Build**, select `Builds_2022_V2` folder
5. Wait ~70 seconds for IL2CPP generation

### Step 2: Visual Studio Build (via Command Line)

```powershell
cd "D:\3D_Unity_2022\Builds_2022_V2"

# Clean previous artifacts
Remove-Item "build" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "AppPackages" -Recurse -Force -ErrorAction SilentlyContinue

# Build using 64-bit MSBuild
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" `
    "3D_Unity_2022.sln" `
    /p:Configuration=Release `
    /p:Platform=ARM64 `
    /p:AppxPackageSigningEnabled=false `
    /t:Build `
    /m `
    /v:minimal
```

Expected output:
```
Build succeeded with 787 successful nodes and 0 failed ones   ← Il2CppOutputProject
3D_Unity_2022.vcxproj -> ...\build\bin\ARM64\Release\3D_Unity_2022.exe
3D_Unity_2022 -> ...\AppPackages\...\3D_Unity_2022_1.0.0.0_ARM64.appx
```

### Step 3: Deploy to HoloLens 2

**Option A — Visual Studio Deploy:**
1. Open `3D_Unity_2022.sln` in Visual Studio
2. Set Release | ARM64
3. Right-click `3D_Unity_2022` project → Deploy
4. Device must be connected via USB or same network

**Option B — Device Portal Sideload:**
1. Navigate to `Builds_2022_V2\AppPackages\3D_Unity_2022\3D_Unity_2022_1.0.0.0_ARM64_Test\`
2. Upload `3D_Unity_2022_1.0.0.0_ARM64.appx` via HoloLens Device Portal (Apps → Deploy)

---

## 11. Git Commit History

The following commits document the complete evolution of HoloLens 2 support:

| Commit | Description |
|--------|-------------|
| `96ba4b9` | Add `link.xml` to prevent IL2CPP code stripping |
| `08f286a` | Rewrite input handling for HoloLens 2 (OpenXR) |
| `9326822` | WorldSpace canvases, XR hand ray, auto-token refresh, XR auto-init |
| `91127dd` | Set `isXRDevice=true` by default, fix GC handle warnings |
| `ed4f84e` | Auto-set `app_logo.png` as HoloLens app icon |
| `fa6c0d6` | Fix 401 auth deadlock: reset `isAuthenticating` before retry |
| `ab5939f` | Fix info panel size and enable rich text |
| `dc8d212` | Restore WorldSpace canvas + manual settings documentation |
| `108e1d0` | Auto-fix: `TrackedPoseDriver` + Cesium colliders (Editor scripts) |
| `19057ee` | Add comprehensive pre-deployment checklist |
| `b254e49` | Enable real-time mode, 60-second polling interval |
| `3a84e33` | Fix: Force alpha=1 for HoloLens MRC video capture |
| `68e3690` | Remove unused `shaderCode` field (CS0414 warning cleanup) |

---

## 12. Lessons Learned

### 1. MSVC Toolset Version Pinning Causes Silent Architecture Fallback

The most insidious error in this project — LNK1112 — was caused by MSBuild silently falling back to an x86 compiler when the ARM64 compiler wasn't found in the resolved MSVC toolset version. There was **no warning** during compilation; the error only surfaced at link time. The lesson is that when targeting ARM64 with UWP, always verify the actual compiler path using `/v:diag` logging and explicitly pin `<VCToolsVersion>` to a version known to include ARM64 tools.

### 2. Unity's UWP Build Template Has Edge Cases

Two separate issues (WindowsMobile SDK reference and incorrect icon dimensions) originated from Unity's UWP project generation. These are not Unity bugs per se, but template assumptions that don't hold for all Windows SDK versions. Always validate the generated vcxproj before spending time debugging Visual Studio errors.

### 3. HoloLens MRC Alpha Channel Is Non-Obvious

The Cesium terrain being invisible in MRC recordings but visible on the display is deeply counterintuitive. The optical display and MRC pipeline use fundamentally different compositing strategies (additive light projection vs. alpha-based overlay). Any shader that writes `alpha = 0` for opaque geometry will be invisible in MRC. This is not documented prominently in most HoloLens development guides and requires understanding the MRC compositing pipeline at the framebuffer level.

### 4. Two-Stage Build Pipeline Requires Independent Verification

The Il2CppOutputProject and the main UWP project use completely different compilation pipelines (Bee + il2cpp.exe vs. MSBuild + cl.exe). An error in one does not imply an error in the other. Binary analysis (reading COFF headers) was essential to pinpointing which stage produced incorrect output.

---

*Document generated from build session logs, binary analysis, and MSBuild diagnostic traces.*
