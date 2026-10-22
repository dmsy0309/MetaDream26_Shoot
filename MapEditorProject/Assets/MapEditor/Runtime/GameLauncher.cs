using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Debug = UnityEngine.Debug;

namespace Metadream.MapEditor
{
    /// <summary>Finds and starts the real Metadream Shoot build (macOS .app or Windows .exe).</summary>
    public static class GameLauncher
    {
        public enum Mode { VersusCpu, CheatDuel, CpuDemo }

        public static string ArgsFor(Mode mode)
        {
            switch (mode)
            {
                case Mode.CheatDuel: return "-cheat 1";
                case Mode.CpuDemo: return "-demo -norematch";
                default: return "-versuscpu";
            }
        }

        /// <summary>Executable inside a game path (.app bundle, .exe, or a folder containing one).</summary>
        public static string FindExecutable(string gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath)) return null;
            string p = gamePath.Trim().TrimEnd('/', '\\');
            if (p.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(p))
            {
                string macos = Path.Combine(p, "Contents", "MacOS");
                if (!Directory.Exists(macos)) return null;
                string plist = Path.Combine(p, "Contents", "Info.plist");
                string name = File.Exists(plist) ? PlistValue(File.ReadAllText(plist), "CFBundleExecutable") : null;
                if (name != null && File.Exists(Path.Combine(macos, name))) return Path.Combine(macos, name);
                var files = Directory.GetFiles(macos);
                return files.Length > 0 ? files[0] : null;
            }
            if (File.Exists(p)) return p;
            if (Directory.Exists(p))
            {
                foreach (var app in Directory.GetDirectories(p, "*.app"))
                    if (Path.GetFileName(app).IndexOf("MapMaker", StringComparison.OrdinalIgnoreCase) < 0) return FindExecutable(app);
                foreach (var exe in Directory.GetFiles(p, "*.exe"))
                {
                    string n = Path.GetFileName(exe);
                    if (n.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase) || n.IndexOf("MapMaker", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    return exe;
                }
            }
            return null;
        }

        private static string PlistValue(string plist, string key)
        {
            int k = plist.IndexOf("<key>" + key + "</key>", StringComparison.Ordinal);
            if (k < 0) return null;
            int s = plist.IndexOf("<string>", k, StringComparison.Ordinal);
            int e = s < 0 ? -1 : plist.IndexOf("</string>", s, StringComparison.Ordinal);
            return s < 0 || e < 0 ? null : plist.Substring(s + 8, e - s - 8).Trim();
        }

        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            foreach (char c in arg)
            {
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Starts the game with -content &lt;dir&gt; and a mode flag. Returns an error text or null.</summary>
        public static string Launch(string gamePath, string contentDir, Mode mode, string extraArgs = null)
        {
            string exe = FindExecutable(gamePath);
            if (exe == null) return "게임 실행 파일을 찾을 수 없습니다: " + gamePath;
            string args = (string.IsNullOrEmpty(contentDir) ? "" : "-content " + Quote(contentDir) + " ") + ArgsFor(mode) + (string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs);
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                    CreateNoWindow = false
                };
                Process.Start(psi);
                Debug.Log("[MapEditor] launched game " + exe + " " + args);
                return null;
            }
            catch (Exception e)
            {
                return "게임을 실행하지 못했습니다: " + e.Message;
            }
        }
    }
}
