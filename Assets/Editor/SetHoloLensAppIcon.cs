using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

/// <summary>
/// Automatically configures the UWP/HoloLens 2 app icon using Assets/app_icon_square.png.
///
/// TWO-PRONGED APPROACH:
/// 1. Pre-build: Sets PlayerSettings.WSA visual assets so Unity writes the correct references
///    into Package.appxmanifest.
/// 2. Post-build: Copies the pre-generated correctly-sized PNGs from Assets/UWPIcons/ into
///    the UWP build's Assets/ folder, overwriting Unity's solid-blue placeholders.
///
/// The pre-generated icons live in Assets/UWPIcons/ and are created by a Python script
/// that pads app_logo.png to square and resizes to every required UWP scale factor.
///
/// HoloLens 2 primarily uses Square44x44Logo (app list) and Square150x150Logo (Start menu tile).
/// 
/// Also configurable via menu: Tools > Set HoloLens App Icon.
/// </summary>
public class SetHoloLensAppIcon : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    private const string SquareLogoPath = "Assets/app_icon_square.png";
    private const string FallbackLogoPath = "Assets/app_logo.png";
    private const string UWPIconsFolder = "Assets/UWPIcons";

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.WSAPlayer)
        {
            ApplyIcon();
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.WSAPlayer)
        {
            CopyIconsToBuild(report.summary.outputPath);
        }
    }

    [MenuItem("Tools/Set HoloLens App Icon")]
    public static void ApplyIcon()
    {
        // Prefer square version, fall back to original
        string logoPath = File.Exists(SquareLogoPath) ? SquareLogoPath : FallbackLogoPath;
        
        Texture2D logo = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath);
        if (logo == null)
        {
            Debug.LogError($"SetHoloLensAppIcon: Could not load {logoPath}. Make sure the file exists.");
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
            if (importer.maxTextureSize < 1024)
            {
                importer.maxTextureSize = 2048;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                logo = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath);
            }
        }

        // Set the default app icon (all platforms fallback)
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new Texture2D[] { logo });

        // Set all UWP/HoloLens visual asset images
        SetAllScales(logoPath, PlayerSettings.WSAImageType.UWPSquare44x44Logo);
        SetAllScales(logoPath, PlayerSettings.WSAImageType.UWPSquare71x71Logo);
        SetAllScales(logoPath, PlayerSettings.WSAImageType.UWPSquare150x150Logo);
        SetAllScales(logoPath, PlayerSettings.WSAImageType.UWPSquare310x310Logo);
        SetAllScales(logoPath, PlayerSettings.WSAImageType.UWPWide310x150Logo);
        SetAllScales(logoPath, PlayerSettings.WSAImageType.SplashScreenImage);
        SetAllScales(logoPath, PlayerSettings.WSAImageType.StoreTileLogo);

        AssetDatabase.SaveAssets();
        Debug.Log($"<color=green>✅ HoloLens app icon set from {logoPath} for all UWP tile sizes.</color>");
    }

    static void SetAllScales(string path, PlayerSettings.WSAImageType type)
    {
        PlayerSettings.WSA.SetVisualAssetsImage(path, type, PlayerSettings.WSAImageScale._100);
        PlayerSettings.WSA.SetVisualAssetsImage(path, type, PlayerSettings.WSAImageScale._125);
        PlayerSettings.WSA.SetVisualAssetsImage(path, type, PlayerSettings.WSAImageScale._150);
        PlayerSettings.WSA.SetVisualAssetsImage(path, type, PlayerSettings.WSAImageScale._200);
        PlayerSettings.WSA.SetVisualAssetsImage(path, type, PlayerSettings.WSAImageScale._400);
    }

    /// <summary>
    /// Post-build: copy pre-generated correctly-sized PNGs from Assets/UWPIcons/
    /// into the UWP build output, overwriting Unity's solid-blue placeholders.
    /// </summary>
    static void CopyIconsToBuild(string buildPath)
    {
        if (!Directory.Exists(UWPIconsFolder))
        {
            Debug.LogWarning($"SetHoloLensAppIcon: {UWPIconsFolder} not found — skipping icon copy.");
            return;
        }

        // Find the Assets/ folder inside the UWP build
        string targetAssetsDir = null;
        
        // Check direct child (buildPath/ProjectName/Assets/)
        if (Directory.Exists(buildPath))
        {
            foreach (string subDir in Directory.GetDirectories(buildPath))
            {
                string subAssets = Path.Combine(subDir, "Assets");
                if (Directory.Exists(subAssets))
                {
                    targetAssetsDir = subAssets;
                    break;
                }
            }
            // Fallback: buildPath/Assets/
            if (targetAssetsDir == null)
            {
                string directAssets = Path.Combine(buildPath, "Assets");
                if (Directory.Exists(directAssets))
                    targetAssetsDir = directAssets;
            }
        }

        if (targetAssetsDir == null)
        {
            Debug.LogWarning("SetHoloLensAppIcon: Could not find Assets/ in UWP build output.");
            return;
        }

        int copied = 0;
        foreach (string srcFile in Directory.GetFiles(UWPIconsFolder, "*.png"))
        {
            string filename = Path.GetFileName(srcFile);
            string destFile = Path.Combine(targetAssetsDir, filename);
            File.Copy(srcFile, destFile, overwrite: true);
            copied++;
        }

        Debug.Log($"<color=green>✅ Copied {copied} icon PNGs into {targetAssetsDir}</color>");
    }
}
