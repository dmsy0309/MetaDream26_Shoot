using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;

namespace Metadream.Shoot.Tests
{
    /// <summary>2v2 teammate spawns: spawns[2] = orange #2, spawns[3] = blue #2, else 2.6 units toward the centre.</summary>
    public class TeamSpawnTests
    {
        private static MapDefinition Map(string spawns, List<ContentIssue> issues = null)
        {
            issues = issues ?? new List<ContentIssue>();
            string json = "{\"id\":\"ts\",\"name\":\"TS\",\"solids\":[{\"x\":0,\"y\":-14,\"w\":60,\"h\":4}],\"spawns\":[" + spawns + "]}";
            var def = ContentDatabase.ParseMap(json, "ts", "test", issues);
            Assert.IsNotNull(def, string.Join("\n", issues));
            return def;
        }

        private static void WithRoot(MapDefinition def, System.Action<MapRoot> check)
        {
            var root = MapRoot.Build(def, Vector2.zero);
            try { check(root); }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        private const string Two = "{\"x\":-20,\"y\":-9},{\"x\":20,\"y\":-9}";

        [Test]
        public void TwoSpawnsKeepTheOldRule()
        {
            WithRoot(Map(Two), m =>
            {
                Assert.AreEqual(new Vector2(-20, -9), m.SpawnPoint(0, 0));
                Assert.AreEqual(new Vector2(20, -9), m.SpawnPoint(1, 0));
                Assert.AreEqual(new Vector2(-20 + 2.6f, -9), m.SpawnPoint(0, 1));
                Assert.AreEqual(new Vector2(20 - 2.6f, -9), m.SpawnPoint(1, 1));
            });
        }

        [Test]
        public void FourSpawnsGiveEachTeammateItsOwnSpawn()
        {
            WithRoot(Map(Two + ",{\"x\":-6,\"y\":-9},{\"x\":6,\"y\":-9}"), m =>
            {
                Assert.AreEqual(new Vector2(-20, -9), m.SpawnPoint(0, 0));
                Assert.AreEqual(new Vector2(20, -9), m.SpawnPoint(1, 0));
                Assert.AreEqual(new Vector2(-6, -9), m.SpawnPoint(0, 1));
                Assert.AreEqual(new Vector2(6, -9), m.SpawnPoint(1, 1));
            });
        }

        [Test]
        public void ThreeSpawnsOnlyMoveOrangeTwo()
        {
            WithRoot(Map(Two + ",{\"x\":-6,\"y\":-9}"), m =>
            {
                Assert.AreEqual(new Vector2(-6, -9), m.SpawnPoint(0, 1));
                Assert.AreEqual(new Vector2(20 - 2.6f, -9), m.SpawnPoint(1, 1), "blue #2 keeps the old rule");
            });
        }

        [Test]
        public void SpawnPointFollowsTheMapOffset()
        {
            var def = Map(Two + ",{\"x\":-6,\"y\":-9},{\"x\":6,\"y\":-9}");
            var root = MapRoot.Build(def, new Vector2(100f, 0f));
            try { Assert.AreEqual(new Vector2(106, -9), root.SpawnPoint(1, 1), "map origin (100, 0) + spawns[3] (6, -9)"); }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        [Test]
        public void MoreThanFourSpawnsWarns()
        {
            var issues = new List<ContentIssue>();
            Map(Two + ",{\"x\":-6,\"y\":-9},{\"x\":6,\"y\":-9},{\"x\":0,\"y\":-9}", issues);
            Assert.IsTrue(issues.Any(i => i.Severity == ContentSeverity.Warning && i.Message.Contains("spawns[4] and later are unused")), string.Join("\n", issues));
        }
    }
}

