using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Content;
using Metadream.Shoot.Network;
using Metadream.Shoot.Presentation;
using Metadream.Shoot.Tutorial;

namespace Metadream.Shoot.Tests
{
    public class MapScriptPlayModeTests
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
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include)) Object.Destroy(p.gameObject);
        }

        private static List<SlotSpec> Slots() => new List<SlotSpec>
        {
            new SlotSpec { Name = "A", Team = 0, Color = Palette.PlayerOne, Input = new DummyInput() },
            new SlotSpec { Name = "B", Team = 1, Color = Palette.PlayerTwo, Input = new DummyInput() }
        };

        private static Maps.MapDefinition Map(params string[] script)
        {
            var issues = new List<ContentIssue>();
            var lines = new List<string>();
            foreach (var l in script) lines.Add("\"" + l.Replace("\"", "\\\"") + "\"");
            string json = "{\"id\":\"t\",\"name\":\"T\",\"solids\":[{\"x\":0,\"y\":-14,\"w\":50,\"h\":4},{\"name\":\"door\",\"x\":0,\"y\":0,\"w\":2,\"h\":6}]," +
                          "\"spawns\":[{\"x\":-10,\"y\":-10},{\"x\":10,\"y\":-10}],\"areas\":[{\"name\":\"trap\",\"x\":10,\"y\":-10,\"w\":4,\"h\":4}]," +
                          "\"script\":[" + string.Join(",", lines) + "]}";
            var map = ContentDatabase.ParseMap(json, "t", "test", issues);
            Assert.IsNotNull(map, string.Join("\n", issues));
            return map;
        }

        [UnityTest]
        public IEnumerator ScriptMovesObjectsHurtsPlayersInAreasAndIsSentToClients()
        {
            yield return null;
            var flow = GameFlow.Instance;
            flow.StartSandbox(Map(
                "var entered = 0",
                "on start",
                "  move_by(door, 0, 5, 0.3)",
                "  hud(\"hello \" + players())",
                "end",
                "on enter trap",
                "  entered = entered + 1",
                "  damage(player, 30)",
                "end",
                "after 0.5",
                "  hide(door)",
                "  color(trap, \"#ff0000\")",
                "end"), Slots());
            yield return new WaitForSeconds(1.0f);
            var map = flow.CurrentMap;
            Assert.IsNull(map.Script.Error);
            var door = map.FindObject("door");
            Assert.AreEqual(5f, door.Center.y, 0.01f, "move_by tween finished");
            Assert.IsFalse(door.Visible);
            Assert.IsFalse(door.Collider.enabled, "hidden solids do not collide");
            Assert.AreEqual(flow.Fighters[1].MaxHealth - 30f, flow.Fighters[1].Health, 0.6f, "B stood in the trap once");
            Assert.AreEqual(flow.Fighters[0].MaxHealth, flow.Fighters[0].Health, 0.01f);
            Assert.AreEqual("hello 2", flow.MapHud);
            StringAssert.Contains("entered=1", map.Script.VariableSummary());

            var s = new MatchSnapshot();
            flow.CaptureSnapshot(s);
            Assert.AreEqual(2, s.MapObjects.Count);
            Assert.IsFalse(s.MapObjects[0].Visible);
            Assert.AreEqual(5f, s.MapObjects[0].Position.y, 0.01f);
            Assert.IsTrue(s.MapObjects[1].HasTint);
            Assert.AreEqual("hello 2", s.ScriptHud);

            // a client copy of the same map applies the state
            var mirror = Maps.MapRoot.Build(map.Definition, new Vector2(500f, 0f));
            mirror.SetSimulated(false);
            mirror.ApplyObjects(s.MapObjects);
            Assert.IsFalse(mirror.FindObject("door").Visible);
            Assert.AreEqual(5f, mirror.FindObject("door").Transform.localPosition.y, 0.01f);
            Object.Destroy(mirror.gameObject);
        }

        [UnityTest]
        public IEnumerator RunawayScriptIsStoppedAndTheMatchContinues()
        {
            yield return null;
            var flow = GameFlow.Instance;
            flow.StartSandbox(Map("on start", "  repeat 1000", "    repeat 1000", "      hud(\"x\")", "    end", "  end", "end", "every 0.1", "  hide(door)", "end"), Slots());
            yield return new WaitForSeconds(0.4f);
            var map = flow.CurrentMap;
            Assert.IsNotNull(map.Script.Error);
            StringAssert.Contains("too many steps", map.Script.Error);
            StringAssert.Contains("STOPPED", map.ScriptStatus());
            Assert.IsTrue(map.FindObject("door").Visible, "a stopped script runs nothing else");
            Assert.IsTrue(flow.HasMatch);
        }

        [UnityTest]
        public IEnumerator CSharpBehaviorsRunAndShootingANamedObjectFiresOnShot()
        {
            yield return null;
            var flow = GameFlow.Instance;
            flow.StartSandbox(ContentDatabase.FindMap("script_turret_core"), Slots());
            yield return new WaitForSeconds(1.2f);
            var map = flow.CurrentMap;
            Assert.Greater(Mathf.Abs(map.FindObject("blade").Rotation), 20f, "spin behavior rotates the blade");
            Assert.AreEqual("코어를 5번 맞히면 포탑이 멈춥니다", flow.MapHud);
            var core = map.FindObject("core");
            Projectile.Spawn(flow.Fighters[0], (Vector2)map.transform.position + core.Position + new Vector2(0f, -4f), Vector2.up, new GunStats());
            yield return new WaitForSeconds(0.4f);
            Assert.IsNull(map.Script.Error, map.Script.Error);
            Assert.AreEqual("코어 1/5", flow.MapHud);
            Assert.IsTrue(core.HasTint);
        }
    }
}

