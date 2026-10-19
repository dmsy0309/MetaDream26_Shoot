using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Metadream.Shoot.Core;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Network
{
    public enum MsgType : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        Lobby = 4,
        Input = 5,
        Snapshot = 6,
        ToLobby = 7,
        Bye = 8,
        /// <summary>Room leader asks a dedicated server to start now.</summary>
        StartRequest = 9,
        /// <summary>Room leader asks for 1v1 (1) or 2v2 (2).</summary>
        TeamSizeRequest = 10
    }

    public enum SlotKind : byte
    {
        Empty = 0,
        Host = 1,
        Remote = 2,
        Cpu = 3,
        /// <summary>Player left mid-match; only the same device may take the slot back (v5).</summary>
        Disconnected = 4
    }

    public static class Protocol
    {
        /// <summary>v3: content hash in Hello. v4: nickname + computer name. v5: Disconnected slots, no rematch, map gimmicks. v6: card rerolls.</summary>
        /// <summary>v7: sound events (FxType.Sound) in snapshots.</summary>
        public const int Version = 7;
        public const int DefaultPort = 7777;
        /// <summary>Same rate as the original PhotonNetwork.SerializationRate (NetworkSettings.Start).</summary>
        public const float SnapshotRate = 30f;
        public const float InputRate = 60f;

        public static byte[] Pack(MsgType type, Action<BinaryWriter> body = null)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)type);
                body?.Invoke(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static MsgType Peek(byte[] data) => data.Length > 0 ? (MsgType)data[0] : 0;

        public static BinaryReader Body(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            r.ReadByte();
            return r;
        }

        public static void WriteV2(BinaryWriter w, Vector2 v)
        {
            w.Write(v.x);
            w.Write(v.y);
        }

        public static Vector2 ReadV2(BinaryReader r) => new Vector2(r.ReadSingle(), r.ReadSingle());

        public static void WriteInput(BinaryWriter w, in InputFrame f)
        {
            WriteV2(w, f.Move);
            WriteV2(w, f.Aim);
            int flags = 0;
            if (f.JumpPressed) flags |= 1;
            if (f.JumpHeld) flags |= 2;
            if (f.ShootPressed) flags |= 4;
            if (f.ShootHeld) flags |= 8;
            if (f.BlockPressed) flags |= 16;
            if (f.ConfirmPressed) flags |= 32;
            if (f.NavLeft) flags |= 64;
            if (f.NavRight) flags |= 128;
            if (f.NavUp) flags |= 256;
            if (f.NavDown) flags |= 512;
            w.Write((ushort)flags);
            w.Write((byte)Mathf.Clamp(f.Reroll, 0, 255));
        }

        public static InputFrame ReadInput(BinaryReader r)
        {
            var f = new InputFrame { Move = ReadV2(r), Aim = ReadV2(r) };
            int flags = r.ReadUInt16();
            f.JumpPressed = (flags & 1) != 0;
            f.JumpHeld = (flags & 2) != 0;
            f.ShootPressed = (flags & 4) != 0;
            f.ShootHeld = (flags & 8) != 0;
            f.BlockPressed = (flags & 16) != 0;
            f.ConfirmPressed = (flags & 32) != 0;
            f.NavLeft = (flags & 64) != 0;
            f.NavRight = (flags & 128) != 0;
            f.NavUp = (flags & 256) != 0;
            f.NavDown = (flags & 512) != 0;
            f.Reroll = r.ReadByte();
            return f;
        }
    }

    public struct LobbySlot
    {
        public SlotKind Kind;
        public string Name;
    }

    public sealed class LobbyInfo
    {
        public string RoomName = "";
        public string Code = "";
        public int TeamSize = 1;
        public bool InMatch;
        public bool Dedicated;
        public int LeaderSlot = -1;
        /// <summary>Seconds until an automatic start, or negative when no countdown runs.</summary>
        public float Countdown = -1f;
        public int PointsToWin = 2;
        public int RoundsToWin = 5;
        public bool FillWithCpu = true;
        public int RerollsAtStart = 3;
        public int RerollsPerRound;
        public readonly List<LobbySlot> Slots = new List<LobbySlot>();

        public void Write(BinaryWriter w)
        {
            w.Write(RoomName);
            w.Write(Code);
            w.Write((byte)TeamSize);
            w.Write(InMatch);
            w.Write(Dedicated);
            w.Write((sbyte)LeaderSlot);
            w.Write(Countdown);
            w.Write((byte)PointsToWin);
            w.Write((byte)RoundsToWin);
            w.Write(FillWithCpu);
            w.Write((byte)Slots.Count);
            foreach (var s in Slots)
            {
                w.Write((byte)s.Kind);
                w.Write(s.Name ?? "");
            }
            w.Write((byte)Mathf.Clamp(RerollsAtStart, 0, 255));
            w.Write((byte)Mathf.Clamp(RerollsPerRound, 0, 255));
        }

        public static LobbyInfo Read(BinaryReader r)
        {
            var l = new LobbyInfo { RoomName = r.ReadString(), Code = r.ReadString(), TeamSize = r.ReadByte(), InMatch = r.ReadBoolean() };
            l.Dedicated = r.ReadBoolean();
            l.LeaderSlot = r.ReadSByte();
            l.Countdown = r.ReadSingle();
            l.PointsToWin = r.ReadByte();
            l.RoundsToWin = r.ReadByte();
            l.FillWithCpu = r.ReadBoolean();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) l.Slots.Add(new LobbySlot { Kind = (SlotKind)r.ReadByte(), Name = r.ReadString() });
            if (r.BaseStream.Position < r.BaseStream.Length) { l.RerollsAtStart = r.ReadByte(); l.RerollsPerRound = r.ReadByte(); }
            return l;
        }
    }

    public struct FighterState
    {
        public bool Present, Dead, Playing, Grounded, TouchingWall, Reloading;
        public Vector2 Position, Velocity, Aim;
        public float Health, MaxHealth, Scale;
        public byte Ammo, MaxAmmo;
        public float ReloadProgress, SinceBlock, BlockReady;
        public byte[] Cards;

        public void Write(BinaryWriter w)
        {
            int flags = (Present ? 1 : 0) | (Dead ? 2 : 0) | (Playing ? 4 : 0) | (Grounded ? 8 : 0) | (TouchingWall ? 16 : 0) | (Reloading ? 32 : 0);
            w.Write((byte)flags);
            Protocol.WriteV2(w, Position);
            Protocol.WriteV2(w, Velocity);
            Protocol.WriteV2(w, Aim);
            w.Write(Health);
            w.Write(MaxHealth);
            w.Write(Scale);
            w.Write(Ammo);
            w.Write(MaxAmmo);
            w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(ReloadProgress) * 255f));
            w.Write(SinceBlock);
            w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(BlockReady) * 255f));
            var cards = Cards ?? Array.Empty<byte>();
            w.Write((byte)cards.Length);
            w.Write(cards);
        }

        public static FighterState Read(BinaryReader r)
        {
            int flags = r.ReadByte();
            var s = new FighterState
            {
                Present = (flags & 1) != 0,
                Dead = (flags & 2) != 0,
                Playing = (flags & 4) != 0,
                Grounded = (flags & 8) != 0,
                TouchingWall = (flags & 16) != 0,
                Reloading = (flags & 32) != 0,
                Position = Protocol.ReadV2(r),
                Velocity = Protocol.ReadV2(r),
                Aim = Protocol.ReadV2(r),
                Health = r.ReadSingle(),
                MaxHealth = r.ReadSingle(),
                Scale = r.ReadSingle(),
                Ammo = r.ReadByte(),
                MaxAmmo = r.ReadByte(),
                ReloadProgress = r.ReadByte() / 255f,
                SinceBlock = r.ReadSingle(),
                BlockReady = r.ReadByte() / 255f
            };
            int n = r.ReadByte();
            s.Cards = r.ReadBytes(n);
            return s;
        }
    }

    public struct ProjectileState
    {
        public int Id;
        public Vector2 Position, Velocity;
        public float Radius, Gravity;
        public sbyte Owner;
    }

    public struct BoxState
    {
        public Vector2 Position;
        public float Rotation;
    }

    /// <summary>Everything a client needs to draw one host frame.</summary>
    public sealed class MatchSnapshot
    {
        public int Tick;
        public float TimeScale = 1f;
        public MatchPhase Phase;
        public int PointsToWin = 2, RoundsToWin = 5;
        public readonly int[] Points = new int[2];
        public readonly int[] Rounds = new int[2];
        public int Picker = -1;
        public readonly List<byte> Offer = new List<byte>();
        public int OfferIndex;
        public string Banner = "", SubBanner = "";
        public Color32 BannerColor = new Color32(255, 255, 255, 255);
        public int MapIndex = -1, IncomingMap = -1;
        public int WinningTeam = -1;
        /// <summary>Rerolls left for the current picker (written last; old replays without it read 0).</summary>
        public int PickerRerolls;
        public readonly List<FighterState> Fighters = new List<FighterState>();
        public readonly List<ProjectileState> Projectiles = new List<ProjectileState>();
        public readonly List<BoxState> Boxes = new List<BoxState>();
        public readonly List<FxEvent> Fx = new List<FxEvent>();

        public void Clear()
        {
            Offer.Clear();
            Fighters.Clear();
            Projectiles.Clear();
            Boxes.Clear();
            Fx.Clear();
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Tick);
            w.Write(TimeScale);
            w.Write((byte)Phase);
            w.Write((byte)PointsToWin);
            w.Write((byte)RoundsToWin);
            for (int t = 0; t < 2; t++) { w.Write((byte)Points[t]); w.Write((byte)Rounds[t]); }
            w.Write((sbyte)Picker);
            w.Write((byte)Offer.Count);
            foreach (var c in Offer) w.Write(c);
            w.Write((byte)OfferIndex);
            w.Write(Banner ?? "");
            w.Write(SubBanner ?? "");
            w.Write(BannerColor.r); w.Write(BannerColor.g); w.Write(BannerColor.b); w.Write(BannerColor.a);
            w.Write((short)MapIndex);
            w.Write((short)IncomingMap);
            w.Write((sbyte)WinningTeam);

            w.Write((byte)Fighters.Count);
            foreach (var f in Fighters) f.Write(w);
            w.Write((ushort)Projectiles.Count);
            foreach (var p in Projectiles)
            {
                w.Write(p.Id);
                Protocol.WriteV2(w, p.Position);
                Protocol.WriteV2(w, p.Velocity);
                w.Write(p.Radius);
                w.Write(p.Gravity);
                w.Write(p.Owner);
            }
            w.Write((byte)Boxes.Count);
            foreach (var b in Boxes) { Protocol.WriteV2(w, b.Position); w.Write(b.Rotation); }
            w.Write((ushort)Fx.Count);
            foreach (var e in Fx)
            {
                w.Write((byte)e.Type);
                Protocol.WriteV2(w, e.Position);
                w.Write(e.Color.r); w.Write(e.Color.g); w.Write(e.Color.b); w.Write(e.Color.a);
                w.Write(e.A);
                w.Write(e.B);
            }
            w.Write((byte)Mathf.Clamp(PickerRerolls, 0, 255));
        }

        public void Read(BinaryReader r)
        {
            Clear();
            Tick = r.ReadInt32();
            TimeScale = r.ReadSingle();
            Phase = (MatchPhase)r.ReadByte();
            PointsToWin = r.ReadByte();
            RoundsToWin = r.ReadByte();
            for (int t = 0; t < 2; t++) { Points[t] = r.ReadByte(); Rounds[t] = r.ReadByte(); }
            Picker = r.ReadSByte();
            int offer = r.ReadByte();
            for (int i = 0; i < offer; i++) Offer.Add(r.ReadByte());
            OfferIndex = r.ReadByte();
            Banner = r.ReadString();
            SubBanner = r.ReadString();
            BannerColor = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
            MapIndex = r.ReadInt16();
            IncomingMap = r.ReadInt16();
            WinningTeam = r.ReadSByte();

            int nf = r.ReadByte();
            for (int i = 0; i < nf; i++) Fighters.Add(FighterState.Read(r));
            int np = r.ReadUInt16();
            for (int i = 0; i < np; i++)
            {
                Projectiles.Add(new ProjectileState
                {
                    Id = r.ReadInt32(),
                    Position = Protocol.ReadV2(r),
                    Velocity = Protocol.ReadV2(r),
                    Radius = r.ReadSingle(),
                    Gravity = r.ReadSingle(),
                    Owner = r.ReadSByte()
                });
            }
            int nb = r.ReadByte();
            for (int i = 0; i < nb; i++) Boxes.Add(new BoxState { Position = Protocol.ReadV2(r), Rotation = r.ReadSingle() });
            int ne = r.ReadUInt16();
            for (int i = 0; i < ne; i++)
            {
                var e = new FxEvent { Type = (FxType)r.ReadByte(), Position = Protocol.ReadV2(r) };
                e.Color = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                e.A = r.ReadSingle();
                e.B = r.ReadSingle();
                Fx.Add(e);
            }
            PickerRerolls = r.BaseStream.Position < r.BaseStream.Length ? r.ReadByte() : 0;
        }
    }

    /// <summary>
    /// Remote player's input on the host. Continuous values keep the latest frame; edge-triggered presses
    /// are OR-ed until the next Read so a press is never lost between host frames.
    /// </summary>
    public sealed class NetworkInput : IInputSource
    {
        private InputFrame _latest;
        private bool _jump, _shoot, _block, _confirm, _left, _right, _up, _down;
        private int _reroll;

        public NetworkInput(string label)
        {
            Label = label;
        }

        public string Label { get; }
        public float LastReceived { get; private set; }

        public void Push(in InputFrame f)
        {
            _latest = f;
            _jump |= f.JumpPressed;
            _shoot |= f.ShootPressed;
            _block |= f.BlockPressed;
            _confirm |= f.ConfirmPressed;
            _left |= f.NavLeft;
            _right |= f.NavRight;
            _up |= f.NavUp;
            _down |= f.NavDown;
            if (f.Reroll != 0) _reroll = f.Reroll;
            LastReceived = Time.unscaledTime;
        }

        public InputFrame Read(in InputContext context)
        {
            var f = _latest;
            f.JumpPressed = _jump;
            f.ShootPressed = _shoot;
            f.BlockPressed = _block;
            f.ConfirmPressed = _confirm;
            f.NavLeft = _left;
            f.NavRight = _right;
            f.NavUp = _up;
            f.NavDown = _down;
            f.Reroll = _reroll;
            _reroll = 0;
            _jump = _shoot = _block = _confirm = _left = _right = _up = _down = false;
            return f;
        }
    }
}

