using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot.Core;

namespace Metadream.Shoot.Tests
{
    public class RerollPlayModeTests
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
            var flow = GameFlow.Instance;
            if (flow != null) flow.ReturnToTitle();
            Object.Destroy(_root);
            Object.Destroy(_cam);
            foreach (var m in Object.FindObjectsByType<Maps.MapRoot>(FindObjectsInactive.Include)) Object.Destroy(m.gameObject);
        }

        [UnityTest]
        public IEnumerator HumanPickerCanRerollThreeTimes()
        {
            yield return null;
            var flow = GameFlow.Instance;
            flow.StartMatch(GameMode.VersusCpu, new MatchSettings(), 99);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!(flow.IsPicking && flow.Picker == flow.Fighters[0]) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(flow.IsPicking && flow.Picker == flow.Fighters[0], "P1 picks the first card");
            Assert.AreEqual(3, flow.PickerRerolls);
            Assert.IsTrue(flow.LocalPicker);
            var before = flow.Offer[0];
            Assert.IsTrue(flow.Reroll(0));
            Assert.AreNotSame(before, flow.Offer[0], "the card was replaced");
            Assert.AreEqual(2, flow.PickerRerolls);
            Assert.IsTrue(flow.Reroll(1));
            Assert.IsTrue(flow.Reroll(1));
            Assert.AreEqual(0, flow.PickerRerolls);
            Assert.IsFalse(flow.Reroll(0), "no rerolls left");
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var c in flow.Offer) Assert.IsTrue(ids.Add(c.Id), "offer has no duplicates");
        }

        [UnityTest]
        public IEnumerator QuitConfirmLocksYesForThreeSecondsAndClosesOnNo()
        {
            yield return null;
            var flow = GameFlow.Instance;
            flow.StartMatch(GameMode.VersusCpu, new MatchSettings(), 7);
            yield return null;
            Assert.IsNotNull(Presentation.QuitConfirm.Instance, "bootstrap adds the dialog");
            bool left = false;
            float scaleBefore = Time.timeScale;
            Presentation.QuitConfirm.Open(() => left = true);
            Assert.IsTrue(Presentation.QuitConfirm.IsOpen);
            Assert.AreEqual(0f, Time.timeScale, "offline game pauses");
            Assert.IsFalse(Presentation.QuitConfirm.Confirm(), "'예' is locked right after opening");
            Assert.IsFalse(left);
            Presentation.QuitConfirm.Close(); // '아니요' / Esc
            Assert.IsFalse(Presentation.QuitConfirm.IsOpen);
            Assert.AreEqual(scaleBefore > 0.01f ? scaleBefore : 1f, Time.timeScale, 1e-4f, "closing resumes");
            Assert.IsTrue(flow.HasMatch, "still in the match");

            Presentation.QuitConfirm.Open(() => left = true);
            float until = Time.realtimeSinceStartup + Presentation.QuitConfirm.YesDelay + 0.2f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(Presentation.QuitConfirm.YesEnabled, "'예' unlocks after 3 seconds");
            Assert.IsTrue(Presentation.QuitConfirm.Confirm());
            Assert.IsTrue(left);
            Assert.IsFalse(Presentation.QuitConfirm.IsOpen);
        }
    }
}
