using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Metadream.Shoot.Network
{
    public enum NetworkChannel : byte
    {
        State = 0,
        Event = 1
    }

    /// <summary>
    /// Transport boundary (§7.3). This build ships only LocalTransport; an online adapter (e.g. a separate
    /// Photon project) is future work and is NOT wire-compatible with the original service.
    /// </summary>
    public interface INetworkTransport
    {
        bool IsConnected { get; }
        void Send(NetworkChannel channel, byte[] payload);
        /// <summary>Delivers queued messages to Received; call once per frame.</summary>
        void Poll();
        event Action<NetworkChannel, byte[]> Received;
    }

    /// <summary>In-process loopback pair used by tests and for future host/client wiring.</summary>
    public sealed class LocalTransport : INetworkTransport
    {
        private readonly Queue<KeyValuePair<NetworkChannel, byte[]>> _inbox = new Queue<KeyValuePair<NetworkChannel, byte[]>>();
        private LocalTransport _peer;

        public bool IsConnected => _peer != null;
        public event Action<NetworkChannel, byte[]> Received;

        public static void CreatePair(out LocalTransport a, out LocalTransport b)
        {
            a = new LocalTransport();
            b = new LocalTransport();
            a._peer = b;
            b._peer = a;
        }

        public void Send(NetworkChannel channel, byte[] payload)
        {
            if (_peer == null) throw new InvalidOperationException("Not connected");
            var copy = new byte[payload.Length];
            Buffer.BlockCopy(payload, 0, copy, 0, payload.Length);
            _peer._inbox.Enqueue(new KeyValuePair<NetworkChannel, byte[]>(channel, copy));
        }

        public void Poll()
        {
            while (_inbox.Count > 0)
            {
                var m = _inbox.Dequeue();
                Received?.Invoke(m.Key, m.Value);
            }
        }

        public void Disconnect()
        {
            if (_peer != null) _peer._peer = null;
            _peer = null;
        }
    }

    /// <summary>
    /// Field order of Photon.Pun.SyncPlayerMovement.OnPhotonSerializeView (0x06000a0a):
    /// position, direction, aimDirection, jumpIsPressed, velocity, didJump, sinceGrounded, time delta.
    /// </summary>
    public struct PlayerSyncPackage
    {
        public const float MaxTimeDelta = 0.1f;
        public Vector2 Position;
        public Vector2 Direction;
        public Vector2 AimDirection;
        public bool JumpIsPressed;
        public Vector2 Velocity;
        public bool DidJump;
        public float SinceGrounded;
        public float TimeDelta;

        /// <summary>The receiver clamps the delta to [0, 0.1].</summary>
        public float ClampedTimeDelta => Mathf.Clamp(TimeDelta, 0f, MaxTimeDelta);

        public void Write(BinaryWriter w)
        {
            WriteV2(w, Position);
            WriteV2(w, Direction);
            WriteV2(w, AimDirection);
            w.Write(JumpIsPressed);
            WriteV2(w, Velocity);
            w.Write(DidJump);
            w.Write(SinceGrounded);
            w.Write(TimeDelta);
        }

        public static PlayerSyncPackage Read(BinaryReader r) => new PlayerSyncPackage
        {
            Position = ReadV2(r),
            Direction = ReadV2(r),
            AimDirection = ReadV2(r),
            JumpIsPressed = r.ReadBoolean(),
            Velocity = ReadV2(r),
            DidJump = r.ReadBoolean(),
            SinceGrounded = r.ReadSingle(),
            TimeDelta = r.ReadSingle()
        };

        internal static void WriteV2(BinaryWriter w, Vector2 v)
        {
            w.Write(v.x);
            w.Write(v.y);
        }

        internal static Vector2 ReadV2(BinaryReader r) => new Vector2(r.ReadSingle(), r.ReadSingle());
    }

    /// <summary>SyncProjectile.OnPhotonSerializeView (0x06000a0f): position, velocity, time delta.</summary>
    public struct ProjectileSyncPackage
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float TimeDelta;

        public void Write(BinaryWriter w)
        {
            PlayerSyncPackage.WriteV2(w, Position);
            PlayerSyncPackage.WriteV2(w, Velocity);
            w.Write(TimeDelta);
        }

        public static ProjectileSyncPackage Read(BinaryReader r) => new ProjectileSyncPackage
        {
            Position = PlayerSyncPackage.ReadV2(r),
            Velocity = PlayerSyncPackage.ReadV2(r),
            TimeDelta = r.ReadSingle()
        };
    }

    public enum GameEventType : byte
    {
        Shoot,
        Block,
        Jump,
        Hit,
        CardPicked,
        PointResult
    }

    /// <summary>Discrete events travel separately from continuous state and carry a per-sender sequence.</summary>
    public struct GameEvent
    {
        public GameEventType Type;
        public int SenderId;
        public uint Sequence;
        public int IntArg;
        public float FloatArg;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)Type);
                w.Write(SenderId);
                w.Write(Sequence);
                w.Write(IntArg);
                w.Write(FloatArg);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static GameEvent FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                return new GameEvent
                {
                    Type = (GameEventType)r.ReadByte(),
                    SenderId = r.ReadInt32(),
                    Sequence = r.ReadUInt32(),
                    IntArg = r.ReadInt32(),
                    FloatArg = r.ReadSingle()
                };
            }
        }
    }

    /// <summary>Drops re-delivered events (same sender + sequence) so scores never double count.</summary>
    public sealed class EventDeduplicator
    {
        private readonly Dictionary<int, HashSet<uint>> _seen = new Dictionary<int, HashSet<uint>>();

        public bool Accept(in GameEvent e)
        {
            if (!_seen.TryGetValue(e.SenderId, out var set))
            {
                set = new HashSet<uint>();
                _seen[e.SenderId] = set;
            }
            return set.Add(e.Sequence);
        }

        public void Clear() => _seen.Clear();
    }

    public static class SyncCodec
    {
        public static byte[] Encode(in PlayerSyncPackage p)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                p.Write(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static PlayerSyncPackage DecodePlayer(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
                return PlayerSyncPackage.Read(r);
        }
    }
}

