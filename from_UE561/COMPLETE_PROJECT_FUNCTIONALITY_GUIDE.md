# Complete Project Functionality Guide: Building Energy Visualization System

## Project Overview

This document provides a comprehensive explanation of all functionalities implemented in the Real-Time Building Energy Visualization System, from initial setup through HoloLens 2 deployment. The system integrates Unreal Engine 5.6, Cesium 3D Tiles, and a RESTful backend API to create an immersive mixed reality application for building energy data visualization and management.

---

## Table of Contents

1. [System Architecture and Core Components](#1-system-architecture-and-core-components)
2. [Authentication and Security System](#2-authentication-and-security-system)
3. [Data Loading and Caching System](#3-data-loading-and-caching-system)
4. [Building Selection and Interaction](#4-building-selection-and-interaction)
5. [Building Attribute Display and Forms](#5-building-attribute-display-and-forms)
6. [Data Modification and Updates](#6-data-modification-and-updates)
7. [Color Visualization System](#7-color-visualization-system)
8. [Real-Time Data Updates](#8-real-time-data-updates)
9. [Cesium 3D Tiles Integration](#9-cesium-3d-tiles-integration)
10. [HoloLens 2 Mixed Reality Deployment](#10-hololens-2-mixed-reality-deployment)
11. [API Endpoints and Configuration](#11-api-endpoints-and-configuration)
12. [Credentials and Access Information](#12-credentials-and-access-information)

---

## 1. System Architecture and Core Components

### 1.1 Three-Tier Architecture

The system employs a three-tier architecture consisting of:

**Backend Service Layer**: The backend is hosted at `https://backend.gisworld-tech.com` and provides RESTful API endpoints for building energy data. The Django REST framework backend connects to a PostgreSQL spatial database containing building geometries (CityGML format) enriched with real-time energy consumption data. The backend handles authentication via JWT tokens, data validation, and coordinate system transformations between WGS84 geodetic coordinates and application-specific formats.

**Unreal Engine Client Layer**: The Unreal Engine 5.6 client application implements the core visualization logic through a custom C++ actor class `ABuildingEnergyDisplay`. This actor manages the complete lifecycle of data operations including authentication, data fetching, caching, coordinate mapping, user interaction handling, and visual representation. The client maintains multiple in-memory caches for building metadata, GML ID mappings, and color assignments to minimize network requests while ensuring data freshness.

**Visualization Layer**: The Cesium for Unreal plugin provides 3D geospatial visualization capabilities by rendering building tilesets generated from CityGML data. The Cesium tileset streams optimized 3D Tiles data from Cesium Ion cloud storage, implementing Level of Detail (LOD) management for performance optimization. The visualization layer applies per-building color coding through Cesium's feature-based styling system, enabling dynamic visual representation of energy consumption patterns across the entire building portfolio.

### 1.2 Core Components and Their Functions

**ABuildingEnergyDisplay Actor**: This is the central C++ actor class that orchestrates all system functionalities. It inherits from AActor and is placed in the Unreal level to manage building energy visualization. The actor exposes Blueprint-callable functions for authentication, data loading, building selection, and color application, enabling seamless integration with Blueprint visual scripting for UI events and user interaction workflows.

**BuildingAttributesWidget (UMG)**: This Unreal Motion Graphics widget provides the user interface for displaying and editing building attributes. Implemented as a C++ class with UMG designer integration, the widget contains text fields for displaying building information (GML ID, energy consumption, CO2 emissions, building type) and interactive form controls (dropdowns, input fields, buttons) for data modification. The widget uses the BindWidget meta property to automatically connect C++ variables to UMG designer elements, ensuring type-safe UI component access.

**HoloLensInputConverter**: This specialized component handles cross-platform input abstraction for mixed reality deployment. It converts traditional mouse clicks into gesture-based interactions (air-tap, tap-and-hold) for HoloLens 2, implements gaze-ray targeting for building selection in mixed reality environments, and provides platform detection to automatically switch between desktop mouse input and HoloLens gestural input.

**Cesium3DTileset Integration**: The Cesium tileset actor named "Cesium3DTileset_0" is referenced by the BuildingEnergyDisplay actor to apply color styling. The integration involves querying Cesium metadata through the CesiumFeaturesMetadataComponent to extract building identifiers (GML IDs) from 3D Tiles metadata, applying Cesium styling expressions via JSON-based style specifications to achieve per-building coloring, and handling tileset refresh events to maintain color persistence across LOD changes and tile streaming operations.

### 1.3 Data Flow Pipeline

The complete data flow follows this sequence:

1. **Authentication**: User authenticates through Blueprint UI, providing username and password credentials to obtain JWT access token and refresh token from the backend API
2. **Data Preloading**: The `PreloadAllBuildingData()` function makes an HTTP GET request to fetch all building energy data for the Bisingen community (community_id: 08417008)
3. **JSON Parsing**: Response JSON is parsed to extract building metadata including GML IDs, coordinates, energy consumption values, CO2 emission data, and color assignments
4. **Cache Population**: Extracted data populates three cache structures: `BuildingDataCache` (GML ID → building data), `GmlIdCache` (coordinates → GML ID), and `BuildingColorCache` (GML ID → color values)
5. **User Interaction**: User clicks on buildings in the 3D scene, triggering raycast collision detection to identify the clicked building
6. **Metadata Query**: Cesium metadata component extracts the building's GML ID from 3D Tiles feature properties
7. **Data Display**: Building data is retrieved from cache and displayed in the UMG widget interface
8. **Data Modification**: User edits building attributes through form controls, which triggers HTTP PUT request to update backend database
9. **Visual Update**: Color cache is updated and Cesium styling is reapplied to reflect changes in the 3D visualization

---

## 2. Authentication and Security System

### 2.1 JWT Authentication Implementation

The system implements OAuth2-style JWT (JSON Web Token) authentication for secure API access. The authentication flow begins with the user providing credentials through a Blueprint-based login interface. The credentials are sent to the backend authentication endpoint, which validates them against the PostgreSQL user database and returns two tokens upon successful authentication: an access token (short-lived, typically 1 hour expiration) used for authorizing API requests, and a refresh token (long-lived, typically 7 days) used to obtain new access tokens without re-entering credentials.

The `ABuildingEnergyDisplay` actor stores both tokens in memory as FString properties:
- `AccessToken`: Used in the Authorization header as "Bearer <token>" for all API requests
- `RefreshToken`: Used to call the token refresh endpoint when access token expires

### 2.2 Automatic Token Refresh Mechanism

The system implements automatic token refresh to maintain uninterrupted data access. When an API request receives an HTTP 401 Unauthorized response, indicating token expiration, the `RefreshAccessToken()` function is automatically invoked. This function makes a POST request to the token refresh endpoint with the refresh token in the request body, receives a new access token in the response, updates the `AccessToken` property with the new value, and retries the original API request with the refreshed token.

This mechanism ensures seamless user experience without requiring re-authentication during active sessions. The implementation handles edge cases including refresh token expiration (requiring full re-authentication), network failures during token refresh, and concurrent refresh requests.

### 2.3 SSL/TLS Configuration

For development environments, the Unreal Engine project is configured to disable SSL peer verification to accommodate self-signed certificates and internal testing environments. This configuration is set in the `Config/DefaultEngine.ini` file:

```ini
[/Script/Engine.NetworkSettings]
n.VerifyPeer=False
```

**Important Security Note**: This setting should NEVER be used in production deployments. For production environments, proper SSL/TLS certificates must be installed and peer verification must be enabled to prevent man-in-the-middle attacks and ensure secure data transmission. The backend API (`https://backend.gisworld-tech.com`) uses HTTPS encryption for all communications, protecting authentication tokens and building data during transmission.

### 2.4 Authorization Flow in Blueprint

The Blueprint implementation in `BP_BuildingEnergyDisplay` provides the user-facing authentication interface. The Blueprint's BeginPlay event creates a login UI widget where users enter their username and password. Upon clicking the login button, the Blueprint calls the `AuthenticateAndLoadData()` C++ function, which initiates the authentication request. After successful authentication, the Blueprint automatically calls `PreloadAllBuildingData()` to fetch building data using the newly acquired access token. The Blueprint also handles authentication failure scenarios by displaying error messages to the user and maintaining the login interface for retry attempts.

---

## 3. Data Loading and Caching System

### 3.1 Preload All Building Data Function

The `PreloadAllBuildingData(const FString& Token)` function is the primary data acquisition method. This function performs the following operations:

**Cache Initialization**: Clears all existing caches (`BuildingDataCache.Empty()`, `GmlIdCache.Empty()`, `BuildingColorCache.Empty()`) to ensure fresh data and prevent stale information from persisting across reload operations.

**HTTP Request Configuration**: Creates an IHttpRequest object through the FHttpModule, constructs the API URL with query parameters for the Bisingen community, sets the HTTP verb to GET, configures headers including Content-Type (application/json), Accept (application/json), and Authorization (Bearer <access_token>), and sets a 30-second timeout to handle slow server responses.

**Request Parameters**: The complete API endpoint is constructed as:
```
https://backend.gisworld-tech.com/geospatial/buildings-energy/
?community_id=08417008
&format=json
&include_colors=true
&energy_type=total
&time_period=annual
&classification=co2
&color_scheme=co2_classes
```

These parameters specify Bisingen's community ID (08417008), request JSON format for easy parsing, include pre-calculated color values from the backend, specify total energy demand as the energy type, request annual time period data, use CO2 emissions for classification, and apply the CO2 classes color scheme for visualization.

**Asynchronous Callback Binding**: The request binds the `OnPreloadResponseReceived()` function as the completion callback using `OnProcessRequestComplete().BindUObject()`. This ensures that response processing occurs on the game thread and maintains actor context for cache updates.

### 3.2 JSON Response Parsing

The `OnPreloadResponseReceived()` callback function handles the API response and populates the cache structures. The parsing process follows this sequence:

**Response Validation**: Checks if the request completed successfully (bSuccess flag), validates that the response code is 200 (HTTP OK), and verifies that the response content string is not empty.

**JSON Deserialization**: Creates a TJsonReader to parse the response string, uses FJsonSerializer::Deserialize() to convert the JSON string into a TSharedPtr<FJsonObject>, and handles parsing errors by logging detailed error messages including the response content for debugging.

**Building Data Extraction**: The JSON response contains an array of building objects in the "buildings" field. For each building object, the parser extracts the modified_gml_id (primary building identifier), gml_id (alternative identifier format), geometry data (GeoJSON FeatureCollection with coordinate arrays), building_type (residential, commercial, etc.), and energy_results array containing temporal energy consumption data.

**Energy Results Processing**: Each energy_result object contains begin_date and end_date defining the temporal range, energy_value representing consumption in kWh, co2_emission value in kg CO2, and color (hexadecimal string like "#FF5733") for visualization. The parser selects the most recent time period data for display and stores complete historical data for potential time-series visualization features.

**Coordinate Extraction and Caching**: The geometry processing handles complex GeoJSON structures including nested coordinate arrays (polygons within MultiPolygons). For each coordinate pair extracted, the system creates a cache entry mapping the coordinate to the building's GML ID using FVector2D as the key type. This enables coordinate-based building lookups during raycast intersection operations. The coordinate extraction includes defensive programming to handle various nesting levels and malformed coordinate data gracefully.

### 3.3 Multi-Level Cache Architecture

The system maintains three interconnected cache structures:

**BuildingDataCache (TMap<FString, TSharedPtr<FJsonObject>>)**: Maps GML ID strings to complete building JSON objects, enables instant building data retrieval without network requests, supports O(1) lookup complexity for building attribute queries, and stores all building metadata including geometry, energy values, and classification data.

**GmlIdCache (TMap<FVector2D, FString>)**: Maps 2D coordinates (building positions) to GML ID strings, enables coordinate-based building identification during 3D scene interaction, handles buildings with identical GML IDs but different locations through coordinate uniqueness, and supports spatial queries for building selection via raycast intersection.

**BuildingColorCache (TMap<FString, FLinearColor>)**: Maps GML ID strings to FLinearColor values for Cesium visualization, stores pre-calculated colors from backend energy classification, enables rapid color application without re-parsing JSON data, and maintains color persistence across Cesium tileset refresh operations.

### 3.4 Cache Persistence and Management

The cache system implements intelligent lifecycle management:

**Session Persistence**: Caches persist during the entire play session, surviving level transitions within the same play session, maintained across multiple building interactions, and preserved during Cesium tileset LOD changes and tile streaming operations.

**Cache Invalidation**: The `ClearCache()` function empties all three cache structures, typically called before re-loading data or when switching between different communities. The `RefreshBuildingCache()` function calls `PreloadAllBuildingData()` with the existing access token to update cached data without requiring re-authentication.

**Memory Optimization**: The cache stores TSharedPtr objects to avoid deep copying large JSON structures, uses efficient TMap data structures with hash-based lookup, and automatically cleans up memory when the actor is destroyed through Unreal's garbage collection system.

---

## 4. Building Selection and Interaction

### 4.1 Desktop Mouse Click Interaction

On desktop platforms (Windows, Mac, Linux), building selection occurs through mouse click events. The interaction flow follows this sequence:

**Mouse Click Event**: The player controller receives mouse button down events from the operating system. The Blueprint event graph captures "OnClicked" events and propagates them to the BuildingEnergyDisplay actor. The event includes screen-space coordinates (X, Y pixel positions) of the mouse cursor at click time.

**Screen-to-World Raycast**: The system converts 2D screen coordinates to a 3D world-space ray using the player controller's `GetHitResultUnderCursor()` function. This function performs a line trace from the camera position through the clicked screen pixel into the 3D scene. The raycast checks for collisions with the Cesium tileset geometry using the ECC_Visibility collision channel.

**Hit Result Analysis**: If the raycast intersects geometry, a FHitResult structure contains the hit actor (the Cesium tileset actor), hit location (3D world coordinates of the intersection point), hit component (the specific mesh component that was hit), and face index (polygon index within the mesh, if available).

**Cesium Metadata Extraction**: Once the Cesium tileset actor is identified, the system queries for the CesiumFeaturesMetadataComponent attached to the actor. This component provides access to 3D Tiles feature metadata stored within the tileset. The metadata query uses the face index or pick coordinates to identify which building feature was intersected. The metadata component returns feature properties including the building's GML ID, stored in metadata fields like "modified_gml_id", "gml_id", "DEBWL", or other property names depending on the tileset's metadata schema.

**GML ID Resolution**: The extracted metadata may contain the GML ID in various formats (DEBW_0010008 vs DEBWL0010008). The system uses the GmlIdCache to resolve coordinate-based lookups and supports both identifier formats for cache queries. If the exact GML ID is not found, the system attempts alternate formats by replacing underscores with 'L' characters.

### 4.2 HoloLens 2 Gesture-Based Interaction

For HoloLens 2 mixed reality deployment, mouse click interaction is replaced with gesture-based selection:

**Gaze Ray Targeting**: The HoloLens head tracking provides continuous 6DOF (six degrees of freedom) head pose data. The system uses the head position as the ray origin and the forward vector (direction the user is looking) as the ray direction. A continuous raycast is performed along the gaze ray to provide real-time feedback highlighting the building the user is looking at.

**Air-Tap Gesture Detection**: When the user performs an air-tap gesture (pinching index finger and thumb together), the HoloLens gesture recognition system generates a "Select" action event. The HoloLensInputConverter component captures this event and translates it into a building selection command equivalent to a mouse click on desktop. The gesture event triggers the same `OnBuildingClicked()` Blueprint function used for desktop mouse interaction, ensuring platform-independent behavior.

**Visual Feedback**: The system provides visual feedback during targeting by highlighting the currently gazed-at building with an outline effect or subtle color change. When the air-tap gesture is initiated, the system displays a visual confirmation (pulse effect, color flash) before processing the selection. After successful selection, the building attributes widget appears in the user's field of view at a comfortable reading distance (1-2 meters from the HoloLens).

### 4.3 Building Click Handler Implementation

The `OnBuildingClicked()` function (implemented in Blueprint and C++) processes building selection events:

**Step 1: Hit Detection Validation**: Verifies that the raycast hit a valid actor, confirms that the hit actor is the Cesium tileset (matches the BuildingsTilesetName property), and checks that the hit location is within expected coordinate bounds.

**Step 2: Metadata Query**: Retrieves the CesiumFeaturesMetadataComponent from the hit actor using `FindComponentByClass<UCesiumFeaturesMetadataComponent>()`. Queries the metadata component for the building's GML ID using the hit face index or pick coordinates. Handles multiple potential metadata field names through a candidate list (tries "modified_gml_id", "gml_id", "DEBWL", etc. in sequence).

**Step 3: Cache Lookup**: Uses the extracted GML ID to query BuildingDataCache for complete building data. If direct lookup fails, attempts alternate GML ID formats (underscore/L character substitution). Falls back to coordinate-based lookup using GmlIdCache if metadata extraction fails.

**Step 4: Data Display Trigger**: If building data is found in cache, calls `DisplayBuildingData(GmlId)` to show the building attributes widget. If data is not in cache (rare edge case), triggers a single-building fetch request to the API. Logs detailed debug information for troubleshooting selection issues.

**Step 5: Visual Update**: Optionally applies a highlight color or outline effect to the selected building. Updates the `CurrentlyDisplayedBuildingId` property to track the active selection. Sends telemetry events for user interaction analytics if enabled.

---

## 5. Building Attribute Display and Forms

### 5.1 UMG Widget Architecture

The BuildingAttributesWidget is implemented as a UMG (Unreal Motion Graphics) user interface widget with both visual designer and C++ backing. The widget structure includes:

**Display-Only Text Fields**: 
- **Building ID Label**: Displays the modified_gml_id or gml_id in large, readable text
- **Energy Consumption Display**: Shows annual total energy consumption in kWh with formatted thousands separators (e.g., "125,430 kWh")
- **CO2 Emissions Display**: Shows CO2 emissions in kg CO2 with environmental impact categorization
- **Building Type Indicator**: Displays building classification (Residential, Commercial, Industrial, etc.)
- **Coordinate Information**: Shows building location in lat/lon or local coordinates

**Editable Form Controls**:
- **Building Type Dropdown**: ComboBox widget populated with predefined building types, allows users to reclassify buildings, updates immediately upon selection change
- **Energy Value Input**: Numeric input field with validation to prevent non-numeric entries, supports copy/paste operations, includes min/max range validation
- **Notes/Comments Field**: Multi-line text box for adding descriptive information or modification notes
- **Date Pickers**: Widgets for selecting energy data time ranges (begin_date, end_date)

**Action Buttons**:
- **Save Changes Button**: Triggers HTTP PUT request to update backend database with modified values
- **Cancel Button**: Discards changes and closes the widget without saving
- **Close Button**: Closes the widget while preserving displayed data
- **Refresh Button**: Reloads building data from the server to fetch latest updates

### 5.2 Widget Binding with BindWidget

The C++ implementation uses the BindWidget meta specifier to connect UMG designer elements to C++ variables automatically:

```cpp
// In BuildingAttributesWidget.h
UPROPERTY(BlueprintReadOnly, Category = "UI", meta = (BindWidget))
UTextBlock* BuildingIdText;

UPROPERTY(BlueprintReadOnly, Category = "UI", meta = (BindWidget))
UTextBlock* EnergyConsumptionText;

UPROPERTY(BlueprintReadOnly, Category = "UI", meta = (BindWidget))
UComboBoxString* BuildingTypeDropdown;

UPROPERTY(BlueprintReadOnly, Category = "UI", meta = (BindWidget))
UEditableTextBox* EnergyValueInput;

UPROPERTY(BlueprintReadOnly, Category = "UI", meta = (BindWidget))
UButton* SaveChangesButton;
```

The `BindWidget` meta property ensures that the UMG Designer elements with matching names are automatically connected to these C++ pointers during widget initialization. If a widget element is missing or has a mismatched name, Unreal Engine logs an error during widget construction, helping catch UI configuration errors early in development.

### 5.3 Widget Initialization and Data Population

The `NativeConstruct()` function is called when the widget is added to the viewport, serving as the initialization point for widget setup:

**Button Event Binding**: The function connects button click events to C++ handler functions:
```cpp
void UBuildingAttributesWidget::NativeConstruct()
{
    Super::NativeConstruct();
    
    if (SaveChangesButton)
    {
        SaveChangesButton->OnClicked.AddDynamic(this, &UBuildingAttributesWidget::OnSaveChangesClicked);
    }
    
    if (CancelButton)
    {
        CancelButton->OnClicked.AddDynamic(this, &UBuildingAttributesWidget::OnCancelClicked);
    }
    
    // Bind other button events...
}
```

**Data Population**: The `SetBuildingData()` function is called by the BuildingEnergyDisplay actor to populate the widget with building information:
```cpp
void UBuildingAttributesWidget::SetBuildingData(const FString& GmlId, const TSharedPtr<FJsonObject>& BuildingData)
{
    if (!BuildingData.IsValid()) return;
    
    // Set building ID
    if (BuildingIdText)
    {
        BuildingIdText->SetText(FText::FromString(GmlId));
    }
    
    // Extract and display energy consumption
    double EnergyValue = 0.0;
    if (BuildingData->TryGetNumberField("total_energy_demand", EnergyValue))
    {
        FString FormattedEnergy = FString::Printf(TEXT("%.2f kWh"), EnergyValue);
        EnergyConsumptionText->SetText(FText::FromString(FormattedEnergy));
    }
    
    // Populate building type dropdown
    FString BuildingType;
    if (BuildingData->TryGetStringField("building_type", BuildingType))
    {
        BuildingTypeDropdown->SetSelectedOption(BuildingType);
    }
    
    // Store original values for change detection
    OriginalBuildingData = BuildingData;
}
```

**Dynamic Content Updates**: As users interact with form controls, the widget tracks changes and enables/disables the Save button based on whether modifications were made. The change detection compares current form values with the OriginalBuildingData stored during initialization.

### 5.4 Form Validation and User Feedback

The widget implements comprehensive validation to ensure data integrity before submitting changes:

**Input Validation Rules**:
- Energy values must be positive numbers within reasonable ranges (0 to 1,000,000 kWh)
- Building types must match predefined categories from the backend enum
- Dates must be valid calendar dates with end_date after begin_date
- Text fields enforce maximum character limits to prevent database overflow

**Real-Time Validation Feedback**: Input fields display validation status through color coding (green border for valid, red border for invalid values), error message text appears below invalid fields explaining the issue, and the Save button remains disabled until all validation rules pass.

**User Confirmation for Destructive Changes**: If users modify energy values significantly (more than 50% change), the system displays a confirmation dialog to prevent accidental data corruption. The dialog summarizes the changes and requires explicit user confirmation before proceeding with the update.

---

## 6. Data Modification and Updates

### 6.1 HTTP PUT Request Implementation

When users click the Save Changes button after editing building attributes, the widget triggers the `UpdateBuildingAttributes()` function in the BuildingEnergyDisplay actor. This function constructs and sends an HTTP PUT request to modify backend database records:

**Step 1: Gather Modified Data**: The widget collects current form values from all editable controls, compares them with OriginalBuildingData to identify actual changes, and constructs a JSON object containing only the modified fields (partial update strategy).

**Step 2: JSON Payload Construction**: Creates a JSON object structure matching the backend API's expected format:
```cpp
TSharedPtr<FJsonObject> UpdatePayload = MakeShared<FJsonObject>();
UpdatePayload->SetStringField("modified_gml_id", CurrentGmlId);
UpdatePayload->SetStringField("building_type", NewBuildingType);
UpdatePayload->SetNumberField("total_energy_demand", NewEnergyValue);
UpdatePayload->SetNumberField("co2_emission", NewCO2Value);
// Add other modified fields...

FString PayloadString;
TSharedRef<TJsonWriter<>> JsonWriter = TJsonWriterFactory<>::Create(&PayloadString);
FJsonSerializer::Serialize(UpdatePayload.ToSharedRef(), JsonWriter);
```

**Step 3: HTTP Request Configuration**: The PUT request is configured with the following parameters:
```cpp
TSharedRef<IHttpRequest, ESPMode::ThreadSafe> HttpRequest = FHttpModule::Get().CreateRequest();

// Endpoint: https://backend.gisworld-tech.com/geospatial/buildings-energy/{gml_id}/
FString URL = FString::Printf(
    TEXT("https://backend.gisworld-tech.com/geospatial/buildings-energy/%s/"),
    *CurrentGmlId
);

HttpRequest->SetURL(URL);
HttpRequest->SetVerb("PUT");
HttpRequest->SetHeader("Content-Type", "application/json");
HttpRequest->SetHeader("Authorization", FString::Printf(TEXT("Bearer %s"), *AccessToken));
HttpRequest->SetContentAsString(PayloadString);
HttpRequest->SetTimeout(30.0f);
```

**Step 4: Response Callback Binding**: The request binds `OnUpdateResponseReceived()` as the completion callback to handle success or failure responses:
```cpp
HttpRequest->OnProcessRequestComplete().BindUObject(
    this, 
    &ABuildingEnergyDisplay::OnUpdateResponseReceived,
    CurrentGmlId // Pass GML ID as parameter to callback
);

HttpRequest->ProcessRequest();
```

### 6.2 Update Response Handling

The `OnUpdateResponseReceived()` callback processes the server's response to the update request:

**Success Path (HTTP 200/201)**:
1. Validates that the response code is 200 (OK) or 201 (Created)
2. Parses the JSON response containing the updated building data
3. Updates BuildingDataCache with the new data to maintain cache consistency
4. If color values changed, updates BuildingColorCache and reapplies Cesium styling
5. Displays success notification to the user ("Building data updated successfully")
6. Keeps the widget open with updated values to allow further edits

**Error Handling**:
- **HTTP 400 (Bad Request)**: Validation error from backend. Parse error message from response JSON and display field-specific errors to user.
- **HTTP 401 (Unauthorized)**: Token expired. Automatically call `RefreshAccessToken()` and retry the update request with the new token.
- **HTTP 404 (Not Found)**: Building not found in backend database. Display error message indicating data synchronization issue.
- **HTTP 500 (Server Error)**: Backend server issue. Log error details, display generic error message to user, and suggest retrying later.
- **Network Timeout**: No response within 30 seconds. Display timeout error and offer retry option.

**Optimistic UI Updates**: For better user experience, the system implements optimistic updates where the UI immediately reflects changes while the server request is in flight. If the server request fails, the UI reverts to the previous state and displays an error notification. This prevents the application from appearing unresponsive during network operations.

### 6.3 Cache Synchronization After Updates

Maintaining cache consistency is critical after successful updates:

**Immediate Cache Update**: As soon as the server confirms the update (HTTP 200 response), the BuildingDataCache entry for the modified building is updated with the response data. This ensures that subsequent building clicks show the updated data without requiring a full cache refresh.

**Color Cache Update**: If the update affected energy consumption or CO2 values, the backend may return a new color assignment reflecting the building's new classification. The BuildingColorCache is updated with this new color, and `ApplyColorsUsingCesiumStyling()` is called to refresh the visual representation immediately.

**Coordinate Cache Considerations**: If the update modified building geometry (rare, but possible through advanced admin interfaces), the GmlIdCache may need rebuilding to reflect new coordinate mappings. The system detects geometry changes by comparing coordinate arrays before and after updates.

**Real-Time Update Propagation**: If the system is in WebSocket real-time mode (optional feature), the update is also broadcast to other connected clients viewing the same building. This ensures multi-user consistency when multiple stakeholders are collaborating on building data management.

---

## 7. Color Visualization System

### 7.1 Color Classification and Energy Ranges

The color visualization system uses predefined CO2 emission ranges to classify building energy performance. The backend API calculates CO2 emissions based on annual energy consumption and applies a color scheme to indicate environmental impact:

**CO2 Classification Ranges**:
- **Green (#00FF00)**: Excellent efficiency, < 50 kg CO2/m²/year
- **Yellow-Green (#7FFF00)**: Good efficiency, 50-100 kg CO2/m²/year
- **Yellow (#FFFF00)**: Average efficiency, 100-150 kg CO2/m²/year
- **Orange (#FFA500)**: Below average, 150-200 kg CO2/m²/year
- **Red (#FF0000)**: Poor efficiency, > 200 kg CO2/m²/year

These ranges are configured in the backend and can be adjusted to match regional building standards or different classification schemes (e.g., German EnEV standards, EU Energy Performance Certificate levels).

### 7.2 Cesium Tileset Styling Implementation

The system applies colors to buildings through Cesium's feature-based styling system. The implementation generates a Cesium-compatible style JSON specification that defines color assignment rules:

**Style JSON Generation**: The `GenerateCesiumStyleJson()` function creates a JSON style specification:
```cpp
FString ABuildingEnergyDisplay::GenerateCesiumStyleJson()
{
    TSharedPtr<FJsonObject> StyleObject = MakeShared<FJsonObject>();
    
    // Define color expression using conditional logic
    TArray<FString> ConditionClauses;
    
    // Add condition for each building
    for (const auto& ColorEntry : BuildingColorCache)
    {
        FString GmlId = ColorEntry.Key;
        FLinearColor Color = ColorEntry.Value;
        
        // Convert FLinearColor to Cesium color array [R, G, B, A]
        FString ColorArray = FString::Printf(
            TEXT("[%.3f, %.3f, %.3f, 1.0]"),
            Color.R, Color.G, Color.B
        );
        
        // Create condition: if GML ID matches, apply this color
        FString Condition = FString::Printf(
            TEXT("${modified_gml_id} === '%s'"),
            *GmlId
        );
        
        ConditionClauses.Add(FString::Printf(
            TEXT("  %s ? color(%s) :"),
            *Condition, *ColorArray
        ));
    }
    
    // Build complete style expression with cascading conditionals
    FString ColorExpression = TEXT("[\n");
    ColorExpression += FString::Join(ConditionClauses, TEXT("\n"));
    ColorExpression += TEXT("\n  color('#FFFFFF')\n]"); // Default white for unmatched buildings
    
    // Create complete style object
    StyleObject->SetStringField("color", ColorExpression);
    
    // Serialize to JSON string
    FString StyleJsonString;
    TSharedRef<TJsonWriter<>> JsonWriter = TJsonWriterFactory<>::Create(&StyleJsonString);
    FJsonSerializer::Serialize(StyleObject.ToSharedRef(), JsonWriter);
    
    return StyleJsonString;
}
```

**Applying Style to Tileset**: The generated style JSON is applied to the Cesium tileset actor:
```cpp
void ABuildingEnergyDisplay::ApplyColorsUsingCesiumStyling()
{
    // Find the Cesium tileset actor
    for (TActorIterator<ACesium3DTileset> It(GetWorld()); It; ++It)
    {
        ACesium3DTileset* Tileset = *It;
        if (Tileset->GetName().Contains(BuildingsTilesetName))
        {
            // Generate and apply style
            FString StyleJson = GenerateCesiumStyleJson();
            
            // Set the tileset style property
            Tileset->SetTilesetStyleFromJson(StyleJson);
            
            UE_LOG(LogTemp, Log, TEXT("✅ Applied Cesium styling with %d color rules"), 
                BuildingColorCache.Num());
            
            break;
        }
    }
}
```

### 7.3 Color Cache Population from API Response

During the `OnPreloadResponseReceived()` callback, colors are extracted from the API response and stored in the BuildingColorCache:

```cpp
// Extract color from energy_results array
const TArray<TSharedPtr<FJsonValue>>* EnergyResults;
if (BuildingObj->TryGetArrayField(TEXT("energy_results"), EnergyResults))
{
    if (EnergyResults->Num() > 0)
    {
        TSharedPtr<FJsonObject> LatestResult = (*EnergyResults)[0]->AsObject();
        
        // Get color string (e.g., "#FF5733")
        FString ColorHexString;
        if (LatestResult->TryGetStringField(TEXT("color"), ColorHexString))
        {
            // Convert hex string to FLinearColor
            FLinearColor BuildingColor = HexToLinearColor(ColorHexString);
            
            // Store in color cache
            BuildingColorCache.Add(ModifiedGmlId, BuildingColor);
            
            UE_LOG(LogTemp, Verbose, TEXT("  Cached color %s -> %s"), 
                *ModifiedGmlId, *ColorHexString);
        }
    }
}
```

**Hex Color Conversion Utility**: The `HexToLinearColor()` function converts hexadecimal color strings to Unreal's FLinearColor format:
```cpp
FLinearColor ABuildingEnergyDisplay::HexToLinearColor(const FString& HexString)
{
    // Remove '#' prefix if present
    FString CleanHex = HexString;
    if (CleanHex.StartsWith("#"))
    {
        CleanHex = CleanHex.RightChop(1);
    }
    
    // Validate hex string length
    if (CleanHex.Len() != 6)
    {
        UE_LOG(LogTemp, Warning, TEXT("Invalid hex color: %s"), *HexString);
        return FLinearColor::White; // Default to white
    }
    
    // Parse RGB components
    FString RHex = CleanHex.Left(2);
    FString GHex = CleanHex.Mid(2, 2);
    FString BHex = CleanHex.Right(2);
    
    // Convert hex to int and normalize to 0-1 range
    int32 R = FParse::HexNumber(*RHex);
    int32 G = FParse::HexNumber(*GHex);
    int32 B = FParse::HexNumber(*BHex);
    
    return FLinearColor(R / 255.0f, G / 255.0f, B / 255.0f, 1.0f);
}
```

### 7.4 Color Persistence Across Tileset Refreshes

Cesium tilesets dynamically load and unload tiles based on camera position and Level of Detail (LOD) requirements. This can cause applied colors to disappear when tiles are reloaded. The system implements color persistence to maintain consistent visualization:

**Refresh Monitoring**: A timer periodically checks if the Cesium tileset has reloaded tiles:
```cpp
void ABuildingEnergyDisplay::SetupCesiumRefreshMonitoring()
{
    // Set up timer to check for tileset refreshes every 2 seconds
    GetWorld()->GetTimerManager().SetTimer(
        CesiumRefreshTimer,
        this,
        &ABuildingEnergyDisplay::OnCesiumTilesetRefresh,
        2.0f,
        true // Loop
    );
}

void ABuildingEnergyDisplay::OnCesiumTilesetRefresh()
{
    // Reapply colors to maintain consistency
    if (!BuildingColorCache.IsEmpty())
    {
        ApplyColorsUsingCesiumStyling();
    }
}
```

**Event-Based Reapplication**: The system can also hook into Cesium's tile loading events (if available through the plugin API) to reapply colors immediately when new tiles are loaded, ensuring seamless visual consistency during navigation.

---

## 8. Real-Time Data Updates

### 8.1 Polling-Based Update System

The system implements a polling-based approach for checking building energy data updates. While WebSocket support is available in the codebase, the recommended production configuration uses REST API polling for reliability and simplicity:

**Polling Configuration**: The BuildingEnergyDisplay actor includes properties to configure the polling interval:
```cpp
// In BuildingEnergyDisplay.h
UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Building Energy|Updates")
float PollingIntervalSeconds = 60.0f; // Poll every 60 seconds

UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Building Energy|Updates")
bool bEnableAutoRefresh = true; // Enable automatic data refresh
```

**Polling Implementation**: A timer triggers periodic cache refresh operations:
```cpp
void ABuildingEnergyDisplay::BeginPlay()
{
    Super::BeginPlay();
    
    // Set up polling timer if auto-refresh is enabled
    if (bEnableAutoRefresh && PollingIntervalSeconds > 0)
    {
        GetWorld()->GetTimerManager().SetTimer(
            PollingTimerHandle,
            this,
            &ABuildingEnergyDisplay::RefreshBuildingCache,
            PollingIntervalSeconds,
            true // Loop
        );
    }
}
```

### 8.2 WebSocket Real-Time Updates (Optional)

For scenarios requiring immediate update propagation (multi-user collaboration, live energy monitoring), the system supports WebSocket connections:

**WebSocket Connection Initialization**:
```cpp
void ABuildingEnergyDisplay::InitializeWebSocket()
{
    if (EnergyWebSocketURL.IsEmpty()) return; // WebSocket disabled
    
    // Create WebSocket connection
    TSharedPtr<IWebSocket> WebSocket = FWebSocketsModule::Get().CreateWebSocket(
        EnergyWebSocketURL,
        TEXT("ws") // Protocol
    );
    
    // Bind event handlers
    WebSocket->OnConnected().AddLambda([this]()
    {
        UE_LOG(LogTemp, Log, TEXT("✅ WebSocket connected for real-time updates"));
        bWebSocketConnected = true;
    });
    
    WebSocket->OnMessage().AddUObject(this, &ABuildingEnergyDisplay::OnWebSocketMessage);
    
    WebSocket->OnConnectionError().AddLambda([](const FString& Error)
    {
        UE_LOG(LogTemp, Error, TEXT("❌ WebSocket error: %s"), *Error);
    });
    
    // Connect
    WebSocket->Connect();
    EnergyWebSocket = WebSocket;
}
```

**Message Processing**: When the backend pushes updates through WebSocket, the client processes them immediately:
```cpp
void ABuildingEnergyDisplay::OnWebSocketMessage(const FString& Message)
{
    // Parse WebSocket message (JSON format)
    TSharedPtr<FJsonObject> MessageObj;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Message);
    
    if (FJsonSerializer::Deserialize(Reader, MessageObj))
    {
        FString MessageType;
        if (MessageObj->TryGetStringField("type", MessageType))
        {
            if (MessageType == "building_update")
            {
                // Extract updated building data
                FString GmlId;
                MessageObj->TryGetStringField("gml_id", GmlId);
                
                TSharedPtr<FJsonObject> BuildingData = MessageObj->GetObjectField("data");
                
                // Update cache
                BuildingDataCache.Add(GmlId, BuildingData);
                
                // Extract and update color if provided
                FString ColorHex;
                if (BuildingData->TryGetStringField("color", ColorHex))
                {
                    BuildingColorCache.Add(GmlId, HexToLinearColor(ColorHex));
                }
                
                // Reapply colors immediately
                ApplyColorsUsingCesiumStyling();
                
                // If this building's widget is currently displayed, update it
                if (CurrentlyDisplayedBuildingId == GmlId && CurrentBuildingInfoWidget)
                {
                    UBuildingAttributesWidget* AttributesWidget = 
                        Cast<UBuildingAttributesWidget>(CurrentBuildingInfoWidget);
                    if (AttributesWidget)
                    {
                        AttributesWidget->SetBuildingData(GmlId, BuildingData);
                    }
                }
                
                UE_LOG(LogTemp, Log, TEXT("📊 Real-time update applied for building %s"), *GmlId);
            }
        }
    }
}
```

### 8.3 Update Notification System

When data updates are detected (either through polling or WebSocket), the system can notify users through various mechanisms:

**Visual Notifications**: 
- Toast notification appearing at screen corner indicating "Building data updated"
- Pulsing animation on buildings that received updates
- Status indicator in the UI showing "Last updated: X seconds ago"

**Audio Feedback**: Optional audio cue when updates are received (disabled by default, configurable in settings)

**Widget Auto-Refresh**: If a building attributes widget is open for a building that receives an update, the widget automatically refreshes to show the new data with a subtle animation indicating the update occurred.

---

## 9. Cesium 3D Tiles Integration

### 9.1 Cesium for Unreal Plugin Configuration

The project uses the Cesium for Unreal plugin to render 3D Tiles-based building geometries. The plugin must be installed from the Unreal Engine Marketplace and enabled in the project:

**Plugin Installation Steps**:
1. Open Epic Games Launcher → Unreal Engine → Library → Vault
2. Search for "Cesium for Unreal" and add to project
3. In Unreal Editor, go to Edit → Plugins → Cesium
4. Enable the Cesium plugin and restart Unreal Editor

**Cesium Ion Token Configuration**: The Cesium plugin requires a Cesium Ion access token to stream 3D Tiles data:
1. Create a free account at [cesium.com](https://cesium.com)
2. Navigate to Access Tokens section in Cesium Ion dashboard
3. Create a new token or use the default token provided
4. In Unreal Editor, go to Edit → Project Settings → Cesium
5. Paste the access token in the "Default Ion Access Token" field

### 9.2 Cesium Tileset Actor Setup

The Cesium3DTileset actor is placed in the Unreal level to load and display the Bisingen building data:

**Actor Placement**:
1. In Unreal Editor, open the Content Browser
2. Navigate to Cesium Content → Place Actors
3. Drag "Cesium 3D Tileset" into the level viewport
4. Name the actor "Cesium3DTileset_0" (matches the BuildingsTilesetName property)

**Tileset Configuration**:
The Cesium3DTileset actor details panel contains the following key settings:

- **Tileset Source**: Set to "From Cesium Ion" for cloud-hosted tilesets
- **Cesium Ion Asset ID**: Enter the asset ID for the Bisingen building tileset (obtained from Cesium Ion dashboard after uploading the 3D Tiles data)
- **Transform**: Set position, rotation, and scale to properly position the buildings in the Unreal level
- **Maximum Cached Bytes**: Set to 512 MB for desktop (256 MB for HoloLens) to control memory usage
- **Maximum Screen Space Error**: Set to 16.0 (lower values = higher quality, higher performance cost)
- **Enable Frustum Culling**: Checked (prevents rendering tiles outside camera view)
- **Enable Fog Culling**: Checked (prevents loading distant tiles)

### 9.3 Metadata Integration

The 3D Tiles dataset includes metadata properties for each building feature. The system accesses this metadata through the CesiumFeaturesMetadataComponent:

**Metadata Property Structure**: The Bisingen tileset includes the following metadata properties per building:
- `modified_gml_id`: Primary building identifier (format: "DEBW_XXXXXXXX")
- `gml_id`: Alternative identifier (format: "DEBWLXXXXXXXX")
- `building_type`: Classification string (Residential, Commercial, etc.)
- Additional properties may include building height, construction year, address information

**Metadata Query Implementation**: When a user clicks a building, the system queries metadata:
```cpp
void ABuildingEnergyDisplay::OnBuildingClicked(AActor* ClickedActor, FKey ButtonPressed)
{
    if (!ClickedActor) return;
    
    // Get Cesium metadata component
    UCesiumFeaturesMetadataComponent* MetadataComponent = 
        ClickedActor->FindComponentByClass<UCesiumFeaturesMetadataComponent>();
    
    if (!MetadataComponent) return;
    
    // Query metadata for the clicked feature
    // Note: Exact API depends on Cesium plugin version
    FString GmlId = GetGmlIdFromMetadata(MetadataComponent, HitResult);
    
    // Display building data
    if (!GmlId.IsEmpty())
    {
        DisplayBuildingData(GmlId);
    }
}
```

### 9.4 Cesium Tileset Styling System

The Cesium plugin supports feature-based styling through JSON style expressions. The system generates these expressions to apply per-building colors:

**Style Expression Format**: Cesium style JSON uses conditional expressions:
```json
{
  "color": "[
    ${modified_gml_id} === 'DEBW_0010008' ? color([0.2, 0.8, 0.2, 1.0]) :
    ${modified_gml_id} === 'DEBW_0010009' ? color([0.8, 0.8, 0.2, 1.0]) :
    ${modified_gml_id} === 'DEBW_0010010' ? color([0.8, 0.2, 0.2, 1.0]) :
    color([1.0, 1.0, 1.0, 1.0])
  ]"
}
```

**Dynamic Style Generation**: The `GenerateCesiumStyleJson()` function creates this JSON programmatically based on the BuildingColorCache, allowing dynamic color updates without manual JSON editing.

**Style Application Timing**: Colors are applied at the following points:
- After initial data preload completes (`OnPreloadResponseReceived`)
- After user updates building attributes (`OnUpdateResponseReceived`)
- Periodically during Cesium tileset refresh monitoring (`OnCesiumTilesetRefresh`)
- On demand when user clicks "Refresh Colors" button

---

## 10. HoloLens 2 Mixed Reality Deployment

### 10.1 Platform Configuration for HoloLens

Deploying to HoloLens 2 requires specific Unreal Engine configuration changes and build settings:

**Step 1: Enable HoloLens Platform Support**
1. In Unreal Editor, go to Edit → Project Settings → Platforms → HoloLens
2. Check "Support HoloLens" to enable the platform
3. Set the following critical settings:
   - **Start in VR**: Checked (enables mixed reality mode at startup)
   - **Support AR**: Checked (enables augmented reality subsystem)
   - **Forward Shading**: Checked (required for HoloLens performance)
   - **Mobile Multi-View**: Unchecked (not supported on HoloLens)

**Step 2: Package Manifest Configuration**
The Package.appxmanifest file (generated during packaging) must declare required capabilities:
```xml
<Capabilities>
  <Capability Name="internetClient" />
  <Capability Name="privateNetworkClientServer" />
  <uap:Capability Name="spatialPerception" />
  <DeviceCapability Name="microphone" />
  <DeviceCapability Name="webcam" />
</Capabilities>
```

These capabilities enable network access for API calls, spatial mapping for environmental understanding, microphone for voice commands, and webcam for gesture tracking.

**Step 3: Rendering Pipeline Optimization**
HoloLens 2 has significantly lower rendering power than desktop PCs. Apply these optimizations:

- **Cesium Tileset Settings**:
  - Maximum Cached Bytes: 256 MB (reduced from 512 MB desktop)
  - Maximum Screen Space Error: 32.0 (relaxed quality for performance)
  - Enable aggressive frustum culling
  
- **Material Complexity Reduction**:
  - Disable dynamic shadows (r.Shadow.Virtual.Enable=0)
  - Disable post-processing effects
  - Use simple materials without complex shader math
  
- **Resolution Settings**:
  - Render resolution: 75-85% of native (dynamic resolution scaling)
  - Target frame rate: 60 FPS (critical for comfort and tracking accuracy)

### 10.2 Input System Transformation

The HoloLensInputConverter component transforms desktop mouse input into gesture-based interaction:

**Gesture Mapping**:
- **Air-Tap (index finger pinch)** → Mouse Left Click → Building Selection
- **Tap-and-Hold** → Mouse Right Click → Context Menu (if implemented)
- **Gaze (head direction)** → Mouse Cursor Position → Targeting Ray

**Implementation Architecture**:
```cpp
// HoloLensInputConverter.h
UCLASS()
class FINAL_PROJECT_API UHoloLensInputConverter : public UActorComponent
{
    GENERATED_BODY()
    
public:
    // Detect platform and enable appropriate input mode
    void BeginPlay() override;
    
    // Process gaze ray for targeting
    void TickComponent(float DeltaTime, enum ELevelTick TickType, 
                      FActorComponentTickFunction *ThisTickFunction) override;
    
protected:
    // Current platform (Desktop or HoloLens)
    bool bIsHoloLensMode;
    
    // Gaze ray origin and direction
    FVector GazeOrigin;
    FVector GazeDirection;
    
    // Handle gesture events
    UFUNCTION()
    void OnAirTapDetected();
    
    UFUNCTION()
    void OnTapAndHoldDetected();
    
    // Perform raycast along gaze direction
    void PerformGazeRaycast();
};
```

**Gaze-Based Targeting Implementation**:
```cpp
void UHoloLensInputConverter::PerformGazeRaycast()
{
    if (!bIsHoloLensMode) return;
    
    // Get HMD position and orientation
    APlayerController* PC = GetWorld()->GetFirstPlayerController();
    if (!PC) return;
    
    // Get camera (HMD) position and forward vector
    PC->GetPlayerViewPoint(GazeOrigin, GazeRotation);
    GazeDirection = GazeRotation.Vector();
    
    // Perform raycast along gaze direction
    FHitResult HitResult;
    FCollisionQueryParams Params;
    Params.AddIgnoredActor(GetOwner());
    
    bool bHit = GetWorld()->LineTraceSingleByChannel(
        HitResult,
        GazeOrigin,
        GazeOrigin + GazeDirection * 10000.0f, // 100m max distance
        ECC_Visibility,
        Params
    );
    
    if (bHit)
    {
        // Highlight the gazed-at building for user feedback
        HighlightBuilding(HitResult.GetActor());
        
        // Store current gaze target for gesture event processing
        CurrentGazeTarget = HitResult.GetActor();
    }
}

void UHoloLensInputConverter::OnAirTapDetected()
{
    // Air-tap gesture detected, simulate building click
    if (CurrentGazeTarget)
    {
        ABuildingEnergyDisplay* BuildingDisplay = 
            Cast<ABuildingEnergyDisplay>(GetOwner());
        if (BuildingDisplay)
        {
            BuildingDisplay->OnBuildingClicked(CurrentGazeTarget, EKeys::LeftMouseButton);
        }
    }
}
```

### 10.3 UI Adaptation for Mixed Reality

The UMG widget system requires modifications for mixed reality environments:

**World-Space Widget Rendering**: Instead of screen-space overlays, widgets are rendered as 3D objects in world space:
```cpp
// Create world-space widget component
UWidgetComponent* WidgetComponent = NewObject<UWidgetComponent>(this);
WidgetComponent->SetWidgetClass(BuildingInfoWidgetClass);
WidgetComponent->SetDrawSize(FVector2D(600.0f, 400.0f)); // Widget size in pixels
WidgetComponent->SetWidgetSpace(EWidgetSpace::World); // Critical: World-space mode
WidgetComponent->SetCollisionEnabled(ECollisionEnabled::NoCollision);

// Position widget in front of user at comfortable distance (1.5 meters)
FVector WidgetLocation = UserLocation + UserForward * 150.0f; // 150 cm
WidgetComponent->SetWorldLocation(WidgetLocation);

// Orient widget to face user
FRotator WidgetRotation = (UserLocation - WidgetLocation).Rotation();
WidgetComponent->SetWorldRotation(WidgetRotation);

// Attach to scene and register
WidgetComponent->AttachToComponent(RootComponent, FAttachmentTransformRules::KeepWorldTransform);
WidgetComponent->RegisterComponent();
```

**Body-Locked UI**: For persistent UI elements (legends, control panels), attach widgets to the camera:
```cpp
// Attach widget to camera with offset for body-locked behavior
WidgetComponent->AttachToComponent(
    PlayerCamera,
    FAttachmentTransformRules::KeepRelativeTransform
);

// Set relative position (lower left of field of view)
WidgetComponent->SetRelativeLocation(FVector(100.0f, -50.0f, -30.0f));
```

**Font Size and Legibility**: Mixed reality requires larger fonts for comfortable reading:
- Minimum font size: 16pt for body text
- Minimum font size: 24pt for headings
- High contrast: White text on dark semi-transparent background
- Avoid thin fonts: Use bold or medium weights

### 10.4 Spatial Anchoring and Georeferencing

Aligning the Cesium geospatial dataset with the physical environment requires spatial anchoring:

**Spatial Anchor Creation**:
```cpp
void ABuildingEnergyDisplay::CreateSpatialAnchor()
{
    // Create spatial anchor at tileset origin
    UARSessionConfig* SessionConfig = NewObject<UARSessionConfig>();
    SessionConfig->SessionType = EARSessionType::AR;
    
    UARBlueprintLibrary::StartARSession(SessionConfig);
    
    // Create anchor at world origin (where Cesium tileset is positioned)
    FTransform AnchorTransform = FTransform::Identity;
    UARPin* SpatialAnchor = UARBlueprintLibrary::PinComponent(
        nullptr, // No component to pin, just create anchor at transform
        AnchorTransform,
        nullptr, // No hit result
        TEXT("BuildingTilesetOrigin") // Debug name
    );
    
    if (SpatialAnchor)
    {
        // Save anchor ID for persistence across sessions
        FString AnchorId = SpatialAnchor->GetDebugName().ToString();
        SaveAnchorIdToLocalStorage(AnchorId);
        
        UE_LOG(LogTemp, Log, TEXT("✅ Spatial anchor created: %s"), *AnchorId);
    }
}
```

**Anchor Restoration**: On subsequent application launches, restore the saved anchor:
```cpp
void ABuildingEnergyDisplay::RestoreSpatialAnchor()
{
    FString SavedAnchorId = LoadAnchorIdFromLocalStorage();
    if (SavedAnchorId.IsEmpty()) return;
    
    // Query Windows Spatial Anchor Store for saved anchor
    // (Exact API depends on Unreal's AR subsystem implementation)
    UARPin* RestoredAnchor = FindSpatialAnchorById(SavedAnchorId);
    
    if (RestoredAnchor)
    {
        // Position Cesium tileset at restored anchor location
        ACesium3DTileset* Tileset = GetCesiumTileset();
        if (Tileset)
        {
            Tileset->SetActorTransform(RestoredAnchor->GetLocalToWorldTransform());
            UE_LOG(LogTemp, Log, TEXT("✅ Tileset aligned with restored anchor"));
        }
    }
}
```

### 10.5 Building and Deployment Process

**Step 1: Package for HoloLens**
1. In Unreal Editor, go to File → Package Project → HoloLens
2. Select output directory for the packaged application
3. Wait for packaging process to complete (10-30 minutes depending on project size)
4. Packaging generates an APPX file in the output directory

**Step 2: Deploy to HoloLens Device**
Option A - USB Deployment:
1. Connect HoloLens 2 to PC via USB-C cable
2. Open Device Portal (https://device-ip-address)
3. Navigate to Apps → Install app
4. Browse to the APPX file and dependencies folder
5. Click Install and wait for deployment

Option B - Wireless Deployment:
1. Ensure HoloLens and PC are on the same network
2. Open Windows Device Portal on HoloLens (enable in Settings → Update & Security → For developers)
3. Note the HoloLens IP address
4. In web browser, navigate to https://hololens-ip-address
5. Login with device credentials (set up during HoloLens setup)
6. Navigate to Apps → Deploy apps
7. Upload APPX and dependencies
8. Click Install

**Step 3: Launch and Test**
1. On HoloLens, say "Go to Start" to open Start menu
2. Select "All Apps" and find the application
3. Click to launch the application
4. Application starts in mixed reality mode with building visualization
5. Test building selection by gazing at buildings and performing air-tap gestures
6. Test UI interactions by gazing at buttons and air-tapping

---

## 11. API Endpoints and Configuration

### 11.1 Backend API Base URL

All API endpoints are hosted at:
```
https://backend.gisworld-tech.com
```

The backend is a Django REST framework application with PostgreSQL/PostGIS database backend. SSL/TLS encryption is enabled (HTTPS) for secure communication.

### 11.2 Authentication Endpoints

**Login / Token Obtain**
- **Endpoint**: `POST /api/token/`
- **Purpose**: Authenticate user and obtain JWT access/refresh tokens
- **Request Body**:
  ```json
  {
    "username": "your_username",
    "password": "your_password"
  }
  ```
- **Response (Success - HTTP 200)**:
  ```json
  {
    "access": "eyJ0eXAiOiJKV1QiLCJhbGc...",
    "refresh": "eyJ0eXAiOiJKV1QiLCJhbGc..."
  }
  ```
- **Response (Failure - HTTP 401)**:
  ```json
  {
    "detail": "Invalid credentials"
  }
  ```

**Token Refresh**
- **Endpoint**: `POST /api/token/refresh/`
- **Purpose**: Obtain new access token using refresh token
- **Request Body**:
  ```json
  {
    "refresh": "eyJ0eXAiOiJKV1QiLCJhbGc..."
  }
  ```
- **Response (Success - HTTP 200)**:
  ```json
  {
    "access": "eyJ0eXAiOiJKV1QiLCJhbGc..."
  }
  ```

### 11.3 Building Energy Data Endpoints

**Get All Buildings (with Energy Data)**
- **Endpoint**: `GET /geospatial/buildings-energy/`
- **Purpose**: Retrieve all building energy data for a community
- **Authentication**: Required (Bearer token)
- **Query Parameters**:
  - `community_id` (required): Community identifier (e.g., "08417008" for Bisingen)
  - `format` (optional): Response format ("json" or "geojson", default: "json")
  - `include_colors` (optional): Include color assignments (true/false, default: false)
  - `energy_type` (optional): Energy type filter ("total", "heating", "electricity")
  - `time_period` (optional): Time period filter ("annual", "monthly", "daily")
  - `classification` (optional): Classification scheme ("co2", "energy", "efficiency")
  - `color_scheme` (optional): Color scheme to use ("co2_classes", "energy_ranges")
  
- **Example Request**:
  ```
  GET /geospatial/buildings-energy/?community_id=08417008&format=json&include_colors=true&energy_type=total&time_period=annual&classification=co2&color_scheme=co2_classes
  Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGc...
  ```

- **Response (Success - HTTP 200)**:
  ```json
  {
    "buildings": [
      {
        "modified_gml_id": "DEBW_0010008",
        "gml_id": "DEBWL0010008",
        "building_type": "Residential",
        "geometry": {
          "type": "FeatureCollection",
          "features": [{
            "type": "Feature",
            "geometry": {
              "type": "Polygon",
              "coordinates": [[[8.8833, 48.3167, 0], ...]]
            }
          }]
        },
        "energy_results": [{
          "begin_date": "2023-01-01",
          "end_date": "2023-12-31",
          "energy_value": 125430.50,
          "co2_emission": 32500.75,
          "color": "#7FFF00"
        }]
      },
      // ... more buildings
    ]
  }
  ```

**Get Single Building**
- **Endpoint**: `GET /geospatial/buildings-energy/{gml_id}/`
- **Purpose**: Retrieve detailed data for a specific building
- **Authentication**: Required (Bearer token)
- **Path Parameters**:
  - `gml_id`: Building identifier (modified_gml_id or gml_id format)
  
- **Example Request**:
  ```
  GET /geospatial/buildings-energy/DEBW_0010008/
  Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGc...
  ```

- **Response**: Same structure as individual building object in "Get All Buildings" response

**Update Building Attributes**
- **Endpoint**: `PUT /geospatial/buildings-energy/{gml_id}/`
- **Purpose**: Update building attributes (energy values, building type, etc.)
- **Authentication**: Required (Bearer token)
- **Path Parameters**:
  - `gml_id`: Building identifier
  
- **Request Body** (partial update supported):
  ```json
  {
    "building_type": "Commercial",
    "total_energy_demand": 150000.00,
    "co2_emission": 40000.00,
    "notes": "Updated after renovation"
  }
  ```

- **Response (Success - HTTP 200)**:
  ```json
  {
    "modified_gml_id": "DEBW_0010008",
    "building_type": "Commercial",
    "total_energy_demand": 150000.00,
    "co2_emission": 40000.00,
    "color": "#FFFF00",
    "updated_at": "2024-02-09T12:34:56Z"
  }
  ```

- **Response (Failure - HTTP 400)**:
  ```json
  {
    "errors": {
      "total_energy_demand": ["Value must be positive"],
      "building_type": ["Invalid building type"]
    }
  }
  ```

### 11.4 WebSocket Endpoint (Optional)

**Real-Time Updates WebSocket**
- **Endpoint**: `wss://backend.gisworld-tech.com/ws/buildings-energy/`
- **Purpose**: Receive real-time building data updates
- **Authentication**: JWT token in connection query parameter
- **Connection URL**:
  ```
  wss://backend.gisworld-tech.com/ws/buildings-energy/?token=eyJ0eXAiOiJKV1QiLCJhbGc...
  ```

- **Message Format (Server → Client)**:
  ```json
  {
    "type": "building_update",
    "gml_id": "DEBW_0010008",
    "data": {
      "total_energy_demand": 150000.00,
      "co2_emission": 40000.00,
      "color": "#FFFF00",
      "updated_at": "2024-02-09T12:34:56Z"
    }
  }
  ```

---

## 12. Credentials and Access Information

### 12.1 Backend API Credentials

**API Base URL**: https://backend.gisworld-tech.com

**Test Account Credentials**:
- **Username**: `test_user_bisingen`
- **Password**: `BisDemo2024!`
- **Access Level**: Read-write access to Bisingen community data (community_id: 08417008)
- **Token Expiration**: Access tokens expire after 1 hour, refresh tokens expire after 7 days

**Admin Account Credentials** (for full system access):
- **Username**: `admin_gisworld`
- **Password**: `GisWorld2024Admin!`
- **Access Level**: Full read-write access to all communities, user management, system configuration
- **Token Expiration**: Same as test account

**Important Security Notes**:
- These credentials are for development and testing purposes
- Production deployments must use unique credentials per user/organization
- Credentials should be stored securely (never hardcoded in source files)
- Consider using environment variables or secure configuration services
- Implement proper credential rotation policies

### 12.2 Cesium Ion Configuration

**Cesium Ion Account**: 
- **Email**: cephas.research@gisworld-tech.com
- **Password**: CesiumGis2024!

**Cesium Ion Access Token**: 
- **Token**: `eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiI5MTcyNDQ1Mi0wYzFhLTQzYmItYjE1MS1jZjg4ZDEzYzE4NzEiLCJpZCI6MTg0NjcxLCJpYXQiOjE2OTg3NDEyMzV9.kZnZ8kYPtR2qV3lH7mN9sK5jQ2wL8xR4pT6vU9yA2bC`
- **Token Name**: "Unreal Engine Bisingen Project"
- **Scope**: Read access to Bisingen building tileset

**Cesium Ion Asset Information**:
- **Asset Name**: Bisingen Buildings 3D Tiles
- **Asset ID**: `2457893`
- **Asset Type**: 3D Tiles
- **Attribution**: "Building data: Landesamt für Geoinformation und Landentwicklung Baden-Württemberg (LGL BW)"

**Configuration in Unreal Engine**:
1. Open Edit → Project Settings → Cesium
2. Set "Default Ion Access Token" to the token above
3. In Cesium3DTileset actor, set "Ion Asset ID" to `2457893`
4. Click "Reload" to load the tileset

### 12.3 HoloLens Device Configuration

**HoloLens Device Information**:
- **Device Name**: HOLOLENS-THESIS-01
- **Device IP Address**: 192.168.1.145 (may vary based on network)
- **Device Portal Credentials**:
  - **Username**: Administrator
  - **Password**: HoloThesis2024!

**Device Portal Access**:
1. Connect to same network as HoloLens
2. Navigate to https://192.168.1.145 in web browser
3. Accept SSL certificate warning (device uses self-signed certificate)
4. Login with credentials above
5. Use for deployment, debugging, performance monitoring

**Developer Mode Settings**:
- Developer Mode: Enabled
- Device Portal: Enabled
- Device Discovery: Enabled (for Visual Studio debugging)
- Allow paired remote devices: Enabled

### 12.4 Project Repository and Version Control

**Git Repository**:
- **URL**: https://github.com/Cephas2374/thesis_c-.git
- **Branch Structure**:
  - `main`: Stable production-ready code
  - `develop`: Active development branch
  - `feature/*`: Feature-specific branches
  - `hololens-deployment`: HoloLens-specific modifications

**Clone Command**:
```bash
git clone https://github.com/Cephas2374/thesis_c-.git
cd thesis_c-
```

**Repository Credentials**:
- **GitHub Username**: Cephas2374
- **Personal Access Token**: (Use your own GitHub PAT for secure access)

---

## Conclusion

This comprehensive guide documents all functionalities of the Building Energy Visualization System from initial setup through HoloLens 2 deployment. The system successfully integrates Unreal Engine 5.6, Cesium 3D Tiles, RESTful APIs, and mixed reality technologies to create an immersive platform for building energy data visualization and management.

**Key Achievements**:
- Seamless JWT authentication with automatic token refresh
- Efficient multi-level caching system for 4,875 buildings
- Interactive building selection on both desktop and HoloLens platforms
- Dynamic color visualization based on CO2 emission classifications
- Real-time data updates through polling or WebSocket connections
- Successful mixed reality deployment with gesture-based interaction

**System Capabilities**:
- Visualize energy consumption for entire communities in 3D
- Click/tap buildings to view detailed energy attributes
- Edit building data through intuitive form interfaces
- Apply per-building color coding for instant visual insights
- Deploy to HoloLens 2 for on-site augmented reality visualization
- Maintain data synchronization with authoritative backend database

This project demonstrates the viability of using game engine technologies (Unreal Engine) combined with geospatial standards (Cesium 3D Tiles) and mixed reality platforms (HoloLens 2) for practical urban planning applications. The architecture is extensible and can be adapted for other communities, different energy metrics, or alternative mixed reality devices.

For technical support or questions about implementation details, refer to the individual documentation files in the project repository or contact the development team at cephas.research@gisworld-tech.com.
