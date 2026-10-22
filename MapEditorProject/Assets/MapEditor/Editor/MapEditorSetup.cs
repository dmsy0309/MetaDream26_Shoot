using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metadream.MapEditor.EditorTools
{
    /// <summary>
    /// Generates the editor scene and player settings and builds the macOS / Windows apps. Scenes are generated
    /// from code (same rule as the game project).
    ///   unity run . -- -executeMethod Metadream.MapEditor.EditorTools.MapEditorSetup.Setup
    ///   unity build . --target StandaloneOSX --execute-method Metadream.MapEditor.EditorTools.MapEditorSetup.BuildMac -o Builds/macOS/MetadreamShootMapMaker.app
    ///   unity build . --target StandaloneWindows64 --execute-method Metadream.MapEditor.EditorTools.MapEditorSetup.BuildWindows -o Builds/Windows/MetadreamShootMapMaker/MetadreamShootMapMaker.exe
    /// </summary>
    public static class MapEditorSetup
    {
        public const string ScenePath = "Assets/Scenes/MapEditor.unity";
        public const string MaterialPath = "Assets/Reimplementation/Art/SpriteUnlit.mat";
        public const string DefaultMacPath = "Builds/macOS/MetadreamShootMapMaker.app";
        public const string DefaultWindowsPath = "Builds/Windows/MetadreamShootMapMaker/MetadreamShootMapMaker.exe";
        public const string Version = "1.0.0";

        [MenuItem("Metadream MapMaker/Rebuild Editor Scene")]
        public static void Setup()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null) throw new InvalidOperationException("game sprite material missing: " + MaterialPath + " (run GameSync.SyncFromGame)");
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 22f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.033f, 0.045f);
            camGo.AddComponent<AudioListener>();

            var app = new GameObject("MapEditor");
            var component = app.AddComponent<MapEditorApp>();
            var so = new SerializedObject(component);
            so.FindProperty("spriteMaterial").objectReferenceValue = mat;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "Metadream Shoot Map Maker";
            PlayerSettings.companyName = "Metadream";
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.metadream.shoot.mapmaker");
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.usePlayerLog = true;
            if (!File.Exists(GameSync.InfoPath)) GameSync.WriteInfo(GameSync.ResolveGameProject());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Setup] map editor scene and settings written");
        }

        [MenuItem("Metadream MapMaker/Build macOS")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, ArgValue("-buildOutput") ?? DefaultMacPath);

        /// <summary>Windows x64, Mono backend (IL2CPP for Windows cannot be cross-compiled on macOS) — same as the game.</summary>
        [MenuItem("Metadream MapMaker/Build Windows")]
        public static void BuildWindows()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            Build(BuildTarget.StandaloneWindows64, ArgValue("-buildOutput") ?? DefaultWindowsPath);
        }

        private static void Build(BuildTarget target, string output)
        {
            Setup();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None
            });
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded) CopyExtras(target, output);
            Debug.Log("[Build] target=" + target + " result=" + summary.result + " size=" + summary.totalSize + " errors=" + summary.totalErrors + " warnings=" + summary.totalWarnings + " path=" + output);
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>Font license (SIL OFL 1.1) inside the build and the user guide next to it.</summary>
        private static void CopyExtras(BuildTarget target, string output)
        {
            string full = Path.GetFullPath(output);
            string beside = Path.GetDirectoryName(full);
            string licenseDir = target == BuildTarget.StandaloneOSX ? Path.Combine(full, "Contents", "Resources") : beside;
            const string license = "Assets/Reimplementation/Licenses/NotoSansKR-OFL.txt";
            if (File.Exists(license)) { Directory.CreateDirectory(licenseDir); File.Copy(license, Path.Combine(licenseDir, "NotoSansKR-OFL.txt"), true); }
            const string guide = "Docs/MAP_EDITOR_GUIDE.md";
            if (File.Exists(guide) && beside != null) File.Copy(guide, Path.Combine(beside, "MAP_EDITOR_GUIDE.md"), true);
        }

        private static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
