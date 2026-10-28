using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using Metadream.Shoot.Network;

namespace Metadream.Shoot.Replay
{
    [Serializable]
    public sealed class ReplayDevice
    {
        public string machineName = "";
        public string userName = "";
        public string os = "";
        public string deviceId = "";
        public string localIp = "";
        /// <summary>"dedicated" or "host".</summary>
        public string role = "";

        public static ReplayDevice Current(bool dedicated)
        {
            // Retain fields for old replay compatibility, but do not collect device/account information.
            return new ReplayDevice { role = dedicated ? "dedicated" : "host" };
        }
    }

    [Serializable]
    public sealed class ReplayPlayerInfo
    {
        public int slot;
        public int team;
        public string nickname = "";
        /// <summary>host / remote / cpu</summary>
        public string kind = "";
        /// <summary>Computer name the player used (from Hello), empty for CPU.</summary>
        public string device = "";
        public string address = "";
    }

    [Serializable]
    public sealed class ReplayMeta
    {
        public string id = "";
        public string createdUtc = "";
        public string createdLocal = "";
        public string gameVersion = "";
        public int protocol;
        public string contentHash = "";
        public string roomName = "";
        public int teamSize = 1;
        public bool dedicated;
        public float duration;
        public int frames;
        public int winningTeam = -1;
        public string score = "";
        public ReplayDevice device = new ReplayDevice();
        public ReplayPlayerInfo[] players = new ReplayPlayerInfo[0];

        /// <summary>Remove host and peer device details, including metadata from older clients/replays.</summary>
        public void ClearDeviceDetails()
        {
            device = ReplayDevice.Current(dedicated);
            if (players == null) return;
            foreach (var player in players)
            {
                if (player == null) continue;
                player.device = "";
                player.address = "";
            }
        }

        public string PlayersLabel
        {
            get
            {
                var parts = new List<string>();
                foreach (var p in players) parts.Add(p.nickname + (p.kind == "cpu" ? "(CPU)" : ""));
                return string.Join(", ", parts);
            }
        }
    }

    public struct ReplayFrame
    {
        public float Time;
        public byte[] Data;
    }

    public sealed class ReplayData
    {
        public ReplayMeta Meta;
        public readonly List<ReplayFrame> Frames = new List<ReplayFrame>();
        public string Path;
        public float Duration => Frames.Count > 0 ? Frames[Frames.Count - 1].Time : 0f;
    }

    /// <summary>
    /// .mdreplay = gzip( "MDREPLAY" | int format | string metaJson | int frameCount | frameCount × (float time, int length, snapshot bytes) ).
    /// Each frame is a full MatchSnapshot exactly as sent to network clients (30 Hz), so playback shows what players saw.
    /// </summary>
    public static class ReplayFile
    {
        public const string Magic = "MDREPLAY";
        public const int Format = 1;
        public const string Extension = ".mdreplay";

        public static void Write(Stream output, ReplayMeta meta, IReadOnlyList<ReplayFrame> frames)
        {
            using (var gz = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(gz))
            {
                w.Write(Magic);
                w.Write(Format);
                w.Write(JsonUtility.ToJson(meta));
                w.Write(frames.Count);
                foreach (var f in frames)
                {
                    w.Write(f.Time);
                    w.Write(f.Data.Length);
                    w.Write(f.Data);
                }
            }
        }

        public static ReplayData Read(Stream input, bool metaOnly = false)
        {
            using (var gz = new GZipStream(input, CompressionMode.Decompress, true))
            using (var r = new BinaryReader(gz))
            {
                if (r.ReadString() != Magic) throw new InvalidDataException("리플레이 파일이 아닙니다");
                int format = r.ReadInt32();
                if (format != Format) throw new InvalidDataException("지원하지 않는 리플레이 형식 v" + format);
                var data = new ReplayData { Meta = JsonUtility.FromJson<ReplayMeta>(r.ReadString()) ?? new ReplayMeta() };
                if (metaOnly) return data;
                int n = r.ReadInt32();
                if (n < 0 || n > 10_000_000) throw new InvalidDataException("손상된 리플레이 (frames " + n + ")");
                for (int i = 0; i < n; i++)
                {
                    float t = r.ReadSingle();
                    int len = r.ReadInt32();
                    if (len < 0 || len > 1 << 20) throw new InvalidDataException("손상된 리플레이 (frame " + i + ")");
                    data.Frames.Add(new ReplayFrame { Time = t, Data = r.ReadBytes(len) });
                }
                return data;
            }
        }

        public static ReplayData Load(string path, bool metaOnly = false)
        {
            using (var fs = File.OpenRead(path))
            {
                var d = Read(fs, metaOnly);
                d.Path = path;
                return d;
            }
        }
    }

    /// <summary>Replays folder inside the game folder: next to the executable (next to the .app on macOS), project root in the editor.</summary>
    public static class ReplayStore
    {
        public static string Folder
        {
            get
            {
                string root = Path.GetDirectoryName(Application.dataPath);
                if (!Application.isEditor && root != null && root.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(root);
                return Path.Combine(root ?? ".", "Replays");
            }
        }

        public static List<ReplayData> List()
        {
            var list = new List<ReplayData>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var file in Directory.GetFiles(Folder, "*" + ReplayFile.Extension))
            {
                try { list.Add(ReplayFile.Load(file, true)); }
                catch (Exception e) { list.Add(new ReplayData { Path = file, Meta = new ReplayMeta { roomName = "읽을 수 없음: " + e.Message } }); }
            }
            list.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b.Path), Path.GetFileName(a.Path)));
            return list;
        }

        /// <summary>Copies an external replay into the Replays folder (validated first). Returns the new path.</summary>
        public static string Import(string source)
        {
            ReplayFile.Load(source, true);
            Directory.CreateDirectory(Folder);
            string full = Path.GetFullPath(source);
            if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase)) return full;
            string name = Path.GetFileNameWithoutExtension(source);
            string dest = Path.Combine(Folder, name + ReplayFile.Extension);
            for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(Folder, name + "-" + i + ReplayFile.Extension);
            File.Copy(source, dest);
            return dest;
        }

        public static string SafeName(string s)
        {
            var chars = (s ?? "").ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0 || chars[i] == ' ') chars[i] = '_';
            string r = new string(chars);
            return r.Length > 24 ? r.Substring(0, 24) : r;
        }
    }

    /// <summary>Host side: collects snapshots of one game and writes/uploads the replay when finished.</summary>
    public sealed class ReplayRecorder
    {
        private readonly List<ReplayFrame> _frames = new List<ReplayFrame>();
        private readonly MemoryStream _buffer = new MemoryStream();
        private readonly float _start;
        public readonly ReplayMeta Meta;
        public float Elapsed => UnityEngine.Time.unscaledTime - _start;
        public int FrameCount => _frames.Count;

        public ReplayRecorder(ReplayMeta meta)
        {
            Meta = meta;
            _start = UnityEngine.Time.unscaledTime;
            var now = DateTime.Now;
            meta.id = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            meta.createdUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            meta.createdLocal = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            meta.gameVersion = Application.version;
            meta.protocol = Protocol.Version;
            meta.contentHash = Content.ContentDatabase.Hash;
        }

        public void Add(MatchSnapshot snapshot) => Add(Elapsed, snapshot);

        public void Add(float time, MatchSnapshot snapshot)
        {
            _buffer.SetLength(0);
            using (var w = new BinaryWriter(_buffer, System.Text.Encoding.UTF8, true)) snapshot.Write(w);
            _frames.Add(new ReplayFrame { Time = time, Data = _buffer.ToArray() });
        }

        public void SetPlayer(ReplayPlayerInfo info)
        {
            var list = new List<ReplayPlayerInfo>(Meta.players);
            int i = list.FindIndex(p => p.slot == info.slot);
            if (i >= 0) list[i] = info; else list.Add(info);
            list.Sort((a, b) => a.slot.CompareTo(b.slot));
            Meta.players = list.ToArray();
        }

        /// <summary>Writes Replays/&lt;date&gt;_&lt;room&gt;.mdreplay and starts the upload. Returns the path, or null when too short.</summary>
        public string Finish(int winningTeam, string score, bool upload, float minSeconds = 5f)
        {
            if (_frames.Count == 0 || _frames[_frames.Count - 1].Time < minSeconds) return null;
            Meta.duration = _frames[_frames.Count - 1].Time;
            Meta.frames = _frames.Count;
            Meta.winningTeam = winningTeam;
            Meta.score = score ?? "";
            try
            {
                Meta.ClearDeviceDetails();
                Directory.CreateDirectory(ReplayStore.Folder);
                string path = Path.Combine(ReplayStore.Folder, Meta.id + "_" + ReplayStore.SafeName(Meta.roomName) + ReplayFile.Extension);
                using (var fs = File.Create(path)) ReplayFile.Write(fs, Meta, _frames);
                Debug.Log("[Replay] saved " + path + " (" + Meta.frames + " frames, " + Meta.duration.ToString("0") + "s, players " + Meta.PlayersLabel + ")");
                if (upload) ReplayUploader.Upload(path, Meta);
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Replay] save failed: " + e.Message);
                return null;
            }
        }
    }
}
