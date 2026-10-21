using System;
using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Core;
using Metadream.Shoot.Network;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Online
{
    public sealed class HostOptions
    {
        public int TeamSize = 1;
        public bool Autoplay;
        /// <summary>Headless server: nobody plays on this machine, slot 0 is open to joiners.</summary>
        public bool Dedicated;
        public int Port = Protocol.DefaultPort;
        public string RoomName = "Metadream 방";
        public bool AllowMidJoin = true;
        public bool FillWithCpu = true;
        public bool LanBroadcast = true;
        /// <summary>Register with the main hosting server (server list) every few seconds.</summary>
        public bool MasterServer = true;
        /// <summary>Address advertised to the master instead of the auto-detected LAN IP (empty = auto).</summary>
        public string PublicAddress = "";
        /// <summary>Record every game as Replays/*.mdreplay on this machine.</summary>
        public bool SaveReplays = true;
        /// <summary>Send saved replays to the main hosting server.</summary>
        public bool UploadReplays = true;
        public MatchSettings Settings = new MatchSettings();
    }

    /// <summary>
    /// Authoritative host: runs the whole simulation, receives remote inputs and streams snapshots at 30 Hz.
    /// Player-host: slot 0 is the local player. Dedicated server: every slot is open to joiners.
    /// Empty slots become CPU at start; joiners may take over a CPU slot mid-match.
    /// </summary>
    public sealed class NetHostSession : MonoBehaviour
    {
        public const int MaxSlots = 4;
        public static NetHostSession Active { get; private set; }

        private readonly TcpHostServer _server = new TcpHostServer();
        private readonly List<HostEvent> _events = new List<HostEvent>();
        private readonly SlotKind[] _kinds = new SlotKind[MaxSlots];
        private readonly string[] _names = new string[MaxSlots];
        private readonly string[] _devices = new string[MaxSlots];
        private readonly string[] _addresses = new string[MaxSlots];
        private readonly Player.CharacterLook[] _looks = new Player.CharacterLook[MaxSlots];
        private Player.CharacterLook _pendingLook;
        private Replay.ReplayRecorder _recorder;
        private int _recMatches;
        private float _finishAt = -1f;
        private readonly int[] _peerOfSlot = { -1, -1, -1, -1 };
        private readonly Dictionary<int, int> _slotOfPeer = new Dictionary<int, int>();
        private readonly NetworkInput[] _inputs = new NetworkInput[MaxSlots];
        private readonly MatchSnapshot _snapshot = new MatchSnapshot();
        private readonly System.Random _rng = new System.Random();
        private HostOptions _options;
        private LanBroadcaster _broadcaster;
        private MasterServerClient _master;
        private float _snapshotTimer;
        private float _lobbyTimer;
        private bool _lobbyDirty = true;

        public string RoomName => _options.RoomName;
        public string Code { get; private set; }
        public string Address { get; private set; }
        public int Port => _server.Port;
        public int TeamSize { get; private set; } = 1;
        public int SlotCount => TeamSize * 2;
        public bool InMatch { get; private set; }
        public bool Dedicated => _options.Dedicated;
        public bool AutoplayHost => _options.Autoplay;
        public bool FillWithCpu => _options.FillWithCpu;
        public MatchSettings Settings => _options.Settings;
        public int SnapshotsSent { get; private set; }
        public int InputsReceived { get; private set; }
        public int MatchesStarted { get; private set; }
        /// <summary>Main hosting server link (null when disabled).</summary>
        public MasterServerClient Master => _master;
        /// <summary>Seconds until an automatic start (set by DedicatedServer); negative when idle.</summary>
        public float Countdown { get; set; } = -1f;

        public event Action<string> StartRequested;

        public SlotKind KindOf(int slot) => _kinds[slot];
        public string NameOf(int slot) => _names[slot];

        public int RemoteCount
        {
            get
            {
                int n = 0;
                foreach (var k in _kinds) if (k == SlotKind.Remote) n++;
                return n;
            }
        }

        /// <summary>Who may start the match: the host player, or the lowest remote slot on a dedicated server.</summary>
        public int LeaderSlot
        {
            get
            {
                if (!Dedicated) return 0;
                for (int i = 0; i < MaxSlots; i++) if (_kinds[i] == SlotKind.Remote) return i;
                return -1;
            }
        }

        public static NetHostSession Open(int teamSize, bool autoplay, out string error)
            => Open(new HostOptions
            {
                TeamSize = teamSize,
                Autoplay = autoplay,
                MasterServer = DevSettings.UseMasterServer && Array.IndexOf(Environment.GetCommandLineArgs(), "-nomaster") < 0
            }, out error);

        public static NetHostSession Open(HostOptions options, out string error)
        {
            error = null;
            if (Active != null) Active.Close();
            var go = new GameObject("NetHostSession");
            var s = go.AddComponent<NetHostSession>();
            s._options = options ?? new HostOptions();
            int port = Mathf.Clamp(s._options.Port, 1, 65535);
            if (!s._server.Start(port, s._options.Dedicated ? 1 : 10))
            {
                error = s._options.Dedicated ? "포트 " + port + " 를 열 수 없습니다 (이미 사용 중?)" : "포트 " + port + "~" + (port + 9) + " 를 열 수 없습니다";
                Destroy(go);
                return null;
            }
            Active = s;
            s.TeamSize = Mathf.Clamp(s._options.TeamSize, 1, 2);
            var ip = LocalAddress.GetLanIPv4();
            s.Address = ip.ToString();
            s.Code = RoomCode.Encode(ip, s.Port);
            for (int i = 0; i < MaxSlots; i++) { s._kinds[i] = SlotKind.Empty; s._names[i] = ""; }
            if (!s.Dedicated)
            {
                s._kinds[0] = SlotKind.Host;
                s._names[0] = PlayerProfile.Nickname;
                s._devices[0] = PlayerProfile.DeviceName;
                s._looks[0] = PlayerProfile.Look;
                s._addresses[0] = s.Address;
            }
            if (s._options.LanBroadcast) s._broadcaster = new LanBroadcaster();
            if (s._options.MasterServer) s._master = new MasterServerClient();
            var flow = GameFlow.Instance;
            if (flow != null)
            {
                flow.Role = NetRole.Host;
                flow.DedicatedServer = s.Dedicated;
            }
            Debug.Log("[Net] " + (s.Dedicated ? "dedicated server" : "host") + " open " + s.Address + ":" + s.Port + " code=" + s.Code + " teamSize=" + s.TeamSize);
            return s;
        }

        public bool SetTeamSize(int teamSize)
        {
            teamSize = Mathf.Clamp(teamSize, 1, 2);
            if (InMatch || teamSize == TeamSize) return false;
            if (teamSize == 1)
            {
                // Compact players into slots 0/1; refuse if they would not fit.
                int occupied = 0;
                for (int i = 0; i < MaxSlots; i++) if (_kinds[i] != SlotKind.Empty) occupied++;
                if (occupied > 2) return false;
                for (int i = 2; i < MaxSlots; i++)
                {
                    if (_kinds[i] == SlotKind.Empty) continue;
                    int target = _kinds[0] == SlotKind.Empty ? 0 : 1;
                    MoveSlot(i, target);
                }
            }
            TeamSize = teamSize;
            _lobbyDirty = true;
            Debug.Log("[Net] team size " + teamSize);
            return true;
        }

        private void MoveSlot(int from, int to)
        {
            int peer = _peerOfSlot[from];
            _kinds[to] = _kinds[from];
            _names[to] = _names[from];
            _devices[to] = _devices[from];
            _looks[to] = _looks[from];
            _addresses[to] = _addresses[from];
            _peerOfSlot[to] = peer;
            _kinds[from] = SlotKind.Empty;
            _names[from] = "";
            _peerOfSlot[from] = -1;
            if (peer >= 0)
            {
                _slotOfPeer[peer] = to;
                _server.Send(peer, Protocol.Pack(MsgType.Welcome, w => w.Write((byte)to)));
            }
        }

        // ------------------------------------------------------------ loop

        private void Update()
        {
            _server.Poll(_events);
            foreach (var e in _events)
            {
                switch (e.Type)
                {
                    case HostEventType.Connected:
                        Debug.Log("[Net] peer " + e.PeerId + " connected");
                        break;
                    case HostEventType.Data:
                        Handle(e.PeerId, e.Data);
                        break;
                    case HostEventType.Disconnected:
                        OnPeerLost(e.PeerId);
                        break;
                }
            }
            _events.Clear();

            float dt = Time.unscaledDeltaTime;
            _lobbyTimer -= dt;
            if (_lobbyDirty || _lobbyTimer <= 0f)
            {
                _lobbyDirty = false;
                _lobbyTimer = Countdown >= 0f ? 0.25f : 1f;
                _server.Broadcast(Protocol.Pack(MsgType.Lobby, w => BuildLobby().Write(w)));
            }

            int humans = RemoteCount + (Dedicated ? 0 : 1);
            _broadcaster?.Tick(dt, new RoomAdvert
            {
                Name = RoomName, Port = Port, Code = Code, TeamSize = TeamSize, Players = humans, MaxPlayers = SlotCount, InMatch = InMatch
            });
            _master?.Heartbeat(dt, MasterInfo());

            if (InMatch)
            {
                var flow = GameFlow.Instance;
                if (flow == null || !flow.HasMatch) return;
                _snapshotTimer -= dt;
                if (_snapshotTimer <= 0f)
                {
                    _snapshotTimer = 1f / Protocol.SnapshotRate;
                    flow.CaptureSnapshot(_snapshot);
                    if (_recorder != null) _recorder.Add(_snapshot);
                    if (_slotOfPeer.Count > 0)
                    {
                        var payload = Protocol.Pack(MsgType.Snapshot, w => _snapshot.Write(w));
                        // Only peers that received Welcome get snapshots (they need their slot first).
                        foreach (var peer in _slotOfPeer.Keys) _server.Send(peer, payload);
                        SnapshotsSent++;
                    }
                }
                UpdateRecording(flow);
            }
        }

        // ------------------------------------------------------------ replays

        private void UpdateRecording(GameFlow flow)
        {
            if (!_options.SaveReplays) return;
            if (_recorder == null)
            {
                if (flow.Phase != MatchPhase.GameOver) BeginRecording(flow);
                return;
            }
            // One file per game: keep the game-over screen for a few seconds, then close the file.
            if (flow.MatchesCompleted != _recMatches)
            {
                if (_finishAt < 0f) _finishAt = Time.unscaledTime + 4f;
                else if (Time.unscaledTime >= _finishAt) FinishRecording();
            }
        }

        private void BeginRecording(GameFlow flow)
        {
            var meta = new Replay.ReplayMeta
            {
                roomName = RoomName,
                teamSize = TeamSize,
                dedicated = Dedicated,
                device = Replay.ReplayDevice.Current(Dedicated)
            };
            _recorder = new Replay.ReplayRecorder(meta);
            for (int i = 0; i < SlotCount; i++) _recorder.SetPlayer(PlayerInfo(i));
            _recMatches = flow.MatchesCompleted;
            _finishAt = -1f;
            Debug.Log("[Replay] recording " + meta.id);
        }

        private Replay.ReplayPlayerInfo PlayerInfo(int slot) => new Replay.ReplayPlayerInfo
        {
            slot = slot,
            team = slot % 2,
            nickname = _names[slot] ?? "",
            kind = _kinds[slot] == SlotKind.Host ? "host" : _kinds[slot] == SlotKind.Remote ? "remote" : "cpu",
            device = _kinds[slot] == SlotKind.Cpu ? "" : _devices[slot] ?? "",
            address = _kinds[slot] == SlotKind.Cpu ? "" : _addresses[slot] ?? ""
        };

        private void FinishRecording()
        {
            if (_recorder == null) return;
            var flow = GameFlow.Instance;
            int winner = flow != null && flow.Phase == MatchPhase.GameOver && flow.Match != null ? flow.Match.WinningTeam : -1;
            string score = _snapshot.Rounds[0] + ":" + _snapshot.Rounds[1];
            _recorder.Finish(winner, score, _options.UploadReplays);
            _recorder = null;
            _finishAt = -1f;
        }

        private void Handle(int peer, byte[] data)
        {
            try
            {
                using (var r = Protocol.Body(data))
                {
                    switch (Protocol.Peek(data))
                    {
                        case MsgType.Hello:
                            int version = r.ReadInt32();
                            if (version != Protocol.Version) { Reject(peer, "버전이 다릅니다 (서버 v" + Protocol.Version + ", 게임을 업데이트하세요)"); return; }
                            string hash = r.ReadString();
                            if (hash != Content.ContentDatabase.Hash)
                            {
                                Reject(peer, "카드/맵 콘텐츠가 서버와 다릅니다 (서버 " + Short(Content.ContentDatabase.Hash) + ", 내 게임 " + Short(hash) + "). 같은 Content 폴더를 사용하세요");
                                return;
                            }
                            string nick = PlayerProfile.Sanitize(r.ReadString());
                            string device = r.ReadString();
                            var look = r.BaseStream.Position + 3 <= r.BaseStream.Length ? new Player.CharacterLook(r.ReadByte(), r.ReadByte(), r.ReadByte()) : Player.CharacterLook.Default;
                            _pendingLook = look.Sanitized;
                            AssignSlot(peer, nick, device.Length > 40 ? device.Substring(0, 40) : device);
                            break;
                        case MsgType.Input:
                            if (_slotOfPeer.TryGetValue(peer, out int slot) && _inputs[slot] != null)
                            {
                                _inputs[slot].Push(Protocol.ReadInput(r));
                                InputsReceived++;
                            }
                            break;
                        case MsgType.StartRequest:
                            if (IsLeader(peer)) StartRequested?.Invoke("leader");
                            break;
                        case MsgType.TeamSizeRequest:
                            int size = r.ReadByte();
                            if (IsLeader(peer)) SetTeamSize(size);
                            break;
                        case MsgType.Bye:
                            _server.Disconnect(peer);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Net] bad message from peer " + peer + ": " + ex.Message);
                _server.Disconnect(peer);
            }
        }

        private bool IsLeader(int peer) => _slotOfPeer.TryGetValue(peer, out int slot) && slot == LeaderSlot;

        private void AssignSlot(int peer, string nick, string device)
        {
            if (_slotOfPeer.ContainsKey(peer)) return;
            int first = Dedicated ? 0 : 1;
            if (InMatch)
            {
                if (!_options.AllowMidJoin) { Reject(peer, "이미 게임이 진행 중입니다"); return; }
                // A player who left may come back from the same computer and gets their own fighter back.
                for (int i = first; i < SlotCount; i++)
                {
                    if (_kinds[i] != SlotKind.Disconnected || !string.Equals(_devices[i], device, StringComparison.OrdinalIgnoreCase)) continue;
                    TakeSlot(peer, i, nick, device);
                    _inputs[i] = new NetworkInput(_names[i]);
                    GameFlow.Instance?.ReplaceInput(i, _inputs[i], _names[i]);
                    GameFlow.Instance?.SetAbsent(i, false);
                    Debug.Log("[Net] peer " + peer + " rejoined slot " + i + " from " + device);
                    return;
                }
                for (int i = first; i < SlotCount; i++)
                {
                    if (_kinds[i] != SlotKind.Cpu) continue;
                    // Take over a CPU-controlled fighter mid-match.
                    string before = _names[i];
                    TakeSlot(peer, i, nick, device);
                    _inputs[i] = new NetworkInput(_names[i]);
                    GameFlow.Instance?.ReplaceInput(i, _inputs[i], _names[i]);
                    if (_recorder != null)
                    {
                        var info = PlayerInfo(i);
                        info.nickname = before + " → " + _names[i];
                        _recorder.SetPlayer(info);
                    }
                    Debug.Log("[Net] peer " + peer + " took over CPU slot " + i + " mid-match");
                    return;
                }
                bool reserved = false;
                for (int i = first; i < SlotCount; i++) reserved |= _kinds[i] == SlotKind.Disconnected;
                Reject(peer, reserved ? "진행 중인 게임의 빈 자리는 나간 플레이어의 컴퓨터에서만 다시 들어올 수 있습니다" : "진행 중인 게임에 빈 자리가 없습니다");
                return;
            }
            for (int i = first; i < SlotCount; i++)
            {
                if (_kinds[i] != SlotKind.Empty) continue;
                TakeSlot(peer, i, nick, device);
                Debug.Log("[Net] peer " + peer + " joined slot " + i);
                return;
            }
            Reject(peer, "방이 가득 찼습니다");
        }

        private void TakeSlot(int peer, int slot, string nick, string device)
        {
            _kinds[slot] = SlotKind.Remote;
            _names[slot] = UniqueName(nick ?? "P" + (slot + 1), slot);
            _devices[slot] = device ?? "";
            _looks[slot] = _pendingLook;
            _addresses[slot] = _server.RemoteOf(peer);
            _peerOfSlot[slot] = peer;
            _slotOfPeer[peer] = slot;
            _server.Send(peer, Protocol.Pack(MsgType.Welcome, w => w.Write((byte)slot)));
            _lobbyDirty = true;
            Debug.Log("[Net] slot " + slot + " = " + _names[slot] + " (" + _devices[slot] + ", " + _addresses[slot] + ")");
        }

        private string UniqueName(string name, int slot)
        {
            string result = name;
            for (int n = 2; ; n++)
            {
                bool taken = false;
                for (int i = 0; i < MaxSlots; i++)
                    if (i != slot && _kinds[i] != SlotKind.Empty && _kinds[i] != SlotKind.Cpu && _names[i] == result) taken = true;
                if (!taken) return result;
                result = name + n;
            }
        }

        private void Reject(int peer, string reason)
        {
            _server.Send(peer, Protocol.Pack(MsgType.Reject, w => w.Write(reason)));
            _server.Disconnect(peer);
            Debug.Log("[Net] rejected peer " + peer + ": " + reason);
        }

        private void OnPeerLost(int peer)
        {
            if (!_slotOfPeer.TryGetValue(peer, out int slot)) return;
            _slotOfPeer.Remove(peer);
            _peerOfSlot[slot] = -1;
            Debug.Log("[Net] peer " + peer + " left slot " + slot);
            if (InMatch)
            {
                // No bot takes over: the slot waits for the same computer to reconnect.
                _kinds[slot] = SlotKind.Disconnected;
                _inputs[slot] = null;
                GameFlow.Instance?.SetAbsent(slot, true);
                CheckForfeit();
            }
            else
            {
                _kinds[slot] = SlotKind.Empty;
                _names[slot] = "";
            }
            _lobbyDirty = true;
        }

        private LobbyInfo BuildLobby()
        {
            var l = new LobbyInfo
            {
                RoomName = RoomName, Code = Code, TeamSize = TeamSize, InMatch = InMatch, Dedicated = Dedicated,
                LeaderSlot = LeaderSlot, Countdown = Countdown, PointsToWin = Settings.pointsToWinRound,
                RoundsToWin = Settings.roundsToWinGame, FillWithCpu = FillWithCpu
                , RerollsAtStart = Settings.rerollsAtStart, RerollsPerRound = Settings.rerollsPerRound
                , PickTimeLimit = Settings.pickTimeLimit
            };
            for (int i = 0; i < SlotCount; i++) l.Slots.Add(new LobbySlot { Kind = _kinds[i], Name = _names[i] });
            return l;
        }

        // ------------------------------------------------------------ match

        public bool CanStart => !InMatch && (FillWithCpu ? (Dedicated ? RemoteCount > 0 : true) : RemoteCount + (Dedicated ? 0 : 1) >= SlotCount);

        /// <summary>Room setting: card rerolls at game start and added per round (applies to the next match).</summary>
        public void SetRerolls(int atStart, int perRound)
        {
            if (InMatch) return;
            Settings.rerollsAtStart = Mathf.Clamp(atStart, 0, 20);
            Settings.rerollsPerRound = Mathf.Clamp(perRound, 0, 10);
            _lobbyDirty = true;
        }

        /// <summary>Room setting: seconds to pick a card, 0 = unlimited (applies to the next match).</summary>
        public void SetPickTime(int seconds)
        {
            if (InMatch) return;
            Settings.pickTimeLimit = Mathf.Clamp(seconds, 0, 120);
            _lobbyDirty = true;
        }

        public bool StartMatch(MatchSettings settings = null, int? seed = null)
        {
            var flow = GameFlow.Instance;
            if (flow == null || !CanStart) return false;
            var slots = new List<SlotSpec>();
            for (int i = 0; i < SlotCount; i++)
            {
                IInputSource input;
                if (_kinds[i] == SlotKind.Host)
                {
                    input = AutoplayHost ? (IInputSource)new BotInput(_rng.Next(), "P1") : new DeviceInput(KeyboardBinding.PlayerOne(), true, 0, "P1");
                    if (string.IsNullOrEmpty(_names[i])) _names[i] = PlayerProfile.Nickname;
                }
                else if (_kinds[i] == SlotKind.Remote)
                {
                    _inputs[i] = new NetworkInput("P" + (i + 1));
                    input = _inputs[i];
                }
                else
                {
                    _kinds[i] = SlotKind.Cpu;
                    _names[i] = "CPU " + (i + 1);
                    input = new BotInput(_rng.Next(), _names[i]);
                }
                string name = _names[i];
                Player.CharacterLook? look = _kinds[i] == SlotKind.Host ? PlayerProfile.Look : _kinds[i] == SlotKind.Remote ? _looks[i] : (Player.CharacterLook?)null;
                slots.Add(new SlotSpec { Name = name, Team = i % 2, Color = Palette.SlotColor(i), Input = input, Look = look });
            }
            InMatch = true;
            Countdown = -1f;
            MatchesStarted++;
            // Push the final lobby (names) before the first snapshot so clients can build their mirror.
            _server.Broadcast(Protocol.Pack(MsgType.Lobby, w => BuildLobby().Write(w)));
            _lobbyDirty = false;
            Fx.Outbox = new List<FxEvent>();
            flow.Role = NetRole.Host;
            flow.StartWithSlots(GameMode.Online, slots, settings ?? Settings, seed ?? Environment.TickCount);
            flow.EscapeOverride = ReturnToLobby;
            _snapshotTimer = 0f;
            Debug.Log("[Net] match start #" + MatchesStarted + " slots=" + SlotCount + " remote=" + RemoteCount);
            return true;
        }

        public void ReturnToLobby()
        {
            if (!InMatch) return;
            FinishRecording();
            InMatch = false;
            Fx.Outbox = null;
            var flow = GameFlow.Instance;
            if (flow != null) flow.ReturnToTitle();
            for (int i = 0; i < MaxSlots; i++)
            {
                _inputs[i] = null;
                if (_kinds[i] == SlotKind.Cpu || _kinds[i] == SlotKind.Disconnected) { _kinds[i] = SlotKind.Empty; _names[i] = ""; }
            }
            _server.Broadcast(Protocol.Pack(MsgType.ToLobby));
            _lobbyDirty = true;
            Debug.Log("[Net] back to lobby");
        }

        public void Close()
        {
            if (Active == this) Active = null;
            Debug.Log("[Net] host close snapshots=" + SnapshotsSent + " inputs=" + InputsReceived);
            FinishRecording();
            try { _server.Broadcast(Protocol.Pack(MsgType.Bye)); } catch (Exception) { }
            _server.Stop();
            _broadcaster?.Dispose();
            _broadcaster = null;
            if (_master != null)
            {
                _master.Unregister(MasterInfo());
                _master.Dispose();
                _master = null;
            }
            Fx.Outbox = null;
            var flow = GameFlow.Instance;
            if (flow != null)
            {
                if (InMatch) flow.ReturnToTitle();
                flow.Role = NetRole.Offline;
                flow.DedicatedServer = false;
            }
            InMatch = false;
            Destroy(gameObject);
        }

        private static string Short(string hash) => string.IsNullOrEmpty(hash) ? "?" : hash.Substring(0, Math.Min(8, hash.Length));

        /// <summary>When every human of one team has left, the other team wins at once.</summary>
        private void CheckForfeit()
        {
            var flow = GameFlow.Instance;
            if (flow == null || !flow.HasMatch || flow.Phase == MatchPhase.GameOver) return;
            var had = new bool[2];
            var has = new bool[2];
            for (int i = 0; i < SlotCount; i++)
            {
                int t = i % 2;
                if (_kinds[i] == SlotKind.Host || _kinds[i] == SlotKind.Remote) had[t] = has[t] = true;
                else if (_kinds[i] == SlotKind.Disconnected) had[t] = true;
            }
            for (int t = 0; t < 2; t++)
            {
                if (!had[t] || has[t] || !has[1 - t]) continue;
                flow.Forfeit(1 - t, "상대 팀이 모두 나갔습니다 · 확인(Enter/Space): 메인 화면으로");
                Debug.Log("[Net] team " + t + " left, team " + (1 - t) + " wins");
                return;
            }
        }

        /// <summary>What this room reports to the main hosting server: ip, port, current and max players.</summary>
        public MasterPacket MasterInfo()
        {
            bool pinned = !string.IsNullOrWhiteSpace(_options.PublicAddress);
            return new MasterPacket
            {
                ip = pinned ? _options.PublicAddress.Trim() : Address,
                ipExplicit = pinned,
                port = Port,
                players = RemoteCount + (Dedicated ? 0 : 1),
                maxPlayers = SlotCount,
                name = RoomName,
                teamSize = TeamSize,
                inMatch = InMatch,
                dedicated = Dedicated,
                code = Code,
                contentHash = Content.ContentDatabase.Hash
            };
        }

        /// <summary>One-line state for server consoles and logs.</summary>
        public string StatusLine()
        {
            var flow = GameFlow.Instance;
            var slots = new System.Text.StringBuilder();
            for (int i = 0; i < SlotCount; i++)
            {
                if (i > 0) slots.Append(", ");
                slots.Append(i).Append(':').Append(_kinds[i]);
            }
            string match = InMatch && flow != null
                ? " phase=" + flow.Phase + " score=" + flow.TeamRounds(0) + "R" + flow.TeamPoints(0) + "P-" + flow.TeamRounds(1) + "R" + flow.TeamPoints(1) + "P"
                : Countdown >= 0f ? " countdown=" + Countdown.ToString("0.0") : "";
            return (InMatch ? "match" : "lobby") + " " + TeamSize + "v" + TeamSize + " players=" + RemoteCount + "/" + SlotCount
                + " [" + slots + "]" + match + " port=" + Port + " code=" + Code + " snapshots=" + SnapshotsSent;
        }

        private void OnApplicationQuit()
        {
            if (Active == this) Close();
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            _server.Stop();
            _broadcaster?.Dispose();
        }
    }
}

