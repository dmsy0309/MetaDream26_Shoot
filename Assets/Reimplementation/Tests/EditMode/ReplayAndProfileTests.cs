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
