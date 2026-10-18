using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot.Abilities;
using Metadream.Shoot.Content;
using Metadream.Shoot.Presentation;
using Metadream.Shoot.Tutorial;

namespace Metadream.Shoot.Tests
{
    public class TutorialAndEffectPlayModeTests
    {
        private GameObject _root;
        private GameObject _cam;

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
            if (TutorialController.Active != null) Object.Destroy(TutorialController.Active.gameObject);
            var flow = GameFlow.Instance;
            if (flow != null) flow.ReturnToTitle();
            Object.Destroy(_root);
            Object.Destroy(_cam);
            foreach (var m in Object.FindObjectsByType<Maps.MapRoot>(FindObjectsInactive.Include)) Object.Destroy(m.gameObject);
        }

        [UnityTest]
        public IEnumerator TutorialStartsOnTrainingMapAndEscapeReturnsToTitle()
        {
            yield return null;
            var flow = GameFlow.Instance;
            var tutorial = TutorialController.Launch();
            yield return null;
            yield return null;
            var first = TutorialController.Active;
            yield return null;
            Assert.AreSame(first, TutorialController.Active, "the tutorial must not restart itself");
            Assert.IsTrue(flow.IsSandbox);
            Assert.IsFalse(flow.InMenu);
            Assert.AreEqual("training", flow.CurrentMap.Definition.Id);
            Assert.AreEqual(2, flow.Fighters.Count);
            Assert.AreEqual(TutorialController.Step.Move, tutorial.Current);
            Assert.IsTrue(flow.CanUseDebugCommands, "debug commands work in the tutorial sandbox");
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(flow.Fighters[0].IsDead, "player stands safely on the training map");
            Assert.IsNotNull(flow.EscapeOverride);
            flow.EscapeOverride();
            yield return null;
            Assert.IsTrue(flow.InMenu);
            Assert.IsNull(TutorialController.Active);
        }

        [UnityTest]
        public IEnumerator ExplosionAndPoisonHurtEnemiesOnly()
        {
            yield return null;
            var flow = GameFlow.Instance;
            var slots = new List<SlotSpec>
            {
                new SlotSpec { Name = "A", Team = 0, Color = Palette.PlayerOne, Input = new DummyInput() },
                new SlotSpec { Name = "B", Team = 1, Color = Palette.PlayerTwo, Input = new DummyInput() }
            };
            flow.StartSandbox(ContentDatabase.FindMap("plateau"), slots);
            yield return new WaitForSeconds(0.5f);
            var a = flow.Fighters[0];
            var b = flow.Fighters[1];
            float ha = a.Health, hb = b.Health;
            int hits = Explosion.Detonate(b.Position, 3f, 25f, a, 0f, false);
            Assert.AreEqual(1, hits);
            Assert.AreEqual(hb - 25f, b.Health, 0.01f);
            Assert.AreEqual(ha, a.Health, 0.01f, "the owner is never hurt by its own explosion");

            hb = b.Health;
            b.AddDamageOverTime(20f, 1f, a);
            Assert.IsTrue(b.IsPoisoned);
            yield return new WaitForSeconds(1.4f);
            Assert.IsFalse(b.IsPoisoned);
            Assert.AreEqual(hb - 20f, b.Health, 2f);

            a.ApplyCard(ContentDatabase.FindCard("explosive_rounds"));
            Assert.AreEqual(1, a.Effects.Count, "card effects are instantiated from JSON");
        }
    }
}
