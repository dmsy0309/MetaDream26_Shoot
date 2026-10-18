using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Cards;

namespace Metadream.Shoot.Tests
{
    public class CardRulesTests
    {
        private static CardDefinition C(string id, CardRarity r, bool multiple = true, string[] cats = null, string[] ban = null) => new CardDefinition
        {
            Id = id, Rarity = r, AllowMultiple = multiple,
            Categories = cats ?? new string[0], BlacklistedCategories = ban ?? new string[0]
        };

        [Test]
        public void WeightsArePerCard()
        {
            Assert.AreEqual(10, CardRules.RarityWeight(CardRarity.Common));
            Assert.AreEqual(4, CardRules.RarityWeight(CardRarity.Uncommon));
            Assert.AreEqual(1, CardRules.RarityWeight(CardRarity.Rare));
        }

        [Test]
        public void WeightedPickCumulativeBoundaries()
        {
            var pool = new List<CardDefinition> { C("c", CardRarity.Common), C("u", CardRarity.Uncommon), C("r", CardRarity.Rare) };
            Assert.AreEqual("c", CardRules.PickWeighted(pool, 0.0).Id);
            Assert.AreEqual("c", CardRules.PickWeighted(pool, 9.999 / 15.0).Id);
            Assert.AreEqual("u", CardRules.PickWeighted(pool, 10.0 / 15.0).Id);
            Assert.AreEqual("u", CardRules.PickWeighted(pool, 13.999 / 15.0).Id);
            Assert.AreEqual("r", CardRules.PickWeighted(pool, 14.0 / 15.0).Id);
            Assert.AreEqual("r", CardRules.PickWeighted(pool, 0.99999).Id);
        }

        [Test]
        public void DenominatorIsSumOfCandidateWeights()
        {
            // Two commons and one rare: the rare has 1/21, not 1/15.
            var pool = new List<CardDefinition> { C("c1", CardRarity.Common), C("c2", CardRarity.Common), C("r", CardRarity.Rare) };
            Assert.AreEqual("c2", CardRules.PickWeighted(pool, 19.99 / 21.0).Id);
            Assert.AreEqual("r", CardRules.PickWeighted(pool, 20.0 / 21.0).Id);
        }

        [Test]
        public void EligibilityRules()
        {
            var unique = C("unique", CardRarity.Rare, multiple: false);
            var bouncy = C("bouncy", CardRarity.Common, cats: new[] { "bounce" });
            var phase = C("phase", CardRarity.Rare, ban: new[] { "bounce" });
            var plain = C("plain", CardRarity.Common);
            Assert.IsFalse(CardRules.IsEligible(unique, new[] { unique }, null));
            Assert.IsTrue(CardRules.IsEligible(plain, new[] { plain }, null), "allowMultiple cards repeat");
            Assert.IsFalse(CardRules.IsEligible(plain, null, new[] { plain }), "no duplicate inside one offer");
            Assert.IsFalse(CardRules.IsEligible(bouncy, new[] { phase }, null), "owned blacklist blocks category");
            Assert.IsTrue(CardRules.IsEligible(phase, new[] { bouncy }, null), "check is owned blacklist vs candidate categories");
        }

        [Test]
        public void OfferIsUniqueAndShrinksWhenPoolExhausted()
        {
            var pool = new List<CardDefinition> { C("a", CardRarity.Common), C("b", CardRarity.Uncommon), C("c", CardRarity.Rare) };
            var offer = CardRules.DrawOffer(pool, null, 5, new System.Random(3));
            Assert.AreEqual(3, offer.Count);
            Assert.AreEqual(3, new HashSet<string>(offer.ConvertAll(x => x.Id)).Count);
            var full = CardRules.DrawOffer(CardCatalog.All, null, 5, new System.Random(7));
            Assert.AreEqual(5, full.Count);
        }

        [Test]
        public void CatalogIdsAreUnique()
        {
            var ids = new HashSet<string>();
            foreach (var c in CardCatalog.All) Assert.IsTrue(ids.Add(c.Id), c.Id);
            Assert.GreaterOrEqual(CardCatalog.All.Count, 15);
        }

        [Test]
        public void AmmoClampedToOneAndNinety()
        {
            var s = FighterStats.CreateDefault();
            StatComposer.Apply(s, new CardModifiers { ammo = -10 });
            Assert.AreEqual(1, s.ammo.maxAmmo);
            StatComposer.Apply(s, new CardModifiers { ammo = 500 });
            Assert.AreEqual(90, s.ammo.maxAmmo);
        }

        [Test]
        public void MultiProjectileDamageCorrection()
        {
            // N == 1: r = 1 -> G * D
            Assert.AreEqual(0.45f, StatComposer.ComposeDamage(1f, 0.45f, 1, 2), 1e-5f);
            // N = 3, K = 2, D = 0.45: r = 0.4 -> 1 - 0.4 * 0.55 = 0.78
            Assert.AreEqual(0.78f, StatComposer.ComposeDamage(1f, 0.45f, 3, 2), 1e-5f);
            // K == 0: plain multiplication
            Assert.AreEqual(1.6f, StatComposer.ComposeDamage(1f, 1.6f, 3, 0), 1e-5f);
            // floor 0.25
            Assert.AreEqual(0.25f, StatComposer.ComposeDamage(0.3f, 0.1f, 1, 0), 1e-6f);
        }

        [Test]
        public void ApplicationOrderMatters()
        {
            var scatter = CardCatalog.Find("scatter").Modifiers;
            var glass = CardCatalog.Find("glass_cannon").Modifiers;
            var a = FighterStats.CreateDefault();
            StatComposer.Apply(a, scatter);
            StatComposer.Apply(a, scatter);
            var b = FighterStats.CreateDefault();
            StatComposer.Apply(b, glass);
            StatComposer.Apply(b, scatter);
            var c = FighterStats.CreateDefault();
            StatComposer.Apply(c, scatter);
            StatComposer.Apply(c, glass);
            Assert.AreEqual(5, a.gun.numberOfProjectiles);
            Assert.AreEqual(0.45f * 0.78f, a.gun.damage, 1e-5f);
            Assert.AreEqual(b.gun.damage, c.gun.damage, 1e-5f, "these two happen to commute");
            Assert.AreEqual(0.9f, b.gun.damage, 1e-5f);
        }

        [Test]
        public void MassAndSizeFromHealth()
        {
            var s = FighterStats.CreateDefault();
            float q = 1.2f;
            Assert.AreEqual(1.2f * Mathf.Pow(q, 0.2f), s.character.scale, 1e-5f);
            Assert.AreEqual(100f * Mathf.Pow(q, 0.8f), s.character.mass, 1e-3f);
            StatComposer.Apply(s, new CardModifiers { health = 2f });
            Assert.AreEqual(200f, s.character.maxHealth, 1e-4f);
            Assert.AreEqual(1.2f * Mathf.Pow(2.4f, 0.2f), s.character.scale, 1e-5f);
        }
    }
}

