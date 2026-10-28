using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Metadream.Shoot.Abilities;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;
using Metadream.Shoot.Network;

namespace Metadream.Shoot.Tests
{
    /// <summary>Content pipeline (cards/maps JSON), effect registry, server rules and master-server packets.</summary>
    public class ContentTests
    {
        private static string Describe(IEnumerable<ContentIssue> issues) => string.Join("\n", issues.Select(i => i.ToString()));

        [Test]
        public void BuiltinContentLoadsWithoutErrors()
        {
            ContentDatabase.Reload();
            Assert.AreEqual(0, ContentDatabase.ErrorCount, Describe(ContentDatabase.Issues));
            Assert.GreaterOrEqual(ContentDatabase.Cards.Count, 22);
            Assert.GreaterOrEqual(ContentDatabase.Maps.Count, 7);
            Assert.IsTrue(ContentDatabase.FindMap("training").TutorialOnly);
            Assert.IsFalse(ContentDatabase.DefaultMap.TutorialOnly);
            Assert.AreEqual(12, ContentDatabase.Hash.Length);
            for (int i = 0; i < ContentDatabase.Cards.Count; i++) Assert.AreEqual(i, ContentDatabase.Cards[i].Index, "network index follows sorted id order");
        }

        [Test]
        public void EffectRegistryDiscoversBuiltinEffects()
        {
            foreach (var id in new[] { "explosion_on_hit", "shockwave_on_block", "poison_on_hit" })
                Assert.IsTrue(EffectRegistry.IsKnown(id), id);
            Assert.IsFalse(EffectRegistry.IsKnown("does_not_exist"));
            Assert.IsNotNull(EffectRegistry.Validate(new EffectSpec { type = "explosion_on_hit", radius = 0f }), "radius 0 is rejected");
            Assert.IsNull(EffectRegistry.Validate(new EffectSpec { type = "explosion_on_hit", radius = 3f, damage = 10f }));
        }

        [Test]
        public void BrokenCardIsRejectedWithReadableErrors()
        {
            var issues = new List<ContentIssue>();
            var card = ContentDatabase.ParseCard("{\"id\":\"bad\",\"title\":\"Bad\",\"rarity\":\"Legendary\",\"modifiers\":{\"damage\":0},\"effects\":[{\"type\":\"nope\"}]}", "bad", "test", issues);
            Assert.IsNull(card);
            string all = Describe(issues);
            StringAssert.Contains("modifiers.damage", all);
            StringAssert.Contains("effects[0]", all);
            StringAssert.Contains("rarity", all);
            Assert.IsNull(ContentDatabase.ParseCard("{not json", "x", "test", issues));
        }

        [Test]
        public void CardWithEffectParses()
        {
            var issues = new List<ContentIssue>();
            var card = ContentDatabase.ParseCard("{\"id\":\"boom\",\"title\":\"Boom\",\"modifiers\":{\"damage\":0.5},\"effects\":[{\"type\":\"explosion_on_hit\",\"radius\":3,\"damage\":20}],\"lines\":[{\"label\":\"a\",\"amount\":\"b\"}]}", "boom", "test", issues);
            Assert.IsNotNull(card, Describe(issues));
            Assert.AreEqual(1, card.Effects.Length);
            Assert.AreEqual(0.5f, card.Modifiers.damage);
            Assert.AreEqual(1f, card.Modifiers.health, "omitted multipliers stay at 1");
        }

        [Test]
        public void BrokenMapIsRejected()
        {
            var issues = new List<ContentIssue>();
            Assert.IsNull(ContentDatabase.ParseMap("{\"id\":\"m\",\"solids\":[{\"x\":0,\"y\":-10,\"w\":40,\"h\":4}],\"spawns\":[{\"x\":0,\"y\":0}]}", "m", "test", issues));
            StringAssert.Contains("at least 2 spawns", Describe(issues));
            issues.Clear();
            Assert.IsNull(ContentDatabase.ParseMap("{\"id\":\"m\",\"solids\":[{\"x\":0,\"y\":-10,\"w\":40,\"h\":4}],\"spawns\":[{\"x\":-5,\"y\":-10},{\"x\":5,\"y\":-5}]}", "m", "test", issues));
            StringAssert.Contains("overlaps solids[0]", Describe(issues));
            issues.Clear();
            Assert.IsNull(ContentDatabase.ParseMap("{\"id\":\"m\",\"solids\":[{\"x\":0,\"y\":-10,\"w\":40,\"h\":4}],\"spawns\":[{\"x\":-50,\"y\":0},{\"x\":5,\"y\":-5}]}", "m", "test", issues));
            StringAssert.Contains("outside", Describe(issues));
        }

        [Test]
        public void PoolsFilterMapsAndCards()
        {
            Assert.IsFalse(MapCatalog.Pool(null).Any(m => m.TutorialOnly), "tutorial map never rotates");
            Assert.AreEqual(1, MapCatalog.Pool(new[] { "steps" }).Count);
            Assert.AreEqual(1, CardCatalog.Pool(new[] { "scatter", "nope" }).Count);
            Assert.AreEqual(CardCatalog.All.Count, CardCatalog.Pool(new string[0]).Count);
        }

        [Test]
        public void ServerRulesAreClampedCopiedAndValidated()
        {
            var c = new Online.ServerConfig();
            c.rules.cardsPerPick = 99;
            c.rules.stats.maxAmmo = 0;
            c.rules.stats.maxHealth = 250f;
            c.rules.mapPool = new[] { "training", "nope", "steps" };
            c.rules.cardPool = new[] { "ghost_card" };
            var s = c.ToSettings();
            Assert.AreEqual(10, s.cardsPerPick);
            Assert.AreEqual(1, s.stats.maxAmmo);
            Assert.AreEqual(250f, s.stats.maxHealth);
            Assert.AreNotSame(c.rules, s);
            Assert.AreNotSame(c.rules.stats, s.stats);
            var problems = c.Validate();
            Assert.AreEqual(3, problems.Count, string.Join("\n", problems));
        }

        [Test]
        public void MasterServerPacketsRoundTrip()
        {
            string json = MasterServerClient.Encode(new MasterPacket { type = "register", ip = "192.168.0.5", port = 7777, players = 2, maxPlayers = 4, name = "방" });
            StringAssert.Contains("\"type\":\"register\"", json);
            StringAssert.Contains("\"players\":2", json);
            StringAssert.Contains("\"maxPlayers\":4", json);
            StringAssert.Contains("\"game\":\"metadream-shoot\"", json);
            // Shape produced by MasterServer/master_server.py (extra fields are ignored).
            var reply = MasterServerClient.Decode("{\"type\":\"list\",\"game\":\"metadream-shoot\",\"time\":1,\"total\":1,\"servers\":[{\"address\":\"100.64.0.9\",\"ip\":\"10.0.0.5\",\"observedIp\":\"100.64.0.9\",\"port\":7777,\"players\":1,\"maxPlayers\":2,\"name\":\"A\",\"teamSize\":1,\"inMatch\":false,\"dedicated\":true,\"code\":\"\",\"version\":" + Protocol.Version + ",\"contentHash\":\"\",\"age\":0.4}]}");
            Assert.IsNotNull(reply);
            Assert.AreEqual(1, reply.servers.Length);
            var e = reply.servers[0];
            Assert.AreEqual("100.64.0.9", e.address);
            Assert.AreEqual(1, e.players);
            Assert.AreEqual(2, e.maxPlayers);
            Assert.IsTrue(e.Compatible);
            StringAssert.Contains("1/2", e.Label);
            Assert.IsNull(MasterServerClient.Decode("garbage"));
            Assert.AreEqual("127.0.0.1", MasterServerEndpoint.DefaultHost);
        }
    }
}
