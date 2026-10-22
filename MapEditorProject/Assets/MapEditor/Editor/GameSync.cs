using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Metadream.MapEditor.EditorTools
{
    /// <summary>
    /// Keeps the map editor's copy of the game runtime byte-identical to the game project. The game project is
    /// only read. Runtime = Assets/Reimplementation without Editor/ and Tests/; physics settings = the
    /// ProjectSettings files that drive Physics2D, the fixed time step and layers.
    /// Batch:
    ///   unity run . -- -executeMethod Metadream.MapEditor.EditorTools.GameSync.SyncFromGame -gameProject &lt;path&gt;
    ///   unity run . -- -executeMethod Metadream.MapEditor.EditorTools.GameSync.Verify [-gameProject &lt;path&gt;]
    /// </summary>
    public static class GameSync
    {
        public const string RuntimeFolder = "Assets/Reimplementation";
        public const string InfoPath = "Assets/MapEditor/Resources/MapEditor/game-runtime.json";
        public static readonly string[] ExcludedTopLevel = { "Editor", "Editor.meta", "Tests", "Tests.meta" };
        public static readonly string[] PhysicsSettings =
        {
            "ProjectSettings/Physics2DSettings.asset",
            "ProjectSettings/PhysicsCoreProjectSettings2D.asset",
            "ProjectSettings/TimeManager.asset",
            "ProjectSettings/DynamicsManager.asset",
            "ProjectSettings/TagManager.asset"
        };
        /// <summary>Copied on sync so rendering and input match too (not part of the physics hash).</summary>
        public static readonly string[] OtherSettings =
        {
            "ProjectSettings/GraphicsSettings.asset",
            "ProjectSettings/QualitySettings.asset",
            "ProjectSettings/URPProjectSettings.asset",
            "ProjectSettings/InputManager.asset"
        };

        private static string EditorRoot => Path.GetDirectoryName(Application.dataPath);

        public static string ResolveGameProject()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-gameProject");
            if (i >= 0 && i + 1 < args.Length) return Path.GetFullPath(args[i + 1]);
            string env = Environment.GetEnvironmentVariable("METADREAM_GAME_PROJECT");
            if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env);
            string fromInfo = File.Exists(InfoPath) ? JsonUtility.FromJson<GameRuntimeInfo>(File.ReadAllText(InfoPath))?.sourceProject : null;
            return string.IsNullOrEmpty(fromInfo) ? null : fromInfo;
        }

        /// <summary>Relative path ('/' separators) → absolute path of every runtime file in a project.</summary>
        public static SortedDictionary<string, string> RuntimeFiles(string projectRoot)
        {
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            string root = Path.Combine(projectRoot, RuntimeFolder);
            if (!Directory.Exists(root)) return files;
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(root.Length + 1).Replace('\\', '/');
                string top = rel.Split('/')[0];
                if (Array.IndexOf(ExcludedTopLevel, top) >= 0) continue;
                if (Path.GetFileName(rel) == ".DS_Store") continue;
                files[RuntimeFolder + "/" + rel] = file;
            }
            string meta = Path.Combine(projectRoot, RuntimeFolder + ".meta");
            if (File.Exists(meta)) files[RuntimeFolder + ".meta"] = meta;
            return files;
        }

        public static string Sha(string path)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(path))
                return Hex(sha.ComputeHash(s));
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>SHA-256 over "relative path TAB file SHA-256" lines (sorted).</summary>
        public static string TreeHash(IEnumerable<KeyValuePair<string, string>> files)
        {
            var sb = new StringBuilder();
            foreach (var kv in files) sb.Append(kv.Key).Append('\t').Append(Sha(kv.Value)).Append('\n');
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public static SortedDictionary<string, string> PhysicsFiles(string projectRoot)
        {
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var rel in PhysicsSettings)
            {
                string p = Path.Combine(projectRoot, rel);
                if (File.Exists(p)) files[rel] = p;
            }
            return files;
        }

        /// <summary>Git blob id of a file (SHA-1 of "blob &lt;len&gt;\0" + bytes), computed without git.</summary>
        public static string GitBlob(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            byte[] header = Encoding.ASCII.GetBytes("blob " + data.Length + "\0");
            using (var sha = SHA1.Create())
            {
                sha.TransformBlock(header, 0, header.Length, null, 0);
                sha.TransformFinalBlock(data, 0, data.Length);
                return Hex(sha.Hash);
            }
        }

        /// <summary>
        /// Runtime + physics files of the game's committed HEAD (path → blob id) via "git ls-tree" (read only).
        /// Null when the game is not a git checkout or git is unavailable.
        /// </summary>
        public static SortedDictionary<string, string> CommittedFiles(string gameProject)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "-C " + Quote(gameProject) + " ls-tree -r HEAD -- " + RuntimeFolder + " " + RuntimeFolder + ".meta " + string.Join(" ", PhysicsSettings))
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) return null;
                    var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var line in output.Split('\n'))
                    {
                        int tab = line.IndexOf('\t');
                        if (tab < 0) continue;
                        var meta = line.Substring(0, tab).Split(' ');
                        string path = line.Substring(tab + 1).Trim();
                        if (path.StartsWith("\"")) continue; // quoted (unusual characters); not used by the runtime
                        if (path.StartsWith(RuntimeFolder + "/"))
                        {
                            string top = path.Substring(RuntimeFolder.Length + 1).Split('/')[0];
                            if (Array.IndexOf(ExcludedTopLevel, top) >= 0) continue;
                        }
                        if (meta.Length >= 3 && meta[1] == "blob") files[path] = meta[2];
                    }
                    return files;
                }
            }
            catch (Exception) { return null; }
        }

        private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

        /// <summary>
        /// Differences between the game's committed HEAD and this editor (empty = identical). Falls back to the
        /// working tree when git is unavailable.
        /// </summary>
        public static List<string> CompareCommitted(string gameProject)
        {
            var committed = CommittedFiles(gameProject);
            if (committed == null) return Compare(gameProject);
            var diffs = new List<string>();
            var mine = RuntimeFiles(EditorRoot);
            foreach (var kv in PhysicsFiles(EditorRoot)) mine[kv.Key] = kv.Value;
            foreach (var kv in committed)
            {
                if (!mine.TryGetValue(kv.Key, out var path)) diffs.Add("missing " + kv.Key);
                else if (GitBlob(path) != kv.Value) diffs.Add("differs " + kv.Key);
            }
            foreach (var kv in mine) if (!committed.ContainsKey(kv.Key)) diffs.Add("extra " + kv.Key);
            return diffs;
        }

        /// <summary>Differences between the game project's working tree and this editor (empty = identical).</summary>
        public static List<string> Compare(string gameProject)
        {
            var diffs = new List<string>();
            var src = RuntimeFiles(gameProject);
            var dst = RuntimeFiles(EditorRoot);
            if (src.Count == 0) { diffs.Add("game runtime not found in " + gameProject); return diffs; }
            foreach (var kv in src)
            {
                if (!dst.TryGetValue(kv.Key, out var mine)) diffs.Add("missing " + kv.Key);
                else if (Sha(kv.Value) != Sha(mine)) diffs.Add("differs " + kv.Key);
            }
            foreach (var kv in dst) if (!src.ContainsKey(kv.Key)) diffs.Add("extra " + kv.Key);
            foreach (var rel in PhysicsSettings)
            {
                string a = Path.Combine(gameProject, rel), b = Path.Combine(EditorRoot, rel);
                if (File.Exists(a) != File.Exists(b)) diffs.Add("settings presence " + rel);
                else if (File.Exists(a) && Sha(a) != Sha(b)) diffs.Add("settings differ " + rel);
            }
            return diffs;
        }

        [MenuItem("Metadream MapMaker/Sync Game Runtime From Game Project")]
        public static void SyncFromGame()
        {
            string game = ResolveGameProject();
            if (game == null || !Directory.Exists(Path.Combine(game, RuntimeFolder)))
            {
                Fail("game project not found (pass -gameProject <path> or set METADREAM_GAME_PROJECT): " + game);
                return;
            }
            if (Path.GetFullPath(game).TrimEnd('/') == Path.GetFullPath(EditorRoot).TrimEnd('/')) { Fail("game project is this project"); return; }
            var src = RuntimeFiles(game);
            var dst = RuntimeFiles(EditorRoot);
            int copied = 0, removed = 0;
            foreach (var kv in src)
            {
                string target = Path.Combine(EditorRoot, kv.Key);
                if (File.Exists(target) && Sha(target) == Sha(kv.Value)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(kv.Value, target, true);
                copied++;
            }
            // Mirror: remove runtime files the game no longer has (only inside this editor's Assets/Reimplementation).
            foreach (var kv in dst)
            {
                if (src.ContainsKey(kv.Key)) continue;
                if (!kv.Value.StartsWith(Path.Combine(EditorRoot, RuntimeFolder), StringComparison.Ordinal)) continue;
                File.Delete(kv.Value);
                removed++;
            }
            foreach (var rel in Concat(PhysicsSettings, OtherSettings))
            {
                string a = Path.Combine(game, rel);
                if (File.Exists(a)) File.Copy(a, Path.Combine(EditorRoot, rel), true);
            }
            CopyFolder(Path.Combine(game, "Assets/Settings"), Path.Combine(EditorRoot, "Assets/Settings"));
            WriteInfo(game);
            AssetDatabase.Refresh();
            Debug.Log("[GameSync] synced from " + game + ": " + copied + " copied, " + removed + " removed, " + src.Count + " runtime files");
        }

        [MenuItem("Metadream MapMaker/Verify Game Runtime")]
        public static void Verify()
        {
            string game = ResolveGameProject();
            if (game == null || !Directory.Exists(game)) { Fail("game project not found: " + game); return; }
            var diffs = CompareCommitted(game);
            var working = Compare(game);
            var info = File.Exists(InfoPath) ? JsonUtility.FromJson<GameRuntimeInfo>(File.ReadAllText(InfoPath)) : null;
            string runtimeHash = TreeHash(RuntimeFiles(EditorRoot));
            string gameHash = TreeHash(RuntimeFiles(game));
            string physicsHash = TreeHash(PhysicsFiles(EditorRoot));
            string gamePhysics = TreeHash(PhysicsFiles(game));
            Debug.Log("[GameSync] verify game=" + game + " commit=" + ReadCommit(game) + " runtime editor=" + runtimeHash + " game=" + gameHash
                + " physics editor=" + physicsHash + " game=" + gamePhysics + " manifest=" + (info != null ? info.runtimeSha256 : "none") + " diffsVsHEAD=" + diffs.Count + " diffsVsWorkingTree=" + working.Count);
            foreach (var d in diffs) Debug.LogWarning("[GameSync] " + d);
            foreach (var d in working) Debug.Log("[GameSync] game working tree (uncommitted, informational): " + d);
            if (diffs.Count == 0 && info != null && info.runtimeSha256 != runtimeHash) Debug.LogWarning("[GameSync] manifest is stale; run SyncFromGame or WriteInfo");
            if (diffs.Count > 0) Fail(diffs.Count + " differences from the game's committed HEAD");
            else Debug.Log("[GameSync] IDENTICAL to game HEAD " + ReadCommit(game) + ": " + RuntimeFiles(EditorRoot).Count + " runtime files and " + PhysicsSettings.Length + " physics settings files");
        }

        /// <summary>Writes the runtime manifest shown in the app (Resources/MapEditor/game-runtime.json).</summary>
        public static void WriteInfo(string gameProject)
        {
            var files = RuntimeFiles(EditorRoot);
            var info = new GameRuntimeInfo
            {
                sourceProject = gameProject ?? "",
                sourceCommit = gameProject != null ? ReadCommit(gameProject) : "",
                syncedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                unityVersion = Application.unityVersion,
                fileCount = files.Count,
                runtimeSha256 = TreeHash(files),
                settingsSha256 = TreeHash(PhysicsFiles(EditorRoot))
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(EditorRoot, InfoPath)));
            File.WriteAllText(Path.Combine(EditorRoot, InfoPath), JsonUtility.ToJson(info, true));
            Debug.Log("[GameSync] info " + JsonUtility.ToJson(info));
        }

        public static void WriteInfoBatch() { WriteInfo(ResolveGameProject()); AssetDatabase.Refresh(); }

        /// <summary>Commit id from .git without running git (read only).</summary>
        public static string ReadCommit(string project)
        {
            try
            {
                string git = Path.Combine(project, ".git");
                string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
                if (!head.StartsWith("ref: ")) return head;
                string refName = head.Substring(5).Trim();
                string refFile = Path.Combine(git, refName);
                if (File.Exists(refFile)) return File.ReadAllText(refFile).Trim();
                string packed = Path.Combine(git, "packed-refs");
                if (File.Exists(packed))
                    foreach (var line in File.ReadAllLines(packed))
                        if (line.EndsWith(" " + refName)) return line.Split(' ')[0];
            }
            catch (Exception) { }
            return "";
        }

        private static IEnumerable<string> Concat(string[] a, string[] b) { foreach (var x in a) yield return x; foreach (var x in b) yield return x; }

        private static void CopyFolder(string from, string to)
        {
            if (!Directory.Exists(from)) return;
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == ".DS_Store") continue;
                string target = Path.Combine(to, file.Substring(from.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private static void Fail(string message)
        {
            Debug.LogError("[GameSync] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
