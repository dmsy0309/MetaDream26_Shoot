using System.Collections.Generic;
using NUnit.Framework;
using Metadream.Shoot.Core;

namespace Metadream.Shoot.Tests
{
    public class MatchRulesTests
    {
        private static MatchStateMachine NewMatch(bool pick = true)
        {
            var m = new MatchStateMachine(new MatchSettings { pickPhase = pick });
            m.Join(0, 0);
            m.Join(1, 1);
            return m;
        }

        private static void FinishPicks(MatchStateMachine m)
        {
            while (m.Phase == MatchPhase.InitialCardPick || m.Phase == MatchPhase.LoserCardPick)
                Assert.IsTrue(m.CompletePick(m.CurrentPicker));
        }

        [Test]
        public void ConstructorValuesMatchObservedDefaults()
        {
            var s = new MatchSettings();
            Assert.AreEqual(2, s.playersNeededToStart);
            Assert.AreEqual(2, s.pointsToWinRound);
            Assert.AreEqual(5, s.roundsToWinGame);
            Assert.IsTrue(s.pickPhase);
        }

        [Test]
        public void CannotStartBelowPlayersNeeded()
        {
            var m = new MatchStateMachine(new MatchSettings());
            m.Join(0, 0);
            Assert.IsFalse(m.StartMatch());
            Assert.AreEqual(MatchPhase.WaitingForPlayers, m.Phase);
        }

        [Test]
        public void StartGivesEveryPlayerOneInitialPickInOrder()
        {
            var m = NewMatch();
            Assert.IsTrue(m.StartMatch());
            Assert.AreEqual(MatchPhase.InitialCardPick, m.Phase);
            Assert.AreEqual(0, m.CurrentPicker);
            Assert.IsFalse(m.CompletePick(1), "out-of-order pick must be rejected");
            Assert.IsTrue(m.CompletePick(0));
            Assert.AreEqual(1, m.CurrentPicker);
            Assert.IsTrue(m.CompletePick(1));
            Assert.AreEqual(MatchPhase.Battle, m.Phase);
        }

        [Test]
        public void FirstPointIsPointTransitionWithoutCardPick()
        {
            var m = NewMatch();
            m.StartMatch();
            FinishPicks(m);
            var r = m.ReportAliveTeams(new[] { 0 });
            Assert.AreEqual(PointResultKind.Point, r.Kind);
            Assert.AreEqual(MatchPhase.PointTransition, m.Phase);
            Assert.AreEqual(1, m.Score.Points(0));
            Assert.AreEqual(0, m.Score.Rounds(0));
            Assert.IsTrue(m.FinishTransition());
            Assert.AreEqual(MatchPhase.Battle, m.Phase, "point transitions never open a card pick");
        }

        [Test]
        public void SecondPointWinsRoundResetsPointsAndOnlyLoserPicks()
        {
            var m = NewMatch();
            m.StartMatch();
            FinishPicks(m);
            m.ReportAliveTeams(new[] { 0 });
            m.FinishTransition();
            m.ReportAliveTeams(new[] { 1 });
            m.FinishTransition();
            Assert.AreEqual(1, m.Score.Points(0));
            Assert.AreEqual(1, m.Score.Points(1));
            var r = m.ReportAliveTeams(new[] { 0 });
            Assert.AreEqual(PointResultKind.Round, r.Kind);
            Assert.AreEqual(1, m.Score.Rounds(0));
            Assert.AreEqual(0, m.Score.Points(0));
            Assert.AreEqual(0, m.Score.Points(1), "RoundOver resets both teams' points");
            Assert.AreEqual(MatchPhase.RoundTransition, m.Phase);
            m.FinishTransition();
            Assert.AreEqual(MatchPhase.LoserCardPick, m.Phase);
            Assert.AreEqual(1, m.CurrentPicker);
            Assert.AreEqual(1, m.PendingPickCount);
            Assert.IsTrue(m.CompletePick(1));
            Assert.AreEqual(MatchPhase.Battle, m.Phase);
        }

        [Test]
        public void MatchEndsExactlyOnceAtRoundsToWin()
        {
            var m = NewMatch(pick: false);
            m.StartMatch();
            int matchResults = 0;
            for (int round = 0; round < 5; round++)
            {
                for (int point = 0; point < 2; point++)
                {
                    var r = m.ReportAliveTeams(new[] { 1 });
                    if (r.Kind == PointResultKind.Match) matchResults++;
                    if (m.Phase != MatchPhase.GameOver) m.FinishTransition();
                }
            }
            Assert.AreEqual(1, matchResults);
            Assert.AreEqual(MatchPhase.GameOver, m.Phase);
            Assert.AreEqual(1, m.WinningTeam);
            Assert.AreEqual(5, m.Score.Rounds(1));
            Assert.AreEqual(PointResultKind.Ignored, m.ReportAliveTeams(new[] { 1 }).Kind);
        }

        [Test]
        public void DuplicateDeathReportsDoNotDoubleCount()
        {
            var m = NewMatch(pick: false);
            m.StartMatch();
            Assert.AreEqual(PointResultKind.Point, m.ReportAliveTeams(new[] { 0 }).Kind);
            Assert.AreEqual(PointResultKind.Ignored, m.ReportAliveTeams(new[] { 0 }).Kind);
            Assert.AreEqual(1, m.Score.Points(0));
        }

        [Test]
        public void SeveralTeamsAliveIsIgnoredAndNoTeamIsDraw()
        {
            var m = NewMatch(pick: false);
            m.StartMatch();
            Assert.AreEqual(PointResultKind.Ignored, m.ReportAliveTeams(new[] { 0, 1 }).Kind);
            Assert.AreEqual(MatchPhase.Battle, m.Phase);
            Assert.AreEqual(PointResultKind.Draw, m.ReportAliveTeams(new List<int>()).Kind);
            Assert.AreEqual(0, m.Score.Points(0));
            Assert.AreEqual(0, m.Score.Points(1));
            m.FinishTransition();
            Assert.AreEqual(MatchPhase.Battle, m.Phase);
        }

        [Test]
        public void RematchResetsScore()
        {
            var m = NewMatch(pick: false);
            m.StartMatch();
            m.ReportAliveTeams(new[] { 0 });
            m.ResetToLobby();
            Assert.IsTrue(m.StartMatch());
            Assert.AreEqual(0, m.Score.Points(0));
            Assert.AreEqual(0, m.Score.Rounds(0));
        }

        [Test]
        public void TwoVersusTwoScoresPerTeamAndWholeLosingTeamPicks()
        {
            var m = new MatchStateMachine(new MatchSettings());
            for (int i = 0; i < 4; i++) m.Join(i, i % 2);
            Assert.AreEqual(2, m.TeamCount);
            m.StartMatch();
            Assert.AreEqual(4, m.PendingPickCount, "every player gets an initial pick");
            FinishPicks(m);
            // Team 1 survives twice: team 0 (players 0 and 2) lose the round.
            m.ReportAliveTeams(new[] { 1 });
            m.FinishTransition();
            var r = m.ReportAliveTeams(new[] { 1 });
            Assert.AreEqual(PointResultKind.Round, r.Kind);
            m.FinishTransition();
            Assert.AreEqual(MatchPhase.LoserCardPick, m.Phase);
            Assert.AreEqual(2, m.PendingPickCount);
            Assert.AreEqual(0, m.CurrentPicker);
            Assert.IsTrue(m.CompletePick(0));
            Assert.AreEqual(2, m.CurrentPicker);
            Assert.IsTrue(m.CompletePick(2));
            Assert.AreEqual(MatchPhase.Battle, m.Phase);
        }
    }
}

