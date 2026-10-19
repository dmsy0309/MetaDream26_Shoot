using System.IO;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Audio;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Tests
{
    public class SoundAndCheatTests
    {
        [Test]
        public void EverySoundUsedByTheGameIsDefinedAndSynthesizes()
        {
            SoundBank.Reload();
            Assert.IsEmpty(SoundBank.Issues, string.Join("\n", SoundBank.Issues));
            foreach (var id in new[] { "shoot", "hit", "impact", "bounce", "jump", "block", "reflect", "reload", "death", "edge", "explosion",
                                       "shockwave", "pad", "card_pick", "reroll", "card_deal", "card_switch", "fight", "point", "round", "victory", "defeat", "ui_click" })
            {
                var def = SoundBank.Find(id);
                Assert.IsNotNull(def, id);
                var clip = SoundBank.ClipFor(def);
                Assert.IsNotNull(clip, id);
                Assert.Greater(clip.samples, 100, id);
                Assert.AreEqual(id, SoundBank.IdOfHash(SoundBank.Hash(id)), "network hash resolves " + id);
            }
            Assert.Less(SoundBank.Hash("shoot"), 1 << 24, "fits exactly in a float");
        }

        [Test]
        public void WavReaderReads16BitPcm()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            short[] samples = { 0, 16384, -16384, 32767 };
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples.Length * 2); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(22050); w.Write(44100); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(samples.Length * 2);
            foreach (var s in samples) w.Write(s);
            var clip = WavReader.FromBytes(ms.ToArray(), "test");
            Assert.AreEqual(4, clip.samples);
            Assert.AreEqual(22050, clip.frequency);
            var data = new float[4];
            clip.GetData(data, 0);
            Assert.AreEqual(0.5f, data[1], 1e-3f);
            Assert.Throws<InvalidDataException>(() => WavReader.FromBytes(new byte[50], "bad"));
        }

        [Test]
        public void CardSearchMatchesKoreanEnglishAndId()
        {
            Assert.IsTrue(CheatPanel.Search("유도").Exists(c => c.Id == "rounds_homing"));
            Assert.IsTrue(CheatPanel.Search("HOMING").Exists(c => c.Id == "rounds_homing"));
            Assert.IsTrue(CheatPanel.Search("rounds_homing").Count >= 1);
            Assert.AreEqual(Cards.CardCatalog.All.Count, CheatPanel.Search("").Count);
            Assert.IsEmpty(CheatPanel.Search("존재하지않는카드"));
            Assert.IsTrue(ContentBrowser.FilterMaps("rounds").Count >= 30);
        }
    }
}
