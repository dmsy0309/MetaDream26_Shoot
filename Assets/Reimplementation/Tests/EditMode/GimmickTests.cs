using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Metadream.Shoot.Abilities;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;

namespace Metadream.Shoot.Tests
{
    public class GimmickTests
    {
        [Test]
        public void AllGimmickEffectsAreRegistered()
        {
            foreach (var id in new[] { "homing", "grow_damage", "slow_on_hit", "push_on_hit", "timed_bomb", "health_cost_on_shoot", "slow_aura", "drain_aura",
                                       "blast_on_block", "pull_on_block", "slow_on_block", "heal_on_block", "teleport_on_block", "dash_on_block",
                                       "reload_on_block", "empower_on_block", "block_on_empty", "reload_on_damage", "block_on_damage", "speed_after_damage" })
                Assert.IsTrue(EffectRegistry.IsKnown(id), id);
        }

        [Test]
        public void OriginalRoundsContentIsLoaded()
        {
            ContentDatabase.Reload();
            Assert.AreEqual(0, ContentDatabase.ErrorCount, string.Join("\n", ContentDatabase.Issues.Select(i => i.ToString())));
            Assert.GreaterOrEqual(ContentDatabase.Cards.Count(c => c.Id.StartsWith("rounds_")), 60);
            Assert.GreaterOrEqual(ContentDatabase.Maps.Count(m => m.Id.StartsWith("rounds_")), 30);
            Assert.Less(ContentDatabase.Cards.Count, 256, "card network index is a byte");
            var homing = ContentDatabase.FindCard("rounds_homing");
            Assert.AreEqual("homing", homing.Effects[0].type);
            Assert.IsTrue(ContentDatabase.Maps.Any(m => m.Movers.Length > 0), "original moving platforms converted");
            Assert.IsTrue(ContentDatabase.Maps.Any(m => m.Solids.Any(s => Mathf.Abs(s.Rotation) > 1f)), "rotated terrain converted");
        }

        [Test]
        public void MapGimmicksParseAndValidate()
        {
            var issues = new List<ContentIssue>();
            string ok = "{\"id\":\"g\",\"name\":\"G\",\"solids\":[{\"x\":0,\"y\":-14,\"w\":50,\"h\":4}],\"spawns\":[{\"x\":-10,\"y\":-9},{\"x\":10,\"y\":-9}]," +
                        "\"movers\":[{\"x\":0,\"y\":0,\"w\":6,\"h\":1,\"path\":[{\"x\":0,\"y\":0},{\"x\":0,\"y\":8}],\"speed\":4,\"wait\":0.5}]," +
                        "\"pads\":[{\"x\":5,\"y\":-11.7,\"w\":3,\"h\":0.6,\"power\":30}],\"hazards\":[{\"x\":0,\"y\":-18,\"w\":10,\"h\":2,\"dps\":50}],\"winds\":[{\"x\":0,\"y\":5,\"w\":6,\"h\":6,\"fy\":20}]}";
            var map = ContentDatabase.ParseMap(ok, "g", "test", issues);
            Assert.IsNotNull(map, string.Join("\n", issues));
            Assert.AreEqual(1, map.Movers.Length);
            Assert.AreEqual(3, map.Zones.Length);
            var m = map.Movers[0];
            Assert.AreEqual(Vector2.zero, m.OffsetAt(0.2f), "waits at the first point");
            Assert.AreEqual(new Vector2(0, 8), m.OffsetAt(0.5f + 2f + 0.1f), "arrives after wait + distance/speed");
            Assert.Less(Vector2.Distance(m.OffsetAt(1.3f), m.OffsetAt(1.3f + m.CycleTime)), 1e-3f, "loops");

            issues.Clear();
            Assert.IsNull(ContentDatabase.ParseMap(ok.Replace("\"power\":30", "\"power\":0").Replace("{\"x\":0,\"y\":0},{\"x\":0,\"y\":8}", "{\"x\":0,\"y\":0}"), "g", "test", issues));
            string all = string.Join("\n", issues);
            StringAssert.Contains("pads[0] needs power > 0", all);
            StringAssert.Contains("movers[0] needs at least 2 path points", all);
        }

        [Test]
        public void RotatedBlockContainsUsesRotation()
        {
            var b = new MapBlock(0, 0, 10, 1, 90);
            Assert.IsTrue(b.Contains(new Vector2(0, 4)));
            Assert.IsFalse(b.Contains(new Vector2(4, 0)));
        }
    }
}
