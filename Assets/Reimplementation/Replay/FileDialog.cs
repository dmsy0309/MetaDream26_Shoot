using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Debug = UnityEngine.Debug;

namespace Metadream.Shoot.Replay
{
    /// <summary>Native "open file" dialog for importing replays: Windows common dialog, macOS AppleScript, Linux zenity.</summary>
    public static class FileDialog
    {
        public static string OpenReplay()
        {
            try
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return OpenWindows();
#else
                if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXPlayer || UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXEditor)
                    return Run("osascript", "-e \"POSIX path of (choose file with prompt \\\"리플레이 파일(.mdreplay) 선택\\\")\"");
                return Run("zenity", "--file-selection --title=Replay --file-filter=*.mdreplay");
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Replay] file dialog unavailable: " + e.Message);
                return null;
            }
        }

        private static string Run(string exe, string args)
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }))
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return p.ExitCode == 0 && output.Length > 0 ? output : null;
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private static string OpenWindows()
        {
            const int max = 2048;
            IntPtr buffer = Marshal.AllocHGlobal(max * 2);
            try
            {
                Marshal.WriteInt16(buffer, 0);
                var ofn = new OpenFileName
                {
                    lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = "Metadream 리플레이 (*.mdreplay)\0*.mdreplay\0모든 파일 (*.*)\0*.*\0\0",
                    lpstrFile = buffer,
                    nMaxFile = max,
                    lpstrTitle = "리플레이 불러오기",
                    Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008 // EXPLORER | FILEMUSTEXIST | PATHMUSTEXIST | NOCHANGEDIR
                };
                return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
#endif
    }
}
