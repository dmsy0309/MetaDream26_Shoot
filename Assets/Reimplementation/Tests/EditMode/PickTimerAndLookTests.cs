using System.IO;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Core;
using Metadream.Shoot.Network;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Tests
{
    public class PickTimerAndLookTests
    {
        [Test]
        public void DefaultsGiveMoreHealthAndSlightlyWeakerBullets()
        {
            var s = new MatchSettings();
            Assert.AreEqual(15, s.pickTimeLimit);
            Assert.AreEqual(150f, s.stats.maxHealth);
            Assert.AreEqual(50f, s.stats.bulletDamage);
            var f = FighterStats.CreateDefault(s.stats);
            Assert.AreEqual(150f, f.character.maxHealth);
            Assert.AreEqual(50f, f.gun.ProjectileDamage);
            Assert.AreEqual(3, Mathf.CeilToInt(f.character.maxHealth / f.gun.ProjectileDamage), "three basic hits to fall");
            var old = FighterStats.CreateDefault();
            Assert.AreEqual(old.character.scale, f.character.scale, 1e-5f, "higher base health does not make the body bigger");
            Assert.AreEqual(old.character.mass, f.character.mass, 1e-3f);
            StatComposer.Apply(f, new CardModifiers { health = 2f });
            Assert.AreEqual(1.2f * Mathf.Pow(2.4f, 0.2f), f.character.scale, 1e-5f, "a health card still grows the body by its ratio");
        }

        [Test]
        public void SnapshotCarriesPickTimerAndLooksAndLobbyCarriesTheLimit()
        {
            var s = new MatchSnapshot { PickTimeLeft = 4.25f, PickTimeLimit = 15 };
            s.Looks.Add(new CharacterLook(0, 76, 19));
            s.Looks.Add(new CharacterLook(0, 92, 60));
            var ms = new MemoryStream();
            s.Write(new BinaryWriter(ms));
            ms.Position = 0;
            var back = new MatchSnapshot();
            back.Read(new BinaryReader(ms));
            Assert.AreEqual(4.25f, back.PickTimeLeft);
            Assert.AreEqual(15, back.PickTimeLimit);
            Assert.AreEqual(2, back.Looks.Count);
            Assert.AreEqual(new CharacterLook(0, 92, 60), back.Looks[1]);
            Assert.AreEqual(10, Protocol.Version);

            var l = new LobbyInfo { PickTimeLimit = 30 };
            var lm = new MemoryStream();
            l.Write(new BinaryWriter(lm));
            lm.Position = 0;
            Assert.AreEqual(30, LobbyInfo.Read(new BinaryReader(lm)).PickTimeLimit);
        }

        [Test]
        public void TimerBlinksRedEveryHalfSecondInTheLastFiveSeconds()
        {
            Assert.IsFalse(GameHud.TimerBlinkOn(7.4f), "no blinking before 5 s");
            Assert.IsFalse(GameHud.TimerBlinkOn(0f));
            int changes = 0;
            bool prev = GameHud.TimerBlinkOn(5f);
            for (float t = 5f; t > 0.001f; t -= 0.01f)
            {
                bool on = GameHud.TimerBlinkOn(t);
                if (on != prev) changes++;
                prev = on;
            }
            Assert.AreEqual(20, changes, 2, "5 s × 2 cycles per second × 2 edges");
            Assert.AreEqual(GameHud.TimerBlinkOn(3.3f), GameHud.TimerBlinkOn(2.8f), "0.5 s period");
            Assert.AreNotEqual(GameHud.TimerBlinkOn(3.3f), GameHud.TimerBlinkOn(3.05f), "half a period later the colour flips");
        }

        [Test]
        public void LooksPackAndSanitize()
        {
            var l = new CharacterLook(0, 76, 19);
            Assert.AreEqual(l, CharacterLook.Unpack(l.Pack()));
            var legacy = new CharacterLook { Hat = 9, Expression = 7, Accessory = 6, Mouth = 3, Accessory2 = 2 };
            Assert.AreEqual(CharacterLook.Default, legacy.Sanitized);
            var invalid = new CharacterLook { Hat = 200, Expression = 255, Accessory = 99, Mouth = 255 };
            Assert.AreEqual(CharacterLook.Default, invalid.Sanitized);
            var extracted = new CharacterLook(0, 92, 60) { Mouth = 65, Accessory2 = 59 };
            Assert.AreEqual(extracted, extracted.Sanitized);
            Assert.IsFalse(l.Describe().Contains("ROUNDS"));

        }
    }
}

