using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;

namespace Metadream.MapEditor.Tests
{
    public class TeamSpawnTests
    {
        /// <summary>The script-block tests below cover games without native support (before 0f7e4bf).</summary>
        [SetUp]
        public void SetUp() => MapDocument.NativeTeamSpawnsOverride = false;

        [TearDown]
        public void TearDown() => MapDocument.NativeTeamSpawnsOverride = null;

        private static MapDocument Map()
        {
            var doc = MapDocument.CreateDefault();
            doc.Data.solids = new[]
            {
                new RectData { x = 0f, y = -14f, w = 60f, h = 6f },
                new RectData { x = -20f, y = 0f, w = 10f, h = 1.2f },
                new RectData { x = 20f, y = 0f, w = 10f, h = 1.2f }
            };
            doc.Normalize();
            return doc;
        }

        [Test]
        public void EnablingAddsTeammateSpawnsAtTheGameDefaultAndAValidScript()
        {
            var doc = Map();
            doc.Data.script = new[] { "on start", "  hud(\"hi\")", "end" };
            doc.SetTeamSpawns(true);
            Assert.AreEqual(4, doc.Count(ObjKind.Spawn));
            Assert.AreEqual(new Vector2(-12.4f, -9f), new Vector2(doc.Data.spawns[2].x, doc.Data.spawns[2].y));
            Assert.AreEqual(new Vector2(12.4f, -9f), new Vector2(doc.Data.spawns[3].x, doc.Data.spawns[3].y));
            Assert.IsTrue(doc.HasTeamSpawnScript);
            Assert.AreEqual("on start", doc.Data.script[0], "user script kept first");
            var issues = GameContent.Validate(doc, true, out var def);
            Assert.IsNotNull(def, string.Join("\n", issues));
            Assert.AreEqual(2, def.Script.Handlers.Count, "user on start + generated on tick");
            CollectionAssert.Contains(def.Script.VarNames, "mm_spawn2");
            CollectionAssert.Contains(def.Script.VarNames, "mm_spawn3");
            Assert.IsFalse(System.Array.Exists(doc.Data.script, l => l.Trim() == "on death"), "a respawns-card revive stays where it died (Fighter.PhoenixRevive)");
        }

        [Test]
        public void SyncedGameRuntimeHasNativeTeamSpawns()
        {
            // Synced game 0f7e4bf: MapRoot.SpawnPoint uses spawns[2]/[3] (Docs/GAME_PATCH_TEAM_SPAWNS.md applied).
            MapDocument.NativeTeamSpawnsOverride = null;
            Assert.IsTrue(MapDocument.GameNativeTeamSpawns);
            var doc = Map();
            doc.SetTeamSpawns(true);
            Assert.AreEqual(4, doc.Count(ObjKind.Spawn));
            Assert.IsFalse(doc.HasTeamSpawnScript, "no script block needed any more");
            Assert.IsTrue(doc.TeamSpawnsEnabled);
            var def = ContentDatabase.ParseMap(doc.ToJson(false), doc.Data.id, "t", new List<ContentIssue>());
            var root = MapRoot.Build(def, Vector2.zero);
            try
            {
                Assert.AreEqual(new Vector2(doc.Data.spawns[2].x, doc.Data.spawns[2].y), root.SpawnPoint(0, 1));
                Assert.AreEqual(new Vector2(doc.Data.spawns[3].x, doc.Data.spawns[3].y), root.SpawnPoint(1, 1));
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        [Test]
        public void WithNativeSupportNoScriptIsGeneratedAndOldBlocksAreRemoved()
        {
            var doc = Map();
            doc.SetTeamSpawns(true);
            Assert.IsTrue(doc.HasTeamSpawnScript);
            MapDocument.NativeTeamSpawnsOverride = true;
            Assert.IsTrue(doc.SyncTeamSpawnScript(), "existing block removed");
            Assert.IsFalse(doc.HasTeamSpawnScript);
            Assert.IsTrue(doc.TeamSpawnsEnabled, "enabled by spawns[2]/[3] alone");
            doc.SetTeamSpawns(false);
            Assert.AreEqual(2, doc.Count(ObjKind.Spawn), "off = no teammate spawns");
            doc.SetTeamSpawns(true);
            Assert.AreEqual(4, doc.Count(ObjKind.Spawn));
            Assert.IsFalse(doc.HasTeamSpawnScript);
        }

        [Test]
        public void MovingASpawnRegeneratesTheBlockAndDisablingRemovesIt()
        {
            var doc = Map();
            doc.SetTeamSpawns(true);
            doc.Data.spawns[2].x = -20f;
            doc.Data.spawns[2].y = 3f;
            Assert.IsTrue(doc.SyncTeamSpawnScript());
            Assert.IsTrue(System.Array.Exists(doc.Data.script, l => l.Trim() == "teleport(player, -20, 3)"));
            Assert.IsFalse(doc.SyncTeamSpawnScript(), "stable");
            doc.SetTeamSpawns(false);
            Assert.IsFalse(doc.HasTeamSpawnScript);
            Assert.AreEqual(0, doc.Data.script.Length);
            Assert.AreEqual(4, doc.Count(ObjKind.Spawn), "spawns stay (unused)");
        }

        [Test]
        public void TeammateSpawnInsideTerrainIsAGameValidationError()
        {
            var doc = Map();
            doc.SetTeamSpawns(true);
            doc.Data.spawns[3].x = 20f;
            doc.Data.spawns[3].y = 0f;
            doc.SyncTeamSpawnScript();
            var issues = GameContent.Validate(doc, true, out var def);
            Assert.IsNull(def);
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("spawns[3] overlaps")));
        }

        [Test]
        public void TwoSpawnMapsKeepTheOldPlacement()
        {
            var doc = Map();
            var def = ContentDatabase.ParseMap(doc.ToJson(false), doc.Data.id, "t", new List<ContentIssue>());
            var root = MapRoot.Build(def, Vector2.zero);
            try
            {
                Assert.AreEqual(new Vector2(-15f, -9f), root.SpawnPoint(0, 0));
                Assert.AreEqual(new Vector2(-12.4f, -9f), root.SpawnPoint(0, 1));
                Assert.AreEqual(new Vector2(15f, -9f), root.SpawnPoint(1, 0));
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        [Test]
        public void SwapSpawnsChangesRoles()
        {
            var doc = Map();
            doc.SetTeamSpawns(true);
            var a = doc.Data.spawns[0];
            doc.SwapSpawns(0, 2);
            Assert.AreSame(a, doc.Data.spawns[2]);
            Assert.AreEqual("주황 2 (2대2)", MapDocument.SpawnRole(2));
        }
    }
}
