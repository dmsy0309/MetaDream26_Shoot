using System;
using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Core;
using Metadream.Shoot.Network;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Online
{
    /// <summary>Joins a host, mirrors its snapshots and sends this player's input at up to 60 Hz.</summary>
    public sealed class NetClientSession : MonoBehaviour
    {
        public enum ClientState
        {
            Connecting,
            Lobby,
            InMatch,
            Ended
        }

        public static NetClientSession Active { get; private set; }

        private readonly TcpClientConnection _conn = new TcpClientConnection();
        private readonly List<byte[]> _messages = new List<byte[]>();
        private readonly MatchSnapshot _snapshot = new MatchSnapshot();
        private readonly NetworkInput _accumulator = new NetworkInput("local");
        private readonly Dictionary<int, ProjectileProxy> _proxies = new Dictionary<int, ProjectileProxy>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _stale = new List<int>();
        private IInputSource _local;
        private bool _helloSent;
        private float _sendTimer;
        private float _autoPickAt = -1f;
        private float _logTimer;
        private float _gameOverSeen = -1f;
        private int _pendingReroll;

        /// <summary>Mouse click on a reroll button: sent with the next input packet.</summary>
        public void QueueReroll(int index) => _pendingReroll = index + 1;
        private float _autoStartTimer = 3f;

        public ClientState State { get; private set; } = ClientState.Connecting;
        public string Target { get; private set; }
        public int MySlot { get; private set; } = -1;
        public LobbyInfo Lobby { get; private set; }
        public bool Autoplay { get; set; }
        public int SnapshotsReceived { get; private set; }
        public int InputsSent { get; private set; }
        public bool IsLeader => Lobby != null && MySlot >= 0 && Lobby.LeaderSlot == MySlot && Lobby.Dedicated;

        public void RequestStart()
        {
            if (IsLeader) _conn.Send(Protocol.Pack(MsgType.StartRequest));
        }

        public void RequestTeamSize(int teamSize)
        {
            if (IsLeader) _conn.Send(Protocol.Pack(MsgType.TeamSizeRequest, w => w.Write((byte)teamSize)));
        }

        public static NetClientSession Join(string host, int port, bool autoplay)
        {
            if (Active != null) Active.Leave(null);
            var go = new GameObject("NetClientSession");
            var s = go.AddComponent<NetClientSession>();
            s.Autoplay = autoplay;
            s.Target = host + ":" + port;
            s._conn.Connect(host, port);
            Active = s;
            Debug.Log("[Net] joining " + s.Target);
            return s;
        }

        private void Update()
        {
            _conn.Poll(_messages);
            if (_conn.State == TcpClientConnection.ConnState.Connected && !_helloSent)
            {
                _helloSent = true;
                _conn.Send(Protocol.Pack(MsgType.Hello, w =>
                {
                    w.Write(Protocol.Version);
                    w.Write(Content.ContentDatabase.Hash ?? "");
                    w.Write(PlayerProfile.Nickname);
                    w.Write(PlayerProfile.DeviceName);
                }));
            }
            if (_conn.State == TcpClientConnection.ConnState.Failed)
            {
                Leave("연결하지 못했습니다: " + _conn.Error);
                return;
            }

            foreach (var data in _messages)
            {
                if (State == ClientState.Ended) break;
                Handle(data);
            }
            _messages.Clear();
            if (State == ClientState.Ended) return;

            if (_conn.State == TcpClientConnection.ConnState.Closed)
            {
                Leave(Lobby != null && Lobby.Dedicated ? "서버와 연결이 끊어졌습니다" : "호스트와 연결이 끊어졌습니다");
                return;
            }

            if (State == ClientState.InMatch) SendInput();
            if (State == ClientState.InMatch && _snapshot.Phase == MatchPhase.GameOver)
            {
                if (_gameOverSeen < 0f) _gameOverSeen = Time.unscaledTime;
                var kb = UnityEngine.InputSystem.Keyboard.current;
                bool ok = kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
                foreach (var gp in UnityEngine.InputSystem.Gamepad.all) ok |= gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame;
                if ((ok && Time.unscaledTime - _gameOverSeen > 1.5f) || (Autoplay && Time.unscaledTime - _gameOverSeen > 3f)) { Leave(null); return; }
            }
            else _gameOverSeen = -1f;
            // Test mode: an autoplaying room leader on a dedicated server presses "start" by itself.
            if (Autoplay && State == ClientState.Lobby && IsLeader && !Lobby.InMatch)
            {
                _autoStartTimer -= Time.unscaledDeltaTime;
                if (_autoStartTimer <= 0f)
                {
                    _autoStartTimer = 3f;
                    Debug.Log("[Net] autoplay leader requests start");
                    RequestStart();
                }
            }
            _logTimer -= Time.unscaledDeltaTime;
            if (_logTimer <= 0f && State == ClientState.InMatch)
            {
                _logTimer = 5f;
                Debug.Log("[Net] client snapshots=" + SnapshotsReceived + " inputs=" + InputsSent + " phase=" + _snapshot.Phase + " score=" + _snapshot.Rounds[0] + "R" + _snapshot.Points[0] + "P:" + _snapshot.Rounds[1] + "R" + _snapshot.Points[1] + "P");
            }
        }

        private void Handle(byte[] data)
        {
            using (var r = Protocol.Body(data))
            {
                switch (Protocol.Peek(data))
                {
                    case MsgType.Welcome:
                        MySlot = r.ReadByte();
                        if (State == ClientState.Connecting) State = ClientState.Lobby;
                        Debug.Log("[Net] welcome slot " + MySlot);
                        break;
                    case MsgType.Reject:
                        Leave("입장 거절: " + r.ReadString());
                        break;
                    case MsgType.Lobby:
                        Lobby = LobbyInfo.Read(r);
                        break;
                    case MsgType.Snapshot:
                        if (MySlot < 0) break; // not welcomed yet
                        _snapshot.Read(r);
                        SnapshotsReceived++;
                        if (State != ClientState.InMatch) BeginMatch();
                        GameFlow.Instance.ApplyMirror(_snapshot);
                        SyncProjectiles();
                        break;
                    case MsgType.ToLobby:
                        if (_snapshot.Phase == MatchPhase.GameOver) { Leave(null); break; } // results are over: back to the title
                        EndMatchMirror();
                        State = ClientState.Lobby;
                        break;
                    case MsgType.Bye:
                        if (State == ClientState.InMatch && _snapshot.Phase == MatchPhase.GameOver) { Leave(null); break; }
                        Leave(Lobby != null && Lobby.Dedicated ? "서버가 종료되었습니다" : "호스트가 방을 닫았습니다");
                        break;
                }
            }
        }

        private void BeginMatch()
        {
            var names = new List<string>();
            int count = _snapshot.Fighters.Count;
            for (int i = 0; i < count; i++)
            {
                string n = Lobby != null && i < Lobby.Slots.Count && !string.IsNullOrEmpty(Lobby.Slots[i].Name) ? Lobby.Slots[i].Name : "P" + (i + 1);
                if (i == MySlot) n += " (나)";
                names.Add(n);
            }
            var flow = GameFlow.Instance;
            flow.BeginMirror(names, MySlot);
            flow.EscapeOverride = () => Leave(null);
            flow.MirrorGameOverText = null;
            if (Autoplay && MySlot >= 0 && MySlot < flow.Fighters.Count)
                _local = new BotInput(Environment.TickCount, "auto") { Self = flow.Fighters[MySlot] };
            else
                _local = new DeviceInput(KeyboardBinding.PlayerOne(), true, 0, "local");
            State = ClientState.InMatch;
            Debug.Log("[Net] match mirror begin slot=" + MySlot + " players=" + count);
        }

        private void EndMatchMirror()
        {
            foreach (var p in _proxies.Values) if (p != null) Destroy(p.gameObject);
            _proxies.Clear();
            if (State == ClientState.InMatch) GameFlow.Instance?.EndMirror();
            _local = null;
        }

        private void SyncProjectiles()
        {
            var flow = GameFlow.Instance;
            _seen.Clear();
            foreach (var s in _snapshot.Projectiles)
            {
                _seen.Add(s.Id);
                if (_proxies.TryGetValue(s.Id, out var proxy) && proxy != null) proxy.Set(s);
                else
                {
                    Color c = s.Owner >= 0 && s.Owner < flow.Fighters.Count ? flow.Fighters[s.Owner].Color : Color.white;
                    _proxies[s.Id] = ProjectileProxy.Create(s, c);
                }
            }
            _stale.Clear();
            foreach (var kv in _proxies) if (!_seen.Contains(kv.Key)) _stale.Add(kv.Key);
            foreach (var id in _stale)
            {
                if (_proxies[id] != null) Destroy(_proxies[id].gameObject);
                _proxies.Remove(id);
            }
        }

        private void SendInput()
        {
            var flow = GameFlow.Instance;
            if (_local == null || flow == null || MySlot < 0 || MySlot >= flow.Fighters.Count) return;
            var me = flow.Fighters[MySlot];
            var frame = _local.Read(new InputContext { Position = me.Position, Camera = Camera.main });
            if (_pendingReroll > 0) { frame.Reroll = _pendingReroll; _pendingReroll = 0; }
            if (Fighter.InputSuspended) { int reroll = frame.Reroll; frame = default; frame.Reroll = reroll; }

            // An autoplaying client still has to confirm its card picks on the host.
            if (Autoplay && flow.IsPicking && flow.Picker == me)
            {
                if (_autoPickAt < 0f) _autoPickAt = Time.unscaledTime + 0.8f;
                if (Time.unscaledTime >= _autoPickAt) { frame.ConfirmPressed = true; _autoPickAt = -1f; }
            }
            _accumulator.Push(frame);

            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer > 0f) return;
            _sendTimer = 1f / Protocol.InputRate;
            var packed = _accumulator.Read(default);
            _conn.Send(Protocol.Pack(MsgType.Input, w => Protocol.WriteInput(w, packed)));
            InputsSent++;
        }

        /// <summary>Leave the room. A non-null reason is shown to the player.</summary>
        public void Leave(string reason)
        {
            if (State == ClientState.Ended) return;
            Debug.Log("[Net] client leave" + (reason != null ? ": " + reason : "") + " snapshots=" + SnapshotsReceived + " inputs=" + InputsSent);
            try { _conn.Send(Protocol.Pack(MsgType.Bye)); } catch (Exception) { }
            _conn.Close();
            EndMatchMirror();
            State = ClientState.Ended;
            var flow = GameFlow.Instance;
            if (flow != null) { flow.ReturnToTitle(); flow.Role = NetRole.Offline; }
            if (Active == this) Active = null;
            if (reason != null) FrontEnd.Instance?.ShowNotice(reason);
            Destroy(gameObject);
        }

        private void OnApplicationQuit()
        {
            if (State != ClientState.Ended) Leave(null);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            _conn.Close();
        }
    }
}

