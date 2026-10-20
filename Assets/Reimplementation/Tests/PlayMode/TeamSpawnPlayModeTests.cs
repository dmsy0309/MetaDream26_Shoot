using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot.Content;
using Metadream.Shoot.Core;

namespace Metadream.Shoot.Tests
{
    public class TeamSpawnPlayModeTests
    {
        private GameObject _root, _cam;
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "md_teamspawn_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_dir, "Maps"));
            File.WriteAllText(Path.Combine(_dir, "Maps", "test_four_spawns.json"),
                "{\"id\":\"test_four_spawns\",\"name\":\"네 스폰\",\"solids\":[{\"x\":0,\"y\":-14,\"w\":60,\"h\":4}]," +
                "\"spawns\":[{\"x\":-20,\"y\":-10.6},{\"x\":20,\"y\":-10.6},{\"x\":-6,\"y\":-10.6},{\"x\":6,\"y\":-10.6}]}");
            ContentDatabase.ExternalDirectory = _dir;
            ContentDatabase.Reload();
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
            ContentDatabase.ExternalDirectory = null;
            ContentDatabase.Reload();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [UnityTest]
        public IEnumerator TeammatesStandOnTheirOwnSpawnsFromTheReadyBanner()
        {
            yield return null;
            Assert.IsNotNull(ContentDatabase.FindMap("test_four_spawns"), "external test map loaded");
            var flow = GameFlow.Instance;
            flow.StartMatch(GameMode.CpuDemoTeams, new MatchSettings { mapPool = new[] { "test_four_spawns" } }, 5);
            float deadline = Time.realtimeSinceStartup + 40f;
            while (!(flow.Phase == MatchPhase.Battle && flow.Banner == "준비") && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual("준비", flow.Banner, "reached the ready banner of the first point");
            Assert.AreEqual("test_four_spawns", flow.CurrentMap.Definition.Id);
            Assert.AreEqual(4, flow.Fighters.Count);
            Assert.IsFalse(flow.Fighters[2].IsPlaying, "still the ready phase");
            // slots 0 and 2 = orange #1 and #2, slots 1 and 3 = blue #1 and #2
            Assert.Less(Vector2.Distance(flow.Fighters[0].Position, new Vector2(-20f, -10.6f)), 0.3f, "orange #1");
            Assert.Less(Vector2.Distance(flow.Fighters[1].Position, new Vector2(20f, -10.6f)), 0.3f, "blue #1");
            Assert.Less(Vector2.Distance(flow.Fighters[2].Position, new Vector2(-6f, -10.6f)), 0.3f, "orange #2 on spawns[2]");
            Assert.Less(Vector2.Distance(flow.Fighters[3].Position, new Vector2(6f, -10.6f)), 0.3f, "blue #2 on spawns[3]");
        }
    }
}

