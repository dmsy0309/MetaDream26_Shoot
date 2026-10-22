using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Metadream.MapEditor
{
    /// <summary>
    /// Native file dialogs: Windows common dialogs (comdlg32 / shell32), macOS AppleScript, Linux zenity.
    /// Every caller also offers a typed path field, so a missing dialog never blocks the user.
    /// </summary>
    public static class NativeDialogs
    {
        public static string OpenFile(string title, string extension)
        {
            try
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return Win.Open(title, extension, false);
#else
                if (IsMac) return Osa("POSIX path of (choose file with prompt " + As(title) + ")");
                return Run("zenity", "--file-selection --title=" + GameLauncher.Quote(title));
#endif
            }
            catch (Exception e) { Debug.LogWarning("[MapEditor] dialog unavailable: " + e.Message); return null; }
        }

        public static string SaveFile(string title, string defaultName, string directory)
        {
            try
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return Win.Save(title, defaultName, directory);
#else
                if (IsMac)
                {
                    string loc = !string.IsNullOrEmpty(directory) && Directory.Exists(directory) ? " default location (POSIX file " + As(directory) + " as alias)" : "";
                    return Osa("POSIX path of (choose file name with prompt " + As(title) + " default name " + As(defaultName) + loc + ")");
                }
                return Run("zenity", "--file-selection --save --confirm-overwrite --filename=" + GameLauncher.Quote(defaultName));
#endif
            }
            catch (Exception e) { Debug.LogWarning("[MapEditor] dialog unavailable: " + e.Message); return null; }
        }

        public static string ChooseFolder(string title)
        {
            try
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return Win.Folder(title);
#else
                if (IsMac) return Osa("POSIX path of (choose folder with prompt " + As(title) + ")");
                return Run("zenity", "--file-selection --directory");
#endif
            }
            catch (Exception e) { Debug.LogWarning("[MapEditor] dialog unavailable: " + e.Message); return null; }
        }

        /// <summary>Game executable: a .app bundle on macOS (selectable as a file), the .exe on Windows.</summary>
        public static string ChooseGame()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try { return Win.Open("게임 실행 파일(MetadreamShoot.exe) 선택", "exe", true); }
            catch (Exception e) { Debug.LogWarning("[MapEditor] dialog unavailable: " + e.Message); return null; }
#else
            if (IsMac)
            {
                try { return Osa("POSIX path of (choose file with prompt " + As("게임 앱(MetadreamShoot.app) 선택") + " of type {\"com.apple.application-bundle\"})"); }
                catch (Exception e) { Debug.LogWarning("[MapEditor] dialog unavailable: " + e.Message); return null; }
            }
            return OpenFile("게임 실행 파일 선택", "");
#endif
        }

        private static bool IsMac => Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor;

        /// <summary>AppleScript string literal.</summary>
        private static string As(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string Osa(string script)
        {
            string path = Run("osascript", "-e " + GameLauncher.Quote(script));
            return path?.TrimEnd('/') is string p && p.Length > 0 ? (script.Contains("choose folder") ? p + "/" : p) : null;
        }

        private static string Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return p.ExitCode == 0 && output.Length > 0 ? output : null;
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private static class Win
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct OpenFileName
            {
                public int lStructSize;
                public IntPtr hwndOwner;
                public IntPtr hInstance;
                public string lpstrFilter;
                public string lpstrCustomFilter;
                public int nMaxCustFilter;
                public int nFilterIndex;
                public IntPtr lpstrFile;
                public int nMaxFile;
                public string lpstrFileTitle;
                public int nMaxFileTitle;
                public string lpstrInitialDir;
                public string lpstrTitle;
                public int Flags;
                public short nFileOffset;
                public short nFileExtension;
                public string lpstrDefExt;
                public IntPtr lCustData;
                public IntPtr lpfnHook;
                public string lpTemplateName;
                public IntPtr pvReserved;
                public int dwReserved;
                public int FlagsEx;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct BrowseInfo
            {
                public IntPtr hwndOwner;
                public IntPtr pidlRoot;
                public IntPtr pszDisplayName;
                public string lpszTitle;
                public uint ulFlags;
                public IntPtr lpfn;
                public IntPtr lParam;
                public int iImage;
            }

            [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool GetOpenFileNameW(ref OpenFileName ofn);
            [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool GetSaveFileNameW(ref OpenFileName ofn);
            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr SHBrowseForFolderW(ref BrowseInfo bi);
            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder path);
            [DllImport("ole32.dll")]
            private static extern void CoTaskMemFree(IntPtr ptr);
            [DllImport("user32.dll")]
            private static extern IntPtr GetActiveWindow();

            private const int Max = 4096;

            private static string Filter(string ext, bool exe)
            {
                if (exe) return "실행 파일 (*.exe)\0*.exe\0모든 파일 (*.*)\0*.*\0\0";
                if (ext == "zip") return "ZIP 압축 (*.zip)\0*.zip\0모든 파일 (*.*)\0*.*\0\0";
                return "맵 JSON (*.json)\0*.json\0모든 파일 (*.*)\0*.*\0\0";
            }

            public static string Open(string title, string ext, bool exe)
            {
                IntPtr buffer = Marshal.AllocHGlobal(Max * 2);
                try
                {
                    Marshal.WriteInt16(buffer, 0);
                    var ofn = new OpenFileName
                    {
                        lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
                        hwndOwner = GetActiveWindow(),
                        lpstrFilter = Filter(ext, exe),
                        lpstrFile = buffer,
                        nMaxFile = Max,
                        lpstrTitle = title,
                        Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008
                    };
                    return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }

            public static string Save(string title, string defaultName, string directory)
            {
                IntPtr buffer = Marshal.AllocHGlobal(Max * 2);
                try
                {
                    var bytes = new byte[Max * 2];
                    var name = Encoding.Unicode.GetBytes(defaultName ?? "");
                    Array.Copy(name, bytes, Math.Min(name.Length, bytes.Length - 2));
                    Marshal.Copy(bytes, 0, buffer, bytes.Length);
                    string ext = Path.GetExtension(defaultName ?? "").TrimStart('.');
                    var ofn = new OpenFileName
                    {
                        lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
                        hwndOwner = GetActiveWindow(),
                        lpstrFilter = Filter(ext, false),
                        lpstrFile = buffer,
                        nMaxFile = Max,
                        lpstrInitialDir = directory,
                        lpstrTitle = title,
                        lpstrDefExt = ext,
                        Flags = 0x00080000 | 0x00000002 | 0x00000800 | 0x00000008 // EXPLORER | OVERWRITEPROMPT | PATHMUSTEXIST | NOCHANGEDIR
                    };
                    return GetSaveFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }

            public static string Folder(string title)
            {
                var bi = new BrowseInfo { hwndOwner = GetActiveWindow(), lpszTitle = title, ulFlags = 0x00000001 | 0x00000040 }; // RETURNONLYFSDIRS | NEWDIALOGSTYLE
                IntPtr pidl = SHBrowseForFolderW(ref bi);
                if (pidl == IntPtr.Zero) return null;
                try
                {
                    var sb = new StringBuilder(Max);
                    return SHGetPathFromIDListW(pidl, sb) ? sb.ToString() : null;
                }
                finally { CoTaskMemFree(pidl); }
            }
        }
#endif
    }
}
