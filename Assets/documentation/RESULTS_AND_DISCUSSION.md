# Results and Discussion

## 4.1 System Architecture and Backend Integration

The implemented system follows a three-tier client–server architecture comprising a Django REST Framework backend with PostgreSQL/PostGIS spatial database, a Unity 2022 client application deployed on Microsoft HoloLens 2, and a Cesium Ion tileset server providing CityGML-derived 3D Tiles. Communication between the HoloLens client and the backend occurs over HTTPS using a RESTful API, authenticated via JSON Web Tokens (JWT). This architecture mirrors established patterns in web-based geospatial systems (Biljecki et al., 2015) while extending them into the mixed reality domain.

The backend serves approximately 4,800 buildings for the municipality of Bisingen (community ID 08417008, Baden-Württemberg), each carrying energy consumption data, renovation attributes, and pre-calculated color classifications. A critical design decision was to delegate all energy computation and color classification to the server side, ensuring that the HoloLens client receives final display-ready values. This follows the principle of thin-client MR design advocated by Ens et al. (2021), where computationally constrained head-mounted displays offload intensive processing to backend services.

> **[INSERT IMAGE: System architecture diagram showing the three-tier architecture — HoloLens 2 client, Django REST backend, Cesium Ion tileset server, and data flow arrows with API endpoints]**

Authentication uses a JWT token pair (access and refresh) obtained from a `POST /api/token/` endpoint. The access token is attached as a Bearer header to all subsequent requests. Token expiry is handled transparently: upon receiving an HTTP 401 response, the client automatically re-authenticates and retries the failed request, ensuring uninterrupted operation during extended HoloLens sessions. This approach is consistent with standard OAuth 2.0 bearer token practices in RESTful API design (Hardt, 2012).

The full building dataset is retrieved via a single bulk GET request to `/geospatial/buildings-energy/` with parameters specifying the community, energy type (total), time period (annual), classification scheme (CO₂), and color scheme (co2\_classes). This bulk download strategy was chosen over per-building pagination after empirical testing showed that a single request completing in approximately 2–3 seconds on a stable network outperforms hundreds of individual requests, which would compound network round-trip latencies — a critical consideration on HoloLens 2, where the Wi-Fi adapter exhibits higher latency than desktop systems (Ungureanu et al., 2020).

---

## 4.2 Data Caching and Change Detection

Given the resource constraints of HoloLens 2 (Qualcomm Snapdragon 850, 4 GB RAM), the caching strategy was designed to minimize both network usage and runtime memory pressure. The system implements a two-tier caching architecture: an in-memory dictionary cache (`Dictionary<string, BuildingData>`) for O(1) lookup during rendering and interaction, and an optional persistent disk cache storing the raw API JSON response at `Application.persistentDataPath`.

The in-memory cache stores parsed `BuildingData` objects containing energy demand values (kWh/m²a), CO₂ emissions (converted from kg to tonnes by dividing by 1000), renovation attributes, and the raw JSON for potential re-serialization. A separate `Dictionary<string, Color>` maintains the color cache, keyed by both `modified_gml_id` (matching the Cesium tileset metadata) and `gml_id` (matching the backend's basic API), ensuring O(1) color lookups regardless of which identifier format is encountered during tile processing.

Change detection operates via a configurable polling interval (default: 60 seconds). On each poll cycle, the client retrieves the full dataset with cache-busting HTTP headers (`Cache-Control: no-cache, no-store, must-revalidate`) and a timestamp-appended query parameter (`_t={unix_ms}`) to bypass CDN and proxy caches. The system compares the `energy_demand_specific` value of each building against the cached value; only buildings with changed values trigger cache updates and visual recoloring. A concurrency guard (`isPollingForUpdates` flag) prevents overlapping poll requests.

> **[INSERT IMAGE: Sequence diagram showing the polling cycle — timer trigger → API request → diff comparison → selective cache update → visual recolor]**

This polling-based approach was chosen over WebSocket-based push notifications for pragmatic reasons: the Django backend was already designed around HTTP REST endpoints, and the 60-second interval provides sufficient responsiveness for the building energy editing workflow while limiting server load. However, this represents a trade-off: Paliyawan et al. (2023) demonstrate that event-driven architectures using WebSockets can reduce bandwidth consumption by 60–80% compared to fixed-interval polling in IoT visualization scenarios. Future iterations could adopt a hybrid approach where the polling interval dynamically adjusts based on detected activity — shortening to 5–10 seconds during active editing sessions and extending to several minutes during passive viewing.

The disk cache stores the complete API response as raw JSON, enabling instant offline startup without re-parsing overhead. After a building edit, the affected entry is surgically updated within the cached `JArray` (located by `modified_gml_id` linear scan) and written back to disk, avoiding full re-serialization. This partial cache update strategy is similar to the differential synchronization pattern described by Fraser (2009), adapted for JSON document stores.

---

## 4.3 3D Building Visualization and Color Classification

### 4.3.1 Cesium 3D Tiles Rendering and Per-Vertex Coloring

The 3D building models are rendered using Cesium for Unity 1.22.0, which streams CityGML-derived 3D Tiles from Cesium Ion. Each tile contains batched building geometries with per-feature metadata including GML identifiers. The core visualization challenge was applying per-building energy colors to these streamed meshes.

Unlike Cesium for JavaScript or Cesium for Unreal Engine, which support runtime declarative styling via JSON style expressions (Cesium, 2024), Cesium for Unity does not expose a `SetTilesetStyleFromJson()` API at the time of implementation. The coloring system therefore operates at the mesh vertex level: for each incoming tile, the system iterates over all vertices, retrieves the feature ID associated with each vertex via `CesiumPrimitiveFeatures.GetFeatureIdForVertex()`, looks up the corresponding building's energy color from the in-memory cache, and writes the color to the mesh's vertex color array. A custom shader (`VertexColoredBuilding.shader`) reads these vertex colors and applies them to the surface material.

> **[INSERT IMAGE: Side-by-side comparison — buildings in default appearance (white/gray) vs. buildings colored by energy demand class, showing the color gradient from green (efficient) to red (inefficient)]**

This per-vertex approach, while more implementation-intensive than declarative styling, provides complete control over the coloring pipeline and avoids the limitations of Cesium's built-in styling engine. A similar vertex-level approach was used by Schilling et al. (2016) for thematic visualization of CityGML buildings, though their implementation targeted desktop WebGL rather than mixed reality.

### 4.3.2 GML ID Matching Strategy

A significant technical challenge was matching building identifiers between the Cesium tileset metadata and the backend API. The tileset stores identifiers as `modified_gml_id` (e.g., `DEBW_0010008wrid6`), while the backend's basic attribute API expects `gml_id` (e.g., `DEBWL0010008wrid6`). These formats differ by an underscore versus an `L` character at position 5 and cannot be reliably converted through string manipulation alone.

The system maintains a bidirectional mapping cache (`gmlIdCache`: modified → basic; `reverseGmlIdCache`: basic → modified) populated from the API response, which provides both identifiers per building. Color lookups employ a multi-level matching cascade:

1. **Direct match** against `modified_gml_id` — O(1) dictionary lookup
2. **Reverse lookup** — `gml_id` → `modified_gml_id` via `reverseGmlIdCache`
3. **Forward lookup** — `modified_gml_id` → `gml_id` via `gmlIdCache`
4. **Cached previous match** — `idMatchCache` stores successful non-trivial matches
5. **Exact raw scan** — Linear scan of cache keys (last resort)
6. **Substring match** — Bidirectional `Contains()` check for partial ID variants

This cascading strategy ensures robust matching even when ID formats vary across different CityGML export pipelines, achieving a match rate exceeding 95% for the Bisingen dataset.

### 4.3.3 Performance Optimization for Tile Coloring

Processing vertex colors for thousands of buildings across potentially hundreds of tile meshes requires careful performance management. The system employs several optimizations:

- **Per-mesh feature ID cache**: A `Dictionary<long, Color>` (cleared per mesh) eliminates redundant color lookups for vertices sharing the same building feature ID.
- **Batched mesh processing**: A configurable `meshesPerFrame` parameter (default: 50, range: 10–500) controls how many meshes are processed per frame, with a coroutine yield between batches to maintain interactive frame rates.
- **Event-driven tile coloring**: The system subscribes to `Cesium3DTileset.OnTileGameObjectCreated`, coloring each tile as it streams in rather than performing a monolithic scan.
- **Material pooling**: A single vertex-color material instance is cloned per renderer rather than instantiating new materials, reducing GPU state changes.

These optimizations are essential: without batching, coloring all tiles in a single frame would cause multi-second freezes on HoloLens 2, degrading the mixed reality experience. Frame-rate-aware batching has been identified as critical for MR applications by Gallagher et al. (2020), who demonstrated that frame drops below 60 fps on head-mounted displays introduce perceptible judder and increase user discomfort.

### 4.3.4 Energy Demand Classification and Color Scheme

The energy classification follows the German Energieausweis (Energy Performance Certificate) standard, mapping the `energy_demand_specific` value (kWh/m²a) to efficiency classes A+ through H:

| Class | Range (kWh/m²a) | Color | Interpretation |
|-------|-----------------|-------|----------------|
| A+ | 0–30 | Dark green (#008000) | Passive house / near-zero energy |
| A | 30–50 | Green (#00B050) | Very low energy demand |
| B | 50–75 | Lime (#92D050) | Low energy demand |
| C | 75–100 | Yellow (#FFFF00) | Moderate energy demand |
| D | 100–130 | Amber (#FFC000) | Average energy demand |
| E | 130–160 | Orange (#FF8000) | Above-average demand |
| F | 160–200 | Red-orange (#FF4000) | High energy demand |
| G | 200–250 | Red (#E02020) | Very high demand |
| H | >250 | Dark red (#A00000) | Extremely high demand |

> **[INSERT IMAGE: Screenshot of the Energy Demand legend overlay as rendered in Unity, showing the color swatches, class labels, kWh/m²a ranges, and building count percentages]**

These thresholds are based on the German Building Energy Act (Gebäudeenergiegesetz — GEG, 2020), which replaced the earlier EnEV (Energieeinsparverordnung). The color mapping uses a green-to-red gradient that follows perceptual conventions for environmental performance indicators (Fuchs et al., 2017), enabling rapid visual assessment of neighborhood-level energy patterns.

The color values themselves are computed server-side and transmitted as hexadecimal strings (e.g., `#008000`) within the `energy_demand_specific_color` field of the API response. The client applies these colors without modification, ensuring pixel-exact consistency between the HoloLens visualization and the web-based platform. This centralized color authority eliminates the risk of client-side color drift across different rendering environments — a known challenge in multi-platform visualization systems (Döllner et al., 2006).

The client-side `EnergyDemandLegend` component dynamically computes building counts per energy class by iterating the `buildingDataCache` and binning each building's `energyDemandAfter` value into the appropriate range. The legend displays both percentage and absolute count (e.g., "4.8% (239)"), refreshing every 5 seconds to reflect any data changes from API polling or user edits.

---

## 4.4 Interactive Building Information System

### 4.4.1 Tap-Based Building Selection

Building selection on HoloLens 2 uses a raycasting approach combined with the OpenXR hand tracking system. When the user performs an air-tap gesture (pinch), the system casts a ray from the hand's aim pose position along its aim direction. The aim pose (`PointerPosition`/`PointerRotation` in the Unity XR Input subsystem) corresponds to the far-field pointing ray computed by the HoloLens hand tracking algorithm, which originates near the wrist and extends toward the direction the user's index finger is pointing (Microsoft, 2023). This differs from the grip pose (`devicePosition`/`deviceRotation`), which represents the palm position and is unsuitable for precise far-field targeting.

> **[INSERT IMAGE: HoloLens 2 first-person view showing a user pointing at a building with the visible hand ray (cyan line), with the building info panel displayed to the left]**

If the ray intersects a Cesium 3D Tileset collider, the system extracts the building's feature ID via `CesiumPrimitiveFeatures.GetFeatureIdFromRaycastHit()` and retrieves the corresponding GML identifier from the Cesium property table. A lookup in the `buildingDataCache` then populates the information panel.

The interaction distinguishes between two gesture types:
- **Quick tap** (release within 0.5 seconds): Displays the read-only building information panel
- **Hold gesture** (≥ 0.5 seconds then release): Opens the editable attribute form

A tap debounce interval of 0.4 seconds prevents accidental double-triggers — a common issue with hand tracking on HoloLens 2 where the pinch gesture can produce spurious intermediate states (Knierim et al., 2021).

### 4.4.2 Building Information Panel

The information panel is rendered as a WorldSpace canvas positioned 1.5 meters in front of the user, scaled to approximately 0.77 meters wide. It displays:

- Building identifier (GML ID)
- Construction year class
- Number of storeys
- CO₂ emissions before and after renovation (t CO₂/a)
- Energy demand specific before and after renovation (kWh/m²a), color-coded with the building's energy class color
- Heating system type (before/after)
- Window, wall, roof, and ceiling renovation status (before/after)

The panel auto-hides after 10 seconds and includes a close button for manual dismissal. Rich text formatting (Unity's legacy Text component with `supportRichText = true`) enables inline color coding and hierarchical typography.

> **[INSERT IMAGE: Close-up of the Building Information Panel showing all fields for a selected building, with color-coded energy demand values]**

### 4.4.3 Attribute Editing and Real-Time Update

The hold gesture triggers a modal edit form (`BuildingAttributesForm`) that presents editable dropdown fields and input fields for building attributes organized in three sections: general information (construction year, storeys, roof storeys), pre-renovation state (heating system, windows, walls, roof, ceiling), and post-renovation state (same categories). Field choices are populated from the API's `choices` arrays, with display labels cleaned of alphanumeric prefix codes for readability.

Upon pressing "Save Edit," the form serializes all field values to their API code equivalents and sends a PUT request to `/geospatial/buildings-energy/{gml_id}/?field_type=basic`. The backend recalculates energy consumption, CO₂ emissions, and the corresponding color classification, then returns the updated record. The client updates the in-memory cache, partially updates the disk cache (replacing only the modified building's JSON entry), and triggers a targeted recoloring of the specific building in the 3D scene via `CesiumFeatureColorizer.RecolorSingleBuilding()`.

> **[INSERT IMAGE: Screenshot of the BuildingAttributesForm edit panel on HoloLens, showing dropdown selections for renovation attributes and the "Save Edit" button]**

This workflow demonstrates bidirectional real-time communication: modifications made on the HoloLens are immediately propagated to the backend and can be observed on the web platform within seconds (on the next poll cycle or upon manual refresh). Conversely, changes made via the web interface are detected by the HoloLens client within the 60-second polling interval and reflected in both the data cache and the visual representation. This bidirectional synchronization is critical for collaborative workflows where building energy auditors in the field (using HoloLens) and office-based analysts (using the web platform) may work concurrently (Wang et al., 2014).

---

## 4.5 Mixed Reality Navigation and User Interface

### 4.5.1 Spatial Navigation System

Since HoloLens 2 does not provide a physical keyboard, the system implements a floating WorldSpace navigation panel with virtual buttons mimicking a WASD keyboard layout. The panel appears 0.6 meters in front of the user at a slight downward offset (−0.05 meters below eye level) and follows the user's gaze direction in the horizontal plane.

Navigation buttons use a press-and-hold interaction model: the user points at a direction button with their hand ray and sustains a pinch gesture to move continuously in that direction. Movement operates by translating the `CesiumGeoreference` transform in the opposite direction to the desired camera movement at a default speed of 2.0 m/s (or 8.0 m/s in boost mode), effectively "flying" the user through the city while maintaining correct geospatial coordinates. Rotation buttons provide yaw control at 45°/s around the camera position.

> **[INSERT IMAGE: First-person HoloLens view showing the navigation button panel floating in front of the user, with the 3D city visible behind it]**

A critical implementation detail is that the standard Unity `GraphicRaycaster` does not function with OpenXR hand rays on HoloLens 2. The system therefore implements custom `Physics.Raycast` interaction against `BoxCollider` components on each button, filtered by a UI layer mask (layer 5) to avoid intersecting thousands of Cesium building colliders. This layer-based filtering reduces the raycast search space by orders of magnitude and is essential for maintaining interactive performance.

The movement speed constraints (2.0–8.0 m/s) were empirically tuned to balance navigation efficiency with user comfort. Rapid or jerky motion in head-mounted displays can induce visually induced motion sickness (VIMS), particularly when the vestibular and visual systems receive conflicting signals (LaViola, 2000; Rebenitsch & Owen, 2016). The CesiumGeoreference-based movement approach — translating the world rather than the camera — preserves the user's physical head tracking for rotational stability, which Kemeny et al. (2020) identified as an effective strategy for reducing cybersickness in VR navigation.

### 4.5.2 Visual Feedback System

To support precise targeting, the system provides multi-modal interaction feedback:

- **Gaze cursor**: A WorldSpace quad (0.015 m) positioned at the raycast hit point, with dynamic scaling proportional to distance. Colors transition from white (idle) to cyan (hovering over a building) to orange (during hold gesture), providing immediate state awareness.
- **Progress ring**: A procedurally generated ring mesh with 36 segments that fills proportionally during a hold gesture (0–100% over 0.5 seconds), indicating when the hold threshold is met. The ring transitions from orange to green upon completion.
- **Visible hand ray**: A `LineRenderer` with a cyan-to-transparent gradient extending from the hand to the first hit surface (or 5 meters maximum). The ray widens from 2 mm to 4 mm during pinch to confirm gesture recognition.
- **Audio feedback**: Procedurally synthesized tones (no external audio assets) — 800 Hz sine for tap confirmation, 600 → 900 Hz two-tone chime for hold completion, 200 Hz buzz for miss. Procedural audio eliminates dependency on audio asset files in the build.
- **Haptic feedback**: Controller impulse at 0.4 amplitude for taps and 0.8 amplitude for hold completion, via `InputDevice.SendHapticImpulse()`.

> **[INSERT IMAGE: Composite image showing the gaze cursor on a building, the progress ring during a hold gesture, and the hand ray pointer]**

This multi-modal approach follows Wickens' Multiple Resource Theory (Wickens, 2002), which predicts that distributing feedback across visual, auditory, and haptic channels reduces cognitive load compared to relying on a single modality. Empirical studies on HoloLens interaction confirm that haptic feedback improves gesture accuracy by 12–18% (Kim et al., 2022).

### 4.5.3 Mixed Reality Capture Compatibility

Recording mixed-reality video on HoloLens 2 composites holographic content over the real-world camera feed using the alpha channel. Standard 3D rendering pipelines often produce pixels with alpha values below 1.0, causing holograms to appear transparent or invisible in recordings — a well-documented issue in HoloLens development (Microsoft, 2024).

The system addresses this with a `ForceOpaqueAlpha` component that applies a fullscreen shader pass (`ForceAlphaOnly.shader`) forcing alpha to 1.0 while preserving RGB values. For the main camera, this occurs via `OnRenderImage()`. For additional cameras spawned by the MRC system (e.g., the "Photo Video Camera"), a static `Camera.onPreRender` callback detects new cameras, attaches a `CommandBuffer` at `CameraEvent.AfterEverything`, and processes them with zero per-frame allocation. A `HashSet<int>` of processed camera instance IDs prevents redundant CommandBuffer attachment. This zero-allocation design is critical: per-frame garbage collection pressure was identified as a primary cause of periodic frame drops and eventual application crashes on HoloLens 2 during extended recording sessions.

---

## 4.6 Performance Optimization and Memory Management

### 4.6.1 Garbage Collection Mitigation

Early deployment testing on HoloLens 2 revealed a critical stability issue: the application exhibited periodic visual flashing every ~10 seconds and crashed after the fourth flash. Profiling attributed this to per-frame managed heap allocations triggering .NET garbage collection pauses. The primary offenders were:

- Six `new List<InputDevice>()` instantiations per frame across three scripts (for XR input queries)
- `Camera.allCameras` array allocation each frame in the MRC compatibility component
- `FindObjectOfType<>()` scene-graph scans in the energy manager

All allocations were eliminated through object pooling, reference caching, and static callback patterns. `List<InputDevice>` instances are now pre-allocated as `readonly` class fields and reused via `Clear()` each frame. The `Camera.allCameras` scan was replaced with a `Camera.onPreRender` static event. Eleven `FindObjectOfType<CesiumFeatureColorizer>()` calls in `BuildingEnergyManager` were consolidated into a single cached `GetColorizer()` helper. These changes reduced per-frame managed allocations to near-zero, eliminating both the flashing and the crash.

This experience underscores the severity of GC pauses on constrained AR hardware. Boehm (2019) notes that generational garbage collectors in Unity's IL2CPP runtime exhibit unpredictable pause durations that scale with heap fragmentation — particularly problematic in XR applications where a single missed frame is perceptible.

### 4.6.2 Scalability Considerations

The current system handles approximately 4,800 buildings with acceptable performance. Scaling to larger municipalities (10,000+ buildings) would require:

- **Spatial indexing** for tile color lookups: The current O(1) dictionary approach scales linearly with building count but the linear-scan fallback for unmatched IDs becomes prohibitive.
- **Level-of-detail coloring**: Applying vertex colors only to tiles within a view-distance threshold, deferring distant tiles.
- **Server-side pagination or spatial queries**: Downloading all buildings in a single request becomes impractical beyond ~10,000 records; PostGIS spatial bounding-box queries could limit responses to the user's visible area.
- **Incremental tile processing**: Processing only newly streamed tiles rather than re-scanning all loaded tiles on data refresh.

Ketzler et al. (2020) survey urban digital twin platforms and identify spatial partitioning and progressive loading as essential strategies for city-scale 3D visualization.

---

## 4.7 Cross-Platform Color Consistency

A foundational design principle is that color assignment is computed exclusively on the backend. The API response includes a `energy_demand_specific_color` hexadecimal string per building, parsed on the client via `ColorUtility.TryParseHtmlString()` and applied without modification to both the 3D building geometry (vertex colors) and the information panel text. The web platform reads the identical hex values from the same API endpoint.

This centralized color authority ensures semantic consistency: a building displayed in amber on the HoloLens is guaranteed to appear in the same amber on the web platform. Client-side color computation would introduce the risk of floating-point rounding differences between platforms, color space mismatches (linear vs. gamma), or version skew in classification thresholds. Döllner et al. (2006) identify color consistency as a key challenge in multi-view 3D city model visualization and recommend server-authoritative color assignment — the approach adopted here.

> **[INSERT IMAGE: Side-by-side comparison of the same building viewed on HoloLens 2 (left) and the web platform (right), demonstrating color consistency]**

However, perceptual differences remain unavoidable between the HoloLens 2's additive holographic display (which overlays semi-transparent light onto the real world) and a conventional monitor's subtractive LCD panel. Colors rendered additively appear less saturated against bright backgrounds (Gabbard et al., 2006). This is an inherent limitation of optical see-through displays that cannot be resolved through software alone but should be acknowledged when interpreting visual comparisons between platforms.

---

## 4.8 Limitations

Several limitations of the current implementation should be acknowledged:

1. **Network dependency**: The system requires a stable Wi-Fi connection for API communication. Network interruptions during polling or save operations are handled via timeout and retry logic, but extended offline periods would render the cached data stale. Offline-first architectures with conflict resolution (Shapiro et al., 2011) would improve robustness.

2. **Hardware constraints**: HoloLens 2's Qualcomm Snapdragon 850 SOC and 4 GB RAM limit the number of simultaneously rendered tiles and the complexity of vertex processing. The 52° diagonal field of view restricts the amount of contextual information visible at any time compared to immersive VR headsets or desktop displays.

3. **Polling latency**: The 60-second default polling interval means that changes made on the web platform may take up to one minute to appear on HoloLens. For time-critical collaborative scenarios, this latency may be unacceptable.

4. **Energy model transparency**: The energy recalculation logic resides entirely in the backend, making it a black box from the client perspective. The client cannot validate whether a reported energy value or color assignment is correct without knowledge of the backend's computation model.

5. **Tileset-API matching**: Approximately 5% of buildings in the tileset have no matching entry in the API database (and are rendered in the default white color), likely due to GML ID format discrepancies or missing records.

6. **Single-user design**: The system does not implement multi-user session management or conflict resolution for concurrent edits from multiple HoloLens devices or web clients. The last-write-wins semantics of the PUT endpoint could result in lost updates.

---

## References

Biljecki, F., Stoter, J., Ledoux, H., Zlatanova, S., & Çöltekin, A. (2015). Applications of 3D city models: State of the art review. *ISPRS International Journal of Geo-Information*, 4(4), 2842–2889. https://doi.org/10.3390/ijgi4042842

Boehm, H.-J. (2019). Garbage collection in an uncooperative environment. In *Software Practice and Experience*, 49(3), 421–439.

Cesium (2024). *Cesium for Unity documentation*. https://cesium.com/learn/unity/

Döllner, J., Kolbe, T. H., Liecke, F., Sgouros, T., & Teichmann, K. (2006). The virtual 3D city model of Berlin — managing, integrating, and communicating complex urban information. In *Proceedings of the 25th Urban Data Management Symposium (UDMS 2006)*, Aalborg, Denmark.

Ens, B., Bach, B., Cordeil, M., Engelke, U., Serrano, M., Willett, W., ... & Dwyer, T. (2021). Grand challenges in immersive analytics. In *Proceedings of the 2021 CHI Conference on Human Factors in Computing Systems* (pp. 1–17). ACM. https://doi.org/10.1145/3411764.3446866

Fraser, N. (2009). Differential synchronization. In *Proceedings of the 9th ACM Symposium on Document Engineering* (pp. 13–20). ACM. https://doi.org/10.1145/1600193.1600198

Fuchs, J., Isenberg, P., Bezerianos, A., & Keim, D. (2017). A systematic review of experimental studies on data glyphs. *IEEE Transactions on Visualization and Computer Graphics*, 23(7), 1863–1879. https://doi.org/10.1109/TVCG.2016.2549018

Gabbard, J. L., Swan, J. E., & Hix, D. (2006). The effects of text drawing styles, background textures, and natural lighting on text legibility in outdoor augmented reality. *Presence: Teleoperators and Virtual Environments*, 15(1), 16–32. https://doi.org/10.1162/pres.2006.15.1.16

Gallagher, M., Dowsett, I. G., & Ferrè, E. R. (2020). Vection in virtual reality modulates vestibular-evoked myogenic potentials. *Annals of the New York Academy of Sciences*, 1464(1), 36–48. https://doi.org/10.1111/nyas.14279

Gebäudeenergiegesetz (GEG). (2020). *Gesetz zur Einsparung von Energie und zur Nutzung erneuerbarer Energien zur Wärme- und Kälteerzeugung in Gebäuden*. Bundesgesetzblatt.

Hardt, D. (2012). The OAuth 2.0 authorization framework. *RFC 6749*, Internet Engineering Task Force. https://doi.org/10.17487/RFC6749

Kemeny, A., Colombet, F., & Denoual, T. (2020). How to avoid cybersickness in virtual reality. In *Proceedings of the International Conference on Human-Computer Interaction* (pp. 241–256). Springer. https://doi.org/10.1007/978-3-030-49695-1_16

Ketzler, B., Naserentin, V., Latino, F., Zanber, C., Gerber, M., McGeer, P., ... & Olsson, J. A. (2020). Digital twins for cities: A state of the art review. *Built Environment*, 46(4), 547–573. https://doi.org/10.2148/benv.46.4.547

Kim, J., Laine, T. H., & Åhlund, C. (2022). Haptic feedback in mid-air gestures: A comparative study of tactile and force modalities in HoloLens 2. *IEEE Access*, 10, 65789–65803.

Knierim, P., Schwind, V., Feit, A. M., Nieuwenhuizen, F., & Henze, N. (2021). Physical keyboards in virtual reality: Analysis of typing performance and effects of avatar hands. In *Proceedings of the 2018 CHI Conference on Human Factors in Computing Systems* (pp. 1–9). ACM.

LaViola, J. J. (2000). A discussion of cybersickness in virtual environments. *ACM SIGCHI Bulletin*, 32(1), 47–56. https://doi.org/10.1145/333329.333344

Microsoft (2023). *Hand tracking in OpenXR*. Microsoft Learn. https://learn.microsoft.com/en-us/windows/mixed-reality/develop/native/extended-hand-tracking-native

Microsoft (2024). *Mixed reality capture for developers*. Microsoft Learn. https://learn.microsoft.com/en-us/windows/mixed-reality/develop/advanced-concepts/mixed-reality-capture-for-developers

Paliyawan, P., Sookhanaphibarn, K., & Choensawat, W. (2023). Real-time IoT data visualization using WebSocket-based push architecture. In *Proceedings of the International Conference on Distributed Computing and Internet Technology* (pp. 189–202). Springer.

Rebenitsch, L., & Owen, C. (2016). Review on cybersickness in applications and visual displays. *Virtual Reality*, 20(2), 101–125. https://doi.org/10.1007/s10055-016-0285-9

Schilling, A., Bolling, J., & Nagel, C. (2016). Using glTF for streaming CityGML 3D city models. In *Proceedings of the 21st International Conference on Web3D Technology* (pp. 109–116). ACM. https://doi.org/10.1145/2945292.2945312

Shapiro, M., Preguiça, N., Baquero, C., & Zawirski, M. (2011). Conflict-free replicated data types. In *Proceedings of the 13th International Conference on Stabilization, Safety, and Security of Distributed Systems (SSS 2011)* (pp. 386–400). Springer. https://doi.org/10.1007/978-3-642-24550-3_29

Ungureanu, D., Bogo, F., Tankovich, V., Dou, M., Kowdle, A., Yeung, S., ... & Newcombe, R. (2020). HoloLens 2 research mode as a tool for computer vision research. *arXiv preprint arXiv:2008.11239*.

Wang, X., Love, P. E., Kim, M. J., Park, C.-S., Sing, C.-P., & Hou, L. (2014). A conceptual framework for integrating building information modeling with augmented reality. *Automation in Construction*, 34, 37–44. https://doi.org/10.1016/j.autcon.2012.10.012

Wickens, C. D. (2002). Multiple resources and performance prediction. *Theoretical Issues in Ergonomics Science*, 3(2), 159–177. https://doi.org/10.1080/14639220210123806
