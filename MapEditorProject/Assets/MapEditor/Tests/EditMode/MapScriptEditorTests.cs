using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps.Scripting;

namespace Metadream.MapEditor.Tests
{
    public class MapScriptEditorTests
    {
        [Test]
        public void ScriptSampleLoadsWithScriptBehaviorAndNames()
        {
            var doc = MapEditorApp.ScriptSampleMap();
            var issues = GameContent.Validate(doc, true, out var def);
            Assert.IsNotNull(def, string.Join("\n", issues));
            Assert.IsNotNull(def.Script);
            Assert.AreEqual(3, def.Script.Handlers.Count);
            CollectionAssert.AreEqual(new[] { "level" }, def.Script.VarNames);
            CollectionAssert.AreEqual(new[] { "blade", "lava", "launch" }, def.ObjectNames);
            Assert.AreEqual(1, def.Behaviors.Length);
            Assert.AreEqual(1, def.Areas.Length);
            CollectionAssert.AreEqual(def.ObjectNames, doc.NamesInGameOrder());
        }

        [Test]
        public void ScriptMapRoundTripsExactly()
        {
            var doc = MapEditorApp.ScriptSampleMap();
            var a = ContentDatabase.ParseMap(doc.ToJson(true), doc.Data.id, "a", new List<ContentIssue>());
            var again = MapDocument.FromJson(doc.ToJson(true));
            var b = ContentDatabase.ParseMap(again.ToJson(true), doc.Data.id, "b", new List<ContentIssue>());
            Assert.IsNull(MapCompare.FullDifference(a, b));
            Assert.AreEqual(doc.ToJson(true), again.ToJson(true));
        }

        [Test]
        public void ScriptErrorsComeFromTheGameParserWithLineNumbers()
        {
            var doc = MapEditorApp.ScriptSampleMap();
            doc.Data.script = new[] { "on start", "  move_by(lavaa, 0, 1)", "end" };
            var issues = GameContent.Validate(doc, true, out var def);
            Assert.IsNull(def);
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("script line 2") && i.Message.Contains("lavaa")), string.Join("\n", issues));
            doc.Data.script = new[] { "every 2", "  hide(lava)" };
            GameContent.Validate(doc, true, out def);
            Assert.IsNull(def, "missing end");
        }

        [Test]
        public void NamesAreValidatedByTheGame()
        {
            var doc = MapEditorApp.ScriptSampleMap();
            doc.Data.solids[0].name = "lava";
            var issues = GameContent.Validate(doc, true, out var def);
            Assert.IsNull(def);
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("used twice")));
            doc.Data.solids[0].name = "move";
            GameContent.Validate(doc, true, out def);
            Assert.IsNull(def, "reserved command name");
        }

        [Test]
        public void DuplicateAndPasteKeepNamesUnique()
        {
            var doc = MapEditorApp.ScriptSampleMap();
            var blade = doc.FindByName("blade");
            var copy = doc.Duplicate(blade, Vector2.one);
            Assert.AreEqual("blade_2", doc.GetName(copy));
            var copy2 = doc.Duplicate(copy, Vector2.one);
            Assert.AreEqual("blade_3", doc.GetName(copy2));
            var area = doc.Add(ObjKind.Area, Vector2.zero);
            Assert.AreEqual("area", doc.GetName(area));
            Assert.AreEqual("area_2", doc.GetName(doc.Add(ObjKind.Area, Vector2.one)));
            Assert.IsNotNull(ContentDatabase.ParseMap(doc.ToJson(true), doc.Data.id, "x", new List<ContentIssue>()));
        }

        [Test]
        public void BehaviorJsonOmitsOnlyDefaults()
        {
            var spec = new MapBehaviorSpec { type = "turret", target = "core", interval = 2.5f, delay = 1.5f, speed = 38f, damage = 18f };
            string json = MapDocument.BehaviorJson(spec);
            var back = JsonUtility.FromJson<MapBehaviorSpec>(json);
            Assert.AreEqual(JsonUtility.ToJson(spec), JsonUtility.ToJson(back));
            StringAssert.DoesNotContain("radius", json);
        }

        [Test]
        public void PreviewNeverCarriesScriptsButKeepsNamesAndAreas()
        {
            var preview = MapEditorApp.ScriptSampleMap().ToPreviewDefinition();
            Assert.IsNull(preview.Script);
            Assert.AreEqual(0, preview.Behaviors.Length);
            Assert.AreEqual(1, preview.Areas.Length);
            CollectionAssert.AreEqual(new[] { "blade", "lava", "launch" }, preview.ObjectNames);
        }

        [Test]
        public void EveryBuiltinBehaviorIsKnownToTheEditor()
        {
            var ids = new List<string>(MapBehaviorRegistry.Ids);
            CollectionAssert.IsSubsetOf(new[] { "spin", "blink", "rise", "turret", "bounce_area" }, ids);
        }
    }
}
