using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Automatically sets Assets/app_logo.png as the UWP/HoloLens 2 app icon
/// on every build. Also configurable via menu: Tools > Set HoloLens App Icon.
/// </summary>
public class SetHoloLensAppIcon : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    private const string LogoPath = "Assets/app_logo.png";

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.WSAPlayer)
        {
            ApplyIcon();
        }
    }

    [MenuItem("Tools/Set HoloLens App Icon")]
    public static void ApplyIcon()
    {
        Texture2D logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
        if (logo == null)
        {
            Debug.LogError($"SetHoloLensAppIcon: Could not load {LogoPath}. Make sure the file exists.");
            return;
        }

        // Ensure the texture is readable and importable for icons
        string assetPath = AssetDatabase.GetAssetPath(logo);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            bool changed = false;

            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }
            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                changed = true;
            }
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                changed = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                // Reload after reimport
                logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
            }
        }

        // Set the default app icon (all platforms fallback)
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new Texture2D[] { logo });

        // Set all UWP/HoloLens visual asset images
        // Square 44x44 - small tile & taskbar
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare44x44Logo, PlayerSettings.WSAImageScale._100);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare44x44Logo, PlayerSettings.WSAImageScale._125);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare44x44Logo, PlayerSettings.WSAImageScale._150);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare44x44Logo, PlayerSettings.WSAImageScale._200);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare44x44Logo, PlayerSettings.WSAImageScale._400);

        // Square 71x71 - small tile
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare71x71Logo, PlayerSettings.WSAImageScale._100);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare71x71Logo, PlayerSettings.WSAImageScale._125);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare71x71Logo, PlayerSettings.WSAImageScale._150);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare71x71Logo, PlayerSettings.WSAImageScale._200);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare71x71Logo, PlayerSettings.WSAImageScale._400);

        // Square 150x150 - medium tile (main app tile on HoloLens)
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare150x150Logo, PlayerSettings.WSAImageScale._100);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare150x150Logo, PlayerSettings.WSAImageScale._125);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare150x150Logo, PlayerSettings.WSAImageScale._150);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare150x150Logo, PlayerSettings.WSAImageScale._200);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare150x150Logo, PlayerSettings.WSAImageScale._400);

        // Square 310x310 - large tile
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare310x310Logo, PlayerSettings.WSAImageScale._100);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare310x310Logo, PlayerSettings.WSAImageScale._125);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare310x310Logo, PlayerSettings.WSAImageScale._150);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare310x310Logo, PlayerSettings.WSAImageScale._200);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPSquare310x310Logo, PlayerSettings.WSAImageScale._400);

        // Wide 310x150 - wide tile
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPWide310x150Logo, PlayerSettings.WSAImageScale._100);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPWide310x150Logo, PlayerSettings.WSAImageScale._125);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPWide310x150Logo, PlayerSettings.WSAImageScale._150);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPWide310x150Logo, PlayerSettings.WSAImageScale._200);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPWide310x150Logo, PlayerSettings.WSAImageScale._400);

        // Store logo
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPStoreLogo, PlayerSettings.WSAImageScale._100);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPStoreLogo, PlayerSettings.WSAImageScale._125);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPStoreLogo, PlayerSettings.WSAImageScale._150);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPStoreLogo, PlayerSettings.WSAImageScale._200);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.UWPStoreLogo, PlayerSettings.WSAImageScale._400);

        // Splash screen
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.SplashScreenImage, PlayerSettings.WSAImageScale._100);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.SplashScreenImage, PlayerSettings.WSAImageScale._125);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.SplashScreenImage, PlayerSettings.WSAImageScale._150);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.SplashScreenImage, PlayerSettings.WSAImageScale._200);
        PlayerSettings.WSA.SetVisualAssetsImage(LogoPath, PlayerSettings.WSAImageType.SplashScreenImage, PlayerSettings.WSAImageScale._400);

        AssetDatabase.SaveAssets();
        Debug.Log($"<color=green>✅ HoloLens app icon set to {LogoPath} for all UWP tile sizes and splash screen.</color>");
    }
}
