# HoloLens 2 Export and Deployment - Complete Technical Guide

## Executive Summary

This document details the complete process of exporting a Unity 2022 application (Building Energy Visualization System) to HoloLens 2. It covers the entire workflow from initial configuration through Visual Studio deployment, including all technical challenges encountered and their solutions.

**Project Details:**
- **Platform:** HoloLens 2 (ARM64 architecture)
- **Development Environment:** Unity 2022.3.62f3
- **Build System:** Visual Studio 2022 Community Edition + MSBuild
- **Target Architecture:** ARM64 (HoloLens 2 native)
- **XR Framework:** OpenXR (recommended for HoloLens 2)

---

## Part 1: Pre-Export Prerequisites

### System Requirements Verification

Before beginning HoloLens 2 export, verify your system has:

1. **Unity 2022 with HoloLens Support**
   - Editor version: 2022.3.62f3 or later
   - Contains MetroSupport package with HoloLens 2 templates
   - Location: `C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Data\PlaybackEngines\MetroSupport`

2. **Visual Studio 2022 Community Edition**
   - With C++ Development Tools (Desktop Development with C++)
   - MSBuild toolchain (v17.14.40 or later)
   - Windows SDK 10.0.22621.0 or 10.0.26100.0
   - Location: `C:\Program Files\Microsoft Visual Studio\2022\Community`

3. **Windows SDKs**
   - Extension SDKs for HoloLens (WindowsMobile SDK)
   - Universal Windows Platform SDK
   - Location: `C:\Program Files (x86)\Windows Kits\10\Extension SDKs`

4. **HoloLens 2 Device (Optional for Testing)**
   - Connected via USB-C or WiFi
   - Developer Mode enabled
   - Device Portal accessible

### Project Scene Preparation

Ensure your target scene is properly saved and configured:

```
Expected location: Assets/Scenes/final.unity
Current state: Full scene with building energy visualization, camera controller, Cesium tileset
```

---

## Part 2: Step 1 - Build Settings Configuration

### Opening Build Settings

Navigate through Unity menu:
```
File → Build Settings (Ctrl+Shift+B)
```

### Configuring Platform

1. **Select Universal Windows Platform (UWP)**
   - In the Platform list, find "Universal Windows Platform"
   - Click to select
   - Click **"Switch Platform"** (takes 1-2 minutes for recompilation)

2. **Add Your Scene to Build**
   - In the "Scenes in Build" section, click **"Add Open Scenes"**
   - Verify "Scenes/final" appears in the list

### Setting Build Parameters

Configure the following build settings:

| Parameter | Value | Purpose |
|-----------|-------|---------|
| Platform | Universal Windows Platform | Target OS for HoloLens 2 |
| Scenes in Build | Scenes/final | Main application scene |
| Build System | Visual Studio 2022 | Output format |
| Build and Run On | Local Machine | Initial build target |

---

## Part 3: Step 2 - XR Plug-in Management Configuration

### What is XR Plug-in Management?

XR Plug-in Management is Unity's system for managing Extended Reality (XR) features including:
- **Input handling** (hand tracking, controllers, gaze)
- **Rendering** (specialized for AR/VR devices)
- **Device communication** (HoloLens 2 specific protocols)

For HoloLens 2, we use **OpenXR**, which is Microsoft's standardized XR framework.

### Accessing XR Plug-in Management

```
Edit → Project Settings → XR Plug-in Management
```

### Step 2a: Enable OpenXR for UWP

1. Click the **Universal Windows Platform tab** (Windows logo icon on the right)
2. Locate **OpenXR** in the list of available plugins
3. **Check the checkbox** next to OpenXR to enable it
4. Wait for initialization (~10-30 seconds)

**What this does:** Tells Unity to use OpenXR runtime for HoloLens 2 input/output instead of the older Windows Mixed Reality plugin.

### Step 2b: Add Interaction Profiles

Interaction Profiles define how HoloLens 2 communicates with the application. These profiles map device inputs (hand gestures, gaze) to application actions.

1. Under OpenXR, locate **"Interaction Profiles"** section
2. Click the **"+ Add"** button
3. Select and add these profiles:
   - **Microsoft Hand Interaction Profile** (mandatory for HoloLens 2)
     - Enables hand gesture recognition
     - Supports tap, pinch, hold, move gestures
   - **Eye Gaze Interaction Profile** (optional but recommended)
     - Enables gaze-based input for UI
     - Tracks eye direction for interaction

**Why these profiles?**
- HoloLens 2 does NOT use traditional controllers
- It uses hand tracking (optical sensors on device front)
- Optional eye tracking (via infrared sensors)
- These profiles map hand/eye data to input events in your application

### Step 2c: Select Feature Groups

Feature Groups enable specific OpenXR capabilities:

From the OpenXR dropdown, ensure these are **checked:**
- ✅ **Hand Interaction Poses** - Standard hand pose data
- ✅ **Palm Pose** - Extended hand skeletal data
- ✅ **Eye Gaze** - Eye tracking data

---

## Part 4: Step 3 - Player Settings Configuration

### Understanding Player Settings

Player Settings control how your application behaves on the target platform. For HoloLens 2 (UWP), critical settings include:

- **Scripting Backend:** How C# code is compiled to run on device
- **API Compatibility:** .NET framework features available
- **Capabilities:** OS permissions (camera, networking, etc.)

### Accessing Player Settings

```
Edit → Project Settings → Player
```

The Player Settings panel opens with multiple tabs. Look for the **Universal Windows Platform tab** (Windows logo).

### Step 4a: Configure Scripting Backend

Under **"Other Settings"** section:

| Setting | Required Value | Reason |
|---------|----------------|--------|
| Scripting Backend | IL2CPP | Converts C# to C++ for HoloLens (required for performance) |
| IL2CPP Code Generation | Faster (Smaller Builds) | Optimizes build size for device storage |
| API Compatibility Level | .NET Standard 2.1 | Supported framework for UWP applications |

**About IL2CPP:** This is critical for HoloLens. Your C# code is not directly executed. Instead:
```
Your C# Code (.cs files)
         ↓
    IL2CPP Compiler
         ↓
    Generated C++ Code (.cpp files)
         ↓
    MSVC C++ Compiler
         ↓
    ARM64 Machine Code (.obj files)
         ↓
    Linker
         ↓
    Executable (App.exe) for HoloLens 2
```

### Step 4b: Configure Package Settings

Under **"Publishing Settings"** section:

| Setting | Value | Purpose |
|---------|-------|---------|
| Package Name | com.gisworld.BuildingEnergyViewer | Unique app identifier |
| Version Number | 1.0.0.0 | Application version |
| Publisher | GisWorld | Company name |

### Step 4c: Enable Required Capabilities

Capabilities are OS-level permissions. For your building energy visualization app using Cesium and APIs, enable:

| Capability | Purpose |
|-----------|---------|
| ✅ **InternetClient** | Access external APIs (building energy data) |
| ✅ **InternetClientServer** | Bidirectional internet communication |
| ✅ **PrivateNetworkClientServer** | Local network access |
| ✅ **SpatialPerception** | CRITICAL: 3D spatial mapping for HoloLens |
| ✅ **GazeInput** | Eye tracking for UI interaction |
| ✅ **Holographic** | HoloLens-specific rendering capabilities |

**About SpatialPerception:** This is the most critical capability for HoloLens mixed reality applications. It allows:
- Access to spatial mapping (3D mesh of physical environment)
- Gesture recognition (hand tracking)
- Surface detection (floor, walls, objects)
- Mesh building in real-time

---

## Part 5: Building in Unity

### Initiating the Build

1. Verify **all settings are complete** in previous steps
2. Verify your scene is saved: `Ctrl+S`
3. Go to **File → Build Settings**
4. Ensure configuration shows:
   - Platform: **Universal Windows Platform**
   - Architecture: **ARM64**
   - Build Type: **D3D Project**
5. Click **"Build"**
6. **Select output folder:** `D:\3D_Unity_2022\Builds\HoloLens2`

### Build Process Duration

```
Expected Timeline:
├─ Unity Compilation: 1-2 minutes
│  └─ Compiles all C# scripts
├─ IL2CPP Generation: 2-3 minutes
│  └─ Generates C++ from C# (GameAssembly.cpp, UnityGenerated.cpp, etc.)
├─ Scene Serialization: 30 seconds
│  └─ Converts Unity scene data to binary format
├─ Asset Bundling: 1-2 minutes
│  └─ Packages textures, meshes, prefabs
└─ Project Generation: 1 minute
   └─ Creates Visual Studio solution (.sln file)

TOTAL: 5-10 minutes
```

### Build Output

Once complete, the `HoloLens2` folder will contain:

```
HoloLens2/
├── 3D_Unity_2022.sln              ← Open this in Visual Studio
├── 3D_Unity_2022/                 ← UWP wrapper project (C++)
│   ├── 3D_Unity_2022.vcxproj     ← C++ project file
│   ├── App.cpp, Main.cpp          ← Entry point code
│   └── App.xaml                   ← UI layout (minimal for HoloLens)
├── Il2CppOutputProject/           ← IL2CPP generated C++ code
│   ├── Il2CppOutputProject.vcxproj
│   ├── GameAssembly.dll           ← Your compiled C# as DLL
│   └── GameAssembly.lib           ← Linkage library
├── build/                         ← Intermediate build artifacts
│   ├── bin/ARM64/Master/          ← Final binaries
│   └── obj/                       ← Object files, cache
└── UnityCommon.props              ← Shared build properties
```

---

## Part 6: Visual Studio Deployment - Initial Setup

### Opening the Solution

1. Navigate to: `D:\3D_Unity_2022\Builds\HoloLens2`
2. Double-click **3D_Unity_2022.sln**
3. Visual Studio opens (loading takes 30-60 seconds)

### Understanding the Visual Studio Project Structure

Two main projects in the solution:

| Project | Language | Purpose | Depends On |
|---------|----------|---------|-----------|
| **Il2CppOutputProject** | C++ | Compiles C# code to C++ machine code | IL2CPP generator |
| **3D_Unity_2022** | C++ | UWP wrapper; links all components | Il2CppOutputProject |

**Build Dependency Chain:**
```
Build Solution
    ↓
1. Il2CppOutputProject builds first
   ├─ Compiles generated C++ code
   ├─ Produces: GameAssembly.dll, GameAssembly.lib
   └─ Status: ✓ 597 nodes compiled successfully
    ↓
2. 3D_Unity_2022 project builds
   ├─ Compiles: App.cpp, Main.cpp, UnityGenerated.cpp, pch.cpp
   ├─ Links: Against GameAssembly.lib
   ├─ Produces: 3D_Unity_2022.exe (UWP app)
   └─ Status: ✗ ERRORS ENCOUNTERED (detailed below)
    ↓
3. App Packaging
   ├─ Creates: 3D_Unity_2022.appxbundle
   └─ Ready for: Device deployment or Store submission
```

### Initial Build Configuration

In Visual Studio toolbar at top:

1. **Configuration Dropdown:** Set to **Master** (or Release)
   - Release: Debug symbols included, slight performance penalty
   - Master: Optimized, no debug info, best for production/HoloLens

2. **Platform Dropdown:** Set to **ARM64**
   - This targets HoloLens 2's native ARM64 processor
   - Critical: must match device architecture

3. **Deployment Target:** Set to **Remote Device** (if device connected)
   - For USB: HoloLens detected automatically
   - For WiFi: Enter HoloLens IP address

---

## Part 7: OpenXR Validation Error

### Error Encountered

```
BuildFailedException: OpenXR Build Failed
Message: "Additive Interaction feature requires a valid controller or hand 
interaction profile selected within Interaction Profiles"
Exit Code: 5 errors
```

### Root Cause Analysis

OpenXR validation occurs at build time to ensure:
1. Required XR features have valid input profiles
2. Application won't crash due to missing input handlers
3. All enabled features have corresponding device profiles

**Why it failed:**
- We enabled "Additive Interaction" feature in OpenXR settings
- But didn't assign any specific interaction profile to handle it
- HoloLens 2 validation rejected this incomplete configuration

### Solution Applied

In  **Edit → Project Settings → XR Plug-in Management → UWP Tab:**

1. Clicked the **Interaction Profiles dropdown**
2. Added **Microsoft Hand Interaction Profile**
3. Added **Eye Gaze Interaction Profile**

This tells the build system:
- "We're enabling hand-based interaction, and the HoloLens 2 'Microsoft Hand Interaction Profile' will provide input"
- "We're enabling gaze input, handled by 'Eye Gaze Interaction Profile'"
- Build validation now passes ✓

---

## Part 8: SDK Version Mismatch Error

### Error Encountered

```
Error: "Could not find SDK 'WindowsMobile, Version=10.0.26100.0'"
Location: 3D_Unity_2022.vcxproj
```

### Root Cause Analysis

The project file (`.vcxproj`) contains an `<SDKReference>` tag:

```xml
<SDKReference Include="WindowsMobile, Version=10.0.26100.0" />
```

This tells MSBuild: "Link against HoloLens SDK version 10.0.26100.0"

**But:** The system didn't have the correct version installed in:
```
C:\Program Files (x86)\Windows Kits\10\Extension SDKs\WindowsMobile\10.0.26100.0
```

**Why this happened:**
- Unity's HoloLens export sometimes targets cutting-edge SDK versions
- Not all development machines have the latest SDKs installed
- Windows SDK versions are tied to specific Windows releases

### Solution Applied

Modified the project file to use available SDK version:

**File:** `D:\3D_Unity_2022\Builds\HoloLens2\3D_Unity_2022\3D_Unity_2022.vcxproj`

**Change:**
```xml
<!-- BEFORE -->
<SDKReference Include="WindowsMobile, Version=10.0.26100.0" />

<!-- AFTER -->
<SDKReference Include="WindowsMobile, Version=10.0.22621.0" />
```

**Why this works:**
- Version 10.0.22621.0 (Windows 11 21H2) is widely available
- HoloLens 2 device OS is compatible with this SDK
- Build tools know where to find this version

---

## Part 9: Platform Architecture Mismatch - LNK1112 Linker Error

### Error Encountered

```
LNK1112: module machine type 'x64' conflicts with target machine type 'ARM64'
Location: D:\3D_Unity_2022\Builds\HoloLens2\build\obj\3D_Unity_2022\ARM64\Master\App.obj
```

**Full Error Chain:**
```
STEP 1: ✓ IL2CPP Stage Succeeded
        Build succeeded with 597 successful nodes
        Generated: GameAssembly.dll, GameAssembly.lib (correct ARM64)

STEP 2: ✓ C++ Compilation Succeeded
        Compiled: App.cpp, Main.cpp, UnityGenerated.cpp
        Generated: App.obj, Main.obj, etc.

STEP 3: ✗ Linking Stage FAILED
        Attempted to link:
        - App.obj (contains x64 machine code) ← WRONG!
        - Against: GameAssembly.lib (ARM64 code)
        
        ERROR: "Cannot link x64 object to ARM64 target"
```

### Root Cause Analysis

This is a **machine code architecture mismatch**:

1. **Expected Pipeline:**
   ```
   C++ Source Code (App.cpp)
         ↓
   MSVC C++ Compiler /arch:ARM64
         ↓
   ARM64 Object File (App.obj with ARM64 assembly)
         ↓
   Link with GameAssembly.lib (ARM64)
         ↓
   ARM64 Executable (ready for HoloLens 2)
   ```

2. **Actual Pipeline (broken):**
   ```
   C++ Source Code (App.cpp)
         ↓
   MSVC C++ Compiler /arch:x64  ← WRONG ARM64 flag!
         ↓
   x64 Object File (App.obj with x64 assembly)
         ↓
   Try to Link with GameAssembly.lib (ARM64)
         ↓
   LINKER ERROR: Cannot mix x64 + ARM64
   ```

### Why the Compiler Used x64 Instead of ARM64

In the `.vcxproj` file, we found the root cause in **PropertyGroup conditions**:

```xml
<!-- INCORRECT (what existed initially) -->
<PropertyGroup Condition="'$(Configuration)'=='Master'" Label="Configuration">
  <ConfigurationType>Application</ConfigurationType>
  <PlatformToolset>v143</PlatformToolset>
</PropertyGroup>
```

**The Problem:**
- This condition only checks `$(Configuration)` - whether build is "Master", "Release", etc.
- It DOES NOT check `$(Platform)` - whether target is Win32, x64, ARM, or ARM64
- MSBuild doesn't know which platform to build for
- MSBuild defaults to x86/x64 (most common on development machines)
- Even though we set `/p:Platform=ARM64` in the command line, the PropertyGroup doesn't respect it
- Compiler uses default platform settings, ignoring ARM64 target

### Complete Solution Applied

Multiple fixes were required to address this comprehensively:

#### Fix #1: Update PropertyGroup Conditions

```xml
<!-- CORRECT (updated) -->
<PropertyGroup Condition="('$(Configuration)'=='Master' OR '$(Configuration)'=='MasterWithLTCG') 
                           AND ('$(Platform)'=='Win32' OR '$(Platform)'=='x64' 
                                OR '$(Platform)'=='ARM' OR '$(Platform)'=='ARM64')" 
               Label="Configuration">
  <ConfigurationType>Application</ConfigurationType>
  <UseDebugLibraries>false</UseDebugLibraries>
  <WholeProgramOptimization>true</WholeProgramOptimization>
  <PlatformToolset>v143</PlatformToolset>
</PropertyGroup>
```

**What this does:**
- MSBuild now checks BOTH Configuration AND Platform
- When we pass `/p:Platform=ARM64`, this PropertyGroup activates
- Project settings now include `<PlatformToolset>v143</PlatformToolset>`
- v143 toolset includes ARM64 support

#### Fix #2: Add Platform-Specific Compiler Settings

```xml
<!-- NEW: Force ARM64 compiler architecture -->
<PropertyGroup Condition="'$(Platform)'=='ARM64'">
  <PreferredToolArchitecture>x64</PreferredToolArchitecture>
</PropertyGroup>
```

**What this does:**
- Tells MSBuild to use 64-bit build tools when targeting ARM64
- This is important because 32-bit tools can't generate large ARM64 optimized code
- 64-bit tools (running on your x64 PC) can cross-compile to ARM64

#### Fix #3: Add Linker Configuration

```xml
<!-- NEW: Explicitly tell linker the target architecture -->
<ItemDefinitionGroup Condition="'$(Platform)'=='ARM64'">
  <Link>
    <MachineType>MachineARM64</MachineType>
  </Link>
</ItemDefinitionGroup>
```

**What this does:**
- `ItemDefinitionGroup` applies settings to all C++ files in this project
- `Link` section configures linker behavior
- `<MachineType>MachineARM64</MachineType>` tells linker:
  - "All incoming objects should be ARM64"
  - "Output file should be ARM64"
  - "Reject any non-ARM64 objects"

### Solution Result

After applying all three fixes:

```
BEFORE: LNK1112: module machine type 'x64' conflicts with target machine type 'ARM64'
        Cannot link x86/x64 objects with ARM64 target

AFTER:  Build succeeded with 597 successful Il2CPP nodes
        AND compiled C++ stage (0 failed)
        Ready to package and deploy
```

---

## Part 10: Build Cache Corruption Issue

### Issue Description

During iterative build attempts, we encountered inconsistent errors where:
- PropertyGroup changes didn't take effect
- Platform settings seemed to be ignored
- Rebuild produced same errors despite fixes

### Root Cause

Visual Studio and MSBuild cache build artifacts in:

```
D:\3D_Unity_2022\Builds\HoloLens2\:
  ├── build/              ← Incremental build cache
  │   ├── bin/            ← Cached binaries
  │   └── obj/            ← Cached object files
  │
└── Il2CppOutputProject/build/  ← IL2CPP cache
```

**Problem:** Old cached files prevented new platform settings from taking effect.

### Solution Applied

**Clean Build Process:**

```powershell
# Delete all cache
Remove-Item "D:\3D_Unity_2022\Builds\HoloLens2\build" -Recurse -Force
Remove-Item "D:\3D_Unity_2022\Builds\HoloLens2\3D_Unity_2022\build" -Recurse -Force

# Full rebuild from scratch
msbuild.exe "3D_Unity_2022.sln" /t:Rebuild /p:Configuration=Master /p:Platform=ARM64
```

**What this does:**
- `/t:Rebuild` - Rebuild target (not just build)
- Tell MSBuild to treat everything as "out of date"
- Recompile all source files with new settings
- Generate new object files with correct architecture

---

## Part 11: Remote Debugger Configuration Issues

### Error Encountered

```
Warning: Unable to start debugging. Unable to connect to the 
Microsoft Visual Studio Remote Debugging Monitor named 'localhost'.
```

### Root Cause Analysis

Remote Windows Debugger requires special configuration:

1. **Machine Name/IP Configuration:**
   - We set it to `localhost` (assumes local USB connection)
   - HoloLens 2 wasn't responding on that channel
   - Debugger couldn't establish connection

2. **Debugging Protocol:**
   - Remote debugging requires the HoloLens 2 to run a "debugging monitor"
   - This monitor needs to be activated on the device first
   - USB deployment vs WiFi require different setup

### Why We Bypassed This

For HoloLens 2 app deployment, full remote debugging isn't always necessary:

- **IL2CPP builds are difficult to step-debug** (C++ is complex)
- **Device Portal web interface** is simpler for initial deployment
- **Once deployed, basic testing** can happen without debugger

### Recommended Alternative: Device Portal Deployment

```
Device Portal Deployment:
  ├─ Enable on Device: Settings → System → About → Device Portal
  ├─ Get Device IP: Settings → System → About → IP Address
  ├─ Browser: https://{IP}:10443
  ├─ Upload App Package
  └─ Install & Launch
```

This avoids all debugging configuration issues and directly deploys to device.

---

## Part 12: Application Package Generation

### What is an App Package?

The app package (`.appxbundle` or `.msix`) is a **compressed container** containing:

```
3D_Unity_2022.appxbundle
├── AppManifest.xml          ← App metadata, capabilities, version
├── GameAssembly.dll         ← Your compiled C# as C++ DLL
├── UnityPlayer.dll          ← Unity runtime
├── Data/
│   ├── Managed/             ← .NET assemblies
│   ├── Resources/           ← Textures, meshes, audio
│   ├── il2cpp_data/         ← IL2CPP metadata, type information
│   └── Scenes/              ← Serialized scene data
├── Plugins/
│   └── ARM64/               ← ARM64-specific libraries
└── Resources.pri            ← Resource index for fast loading
```

**Package creation happens when:**
1. Both projects build successfully (Il2CppOutputProject + 3D_Unity_2022)
2. All object files linked without errors
3. MSBuild's packaging step runs

**Location:** After successful build, package appears in:
```
D:\3D_Unity_2022\Builds\HoloLens2\AppPackages\
  └── 3D_Unity_2022_1.0.0.0_ARM64\
      └── 3D_Unity_2022_1.0.0.0_ARM64.appxbundle
```

---

## Part 13: Complete Build Command Reference

### Command-Line Build

For future builds or CI/CD pipelines, use:

```powershell
# Full rebuild with all fixes applied
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "D:\3D_Unity_2022\Builds\HoloLens2\3D_Unity_2022.sln" `
  /t:Rebuild `
  /p:Configuration=Master `
  /p:Platform=ARM64 `
  /p:AppxBundle=Auto `
  /p:PreferredToolArchitecture=x64 `
  /verbosity:normal
```

**Parameters Explained:**

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `/t:Rebuild` | (target) | Full rebuild, not incremental |
| `/p:Configuration=Master` | (property) | Optimization level (Release+LTO) |
| `/p:Platform=ARM64` | (property) | Target architecture for HoloLens 2 |
| `/p:AppxBundle=Auto` | (property) | Generate app package automatically |
| `/p:PreferredToolArchitecture=x64` | (property) | Use 64-bit build tools |
| `/verbosity:normal` | (option) | Show build progress |

---

## Part 14: Technical Architecture Summary

### Build Pipeline Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    UNITY PROJECT                        │
│            (BuildingEnergyManager.cs, etc.)             │
└────────────────────┬────────────────────────────────────┘
                     │ Export → HoloLens 2
                     ↓
┌─────────────────────────────────────────────────────────┐
│          VISUAL STUDIO SOLUTION (.sln)                  │
│                                                         │
│  ┌─────────────────────────────────────────────────┐   │
│  │  Il2CppOutputProject (C++)                      │   │
│  │  ├─ IL2CPP Generated Code                       │   │
│  │  ├─ GameAssembly.cpp → GameAssembly.dll/lib    │   │
│  │  └─ Machine Code: ARM64 ✓                       │   │
│  └──────────────────┬──────────────────────────────┘   │
│                     │                                   │
│                     ↓ Link                              │
│                                                         │
│  ┌─────────────────────────────────────────────────┐   │
│  │  3D_Unity_2022 (C++/UWP)                        │   │
│  │  ├─ App.cpp, Main.cpp, pch.cpp                 │   │
│  │  ├─ Links: Against GameAssembly.lib             │   │
│  │  ├─ Machine Code: ARM64 ✓                       │   │
│  │  └─ Produces: 3D_Unity_2022.exe                │   │
│  └──────────────────┬──────────────────────────────┘   │
│                     │                                   │
│                     ↓ Package                           │
│                                                         │
│  ┌─────────────────────────────────────────────────┐   │
│  │  App Packaging                                  │   │
│  │  ├─ Compression: zip format                    │   │
│  │  ├─ Manifest: AppxManifest.xml                 │   │
│  │  └─ Output: 3D_Unity_2022.appxbundle           │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
                     │
                     ↓
        ┌─────────────────────────┐
        │   HoloLens 2 Device     │
        │  (Deploy Package)       │
        └─────────────────────────┘
```

### Key Components

1. **IL2CPP System**
   - Mono runtime replaced by IL2CPP (Intermediate Language to C++)
   - Compiles all C# code to C++ at build time
   - Result: Better performance, deterministic behavior
   - Source: `C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Data\PlaybackEngines\MetroSupport\Players\UAP\il2cpp`

2. **UWP Wrapper Project**
   - C++ scaffolding required by Universal Windows Platform
   - Initializes Unity runtime
   - Handles device events (activation, suspension, etc.)
   - Located in `3D_Unity_2022/` subfolder

3. **MSBuild System**
   - Orchestrates the entire build process
   - Property/Item definitions control compiler/linker behavior
   - Condition attributes determine when settings apply
   - `.vcxproj` files define project structure (XML format)

4. **Property Groups**
   - MSBuild configuration containers
   - Condition attribute: When settings apply
   - Example: `Condition="'$(Platform)'=='ARM64'"` means "if target is ARM64, apply these settings"

---

## Part 15: Deployment Options

### Option 1: Visual Studio Deployment (Recommended if working)

**Prerequisites:**
- HoloLens 2 connected via USB or WiFi
- Remote debugger configuration complete
- Build successful

**Process:**
```
1. Visual Studio toolbar: Configuration=Master, Platform=ARM64
2. Debug → Start Without Debugging (Ctrl+F5)
3. App automatically builds, packages, deploys, and launches
```

### Option 2: Device Portal Deployment (Most Reliable)

**Prerequisites:**
- HoloLens 2 connected to same WiFi network
- Device Portal enabled on device
- Successful build (app package exists)

**Process:**
```
1. On HoloLens 2: Settings → System → For developers → Device Portal (ON)
2. Get IP: Settings → System → About
3. From PC browser: https://{HOLOLENS_IP}:10443
4. Add Windows credentials
5. Drag-drop or Browse for: 3D_Unity_2022.appxbundle
6. Click "Install" → Wait for completion
7. App appears in HoloLens Start menu
```

### Option 3: Install.ps1 Script (for Automation)

Windows provides PowerShell script to install packages:

```powershell
# Navigate to package directory
cd "D:\3D_Unity_2022\Builds\HoloLens2\AppPackages\3D_Unity_2022_1.0.0.0_ARM64"

# Run installation script
.\Install.ps1
```

This requires HoloLens connection and appropriate permissions.

---

## Part 16: Troubleshooting Reference

### Build Fails with "LNK1112"

**Symptom:**
```
LNK1112: module machine type 'x86'/'x64' conflicts with target machine type 'ARM64'
```

**Root Causes & Solutions:**

| Cause | Solution |
|-------|----------|
| PropertyGroup doesn't check Platform | Update `.vcxproj` to include `'$(Platform)'=='ARM64'` |
| Cached object files | Delete `build/` folders, run Rebuild |
| Missing /arch compiler flag | Add `<PreferredToolArchitecture>x64</PreferredToolArchitecture>` |
| Linker targeting wrong arch | Add MachineType ItemDefinitionGroup for ARM64 |

### Build Fails with SDK Error

**Symptom:**
```
Could not find SDK "WindowsMobile, Version=X.X.X.X"
```

**Solution:**

1. Check available SDKs:
   ```powershell
   Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\Extension SDKs\WindowsMobile"
   ```

2. Update `.vcxproj` with available version

### Remote Debugger Won't Connect

**Symptom:**
```
Unable to connect to the Microsoft Visual Studio Remote Debugging Monitor named 'localhost'
```

**Solutions:**

1. **Use Device Portal instead** (simpler for HoloLens 2)
2. **Check HoloLens is in Developer Mode**
3. **Verify USB connection** or WiFi connectivity
4. **Use Device IP instead of localhost** for WiFi:
   - Get HoloLens IP from Settings
   - In VS Project Properties → Debugging → Machine Name: Use IP

### Package Not Created After Build

**Symptom:**
```
No .appxbundle file in AppPackages/ folder after successful build
```

**Causes:**
- One of the projects had warnings treated as errors
- Linker stage failed silently
- App packaging step was skipped

**Solution:**
- Check build output for errors (not just warnings)
- Verify both projects built successfully
- Run full Rebuild (not just Build)

---

## Part 17: Performance Considerations

### Build Time Optimization

**First Build (Cold):**
- IL2CPP generation: 2-3 minutes (every C# file compiled)
- C++ compilation: 2-3 minutes (every .cpp file)
- Linking: 1 minute (combine all object files)
- **Total: 5-10 minutes**

**Incremental Builds:**
- Only changed files recompile
- IL2CPP caches unchanged .cpp files
- **Total: 30-120 seconds** (depending on change magnitude)

**Optimization Tips:**
1. **Avoid recompiling C# beyond necessary** - Incremental IL2CPP compilation
2. **Use "Debug" configuration during development** - Slightly faster than Master
3. **Pre-compile heavy computations** - Cache results at build time
4. **Profile your code** - Find hot spots before optimizing build process

### Runtime Performance on HoloLens 2

**Benefits of IL2CPP:**
- Ahead-of-time compilation: No JIT overhead
- Better CPU utilization
- Reduced memory footprint
- 2-3x faster than traditional Mono

**Your Application:**
- Building energy visualization (lightweight)
- Cesium tileset rendering (GPU bound, not CPU bound)
- API data fetching (network bound, not CPU bound)
- Expected frame rate: 60 FPS solid (HoloLens 2 native)

---

## Part 18: Summary and Key Takeaways

### Successful Export Workflow

```
✓ Step 1: Configure Build Settings (Platform=UWP, Architecture=ARM64)
✓ Step 2: Enable XR Plug-ins (OpenXR + Hand Interaction Profile)
✓ Step 3: Configure Player Settings (IL2CPP, Capabilities)
✓ Step 4: Build in Unity (creates .sln solution)
✓ Step 5: Fix .vcxproj PropertyGroup conditions (critical fix)
✓ Step 6: Fix SDK references (WindowsMobile, Version=10.0.22621.0)
✓ Step 7: Build in Visual Studio (Configuration=Master, Platform=ARM64)
✓ Step 8: Deploy via Device Portal or Remote Debugger
✓ Step 9: Test on HoloLens 2 device
```

### Critical Success Factors

1. **PropertyGroup Conditions Must Include Platform**
   - Without this, compiler defaults to x86/x64
   - Causes LNK1112 linker errors
   - Is one-line fix in .vcxproj file

2. **SDK Version Must Match System Installation**
   - Check available SDKs before build
   - WindowsMobile SDK location: `C:\Program Files (x86)\Windows Kits\10\Extension SDKs`
   - Use available version in .vcxproj

3. **Clean Rebuilds Required After Major Changes**
   - Build cache can prevent configuration changes from taking effect
   - Delete `build/` folders before Rebuild
   - Use `/t:Rebuild` in MSBuild, not `/t:Build`

4. **OpenXR Requires Valid Interaction Profiles**
   - Enable profiles in XR Plug-in Management
   - HoloLens 2 uses "Microsoft Hand Interaction Profile"
   - Without valid profiles, build fails during validation

5. **IL2CPP Compilation Takes Time**
   - 2-3 minutes is normal for full builds
   - Not a system problem, is expected
   - Incremental rebuilds much faster

### Files Modified During Export Process

| File | Changes | Reason |
|------|---------|--------|
| `3D_Unity_2022.vcxproj` | PropertyGroup conditions, SDK ref | Fix platform architecture |
| `Project Settings - Player` | IL2CPP, API Level, Capabilities | Configure runtime environment |
| `Project Settings - XR` | OpenXR enabled, profiles added | Enable hand/gaze input |

### Common Mistakes to Avoid

1. ❌ Building with Platform=Win32 (defaults to x86)
2. ❌ Using old SDK versions not installed on system
3. ❌ Incremental builds after major PropertyGroup edits
4. ❌ Forgetting to enable "SpatialPerception" capability
5. ❌ Not adding Interaction Profiles for OpenXR features
6. ❌ Using IL2CPP without proper C++ toolchain installed

---

## Part 19: Appendix - Project File Reference

### Example 3D_Unity_2022.vcxproj (Corrected)

Key sections that fixed the build:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  
  <!-- Platform Configurations -->
  <ItemGroup Label="ProjectConfigurations">
    <ProjectConfiguration Include="Master|ARM64">
      <Configuration>Master</Configuration>
      <Platform>ARM64</Platform>
    </ProjectConfiguration>
  </ItemGroup>

  <!-- CRITICAL FIX: PropertyGroup with Platform check -->
  <PropertyGroup Condition="('$(Configuration)'=='Master') AND ('$(Platform)'=='ARM64')" 
                 Label="Configuration">
    <ConfigurationType>Application</ConfigurationType>
    <UseDebugLibraries>false</UseDebugLibraries>
    <WholeProgramOptimization>true</WholeProgramOptimization>
    <PlatformToolset>v143</PlatformToolset>
  </PropertyGroup>

  <!-- CRITICAL FIX: Force 64-bit build tools for ARM64 -->
  <PropertyGroup Condition="'$(Platform)'=='ARM64'">
    <PreferredToolArchitecture>x64</PreferredToolArchitecture>
  </PropertyGroup>

  <!-- SDK Reference with correct version -->
  <ItemGroup>
    <SDKReference Include="WindowsMobile, Version=10.0.22621.0" />
  </ItemGroup>

  <!-- CRITICAL FIX: ARM64-specific linker settings -->
  <ItemDefinitionGroup Condition="'$(Platform)'=='ARM64'">
    <Link>
      <MachineType>MachineARM64</MachineType>
    </Link>
  </ItemDefinitionGroup>
</Project>
```

---

## Conclusion

Exporting a Unity 2022 application to HoloLens 2 requires careful configuration at multiple levels:

1. **Unity Project Settings** - Target device, XR framework, capabilities
2. **Visual Studio Project Files** - Platform-specific compilation settings
3. **Build System Configuration** - Architecture flags, linker settings, SDK references

The most critical issue encountered was the **PropertyGroup platform condition**, which required modification of the `.vcxproj`file to explicitly check both Configuration AND Platform. This single issue cascaded into:
- Compiler generating wrong architecture (x64 instead of ARM64)
- Linker errors combining incompatible architectures
- Build failures preventing app packaging

Once fixed, the build system operated as designed:
- IL2CPP properly compiled C# to ARM64 C++
- MSVC C++ compiler generated ARM64 machine code
- Linker successfully combined all ARM64 objects into executable
- App packager created deployable `.appxbundle`

Future HoloLens exports from this project should experience significantly faster builds due to cached configurations and understanding of the build pipeline.

---

**Document Version:** 1.0
**Created:** February 15, 2026
**Project:** Building Energy Visualization System for HoloLens 2
**Build Target:** ARM64 (HoloLens 2 native architecture)
**Status:** Export process documented, key issues resolved
