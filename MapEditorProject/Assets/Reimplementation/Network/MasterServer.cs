using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Metadream.Shoot.Network
{
    /// <summary>
    /// Main hosting (master) server endpoint. Hardcoded on purpose: every client and every server talks to
    /// this address. The dedicated-server config may override it (masterServer.host / port) for testing.
    /// Service source: MasterServer/master_server.py (UDP JSON, see Docs/MASTER_SERVER.md).
    /// </summary>
    public static class MasterServerEndpoint
    {
        /// <summary>Public IP of the main hosting server (not the tailnet address), so PCs outside the tailnet work too.</summary>
        public const string DefaultHost = "203.0.113.10";
        public const int DefaultPort = 47800;
        public const float HeartbeatInterval = 5f;
        public const float QueryInterval = 3f;
        public const string Game = "metadream-shoot";

        public static string Host = DefaultHost;
        public static int Port = DefaultPort;
    }

    /// <summary>Wire format sent to the master (JsonUtility, one UDP datagram).</summary>
    [Serializable]
    public sealed class MasterPacket
    {
        public string type = "query";
        public string game = MasterServerEndpoint.Game;
        public int version = Protocol.Version;
        /// <summary>IP the server is hosting on (LAN address, or the configured public address).</summary>
        public string ip = "";
        /// <summary>True when ip was configured explicitly (publicAddress) and must be used as-is.</summary>
        public bool ipExplicit;
        public int port;
        public int players;
        public int maxPlayers;
        public string name = "";
        public int teamSize = 1;
        public bool inMatch;
        public bool dedicated;
        public string code = "";
        public string contentHash = "";
    }

    [Serializable]
    public sealed class MasterServerEntry
    {
        /// <summary>Address to connect to (observed by the master unless the server pinned one).</summary>
        public string address = "";
        public string ip = "";
        public string observedIp = "";
        public int port;
        public int players;
        public int maxPlayers;
        public string name = "";
        public int teamSize = 1;
        public bool inMatch;
        public bool dedicated;
        public string code = "";
        public int version;
        public string contentHash = "";
        public float age;

        public bool Compatible => version == Protocol.Version;
        public bool Full => maxPlayers > 0 && players >= maxPlayers;

        public string Label => (dedicated ? "[서버] " : "[방] ") + name + "  ·  " + teamSize + "대" + teamSize + "  ·  " + players + "/" + maxPlayers
                               + (inMatch ? "  ·  게임 중" : "") + (Compatible ? "" : "  ·  버전 다름 v" + version) + "  ·  포트 " + port;
    }

    [Serializable]
    public sealed class MasterReply
    {
        public string type = "";
        public long time;
        public int total;
        public MasterServerEntry[] servers = new MasterServerEntry[0];
    }

    /// <summary>
    /// Non-blocking UDP client for the master server. Hosts call Heartbeat() every frame (it rate-limits itself
    /// to HeartbeatInterval); browsers call Query(). Every reply refreshes Servers with the master's full list.
    /// </summary>
    public sealed class MasterServerClient : IDisposable
    {
        private UdpClient _udp;
        private IPEndPoint _endpoint;
        private float _timer;
        private float _sentAt = -1f;
        private bool _registered;
        private string _lastLoggedError;

        public readonly List<MasterServerEntry> Servers = new List<MasterServerEntry>();
        public string Error { get; private set; }
        public float LastReplyTime { get; private set; } = -1f;
        public int RepliesReceived { get; private set; }
        public int PacketsSent { get; private set; }
        public string Endpoint => MasterServerEndpoint.Host + ":" + MasterServerEndpoint.Port;

        /// <summary>Waiting for a reply for more than 4 s counts as unreachable.</summary>
        public bool Unreachable => _sentAt >= 0f && Time.unscaledTime - _sentAt > 4f && (LastReplyTime < _sentAt);

        public MasterServerClient()
        {
            try
            {
                if (!IPAddress.TryParse(MasterServerEndpoint.Host, out var ip))
                {
                    var found = Dns.GetHostAddresses(MasterServerEndpoint.Host);
                    ip = Array.Find(found, a => a.AddressFamily == AddressFamily.InterNetwork) ?? found[0];
                }
                _endpoint = new IPEndPoint(ip, MasterServerEndpoint.Port);
                _udp = new UdpClient(AddressFamily.InterNetwork);
                _udp.Client.Blocking = false;
                Debug.Log("[Master] endpoint " + _endpoint);
            }
            catch (Exception e)
            {
                SetError(e.Message);
                _udp = null;
            }
        }

        private void SetError(string message)
        {
            Error = message;
            if (message != _lastLoggedError) Debug.LogWarning("[Master] " + Endpoint + " error: " + message);
            _lastLoggedError = message;
        }

        /// <summary>Send a register heartbeat at most every HeartbeatInterval seconds and process replies.</summary>
        public void Heartbeat(float deltaTime, MasterPacket info)
        {
            Receive();
            _timer -= deltaTime;
            if (_timer > 0f) return;
            _timer = MasterServerEndpoint.HeartbeatInterval;
            info.type = "register";
            Send(info);
            _registered = true;
        }

        /// <summary>Ask for the list every QueryInterval seconds (browsing clients).</summary>
        public void Query(float deltaTime)
        {
            Receive();
            _timer -= deltaTime;
            if (_timer > 0f) return;
            _timer = MasterServerEndpoint.QueryInterval;
            Send(new MasterPacket { type = "query" });
        }

        public void ForceNext() => _timer = 0f;

        public void Unregister(MasterPacket info)
        {
            if (!_registered) return;
            info.type = "unregister";
            Send(info);
            _registered = false;
        }

        public static string Encode(MasterPacket p) => JsonUtility.ToJson(p);

        public static MasterReply Decode(string json)
        {
            try
            {
                var reply = JsonUtility.FromJson<MasterReply>(json);
                return reply != null && reply.type == "list" ? reply : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void Send(MasterPacket p)
        {
            if (_udp == null) return;
            try
            {
                var data = Encoding.UTF8.GetBytes(Encode(p));
                _udp.Send(data, data.Length, _endpoint);
                PacketsSent++;
                if (PacketsSent == 1 || p.type != "query" && p.type != "register") Debug.Log("[Master] sent " + p.type + " to " + Endpoint + (p.port > 0 ? " (" + p.ip + ":" + p.port + " players " + p.players + "/" + p.maxPlayers + ")" : ""));
                if (_sentAt < 0f || LastReplyTime >= _sentAt) _sentAt = Time.unscaledTime;
            }
            catch (Exception e)
            {
                SetError(e.Message);
            }
        }

        private void Receive()
        {
            if (_udp == null) return;
            try
            {
                while (_udp.Available > 0)
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    var data = _udp.Receive(ref ep);
                    var reply = Decode(Encoding.UTF8.GetString(data));
                    if (reply == null) continue;
                    Servers.Clear();
                    if (reply.servers != null) Servers.AddRange(reply.servers);
                    LastReplyTime = Time.unscaledTime;
                    RepliesReceived++;
                    if (RepliesReceived == 1) Debug.Log("[Master] list from " + Endpoint + ": " + Servers.Count + " server(s)");
                    Error = null;
                    _lastLoggedError = null;
                }
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock) { }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset) { }
            catch (Exception e) { SetError(e.Message); }
        }

        public void Dispose()
        {
            try { _udp?.Close(); } catch (Exception) { }
            _udp = null;
        }
    }
}
