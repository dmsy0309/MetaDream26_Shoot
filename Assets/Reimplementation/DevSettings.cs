using System;
using UnityEngine;
using Metadream.Shoot.Abilities;

namespace Metadream.Shoot
{
    /// <summary>Player-facing settings persisted in PlayerPrefs (Settings menu).</summary>
    public static class DevSettings
    {
        private const string KeyDebug = "settings.debugMode";
        private const string KeyFullscreen = "settings.fullscreen";
        private const string KeyMaster = "settings.masterServer";
        private const string KeySfx = "settings.sfxVolume";
        private static bool _loaded;
        private static bool _debug, _fullscreen, _master = true;
        private static float _sfx = 0.8f;

        public static event Action Changed;

        /// <summary>Developer debug mode: overlay, gizmos, content report, F1–F7 commands, verbose logs.</summary>
        public static bool DebugMode
        {
            get { Load(); return _debug; }
            set { Load(); _debug = value; DevLog.Enabled = value; Save(); }
        }

        public static bool Fullscreen
        {
            get { Load(); return _fullscreen; }
            set
            {
                Load();
                _fullscreen = value;
                Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (!value && Screen.width > 0) Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
                Save();
            }
        }

        /// <summary>Show and register rooms with the main hosting server (server list).</summary>
        public static bool UseMasterServer
        {
            get { Load(); return _master; }
            set { Load(); _master = value; Save(); }
        }

        /// <summary>Sound effect volume 0..1 (Settings → 효과음 볼륨).</summary>
        public static float SfxVolume
        {
            get { Load(); return _sfx; }
            set { Load(); _sfx = Mathf.Clamp01(value); Save(); }
        }

        /// <summary>-debug forces debug mode on for this run without saving.</summary>
        public static void ApplyCommandLine(string[] args)
        {
            Load();
            if (Array.IndexOf(args, "-debug") >= 0) { _debug = true; DevLog.Enabled = true; }
            if (Array.IndexOf(args, "-nodebug") >= 0) { _debug = false; DevLog.Enabled = false; } // this session only (not saved)
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _debug = PlayerPrefs.GetInt(KeyDebug, 0) == 1;
            _fullscreen = PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
            _master = PlayerPrefs.GetInt(KeyMaster, 1) == 1;
            _sfx = PlayerPrefs.GetFloat(KeySfx, 0.8f);
            DevLog.Enabled = _debug;
        }

        private static void Save()
        {
            PlayerPrefs.SetInt(KeyDebug, _debug ? 1 : 0);
            PlayerPrefs.SetInt(KeyFullscreen, _fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(KeyMaster, _master ? 1 : 0);
            PlayerPrefs.SetFloat(KeySfx, _sfx);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}

