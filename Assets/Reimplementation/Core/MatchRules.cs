using System;
using System.Collections.Generic;

namespace Metadream.Shoot.Core
{
    /// <summary>
    /// Match phases. Names are a development decision (see ROUNDS_REVERSE_DEVELOPMENT.md §5.1);
    /// the transitions mirror the observed GM_ArmsRace flow.
    /// </summary>
    public enum MatchPhase
    {
        WaitingForPlayers,
        InitialCardPick,
        Battle,
        PointTransition,
        RoundTransition,
        LoserCardPick,
        GameOver
    }

    public enum PointResultKind
    {
        /// <summary>Report was ignored (not in battle, already transitioning, or several teams alive).</summary>
        Ignored,
        /// <summary>No team alive. Original behaviour is UNKNOWN-04; this build replays the point without scoring.</summary>
        Draw,
        Point,
        Round,
        Match
    }

    [Serializable]
    public sealed class MatchSettings
    {
        // Constructor values observed in GM_ArmsRace..ctor (0x0600027d). Serialized scene values are UNKNOWN-01.
        public int playersNeededToStart = 2;
        public int pointsToWinRound = 2;
        public int roundsToWinGame = 5;
        public bool pickPhase = true;
        // Number of cards shown per pick is UNKNOWN-03 (CardChoice.picks=6 is not the shown count). Provisional.
        public int cardsPerPick = 5;
        /// <summary>Map ids used in rotation (empty = every non-tutorial map).</summary>
        public string[] mapPool = new string[0];
        /// <summary>Card ids that can be offered (empty = every card).</summary>
        public string[] cardPool = new string[0];
        /// <summary>Starting stats of every fighter.</summary>
        public BaseStats stats = new BaseStats();
    }

    /// <summary>
    /// Starting fighter stats. Defaults equal the values used so far (constructor values where observed,
    /// provisional values otherwise; see the report). Editable from server.json "rules.stats".
    /// </summary>
    [Serializable]
    public sealed class BaseStats
    {
        public float maxHealth = 100f;
        public float bulletDamage = 55f;
        public float bulletSpeed = 70f;
        public float bulletGravity = 1f;
        public float attackCooldown = 0.3f;
        public int maxAmmo = 3;
        public float reloadTime = 1.5f;
        public float blockCooldown = 4f;
        public float movementSpeed = 1f;
        public int jumps = 1;
    }

    public readonly struct PointResult
    {
        public readonly PointResultKind Kind;
        public readonly int WinnerTeam;

        public PointResult(PointResultKind kind, int winnerTeam)
        {
            Kind = kind;
            WinnerTeam = winnerTeam;
        }

        public override string ToString() => Kind + "(team " + WinnerTeam + ")";
    }

    /// <summary>Points and rounds are stored separately, as in GM_ArmsRace (p1Points/p2Points, p1Rounds/p2Rounds).</summary>
    public sealed class MatchScore
    {
        private readonly MatchSettings _settings;
        private readonly int[] _points;
        private readonly int[] _rounds;

        public MatchScore(MatchSettings settings, int teamCount)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (teamCount < 1) throw new ArgumentOutOfRangeException(nameof(teamCount));
            _points = new int[teamCount];
            _rounds = new int[teamCount];
        }

        public int TeamCount => _points.Length;
        public int Points(int team) => _points[team];
        public int Rounds(int team) => _rounds[team];

        public void Reset()
        {
            Array.Clear(_points, 0, _points.Length);
            Array.Clear(_rounds, 0, _rounds.Length);
        }

        /// <summary>
        /// RPCA_NextRound order: add a point to the winner; on reaching pointsToWinRound add a round and
        /// reset every team's points (RoundOver); on reaching roundsToWinGame the match ends.
        /// </summary>
        public PointResult AwardPoint(int winningTeam)
        {
            if (winningTeam < 0 || winningTeam >= _points.Length) throw new ArgumentOutOfRangeException(nameof(winningTeam));
            _points[winningTeam]++;
            if (_points[winningTeam] < _settings.pointsToWinRound)
                return new PointResult(PointResultKind.Point, winningTeam);

            _rounds[winningTeam]++;
            Array.Clear(_points, 0, _points.Length);
            return _rounds[winningTeam] >= _settings.roundsToWinGame
                ? new PointResult(PointResultKind.Match, winningTeam)
                : new PointResult(PointResultKind.Round, winningTeam);
        }
    }

    public readonly struct Participant
    {
        public readonly int PlayerId;
        public readonly int TeamId;

        public Participant(int playerId, int teamId)
        {
            PlayerId = playerId;
            TeamId = teamId;
        }
    }

    /// <summary>
    /// Pure match state machine. One transition lock (isTransitioning in the original) guarantees a
    /// point/round/match is decided exactly once per battle.
    /// </summary>
    public sealed class MatchStateMachine
    {
        private readonly List<Participant> _participants = new List<Participant>();
        private readonly Queue<int> _pickQueue = new Queue<int>();
        private MatchScore _score;

        public MatchStateMachine(MatchSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public MatchSettings Settings { get; }
        public MatchPhase Phase { get; private set; } = MatchPhase.WaitingForPlayers;
        public bool IsTransitioning { get; private set; }
        public MatchScore Score => _score;
        public PointResult LastResult { get; private set; }
        public IReadOnlyList<Participant> Participants => _participants;
        public int CurrentPicker => _pickQueue.Count > 0 ? _pickQueue.Peek() : -1;
        public int PendingPickCount => _pickQueue.Count;
        public int WinningTeam { get; private set; } = -1;

        public event Action<MatchPhase, MatchPhase> PhaseChanged;

        public void Join(int playerId, int teamId)
        {
            if (Phase != MatchPhase.WaitingForPlayers) throw new InvalidOperationException("Players can only join while waiting.");
            foreach (var p in _participants)
                if (p.PlayerId == playerId) throw new InvalidOperationException("Duplicate player " + playerId);
            _participants.Add(new Participant(playerId, teamId));
        }

        public int TeamCount
        {
            get
            {
                int max = -1;
                foreach (var p in _participants) max = Math.Max(max, p.TeamId);
                return max + 1;
            }
        }

        public bool StartMatch()
        {
            if (Phase != MatchPhase.WaitingForPlayers || _participants.Count < Settings.playersNeededToStart) return false;
            _score = new MatchScore(Settings, TeamCount);
            WinningTeam = -1;
            IsTransitioning = false;
            _pickQueue.Clear();
            if (Settings.pickPhase)
            {
                // DoStartGame: CardChoice.DoPick(1, playerID, ...) for every player in order.
                foreach (var p in _participants) _pickQueue.Enqueue(p.PlayerId);
                SetPhase(MatchPhase.InitialCardPick);
            }
            else
            {
                SetPhase(MatchPhase.Battle);
            }
            return true;
        }

        public bool CompletePick(int playerId)
        {
            if (Phase != MatchPhase.InitialCardPick && Phase != MatchPhase.LoserCardPick) return false;
            if (_pickQueue.Count == 0 || _pickQueue.Peek() != playerId) return false;
            _pickQueue.Dequeue();
            if (_pickQueue.Count == 0) SetPhase(MatchPhase.Battle);
            return true;
        }

        /// <summary>Called whenever a player dies; aliveTeams lists the teams that still have a living player.</summary>
        public PointResult ReportAliveTeams(IReadOnlyCollection<int> aliveTeams)
        {
            if (Phase != MatchPhase.Battle || IsTransitioning || aliveTeams == null || aliveTeams.Count > 1)
                return new PointResult(PointResultKind.Ignored, -1);

            IsTransitioning = true;
            if (aliveTeams.Count == 0)
            {
                LastResult = new PointResult(PointResultKind.Draw, -1);
                SetPhase(MatchPhase.PointTransition);
                return LastResult;
            }

            int winner = -1;
            foreach (var t in aliveTeams) winner = t;
            LastResult = _score.AwardPoint(winner);
            switch (LastResult.Kind)
            {
                case PointResultKind.Point:
                    SetPhase(MatchPhase.PointTransition);
                    break;
                case PointResultKind.Round:
                    _pickQueue.Clear();
                    // RoundTransition: the players of the killed (losing) team pick.
                    foreach (var p in _participants)
                        if (p.TeamId != winner) _pickQueue.Enqueue(p.PlayerId);
                    SetPhase(MatchPhase.RoundTransition);
                    break;
                case PointResultKind.Match:
                    WinningTeam = winner;
                    SetPhase(MatchPhase.GameOver);
                    break;
            }
            return LastResult;
        }

        /// <summary>Ends a point or round transition (map swapped, players revived).</summary>
        public bool FinishTransition()
        {
            if (Phase == MatchPhase.PointTransition)
            {
                IsTransitioning = false;
                SetPhase(MatchPhase.Battle);
                return true;
            }
            if (Phase == MatchPhase.RoundTransition)
            {
                IsTransitioning = false;
                if (Settings.pickPhase && _pickQueue.Count > 0) SetPhase(MatchPhase.LoserCardPick);
                else SetPhase(MatchPhase.Battle);
                return true;
            }
            return false;
        }

        /// <summary>Returns to the lobby keeping participants; StartMatch resets the score.</summary>
        public void ResetToLobby()
        {
            IsTransitioning = false;
            _pickQueue.Clear();
            WinningTeam = -1;
            SetPhase(MatchPhase.WaitingForPlayers);
        }

        private void SetPhase(MatchPhase next)
        {
            var prev = Phase;
            Phase = next;
            PhaseChanged?.Invoke(prev, next);
        }
    }
}

