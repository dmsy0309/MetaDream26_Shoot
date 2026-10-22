using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.MapEditor.EditorTools;

namespace Metadream.MapEditor.Tests
{
    public class MapDocumentTests
    {
        private string _tmp;

        [SetUp]
        public void SetUp()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "mapmaker-tests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TearDown]
        public void TearDown()
        {
            ContentDatabase.ExternalDirectory = null;
            ContentDatabase.Reload();
            if (_tmp != null && _tmp.StartsWith(Path.GetTempPath()) && Directory.Exists(_tmp)) Directory.Delete(_tmp, true);
        }

        [Test]
        public void BuiltinMapsRoundTripLosslesslyThroughTheGameLoader()
        {
            var failures = MapCompare.RoundTripBuiltins(out int count);
            Assert.GreaterOrEqual(count, 30, "built-in maps of the synced game runtime");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void PreviewDefinitionEqualsGameParseForBuiltins()
        {
            foreach (var t in Resources.LoadAll<TextAsset>("Content/Maps"))
            {
                var game = ContentDatabase.ParseMap(t.text, t.name, "builtin", new List<ContentIssue>());
                if (game == null) continue;
                var preview = MapDocument.FromJson(t.text, t.name).ToPreviewDefinition();
                string diff = MapCompare.Difference(game, preview);
                Assert.IsNull(diff, t.name + ": " + diff);
            }
        }

        [Test]
        public void SampleUsesEveryObjectTypeAndLoadsInGame()
        {
            var doc = MapEditorApp.SampleMap();
            foreach (ObjKind k in new[] { ObjKind.Solid, ObjKind.Box, ObjKind.Spawn, ObjKind.Mover, ObjKind.Pad, ObjKind.Hazard, ObjKind.Wind })
                Assert.Greater(doc.Count(k), 0, k.ToString());
            var issues = GameContent.Validate(doc, true, out var def);
            Assert.IsNotNull(def, string.Join("\n", issues));
            Assert.AreEqual(0, GameContent.ErrorCount(issues));
            Assert.AreEqual(1, def.Movers.Length);
            Assert.AreEqual(3, def.Zones.Length);
        }

        [Test]
        public void BackgroundIsKeptInTheFileAndIgnoredByTheGame()
        {
            var doc = MapEditorApp.SampleMap();
            string with = doc.ToJson(true), without = doc.ToJson(false);
            StringAssert.Contains("\"background\"", with);
            StringAssert.DoesNotContain("\"background\"", without);
            var a = ContentDatabase.ParseMap(with, doc.Data.id, "a", new List<ContentIssue>());
            var b = ContentDatabase.ParseMap(without, doc.Data.id, "b", new List<ContentIssue>());
            Assert.IsNull(MapCompare.Difference(a, b));
            var again = MapDocument.FromJson(with);
            Assert.IsTrue(again.Background.enabled);
            Assert.AreEqual(doc.Background.top, again.Background.top);
        }

        [Test]
        public void ValidationIsTheGameLoader()
        {
            var doc = MapDocument.CreateDefault();
            doc.Data.spawns = new[] { new PointData { x = 0f, y = -14f } };
            var issues = GameContent.Validate(doc, false, out var def);
            Assert.IsNull(def);
            var direct = new List<ContentIssue>();
            Assert.IsNull(ContentDatabase.ParseMap(doc.ToJson(false), doc.Data.id, doc.Data.id + ".json", direct));
            Assert.AreEqual(direct.Count, issues.Count);
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("at least 2 spawns")));
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("spawns[0] overlaps solids[0]")));
        }

        [Test]
        public void InvalidIdIsReportedAndSanitized()
        {
            Assert.AreEqual("my_cool_map", GameContent.SanitizeId("My Cool-Map"));
            var doc = MapDocument.CreateDefault();
            doc.Data.id = "My Map";
            var issues = GameContent.Validate(doc, false, out _);
            Assert.Greater(GameContent.ErrorCount(issues), 0);
        }

        [Test]
        public void EditingOperations()
        {
            var doc = MapDocument.CreateDefault();
            int solids = doc.Count(ObjKind.Solid);
            var box = doc.Add(ObjKind.Box, new Vector2(3f, 4f));
            var mover = doc.Add(ObjKind.Mover, new Vector2(0f, 0f));
            Assert.AreEqual(1, doc.Count(ObjKind.Box));
            Assert.AreEqual(2, doc.MoverAt(mover).path.Length);
            var copy = doc.Duplicate(box, new Vector2(1f, -1f));
            Assert.AreEqual(new Vector2(4f, 3f), doc.GetShape(copy).Center);
            var s = doc.GetShape(box);
            s.r = 30f; s.w = 5f;
            doc.SetShape(box, s);
            Assert.AreEqual(30f, doc.Data.boxes[0].r);
            doc.Remove(box);
            Assert.AreEqual(1, doc.Count(ObjKind.Box));
            Assert.AreEqual(solids, doc.Count(ObjKind.Solid));
            var json = doc.ToJson(true);
            var back = MapDocument.FromJson(json);
            Assert.AreEqual(json, back.ToJson(true));
        }

        [Test]
        public void NumbersRoundTripExactly()
        {
            var rng = new System.Random(7);
            for (int i = 0; i < 2000; i++)
            {
                float v = (float)((rng.NextDouble() - 0.5) * Mathf.Pow(10f, rng.Next(-3, 4)));
                string s = MapDocument.Num(v);
                Assert.AreEqual(v, float.Parse(s, CultureInfo.InvariantCulture), s);
            }
            Assert.AreEqual("0", MapDocument.Num(-0f));
            Assert.AreEqual("1.2", MapDocument.Num(1.2f));
            Assert.AreEqual("-14", MapDocument.Num(-14f));
        }

        [Test]
        public void InstallIntoGameContentFolderIsLoadedByTheGame()
        {
            var doc = MapEditorApp.SampleMap();
            string content = Path.Combine(_tmp, "Content");
            string path = GameContent.InstallToContentDir(doc, content, true, out bool replaced);
            Assert.IsFalse(replaced);
            Assert.AreEqual(Path.Combine(content, "Maps", "editor_sample.json"), path);
            ContentDatabase.ExternalDirectory = content;
            ContentDatabase.Reload();
            Assert.IsNotNull(ContentDatabase.FindMap("editor_sample"));
            Assert.IsFalse(new List<ContentIssue>(ContentDatabase.Issues).Exists(i => i.Severity == ContentSeverity.Error));
            doc.Data.name = "changed";
            GameContent.InstallToContentDir(doc, content, true, out replaced);
            Assert.IsTrue(replaced);
            Assert.IsTrue(File.Exists(path + ".bak"), "previous file kept");
        }

        [Test]
        public void GameRootResolution()
        {
            GameRootResolutionImpl();
        }

        [Test]
        public void EditedBuiltinMapOverridesTheBuiltinInTheGame()
        {
            // Open built-in "plateau" → move a platform, add 2v2 teammate spawns → apply to a game Content folder.
            var doc = MapDocument.FromJson(GameContent.BuiltinJson("plateau"), "plateau");
            doc.Data.solids[1].y = -2f;
            doc.SetTeamSpawns(true);
            doc.Data.spawns[2].x = -21f; doc.Data.spawns[2].y = 0.5f;
            doc.Data.spawns[3].x = 21f; doc.Data.spawns[3].y = 0.5f;
            doc.SyncTeamSpawnScript();
            var issues = GameContent.Validate(doc, false, out var def);
            Assert.IsNotNull(def, string.Join("\n", issues));

            ContentDatabase.ExternalDirectory = null;
            ContentDatabase.Reload();
            int builtinCount = ContentDatabase.Maps.Count;
            string builtinHash = ContentDatabase.Hash;

            string content = Path.Combine(_tmp, "Content");
            string path = GameContent.InstallToContentDir(doc, content, false, out _);
            Assert.AreEqual(Path.Combine(content, "Maps", "plateau.json"), path, "file name = id → replaces the built-in");
            ContentDatabase.ExternalDirectory = content;
            ContentDatabase.Reload();
            Assert.AreEqual(builtinCount, ContentDatabase.Maps.Count, "override, not an extra map");
            Assert.AreNotEqual(builtinHash, ContentDatabase.Hash, "content hash changes (online peers need the same file)");
            var all = new List<ContentIssue>(ContentDatabase.Issues);
            Assert.IsTrue(all.Exists(i => i.Source == path && i.Message.Contains("overrides built-in 'plateau'")));
            Assert.IsFalse(all.Exists(i => i.Source == path && i.Severity == ContentSeverity.Error));
            var loaded = ContentDatabase.FindMap("plateau");
            Assert.AreEqual(-2f, loaded.Solids[1].Center.y);
            Assert.AreEqual(4, loaded.Spawns.Length);
            Assert.IsNull(MapCompare.FullDifference(def, loaded));

            File.Delete(path);
            ContentDatabase.Reload();
            Assert.AreEqual(builtinHash, ContentDatabase.Hash, "removing the file restores the built-in map");
        }

        private void GameRootResolutionImpl()
        {
            string app = Path.Combine(_tmp, "MetadreamShoot.app");
            Directory.CreateDirectory(Path.Combine(app, "Contents", "MacOS"));
            File.WriteAllText(Path.Combine(app, "Contents", "MacOS", "Metadream Shoot"), "");
            Assert.AreEqual(Path.Combine(_tmp, "Content"), GameContent.ContentDirForGame(app));
            Assert.AreEqual(Path.Combine(app, "Contents", "MacOS", "Metadream Shoot"), GameLauncher.FindExecutable(app));
            string exe = Path.Combine(_tmp, "Win", "MetadreamShoot.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            File.WriteAllText(exe, "");
            Assert.AreEqual(Path.Combine(_tmp, "Win", "Content"), GameContent.ContentDirForGame(exe));
            Assert.AreEqual(exe, GameLauncher.FindExecutable(Path.Combine(_tmp, "Win")));
            Assert.AreEqual(Path.Combine(_tmp, "Win", "Content"), GameContent.ContentDirForGame(Path.Combine(_tmp, "Win", "Content")));
        }

        [Test]
        public void ExclusiveGameContentLeavesOnlyThisMapPlayable()
        {
            var doc = MapEditorApp.SampleMap();
            string dir = GameContent.PrepareExclusiveGameContent(doc, null);
            ContentDatabase.ExternalDirectory = dir;
            ContentDatabase.Reload();
            var playable = new List<string>();
            bool tutorial = false;
            foreach (var m in ContentDatabase.Maps) { if (m.TutorialOnly) tutorial = true; else playable.Add(m.Id); }
            CollectionAssert.AreEqual(new[] { "editor_sample" }, playable);
            Assert.IsTrue(tutorial, "tutorial map kept");
            Assert.Greater(ContentDatabase.Cards.Count, 0);
        }

        [Test]
        public void PackageContainsContentMapsFile()
        {
            var doc = MapEditorApp.SampleMap();
            string zip = GameContent.BuildPackage(doc, Path.Combine(_tmp, "p.zip"), true);
            using (var archive = ZipFile.OpenRead(zip))
            {
                var names = new List<string>();
                foreach (var e in archive.Entries) names.Add(e.FullName);
                CollectionAssert.Contains(names, "Content/Maps/editor_sample.json");
                using (var r = new StreamReader(archive.GetEntry("Content/Maps/editor_sample.json").Open()))
                    Assert.IsNotNull(ContentDatabase.ParseMap(r.ReadToEnd(), "editor_sample", "zip", new List<ContentIssue>()));
            }
        }

        [Test]
        public void GameRuntimeCopyIsByteIdenticalToTheGameProject()
        {
            string game = GameSync.ResolveGameProject();
            if (game == null || !Directory.Exists(game)) Assert.Ignore("game project not available on this machine");
            var diffs = GameSync.CompareCommitted(game);
            Assert.IsEmpty(diffs, string.Join("\n", diffs));
            var working = GameSync.Compare(game);
            if (working.Count > 0) Debug.Log("[GameSync] game has uncommitted runtime changes not in the editor: " + string.Join(", ", working));
            Assert.AreEqual(GameSync.TreeHash(GameSync.RuntimeFiles(System.IO.Path.GetDirectoryName(Application.dataPath))), GameRuntimeInfo.Current.runtimeSha256, "manifest matches the editor's runtime copy");
        }
    }
}
