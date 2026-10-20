using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;
using Metadream.Shoot.Maps.Scripting;
using Metadream.Shoot.Network;

namespace Metadream.Shoot.Tests
{
    public class MapScriptTests
    {
        private const string Base = "{\"id\":\"s\",\"name\":\"S\",\"solids\":[{\"x\":0,\"y\":-14,\"w\":50,\"h\":4},{\"name\":\"door\",\"x\":0,\"y\":0,\"w\":2,\"h\":6}]," +
                                    "\"spawns\":[{\"x\":-10,\"y\":-9},{\"x\":10,\"y\":-9}],\"areas\":[{\"name\":\"trap\",\"x\":0,\"y\":-10,\"w\":4,\"h\":2}]";

        private static string Script(params string[] lines) => ",\"script\":[" + string.Join(",", lines.Select(l => "\"" + l.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "]";

        private static MapDefinition Parse(string tail, List<ContentIssue> issues) => ContentDatabase.ParseMap(Base + tail + "}", "s", "test", issues);

        private static string Errors(params string[] lines)
        {
            var issues = new List<ContentIssue>();
            var map = Parse(Script(lines), issues);
            Assert.IsNull(map, "should be rejected: " + string.Join(" / ", lines));
            return string.Join("\n", issues.Select(i => i.Message));
        }

        [Test]
        public void ValidScriptParses()
        {
            var issues = new List<ContentIssue>();
            var map = Parse(Script(
                "# comment",
                "var level = 0",
                "var label = \"x\"",
                "on start",
                "  hud(\"start \" + players())",
                "end",
                "every 2.5",
                "  level = level + 1",
                "  if level > 3 and door.visible",
                "    hide(door)",
                "  elif level == 2",
                "    door.y = door.y + 1",
                "  else",
                "    move_by(door, 0, -1, 0.5)",
                "  end",
                "end",
                "on enter trap",
                "  damage(player, 10)   // also a comment",
                "  push(player, 0, 30)",
                "end",
                "after 3",
                "  repeat 3",
                "    effect(\"ring\", random(-5, 5), 0, 2, \"#ff0000\")",
                "  end",
                "  foreach player",
                "    if player.hp < 50",
                "      heal(player, 5)",
                "    end",
                "  end",
                "end"), issues);
            Assert.IsNotNull(map, string.Join("\n", issues));
            Assert.IsNotNull(map.Script);
            Assert.AreEqual(4, map.Script.Handlers.Count);
            CollectionAssert.AreEqual(new[] { "level", "label" }, map.Script.VarNames);
            CollectionAssert.AreEqual(new[] { "door", "trap" }, map.ObjectNames);
            Assert.AreEqual(1, map.Areas.Length);
            Assert.AreEqual("door", map.SolidNames[1]);
        }

        [Test]
        public void SyntaxErrorsReportLineAndText()
        {
            var e = Errors("on start", "  move(window, 1, 2)", "end");
            StringAssert.Contains("script line 2", e);
            StringAssert.Contains("unknown name 'window'", e);
            StringAssert.Contains("named objects: door, trap", e);
            StringAssert.Contains("\"move(window, 1, 2)\"", e, "the offending line is quoted");

            StringAssert.Contains("missing its 'end'", Errors("on start", "  hide(door)"));
            StringAssert.Contains("'explode' takes 4–5 arguments", Errors("on start", "  explode(0, 0)", "end"));
            StringAssert.Contains("'player' only exists", Errors("on start", "  damage(player, 5)", "end"));
            StringAssert.Contains("unknown command or function 'teleport_all'", Errors("on start", "  teleport_all()", "end"));
            StringAssert.Contains("at the top level", Errors("hide(door)"));
            StringAssert.Contains("unknown name 'x'", Errors("on start", "  x = 1", "end"));
            StringAssert.Contains("'nowhere' is not a named object", Errors("on enter nowhere", "end"));
            StringAssert.Contains("returns a value", Errors("on start", "  abs(3)", "end"));
            StringAssert.Contains("closing", Errors("on start", "  hud(\"open)", "end"));
            StringAssert.Contains("cannot be set", Errors("on start", "  door.name = 3", "end"));
            StringAssert.Contains("unknown event", Errors("on jump", "end"));
        }

        [Test]
        public void NamesAndBehaviorsAreValidated()
        {
            var issues = new List<ContentIssue>();
            Assert.IsNull(ContentDatabase.ParseMap(Base.Replace("\"name\":\"trap\"", "\"name\":\"door\"") + "}", "s", "test", issues));
            StringAssert.Contains("used twice", string.Join("\n", issues.Select(i => i.Message)));

            issues.Clear();
            Assert.IsNull(ContentDatabase.ParseMap(Base.Replace("\"name\":\"trap\"", "\"name\":\"move\"") + "}", "s", "test", issues));
            StringAssert.Contains("reserved script word", string.Join("\n", issues.Select(i => i.Message)));

            issues.Clear();
            Assert.IsNull(Parse(",\"behaviors\":[{\"type\":\"teleporter\"},{\"type\":\"spin\"},{\"type\":\"spin\",\"target\":\"ghost\"}]", issues));
            string all = string.Join("\n", issues.Select(i => i.Message));
            StringAssert.Contains("behaviors[0]: unknown behavior type 'teleporter'", all);
            StringAssert.Contains("behaviors[1]: 'spin' needs \"target\"", all);
            StringAssert.Contains("behaviors[2]: target 'ghost' is not a named object", all);

            issues.Clear();
            Assert.IsNotNull(Parse(",\"behaviors\":[{\"type\":\"spin\",\"target\":\"door\",\"speed\":90},{\"type\":\"bounce_area\",\"area\":\"trap\"}]", issues), string.Join("\n", issues));
            foreach (var id in new[] { "spin", "blink", "rise", "turret", "bounce_area" }) Assert.IsTrue(MapBehaviorRegistry.IsKnown(id), id);
        }

        [Test]
        public void SnapshotCarriesNamedObjectsAndHud()
        {
            var s = new MatchSnapshot { Tick = 3, PickerRerolls = 1, ScriptHud = "용암 3초" };
            s.MapObjects.Add(new MapObjectState { Position = new Vector2(1, 2), Rotation = 30, Size = new Vector2(4, 1), Visible = false, HasTint = true, Tint = new Color32(255, 128, 0, 255) });
            var ms = new MemoryStream();
            s.Write(new BinaryWriter(ms));
            var back = new MatchSnapshot();
            ms.Position = 0;
            back.Read(new BinaryReader(ms));
            Assert.AreEqual("용암 3초", back.ScriptHud);
            Assert.AreEqual(1, back.MapObjects.Count);
            var o = back.MapObjects[0];
            Assert.AreEqual(new Vector2(1, 2), o.Position);
            Assert.AreEqual(30f, o.Rotation);
            Assert.IsFalse(o.Visible);
            Assert.IsTrue(o.HasTint);
            Assert.AreEqual(128, o.Tint.g);
            Assert.AreEqual(8, Protocol.Version);
        }

        [Test]
        public void ExampleScriptMapsLoad()
        {
            ContentDatabase.Reload();
            Assert.AreEqual(0, ContentDatabase.ErrorCount, string.Join("\n", ContentDatabase.Issues.Select(i => i.ToString())));
            var lava = ContentDatabase.FindMap("script_rising_lava");
            Assert.IsNotNull(lava?.Script, "rising lava script");
            Assert.IsNotNull(ContentDatabase.FindMap("script_switch_gate")?.Script, "switch gate script");
            var turret = ContentDatabase.FindMap("script_turret_core");
            Assert.AreEqual(3, turret.Behaviors.Length);
            Assert.IsNotNull(turret.Script);
        }
    }
}

