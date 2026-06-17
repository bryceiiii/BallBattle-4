using UnityEngine;

/// <summary>
/// 手机端构建配置助手。在 Editor 中一键设置 Android/iOS 打包参数。
/// 提供 Editor 菜单命令：Tools → Mobile Build → ...
/// 不参与运行时逻辑。
/// </summary>
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class MobileBuildConfig
{
    public const string CompanyName = "BallBattle";
    public const string ProductName = "BallBattle-4";
    public const string BundleId = "com.ballbattle.ballbattle4";
    public const string Version = "0.1.0";

#if UNITY_EDITOR
    [MenuItem("Tools/Mobile Build/📱 配置 Android 打包参数", false, 100)]
    public static void ConfigureAndroid()
    {
        PlayerSettings.companyName = CompanyName;
        PlayerSettings.productName = ProductName;

        // Android 专属
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26; // Android 8.0+
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34; // Android 14
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.stripEngineCode = true;                    // 减小包体（全局）
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.High);

        // 图形设置 → OpenGL ES 3.0+（主流手机支持）
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] {
            UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,
            UnityEngine.Rendering.GraphicsDeviceType.Vulkan
        });

        // Quality → 移动端默认中等画质
        QualitySettings.SetQualityLevel(1); // Medium (0=Low, 1=Med, 2=High)
        QualitySettings.vSyncCount = 0;     // 手机无需垂直同步
        Application.targetFrameRate = 60;

        // 应用图标（默认使用 Assets/Textures/app_icon.png，如不存在则跳过）
        var iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/app_icon.png");
        if (iconTex != null)
        {
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, new[] { iconTex });
            Debug.Log("   - 应用图标: 已从 Assets/Textures/app_icon.png 加载");
        }
        else
        {
            Debug.LogWarning("   - 应用图标: 未找到 app_icon.png，APK 将使用默认图标");
        }

        // 屏幕方向 → 横屏
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;

        Debug.Log("✅ Android 打包参数已配置完成！");
        Debug.Log($"   - 包名: {BundleId}");
        Debug.Log($"   - 最低SDK: Android 8.0 (API 26)");
        Debug.Log($"   - 目标SDK: Android 14 (API 34)");
        Debug.Log($"   - 架构: ARM64");
        Debug.Log($"   - 目标帧率: 60 FPS");
        Debug.Log($"   - 屏幕方向: 横屏 (Landscape)");
    }

    [MenuItem("Tools/Mobile Build/📱 配置 iOS 打包参数", false, 101)]
    public static void ConfigureIOS()
    {
        PlayerSettings.companyName = CompanyName;
        PlayerSettings.productName = ProductName;

        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleId);
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.iOS.scriptCallOptimization = ScriptCallOptimizationLevel.SlowAndSafe;

        // Metal 图形 API
        PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] {
            UnityEngine.Rendering.GraphicsDeviceType.Metal
        });

        QualitySettings.SetQualityLevel(1);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        // 应用图标
        var iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/app_icon.png");
        if (iconTex != null)
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.iOS, new[] { iconTex });

        // 屏幕方向 → 横屏
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;

        Debug.Log("✅ iOS 打包参数已配置完成！");
    }

    [MenuItem("Tools/Mobile Build/🔨 快速构建 Android APK", false, 200)]
    public static void BuildAndroid()
    {
        ConfigureAndroid();

        string buildPath = EditorUtility.SaveFilePanel("保存 APK", "", ProductName + ".apk", "apk");
        if (string.IsNullOrEmpty(buildPath)) return;

        var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.path))
        {
            Debug.LogError("未找到激活的场景，请先保存场景！");
            return;
        }

        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = new[] { activeScene.path },
            locationPathName = buildPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        BuildPipeline.BuildPlayer(buildPlayerOptions);

        Debug.Log($"✅ APK 已构建到: {buildPath}");
    }

    [MenuItem("Tools/Mobile Build/🧹 清理构建缓存", false, 300)]
    public static void CleanBuildCache()
    {
        if (EditorUtility.DisplayDialog("清理构建缓存",
            "将删除 Library/BuildCache 和 Temp 目录，下次构建需重新编译。确定吗？",
            "确定", "取消"))
        {
            var libPath = Application.dataPath.Replace("/Assets", "") + "/Library/BuildCache";
            if (System.IO.Directory.Exists(libPath))
                System.IO.Directory.Delete(libPath, true);

            var tempPath = Application.dataPath.Replace("/Assets", "") + "/Temp";
            if (System.IO.Directory.Exists(tempPath))
                System.IO.Directory.Delete(tempPath, true);

            Debug.Log("✅ 构建缓存已清理");
        }
    }
#endif
}
