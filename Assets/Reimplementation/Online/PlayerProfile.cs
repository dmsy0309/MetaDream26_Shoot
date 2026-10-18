using System;
using System.Text;
using UnityEngine;

namespace Metadream.Shoot.Online
{
    /// <summary>Player nickname (PlayerPrefs) and the device name sent to hosts and stored in replays.</summary>
    public static class PlayerProfile
    {
        public const int MaxLength = 12;
        private const string Key = "player.nickname";

        public static bool HasNickname => !string.IsNullOrEmpty(PlayerPrefs.GetString(Key, ""));

        /// <summary>-nickname: this run only (not saved), so several test instances on one PC keep their own names.</summary>
        public static string SessionOverride { get; set; }

        public static string Nickname
        {
            get
            {
                if (SessionOverride != null) return SessionOverride;
                string n = Sanitize(PlayerPrefs.GetString(Key, ""));
                if (n != null) return n;
                n = "플레이어" + UnityEngine.Random.Range(100, 1000);
                PlayerPrefs.SetString(Key, n);
                return n;
            }
            set
            {
                string n = Sanitize(value);
                if (n == null) return;
                if (SessionOverride != null) SessionOverride = n;
                PlayerPrefs.SetString(Key, n);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Trimmed, single-spaced, printable, at most MaxLength characters; null when empty.</summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var sb = new StringBuilder();
            bool space = false;
            foreach (char c in raw.Trim())
            {
                if (char.IsControl(c) || c == '|' || c == '"' || c == '\\') continue;
                if (char.IsWhiteSpace(c)) { if (!space && sb.Length > 0) sb.Append(' '); space = true; continue; }
                space = false;
                sb.Append(c);
                if (sb.Length >= MaxLength) break;
            }
            string s = sb.ToString().Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>Computer name (e.g. school PC "LAB-PC-07"), used to find players later.</summary>
        public static string DeviceName
        {
            get
            {
                try
                {
                    string m = Environment.MachineName;
                    if (!string.IsNullOrWhiteSpace(m)) return m.Length > 40 ? m.Substring(0, 40) : m;
                }
                catch (Exception) { }
                return SystemInfo.deviceName ?? "unknown";
            }
        }
    }
}
