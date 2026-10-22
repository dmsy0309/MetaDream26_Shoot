using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;

namespace Metadream.MapEditor.Tests
{
    public class PlaytestPlayModeTests
    {
        private GameObject _cam, _app;
        private MapEditorApp _editor;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _cam = new GameObject("Main Camera") { tag = "MainCamera" };
            _cam.AddComponent<Camera>();
            _app = new GameObject("MapEditor");
            _editor = _app.AddComponent<MapEditorApp>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_editor != null && _editor.IsPlaying) _editor.Play.Stop();
            Object.Destroy(_app);
            Object.Destroy(_cam);
            foreach (var m in Object.FindObjectsByType<MapRoot>(FindObjectsInactive.Include)) Object.Destroy(m.gameObject);
            foreach (var go in GameObject.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include)) if (go.name.StartsWith("Editor")) Object.Destroy(go.gameObject);
            Time.timeScale = 1f;
            yield return null;
            ContentDatabase.ExternalDirectory = null;
            ContentDatabase.Reload();
        }

        private static MapDocument FlatMap()
        {
            var doc = MapDocument.CreateDefault();
            doc.Data.id = "playmode_flat";
            doc.Data.boxes = new[] { new RectData { x = 5f, y = -6f, w = 2f, h = 2f } };
            doc.Normalize();
            return doc;
        }

        [UnityTest]
        public IEnumerator EditorPreviewIsTheGamesMapRoot()
        {
            _editor.SetDocument(FlatMap(), false);
            yield return null;
            yield return null;
            var maps = Object.FindObjectsByType<MapRoot>(FindObjectsInactive.Exclude);
            Assert.AreEqual(1, maps.Length);
            Assert.AreEqual("playmode_flat", maps[0].Definition.Id);
            Assert.AreEqual(1, maps[0].Bodies.Count, "box rigidbody built by the game's MapRoot");
            Assert.IsFalse(maps[0].Bodies[0].simulated, "edit preview is frozen");
        }

        [UnityTest]
        public IEnumerator SandboxPlaytestRunsGameCodeAndFighterLandsOnTheFloor()
        {
            _editor.SetDocument(FlatMap(), false);
            yield return null;
            Assert.IsNull(_editor.StartPlaytest(Playtest.Kind.Sandbox, Playtest.Opponent.Dummy));
            Assert.IsTrue(_editor.IsPlaying);
            var flow = GameFlow.Instance;
            Assert.IsNotNull(flow);
            Assert.IsTrue(flow.IsSandbox);
            Assert.AreEqual(2, flow.Fighters.Count);
            Assert.AreEqual(0.02f, Time.fixedDeltaTime, 1e-6f);
            yield return null;
            var maps = Object.FindObjectsByType<MapRoot>(FindObjectsInactive.Exclude);
            Assert.AreEqual(1, maps.Length, "only the game's map is in the physics world");
            Assert.AreEqual("playmode_flat", flow.CurrentMap.Definition.Id);
            Assert.IsTrue(flow.CurrentMap.Bodies[0].simulated);
            yield return new WaitForSeconds(2.5f);
            var p1 = flow.Fighters[0];
            Assert.IsFalse(p1.IsDead);
            Assert.IsTrue(p1.IsGrounded, "fighter stands on the floor");
            float floorTop = -14f + 3f;
            Assert.AreEqual(floorTop + p1.Radius, p1.Position.y, 0.15f);
            Assert.AreEqual(-15f, p1.Position.x, 0.3f);
            var box = flow.CurrentMap.Bodies[0];
            Assert.Less(box.position.y, -6f + 0.01f, "box fell under game gravity");
            Assert.AreEqual(3f, box.gravityScale);
            _editor.Play.Stop();
            yield return null;
            yield return null;
            Assert.IsFalse(_editor.IsPlaying);
            Assert.IsNull(GameFlow.Instance);
            maps = Object.FindObjectsByType<MapRoot>(FindObjectsInactive.Exclude);
            Assert.AreEqual(1, maps.Length, "edit preview rebuilt");
            Assert.IsFalse(maps[0].Bodies[0].simulated);
        }

        [UnityTest]
        public IEnumerator CpuMatchUsesOnlyTheEditedMap()
        {
            _editor.SetDocument(MapEditorApp.SampleMap(), false);
            yield return null;
            Assert.IsNull(_editor.StartPlaytest(Playtest.Kind.CpuDemo, Playtest.Opponent.None));
            var flow = GameFlow.Instance;
            Assert.IsNotNull(flow.Match);
            Assert.AreEqual("editor_sample", flow.CurrentMap.Definition.Id);
            Time.timeScale = 4f;
            yield return new WaitForSecondsRealtime(4f);
            Assert.IsTrue(_editor.IsPlaying);
            Assert.AreEqual("editor_sample", GameFlow.Instance.CurrentMap.Definition.Id);
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator InvalidMapDoesNotStart()
        {
            var doc = MapDocument.CreateDefault();
            doc.Data.spawns = new PointData[0];
            _editor.SetDocument(doc, false);
            yield return null;
            LogAssert.ignoreFailingMessages = true;
            Assert.IsNotNull(_editor.StartPlaytest(Playtest.Kind.Sandbox, Playtest.Opponent.None));
            Assert.IsFalse(_editor.IsPlaying);
            Assert.IsNull(GameFlow.Instance);
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator EditPreviewDoesNotRunScripts()
        {
            _editor.SetDocument(MapEditorApp.ScriptSampleMap(), false);
            yield return null;
            yield return new WaitForSeconds(1f);
            var maps = Object.FindObjectsByType<MapRoot>(FindObjectsInactive.Exclude);
            Assert.AreEqual(1, maps.Length);
            Assert.IsFalse(maps[0].HasScripts, "preview carries no script");
            var blade = maps[0].FindObject("blade");
            Assert.IsNotNull(blade, "named object exists in preview");
            Assert.AreEqual(0f, blade.Rotation, 1e-4f, "spin behavior not running in the editor");
        }

        [UnityTest]
        public IEnumerator ScriptAndBehaviorRunInPlaytestThroughTheGame()
        {
            var doc = MapEditorApp.ScriptSampleMap();
            doc.Data.script = new[]
            {
                "var ran = 0",
                "on start",
                "  ran = 1",
                "  move(lava, 0, -15)",
                "  hud(\"editor playtest\")",
                "end"
            };
            _editor.SetDocument(doc, false);
            yield return null;
            Assert.IsNull(_editor.StartPlaytest(Playtest.Kind.Sandbox, Playtest.Opponent.Dummy));
            yield return new WaitForSeconds(1f);
            var map = GameFlow.Instance.CurrentMap;
            Assert.IsTrue(map.HasScripts);
            Assert.AreEqual(-15f, map.FindObject("lava").Center.y, 0.01f, "script moved the lava");
            Assert.AreEqual("editor playtest", map.HudText);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0f, map.FindObject("blade").Rotation)), 10f, "spin behavior rotated the blade");
            StringAssert.Contains("ran=", map.ScriptStatus());
        }

        [UnityTest]
        public IEnumerator DragSelectGroupsMoveDuplicateAndDelete()
        {
            var doc = MapDocument.CreateDefault();
            doc.Data.boxes = new[] { new RectData { x = -5f, y = 5f, w = 2f, h = 2f }, new RectData { x = 5f, y = 5f, w = 2f, h = 2f } };
            doc.Normalize();
            _editor.SetDocument(doc, false);
            yield return null;
            // Left → right: only objects fully inside (the two boxes and the small platform at y = -2).
            _editor.FinishMarquee(new Vector2(-10f, -4f), new Vector2(10f, 8f), false);
            Assert.AreEqual(3, _editor.SelectionSet.Count);
            // Right → left: also touches the big floor (y -17 .. -11).
            _editor.FinishMarquee(new Vector2(10f, -12f), new Vector2(-10f, 8f), false);
            Assert.AreEqual(4, _editor.SelectionSet.Count);
            _editor.FinishMarquee(new Vector2(-10f, -4f), new Vector2(10f, 8f), false);
            _editor.MoveSelectionBy(new Vector2(1f, 2f));
            Assert.AreEqual(new Vector2(-4f, 7f), new Vector2(_editor.Doc.Data.boxes[0].x, _editor.Doc.Data.boxes[0].y));
            Assert.AreEqual(new Vector2(6f, 7f), new Vector2(_editor.Doc.Data.boxes[1].x, _editor.Doc.Data.boxes[1].y));
            Assert.AreEqual(1f, _editor.Doc.Data.solids[1].x, 1e-4f);
            Assert.AreEqual(-14f, _editor.Doc.Data.solids[0].y, 1e-4f, "floor not selected, not moved");
            _editor.DuplicateSelection();
            Assert.AreEqual(4, _editor.Doc.Count(ObjKind.Box));
            Assert.AreEqual(3, _editor.SelectionSet.Count, "copies are selected");
            _editor.DeleteSelection();
            Assert.AreEqual(2, _editor.Doc.Count(ObjKind.Box));
            Assert.AreEqual(2, _editor.Doc.Count(ObjKind.Solid));
            Assert.AreEqual(0, _editor.SelectionSet.Count);
            _editor.Undo();
            Assert.AreEqual(4, _editor.Doc.Count(ObjKind.Box), "undo restores the deleted group");
        }

        [UnityTest]
        public IEnumerator TwoVsTwoTeammatesStartAtTheirOwnSpawns()
        {
            var doc = MapDocument.CreateDefault();
            doc.Data.id = "playmode_teams";
            doc.Data.solids = new[] { new RectData { x = 0f, y = -14f, w = 64f, h = 6f } };
            doc.Normalize();
            doc.SetTeamSpawns(true);
            doc.Data.spawns[2].x = -28f;
            doc.Data.spawns[3].x = 28f;
            doc.SyncTeamSpawnScript();
            _editor.SetDocument(doc, false);
            yield return null;
            Assert.IsNull(_editor.StartPlaytest(Playtest.Kind.Sandbox, Playtest.Opponent.Teams));
            var flow = GameFlow.Instance;
            Assert.AreEqual(4, flow.Fighters.Count);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var f2 = flow.Fighters[2];
            var f3 = flow.Fighters[3];
            Assert.AreEqual(2, f2.PlayerId);
            Assert.AreEqual(0, f2.TeamId);
            Assert.Less(Mathf.Abs(f2.Position.x - -28f), 1.5f, "orange #2 teleported to spawns[2] by the game's script runtime");
            Assert.Less(Mathf.Abs(f3.Position.x - 28f), 1.5f, "blue #2 at spawns[3]");
            Assert.Less(Mathf.Abs(flow.Fighters[0].Position.x - -15f), 1.5f, "orange #1 stays at spawns[0]");
        }

        [UnityTest]
        public IEnumerator ReadyBannerAlreadyShowsTeammatesAtTheirSpawns()
        {
            var doc = MapDocument.CreateDefault();
            doc.Data.id = "playmode_ready";
            doc.Data.solids = new[] { new RectData { x = 0f, y = -14f, w = 64f, h = 6f } };
            doc.Normalize();
            doc.SetTeamSpawns(true);
            doc.Data.spawns[2].x = -28f;
            doc.Data.spawns[3].x = 28f;
            doc.SyncTeamSpawnScript();
            Assert.IsFalse(doc.HasTeamSpawnScript, "native support: plain spawns[2]/[3]");
            _editor.SetDocument(doc, false);
            yield return null;
            Assert.IsNull(_editor.StartPlaytest(Playtest.Kind.CpuDemoTeams, Playtest.Opponent.None));
            var flow = GameFlow.Instance;
            float until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until && !(flow.Phase == Metadream.Shoot.Core.MatchPhase.Battle && !flow.Fighters[2].IsPlaying && !flow.Fighters[2].IsDead)) yield return null;
            Assert.AreEqual(Metadream.Shoot.Core.MatchPhase.Battle, flow.Phase);
            Assert.IsFalse(flow.Fighters[2].IsPlaying, "still in the '준비' banner");
            Assert.Less(Vector2.Distance(flow.Fighters[2].Position, new Vector2(-28f, -9f)), 0.3f, "orange #2 placed at spawns[2] before the fight starts");
            Assert.Less(Vector2.Distance(flow.Fighters[3].Position, new Vector2(28f, -9f)), 0.3f, "blue #2 at spawns[3]");
        }
    }
}
