using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Metadream.Shoot.Network
{
    /// <summary>Length-prefixed framing over a TCP byte stream: [int32 little-endian length][payload].</summary>
    public sealed class FrameBuffer
    {
        public const int MaxFrame = 1 << 20;
        private byte[] _buf = new byte[64 * 1024];
        private int _len;

        public static byte[] Frame(byte[] payload)
        {
            var framed = new byte[payload.Length + 4];
            framed[0] = (byte)payload.Length;
            framed[1] = (byte)(payload.Length >> 8);
            framed[2] = (byte)(payload.Length >> 16);
            framed[3] = (byte)(payload.Length >> 24);
            Buffer.BlockCopy(payload, 0, framed, 4, payload.Length);
            return framed;
        }

        /// <summary>Returns false on a malformed stream.</summary>
        public bool Append(byte[] data, int count, List<byte[]> output)
        {
            if (_len + count > _buf.Length)
            {
                if (_len + count > MaxFrame + 4) return false;
                Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + count));
            }
            Buffer.BlockCopy(data, 0, _buf, _len, count);
            _len += count;
            int offset = 0;
            while (_len - offset >= 4)
            {
                int n = _buf[offset] | (_buf[offset + 1] << 8) | (_buf[offset + 2] << 16) | (_buf[offset + 3] << 24);
                if (n < 0 || n > MaxFrame) return false;
                if (_len - offset < 4 + n) break;
                var frame = new byte[n];
                Buffer.BlockCopy(_buf, offset + 4, frame, 0, n);
                output.Add(frame);
                offset += 4 + n;
            }
            if (offset > 0)
            {
                Buffer.BlockCopy(_buf, offset, _buf, 0, _len - offset);
                _len -= offset;
            }
            return true;
        }
    }

    /// <summary>One TCP connection, polled from the main thread (no worker threads).</summary>
    public sealed class TcpPeer
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly FrameBuffer _frames = new FrameBuffer();
        private readonly byte[] _read = new byte[32 * 1024];

        public TcpPeer(int id, TcpClient client)
        {
            Id = id;
            _client = client;
            _client.NoDelay = true;
            _client.SendTimeout = 3000;
            _stream = client.GetStream();
            try { Remote = client.Client.RemoteEndPoint?.ToString(); } catch { Remote = "?"; }
        }

        public int Id { get; }
        public string Remote { get; }
        public bool Connected { get; private set; } = true;

        public void Send(byte[] payload)
        {
            if (!Connected) return;
            try
            {
                var framed = FrameBuffer.Frame(payload);
                _stream.Write(framed, 0, framed.Length);
            }
            catch (Exception)
            {
                Close();
            }
        }

        public void Poll(List<byte[]> output)
        {
            if (!Connected) return;
            try
            {
                while (_client.Available > 0)
                {
                    int n = _stream.Read(_read, 0, Math.Min(_read.Length, _client.Available));
                    if (n <= 0) { Close(); return; }
                    if (!_frames.Append(_read, n, output)) { Close(); return; }
                }
                // A readable socket with nothing available means the remote side closed.
                if (_client.Client.Poll(0, SelectMode.SelectRead) && _client.Available == 0) Close();
            }
            catch (Exception)
            {
                Close();
            }
        }

        public void Close()
        {
            if (!Connected) return;
            Connected = false;
            try { _client.Close(); } catch (Exception) { }
        }
    }

    public enum HostEventType
    {
        Connected,
        Data,
        Disconnected
    }

    public struct HostEvent
    {
        public HostEventType Type;
        public int PeerId;
        public byte[] Data;
    }

    public sealed class TcpHostServer
    {
        private readonly Dictionary<int, TcpPeer> _peers = new Dictionary<int, TcpPeer>();
        private readonly List<byte[]> _scratch = new List<byte[]>();
        private readonly List<int> _ids = new List<int>();
        private TcpListener _listener;
        private int _nextId = 1;

        public int Port { get; private set; }
        public bool Running { get; private set; }
        public int PeerCount => _peers.Count;

        /// <summary>Remote address of a connected peer ("" when unknown).</summary>
        public string RemoteOf(int peerId) => _peers.TryGetValue(peerId, out var p) && p != null ? p.Remote : "";

        public bool Start(int preferredPort, int attempts = 10)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Any, preferredPort + i);
                    _listener.Start();
                    Port = preferredPort + i;
                    Running = true;
                    return true;
                }
                catch (SocketException)
                {
                    _listener = null;
                }
            }
            return false;
        }

        public void Poll(List<HostEvent> events)
        {
            if (!Running) return;
            try
            {
                while (_listener.Pending())
                {
                    var peer = new TcpPeer(_nextId++, _listener.AcceptTcpClient());
                    _peers.Add(peer.Id, peer);
                    events.Add(new HostEvent { Type = HostEventType.Connected, PeerId = peer.Id });
                }
            }
            catch (Exception) { }

            _ids.Clear();
            _ids.AddRange(_peers.Keys);
            foreach (var id in _ids)
            {
                var peer = _peers[id];
                _scratch.Clear();
                peer.Poll(_scratch);
                foreach (var data in _scratch) events.Add(new HostEvent { Type = HostEventType.Data, PeerId = id, Data = data });
                if (!peer.Connected)
                {
                    _peers.Remove(id);
                    events.Add(new HostEvent { Type = HostEventType.Disconnected, PeerId = id });
                }
            }
        }

        public void Send(int peerId, byte[] payload)
        {
            if (_peers.TryGetValue(peerId, out var peer)) peer.Send(payload);
        }

        public void Broadcast(byte[] payload)
        {
            foreach (var peer in _peers.Values) peer.Send(payload);
        }

        public void Disconnect(int peerId)
        {
            if (_peers.TryGetValue(peerId, out var peer)) peer.Close();
        }

        public void Stop()
        {
            foreach (var peer in _peers.Values) peer.Close();
            _peers.Clear();
            if (_listener != null) { try { _listener.Stop(); } catch (Exception) { } }
            _listener = null;
            Running = false;
        }
    }

    public sealed class TcpClientConnection
    {
        public enum ConnState
        {
            Idle,
            Connecting,
            Connected,
            Failed,
            Closed
        }

        private TcpClient _client;
        private IAsyncResult _pending;
        private DateTime _deadline;
        private TcpPeer _peer;

        public ConnState State { get; private set; } = ConnState.Idle;
        public string Error { get; private set; }

        public void Connect(string host, int port, double timeoutSeconds = 6)
        {
            _client = new TcpClient(AddressFamily.InterNetwork);
            _deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            State = ConnState.Connecting;
            try
            {
                _pending = IPAddress.TryParse(host, out var ip)
                    ? _client.BeginConnect(ip, port, null, null)
                    : _client.BeginConnect(host, port, null, null);
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        public void Poll(List<byte[]> output)
        {
            if (State == ConnState.Connecting)
            {
                if (_pending != null && _pending.IsCompleted)
                {
                    try
                    {
                        _client.EndConnect(_pending);
                        _peer = new TcpPeer(0, _client);
                        State = ConnState.Connected;
                    }
                    catch (Exception e)
                    {
                        Fail(e.Message);
                    }
                }
                else if (DateTime.UtcNow > _deadline)
                {
                    Fail("응답 시간 초과");
                }
                return;
            }
            if (State == ConnState.Connected)
            {
                _peer.Poll(output);
                if (!_peer.Connected) State = ConnState.Closed;
            }
        }

        public void Send(byte[] payload)
        {
            if (State == ConnState.Connected) _peer.Send(payload);
        }

        public void Close()
        {
            if (_peer != null) _peer.Close();
            else if (_client != null) { try { _client.Close(); } catch (Exception) { } }
            if (State == ConnState.Connecting || State == ConnState.Connected) State = ConnState.Closed;
        }

        private void Fail(string message)
        {
            Error = message;
            State = ConnState.Failed;
            try { _client?.Close(); } catch (Exception) { }
        }
    }

    public sealed class RoomAdvert
    {
        public string Name;
        public string Host;
        public int Port;
        public string Code;
        public int TeamSize;
        public int Players;
        public int MaxPlayers;
        public bool InMatch;
        public float LastSeen;

        public string Label => Name + "  ·  " + TeamSize + "대" + TeamSize + "  ·  " + Players + "/" + MaxPlayers + (InMatch ? "  ·  게임 중" : "") + "  ·  포트 " + Port;
    }

    /// <summary>LAN room discovery over UDP broadcast (port 47777). Text payload, one datagram per second.</summary>
    public static class LanDiscovery
    {
        public const int Port = 47777;
        public const string Magic = "MDSHOOT1";

        public static byte[] Encode(RoomAdvert a)
        {
            string s = string.Join("|", Magic, a.Name.Replace("|", "/"), a.Port, a.Code, a.TeamSize, a.Players, a.MaxPlayers, a.InMatch ? 1 : 0);
            return Encoding.UTF8.GetBytes(s);
        }

        public static RoomAdvert Decode(byte[] data, string senderHost)
        {
            try
            {
                var parts = Encoding.UTF8.GetString(data).Split('|');
                if (parts.Length < 8 || parts[0] != Magic) return null;
                return new RoomAdvert
                {
                    Name = parts[1],
                    Port = int.Parse(parts[2]),
                    Code = parts[3],
                    TeamSize = int.Parse(parts[4]),
                    Players = int.Parse(parts[5]),
                    MaxPlayers = int.Parse(parts[6]),
                    InMatch = parts[7] == "1",
                    Host = senderHost
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public sealed class LanBroadcaster : IDisposable
    {
        private UdpClient _udp;
        private float _timer;

        public LanBroadcaster()
        {
            try { _udp = new UdpClient { EnableBroadcast = true }; } catch (Exception) { _udp = null; }
        }

        public void Tick(float deltaTime, RoomAdvert advert)
        {
            if (_udp == null) return;
            _timer -= deltaTime;
            if (_timer > 0f) return;
            _timer = 1f;
            var data = LanDiscovery.Encode(advert);
            try { _udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, LanDiscovery.Port)); } catch (Exception) { }
            try { _udp.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, LanDiscovery.Port)); } catch (Exception) { }
        }

        public void Dispose()
        {
            try { _udp?.Close(); } catch (Exception) { }
            _udp = null;
        }
    }

    public sealed class LanListener : IDisposable
    {
        private UdpClient _udp;
        public readonly List<RoomAdvert> Rooms = new List<RoomAdvert>();
        public string Error { get; private set; }

        public LanListener()
        {
            try
            {
                _udp = new UdpClient { ExclusiveAddressUse = false };
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, LanDiscovery.Port));
            }
            catch (Exception e)
            {
                Error = e.Message;
                _udp = null;
            }
        }

        public void Poll(float now)
        {
            if (_udp != null)
            {
                try
                {
                    while (_udp.Available > 0)
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        var data = _udp.Receive(ref ep);
                        var advert = LanDiscovery.Decode(data, ep.Address.ToString());
                        if (advert == null) continue;
                        advert.LastSeen = now;
                        int existing = Rooms.FindIndex(r => r.Code == advert.Code);
                        if (existing >= 0) Rooms[existing] = advert;
                        else Rooms.Add(advert);
                    }
                }
                catch (Exception) { }
            }
            Rooms.RemoveAll(r => now - r.LastSeen > 3.5f);
        }

        public void Dispose()
        {
            try { _udp?.Close(); } catch (Exception) { }
            _udp = null;
        }
    }

    public static class LocalAddress
    {
        /// <summary>Best-effort LAN IPv4 of this machine (private ranges preferred).</summary>
        public static IPAddress GetLanIPv4()
        {
            IPAddress fallback = null;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        var a = ua.Address;
                        if (a.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a)) continue;
                        var b = a.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;
                        bool isPrivate = b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
                        if (isPrivate) return a;
                        if (fallback == null) fallback = a;
                    }
                }
            }
            catch (Exception) { }
            if (fallback != null) return fallback;
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("10.255.255.255", 9);
                    return ((IPEndPoint)s.LocalEndPoint).Address;
                }
            }
            catch (Exception)
            {
                return IPAddress.Loopback;
            }
        }
    }

    /// <summary>10-character room code = IPv4 (32 bits) + port (16 bits) in Crockford base32, e.g. "60R0G-01Y71".</summary>
    public static class RoomCode
    {
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static string Encode(IPAddress ip, int port)
        {
            var b = ip.GetAddressBytes();
            ulong v = ((ulong)b[0] << 40) | ((ulong)b[1] << 32) | ((ulong)b[2] << 24) | ((ulong)b[3] << 16) | (ushort)(port & 0xFFFF);
            var chars = new char[10];
            for (int i = 9; i >= 0; i--)
            {
                chars[i] = Alphabet[(int)(v & 31)];
                v >>= 5;
            }
            return new string(chars, 0, 5) + "-" + new string(chars, 5, 5);
        }

        public static bool TryDecode(string code, out string host, out int port)
        {
            host = null;
            port = 0;
            if (string.IsNullOrEmpty(code)) return false;
            var clean = new StringBuilder();
            foreach (char c in code.ToUpperInvariant())
            {
                if (c == '-' || c == ' ') continue;
                char m = c == 'O' ? '0' : (c == 'I' || c == 'L') ? '1' : c;
                clean.Append(m);
            }
            if (clean.Length != 10) return false;
            ulong v = 0;
            foreach (char c in clean.ToString())
            {
                int d = Alphabet.IndexOf(c);
                if (d < 0) return false;
                v = (v << 5) | (ulong)(uint)d;
            }
            if (v >> 48 != 0) return false;
            port = (int)(v & 0xFFFF);
            host = ((v >> 40) & 255) + "." + ((v >> 32) & 255) + "." + ((v >> 24) & 255) + "." + ((v >> 16) & 255);
            return port > 0;
        }

        /// <summary>Accepts a room code, "ip", "ip:port" or a host name.</summary>
        public static bool TryParseTarget(string text, out string host, out int port)
        {
            host = null;
            port = Protocol.DefaultPort;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.IndexOf('.') < 0 && TryDecode(text, out host, out port)) return true;
            port = Protocol.DefaultPort;
            int colon = text.LastIndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(text.Substring(colon + 1), out port) || port <= 0 || port > 65535) return false;
                text = text.Substring(0, colon);
            }
            if (text.Length == 0 || text.IndexOf(' ') >= 0) return false;
            host = text.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : text;
            return true;
        }
    }
}

