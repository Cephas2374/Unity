// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Http.h"
#include "TimerManager.h"
#include "Components/StaticMeshComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "Components/CanvasPanelSlot.h"
#include "Engine/LocalPlayer.h"
#include "Engine/GameViewportClient.h"
#include "WebSocketsModule.h"
#include "IWebSocket.h"
#include "BuildingEnergyDisplay.generated.h"

// Forward declarations for UMG widgets
class UUserWidget;
class UTextBlock;
class ACesium3DTileset;

USTRUCT(BlueprintType)
struct FBuildingBoundingBox
{
	GENERATED_BODY()
	
	UPROPERTY(BlueprintReadWrite, Category = "Bounding Box")
	FVector MinBounds;
	
	UPROPERTY(BlueprintReadWrite, Category = "Bounding Box")
	FVector MaxBounds;
	
	UPROPERTY(BlueprintReadWrite, Category = "Bounding Box")
	FVector Center;
	
	UPROPERTY(BlueprintReadWrite, Category = "Bounding Box")
	FVector Size;
	
	FBuildingBoundingBox()
	{
		MinBounds = FVector::ZeroVector;
		MaxBounds = FVector::ZeroVector;
		Center = FVector::ZeroVector;
		Size = FVector::ZeroVector;
	}
};

UCLASS(Blueprintable)
class FINAL_PROJECT_API ABuildingEnergyDisplay : public AActor
{
	GENERATED_BODY()
	
public:
	ABuildingEnergyDisplay();

protected:
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

public:
	virtual void Tick(float DeltaTime) override;

	// ================= CESIUM STYLING CONTROLS =================
	// Name (or substring) of the buildings tileset actor in the World Outliner.
	// The bisingen buildings tileset actor is named "Cesium3DTileset_0" in Unreal
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Building Energy|Cesium")
	FString BuildingsTilesetName = TEXT("Cesium3DTileset_0");

	// Enable Cesium 3D Tiles Styling (per-feature colors). If disabled, we will not call SetTilesetStyleFromJson.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Building Energy|Cesium")
	bool bEnableCesiumPerFeatureStyling = true;

	// Debug: force all buildings to red via Cesium styling to validate the pipeline.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Building Energy|Debug")
	bool bDebugForceRedStyle = false;

	// Candidate metadata keys for the building id in the tileset.
	// We will try these in order (via coalesce) when building the style JSON.
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Building Energy|Cesium")
	TArray<FString> CandidateGmlIdPropertyKeys;


	UPROPERTY(BlueprintReadWrite, Category = "Building Energy")
	FString AccessToken;

	UPROPERTY(BlueprintReadWrite, Category = "Building Energy")
	FString ModifiedGmlId;

	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	void PreloadAllBuildingData(const FString& Token);

	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	void DisplayBuildingData(const FString& GmlId);

	void FetchBuildingEnergyData(const FString& GmlId, const FString& Token);

	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	void ClearCache();

	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	void RefreshBuildingCache();

	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	void AuthenticateAndLoadData();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	void RefreshAccessToken(); // Use refresh token to get new access token
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	void ApplyColorsToCSiumTileset();
	
	// Debug and test functions
	UFUNCTION(BlueprintCallable, Category = "Building Energy Debug")
	void TestColorSystem();
	
	// Color persistence for Cesium tileset refreshes
	UPROPERTY()
	FTimerHandle CesiumRefreshTimer;
	
	void SetupCesiumRefreshMonitoring();
	void OnCesiumTilesetRefresh();
	
	// Direct color application (bypass Cesium metadata system)
	void SetupDirectColorApplication();
	void ApplyColorsDirectlyToGeometry();
	void ApplyRepresentativeColorToAllBuildings(AActor* TilesetActor);
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Debug")
	void LogColorCacheStatus();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Debug")
	void ForceApplyColors();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Debug")
	void DebugCesiumPropertyMapping();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	void ApplyColorsNow();
	
	// Force immediate color application (bypasses all delays)
	UFUNCTION(BlueprintCallable, Category = "Building Energy Debug")  
	void ForceColorsNow();
	
	// Material utilities for proper color application
	UMaterialInstanceDynamic* CreateOrGetDynamicMaterial(UStaticMeshComponent* MeshComp, int32 MaterialIndex);
	void EnsureProperMaterialParameters(UMaterialInstanceDynamic* DynMaterial);
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	UMaterialInstanceDynamic* CreateBuildingEnergyMaterial();

	UFUNCTION(BlueprintCallable, BlueprintPure, Category = "Building Energy")
	bool IsDataLoaded() const { return bDataLoaded; }

	UFUNCTION(BlueprintCallable, Category = "Building Energy")
	void DisplayBuildingEnergyData(const FString& BuildingGmlId);
	
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Building Energy", meta = (DisplayName = "Building Energy Material"))
	UMaterialInstanceDynamic* BuildingEnergyMaterial;
	
	// UMG Widget for Building Information Display
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Building Info Widget")
	TSubclassOf<UUserWidget> BuildingInfoWidgetClass;
	
	UPROPERTY(BlueprintReadOnly, Category = "Building Info Widget")
	UUserWidget* CurrentBuildingInfoWidget;
	
	// Single Building Display Control
	UPROPERTY(BlueprintReadOnly, Category = "Building Energy")
	FString CurrentlyDisplayedBuildingId;
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void AssignMaterialToCesiumTileset();
	
	UFUNCTION(CallInEditor, BlueprintCallable, Category = "Building Energy Display")
	void CreateMaterialForEditor();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void ApplyPerBuildingColorsToCesium();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void ApplyColorsUsingCesiumStyling();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void ApplyConditionalStylingToTileset();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void ApplyOfficialCesiumMetadataVisualization();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void ApplyColorToClickedBuilding(const FString& BuildingGmlId);
	
	UFUNCTION(CallInEditor, BlueprintCallable, Category = "Building Energy Display")
	void CreateTextureBasedMaterial();
	
	UFUNCTION(CallInEditor, BlueprintCallable, Category = "Building Energy Display")
	void CreatePerBuildingColorMaterial();
	
	// ✨ RENDER TARGET TEXTURE APPROACH - Per-building colors via texture lookup
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void CreateBuildingColorLookupTexture();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void UpdateBuildingColorLookupTexture();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void ApplyColorLookupTextureToTileset();
	
	// 🔴 MANUAL COLOR APPLICATION - Call this to force apply colors
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void ForceApplyColorsNow();
	
	// 🔍 HELPER: Get gml:id from component using Cesium metadata
	FString GetGmlIdFromComponent(UPrimitiveComponent* Component);
	
	// 🎨 NEW: Cesium Material Layer Color Lookup System
	UFUNCTION(CallInEditor, BlueprintCallable, Category = "Building Energy Display")
	void GenerateFeatureIDToColorTexture();
	
	UFUNCTION(CallInEditor, BlueprintCallable, Category = "Building Energy Display")
	void ApplyColorLookupTextureToCesiumMaterial();
	
	// Builds mapping: FeatureID → gml:id → Color
	void BuildFeatureIDToGmlIdMapping();
	
	// NEW: Storage for Feature ID → gml:id → Color
	UPROPERTY()
	TMap<int32, FString> FeatureIDToGmlId; // FeatureID → gml:id
	
	UPROPERTY()
	UTexture2D* FeatureColorLookupTexture; // 1D texture: pixel position = Feature ID, color = building color
	
	// ✨ RENDER TARGET TEXTURE - Color lookup system (must be public for Blueprint access)
	UPROPERTY(BlueprintReadOnly, Category = "Building Energy Display")
	class UTextureRenderTarget2D* BuildingColorLookupTexture;
	
	UPROPERTY(BlueprintReadOnly, Category = "Building Energy Display")
	UMaterialInstanceDynamic* ColorLookupMaterial;
	
	// Maps building GML ID to texture coordinates (U,V) where its color is stored
	TMap<FString, FVector2D> BuildingUVLookup;
	
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Building Energy Display")
	int32 ColorTextureSize = 256; // 256x256 = 65,536 buildings max
	
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Building Energy Display")
	UMaterial* BaseCesiumMaterial; // Optional: Base material to override
	
	void GetBuildingAttributes(const FString& BuildingKey, const FString& CommunityId, const FString& Token);

	void UpdateBuildingAttributes(const FString& BuildingKey, const FString& CommunityId, const FString& AttributesJson, const FString& Token);
	
	UFUNCTION(BlueprintCallable, Category = "Real-Time Data")
	void FetchRealTimeEnergyData();
	
	UFUNCTION(BlueprintCallable, Category = "Real-Time Data")  
	void ForceRealTimeRefresh();
	
	// 🎨 COLOR APPLICATION FUNCTIONS
	UFUNCTION(BlueprintCallable, Category = "Building Energy Colors")
	void ApplyBuildingColorsImmediately();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Colors")
	void RefreshAllBuildingColors();
	
	// WebSocket Real-Time Energy Functions
	UFUNCTION(BlueprintCallable, Category = "WebSocket Energy")
	void ConnectEnergyWebSocket();
	
	UFUNCTION(BlueprintCallable, Category = "WebSocket Energy")
	void DisconnectEnergyWebSocket();
	
	void OnEnergyWebSocketConnected();
	void OnEnergyWebSocketConnectionError(const FString& Error);
	void OnEnergyWebSocketClosed(int32 StatusCode, const FString& Reason, bool bWasClean);
	void OnEnergyWebSocketMessage(const FString& Message);
	void ProcessEnergyWebSocketUpdate(const FString& JsonData);

// Debounce style re-application when many updates arrive quickly
UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="WebSocket Energy")
float StyleReapplyDebounceSeconds = 0.25f;

void ScheduleReapplyCesiumStyle();
void ReapplyCesiumStyleNow();

// Extract and cache per-building color from an energy_result payload.
bool UpdateBuildingCachesFromEnergyPayload(const FString& BuildingId, const TSharedPtr<FJsonObject>& PayloadObj);
FString ExtractEnergyDemandSpecificHex(const TSharedPtr<FJsonObject>& RootObj) const;

	
	// Coordinate-Based Building Validation Functions
	UFUNCTION(BlueprintCallable, Category = "Coordinate Validation")
	bool ValidateBuildingPosition(const FVector& ClickPosition, const FString& GmlId);
	
	FBuildingBoundingBox CreateBuildingBoundingBox(const FString& GmlId);
	bool IsPointInBoundingBox(const FVector& Point, const FBuildingBoundingBox& BoundingBox);
	bool IsPointInBuildingBounds(const FVector& Point, const FString& BuildingCoordinates);
	bool ParseBuildingCoordinates(const FString& CoordinatesString, TArray<FVector>& OutCoordinates);
	bool IsPointInPolygon(const FVector& Point, const TArray<FVector>& PolygonVertices);
	FString GetBuildingByCoordinates(const FVector& ClickPosition);
	void StoreBuildingCoordinates(const FString& GmlId, const FString& CoordinatesData);
	
	void TestBuildingAttributesAPI();

	UFUNCTION(BlueprintCallable, Category = "Building Attributes")
	void ShowBuildingAttributesForm(const FString& BuildingGmlId);
	
	// UMG Widget Functions for Building Information Display
	UFUNCTION(BlueprintCallable, Category = "Building Info Widget")
	void ShowBuildingInfoWidget(const FString& BuildingId, const FString& BuildingData);
	
	UFUNCTION(BlueprintCallable, Category = "Building Info Widget") 
	void HideBuildingInfoWidget();
	
	UFUNCTION(BlueprintCallable, Category = "Building Info Widget")
	void CreateBuildingInfoWidget();
	
	UFUNCTION(BlueprintCallable, Category = "Cache Statistics")
	void LogCacheStatistics();
	
	// GML ID Case Sensitivity Validation Functions
	UFUNCTION(BlueprintCallable, Category = "GML ID Validation")
	void ValidateGmlIdCaseSensitivity();
	
	UFUNCTION(BlueprintCallable, Category = "GML ID Validation")
	void CleanDuplicateColorCacheEntries();
	
	UFUNCTION(BlueprintCallable, Category = "GML ID Validation")
	void TestColorRetrieval(const FString& TestGmlId);
	
	bool IsGmlIdCaseSensitive(const FString& GmlId);

	void CreateBuildingAttributesForm(const FString& JsonData = TEXT(""));
	
	void PopulateBuildingAttributesWidget(const FString& JsonData);

	void GetBuildingAttributeOptions();
	
	UFUNCTION(BlueprintCallable, Category = "Building Attributes")
	void OnBuildingClicked(const FString& BuildingGmlId);
	
	// Enhanced Building Click Handler with Position Validation
	UFUNCTION(BlueprintCallable, Category = "Building Interaction")
	void OnBuildingClickedWithPosition(const FString& BuildingGmlId, const FVector& ClickPosition);
	
	UFUNCTION(BlueprintCallable, Category = "Building Attributes")
	void CloseAttributesForm();
	
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "UI")
	TSubclassOf<class UUserWidget> BuildingAttributesWidgetClass;
	
	UPROPERTY(BlueprintReadOnly, Category = "UI")
	class UUserWidget* BuildingAttributesWidget;
	
	void CreateMultipleMaterialsForCesium(AActor* CesiumActor);
	
	FString CreateCesiumColorExpression();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void SetupCesiumColorMaterial();
	
	UFUNCTION(BlueprintCallable, Category = "Building Energy Display")
	void ApplyTilesetColors();
	
	void CreateTestColors();
	
	// New Cesium styling functions
	void ApplyCesiumTilesetStyling(AActor* CesiumActor);
	void ApplyFallbackMaterialStyling(AActor* CesiumActor);
	bool ApplyCesiumStyleJsonToTileset(ACesium3DTileset* Tileset, const FString& StyleJson) const;
	TArray<FString> MakeIdVariants(const FString& InId) const;
	void ApplyColorLookupMaterialToTileset(ACesium3DTileset* Tileset) const;
	void ApplyColorsViaMetadataMaterial(ACesium3DTileset* Tileset);

private:
	FLinearColor ConvertHexToLinearColor(const FString& HexColor);

	FString ConvertGmlIdToBuildingKey(const FString& GmlId);
	
	FString ConvertActualGmlIdToModified(const FString& ActualGmlId);

	void OnResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);

	void OnPreloadResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);

	void OnAuthResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	
	void OnRefreshTokenResponseReceived(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	
	// REST API Energy Polling Functions
	void FetchUpdatedEnergyData(); // Fetch fresh energy data using REST API
	void OnEnergyUpdateResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);

	void ParseAndCacheAllBuildings(const FString& JsonResponse);
	
	void OnGetBuildingAttributesResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);

	void OnUpdateBuildingAttributesResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	
	void OnRealTimeEnergyDataResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);

	TMap<FString, FString> BuildingDataCache;
	
	TMap<FString, FLinearColor> BuildingColorCache;

	FTimerHandle StyleReapplyTimerHandle;


	// NEW: Store full JSON objects for color extraction
	TMap<FString, TSharedPtr<FJsonObject>> BuildingJsonCache;

	TMap<FString, FString> GmlIdCache;
	
	FString CurrentRequestedBuildingKey;
	FString CurrentRequestedCommunityId;

	bool bDataLoaded;

	bool bIsLoading;
	
	float RealTimeMonitoringTimer = 0.0f;
	float RealTimeUpdateInterval = 2.0f;
	
	bool bEnhancedPollingMode = true;
	float FastPollingInterval = 1.0f;
	float SlowPollingInterval = 5.0f;
	int32 NoChangesCount = 0;
	
	// WebSocket Real-Time Energy Variables
	TSharedPtr<IWebSocket> EnergyWebSocket;
	bool bEnergyWebSocketConnected = false;
	// WebSocket endpoint (e.g., ws://host:port/path). If empty, WebSocket push is disabled.
	UPROPERTY(EditAnywhere, Category="WebSocket Energy")
	FString EnergyWebSocketURL;
	float WebSocketReconnectTimer = 0.0f;
	float WebSocketReconnectInterval = 5.0f;
	bool bAutoReconnectWebSocket = true;
	bool bAuthenticationMessageShown = false; // Flag to prevent authentication spam
	int32 EnergyUpdateCounter = 0;
	
	// Token Management Variables
	FString RefreshToken; // Store refresh token for automatic token renewal
	
	// Coordinate-Based Building Validation Variables
	TMap<FString, TArray<FVector>> BuildingCoordinatesCache; // Cache of building coordinates for validation
	TMap<FString, FString> CoordinateToGmlIdMap; // Map coordinates to correct gml_id
	float CoordinateValidationTolerance = 10.0f; // Tolerance for coordinate matching in meters
	int32 SlowDownThreshold = 10;
	
	TMap<FString, FString> PreviousBuildingDataSnapshot;
	TMap<FString, FLinearColor> PreviousColorSnapshot;
	
	bool bRealTimeMonitoringEnabled = true;
	bool bIsPerformingRealTimeUpdate = false;
	
	UFUNCTION(BlueprintCallable, Category = "Real-Time")
	void StartRealTimeMonitoring();
	
	UFUNCTION(BlueprintCallable, Category = "Real-Time")
	void StopRealTimeMonitoring();
	
	UFUNCTION(BlueprintCallable, Category = "Real-Time")
	void SetRealTimeUpdateInterval(float Seconds);
	
	UFUNCTION(BlueprintCallable, Category = "Real-Time")
	void EnableEnhancedPolling(bool bEnable);
	
	void PerformRealTimeDataCheck();
	void OnRealTimeDataResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	void DetectAndApplyChanges(const FString& NewJsonData);
	void NotifyRealTimeChanges(const TArray<FString>& ChangedBuildings);
	void UpdatePollingStrategy(bool bChangesDetected);
	
	float CacheRefreshTimer = 0.0f;
	
	FString LastDisplayedGmlId;
	double LastDisplayTime;
	
	FString CurrentBuildingGmlId;
	
	bool bShowScreenMessages; // Control whether to show on-screen debug messages

private:
	// Internal: prevents spamming style application every tick.
	bool bCesiumStyleApplied = false;
	// Retry until tileset becomes available/loaded.
	int32 CesiumStyleRetryCount = 0;
	float CesiumStyleRetryAccumulator = 0.0f;

};
