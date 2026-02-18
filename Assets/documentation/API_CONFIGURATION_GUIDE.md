# Building Energy API Configuration Guide

## Overview
This document provides the complete API configuration extracted from the Unreal Engine 5.6.1 implementation for use with Unity's BuildingEnergyManager system.

---

## API Endpoints

### Base URL
```
https://backend.gisworld-tech.com
```

### 1. Get Building Data (Individual)
**Endpoint:** `/geospatial/buildings-energy/{gml_id}/`

**Method:** `GET`

**Query Parameters:**
- `community_id` (required): Community identifier
- `field_type`: `basic` (standard fields)

**Example:**
```
GET https://backend.gisworld-tech.com/geospatial/buildings-energy/DENW19AL0000geMH/?community_id=123&field_type=basic
```

**Headers:**
```
Authorization: Bearer {your_access_token}
Content-Type: application/json
Cache-Control: no-cache, no-store, must-revalidate, max-age=0
Pragma: no-cache
```

---

### 2. List All Buildings (Community)
**Endpoint:** `/geospatial/buildings-energy/`

**Method:** `GET`

**Query Parameters:**
- `community_id` (required): Community identifier
- `field_type`: `basic`

**Example:**
```
GET https://backend.gisworld-tech.com/geospatial/buildings-energy/?community_id=123&field_type=basic
```

**Response:** Array of building objects

---

### 3. Update Building Data
**Endpoint:** `/geospatial/buildings-energy/{gml_id}/`

**Method:** `PUT`

**Query Parameters:**
- `community_id` (required)

**Headers:**
```
Authorization: Bearer {your_access_token}
Content-Type: application/json
```

**Request Body:** JSON with updated fields (see below)

---

## API Response Field Mapping

### JSON Field Names (from API)
Based on the UE5 implementation, the API uses these field names:

| API Field Name | Unity Property | Description |
|---|---|---|
| `gml_id` | gmlId | Building unique identifier (e.g., "DENW19AL0000geMH") |
| `construction_year_class` | constructionYear | Construction year or period |
| `storey` or `number_of_storey` | numberOfStorey | Number of floors |
| `energy_consumption` | energyConsumption | Energy usage value (kWh/year) |
| `begin_heating_system_type_1` | heatingSystemBefore | Heating system before renovation |
| `end_heating_system_type_1` | heatingSystemAfter | Heating system after renovation |
| `begin_window_type_1` | windowBefore | Window type before renovation |
| `end_window_type_1` | windowAfter | Window type after renovation |
| `begin_wall_type_1` | wallBefore | Wall type before renovation |
| `end_wall_type_1` | wallAfter | Wall type after renovation |
| `begin_roof_type_1` | roofBefore | Roof type before renovation |
| `end_roof_type_1` | roofAfter | Roof type after renovation |
| `begin_ceiling_type_1` | ceilingBefore | Ceiling type before renovation |
| `end_ceiling_type_1` | ceilingAfter | Ceiling type after renovation |
| `roof_storey` or `roof_storey_type` | roofStorey | Roof storey type |

---

## Authentication

### Bearer Token Format
The API uses **OAuth Bearer Token** authentication.

**Header Format:**
```
Authorization: Bearer {your_token_here}
```

### Token Characteristics
- Long alphanumeric string
- Typically 100+ characters
- Stored securely in application configuration
- May expire after a certain period (check with API provider)

**UE5 Implementation Notes:**
- Token is validated with `AccessToken.IsEmpty()` checks
- Token length is logged for debugging (first 20 chars only)
- System warns if token is missing before API calls

---

## Unity Setup Instructions

### Step 1: Configure BuildingEnergyManager

1. Open your Unity scene containing the Cesium tileset
2. Create an empty GameObject: `GameObject > Create Empty`
3. Rename it to "BuildingEnergyManager"
4. Add the script: `Add Component > Building Energy Manager`

### Step 2: Inspector Configuration

#### API Configuration Section
- **API Base Url:** `https://backend.gisworld-tech.com`
- **Access Token:** [Your Bearer Token Here]
- **Community Id:** [Your Community ID]

#### Cesium Configuration Section
- **Buildings Tileset Name:** `bisingen` (or your tileset GameObject name)
- **Enable Cesium Styling:** ✓ (checked)

#### Color Settings Section
- **Low Energy Color:** Green (RGB: 0, 255, 0)
- **Medium Energy Color:** Yellow (RGB: 255, 255, 0)
- **High Energy Color:** Red (RGB: 255, 0, 0)
- **Default Color:** Gray (RGB: 128, 128, 128)

#### Real-Time Sync Section
- **Enable Real Time Sync:** ✓ (optional - for 2-second polling like UE5)
- **Sync Interval:** `2.0` seconds

### Step 3: Test API Connection

1. In Unity Editor, select the BuildingEnergyManager GameObject
2. Right-click on the script component in Inspector
3. Select **"Force Reload Data"** from context menu
4. Check Console for API response:
   - ✅ Success: "Loaded data for X buildings"
   - ❌ Error: Check token/community ID

### Step 4: Apply Building Colors

After successful data load:
1. Right-click BuildingEnergyManager component
2. Select **"Test Color System"**
3. Observe buildings changing colors based on energy consumption

---

## Building ID Format (gml_id)

### Format
```
DENW19AL0000geMH
```

### Characteristics
- Prefix: `DENW19AL` (region identifier)
- Suffix: Variable alphanumeric code
- **Case-Sensitive:** Must preserve exact case from tileset metadata
- **With 'L' Prefix:** Some API calls may expect format like `LDENW19AL0000geMH`

### Cesium Metadata Property Names
The building identifier may appear in tileset metadata as:
- `gml_id` (most common)
- `gmlId` (camelCase variant)
- `id` (generic)
- `building_id`

The Unity implementation checks all variants.

---

## Energy Color Thresholds

### Default Values (Configurable in Inspector)

```csharp
// Low Energy (Green)
if (energyConsumption >= 0 && energyConsumption <= 50)
    return Color.Green;

// Medium Energy (Yellow)
if (energyConsumption > 50 && energyConsumption <= 150)
    return Color.Yellow;

// High Energy (Red)
if (energyConsumption > 150)
    return Color.Red;

// No Data (Gray)
if (energyConsumption == 0)
    return Color.Gray;
```

### Adjusting Thresholds
Edit [BuildingEnergyManager.cs](BuildingEnergyManager.cs) line ~150 in `CalculateEnergyColor()` method.

---

## Cache-Busting Implementation

The API uses aggressive cache-busting to ensure real-time data:

```
Cache-Control: no-cache, no-store, must-revalidate, max-age=0
Pragma: no-cache
If-None-Match: 
If-Modified-Since: Thu, 01 Jan 1970 00:00:00 GMT
```

This prevents browsers/Unity from caching responses and ensures fresh data on every request.

---

## Troubleshooting

### Issue: "Failed to preload building data"
**Solutions:**
- Verify `accessToken` is correct and not expired
- Check `communityId` matches your API access permissions
- Ensure network connectivity to backend.gisworld-tech.com
- Check Unity Console for detailed error messages

### Issue: Buildings not changing color
**Solutions:**
- Verify tileset name matches `buildingsTilesetName` exactly
- Check Console for "Found buildings tileset" message
- Ensure tileset has loaded (tiles visible in Game view)
- Try "Apply Test Colors" from context menu to force refresh
- Verify gml_id properties exist in tileset metadata (use CesiumMetadataReader to click buildings)

### Issue: "No access token available"
**Solutions:**
- Enter token in BuildingEnergyManager Inspector
- Token must be non-empty string
- Token format: `Authorization: Bearer {token}`

### Issue: Wrong buildings being colored
**Solutions:**
- Check gml_id case sensitivity - must match exactly
- Verify API response gml_id values match tileset metadata gml_id
- Check Console logs during data fetch for ID mismatches
- Use CesiumMetadataReader to click buildings and see actual metadata property names

---

## Real-Time Synchronization

### UE5 Implementation
The Unreal Engine version uses **2-second polling intervals** to refresh building data when the form is open.

### Unity Implementation
Enable in Inspector:
- ✓ Enable Real Time Sync
- Sync Interval: 2.0

This will automatically refresh the currently displayed building every 2 seconds.

### Performance Considerations
- Each sync = 1 API call
- Only refreshes currently displayed building (not all buildings)
- Disable when not needed to reduce API load
- Suitable for scenarios where building data changes frequently

---

## Example API Responses

### Individual Building Response
```json
{
  "gml_id": "DENW19AL0000geMH",
  "construction_year_class": "1949-1957",
  "storey": 2,
  "energy_consumption": 87.5,
  "begin_heating_system_type_1": "gas_boiler",
  "end_heating_system_type_1": "heat_pump",
  "begin_window_type_1": "single_pane",
  "end_window_type_1": "triple_pane",
  "roof_storey": "yes"
}
```

### Buildings List Response
```json
[
  {
    "gml_id": "DENW19AL0000geMH",
    "construction_year_class": "1949-1957",
    "storey": 2,
    "energy_consumption": 87.5
  },
  {
    "gml_id": "DENW19AL0000geXZ",
    "construction_year_class": "1958-1968",
    "storey": 3,
    "energy_consumption": 145.2
  }
]
```

---

## Next Steps

1. **Configure API credentials** in BuildingEnergyManager
2. **Test data loading** with "Force Reload Data"
3. **Test building coloring** with "Test Color System"
4. **Implement BuildingAttributesForm** (renovation form with dropdowns)
5. **Add UI panels** (BuildingInfoPanel for displaying details)
6. **Test real-time sync** if needed

---

## Contact & Support

For API access credentials:
- Contact your backend administrator
- Request Bearer token and Community ID
- Verify API endpoint availability

For Unity implementation issues:
- Check Unity Console for detailed error messages
- Enable Debug logging in BuildingEnergyManager
- Verify Cesium for Unity version compatibility (tested with v1.22.0)
