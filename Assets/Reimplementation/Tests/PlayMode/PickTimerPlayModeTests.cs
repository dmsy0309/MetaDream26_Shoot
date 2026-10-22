using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot.Core;
using Metadream.Shoot.Network;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Tests
{
    public class PickTimerPlayModeTests
    {
        private GameObject _root, _cam;

        [SetUp]
        public void SetUp()
        {
            _cam = new GameObject("Main Camera") { tag = "MainCamera" };
            _cam.AddComponent<Camera>();
            _root = new GameObject("GameBootstrap");
            _root.AddComponent<GameBootstrap>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Online.PlayerProfile.SessionLook = null;
            var flow = GameFlow.Instance;
            if (flow != null) flow.ReturnToTitle();
            Object.Destroy(_root);
            Object.Destroy(_cam);
            foreach (var m in Object.FindObjectsByType<Maps.MapRoot>(FindObjectsInactive.Include)) Object.Destroy(m.gameObject);
            CharacterPreview.Hide();
        }

        [UnityTest]
        public IEnumerator TimeLimitPicksTheHighlightedCardWhenItRunsOut()
        {
            yield return null;
            Online.PlayerProfile.SessionLook = new CharacterLook(0, 76, 19);
            var flow = GameFlow.Instance;
            flow.StartMatch(GameMode.VersusCpu, new MatchSettings { pickTimeLimit = 2 }, 21);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!(flow.IsPicking && flow.Picker == flow.Fighters[0]) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(flow.IsPicking && flow.Picker == flow.Fighters[0], "P1 picks first");
            Assert.AreEqual(new CharacterLook(0, 76, 19), flow.Fighters[0].Look, "the local player wears the profile look");
            Assert.AreEqual(2, flow.PickTimeLimit);
            Assert.Greater(flow.PickTimeLeft, 1.5f);
            var s = new MatchSnapshot();
            flow.CaptureSnapshot(s);
            Assert.Greater(s.PickTimeLeft, 1.5f, "the timer is sent to clients and replays");
            Assert.AreEqual(new CharacterLook(0, 76, 19), s.Looks[0]);

            Assert.IsTrue(flow.Reroll(flow.Offer.Count - 1), "reroll also moves the highlight to that card");
            Assert.AreEqual(flow.Offer.Count - 1, flow.OfferIndex);
            var expected = flow.Offer[flow.Offer.Count - 1];
            int cardsBefore = flow.Fighters[0].Cards.Count;
            deadline = Time.realtimeSinceStartup + 4f;
            while (flow.Picker == flow.Fighters[0] && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreNotEqual(flow.Fighters[0], flow.Picker, "the pick ended by itself");
            Assert.AreEqual(cardsBefore + 1, flow.Fighters[0].Cards.Count);
            Assert.AreSame(expected, flow.Fighters[0].Cards[flow.Fighters[0].Cards.Count - 1], "the highlighted card was taken");
        }

        [UnityTest]
        public IEnumerator ZeroMeansNoTimeLimit()
        {
            yield return null;
            var flow = GameFlow.Instance;
            flow.StartMatch(GameMode.VersusCpu, new MatchSettings { pickTimeLimit = 0 }, 22);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!(flow.IsPicking && flow.Picker == flow.Fighters[0]) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.Less(flow.PickTimeLeft, 0f);
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.AreEqual(flow.Fighters[0], flow.Picker, "still waiting for the player");
        }

        [UnityTest]
        public IEnumerator PreviewRendersTheChosenLook()
        {
            yield return null;
            CharacterPreview.Show(new CharacterLook(0, 8, 25), Color.red, Vector2.right);
            yield return null;
            yield return null;
            Assert.IsNotNull(CharacterPreview.Texture);
            Assert.AreEqual(512, CharacterPreview.Texture.width);
            var hat = GameObject.Find("P_A_X10");
            Assert.IsNotNull(hat, "신사 모자 parts are built");
            CharacterPreview.Show(new CharacterLook(0, 0, 0), Color.red, Vector2.right);
            yield return null;
            Assert.IsNull(GameObject.Find("P_A_X10"), "changing the look rebuilds the parts");
        }
    }
}

