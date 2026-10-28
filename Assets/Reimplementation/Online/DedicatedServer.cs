using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;
using Metadream.Shoot.Core;
using Metadream.Shoot.Network;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Online
{
    /// <summary>
    /// server.json. Every field can be overridden on the command line (see DedicatedServer.ApplyArgs).
    /// "rules" holds the default game settings (points, rounds, cards per pick, map/card pools and the
    /// starting fighter stats) and is the main knob for server operators. See Docs/SERVER_CONFIG.md.
    /// </summary>
    [Serializable]
    public sealed class ServerConfig
    {
        public string roomName = "Metadream 서버";
        public int port = Protocol.DefaultPort;
        /// <summary>1 = 1v1, 2 = 2v2.</summary>
        public int teamSize = 1;
        /// <summary>Start automatically once this many players joined (0 = only the room leader starts).</summary>
        public int minPlayersToStart = 2;
        public float startCountdown = 10f;
        /// <summary>Fill empty slots with CPU players; when false the room must be full to start.</summary>
        public bool fillWithCpu = true;
        public bool allowMidJoin = true;
        public float rematchDelay = 8f;
        public bool lanBroadcast = true;
        public int targetFrameRate = 60;
        public float statusInterval = 30f;
        /// <summary>Register with the main hosting server so the room shows up in every client's server list.</summary>
        public bool masterServer = true;
        public string masterHost = Environment.GetEnvironmentVariable("METADREAM_MASTER_HOST") ?? MasterServerEndpoint.DefaultHost;
        public int masterPort = MasterServerEndpoint.DefaultPort;
        /// <summary>Address players should use (e.g. public IP or DNS). Empty = what the master sees.</summary>
        public string publicAddress = "";
        /// <summary>Extra cards/maps folder (empty = "Content" next to the executable, if present).</summary>
        public string contentDir = "";
        /// <summary>Verbose ability/effect logs (DevLog) on the server console.</summary>
        public bool debugLog;
        /// <summary>Save every game as Replays/*.mdreplay next to the server executable.</summary>
        public bool saveReplays = true;
        /// <summary>Opt-in replay upload; also requires explicit HTTPS endpoint and upload credentials.</summary>
        public bool uploadReplays = false;
        /// <summary>Default game settings used for every match on this server.</summary>
        public MatchSettings rules = new MatchSettings();

        /// <summary>Validated copy of rules (a fresh object so console edits never leak into a running match).</summary>
        public MatchSettings ToSettings()
        {
            var r = rules ?? new MatchSettings();
            var s = JsonUtility.FromJson<MatchSettings>(JsonUtility.ToJson(r));
            s.pointsToWinRound = Mathf.Max(1, s.pointsToWinRound);
            s.roundsToWinGame = Mathf.Max(1, s.roundsToWinGame);
            s.cardsPerPick = Mathf.Clamp(s.cardsPerPick, 1, 10);
            s.rerollsAtStart = Mathf.Clamp(s.rerollsAtStart, 0, 20);
            s.rerollsPerRound = Mathf.Clamp(s.rerollsPerRound, 0, 10);
            s.pickTimeLimit = Mathf.Clamp(s.pickTimeLimit, 0, 120);
            s.mapPool = s.mapPool ?? new string[0];
            s.cardPool = s.cardPool ?? new string[0];
            var b = s.stats ?? new BaseStats();
            b.maxHealth = Mathf.Max(1f, b.maxHealth);
            b.bulletDamage = Mathf.Max(0f, b.bulletDamage);
            b.bulletSpeed = Mathf.Max(1f, b.bulletSpeed);
            b.attackCooldown = Mathf.Max(0.02f, b.attackCooldown);
            b.maxAmmo = Mathf.Clamp(b.maxAmmo, 1, 99);
            b.reloadTime = Mathf.Max(0f, b.reloadTime);
            b.blockCooldown = Mathf.Max(0f, b.blockCooldown);
            b.movementSpeed = Mathf.Max(0.1f, b.movementSpeed);
            b.jumps = Mathf.Clamp(b.jumps, 1, 10);
            s.stats = b;
            return s;
        }

        /// <summary>Human-readable problems with rules (unknown map/card ids...). Empty when fine.</summary>
        public System.Collections.Generic.List<string> Validate()
        {
            var problems = new System.Collections.Generic.List<string>();
            var s = rules ?? new MatchSettings();
            if (s.mapPool != null)
                foreach (var id in s.mapPool)
                {
                    var m = Content.ContentDatabase.FindMap(id);
                    if (m == null) problems.Add("rules.mapPool: unknown map '" + id + "'");
                    else if (m.TutorialOnly) problems.Add("rules.mapPool: '" + id + "' is tutorial-only");
                }
            if (s.cardPool != null)
                foreach (var id in s.cardPool)
                    if (Content.ContentDatabase.FindCard(id) == null) problems.Add("rules.cardPool: unknown card '" + id + "'");
            if (teamSize < 1 || teamSize > 2) problems.Add("teamSize must be 1 or 2");
            return problems;
        }

        public static ServerConfig Load(string path, out string message)
        {
            message = null;
            if (string.IsNullOrEmpty(path)) return new ServerConfig();
            try
            {
                if (!File.Exists(path))
                {
                    message = "config not found: " + path + " (using defaults)";
                    return new ServerConfig();
                }
                var cfg = JsonUtility.FromJson<ServerConfig>(File.ReadAllText(path));
                message = "config loaded: " + Path.GetFullPath(path);
                return cfg ?? new ServerConfig();
            }
            catch (Exception e)
            {
                message = "config error in " + path + ": " + e.Message + " (using defaults)";
                return new ServerConfig();
            }
        }

        public string ToJson() => JsonUtility.ToJson(this, true);
    }

    /// <summary>
    /// Headless dedicated server runner: owns a NetHostSession in dedicated mode, auto-starts matches,
    /// auto-rematches, returns to the lobby when everyone leaves and accepts console commands on stdin.
    /// </summary>
    public sealed class DedicatedServer : MonoBehaviour
    {
        public static DedicatedServer Instance { get; private set; }

        private readonly ConcurrentQueue<string> _commands = new ConcurrentQueue<string>();
        private NetHostSession _host;
        private float _statusTimer;
        private float _gameOverAt = -1f;
        private float _startedAt;
        private Thread _console;
        private bool _quitting;

        public ServerConfig Config { get; private set; }

        public static DedicatedServer Launch(string[] args)
        {
            var go = new GameObject("DedicatedServer");
            var s = go.AddComponent<DedicatedServer>();
            s.Boot(args);
            return s;
        }

        private void Boot(string[] args)
        {
            Instance = this;
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            string configPath = StringArg(args, "-config");
            Config = ServerConfig.Load(configPath, out string message);
            ApplyArgs(Config, args);
            if (Config.debugLog || Array.IndexOf(args, "-debug") >= 0) Abilities.DevLog.Enabled = true;
            if (!string.IsNullOrWhiteSpace(Config.contentDir))
            {
                string dir = Config.contentDir;
                if (!Path.IsPathRooted(dir) && !string.IsNullOrEmpty(configPath)) dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? "", dir);
                Content.ContentDatabase.ExternalDirectory = dir;
                Content.ContentDatabase.Reload();
            }
            MasterServerEndpoint.Host = string.IsNullOrWhiteSpace(Config.masterHost) ? MasterServerEndpoint.DefaultHost : Config.masterHost.Trim();
            MasterServerEndpoint.Port = Config.masterPort > 0 ? Config.masterPort : MasterServerEndpoint.DefaultPort;
            Log("Metadream Shoot dedicated server " + Application.version + " (protocol v" + Protocol.Version + ")");
            if (message != null) Log(message);
            Log("settings " + JsonUtility.ToJson(Config));
            Log("content " + Content.ContentDatabase.Cards.Count + " cards, " + Content.ContentDatabase.Maps.Count + " maps, hash " + Content.ContentDatabase.Hash
                + (Content.ContentDatabase.LoadedExternalDirectory != null ? " (external " + Content.ContentDatabase.LoadedExternalDirectory + ")" : "")
                + ", errors " + Content.ContentDatabase.ErrorCount);
            foreach (var problem in Config.Validate()) Log("CONFIG WARNING " + problem);

            Application.targetFrameRate = Mathf.Clamp(Config.targetFrameRate, 20, 240);
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
            Fx.Headless = true;
            _startedAt = Time.realtimeSinceStartup;

            var flow = GameFlow.Instance;
            flow.AutoRematch = false; // no rematch: after the result screen the room empties and players return to their title
            flow.RematchDelay = Mathf.Max(1f, Config.rematchDelay);
            flow.DedicatedServer = true;

            _host = NetHostSession.Open(new HostOptions
            {
                Dedicated = true,
                TeamSize = Config.teamSize,
                Port = Config.port,
                RoomName = Config.roomName,
                AllowMidJoin = Config.allowMidJoin,
                FillWithCpu = Config.fillWithCpu,
                LanBroadcast = Config.lanBroadcast,
                MasterServer = Config.masterServer,
                PublicAddress = Config.publicAddress ?? "",
                SaveReplays = Config.saveReplays,
                UploadReplays = Config.uploadReplays,
                Settings = Config.ToSettings()
            }, out string error);
            if (_host == null)
            {
                Log("FATAL " + error);
                Application.Quit(2);
                return;
            }
            _host.StartRequested += _ => TryStart("room leader");
            Log("listening on TCP " + _host.Port + " · LAN address " + _host.Address + " · room code " + _host.Code);
            Log(Config.masterServer ? "main hosting server " + MasterServerEndpoint.Host + ":" + MasterServerEndpoint.Port + " (heartbeat every " + MasterServerEndpoint.HeartbeatInterval + "s)" : "main hosting server disabled");
            Log("type 'help' for console commands");
            StartConsole();
        }

        /// <summary>-port -teamsize -name -minplayers -countdown -rounds -points -cards -rematch -fps -status -public -master host[:port] -content dir -nofill -nomidjoin -nolan -nomaster</summary>
        public static void ApplyArgs(ServerConfig c, string[] args)
        {
            if (c.rules == null) c.rules = new MatchSettings();
            c.port = (int)FloatArg(args, "-port", c.port);
            c.teamSize = Mathf.Clamp((int)FloatArg(args, "-teamsize", c.teamSize), 1, 2);
            c.roomName = StringArg(args, "-name") ?? c.roomName;
            c.minPlayersToStart = (int)FloatArg(args, "-minplayers", c.minPlayersToStart);
            c.startCountdown = FloatArg(args, "-countdown", c.startCountdown);
            c.rules.roundsToWinGame = (int)FloatArg(args, "-rounds", c.rules.roundsToWinGame);
            c.rules.pointsToWinRound = (int)FloatArg(args, "-points", c.rules.pointsToWinRound);
            c.rules.cardsPerPick = (int)FloatArg(args, "-cards", c.rules.cardsPerPick);
            c.rules.rerollsAtStart = (int)FloatArg(args, "-rerolls", c.rules.rerollsAtStart);
            c.rules.rerollsPerRound = (int)FloatArg(args, "-rerollsround", c.rules.rerollsPerRound);
            c.rules.pickTimeLimit = (int)FloatArg(args, "-picktime", c.rules.pickTimeLimit);
            c.rematchDelay = FloatArg(args, "-rematch", c.rematchDelay);
            c.targetFrameRate = (int)FloatArg(args, "-fps", c.targetFrameRate);
            c.statusInterval = FloatArg(args, "-status", c.statusInterval);
            c.publicAddress = StringArg(args, "-public") ?? c.publicAddress;
            c.contentDir = StringArg(args, "-content") ?? c.contentDir;
            string master = StringArg(args, "-master") ?? Environment.GetEnvironmentVariable("METADREAM_MASTER_HOST");
            if (master != null)
            {
                int colon = master.LastIndexOf(':');
                if (colon > 0 && int.TryParse(master.Substring(colon + 1), out int mp)) { c.masterHost = master.Substring(0, colon); c.masterPort = mp; }
                else c.masterHost = master;
            }
            if (Array.IndexOf(args, "-nofill") >= 0) c.fillWithCpu = false;
            if (Array.IndexOf(args, "-nomidjoin") >= 0) c.allowMidJoin = false;
            if (Array.IndexOf(args, "-nolan") >= 0) c.lanBroadcast = false;
            if (Array.IndexOf(args, "-nomaster") >= 0) c.masterServer = false;
            if (Array.IndexOf(args, "-noreplay") >= 0) c.saveReplays = false;
            if (Array.IndexOf(args, "-noupload") >= 0) c.uploadReplays = false;
        }

        private void Update()
        {
            while (_commands.TryDequeue(out var line)) Execute(line);
            if (_host == null || _quitting) return;
            var flow = GameFlow.Instance;
            float dt = Time.unscaledDeltaTime;

            if (!_host.InMatch)
            {
                int need = Config.fillWithCpu ? Config.minPlayersToStart : _host.SlotCount;
                bool ready = need > 0 && _host.RemoteCount >= Mathf.Min(need, _host.SlotCount) && _host.CanStart;
                if (!ready)
                {
                    if (_host.Countdown >= 0f) Log("countdown cancelled");
                    _host.Countdown = -1f;
                }
                else if (_host.Countdown < 0f)
                {
                    _host.Countdown = Mathf.Max(0f, Config.startCountdown);
                    Log("players ready (" + _host.RemoteCount + "), starting in " + _host.Countdown.ToString("0") + "s");
                }
                else
                {
                    _host.Countdown -= dt;
                    if (_host.Countdown <= 0f) TryStart("countdown");
                }
            }
            else if (_host.RemoteCount == 0)
            {
                Log("all players left, returning to lobby");
                _host.ReturnToLobby();
            }
            else if (flow != null && flow.Phase == MatchPhase.GameOver)
            {
                // No rematch: show the result for rematchDelay seconds, then empty the room (clients go to their title).
                if (_gameOverAt < 0f) _gameOverAt = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _gameOverAt > Mathf.Max(3f, Config.rematchDelay)) { Log("match finished, back to lobby"); _gameOverAt = -1f; _host.ReturnToLobby(); }
            }
            else if (flow != null && !flow.HasMatch)
            {
                _host.ReturnToLobby();
            }
            if (flow == null || flow.Phase != MatchPhase.GameOver) _gameOverAt = -1f;

            _statusTimer -= dt;
            if (Config.statusInterval > 0f && _statusTimer <= 0f)
            {
                _statusTimer = Config.statusInterval;
                Log("status " + _host.StatusLine() + " uptime=" + Uptime());
            }
        }

        private void TryStart(string reason)
        {
            if (_host == null || _host.InMatch) return;
            if (!_host.CanStart)
            {
                Log("start refused (" + reason + "): " + (_host.FillWithCpu ? "no players" : "room is not full"));
                return;
            }
            Log("match starting (" + reason + ")");
            _host.StartMatch(Config.ToSettings());
        }

        // ------------------------------------------------------------ console

        private void StartConsole()
        {
            _console = new Thread(() =>
            {
                try
                {
                    string line;
                    while ((line = Console.ReadLine()) != null) _commands.Enqueue(line);
                }
                catch (Exception) { }
            }) { IsBackground = true, Name = "ServerConsole" };
            _console.Start();
        }

        public void Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var parts = line.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";
            switch (cmd)
            {
                case "help":
                    Log("commands: status | start | lobby | teamsize 1|2 | rounds N | points N | rerolls START [PER_ROUND] | picktime S | countdown S | rules | master | content | quit");
                    break;
                case "picktime":
                    if (int.TryParse(arg, out int pickSeconds))
                    {
                        Config.rules.pickTimeLimit = Mathf.Clamp(pickSeconds, 0, 120);
                        _host.SetPickTime(Config.rules.pickTimeLimit);
                        Log("card pick time limit " + (Config.rules.pickTimeLimit > 0 ? Config.rules.pickTimeLimit + "s" : "unlimited") + " (next match)");
                    }
                    break;
                case "rerolls":
                {
                    var p = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length > 0 && int.TryParse(p[0], out int rs))
                    {
                        int rr = p.Length > 1 && int.TryParse(p[1], out int x) ? x : Config.rules.rerollsPerRound;
                        Config.rules.rerollsAtStart = Mathf.Clamp(rs, 0, 20);
                        Config.rules.rerollsPerRound = Mathf.Clamp(rr, 0, 10);
                        _host.SetRerolls(Config.rules.rerollsAtStart, Config.rules.rerollsPerRound);
                        Log("rerolls start=" + Config.rules.rerollsAtStart + " perRound=" + Config.rules.rerollsPerRound + " (next match)");
                    }
                }
                    break;
                case "status":
                    Log("status " + _host.StatusLine() + " uptime=" + Uptime());
                    break;
                case "rules":
                    Log("rules " + JsonUtility.ToJson(Config.rules));
                    break;
                case "master":
                    var m = _host.Master;
                    Log(m == null ? "main hosting server disabled" : "main hosting server " + m.Endpoint + " sent=" + m.PacketsSent + " replies=" + m.RepliesReceived + (m.Unreachable ? " UNREACHABLE" : "") + (m.Error != null ? " error=" + m.Error : "") + " listed=" + m.Servers.Count);
                    break;
                case "content":
                    Log("content " + Content.ContentDatabase.Cards.Count + " cards, " + Content.ContentDatabase.Maps.Count + " maps, hash " + Content.ContentDatabase.Hash + ", errors " + Content.ContentDatabase.ErrorCount);
                    foreach (var issue in Content.ContentDatabase.Issues) if (issue.Severity != Content.ContentSeverity.Info) Log("  " + issue);
                    break;
                case "start":
                    TryStart("console");
                    break;
                case "lobby":
                    _host.ReturnToLobby();
                    break;
                case "teamsize":
                    if (int.TryParse(arg, out int size)) Log(_host.SetTeamSize(size) ? "team size " + size : "team size unchanged (in match or players do not fit)");
                    break;
                case "rounds":
                    if (int.TryParse(arg, out int rounds) && rounds > 0) { Config.rules.roundsToWinGame = rounds; _host.Settings.roundsToWinGame = rounds; Log("rounds to win = " + rounds + " (next match)"); }
                    break;
                case "points":
                    if (int.TryParse(arg, out int points) && points > 0) { Config.rules.pointsToWinRound = points; _host.Settings.pointsToWinRound = points; Log("points per round = " + points + " (next match)"); }
                    break;
                case "countdown":
                    if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float s) && s >= 0f) { Config.startCountdown = s; Log("start countdown = " + s + "s"); }
                    break;
                case "quit":
                case "exit":
                case "stop":
                    Shutdown(0);
                    break;
                default:
                    Log("unknown command '" + cmd + "' (type help)");
                    break;
            }
        }

        public void Shutdown(int code)
        {
            if (_quitting) return;
            _quitting = true;
            Log("shutting down");
            if (_host != null) _host.Close();
            Application.Quit(code);
        }

        private string Uptime()
        {
            var t = TimeSpan.FromSeconds(Time.realtimeSinceStartup - _startedAt);
            return ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        private static void Log(string message) => Debug.Log("[Server] " + DateTime.Now.ToString("HH:mm:ss") + " " + message);

        private static string StringArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static float FloatArg(string[] args, string name, float fallback)
        {
            int i = Array.IndexOf(args, name);
            if (i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            return fallback;
        }

        private void OnApplicationQuit()
        {
            _quitting = true;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}

