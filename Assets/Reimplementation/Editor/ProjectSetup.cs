using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.EditorTools
{
    /// <summary>
    /// Creates the Arena scene, the unlit sprite material and player settings; builds macOS.
    /// Batch: unity run . -- -executeMethod Metadream.Shoot.EditorTools.ProjectSetup.Setup
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Arena.unity";
        public const string MaterialPath = "Assets/Reimplementation/Art/SpriteUnlit.mat";
        public const string DefaultBuildPath = "Builds/macOS/MetadreamShoot.app";
        public const string DefaultWindowsPath = "Builds/Windows/MetadreamShoot/MetadreamShoot.exe";
        public const string DefaultLinuxServerPath = "Builds/Server/Linux/MetadreamShootServer/MetadreamShootServer.x86_64";
        public const string DefaultWindowsServerPath = "Builds/Server/Windows/MetadreamShootServer/MetadreamShootServer.exe";
        /// <summary>Launcher templates (config, start scripts, docs) copied next to every server build.</summary>
        public const string LauncherDir = "ServerLauncher";

        [MenuItem("Metadream/Rebuild Arena Scene")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Reimplementation/Art");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) throw new InvalidOperationException("URP 2D unlit sprite shader not found");
                mat = new Material(shader) { name = "SpriteUnlit" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = CameraRig.OrthographicSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Background;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CameraRig>();

            var boot = new GameObject("GameBootstrap");
            var component = boot.AddComponent<GameBootstrap>();
            var so = new SerializedObject(component);
            so.FindProperty("spriteMaterial").objectReferenceValue = mat;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "Metadream Shoot";
            PlayerSettings.companyName = "Metadream";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.metadream.shoot");
            PlayerSettings.runInBackground = true;
            // Replays are uploaded to the main hosting server over plain HTTP inside the tailnet.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Arena scene and settings written");
        }

        [MenuItem("Metadream/Build macOS")]
        public static void BuildMac()
        {
            Build(BuildTarget.StandaloneOSX, ArgValue("-buildOutput") ?? DefaultBuildPath);
        }

        /// <summary>Windows x64, Mono backend (IL2CPP for Windows cannot be cross-compiled on macOS).</summary>
        [MenuItem("Metadream/Build Windows")]
        public static void BuildWindows()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            Build(BuildTarget.StandaloneWindows64, ArgValue("-buildOutput") ?? DefaultWindowsPath);
        }

        private static void Build(BuildTarget target, string output)
        {
            Build(target, output, StandaloneBuildSubtarget.Player);
        }

        /// <summary>
        /// Headless Linux x64 dedicated server. IL2CPP by default (ahead-of-time native code, no JIT; needs the
        /// Linux IL2CPP module and com.unity.toolchain.macos-arm64-linux on a Mac). Pass -mono for a Mono build.
        /// </summary>
        [MenuItem("Metadream/Build Dedicated Server (Linux)")]
        public static void BuildServerLinux()
        {
            bool mono = Array.IndexOf(Environment.GetCommandLineArgs(), "-mono") >= 0;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, mono ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);
            string output = ArgValue("-buildOutput") ?? DefaultLinuxServerPath;
            if (Build(BuildTarget.StandaloneLinux64, output, StandaloneBuildSubtarget.Server)) CopyLauncher("linux", output);
        }

        /// <summary>Headless Windows x64 dedicated server, console subsystem (Mono).</summary>
        [MenuItem("Metadream/Build Dedicated Server (Windows)")]
        public static void BuildServerWindows()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.Mono2x);
            string output = ArgValue("-buildOutput") ?? DefaultWindowsServerPath;
            if (Build(BuildTarget.StandaloneWindows64, output, StandaloneBuildSubtarget.Server)) CopyLauncher("windows", output);
        }

        private static void CopyLauncher(string platform, string output)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(output));
            foreach (var sub in new[] { "common", platform })
            {
                string src = Path.Combine(LauncherDir, sub);
                if (!Directory.Exists(src)) continue;
                foreach (var file in Directory.GetFiles(src))
                    File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), true);
            }
            Debug.Log("[Build] launcher files copied to " + dir);
        }

        private static bool Build(BuildTarget target, string output, StandaloneBuildSubtarget subtarget)
        {
            if (!File.Exists(ScenePath)) Setup();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                subtarget = (int)subtarget,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded) CopyLicenses(target, output);
            Debug.Log("[Build] target=" + target + " subtarget=" + subtarget + " result=" + summary.result + " size=" + summary.totalSize + " errors=" + summary.totalErrors + " path=" + output);
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            return summary.result == BuildResult.Succeeded;
        }

        /// <summary>The bundled Noto Sans KR font is SIL OFL 1.1: ship its license with every build.</summary>
        private static void CopyLicenses(BuildTarget target, string output)
        {
            const string license = "Assets/Reimplementation/Licenses/NotoSansKR-OFL.txt";
            if (!File.Exists(license)) return;
            string dir = target == BuildTarget.StandaloneOSX
                ? Path.Combine(output, "Contents", "Resources")
                : Path.GetDirectoryName(Path.GetFullPath(output));
            Directory.CreateDirectory(dir);
            File.Copy(license, Path.Combine(dir, "NotoSansKR-OFL.txt"), true);
        }

        private static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}

