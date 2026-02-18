# HoloLens 2 Setup and Interaction Guide

## Overview
This Unity project is configured for both desktop development and HoloLens 2 deployment. The application allows users to interact with georeferenced 3D building data and view energy information.

---

## Platform Detection

The application automatically detects the runtime platform:
- **Desktop**: Full keyboard/mouse controls
- **HoloLens 2**: XR gestures and head tracking

---

## HoloLens 2 Controls

### Building Interaction

#### View Building Data (Quick Tap)
1. **Gaze** at a building (direct eye gaze or hand ray)
2. **Air Tap** or **Pinch** gesture (quick release)
3. Building energy data panel appears

#### Edit Building Attributes (Hold Gesture)
1. **Gaze** at a building
2. **Hold** the pinch gesture for **0.5+ seconds**
3. Building attributes edit form opens

### Navigation

#### Camera Movement
- **Head Tracking**: Camera follows your head position automatically
- **Physical Movement**: Walk around to navigate (room-scale or larger spaces)
- Desktop camera controls (WASD, mouse) are automatically disabled

#### Hand Menu (Optional - can be added)
- Voice commands for additional features
- Hand-based UI panels for settings

---

## Desktop Development Controls

When running in Unity Editor or on Windows desktop:

### Building Interaction
- **Left Click**: View building energy data
- **Ctrl + Left Click**: Open building attributes edit form

### Camera Controls
- **WASD / Arrow Keys**: Move forward/back/left/right
- **E/R**: Move up
- **C/Z**: Move down
- **Right Click + Drag**: Look around
- **Mouse Scroll**: Zoom in/out
- **Left Shift**: Fast movement

---

## Configuration Settings

### CesiumMetadataReader Settings
Located on the CesiumMetadataReader GameObject:

- **Hold Duration**: Time in seconds to trigger edit mode (default: 0.5s)
- **Use XR Input**: Auto-detects XR device, can be manually overridden
- **Display Duration**: How long to show metadata panels (10 seconds)

### CameraController Settings
Located on the Camera GameObject:

- **Move Speed**: Normal movement speed (100)
- **Fast Move Speed**: Sprint speed (500)
- **Rotation Speed**: Mouse sensitivity (2.0)
- **Disable On XR**: Automatically disable on HoloLens 2 (recommended: ON)

### BuildingEnergyManager Settings
Located on the BuildingEnergyManager GameObject:

- **Init Delay**: Delay before initialization to avoid conflicts (1.0s)
- **Disable Auto Init**: Prevent automatic initialization if needed

---

## HoloLens 2 Build Configuration

### Prerequisites
1. **Unity 2022.3.23f1** (or compatible version)
2. **Visual Studio 2022** with:
   - Universal Windows Platform development workload
   - Windows 10/11 SDK (10.0.19041.0 or newer)
3. **Windows Device Portal** access to your HoloLens 2

### Build Settings

1. **File → Build Settings**
   - Platform: **Universal Windows Platform**
   - Target Device: **HoloLens**
   - Architecture: **ARM64**
   - Build Type: **D3D Project**
   - Minimum Platform Version: **10.0.10240.0**
   - Target SDK Version: **10.0.19041.0** or newer

2. **Player Settings** (Edit → Project Settings → Player → UWP):
   
   #### XR Settings
   - Enable **Virtual Reality Supported**
   - Add **Windows Mixed Reality** to SDK list
   - Or use **OpenXR** for modern HoloLens 2 apps
   
   #### Other Settings
   - **Scripting Backend**: IL2CPP
   - **API Compatibility Level**: .NET Standard 2.1
   
   #### Publishing Settings
   - **Package Name**: com.YourCompany.BuildingEnergyViewer
   - **Capabilities** (enable these):
     - InternetClient
     - InternetClientServer
     - PrivateNetworkClientServer
     - SpatialPerception
     - Microphone (if using voice commands)
     - WebCam (if needed)
     - Gaze Input

3. **XR Plug-in Management**
   - Go to **Edit → Project Settings → XR Plug-in Management**
   - Select **Universal Windows Platform** tab
   - Enable:
     - **OpenXR** (recommended)
     - Or **Windows Mixed Reality** (legacy)
   
   #### OpenXR Configuration (if using)
   - Add **Microsoft HoloLens** feature group
   - Add **Hand Tracking** feature
   - Add **Eye Gaze Interaction** feature

### Build Process

1. **Build the UWP Solution**
   ```
   File → Build Settings → Build
   ```
   - Choose an output folder (e.g., `Builds/HoloLens2`)
   - Unity generates a Visual Studio solution

2. **Open in Visual Studio**
   - Open the `.sln` file in the build folder
   - Select **Master** or **Release** configuration
   - Select **ARM64** architecture
   - Connect your HoloLens 2 via USB or WiFi

3. **Deploy to HoloLens 2**
   - **Debug → Start Without Debugging** (Ctrl+F5)
   - Or right-click project → **Deploy**

---

## API Configuration for HoloLens 2

The application connects to a backend API for building energy data:

### Network Requirements
- HoloLens 2 must have internet connectivity
- API endpoint: `https://backend.gisworld-tech.com`
- Ensure the API server accepts requests from HoloLens 2 IP addresses

### Authentication
- Automatic token authentication on startup
- Default credentials configured in BuildingEnergyManager
- Token stored for session duration

---

## Cesium for Unity on HoloLens 2

### Tileset Configuration
- **Bisingen** 3D Tileset is pre-configured
- Supports streaming 3D tiles over network
- Optimized LOD (Level of Detail) for HoloLens 2

### Performance Optimization
- Reduce tileset maximum screen space error for better performance
- Enable occlusion culling
- Limit concurrent tile downloads

### Cesium Ion Token
- Requires valid Cesium ion access token
- Configure in **Window → Cesium** panel before building
- Token embedded in build

---

## Testing in Unity Editor

### Play Mode Simulation
1. Use **Device Simulator** (Window → Device Simulator)
2. Or test with desktop controls
3. Enable/disable XR simulation in Player Settings

### Input Simulation
- Hold **Space** to simulate air tap
- Hold **Space** for 0.5s to simulate hold gesture
- Mouse click simulates tap in editor

---

## Troubleshooting

### Application Won't Deploy
- Check USB connection and drivers
- Enable **Developer Mode** on HoloLens 2 (Settings → Update & Security → For Developers)
- Verify Windows Device Portal access
- Try WiFi deployment if USB fails

### Gestures Not Working
- Verify **SpatialPerception** capability is enabled
- Check Hand Tracking is enabled in OpenXR features
- Recalibrate HoloLens 2 (Settings → System → Calibration → Eye calibration)

### Buildings Not Colored
- Check network connectivity
- Verify API authentication token in console
- Ensure BuildingEnergyManager completed initialization
- Check Cesium tileset loaded successfully

### Performance Issues
- Reduce Cesium tileset quality settings
- Lower Unity quality settings (Edit → Project Settings → Quality)
- Close background applications on HoloLens 2
- Reduce maximum cached tiles

### Domain Reload Error on Startup
- Increase `Init Delay` in BuildingEnergyManager (try 2.0s)
- Clear Library and Temp folders before building
- Ensure Cesium ion token is valid

---

## Voice Commands (Future Enhancement)

### Planned Commands
- "View Data" - Show building information
- "Edit Building" - Open attributes form
- "Hide Panel" - Close current UI panel
- "Refresh Data" - Reload building data from API

### Implementation
- Use Unity's KeywordRecognizer
- Or implement with MRTK Speech Input Handler

---

## Additional Resources

### Microsoft Documentation
- [HoloLens 2 Development](https://docs.microsoft.com/hololens/)
- [Unity for HoloLens 2](https://docs.microsoft.com/windows/mixed-reality/develop/unity/unity-development-overview)
- [OpenXR for HoloLens 2](https://docs.microsoft.com/windows/mixed-reality/develop/native/openxr-best-practices)

### Cesium Documentation
- [Cesium for Unity](https://cesium.com/learn/unity/)
- [3D Tiles Specification](https://github.com/CesiumGS/3d-tiles)

### Support
- Check console logs for detailed error messages
- Use Windows Device Portal for live debugging
- Enable USB debugging for Visual Studio profiling

---

## Known Limitations

1. **Network Dependency**: Requires internet for Cesium tiles and API
2. **Spatial Mapping**: Limited to room-scale by default
3. **Rendering Performance**: Complex tilesets may reduce framerate
4. **Gesture Accuracy**: Requires proper HoloLens 2 calibration

---

## Future Enhancements

- [ ] Hand menu for quick settings access
- [ ] Voice command integration
- [ ] Spatial anchors for persistent building annotations
- [ ] Offline mode with cached tiles
- [ ] Multi-user collaboration via Azure Spatial Anchors
- [ ] Hand-based manipulation of 3D models
- [ ] Eye tracking analytics

---

*Last Updated: February 2026*
*Unity Version: 2022.3.23f1*
*Target Device: Microsoft HoloLens 2*
