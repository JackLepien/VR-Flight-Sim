using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;

// Builds the EnvTerrain scene to a standalone Meta Quest APK.
// Use the menu: Build > Build Quest APK (EnvTerrain)
public static class QuestBuild
{
    const string ScenePath = "Assets/Scenes/EnvTerrain.unity";
    const string OutputPath = "Builds/FlightSim.apk";

    [MenuItem("Build/Configure for Quest")]
    public static void Configure()
    {
        // Platform / player settings
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.jack.flightsim");
        PlayerSettings.productName = "VR Flight Sim";
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);

        // OpenXR features required for Quest
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (settings == null)
        {
            Debug.LogError("OpenXR settings for Android not found. Enable OpenXR under Project Settings > XR Plug-in Management > Android tab.");
            return;
        }
        foreach (var feature in settings.GetFeatures())
        {
            if (feature == null) continue;
            string typeName = feature.GetType().Name;
            if (typeName == "MetaQuestFeature" || typeName == "OculusTouchControllerProfile")
            {
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }
        }
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("Quest configuration applied.");
    }

    [MenuItem("Build/Build Quest APK (EnvTerrain)")]
    public static void Build()
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogError("Scene not found: " + ScenePath);
            return;
        }

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        Configure();

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("Build " + report.summary.result + ": " + Path.GetFullPath(OutputPath));
    }
}
