// Fill out your copyright notice in the Description page of Project Settings. [COPYRIGHT NOTICE PLACEHOLDER]

using UnrealBuildTool; // Import Unreal Build Tool namespace for module building [UBT IMPORT]

public class Final_project : ModuleRules // Define build rules class inheriting from ModuleRules [BUILD RULES CLASS]
{
	public Final_project(ReadOnlyTargetRules Target) : base(Target) // Constructor taking target rules parameter [CONSTRUCTOR]
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs; // Set precompiled header usage mode for faster compilation [PCH CONFIGURATION]
	
		PublicDependencyModuleNames.AddRange(new string[] {  // Add public dependency modules that this module requires [PUBLIC DEPENDENCIES START]
			"Core",  // Core Unreal Engine functionality [CORE MODULE]
			"CoreUObject",  // Core object system and reflection [CORE OBJECT MODULE]
			"Engine",  // Main engine functionality [ENGINE MODULE]
			"InputCore", // Input system for handling user input [INPUT MODULE]
			"HTTP", // HTTP module for web requests [HTTP MODULE]
			"Json", // JSON parsing and serialization [JSON MODULE]
			"JsonUtilities", // JSON utility functions [JSON UTILITIES MODULE]
			"UMG", // Unreal Motion Graphics for UI [UMG MODULE]
			"Slate", // Low-level UI framework [SLATE MODULE]
			"SlateCore", // Core Slate UI functionality [SLATE CORE MODULE]
			"WebSockets", // WebSocket module for real-time energy updates [WEBSOCKET MODULE]
			"Networking", // Networking support for WebSocket connections [NETWORKING MODULE]
			"ApplicationCore", // Application core for input events [APP CORE MODULE]
            "CesiumRuntime"
        }); // End of public dependency modules array [PUBLIC DEPENDENCIES END]

		PrivateDependencyModuleNames.AddRange(new string[] {  // Add private dependency modules [PRIVATE DEPENDENCIES START]
			"ToolMenus" // Tool menus for editor integration [TOOL MENUS MODULE]
		}); // End of private dependency modules array [PRIVATE DEPENDENCIES END]
		
		// Note: Mixed Reality modules commented out for now - can be added back with proper setup
		// Mixed Reality / HoloLens support - include for Windows builds
		// if (Target.Platform == UnrealTargetPlatform.Win64)
		// {
		//	PublicDependencyModuleNames.AddRange(new string[] {
		//		"MixedReality", // Mixed Reality support for HoloLens
		//		"HeadMountedDisplay" // HMD support
		//	});
		//	
		//	// Conditionally add MRMesh if available
		//	if (Target.bBuildEditor || Target.Type == TargetType.Game)
		//	{
		//		PrivateDependencyModuleNames.Add("MRMesh");
		//	}
		//	
		//	PublicDefinitions.Add("WITH_HOLOLENS=1");
		// }

		// Uncomment if you are using Slate UI [SLATE UI COMMENT]
		// PrivateDependencyModuleNames.AddRange(new string[] { "Slate", "SlateCore" }); [SLATE UI DEPENDENCIES - COMMENTED]
		
		// Uncomment if you are using online features [ONLINE FEATURES COMMENT]
		// PrivateDependencyModuleNames.Add("OnlineSubsystem"); [ONLINE SUBSYSTEM DEPENDENCY - COMMENTED]

		// To include OnlineSubsystemSteam, add it to the plugins section in your uproject file with the Enabled attribute set to true [STEAM SUBSYSTEM COMMENT]
	} // End of constructor [CONSTRUCTOR END]
} // End of build rules class [BUILD RULES CLASS END]
