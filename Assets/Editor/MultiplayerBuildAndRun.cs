using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MultiplayerBuildAndRun
{
    private const string MenuRoot = "Tools/Run Multiplayer";

    private static int pendingPlayerCount;
    private static BuildTarget pendingBuildTarget;
    private static bool hasPendingBuild;

    [MenuItem(MenuRoot + "/Win64/1 Player")]
    private static void PerformWin64Build1()
    {
        QueueBuild(1, BuildTarget.StandaloneWindows64);
    }

    [MenuItem(MenuRoot + "/Win64/2 Players")]
    private static void PerformWin64Build2()
    {
        QueueBuild(2, BuildTarget.StandaloneWindows64);
    }

    [MenuItem(MenuRoot + "/Win64/3 Players")]
    private static void PerformWin64Build3()
    {
        QueueBuild(3, BuildTarget.StandaloneWindows64);
    }

    [MenuItem(MenuRoot + "/Win64/4 Players")]
    private static void PerformWin64Build4()
    {
        QueueBuild(4, BuildTarget.StandaloneWindows64);
    }

    [MenuItem(MenuRoot + "/Mac/1 Player")]
    private static void PerformMacBuild1()
    {
        QueueBuild(1, BuildTarget.StandaloneOSX);
    }

    [MenuItem(MenuRoot + "/Mac/2 Players")]
    private static void PerformMacBuild2()
    {
        QueueBuild(2, BuildTarget.StandaloneOSX);
    }

    [MenuItem(MenuRoot + "/Mac/3 Players")]
    private static void PerformMacBuild3()
    {
        QueueBuild(3, BuildTarget.StandaloneOSX);
    }

    [MenuItem(MenuRoot + "/Mac/4 Players")]
    private static void PerformMacBuild4()
    {
        QueueBuild(4, BuildTarget.StandaloneOSX);
    }

    private static void QueueBuild(int playerCount, BuildTarget buildTarget)
    {
        pendingPlayerCount = playerCount;
        pendingBuildTarget = buildTarget;
        hasPendingBuild = true;

        EditorApplication.delayCall -= RunPendingBuildWhenReady;
        EditorApplication.delayCall += RunPendingBuildWhenReady;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            Debug.Log("Scripts are compiling. Multiplayer build will start when compilation finishes.");
        }
    }

    private static void RunPendingBuildWhenReady()
    {
        if (!hasPendingBuild)
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunPendingBuildWhenReady;
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Exit Play Mode before building multiplayer players.");
            hasPendingBuild = false;
            return;
        }

        hasPendingBuild = false;
        PerformBuildAndRun(pendingPlayerCount, pendingBuildTarget);
    }

    private static void PerformBuildAndRun(int playerCount, BuildTarget buildTarget)
    {
        string[] scenes = GetEnabledScenePaths();
        if (scenes.Length == 0)
        {
            Debug.LogError("No enabled scenes found in Build Settings.");
            return;
        }

        BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
        if (EditorUserBuildSettings.activeBuildTarget != buildTarget)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueBuild(playerCount, buildTarget);
                return;
            }
        }

        for (int i = 1; i <= playerCount; i++)
        {
            string locationPathName = GetBuildPath(buildTarget, i);
            Directory.CreateDirectory(Path.GetDirectoryName(locationPathName));

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPathName,
                target = buildTarget,
                options = BuildOptions.None
            };

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Player {i} build failed: {report.summary.result}");
                return;
            }

            LaunchPlayer(locationPathName, buildTarget, i);
        }
    }

    private static void LaunchPlayer(string locationPathName, BuildTarget buildTarget, int playerNumber)
    {
        string arguments = "-screen-fullscreen 0 -screen-width 1280 -screen-height 720";

        if (buildTarget == BuildTarget.StandaloneWindows64)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.GetFullPath(locationPathName),
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(locationPathName)),
                UseShellExecute = false
            });

            return;
        }

        if (buildTarget == BuildTarget.StandaloneOSX)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"-n \"{Path.GetFullPath(locationPathName)}\" --args {arguments}",
                UseShellExecute = false
            });

            return;
        }

        Debug.LogWarning($"Auto launch is not configured for {buildTarget}. Built player {playerNumber} at {locationPathName}.");
    }

    private static string GetBuildPath(BuildTarget buildTarget, int playerNumber)
    {
        string projectName = GetProjectName();
        string playerName = $"{projectName}{playerNumber}";

        switch (buildTarget)
        {
            case BuildTarget.StandaloneWindows64:
                return Path.Combine("Builds", "Win64", playerName, playerName + ".exe");
            case BuildTarget.StandaloneOSX:
                return Path.Combine("Builds", "Mac", playerName, playerName + ".app");
            default:
                return Path.Combine("Builds", buildTarget.ToString(), playerName, playerName);
        }
    }

    private static string GetProjectName()
    {
        DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
        return projectDirectory != null ? projectDirectory.Name : Application.productName;
    }

    private static string[] GetEnabledScenePaths()
    {
        return EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
    }
}
