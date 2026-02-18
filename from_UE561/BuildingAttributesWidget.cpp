#include "BuildingAttributesWidget.h" // Include the header file for this widget class [BUILDING ATTRIBUTES WIDGET INCLUDE]
#include "Components/Button.h" // Include button UI component for user interactions [BUTTON INCLUDE]
#include "Components/ComboBoxString.h" // Include combo box UI component for dropdown selections [COMBO BOX INCLUDE]
#include "Components/EditableTextBox.h" // Include editable text box UI component for text input [EDITABLE TEXT BOX INCLUDE]
#include "Engine/Engine.h" // Include engine functionality for global engine access [ENGINE INCLUDE]
#include "Http.h" // Include HTTP module for web request functionality [HTTP INCLUDE]
#include "Json.h" // Include JSON library for parsing and creating JSON data [JSON INCLUDE]
#include "Styling/SlateColor.h" // Include Slate color styling support [SLATE COLOR INCLUDE]

void UBuildingAttributesWidget::NativeConstruct() // NativeConstruct method called when widget is constructed [NATIVE CONSTRUCT DECLARATION]
{ // Start of NativeConstruct method body [NATIVE CONSTRUCT BODY START]
    Super::NativeConstruct(); // Call parent class NativeConstruct to ensure proper widget initialization [SUPER NATIVE CONSTRUCT CALL]

    // Set initial title [SET INITIAL TITLE COMMENT]
    if (BuildingTitleText) // Check if building title text widget is valid [BUILDING TITLE TEXT VALIDATION]
    { // Start of building title text validation block [BUILDING TITLE TEXT VALIDATION BLOCK START]
        BuildingTitleText->SetText(FText::FromString(TEXT("Building Attributes Form"))); // Set default title text for the widget [SET TITLE TEXT]
    } // End of building title text validation block [BUILDING TITLE TEXT VALIDATION BLOCK END]

    // Bind button events [BIND BUTTON EVENTS COMMENT]
    if (BTN_Save) // Check if save button widget is valid [SAVE BUTTON VALIDATION]
    { // Start of save button validation block [SAVE BUTTON VALIDATION BLOCK START]
        BTN_Save->OnClicked.AddDynamic(this, &UBuildingAttributesWidget::OnSaveButtonClicked); // Bind save button click event to callback function [SAVE BUTTON BIND EVENT]
    } // End of save button validation block [SAVE BUTTON VALIDATION BLOCK END]
    else // If save button is invalid [SAVE BUTTON ELSE]
    { // Start of save button error handling block [SAVE BUTTON ERROR BLOCK START]
        UE_LOG(LogTemp, Error, TEXT("BTN_Save is NULL! Check UMG widget variable name and binding.")); // Log error message for missing save button [SAVE BUTTON ERROR LOG]
    } // End of save button error handling block [SAVE BUTTON ERROR BLOCK END]

    if (BTN_Close) // Check if close button widget is valid [CLOSE BUTTON VALIDATION]
    { // Start of close button validation block [CLOSE BUTTON VALIDATION BLOCK START]
        BTN_Close->OnClicked.AddDynamic(this, &UBuildingAttributesWidget::OnCloseButtonClicked); // Bind close button click event to callback function [CLOSE BUTTON BIND EVENT]
    } // End of close button validation block [CLOSE BUTTON VALIDATION BLOCK END]
    else // If close button is invalid [CLOSE BUTTON ELSE]
    { // Start of close button error handling block [CLOSE BUTTON ERROR BLOCK START]
        UE_LOG(LogTemp, Error, TEXT("BTN_Close is NULL! Check UMG widget variable name and binding.")); // Log error message for missing close button [CLOSE BUTTON ERROR LOG]
    } // End of close button error handling block [CLOSE BUTTON ERROR BLOCK END]

    // Populate basic dropdown options (will be replaced by API data) [POPULATE DROPDOWN OPTIONS COMMENT]
    PopulateDropdownOptions(); // Call function to populate combo box options with initial values [POPULATE DROPDOWN OPTIONS CALL]
    
    // Configure dropdown styling with white backgrounds [CONFIGURE DROPDOWN STYLING COMMENT]
    ConfigureDropdownStyling(); // Set white background color for all dropdown controls [CONFIGURE DROPDOWN STYLING CALL]
    
    // TEMPORARILY DISABLE real-time form synchronization to debug data loading issues [DISABLE REAL-TIME SYNC COMMENT]
    // StartFormRealTimeSync(); [START FORM REAL-TIME SYNC COMMENTED]
    UE_LOG(LogTemp, Error, TEXT("🛑 Real-time form sync DISABLED for debugging")); // Log message indicating real-time sync is disabled for debugging [REAL-TIME SYNC DISABLED LOG]

    UE_LOG(LogTemp, Log, TEXT("Building Attributes Widget initialized with REAL-TIME synchronization")); // Log message indicating widget has been initialized [WIDGET INITIALIZED LOG]
} // End of NativeConstruct method body [NATIVE CONSTRUCT BODY END]

void UBuildingAttributesWidget::SetBuildingData(const FString& GmlId, const FString& Token) // SetBuildingData method for initializing widget with building data [SET BUILDING DATA DECLARATION]
{ // Start of SetBuildingData method body [SET BUILDING DATA BODY START]
    UE_LOG(LogTemp, Error, TEXT("🔍 === WIDGET SetBuildingData DEBUG ====")); // Log debug message for widget data initialization [SET BUILDING DATA DEBUG LOG]
    UE_LOG(LogTemp, Error, TEXT("🔍 Received gml_id (should have L): %s"), *GmlId); // Log received GML ID for debugging format validation [RECEIVED GML ID LOG]
    UE_LOG(LogTemp, Error, TEXT("🔍 Token Length: %d"), Token.Len()); // Log token length for authentication validation [TOKEN LENGTH LOG]
    UE_LOG(LogTemp, Error, TEXT("🔍 ID Length: %d"), GmlId.Len()); // Log GML ID length for format validation [GML ID LENGTH LOG]
    UE_LOG(LogTemp, Error, TEXT("🔍 Contains L: %s"), GmlId.Contains(TEXT("L")) ? TEXT("YES") : TEXT("NO")); // Check if GML ID contains L character [CONTAINS L CHECK LOG]
    UE_LOG(LogTemp, Error, TEXT("🔍 Contains _: %s"), GmlId.Contains(TEXT("_")) ? TEXT("YES") : TEXT("NO")); // Check if GML ID contains underscore character [CONTAINS UNDERSCORE CHECK LOG]
    
    CurrentBuildingGmlId = GmlId; // Store current building GML ID for reference [STORE CURRENT BUILDING GML ID]
    CurrentBuildingKey = GmlId;  // This should be the gml_id format with L for API [STORE CURRENT BUILDING KEY]
    AccessToken = Token; // Store access token for API authentication [STORE ACCESS TOKEN]
    
    // Use the same community ID as the main application [USE SAME COMMUNITY ID COMMENT]
    CommunityId = TEXT("08417008");  // Same as DefaultCommunityId in BuildingEnergyDisplay [SET COMMUNITY ID]
    UE_LOG(LogTemp, Error, TEXT("🔍 Using Community ID: %s"), *CommunityId); // Log community ID for debugging [COMMUNITY ID LOG]
    
    if (BuildingTitleText) // Check if building title text widget is valid [BUILDING TITLE TEXT VALIDATION]
    { // Start of building title text validation block [BUILDING TITLE TEXT VALIDATION BLOCK START]
        FString TitleText = FString::Printf(TEXT("Building Attributes - %s"), *GmlId); // Create formatted title text with GML ID [CREATE TITLE TEXT]
        BuildingTitleText->SetText(FText::FromString(TitleText)); // Set widget title text to formatted string [SET TITLE TEXT]
        UE_LOG(LogTemp, Warning, TEXT("WIDGET Updated title to: %s"), *TitleText); // Log title update for debugging [TITLE UPDATE LOG]
    } // End of building title text validation block [BUILDING TITLE TEXT VALIDATION BLOCK END]

    UE_LOG(LogTemp, Warning, TEXT("WIDGET About to call LoadBuildingAttributes with REAL-TIME gml_id: %s"), *CurrentBuildingKey); // Log before calling load attributes function [LOAD ATTRIBUTES CALL LOG]

    // Load current building attributes from API with fresh data [LOAD CURRENT BUILDING ATTRIBUTES COMMENT]
    LoadBuildingAttributes(); // Call function to fetch building attributes from API [LOAD BUILDING ATTRIBUTES CALL]

    UE_LOG(LogTemp, Warning, TEXT("WIDGET === SetBuildingData COMPLETED ===")); // Log completion of SetBuildingData method [SET BUILDING DATA COMPLETED LOG]
} // End of SetBuildingData method body [SET BUILDING DATA BODY END]

void UBuildingAttributesWidget::LoadBuildingAttributes() // LoadBuildingAttributes method to fetch building data from API [LOAD BUILDING ATTRIBUTES DECLARATION]
{ // Start of LoadBuildingAttributes method body [LOAD BUILDING ATTRIBUTES BODY START]
    if (AccessToken.IsEmpty()) // Check if access token is available for API authentication [ACCESS TOKEN EMPTY CHECK]
    { // Start of empty access token block [EMPTY ACCESS TOKEN BLOCK START]
        UE_LOG(LogTemp, Error, TEXT("🚨 ERROR No access token available")); // Log error for missing access token [ACCESS TOKEN ERROR LOG]
        return; // Exit method early due to missing authentication [EARLY RETURN ON MISSING TOKEN]
    } // End of empty access token block [EMPTY ACCESS TOKEN BLOCK END]

    UE_LOG(LogTemp, Error, TEXT("🔍 === FRESH API CALL DEBUG ====")); // Log debug header for API call [FRESH API CALL DEBUG LOG]
    UE_LOG(LogTemp, Error, TEXT("🔍 Building ID being used: %s"), *CurrentBuildingKey); // Log building ID for API request [BUILDING ID LOG]
    UE_LOG(LogTemp, Error, TEXT("🔍 Community ID: %s"), *CommunityId); // Log community ID for API request [COMMUNITY ID LOG]

    // Create HTTP request to get building attributes with real-time data [CREATE HTTP REQUEST COMMENT]
    TSharedRef<IHttpRequest> Request = FHttpModule::Get().CreateRequest(); // Create HTTP request object for API call [CREATE HTTP REQUEST OBJECT]
    
    // API configuration - should be externally configurable [API CONFIGURATION COMMENT]
    FString ApiBaseUrl = TEXT("https://backend.gisworld-tech.com");  // Should come from config [API BASE URL ASSIGNMENT]
    
    // Build the URL with correct format: /geospatial/buildings-energy/{gml_id}/?community_id={community_id}&field_type=basic [BUILD URL COMMENT]
    FString Url = FString::Printf(TEXT("%s/geospatial/buildings-energy/%s/?community_id=%s&field_type=basic"), 
        *ApiBaseUrl, *CurrentBuildingKey, *CommunityId); // Construct API URL with building key and community ID parameters [CONSTRUCT API URL]
    
    UE_LOG(LogTemp, Error, TEXT("🌐 === MAKING FRESH API REQUEST ====")); // Log API request initiation [API REQUEST INITIATION LOG]
    UE_LOG(LogTemp, Error, TEXT("🌐 FULL URL: %s"), *Url); // Log complete API URL for debugging [FULL URL LOG]
    UE_LOG(LogTemp, Error, TEXT("🌐 Building key (gml_id with L): %s"), *CurrentBuildingKey); // Log building key format [BUILDING KEY LOG]
    UE_LOG(LogTemp, Error, TEXT("🌐 Community ID: %s"), *CommunityId); // Log community ID parameter [COMMUNITY ID PARAMETER LOG]
    UE_LOG(LogTemp, Error, TEXT("🌐 Token (first 20 chars): %s"), *AccessToken.Left(20)); // Log partial token for debugging without exposing full token [PARTIAL TOKEN LOG]
    
    Request->SetURL(Url); // Set HTTP request URL [SET REQUEST URL]
    Request->SetVerb(TEXT("GET")); // Set HTTP method to GET for data retrieval [SET HTTP VERB]
    Request->SetHeader(TEXT("Authorization"), FString::Printf(TEXT("Bearer %s"), *AccessToken)); // Set authorization header with bearer token [SET AUTHORIZATION HEADER]
    Request->SetHeader(TEXT("Content-Type"), TEXT("application/json")); // Set content type header for JSON data [SET CONTENT TYPE HEADER]
    
    // FORCE NO CACHE - Aggressive cache busting headers [FORCE NO CACHE COMMENT]
    Request->SetHeader(TEXT("Cache-Control"), TEXT("no-cache, no-store, must-revalidate, max-age=0")); // Set cache control header to prevent caching [SET CACHE CONTROL HEADER]
    Request->SetHeader(TEXT("Pragma"), TEXT("no-cache")); // Set pragma header for legacy cache prevention [SET PRAGMA HEADER]
    Request->SetHeader(TEXT("Expires"), TEXT("0")); // Set expires header to force immediate expiration [SET EXPIRES HEADER]
    Request->SetHeader(TEXT("If-None-Match"), TEXT("")); // Set If-None-Match header to bypass ETag caching [SET IF NONE MATCH HEADER]
    Request->SetHeader(TEXT("If-Modified-Since"), TEXT("Thu, 01 Jan 1970 00:00:00 GMT")); // Set If-Modified-Since header to epoch time [SET IF MODIFIED SINCE HEADER]
    
    UE_LOG(LogTemp, Error, TEXT("🔄 FORCING FRESH DATA - No cache headers applied")); // Log cache busting configuration [CACHE BUSTING LOG]
    
    Request->OnProcessRequestComplete().BindUObject(this, &UBuildingAttributesWidget::OnGetAttributesResponse); // Bind response callback method for handling API response [BIND RESPONSE CALLBACK]
    
    UE_LOG(LogTemp, Error, TEXT("🎯 REQUEST BINDING: OnGetAttributesResponse callback bound to request")); // Log callback binding [REQUEST BINDING LOG]
    UE_LOG(LogTemp, Error, TEXT("🎯 Widget instance: %p"), this); // Log widget instance pointer for debugging [WIDGET INSTANCE LOG]
    
    if (!Request->ProcessRequest()) // Attempt to execute HTTP request and check for immediate failure [PROCESS REQUEST CHECK]
    { // Start of request processing failure block [REQUEST PROCESSING FAILURE BLOCK START]
        UE_LOG(LogTemp, Error, TEXT("🚨 ERROR Failed to start GET building attributes request")); // Log error for failed request processing [REQUEST PROCESSING ERROR LOG]
        return; // Exit method early due to failed request [EARLY RETURN ON REQUEST FAILURE]
    } // End of request processing failure block [REQUEST PROCESSING FAILURE BLOCK END]
    
    UE_LOG(LogTemp, Error, TEXT("✅ HTTP REQUEST STARTED - waiting for response...")); // Log successful request initiation [REQUEST STARTED LOG]
    UE_LOG(LogTemp, Error, TEXT("✅ Request URL: %s"), *Url); // Log request URL confirmation [REQUEST URL CONFIRMATION LOG]
    UE_LOG(LogTemp, Error, TEXT("✅ Expected callback: OnGetAttributesResponse")); // Log expected callback function name [EXPECTED CALLBACK LOG]
} // End of LoadBuildingAttributes method body [LOAD BUILDING ATTRIBUTES BODY END]

void UBuildingAttributesWidget::PopulateDropdownOptions() // PopulateDropdownOptions method to initialize combo box controls [POPULATE DROPDOWN OPTIONS DECLARATION]
{ // Start of PopulateDropdownOptions method body [POPULATE DROPDOWN OPTIONS BODY START]
    UE_LOG(LogTemp, Warning, TEXT("WIDGET Populating initial dropdown options")); // Log method initiation for debugging [POPULATE OPTIONS LOG]

    // Add basic options as placeholders until API loads [ADD BASIC OPTIONS COMMENT]
    if (CB_ConstructionYear) // Check if construction year combo box widget is valid [CONSTRUCTION YEAR VALIDATION]
    { // Start of construction year validation block [CONSTRUCTION YEAR VALIDATION BLOCK START]
        CB_ConstructionYear->ClearOptions(); // Clear existing options in construction year combo box [CLEAR CONSTRUCTION YEAR OPTIONS]
        UE_LOG(LogTemp, Warning, TEXT("WIDGET CB_ConstructionYear ready for API data")); // Log construction year combo box readiness [CONSTRUCTION YEAR READY LOG]
    } // End of construction year validation block [CONSTRUCTION YEAR VALIDATION BLOCK END]

    if (CB_HeatingSystemBefore) // Check if heating system before combo box widget is valid [HEATING SYSTEM BEFORE VALIDATION]
    { // Start of heating system before validation block [HEATING SYSTEM BEFORE VALIDATION BLOCK START]
        CB_HeatingSystemBefore->ClearOptions();
        UE_LOG(LogTemp, Warning, TEXT("WIDGET CB_HeatingSystemBefore ready for API data"));
    }

    if (CB_HeatingSystemAfter)
    {
        CB_HeatingSystemAfter->ClearOptions();
        UE_LOG(LogTemp, Warning, TEXT("WIDGET CB_HeatingSystemAfter ready for API data"));
    }

    if (CB_RoofStorey) // Check if roof storey combo box widget is valid [ROOF STOREY VALIDATION]
    { // Start of roof storey validation block [ROOF STOREY VALIDATION BLOCK START]
        CB_RoofStorey->ClearOptions(); // Clear existing options in roof storey combo box [CLEAR ROOF STOREY OPTIONS]
        UE_LOG(LogTemp, Warning, TEXT("WIDGET CB_RoofStorey ready for API data")); // Log roof storey combo box readiness [ROOF STOREY READY LOG]
    } // End of roof storey validation block [ROOF STOREY VALIDATION BLOCK END]

    // Check if all widgets are properly bound [CHECK WIDGET BINDING COMMENT]
    UE_LOG(LogTemp, Warning, TEXT("WIDGET Widget validation:")); // Log widget validation header [WIDGET VALIDATION HEADER LOG]
    UE_LOG(LogTemp, Warning, TEXT("  BuildingTitleText: %s"), BuildingTitleText ? TEXT("VALID") : TEXT("NULL")); // Log building title text widget validation status [BUILDING TITLE TEXT VALIDATION LOG]
    UE_LOG(LogTemp, Warning, TEXT("  CB_ConstructionYear: %s"), CB_ConstructionYear ? TEXT("VALID") : TEXT("NULL")); // Log construction year combo box validation status [CONSTRUCTION YEAR VALIDATION LOG]
    UE_LOG(LogTemp, Warning, TEXT("  CB_HeatingSystemBefore: %s"), CB_HeatingSystemBefore ? TEXT("VALID") : TEXT("NULL")); // Log heating system before combo box validation status [HEATING SYSTEM BEFORE VALIDATION LOG]
    UE_LOG(LogTemp, Warning, TEXT("  TB_NumberOfStorey: %s"), TB_NumberOfStorey ? TEXT("VALID") : TEXT("NULL")); // Log number of storey text box validation status [NUMBER OF STOREY VALIDATION LOG]

    UE_LOG(LogTemp, Warning, TEXT("WIDGET Initial dropdown options populated")); // Log completion of dropdown population [DROPDOWN POPULATION COMPLETED LOG]
} // End of PopulateDropdownOptions method body [POPULATE DROPDOWN OPTIONS BODY END]

void UBuildingAttributesWidget::ConfigureDropdownStyling() // ConfigureDropdownStyling method to set white backgrounds for dropdown controls [CONFIGURE DROPDOWN STYLING DECLARATION]
{ // Start of ConfigureDropdownStyling method body [CONFIGURE DROPDOWN STYLING BODY START]
    UE_LOG(LogTemp, Warning, TEXT("WIDGET Dropdown styling - Background colors should be configured in UMG Blueprint")); // Log styling configuration note [DROPDOWN STYLING LOG]
    
    // Note: For ComboBoxString background styling, the most effective approach is to:
    // 1. Open the UMG widget blueprint (WBP_BuildingAttributes)
    // 2. Select each ComboBoxString widget
    // 3. In the Style section, set Background Color to white
    // 4. Ensure text colors are set to black for visibility
    
    UE_LOG(LogTemp, Warning, TEXT("WIDGET To set white dropdown backgrounds:")); // Log instructions [DROPDOWN INSTRUCTIONS LOG]
    UE_LOG(LogTemp, Warning, TEXT("WIDGET 1. Open UMG Blueprint for this widget")); // Log instruction step 1 [INSTRUCTION STEP 1]
    UE_LOG(LogTemp, Warning, TEXT("WIDGET 2. Select each ComboBoxString in the designer")); // Log instruction step 2 [INSTRUCTION STEP 2]
    UE_LOG(LogTemp, Warning, TEXT("WIDGET 3. Set Style > Background Color to white")); // Log instruction step 3 [INSTRUCTION STEP 3]
    UE_LOG(LogTemp, Warning, TEXT("WIDGET 4. Set Style > Foreground Color to black")); // Log instruction step 4 [INSTRUCTION STEP 4]
    
    UE_LOG(LogTemp, Warning, TEXT("WIDGET Dropdown styling guidance completed")); // Log completion of dropdown styling [DROPDOWN STYLING COMPLETED LOG]
} // End of ConfigureDropdownStyling method body [CONFIGURE DROPDOWN STYLING BODY END]

void UBuildingAttributesWidget::OnSaveButtonClicked() // OnSaveButtonClicked method called when save button is pressed [ON SAVE BUTTON CLICKED DECLARATION]
{ // Start of OnSaveButtonClicked method body [ON SAVE BUTTON CLICKED BODY START]
    UE_LOG(LogTemp, Log, TEXT("Save button clicked - Starting save process")); // Log save process initiation [SAVE PROCESS LOG]
    
    if (AccessToken.IsEmpty()) // Check if access token is available for API authentication [ACCESS TOKEN EMPTY CHECK]
    { // Start of empty access token block [EMPTY ACCESS TOKEN BLOCK START]
        UE_LOG(LogTemp, Error, TEXT("No access token available for save")); // Log error for missing access token [SAVE ACCESS TOKEN ERROR LOG]
        if (GEngine) // Check if global engine instance is available [ENGINE INSTANCE CHECK FOR SAVE ERROR]
        { // Start of engine save error display block [ENGINE SAVE ERROR DISPLAY BLOCK START]
            GEngine->AddOnScreenDebugMessage(-1, 5.0f, FColor::Red, TEXT("ERROR: No access token for save")); // Display save error on screen [DISPLAY SAVE ACCESS TOKEN ERROR]
        } // End of engine save error display block [ENGINE SAVE ERROR DISPLAY BLOCK END]
        return; // Exit method early due to missing access token [EARLY RETURN ON MISSING SAVE TOKEN]
    } // End of empty access token block [EMPTY ACCESS TOKEN BLOCK END]
    
    if (CurrentBuildingKey.IsEmpty()) // Check if building key is available for API operation [BUILDING KEY EMPTY CHECK]
    { // Start of empty building key block [EMPTY BUILDING KEY BLOCK START]
        UE_LOG(LogTemp, Error, TEXT("No building key available for save")); // Log error for missing building key [SAVE BUILDING KEY ERROR LOG]
        if (GEngine) // Check if global engine instance is available [ENGINE INSTANCE CHECK FOR BUILDING ERROR]
        { // Start of engine building error display block [ENGINE BUILDING ERROR DISPLAY BLOCK START]
            GEngine->AddOnScreenDebugMessage(-1, 5.0f, FColor::Red, TEXT("ERROR: No building selected")); // Display building selection error on screen [DISPLAY SAVE BUILDING ERROR]
        } // End of engine building error display block [ENGINE BUILDING ERROR DISPLAY BLOCK END]
        return; // Exit method early due to missing building key [EARLY RETURN ON MISSING BUILDING KEY]
    } // End of empty building key block [EMPTY BUILDING KEY BLOCK END]
    
    // Collect form data and send PUT request
    SaveBuildingAttributesToAPI();
}

void UBuildingAttributesWidget::SaveBuildingAttributesToAPI()
{
    UE_LOG(LogTemp, Warning, TEXT("SAVE === Starting API save process ==="));
    UE_LOG(LogTemp, Warning, TEXT("SAVE Using building key: %s"), *CurrentBuildingKey);
    UE_LOG(LogTemp, Warning, TEXT("SAVE Using community ID: %s"), *CommunityId);
    
    // Validate required data
    if (CurrentBuildingKey.IsEmpty())
    {
        UE_LOG(LogTemp, Error, TEXT("SAVE ERROR: No building key available for save"));
        if (GEngine)
        {
            GEngine->AddOnScreenDebugMessage(-1, 5.0f, FColor::Red, TEXT("ERROR: No building ID for save"));
        }
        return;
    }
    
    if (AccessToken.IsEmpty())
    {
        UE_LOG(LogTemp, Error, TEXT("SAVE ERROR: No access token available for save"));
        if (GEngine)
        {
            GEngine->AddOnScreenDebugMessage(-1, 5.0f, FColor::Red, TEXT("ERROR: No access token for save"));
        }
        return;
    }
    
    // Collect current form values
    FString FormDataJson = CreateAttributesJsonFromForm();
    
    if (FormDataJson.IsEmpty())
    {
        UE_LOG(LogTemp, Error, TEXT("SAVE ERROR: No form data collected"));
        return;
    }
    
    // Create HTTP PUT request with case-preserved building ID
    TSharedRef<IHttpRequest> Request = FHttpModule::Get().CreateRequest();
    
    FString ApiBaseUrl = TEXT("https://backend.gisworld-tech.com");
    FString Url = FString::Printf(TEXT("%s/geospatial/buildings-energy/%s/?community_id=%s"), 
        *ApiBaseUrl, *CurrentBuildingKey, *CommunityId);
    
    UE_LOG(LogTemp, Warning, TEXT("SAVE PUT request URL: %s"), *Url);
    UE_LOG(LogTemp, Warning, TEXT("SAVE JSON Data: %s"), *FormDataJson);
    
    Request->SetURL(Url);
    Request->SetVerb(TEXT("PUT"));
    Request->SetHeader(TEXT("Authorization"), FString::Printf(TEXT("Bearer %s"), *AccessToken));
    Request->SetHeader(TEXT("Content-Type"), TEXT("application/json"));
    Request->SetContentAsString(FormDataJson);
    
    Request->OnProcessRequestComplete().BindUObject(this, &UBuildingAttributesWidget::OnPutAttributesResponse);
    
    if (!Request->ProcessRequest())
    {
        UE_LOG(LogTemp, Error, TEXT("SAVE ERROR: Failed to start PUT request"));
        if (GEngine)
        {
            GEngine->AddOnScreenDebugMessage(-1, 5.0f, FColor::Red, TEXT("ERROR: Failed to start save request"));
        }
        return;
    }
    
    UE_LOG(LogTemp, Warning, TEXT("SAVE PUT request started successfully"));
    if (GEngine)
    {
        GEngine->AddOnScreenDebugMessage(-1, 3.0f, FColor::Yellow, TEXT("SAVING: Building attributes..."));
    }
}

void UBuildingAttributesWidget::OnCloseButtonClicked()
{
    UE_LOG(LogTemp, Warning, TEXT("ERROR Close button clicked - Widget functions connected!"));
    if (GEngine)
    {
        GEngine->AddOnScreenDebugMessage(-1, 3.0f, FColor::Yellow, TEXT("🚪 Close button works! Closing form..."));
    }
    CloseWidget();
}

void UBuildingAttributesWidget::CloseWidget()
{
    RemoveFromParent();
}

void UBuildingAttributesWidget::SaveBuildingAttributes()
{
    OnSaveButtonClicked();
}

void UBuildingAttributesWidget::OnGetAttributesResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful)
{
    UE_LOG(LogTemp, Error, TEXT("🎯🎯🎯 === CALLBACK TRIGGERED! OnGetAttributesResponse CALLED ===🎯🎯🎯"));
    UE_LOG(LogTemp, Error, TEXT("🎯 Widget instance: %p"), this);
    UE_LOG(LogTemp, Error, TEXT("🎯 Request ptr: %p"), Request.Get());
    UE_LOG(LogTemp, Error, TEXT("🎯 Response ptr: %p"), Response.Get());
    
    UE_LOG(LogTemp, Error, TEXT("📥 === API RESPONSE DEBUG ===="));
    UE_LOG(LogTemp, Error, TEXT("📥 Request successful: %s"), bWasSuccessful ? TEXT("YES") : TEXT("NO"));
    
    if (!bWasSuccessful)
    {
        UE_LOG(LogTemp, Error, TEXT("🚨 RESPONSE Failed to get building attributes"));
        UE_LOG(LogTemp, Error, TEXT("🚨 This means network/connection issue"));
        return;
    }

    if (!Response.IsValid())
    {
        UE_LOG(LogTemp, Error, TEXT("🚨 RESPONSE Invalid response object"));
        return;
    }

    int32 ResponseCode = Response->GetResponseCode();
    FString ResponseContent = Response->GetContentAsString();
    
    UE_LOG(LogTemp, Error, TEXT("📥 Response Code: %d"), ResponseCode);
    UE_LOG(LogTemp, Error, TEXT("📥 Content Length: %d"), ResponseContent.Len());
    
    if (ResponseContent.Len() > 500)
    {
        FString TruncatedContent = ResponseContent.Left(500) + TEXT("...");
        UE_LOG(LogTemp, Error, TEXT("📥 Raw Response: %s"), *TruncatedContent);
    }
    else
    {
        UE_LOG(LogTemp, Error, TEXT("📥 Raw Response: %s"), *ResponseContent);
    }

    if (ResponseCode != 200)
    {
        UE_LOG(LogTemp, Error, TEXT("🚨 HTTP Error Code: %d - API returned error"), ResponseCode);
        UE_LOG(LogTemp, Error, TEXT("🚨 Error content: %s"), *ResponseContent);
        return;
    }

    // Parse JSON response
    TSharedPtr<FJsonObject> JsonObject;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(ResponseContent);

    if (FJsonSerializer::Deserialize(Reader, JsonObject) && JsonObject.IsValid())
    {
        UE_LOG(LogTemp, Error, TEXT("✅ JSON parsed successfully"));
        UE_LOG(LogTemp, Error, TEXT("✅ JSON has %d fields"), JsonObject->Values.Num());
        
        // Log all JSON fields for debugging
        UE_LOG(LogTemp, Error, TEXT("📋 All JSON fields:"));
        for (auto& Pair : JsonObject->Values)
        {
            if (Pair.Value.IsValid())
            {
                UE_LOG(LogTemp, Error, TEXT("  📋 %s: %s"), *Pair.Key, *Pair.Value->AsString());
            }
        }
        
        // Call the populate function
        PopulateFormFromJson(JsonObject);
    }
    else
    {
        UE_LOG(LogTemp, Error, TEXT("🚨 Failed to parse JSON response"));
        UE_LOG(LogTemp, Error, TEXT("🚨 Raw content: %s"), *ResponseContent);
        UE_LOG(LogTemp, Error, TEXT("🚨 This means the API returned invalid JSON or error message"));
    }
}

void UBuildingAttributesWidget::OnPutAttributesResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful)
{
    UE_LOG(LogTemp, Warning, TEXT("RESPONSE === PUT Attributes Response ==="));
    
    if (!bWasSuccessful)
    {
        UE_LOG(LogTemp, Error, TEXT("SAVE ERROR: PUT request failed"));
        if (GEngine)
        {
            GEngine->AddOnScreenDebugMessage(-1, 5.0f, FColor::Red, TEXT("ERROR: Failed to save building attributes"));
        }
        return;
    }
    
    if (!Response.IsValid())
    {
        UE_LOG(LogTemp, Error, TEXT("SAVE ERROR: Invalid response"));
        if (GEngine)
        {
            GEngine->AddOnScreenDebugMessage(-1, 5.0f, FColor::Red, TEXT("ERROR: Invalid response from server"));
        }
        return;
    }
    
    int32 ResponseCode = Response->GetResponseCode();
    FString ResponseContent = Response->GetContentAsString();
    
    UE_LOG(LogTemp, Warning, TEXT("SAVE Response Code: %d"), ResponseCode);
    UE_LOG(LogTemp, Warning, TEXT("SAVE Response Content: %s"), *ResponseContent.Left(200));
    
    if (ResponseCode == 200 || ResponseCode == 201 || ResponseCode == 204)
    {
        UE_LOG(LogTemp, Warning, TEXT("SUCCESS: Building attributes saved successfully"));
        if (GEngine)
        {
            GEngine->AddOnScreenDebugMessage(-1, 3.0f, FColor::Green, TEXT("Building saved successfully!"));
        }
    }
    else
    {
        UE_LOG(LogTemp, Error, TEXT("SAVE ERROR: HTTP %d - %s"), ResponseCode, *ResponseContent);
        if (GEngine)
        {
            GEngine->AddOnScreenDebugMessage(-1, 5.0f, FColor::Red, 
                FString::Printf(TEXT("SAVE ERROR: HTTP %d"), ResponseCode));
        }
    }
}

FString UBuildingAttributesWidget::CreateAttributesJsonFromForm()
{
    // Build JSON from actual form values using correct API field names and mapped values
    TSharedPtr<FJsonObject> JsonObject = MakeShareable(new FJsonObject);
    
    UE_LOG(LogTemp, Warning, TEXT("SAVE === Collecting form data with proper API mapping ==="));
    
    // Construction year - use mapped API value instead of display value
    if (CB_ConstructionYear && !CB_ConstructionYear->GetSelectedOption().IsEmpty())
    {
        FString DisplayValue = CB_ConstructionYear->GetSelectedOption();
        FString* ApiValuePtr = ConstructionYearChoiceMap.Find(DisplayValue);
        FString ApiValue = ApiValuePtr ? *ApiValuePtr : DisplayValue;
        
        JsonObject->SetStringField(TEXT("construction_year_class"), ApiValue);
        UE_LOG(LogTemp, Warning, TEXT("SAVE Construction Year: Display='%s' -> API='%s'"), *DisplayValue, *ApiValue);
    }
    
    // Number of storeys - API expects 'storey' not 'number_of_storey'
    if (TB_NumberOfStorey && !TB_NumberOfStorey->GetText().IsEmpty())
    {
        FString StoreyValue = TB_NumberOfStorey->GetText().ToString();
        JsonObject->SetStringField(TEXT("storey"), StoreyValue);
        UE_LOG(LogTemp, Warning, TEXT("SAVE Storey: %s"), *StoreyValue);
    }
    
    // Roof storey - use mapped API value
    if (CB_RoofStorey && !CB_RoofStorey->GetSelectedOption().IsEmpty())
    {
        FString DisplayValue = CB_RoofStorey->GetSelectedOption();
        FString* ApiValuePtr = RoofStoreyChoiceMap.Find(DisplayValue);
        FString ApiValue = ApiValuePtr ? *ApiValuePtr : DisplayValue;
        
        JsonObject->SetStringField(TEXT("roof_storey"), ApiValue);
        UE_LOG(LogTemp, Warning, TEXT("SAVE Roof Storey: Display='%s' -> API='%s'"), *DisplayValue, *ApiValue);
    }
    
    // Heating system before - use mapped API value
    if (CB_HeatingSystemBefore && !CB_HeatingSystemBefore->GetSelectedOption().IsEmpty())
    {
        FString DisplayValue = CB_HeatingSystemBefore->GetSelectedOption();
        FString* ApiValuePtr = HeatingSystemChoiceMap.Find(DisplayValue);
        FString ApiValue = ApiValuePtr ? *ApiValuePtr : DisplayValue;
        
        JsonObject->SetStringField(TEXT("begin_heating_system_type_1"), ApiValue);
        UE_LOG(LogTemp, Warning, TEXT("SAVE Heating Before: Display='%s' -> API='%s'"), *DisplayValue, *ApiValue);
    }
    
    // Heating system after - use mapped API value
    if (CB_HeatingSystemAfter && !CB_HeatingSystemAfter->GetSelectedOption().IsEmpty())
    {
        FString DisplayValue = CB_HeatingSystemAfter->GetSelectedOption();
        FString* ApiValuePtr = HeatingSystemChoiceMap.Find(DisplayValue);
        FString ApiValue = ApiValuePtr ? *ApiValuePtr : DisplayValue;
        
        JsonObject->SetStringField(TEXT("end_heating_system_type_1"), ApiValue);
        UE_LOG(LogTemp, Warning, TEXT("SAVE Heating After: Display='%s' -> API='%s'"), *DisplayValue, *ApiValue);
    }
    
    // Convert to JSON string
    FString OutputString;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutputString);
    FJsonSerializer::Serialize(JsonObject.ToSharedRef(), Writer);
    
    return OutputString;
}

void UBuildingAttributesWidget::PopulateFormFromJson(TSharedPtr<FJsonObject> JsonObject)
{
    UE_LOG(LogTemp, Error, TEXT("📝 === PopulateFormFromJson DEBUG ==="));
    
    if (!JsonObject.IsValid())
    {
        UE_LOG(LogTemp, Error, TEXT("🚨 ERROR: Invalid JSON object for form population"));
        return;
    }

    // Set building title text with current building ID (case-preserved)
    if (BuildingTitleText)
    {
        FString TitleText = FString::Printf(TEXT("Building Attributes - %s"), *CurrentBuildingKey);
        BuildingTitleText->SetText(FText::FromString(TitleText));
        UE_LOG(LogTemp, Error, TEXT("📝 Set title text: %s"), *TitleText);
    }

    // Debug: Log all top-level fields in JSON response
    UE_LOG(LogTemp, Error, TEXT("📝 JSON Debug - Available fields (%d total):"), JsonObject->Values.Num());
    for (auto& Pair : JsonObject->Values)
    {
        if (Pair.Value.IsValid())
        {
            FString ValueStr = TEXT("NULL");
            if (Pair.Value->Type == EJson::String)
            {
                ValueStr = Pair.Value->AsString();
            }
            else if (Pair.Value->Type == EJson::Number)
            {
                ValueStr = FString::Printf(TEXT("%.2f"), Pair.Value->AsNumber());
            }
            else if (Pair.Value->Type == EJson::Boolean)
            {
                ValueStr = Pair.Value->AsBool() ? TEXT("true") : TEXT("false");
            }
            UE_LOG(LogTemp, Error, TEXT("  📝 JSON Field: %s = %s"), *Pair.Key, *ValueStr);
        }
    }
    
    // Extract direct field values from JSON response
    // The API returns building data directly, not in nested sections
    
    // Construction year class
    FString ConstructionYear;
    if (JsonObject->TryGetStringField(TEXT("construction_year_class"), ConstructionYear))
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM Found construction_year_class: %s"), *ConstructionYear);
        if (CB_ConstructionYear)
        {
            CB_ConstructionYear->SetSelectedOption(ConstructionYear);
        }
    }
    
    // Number of storeys (API field: 'storey')
    FString StoreyValue;
    if (JsonObject->TryGetStringField(TEXT("storey"), StoreyValue))
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM Found storey: %s"), *StoreyValue);
        if (TB_NumberOfStorey)
        {
            TB_NumberOfStorey->SetText(FText::FromString(StoreyValue));
        }
    }
    
    // Roof storey
    FString RoofStorey;
    if (JsonObject->TryGetStringField(TEXT("roof_storey"), RoofStorey))
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM Found roof_storey: %s"), *RoofStorey);
        if (CB_RoofStorey)
        {
            CB_RoofStorey->SetSelectedOption(RoofStorey);
        }
    }
    
    // Heating system before renovation
    FString HeatingBefore;
    if (JsonObject->TryGetStringField(TEXT("begin_heating_system_type_1"), HeatingBefore))
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM Found begin_heating_system_type_1: %s"), *HeatingBefore);
        if (CB_HeatingSystemBefore)
        {
            CB_HeatingSystemBefore->SetSelectedOption(HeatingBefore);
        }
    }
    
    // Heating system after renovation
    FString HeatingAfter;
    if (JsonObject->TryGetStringField(TEXT("end_heating_system_type_1"), HeatingAfter))
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM Found end_heating_system_type_1: %s"), *HeatingAfter);
        if (CB_HeatingSystemAfter)
        {
            CB_HeatingSystemAfter->SetSelectedOption(HeatingAfter);
        }
    }
    
    UE_LOG(LogTemp, Warning, TEXT("FORM === PopulateFormFromJson COMPLETED ==="));

    // Debug: Check if widgets are valid
    UE_LOG(LogTemp, Warning, TEXT("FORM Widget Validation:"));
    UE_LOG(LogTemp, Warning, TEXT("  CB_ConstructionYear: %s"), CB_ConstructionYear ? TEXT("VALID") : TEXT("NULL"));
    UE_LOG(LogTemp, Warning, TEXT("  TB_NumberOfStorey: %s"), TB_NumberOfStorey ? TEXT("VALID") : TEXT("NULL"));
    UE_LOG(LogTemp, Warning, TEXT("  CB_RoofStorey: %s"), CB_RoofStorey ? TEXT("VALID") : TEXT("NULL"));
    UE_LOG(LogTemp, Warning, TEXT("  CB_HeatingSystemBefore: %s"), CB_HeatingSystemBefore ? TEXT("VALID") : TEXT("NULL"));
    UE_LOG(LogTemp, Warning, TEXT("  BuildingTitleText: %s"), BuildingTitleText ? TEXT("VALID") : TEXT("NULL"));

    // Debug: Log all top-level sections in JSON (like Python code)
    UE_LOG(LogTemp, Warning, TEXT("FORM JSON Debug - Top level sections:"));
    for (auto& Pair : JsonObject->Values)
    {
        UE_LOG(LogTemp, Warning, TEXT("  JSON Section: %s"), *Pair.Key);
    }
    
    // Debug: Log complete JSON structure for analysis
    UE_LOG(LogTemp, Warning, TEXT("FORM === COMPLETE API STRUCTURE DEBUG ==="));
    for (auto& SectionPair : JsonObject->Values)
    {
        UE_LOG(LogTemp, Warning, TEXT("SECTION: %s"), *SectionPair.Key);
        
        const TSharedPtr<FJsonObject>* SectionObjPtr = nullptr;
        if (SectionPair.Value->TryGetObject(SectionObjPtr))
        {
            const TSharedPtr<FJsonObject> SectionObj = *SectionObjPtr;
            
            if (SectionObj->HasField(TEXT("fields")))
            {
                const TSharedPtr<FJsonObject>* FieldsPtr = nullptr;
                if (SectionObj->TryGetObjectField(TEXT("fields"), FieldsPtr))
                {
                    const TSharedPtr<FJsonObject> Fields = *FieldsPtr;
                    UE_LOG(LogTemp, Warning, TEXT("  %s has %d fields:"), *SectionPair.Key, Fields->Values.Num());
                    
                    for (auto& FieldPair : Fields->Values)
                    {
                        UE_LOG(LogTemp, Warning, TEXT("    FIELD: %s"), *FieldPair.Key);
                        
                        const TSharedPtr<FJsonObject>* FieldObjPtr = nullptr;
                        if (FieldPair.Value->TryGetObject(FieldObjPtr))
                        {
                            const TSharedPtr<FJsonObject> FieldObj = *FieldObjPtr;
                            
                        // Check if field has choices and build mappings for dropdown fields
                        const TArray<TSharedPtr<FJsonValue>>* ChoicesArray;
                        if (FieldObj->TryGetArrayField(TEXT("choices"), ChoicesArray))
                        {
                            UE_LOG(LogTemp, Log, TEXT("      Building choice mapping for %s with %d choices"), *FieldPair.Key, ChoicesArray->Num());
                            
                            // Clear existing mappings for this field
                            if (FieldPair.Key.Equals(TEXT("construction_year_class")))
                            {
                                ConstructionYearChoiceMap.Empty();
                            }
                            else if (FieldPair.Key.Equals(TEXT("roof_storey")))
                            {
                                RoofStoreyChoiceMap.Empty();
                            }
                            else if (FieldPair.Key.Contains(TEXT("heating_system")))
                            {
                                HeatingSystemChoiceMap.Empty();
                            }
                            
                            for (int32 i = 0; i < ChoicesArray->Num(); i++)
                            {
                                const TSharedPtr<FJsonValue>& ChoiceValue = (*ChoicesArray)[i];
                                
                                // Handle array format: [code, label]
                                const TArray<TSharedPtr<FJsonValue>>* ChoiceArray;
                                if (ChoiceValue->TryGetArray(ChoiceArray) && ChoiceArray->Num() >= 2)
                                {
                                    FString Code, Label;
                                    (*ChoiceArray)[0]->TryGetString(Code);
                                    (*ChoiceArray)[1]->TryGetString(Label);
                                    
                                    // Store mapping: Display Label -> API Code
                                    if (FieldPair.Key.Equals(TEXT("construction_year_class")))
                                    {
                                        ConstructionYearChoiceMap.Add(Label, Code);
                                    }
                                    else if (FieldPair.Key.Equals(TEXT("roof_storey")))
                                    {
                                        RoofStoreyChoiceMap.Add(Label, Code);
                                    }
                                    else if (FieldPair.Key.Contains(TEXT("heating_system")))
                                    {
                                        HeatingSystemChoiceMap.Add(Label, Code);
                                    }
                                    
                                    UE_LOG(LogTemp, Log, TEXT("        Choice mapping: '%s' -> '%s'"), *Label, *Code);
                                }
                                // Handle simple string choices
                                else
                                {
                                    FString ChoiceString;
                                    if (ChoiceValue->TryGetString(ChoiceString))
                                    {
                                        UE_LOG(LogTemp, Log, TEXT("        Simple choice: %s"), *ChoiceString);
                                    }
                                }
                            }
                        }
                        }
                    }
                }
            }
        }
    }
    UE_LOG(LogTemp, Warning, TEXT("FORM === END COMPLETE API STRUCTURE DEBUG ==="));
    
    // Helper function to extract field value - prefer "display" over "value" (Python pattern)
    auto ExtractFieldValue = [](const TSharedPtr<FJsonObject>& FieldObject) -> FString
    {
        if (!FieldObject.IsValid()) return TEXT("");
        
        // Prefer display value (human-readable) over raw value
        FString DisplayValue;
        if (FieldObject->TryGetStringField(TEXT("display"), DisplayValue))
        {
            return DisplayValue;
        }
        
        FString Value;
        if (FieldObject->TryGetStringField(TEXT("value"), Value))
        {
            return Value;
        }
        
        return TEXT("");
    };
    
    // Helper function to process choices and populate ComboBox (inspired by Python _extract_choice_list)
    auto PopulateComboBoxFromChoices = [this](UComboBoxString* ComboBox, const TSharedPtr<FJsonObject>& FieldObject, const FString& CurrentValue) -> bool
    {
        if (!ComboBox || !FieldObject.IsValid()) return false;
        
        const TArray<TSharedPtr<FJsonValue>>* ChoicesArray;
        if (FieldObject->TryGetArrayField(TEXT("choices"), ChoicesArray))
        {
            UE_LOG(LogTemp, Warning, TEXT("FORM Found choices array with %d options"), ChoicesArray->Num());
            ComboBox->ClearOptions();
            
            for (int32 i = 0; i < ChoicesArray->Num(); i++)
            {
                const TSharedPtr<FJsonValue>& ChoiceValue = (*ChoicesArray)[i];
                
                // Handle array format: [code, label] like Python code
                const TArray<TSharedPtr<FJsonValue>>* ChoiceArray;
                if (ChoiceValue->TryGetArray(ChoiceArray) && ChoiceArray->Num() >= 2)
                {
                    FString Label;
                    if ((*ChoiceArray)[1]->TryGetString(Label))
                    {
                        ComboBox->AddOption(Label);
                        UE_LOG(LogTemp, Warning, TEXT("FORM Added choice: %s"), *Label);
                        
                        // Set selection if this matches current value
                        if (Label == CurrentValue || (ChoiceArray->Num() > 0 && (*ChoiceArray)[0]->AsString() == CurrentValue))
                        {
                            ComboBox->SetSelectedOption(Label);
                            UE_LOG(LogTemp, Warning, TEXT("FORM Set selected option: %s"), *Label);
                        }
                    }
                }
                // Handle simple string choices
                else
                {
                    FString ChoiceString;
                    if (ChoiceValue->TryGetString(ChoiceString))
                    {
                        ComboBox->AddOption(ChoiceString);
                        if (ChoiceString == CurrentValue)
                        {
                            ComboBox->SetSelectedOption(ChoiceString);
                        }
                    }
                }
            }
            return true;
        }
        return false;
    };

    // Process sections following the Python pattern
    bool bFoundValidData = false;

    // Process general_info section
    const TSharedPtr<FJsonObject>* GeneralInfoPtr = nullptr;
    if (JsonObject->TryGetObjectField(TEXT("general_info"), GeneralInfoPtr))
    {
        const TSharedPtr<FJsonObject> GeneralInfo = *GeneralInfoPtr;
        UE_LOG(LogTemp, Warning, TEXT("FORM === GENERAL INFO SECTION ==="));

        // Check for nested fields structure
        if (GeneralInfo->HasField(TEXT("fields")))
        {
            const TSharedPtr<FJsonObject>* FieldsPtr = nullptr;
            if (GeneralInfo->TryGetObjectField(TEXT("fields"), FieldsPtr))
            {
                const TSharedPtr<FJsonObject> Fields = *FieldsPtr;
                UE_LOG(LogTemp, Warning, TEXT("FORM general_info has %d fields"), Fields->Values.Num());
                
                for (auto& FieldPair : Fields->Values)
                {
                    const TSharedPtr<FJsonObject>* FieldObjPtr = nullptr;
                    if (FieldPair.Value->TryGetObject(FieldObjPtr))
                    {
                        const TSharedPtr<FJsonObject> FieldObj = *FieldObjPtr;
                        FString FieldValue = ExtractFieldValue(FieldObj);
                        UE_LOG(LogTemp, Warning, TEXT("FORM Field '%s': '%s'"), *FieldPair.Key, *FieldValue);
                        
                        // Map fields to widgets based on exact field names (more precise mapping)
                        UE_LOG(LogTemp, Warning, TEXT("FORM Processing field: '%s' with value: '%s'"), *FieldPair.Key, *FieldValue);
                        
                        // Roof type - dropdown (MUST BE BEFORE storey check to catch roof_storey)
                        if (FieldPair.Key.Contains(TEXT("roof")) ||
                            FieldPair.Key == TEXT("roof_storey_type") ||
                            FieldPair.Key == TEXT("roof_type") ||
                            FieldPair.Key == TEXT("roof_storey") ||
                            FieldPair.Key.Contains(TEXT("number_type_of_roof")) ||
                            FieldPair.Key.Contains(TEXT("roof_construction")))
                        {
                            if (CB_RoofStorey)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_RoofStorey"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_RoofStorey, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for roof field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                            else
                            {
                                UE_LOG(LogTemp, Error, TEXT("FORM CB_RoofStorey widget is NULL"));
                            }
                        }
                        // Construction Year Class - exact field name matching
                        else if (FieldPair.Key == TEXT("construction_year_class") || 
                            FieldPair.Key.Contains(TEXT("construction_year")) || 
                            FieldPair.Key.Contains(TEXT("building_year")))
                        {
                            if (CB_ConstructionYear)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_ConstructionYear"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_ConstructionYear, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for construction year field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                            else
                            {
                                UE_LOG(LogTemp, Error, TEXT("FORM CB_ConstructionYear widget is NULL"));
                            }
                        }
                        // Number of storeys - text input (must be AFTER roof check to avoid catching roof_storey)
                        else if ((FieldPair.Key.Contains(TEXT("storey")) && !FieldPair.Key.Contains(TEXT("roof"))) || 
                                FieldPair.Key.Contains(TEXT("floor")) ||
                                FieldPair.Key == TEXT("number_of_storey") ||
                                FieldPair.Key == TEXT("storey"))
                        {
                            if (TB_NumberOfStorey)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to TB_NumberOfStorey"), *FieldPair.Key);
                                TB_NumberOfStorey->SetText(FText::FromString(FieldValue));
                                UE_LOG(LogTemp, Warning, TEXT("FORM Set Number of Storeys: %s"), *FieldValue);
                                bFoundValidData = true;
                            }
                            else
                            {
                                UE_LOG(LogTemp, Error, TEXT("FORM TB_NumberOfStorey widget is NULL"));
                            }
                        }
                        else
                        {
                            UE_LOG(LogTemp, Warning, TEXT("FORM No widget mapping found for field: %s"), *FieldPair.Key);
                        }
                    }
                }
            }
        }
    }

    // Process begin_of_project section (Before Renovation)
    const TSharedPtr<FJsonObject>* BeginProjectPtr = nullptr;
    if (JsonObject->TryGetObjectField(TEXT("begin_of_project"), BeginProjectPtr))
    {
        const TSharedPtr<FJsonObject> BeginProject = *BeginProjectPtr;
        UE_LOG(LogTemp, Warning, TEXT("FORM === BEFORE RENOVATION SECTION ==="));

        if (BeginProject->HasField(TEXT("fields")))
        {
            const TSharedPtr<FJsonObject>* FieldsPtr = nullptr;
            if (BeginProject->TryGetObjectField(TEXT("fields"), FieldsPtr))
            {
                const TSharedPtr<FJsonObject> Fields = *FieldsPtr;
                UE_LOG(LogTemp, Warning, TEXT("FORM begin_of_project has %d fields"), Fields->Values.Num());
                
                for (auto& FieldPair : Fields->Values)
                {
                    const TSharedPtr<FJsonObject>* FieldObjPtr = nullptr;
                    if (FieldPair.Value->TryGetObject(FieldObjPtr))
                    {
                        const TSharedPtr<FJsonObject> FieldObj = *FieldObjPtr;
                        FString FieldValue = ExtractFieldValue(FieldObj);
                        UE_LOG(LogTemp, Warning, TEXT("FORM Before Field '%s': '%s'"), *FieldPair.Key, *FieldValue);
                        
                        // Map fields to "Before" widgets with specific field name matching
                        UE_LOG(LogTemp, Warning, TEXT("FORM Before Field Processing: '%s' with value: '%s'"), *FieldPair.Key, *FieldValue);
                        
                        // Heating System Types - multiple possible field names
                        if (FieldPair.Key.Contains(TEXT("heating")) ||
                            FieldPair.Key == TEXT("heating_system_type") ||
                            FieldPair.Key == TEXT("heating_system_type_1") ||
                            FieldPair.Key.Contains(TEXT("heating_before")))
                        {
                            if (CB_HeatingSystemBefore)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_HeatingSystemBefore"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_HeatingSystemBefore, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for heating before field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                            else
                            {
                                UE_LOG(LogTemp, Error, TEXT("FORM CB_HeatingSystemBefore widget is NULL"));
                            }
                        }
                        // Window renovation class
                        else if (FieldPair.Key.Contains(TEXT("window")) ||
                                FieldPair.Key == TEXT("construction_year_class_of_renovated_window") ||
                                FieldPair.Key.Contains(TEXT("window_renovation")) ||
                                FieldPair.Key.Contains(TEXT("window_construction")))
                        {
                            if (CB_WindowBefore)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_WindowBefore"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_WindowBefore, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for window before field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                            else
                            {
                                UE_LOG(LogTemp, Error, TEXT("FORM CB_WindowBefore widget is NULL"));
                            }
                        }
                        // Wall renovation class
                        else if (FieldPair.Key.Contains(TEXT("wall")) ||
                                FieldPair.Key == TEXT("construction_year_class_of_renovated_wall") ||
                                FieldPair.Key.Contains(TEXT("wall_renovation")) ||
                                FieldPair.Key.Contains(TEXT("wall_construction")))
                        {
                            if (CB_WallBefore)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_WallBefore"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_WallBefore, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for wall before field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                            else
                            {
                                UE_LOG(LogTemp, Error, TEXT("FORM CB_WallBefore widget is NULL"));
                            }
                        }
                        // Roof and Ceiling before renovation
                        else if (FieldPair.Key.Contains(TEXT("roof")) && !FieldPair.Key.Contains(TEXT("storey")))
                        {
                            if (CB_RoofBefore)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_RoofBefore"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_RoofBefore, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for roof before field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                        }
                        else if (FieldPair.Key.Contains(TEXT("ceiling")))
                        {
                            if (CB_CeilingBefore)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_CeilingBefore"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_CeilingBefore, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for ceiling before field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                        }
                        else
                        {
                            UE_LOG(LogTemp, Warning, TEXT("FORM No Before widget mapping found for field: %s"), *FieldPair.Key);
                        }
                    }
                }
            }
        }
    }

    // Process end_of_project section (After Renovation)  
    const TSharedPtr<FJsonObject>* EndProjectPtr = nullptr;
    if (JsonObject->TryGetObjectField(TEXT("end_of_project"), EndProjectPtr))
    {
        const TSharedPtr<FJsonObject> EndProject = *EndProjectPtr;
        UE_LOG(LogTemp, Warning, TEXT("FORM === AFTER RENOVATION SECTION ==="));

        if (EndProject->HasField(TEXT("fields")))
        {
            const TSharedPtr<FJsonObject>* FieldsPtr = nullptr;
            if (EndProject->TryGetObjectField(TEXT("fields"), FieldsPtr))
            {
                const TSharedPtr<FJsonObject> Fields = *FieldsPtr;
                UE_LOG(LogTemp, Warning, TEXT("FORM end_of_project has %d fields"), Fields->Values.Num());
                
                for (auto& FieldPair : Fields->Values)
                {
                    const TSharedPtr<FJsonObject>* FieldObjPtr = nullptr;
                    if (FieldPair.Value->TryGetObject(FieldObjPtr))
                    {
                        const TSharedPtr<FJsonObject> FieldObj = *FieldObjPtr;
                        FString FieldValue = ExtractFieldValue(FieldObj);
                        UE_LOG(LogTemp, Warning, TEXT("FORM After Field '%s': '%s'"), *FieldPair.Key, *FieldValue);
                        
                        // Map fields to "After" widgets with specific field name matching
                        UE_LOG(LogTemp, Warning, TEXT("FORM After Field Processing: '%s' with value: '%s'"), *FieldPair.Key, *FieldValue);
                        
                        // Heating System Types - multiple possible field names
                        if (FieldPair.Key.Contains(TEXT("heating")) ||
                            FieldPair.Key == TEXT("heating_system_type") ||
                            FieldPair.Key == TEXT("heating_system_type_1") ||
                            FieldPair.Key.Contains(TEXT("heating_after")))
                        {
                            if (CB_HeatingSystemAfter)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_HeatingSystemAfter"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_HeatingSystemAfter, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for heating after field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                            else
                            {
                                UE_LOG(LogTemp, Error, TEXT("FORM CB_HeatingSystemAfter widget is NULL"));
                            }
                        }
                        // Window renovation class
                        else if (FieldPair.Key.Contains(TEXT("window")) ||
                                FieldPair.Key == TEXT("construction_year_class_of_renovated_window") ||
                                FieldPair.Key.Contains(TEXT("window_renovation")) ||
                                FieldPair.Key.Contains(TEXT("window_construction")))
                        {
                            if (CB_WindowAfter)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_WindowAfter"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_WindowAfter, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for window after field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                            else
                            {
                                UE_LOG(LogTemp, Error, TEXT("FORM CB_WindowAfter widget is NULL"));
                            }
                        }
                        // Wall renovation class
                        else if (FieldPair.Key.Contains(TEXT("wall")) ||
                                FieldPair.Key == TEXT("construction_year_class_of_renovated_wall") ||
                                FieldPair.Key.Contains(TEXT("wall_renovation")) ||
                                FieldPair.Key.Contains(TEXT("wall_construction")))
                        {
                            if (CB_WallAfter)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_WallAfter"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_WallAfter, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for wall after field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                            else
                            {
                                UE_LOG(LogTemp, Error, TEXT("FORM CB_WallAfter widget is NULL"));
                            }
                        }
                        // Roof and Ceiling after renovation
                        else if (FieldPair.Key.Contains(TEXT("roof")) && !FieldPair.Key.Contains(TEXT("storey")))
                        {
                            if (CB_RoofAfter)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_RoofAfter"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_RoofAfter, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for roof after field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                        }
                        else if (FieldPair.Key.Contains(TEXT("ceiling")))
                        {
                            if (CB_CeilingAfter)
                            {
                                UE_LOG(LogTemp, Warning, TEXT("FORM Mapping '%s' to CB_CeilingAfter"), *FieldPair.Key);
                                bool bPopulatedFromChoices = PopulateComboBoxFromChoices(CB_CeilingAfter, FieldObj, FieldValue);
                                
                                if (!bPopulatedFromChoices)
                                {
                                    UE_LOG(LogTemp, Warning, TEXT("FORM No choices provided for ceiling after field '%s' - leaving empty"), *FieldPair.Key);
                                }
                                bFoundValidData = true;
                            }
                        }
                        else
                        {
                            UE_LOG(LogTemp, Warning, TEXT("FORM No After widget mapping found for field: %s"), *FieldPair.Key);
                        }
                    }
                }
            }
        }
    }

    if (bFoundValidData)
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM Successfully populated form with API data!"));
    }
    else
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM No valid field data found - form may be empty or API structure different"));
    }

    UE_LOG(LogTemp, Warning, TEXT("FORM === PopulateFormFromJson COMPLETED ==="));
}

// === REAL-TIME FORM SYNCHRONIZATION IMPLEMENTATION ===

void UBuildingAttributesWidget::NativeTick(const FGeometry& MyGeometry, float InDeltaTime)
{
    Super::NativeTick(MyGeometry, InDeltaTime);
    
    // Real-time form monitoring
    if (bFormRealTimeEnabled && !bIsFormDataChecking && !CurrentBuildingKey.IsEmpty())
    {
        FormRealTimeTimer += InDeltaTime;
        if (FormRealTimeTimer >= FormUpdateInterval)
        {
            FormRealTimeTimer = 0.0f;
            UE_LOG(LogTemp, Verbose, TEXT("FORM-RT Performing background form data check..."));
            PerformFormDataCheck();
        }
    }
}

void UBuildingAttributesWidget::StartFormRealTimeSync()
{
    bFormRealTimeEnabled = true;
    FormRealTimeTimer = 0.0f;
    UE_LOG(LogTemp, Warning, TEXT("FORM-RT Form real-time synchronization STARTED (checking every %.1f seconds)"), FormUpdateInterval);
}

void UBuildingAttributesWidget::StopFormRealTimeSync()
{
    bFormRealTimeEnabled = false;
    bIsFormDataChecking = false;
    UE_LOG(LogTemp, Warning, TEXT("FORM-RT Form real-time synchronization STOPPED"));
}

void UBuildingAttributesWidget::SetFormUpdateInterval(float Seconds)
{
    if (Seconds < 1.0f)
    {
        FormUpdateInterval = 1.0f;
        UE_LOG(LogTemp, Warning, TEXT("FORM-RT Minimum form update interval is 1 second"));
    }
    else if (Seconds > 30.0f)
    {
        FormUpdateInterval = 30.0f;
        UE_LOG(LogTemp, Warning, TEXT("FORM-RT Maximum form update interval is 30 seconds"));
    }
    else
    {
        FormUpdateInterval = Seconds;
    }
    
    UE_LOG(LogTemp, Warning, TEXT("FORM-RT Form update interval set to %.1f seconds"), FormUpdateInterval);
}

void UBuildingAttributesWidget::EnableFormRealTime(bool bEnable)
{
    if (bEnable)
    {
        StartFormRealTimeSync();
    }
    else
    {
        StopFormRealTimeSync();
    }
}

void UBuildingAttributesWidget::PerformFormDataCheck()
{
    if (bIsFormDataChecking)
    {
        UE_LOG(LogTemp, Verbose, TEXT("FORM-RT Form data check already in progress, skipping"));
        return;
    }
    
    if (CurrentBuildingKey.IsEmpty() || AccessToken.IsEmpty())
    {
        UE_LOG(LogTemp, Verbose, TEXT("FORM-RT No building or token available for form data check"));
        return;
    }
    
    bIsFormDataChecking = true;
    
    // Create HTTP request to check current form data
    TSharedRef<IHttpRequest> Request = FHttpModule::Get().CreateRequest();
    
    FString ApiBaseUrl = TEXT("https://backend.gisworld-tech.com");
    FString Url = FString::Printf(TEXT("%s/geospatial/buildings-energy/%s/?community_id=%s&field_type=basic"), 
        *ApiBaseUrl, *CurrentBuildingKey, *CommunityId);
    
    Request->SetURL(Url);
    Request->SetVerb(TEXT("GET"));
    Request->SetHeader(TEXT("Authorization"), FString::Printf(TEXT("Bearer %s"), *AccessToken));
    Request->SetHeader(TEXT("Content-Type"), TEXT("application/json"));
    
    // Real-time cache busting headers
    Request->SetHeader(TEXT("Cache-Control"), TEXT("no-cache, no-store, must-revalidate"));
    Request->SetHeader(TEXT("Pragma"), TEXT("no-cache"));
    
    Request->OnProcessRequestComplete().BindUObject(this, &UBuildingAttributesWidget::OnFormRealTimeDataResponse);
    
    if (Request->ProcessRequest())
    {
        UE_LOG(LogTemp, Verbose, TEXT("FORM-RT Background form data check request sent for: %s"), *CurrentBuildingKey);
    }
    else
    {
        UE_LOG(LogTemp, Error, TEXT("FORM-RT Failed to send background form data check request"));
        bIsFormDataChecking = false;
    }
}

void UBuildingAttributesWidget::OnFormRealTimeDataResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful)
{
    bIsFormDataChecking = false;
    
    if (!bWasSuccessful || !Response.IsValid())
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM-RT Background form data check failed"));
        return;
    }
    
    if (Response->GetResponseCode() != 200)
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM-RT Background form data check returned HTTP %d"), Response->GetResponseCode());
        return;
    }
    
    FString ResponseContent = Response->GetContentAsString();
    if (ResponseContent.IsEmpty())
    {
        UE_LOG(LogTemp, Warning, TEXT("FORM-RT Background form data check returned empty response"));
        return;
    }
    
    UE_LOG(LogTemp, Verbose, TEXT("FORM-RT Background form data check successful, analyzing for changes..."));
    DetectAndApplyFormChanges(ResponseContent);
}

void UBuildingAttributesWidget::DetectAndApplyFormChanges(const FString& NewFormData)
{
    // Check if form data has changed
    if (PreviousFormDataSnapshot.IsEmpty())
    {
        // First time - just store the snapshot
        PreviousFormDataSnapshot = NewFormData;
        UE_LOG(LogTemp, Verbose, TEXT("FORM-RT Initial form data snapshot created"));
        return;
    }
    
    if (PreviousFormDataSnapshot.Equals(NewFormData))
    {
        UE_LOG(LogTemp, Verbose, TEXT("FORM-RT No form changes detected"));
        return;
    }
    
    // Changes detected - update form
    UE_LOG(LogTemp, Warning, TEXT("FORM-RT CHANGES DETECTED! Updating form fields automatically..."));
    
    // Parse new JSON data
    TSharedPtr<FJsonObject> JsonObject;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(NewFormData);
    
    if (FJsonSerializer::Deserialize(Reader, JsonObject) && JsonObject.IsValid())
    {
        UpdateFormFieldsAutomatically(JsonObject);
        NotifyFormRealTimeChanges();
    }
    else
    {
        UE_LOG(LogTemp, Error, TEXT("FORM-RT Failed to parse new form data"));
    }
    
    // Update snapshot
    PreviousFormDataSnapshot = NewFormData;
}

void UBuildingAttributesWidget::UpdateFormFieldsAutomatically(TSharedPtr<FJsonObject> NewData)
{
    // Extract the building attributes from the results array
    const TArray<TSharedPtr<FJsonValue>>* ResultsArray;
    if (!NewData->TryGetArrayField(TEXT("results"), ResultsArray) || ResultsArray->Num() == 0)
    {
        UE_LOG(LogTemp, Error, TEXT("FORM-RT No results array in new form data"));
        return;
    }
    
    TSharedPtr<FJsonObject> BuildingData = (*ResultsArray)[0]->AsObject();
    if (!BuildingData.IsValid())
    {
        UE_LOG(LogTemp, Error, TEXT("FORM-RT Invalid building data in results"));
        return;
    }
    
    UE_LOG(LogTemp, Warning, TEXT("FORM-RT Automatically updating form fields with fresh data..."));
    
    // Use existing form population logic but mark it as automatic update
    PopulateFormFromJson(BuildingData);
    
    UE_LOG(LogTemp, Warning, TEXT("FORM-RT Form fields updated automatically"));
}

void UBuildingAttributesWidget::NotifyFormRealTimeChanges()
{
    UE_LOG(LogTemp, Warning, TEXT("FORM-RT Real-time form changes applied"));
}