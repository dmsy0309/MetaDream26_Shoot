using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Core;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Tests
{
    public class MatchPlayModeTests
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
            var flow = GameFlow.Instance;
            if (flow != null) flow.ReturnToTitle();
            Object.Destroy(_root);
            Object.Destroy(_cam);
            foreach (var m in Object.FindObjectsByType<Maps.MapRoot>(FindObjectsInactive.Include)) Object.Destroy(m.gameObject);
        }

        [UnityTest]
        public IEnumerator CpuMatchPlaysToCompletionWithLoserOnlyPicks()
        {
            yield return null;
            var flow = GameFlow.Instance;
            Assert.IsNotNull(flow);
            var settings = new MatchSettings { roundsToWinGame = 2 };
            flow.StartMatch(GameMode.CpuDemo, settings, 1234);
            float deadline = Time.realtimeSinceStartup + 400f;
            while (flow.Match.Phase != MatchPhase.GameOver && Time.realtimeSinceStartup < deadline)
            {
                if (Time.timeScale >= 0.99f) Time.timeScale = 4f;
                yield return null;
            }
            Time.timeScale = 1f;
            Assert.AreEqual(MatchPhase.GameOver, flow.Match.Phase, "match did not finish in time. History: " + string.Join(" | ", flow.History));
            var m = flow.Match;
            int winner = m.WinningTeam;
            Assert.AreEqual(2, m.Score.Rounds(winner));
            foreach (var f in flow.Fighters)
            {
                int opponentRounds = m.Score.Rounds(1 - f.TeamId);
                // The round that ends the match goes straight to GameOver without a pick.
                int picksFromLosses = opponentRounds - (1 - f.TeamId == winner ? 1 : 0);
                Assert.AreEqual(1 + picksFromLosses, f.Cards.Count,
                    f.DisplayName + " should own one initial card plus one per non-final round lost. " + flow.CardSummary());
            }
            Assert.GreaterOrEqual(flow.PointsPlayed, 3);
            Debug.Log("[Test] points=" + flow.PointsPlayed + " " + flow.CardSummary() + " history=" + string.Join(" | ", flow.History));
        }

        [UnityTest]
        public IEnumerator ProjectileDamagesAndBlockReflects()
        {
            yield return null;
            var a = Fighter.Create(10, 0, Palette.PlayerOne, "A", null);
            var b = Fighter.Create(11, 1, Palette.PlayerTwo, "B", null);
            a.Revive(new Vector2(-6f, 0f), true);
            b.Revive(new Vector2(6f, 0f), true);
            a.SetPlaying(true);
            b.SetPlaying(true);
            a.Stats.gun.gravity = 0f;
            yield return null;

            float hp = b.Health;
            Projectile.Spawn(a, new Vector2(-4f, 0f), Vector2.right, a.Stats.gun);
            yield return new WaitForSeconds(0.4f);
            Assert.Less(b.Health, hp, "unblocked projectile deals damage");

            Assert.IsTrue(b.TryBlock() || b.Block.IsReady(b.Stats.block.EffectiveCooldown) == false);
            // The first hit applies knockback: start relative to the target's current position.
            var p = Projectile.Spawn(a, b.Position - Vector2.right * 1.5f, Vector2.right, a.Stats.gun);
            float before = b.Health;
            yield return new WaitForSeconds(0.05f);
            Assert.AreEqual(before, b.Health, 1e-4f, "blocked projectile deals no damage");
            if (p != null) Assert.Less(p.Velocity.x, 0f, "blocked projectile reverses toward the shooter");
            Object.Destroy(a.gameObject);
            Object.Destroy(b.gameObject);
            Projectile.ClearAll();
        }

        [UnityTest]
        public IEnumerator TwoVersusTwoCpuMatchCompletes()
        {
            yield return null;
            var flow = GameFlow.Instance;
            flow.StartMatch(GameMode.CpuDemoTeams, new MatchSettings { roundsToWinGame = 2 }, 99);
            Assert.AreEqual(4, flow.Fighters.Count);
            Assert.AreEqual(2, flow.TeamSize);
            float deadline = Time.realtimeSinceStartup + 400f;
            while (flow.Match.Phase != MatchPhase.GameOver && Time.realtimeSinceStartup < deadline)
            {
                if (Time.timeScale >= 0.99f) Time.timeScale = 4f;
                yield return null;
            }
            Time.timeScale = 1f;
            Assert.AreEqual(MatchPhase.GameOver, flow.Match.Phase, "2v2 match did not finish. History: " + string.Join(" | ", flow.History));
            var m = flow.Match;
            int winner = m.WinningTeam;
            foreach (var f in flow.Fighters)
            {
                int lost = m.Score.Rounds(1 - f.TeamId) - (1 - f.TeamId == winner ? 1 : 0);
                Assert.AreEqual(1 + lost, f.Cards.Count, f.DisplayName + " " + flow.CardSummary());
            }
            Debug.Log("[Test] 2v2 points=" + flow.PointsPlayed + " " + flow.CardSummary());
        }

        [UnityTest]
        public IEnumerator TeammateBulletsPassThrough()
        {
            yield return null;
            var a = Fighter.Create(20, 0, Palette.PlayerOne, "A", null);
            var mate = Fighter.Create(21, 0, Palette.PlayerThree, "Mate", null);
            var foe = Fighter.Create(22, 1, Palette.PlayerTwo, "Foe", null);
            a.Revive(new Vector2(-8f, 0f), true);
            mate.Revive(new Vector2(-2f, 0f), true);
            foe.Revive(new Vector2(4f, 0f), true);
            a.SetPlaying(true);
            mate.SetPlaying(true);
            foe.SetPlaying(true);
            a.Stats.gun.gravity = 0f;
            yield return null;
            float mateHp = mate.Health;
            Projectile.Spawn(a, new Vector2(-6.5f, mate.Position.y), Vector2.right, a.Stats.gun);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(mateHp, mate.Health, 1e-4f, "no friendly fire");
            Object.Destroy(a.gameObject);
            Object.Destroy(mate.gameObject);
            Object.Destroy(foe.gameObject);
            Projectile.ClearAll();
        }
    }
}

