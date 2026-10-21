using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Tests
{
    public class CardArtTests
    {
        private string _dir;

        [TearDown]
        public void TearDown()
        {
            ContentDatabase.ExternalDirectory = null;
            ContentDatabase.Reload();
            if (_dir != null) try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void ArtAreaSpecMatchesTheCardFace()
        {
            Assert.AreEqual(new Rect(20f, 60f, 260f, 112f), CardArt.ArtRect(new Rect(0f, 0f, 300f, 480f)), "art rectangle inside a 300×480 card");
            Assert.AreEqual(new Rect(110f, 160f, 520f, 224f), CardArt.ArtRect(new Rect(70f, 40f, 600f, 960f)), "scales with the card");
            Assert.AreEqual(96f, CardArt.EmblemOuterRadius * 2f);
            Assert.AreEqual(80f, CardArt.IconSize);
            Assert.AreEqual(CardArt.ArtWidth / CardArt.ArtHeight, (float)CardArt.RecommendedFillWidth / CardArt.RecommendedFillHeight, 1e-4f, "recommended image keeps the 65:28 aspect");
        }

        [Test]
        public void ImageNamesCannotLeaveTheCardArtFolder()
        {
            Assert.IsTrue(CardArt.IsSafeName("ice_rounds"));
            Assert.IsTrue(CardArt.IsSafeName("rounds-homing2"));
            foreach (var bad in new[] { "", "../secret", "a/b", "C:\\x", "x.png", "a b" }) Assert.IsFalse(CardArt.IsSafeName(bad), bad);

            var issues = new List<ContentIssue>();
            string json = "{\"id\":\"c\",\"title\":\"C\",\"rarity\":\"Common\",\"lines\":[{\"label\":\"a\",\"amount\":\"1\"}],\"image\":\"../x\",\"imageMode\":\"poster\"}";
            Assert.IsNull(ContentDatabase.ParseCard(json, "c", "test", issues));
            string all = string.Join("\n", issues.Select(i => i.Message));
            StringAssert.Contains("image '../x' must be a file name", all);
            StringAssert.Contains("imageMode must be \"fill\" or \"icon\"", all);

            issues.Clear();
            var card = ContentDatabase.ParseCard(json.Replace("../x", "frost").Replace("poster", "ICON"), "c", "test", issues);
            Assert.IsNotNull(card, string.Join("\n", issues));
            Assert.AreEqual("frost", card.Image);
            Assert.AreEqual("icon", card.ImageMode);
            Assert.AreEqual("frost", CardArt.NameOf(card));
        }

        [Test]
        public void ExternalImagesLoadByCardIdOrImageNameAndMissingOnesWarn()
        {
            _dir = Path.Combine(Path.GetTempPath(), "md_cardart_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_dir, "CardArt"));
            Directory.CreateDirectory(Path.Combine(_dir, "Cards"));
            var tex = new Texture2D(104, 45, TextureFormat.RGBA32, false);
            File.WriteAllBytes(Path.Combine(_dir, "CardArt", "explosive_rounds.png"), tex.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(_dir, "CardArt", "shared_art.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            File.WriteAllText(Path.Combine(_dir, "Cards", "art_named.json"),
                "{\"id\":\"art_named\",\"title\":\"A\",\"rarity\":\"Common\",\"lines\":[{\"label\":\"a\",\"amount\":\"1\"}],\"image\":\"shared_art\",\"imageMode\":\"icon\"}");
            File.WriteAllText(Path.Combine(_dir, "Cards", "art_missing.json"),
                "{\"id\":\"art_missing\",\"title\":\"B\",\"rarity\":\"Common\",\"lines\":[{\"label\":\"a\",\"amount\":\"1\"}],\"image\":\"no_such_art\"}");
            ContentDatabase.ExternalDirectory = _dir;
            ContentDatabase.Reload();

            var byId = CardArt.Get(ContentDatabase.FindCard("explosive_rounds"));
            Assert.IsNotNull(byId, "Content/CardArt/<card id>.png is used without any JSON change");
            Assert.AreEqual(104, byId.width);
            StringAssert.StartsWith("외부", CardArt.OriginOf(ContentDatabase.FindCard("explosive_rounds")));

            var named = ContentDatabase.FindCard("art_named");
            Assert.IsNotNull(CardArt.Get(named), "\"image\" picks another file");
            Assert.IsTrue(CardArt.IsIcon(named));

            Assert.IsNull(CardArt.Get(ContentDatabase.FindCard("art_missing")));
            Assert.IsTrue(ContentDatabase.Issues.Any(i => i.Severity == ContentSeverity.Warning && i.Message.Contains("image 'no_such_art' not found")),
                string.Join("\n", ContentDatabase.Issues.Select(i => i.ToString())));
            Assert.IsNull(CardArt.Get(ContentDatabase.FindCard("rounds_homing")), "cards without an image keep the letter emblem");
        }
    }
}

