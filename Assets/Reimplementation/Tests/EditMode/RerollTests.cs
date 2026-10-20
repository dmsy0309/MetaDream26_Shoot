using System.IO;
using NUnit.Framework;
using Metadream.Shoot.Core;
using Metadream.Shoot.Network;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Tests
{
    public class RerollTests
    {
        [Test]
        public void DefaultsAreThreeAtStartAndZeroPerRound()
        {
            var s = new MatchSettings();
            Assert.AreEqual(3, s.rerollsAtStart);
            Assert.AreEqual(0, s.rerollsPerRound);
        }

        [Test]
        public void RerollTravelsInInputPacketsAndIsLatched()
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) Protocol.WriteInput(w, new InputFrame { Reroll = 3, NavLeft = true });
            ms.Position = 0;
            InputFrame back;
            using (var r = new BinaryReader(ms)) back = Protocol.ReadInput(r);
            Assert.AreEqual(3, back.Reroll);
            Assert.IsTrue(back.NavLeft);

            var net = new NetworkInput("x");
            net.Push(new InputFrame { Reroll = 255 });
            net.Push(new InputFrame());
            Assert.AreEqual(255, net.Read(default).Reroll, "a reroll press survives later empty frames");
            Assert.AreEqual(0, net.Read(default).Reroll);
        }

        [Test]
        public void SnapshotCarriesPickerRerollsAndOldDataStillReads()
        {
            var s = new MatchSnapshot { PickerRerolls = 2 };
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) s.Write(w);
            var bytes = ms.ToArray();
            var back = new MatchSnapshot();
            using (var r = new BinaryReader(new MemoryStream(bytes))) back.Read(r);
            Assert.AreEqual(2, back.PickerRerolls);
            // v6/v7 replays end after the rerolls byte (no v8 map script data: 1 count byte + 1 empty-string byte).
            var v7 = new byte[bytes.Length - 2];
            System.Array.Copy(bytes, v7, v7.Length);
            var seven = new MatchSnapshot();
            using (var r = new BinaryReader(new MemoryStream(v7))) seven.Read(r);
            Assert.AreEqual(2, seven.PickerRerolls);
            Assert.AreEqual(0, seven.MapObjects.Count);
            // Replays recorded before v6 have no rerolls byte either.
            var old = new byte[bytes.Length - 3];
            System.Array.Copy(bytes, old, old.Length);
            var legacy = new MatchSnapshot();
            using (var r = new BinaryReader(new MemoryStream(old))) legacy.Read(r);
            Assert.AreEqual(0, legacy.PickerRerolls);
        }
    }
}
