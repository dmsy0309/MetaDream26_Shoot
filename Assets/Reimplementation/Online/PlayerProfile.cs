using System;
using System.Text;
using UnityEngine;

namespace Metadream.Shoot.Online
{
    /// <summary>Player nickname and a random connection-session alias, never the OS account or machine name.</summary>
    public static class PlayerProfile
    {
        public const int MaxLength = 12;
        private const string Key = "player.nickname";
        private const string LookKey = "player.look";

        /// <summary>-look H,E,A: this run only (not saved), for screenshots and several test instances on one PC.</summary>
        public static Player.CharacterLook? SessionLook { get; set; }

        /// <summary>Character look chosen in the main menu (캐릭터 꾸미기), saved in PlayerPrefs.</summary>
        public static Player.CharacterLook Look
        {
            get
            {
                if (SessionLook.HasValue) return SessionLook.Value.Sanitized;
                string json = PlayerPrefs.GetString(LookKey + ".v2", "");
                if (!string.IsNullOrEmpty(json)) { try { return JsonUtility.FromJson<Player.CharacterLook>(json).Sanitized; } catch (ArgumentException) { } }
                return Player.CharacterLook.Unpack(PlayerPrefs.GetInt(LookKey, 0)).Sanitized;
            }
            set
            {
                if (SessionLook.HasValue) { SessionLook = value.Sanitized; return; }
                PlayerPrefs.SetInt(LookKey, value.Sanitized.Pack());
                PlayerPrefs.SetString(LookKey + ".v2", JsonUtility.ToJson(value.Sanitized));
                PlayerPrefs.Save();
            }
        }

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

        private static readonly string SessionDevice = "session-" + Guid.NewGuid().ToString("N");

        /// <summary>Stable only for this process, so reconnects work without identifying the computer.</summary>
        public static string DeviceName => SessionDevice;
    }
}
