using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot.Core;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Tests
{
    public class CheatMatchPlayModeTests
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
            var flow = GameFlow.Instance;
            if (flow != null) flow.ReturnToTitle();
            Object.Destroy(_root);
            Object.Destroy(_cam);
            foreach (var m in Object.FindObjectsByType<Maps.MapRoot>(FindObjectsInactive.Include)) Object.Destroy(m.gameObject);
        }

        [UnityTest]
        public IEnumerator CheatTeamsGivesSearchedCardToPlayerOneOnly()
        {
            yield return null;
            var flow = GameFlow.Instance;
            flow.StartMatch(GameMode.CheatTeams, new MatchSettings { mapPool = new[] { "plateau" } }, 5);
            yield return null;
            Assert.AreEqual(4, flow.Fighters.Count);
            Assert.IsTrue(flow.IsCheatMatch);
            Assert.AreEqual(0, flow.LocalTeam);
            Assert.AreEqual(0, flow.Fighters[2].TeamId, "CPU teammate");
            var card = CheatPanel.Search("rounds_homing")[0];
            int before = flow.Fighters[0].Cards.Count;
            int others = flow.Fighters[1].Cards.Count + flow.Fighters[2].Cards.Count + flow.Fighters[3].Cards.Count;
            Assert.IsTrue(flow.CanUseDebugCommands);
            flow.DebugGiveCard(card);
            Assert.AreEqual(before + 1, flow.Fighters[0].Cards.Count);
            Assert.AreEqual(others, flow.Fighters[1].Cards.Count + flow.Fighters[2].Cards.Count + flow.Fighters[3].Cards.Count, "only P1 gets it");
            Assert.AreEqual(1, flow.Fighters[0].Effects.Count);

            var panel = Object.FindAnyObjectByType<CheatPanel>();
            Assert.IsNotNull(panel);
            panel.Open();
            Assert.IsTrue(CheatPanel.IsOpen);
            Assert.AreEqual(0f, Time.timeScale, "game pauses while searching");
            Assert.IsTrue(Player.Fighter.InputSuspended);
            panel.Close();
            Assert.IsFalse(Player.Fighter.InputSuspended);
            Assert.Greater(Time.timeScale, 0f);
        }
    }
}
