using System;
using System.IO;
using NUnit.Framework;
using Metadream.Shoot.Network;
using Metadream.Shoot.Online;
using Metadream.Shoot.Replay;

namespace Metadream.Shoot.Tests
{
    public class ReplayAndProfileTests
    {
        [Test]
        public void RecordingDoesNotCollectDeviceIdentity()
        {
            var device = ReplayDevice.Current(true);
            Assert.AreEqual("dedicated", device.role);
            Assert.AreEqual("", device.machineName);
            Assert.AreEqual("", device.userName);
            Assert.AreEqual("", device.os);
            Assert.AreEqual("", device.deviceId);
            Assert.AreEqual("", device.localIp);
            StringAssert.StartsWith("session-", PlayerProfile.DeviceName);
            Assert.AreEqual(PlayerProfile.DeviceName, PlayerProfile.DeviceName, "reconnect identity lasts for this process");
        }

        [Test]
        public void UploadRewritesLegacyMetadataWithoutChangingGameplayFrames()
        {
            var meta = new ReplayMeta
            {
                roomName = "Test room",
                device = new ReplayDevice { machineName = "PRIVATE-PC", userName = "private-user", deviceId = "private-device", localIp = "192.168.1.9" },
                players = new[] { new ReplayPlayerInfo { nickname = "Player", device = "PRIVATE-PEER", address = "192.168.1.8" } }
            };
            var frames = new[] { new ReplayFrame { Time = 1f, Data = new byte[] { 1, 2, 3 } } };
            using (var original = new MemoryStream())
            {
                ReplayFile.Write(original, meta, frames);
                byte[] body = ReplayUploader.PrepareUpload(original.ToArray(), out var header);
                using (var sanitized = new MemoryStream(body))
                {
                    var replay = ReplayFile.Read(sanitized);
                    Assert.AreEqual("", header.device.userName);
                    Assert.AreEqual("", replay.Meta.device.machineName);
                    Assert.AreEqual("", replay.Meta.device.deviceId);
                    Assert.AreEqual("", replay.Meta.device.localIp);
                    Assert.AreEqual("", replay.Meta.players[0].device);
                    Assert.AreEqual("", replay.Meta.players[0].address);
                    Assert.AreEqual("Player", replay.Meta.players[0].nickname);
                    Assert.AreEqual("Test room", replay.Meta.roomName);
                    CollectionAssert.AreEqual(frames[0].Data, replay.Frames[0].Data);
                    Assert.AreEqual(1f, replay.Frames[0].Time);
                }
                Assert.AreEqual("private-user", meta.device.userName, "existing local replay is not changed");
            }
        }

        [TestCase(null, null, null, false)]
        [TestCase(null, "https://replays.example.invalid", "test-only", false)]
        [TestCase("1", "http://replays.example.invalid", "test-only", false)]
        [TestCase("1", "https://user:pass@replays.example.invalid", "test-only", false)]
        [TestCase("1", "https://replays.example.invalid?token=test-only", "test-only", false)]
        [TestCase("1", "https://replays.example.invalid", null, false)]
        [TestCase("1", "https://replays.example.invalid", "test-only", true)]
        public void UploadRequiresConsentHttpsAndConfiguredKey(string consent, string url, string key, bool expected)
        {
            string[] names = { "METADREAM_REPLAY_UPLOAD", "METADREAM_REPLAY_UPLOAD_URL", "METADREAM_REPLAY_UPLOAD_KEY" };
            string[] before = Array.ConvertAll(names, Environment.GetEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(names[0], consent);
                Environment.SetEnvironmentVariable(names[1], url);
                Environment.SetEnvironmentVariable(names[2], key);
                Assert.AreEqual(expected, ReplayUploader.Enabled);
                if (!expected)
                {
                    int uploaded = ReplayUploader.Uploaded;
                    ReplayUploader.Upload("missing-replay-file", new ReplayMeta());
                    Assert.AreEqual(uploaded, ReplayUploader.Uploaded);
                    StringAssert.Contains("꺼짐", ReplayUploader.LastResult);
                }
            }
            finally
            {
                for (int i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], before[i]);
            }
        }

        [Test]
        public void ReplayFileRoundTripsMetaAndFrames()
        {
            var rec = new ReplayRecorder(new ReplayMeta { roomName = "동아리 방", device = new ReplayDevice { machineName = "LAB-PC-01" } });
            rec.SetPlayer(new ReplayPlayerInfo { slot = 1, nickname = "영희", kind = "remote", device = "LAB-PC-07" });
            rec.SetPlayer(new ReplayPlayerInfo { slot = 0, nickname = "철수", kind = "host", device = "LAB-PC-01" });
            var snap = new MatchSnapshot { Tick = 5, MapIndex = 2 };
            snap.Points[1] = 1;
            snap.Fighters.Add(new FighterState { Present = true, Health = 80, MaxHealth = 100, Cards = new byte[] { 3 } });
            rec.Add(0f, snap);
            snap.Tick = 6;
            rec.Add(0.5f, snap);
            var frames = new System.Collections.Generic.List<ReplayFrame>();
            var ms = new MemoryStream();
            rec.Meta.duration = 0.5f;
            // Write through the public format API with the recorder's meta.
            var field = typeof(ReplayRecorder).GetField("_frames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ReplayFile.Write(ms, rec.Meta, (System.Collections.Generic.List<ReplayFrame>)field.GetValue(rec));
            ms.Position = 0;
            var data = ReplayFile.Read(ms);
            Assert.AreEqual("동아리 방", data.Meta.roomName);
            Assert.AreEqual("LAB-PC-01", data.Meta.device.machineName);
            Assert.AreEqual(2, data.Meta.players.Length);
            Assert.AreEqual("철수", data.Meta.players[0].nickname, "players are sorted by slot");
            Assert.AreEqual("LAB-PC-07", data.Meta.players[1].device);
            Assert.AreEqual(Protocol.Version, data.Meta.protocol);
            Assert.AreEqual(2, data.Frames.Count);
            Assert.AreEqual(0.5f, data.Duration, 1e-4f);
            var back = new MatchSnapshot();
            using (var r = new BinaryReader(new MemoryStream(data.Frames[1].Data))) back.Read(r);
            Assert.AreEqual(6, back.Tick);
            Assert.AreEqual(2, back.MapIndex);
            Assert.AreEqual(1, back.Points[1]);
            Assert.AreEqual(80f, back.Fighters[0].Health);
            Assert.AreEqual(3, back.Fighters[0].Cards[0]);
            Assert.AreEqual(1, data.Frames[1].Data[13], "BuildMarks reads Points[1] at byte 13");
        }

        [Test]
        public void GarbageIsNotAReplay()
        {
            Assert.Throws<System.IO.InvalidDataException>(() => ReplayFile.Read(new MemoryStream(Gzip("hello"))));
        }

        private static byte[] Gzip(string s)
        {
            var ms = new MemoryStream();
            using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, true))
            using (var w = new BinaryWriter(gz)) w.Write(s);
            return ms.ToArray();
        }

        [Test]
        public void NicknamesAreSanitized()
        {
            Assert.IsNull(PlayerProfile.Sanitize("   "));
            Assert.AreEqual("김 철수", PlayerProfile.Sanitize("  김   철수 "));
            Assert.AreEqual("abc", PlayerProfile.Sanitize("a|b\"c\n"));
            Assert.AreEqual(PlayerProfile.MaxLength, PlayerProfile.Sanitize("가나다라마바사아자차카타파하").Length);
        }
    }
}
