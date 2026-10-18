using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Core;
using Metadream.Shoot.Network;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Tests
{
    public class OnlineProtocolTests
    {
        [Test]
        public void RoomCodeRoundTrips()
        {
            string code = RoomCode.Encode(IPAddress.Parse("192.168.0.12"), 7777);
            Assert.AreEqual(11, code.Length);
            Assert.IsTrue(RoomCode.TryDecode(code, out var host, out var port));
            Assert.AreEqual("192.168.0.12", host);
            Assert.AreEqual(7777, port);
            Assert.IsTrue(RoomCode.TryDecode(code.ToLowerInvariant().Replace("-", " "), out host, out port), "case and separators are ignored");
            Assert.AreEqual("192.168.0.12", host);
        }

        [Test]
        public void JoinTargetsAcceptCodeIpAndHostPort()
        {
            Assert.IsTrue(RoomCode.TryParseTarget("10.0.0.5", out var h, out var p));
            Assert.AreEqual("10.0.0.5", h);
            Assert.AreEqual(Protocol.DefaultPort, p);
            Assert.IsTrue(RoomCode.TryParseTarget("10.0.0.5:8000", out h, out p));
            Assert.AreEqual(8000, p);
            Assert.IsTrue(RoomCode.TryParseTarget("localhost", out h, out p));
            Assert.AreEqual("127.0.0.1", h);
            Assert.IsTrue(RoomCode.TryParseTarget(RoomCode.Encode(IPAddress.Parse("172.16.4.9"), 7778), out h, out p));
            Assert.AreEqual("172.16.4.9", h);
            Assert.AreEqual(7778, p);
            Assert.IsFalse(RoomCode.TryParseTarget("", out h, out p));
            Assert.IsFalse(RoomCode.TryParseTarget("1.2.3.4:99999", out h, out p));
        }

        [Test]
        public void FramesSurviveArbitraryChunking()
        {
            var a = FrameBuffer.Frame(new byte[] { 1, 2, 3 });
            var b = FrameBuffer.Frame(new byte[300]);
            var stream = new List<byte>();
            stream.AddRange(a);
            stream.AddRange(b);
            var fb = new FrameBuffer();
            var output = new List<byte[]>();
            var bytes = stream.ToArray();
            for (int i = 0; i < bytes.Length; i += 7)
            {
                int n = Mathf.Min(7, bytes.Length - i);
                var chunk = new byte[n];
                System.Array.Copy(bytes, i, chunk, 0, n);
                Assert.IsTrue(fb.Append(chunk, n, output));
            }
            Assert.AreEqual(2, output.Count);
            Assert.AreEqual(3, output[0].Length);
            Assert.AreEqual(300, output[1].Length);
            Assert.IsFalse(new FrameBuffer().Append(new byte[] { 255, 255, 255, 127 }, 4, output), "oversized frame is rejected");
        }

        [Test]
        public void InputFrameRoundTripsAndPressesAccumulate()
        {
            var f = new InputFrame { Move = new Vector2(1, 0), Aim = new Vector2(0.6f, 0.8f), JumpPressed = true, ShootHeld = true, NavRight = true };
            var bytes = Protocol.Pack(MsgType.Input, w => Protocol.WriteInput(w, f));
            Assert.AreEqual(MsgType.Input, Protocol.Peek(bytes));
            var g = Protocol.ReadInput(Protocol.Body(bytes));
            Assert.AreEqual(f.Move, g.Move);
            Assert.AreEqual(f.Aim, g.Aim);
            Assert.IsTrue(g.JumpPressed && g.ShootHeld && g.NavRight);
            Assert.IsFalse(g.BlockPressed || g.ConfirmPressed);

            var input = new NetworkInput("t");
            input.Push(new InputFrame { JumpPressed = true, Move = Vector2.left });
            input.Push(new InputFrame { Move = Vector2.right });
            var read = input.Read(default);
            Assert.IsTrue(read.JumpPressed, "a press between host frames is not lost");
            Assert.AreEqual(Vector2.right, read.Move, "continuous values use the latest frame");
            Assert.IsFalse(input.Read(default).JumpPressed, "a press is delivered once");
        }

        [Test]
        public void SnapshotRoundTrips()
        {
            var s = new MatchSnapshot { Tick = 42, TimeScale = 0.3f, Phase = MatchPhase.LoserCardPick, PointsToWin = 2, RoundsToWin = 5, Picker = 3, OfferIndex = 2, Banner = "파랑 팀 포인트", SubBanner = "", MapIndex = 4, IncomingMap = -1, WinningTeam = -1 };
            s.Points[1] = 1;
            s.Rounds[0] = 3;
            s.Offer.AddRange(new byte[] { 0, 5, 9 });
            s.Fighters.Add(new FighterState { Present = true, Grounded = true, Position = new Vector2(3, -4), Velocity = new Vector2(1, 2), Aim = Vector2.right, Health = 45, MaxHealth = 100, Scale = 1.24f, Ammo = 2, MaxAmmo = 3, ReloadProgress = 1, SinceBlock = 0.1f, BlockReady = 0.5f, Cards = new byte[] { 1, 2 } });
            s.Fighters.Add(new FighterState { Dead = true, Cards = new byte[0] });
            s.Projectiles.Add(new ProjectileState { Id = 77, Position = new Vector2(1, 1), Velocity = new Vector2(70, 0), Radius = 0.22f, Gravity = 30, Owner = 1 });
            s.Boxes.Add(new BoxState { Position = new Vector2(0, -10), Rotation = 15 });
            s.Fx.Add(new FxEvent { Type = FxType.Burst, Position = new Vector2(2, 2), Color = new Color32(10, 20, 30, 255), A = 22, B = 14 });

            var bytes = Protocol.Pack(MsgType.Snapshot, w => s.Write(w));
            var t = new MatchSnapshot();
            t.Read(Protocol.Body(bytes));
            Assert.AreEqual(42, t.Tick);
            Assert.AreEqual(MatchPhase.LoserCardPick, t.Phase);
            Assert.AreEqual(1, t.Points[1]);
            Assert.AreEqual(3, t.Rounds[0]);
            Assert.AreEqual(3, t.Picker);
            CollectionAssert.AreEqual(new byte[] { 0, 5, 9 }, t.Offer);
            Assert.AreEqual("파랑 팀 포인트", t.Banner);
            Assert.AreEqual(4, t.MapIndex);
            Assert.AreEqual(2, t.Fighters.Count);
            Assert.AreEqual(new Vector2(3, -4), t.Fighters[0].Position);
            Assert.AreEqual(45f, t.Fighters[0].Health);
            Assert.IsTrue(t.Fighters[0].Grounded && t.Fighters[0].Present);
            Assert.IsTrue(t.Fighters[1].Dead);
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, t.Fighters[0].Cards);
            Assert.AreEqual(77, t.Projectiles[0].Id);
            Assert.AreEqual(15f, t.Boxes[0].Rotation);
            Assert.AreEqual(FxType.Burst, t.Fx[0].Type);
            Assert.AreEqual(22f, t.Fx[0].A);
            Assert.Less(bytes.Length, 400, "a small 1v1 snapshot stays compact");
        }

        [Test]
        public void LobbyRoundTrips()
        {
            var l = new LobbyInfo { RoomName = "방", Code = "ABCDE-FGHJK", TeamSize = 2, InMatch = true, Dedicated = true, LeaderSlot = 1, Countdown = 7.5f, PointsToWin = 3, RoundsToWin = 4, FillWithCpu = false };
            l.Slots.Add(new LobbySlot { Kind = SlotKind.Host, Name = "P1 (호스트)" });
            l.Slots.Add(new LobbySlot { Kind = SlotKind.Remote, Name = "P2" });
            l.Slots.Add(new LobbySlot { Kind = SlotKind.Empty, Name = "" });
            l.Slots.Add(new LobbySlot { Kind = SlotKind.Cpu, Name = "CPU 4" });
            var bytes = Protocol.Pack(MsgType.Lobby, w => l.Write(w));
            var m = LobbyInfo.Read(Protocol.Body(bytes));
            Assert.AreEqual(2, m.TeamSize);
            Assert.IsTrue(m.InMatch);
            Assert.IsTrue(m.Dedicated);
            Assert.AreEqual(1, m.LeaderSlot);
            Assert.AreEqual(7.5f, m.Countdown);
            Assert.AreEqual(3, m.PointsToWin);
            Assert.AreEqual(4, m.RoundsToWin);
            Assert.IsFalse(m.FillWithCpu);
            Assert.AreEqual(4, m.Slots.Count);
            Assert.AreEqual(SlotKind.Remote, m.Slots[1].Kind);
            Assert.AreEqual("CPU 4", m.Slots[3].Name);
        }

        [Test]
        public void TcpLoopbackConnectExchangeAndDisconnect()
        {
            var server = new TcpHostServer();
            Assert.IsTrue(server.Start(47900, 20));
            var client = new TcpClientConnection();
            client.Connect("127.0.0.1", server.Port, 3);
            var events = new List<HostEvent>();
            var received = new List<byte[]>();
            int peer = -1;
            byte[] fromClient = null;
            for (int i = 0; i < 300 && (fromClient == null || received.Count == 0); i++)
            {
                server.Poll(events);
                foreach (var e in events)
                {
                    if (e.Type == HostEventType.Connected) { peer = e.PeerId; server.Send(peer, new byte[] { 9, 8, 7 }); }
                    if (e.Type == HostEventType.Data) fromClient = e.Data;
                }
                events.Clear();
                client.Poll(received);
                if (client.State == TcpClientConnection.ConnState.Connected && i == 5) client.Send(new byte[] { 1, 2, 3, 4 });
                Thread.Sleep(5);
            }
            Assert.AreEqual(TcpClientConnection.ConnState.Connected, client.State);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, fromClient);
            CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, received[0]);

            client.Close();
            bool disconnected = false;
            for (int i = 0; i < 200 && !disconnected; i++)
            {
                server.Poll(events);
                foreach (var e in events) if (e.Type == HostEventType.Disconnected && e.PeerId == peer) disconnected = true;
                events.Clear();
                Thread.Sleep(5);
            }
            Assert.IsTrue(disconnected, "host notices the client leaving");
            server.Stop();
        }
    }

    public class ServerConfigTests
    {
        [Test]
        public void DefaultsMatchShippedServerJson()
        {
            var shipped = JsonUtility.FromJson<Online.ServerConfig>(File.ReadAllText("ServerLauncher/common/server.json"));
            var defaults = new Online.ServerConfig();
            Assert.AreEqual(JsonUtility.ToJson(defaults), JsonUtility.ToJson(shipped), "server.json and ServerConfig defaults drifted apart");
        }

        [Test]
        public void CommandLineOverridesConfig()
        {
            var c = new Online.ServerConfig();
            Online.DedicatedServer.ApplyArgs(c, new[] { "server", "-port", "7800", "-teamsize", "5", "-name", "금요일 방", "-minplayers", "4", "-rounds", "3", "-nofill", "-nolan" });
            Assert.AreEqual(7800, c.port);
            Assert.AreEqual(2, c.teamSize, "team size is clamped to 1..2");
            Assert.AreEqual("금요일 방", c.roomName);
            Assert.AreEqual(4, c.minPlayersToStart);
            Assert.AreEqual(3, c.rules.roundsToWinGame);
            Assert.IsFalse(c.fillWithCpu);
            Assert.IsFalse(c.lanBroadcast);
            Assert.IsTrue(c.allowMidJoin);
            var s = c.ToSettings();
            Assert.AreEqual(3, s.roundsToWinGame);
            Assert.AreEqual(2, s.pointsToWinRound);
        }

        [Test]
        public void MissingOrBrokenConfigFallsBackToDefaults()
        {
            var missing = Online.ServerConfig.Load("does-not-exist.json", out var m1);
            Assert.AreEqual(7777, missing.port);
            StringAssert.Contains("not found", m1);
            string broken = Path.Combine(Path.GetTempPath(), "metadream-broken.json");
            File.WriteAllText(broken, "{ this is not json");
            var bad = Online.ServerConfig.Load(broken, out var m2);
            Assert.AreEqual(7777, bad.port);
            StringAssert.Contains("error", m2);
        }
    }
}

