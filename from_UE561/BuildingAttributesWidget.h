// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "Blueprint/UserWidget.h"
#include "Components/ComboBoxString.h"
#include "Components/TextBlock.h"
#include "Components/Button.h"
#include "Components/EditableTextBox.h"
#include "Http.h"
#include "Json.h"
#include "BuildingAttributesWidget.generated.h"

UCLASS(Blueprintable)
class FINAL_PROJECT_API UBuildingAttributesWidget : public UUserWidget
{
	GENERATED_BODY()

protected:
	virtual void NativeConstruct() override;

public:
	UFUNCTION(BlueprintCallable, Category = "Building Attributes")
	void SetBuildingData(const FString& GmlId, const FString& Token);

	UFUNCTION(BlueprintCallable, Category = "Building Attributes")
	void CloseWidget();

	UFUNCTION(BlueprintCallable, Category = "Building Attributes")
	void SaveBuildingAttributes();

	// UI Components - General Information
	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_ConstructionYear;

	UPROPERTY(meta = (BindWidget))
	class UEditableTextBox* TB_NumberOfStorey;

	UPROPERTY(meta = (BindWidget))
	class UTextBlock* BuildingTitleText;

	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_RoofStorey;

	// UI Components - Before Renovation
	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_HeatingSystemBefore;

	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_WindowBefore;

	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_WallBefore;

	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_RoofBefore;

	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_CeilingBefore;

	// UI Components - After Renovation
	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_HeatingSystemAfter;

	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_WindowAfter;

	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_WallAfter;

	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_RoofAfter;

	UPROPERTY(meta = (BindWidget))
	class UComboBoxString* CB_CeilingAfter;

	// UI Components - Buttons
	UPROPERTY(meta = (BindWidget))
	class UButton* BTN_Save;

	UPROPERTY(meta = (BindWidget))
	class UButton* BTN_Close;

	// Real-time form synchronization
	UFUNCTION(BlueprintCallable, Category = "Real-Time")
	void NativeTick(const FGeometry& MyGeometry, float InDeltaTime) override;

	UFUNCTION(BlueprintCallable, Category = "Real-Time")
	void StartFormRealTimeSync();

	UFUNCTION(BlueprintCallable, Category = "Real-Time")
	void StopFormRealTimeSync();

	UFUNCTION(BlueprintCallable, Category = "Real-Time")
	void SetFormUpdateInterval(float Seconds);

	UFUNCTION(BlueprintCallable, Category = "Real-Time")
	void EnableFormRealTime(bool bEnable);

	UPROPERTY(BlueprintReadOnly, Category = "Building Data")
	FString CurrentBuildingGmlId;

	UPROPERTY(BlueprintReadOnly, Category = "Building Data")
	FString CurrentBuildingKey;

	UPROPERTY(BlueprintReadOnly, Category = "Building Data")
	FString AccessToken;

	UPROPERTY(BlueprintReadOnly, Category = "Building Data")
	FString CommunityId;

private:
	void PopulateDropdownOptions();
	void ConfigureDropdownStyling(); // Configure dropdown controls with white backgrounds
	
	void LoadBuildingAttributes();

	void SaveBuildingAttributesToAPI();

	UFUNCTION()
	void OnSaveButtonClicked();

	UFUNCTION()
	void OnCloseButtonClicked();

	void OnGetAttributesResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	void OnPutAttributesResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	FString CreateAttributesJsonFromForm();
	void PopulateFormFromJson(TSharedPtr<FJsonObject> JsonObject);

	// Form synchronization
	float FormRealTimeTimer = 0.0f;
	float FormUpdateInterval = 2.0f;
	bool bFormRealTimeSyncEnabled = false;
	bool bIsPerformingFormUpdate = false;
	
	void PerformFormRealTimeCheck();
	void OnFormRealTimeResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	void DetectAndApplyFormChanges(TSharedPtr<FJsonObject> NewJsonData);

	// Missing choice maps for dropdown options
	TMap<FString, FString> ConstructionYearChoiceMap;
	TMap<FString, FString> RoofStoreyChoiceMap;
	TMap<FString, FString> HeatingSystemChoiceMap;

	// Missing real-time form monitoring variables
	bool bFormRealTimeEnabled = false;
	bool bIsFormDataChecking = false;
	FString PreviousFormDataSnapshot;

	// Missing form real-time functions
	void PerformFormDataCheck();
	void OnFormRealTimeDataResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
	void DetectAndApplyFormChanges(const FString& NewFormData);
	void UpdateFormFieldsAutomatically(TSharedPtr<FJsonObject> NewData);
	void NotifyFormRealTimeChanges();
};