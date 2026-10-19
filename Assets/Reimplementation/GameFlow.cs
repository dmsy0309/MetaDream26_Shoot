using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Content;
using Metadream.Shoot.Core;
using Metadream.Shoot.Maps;
using Metadream.Shoot.Network;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot
{
    public enum GameMode
    {
        LocalVersus = 0,
        LocalTeams = 1,
        VersusCpu = 2,
        CpuDemo = 3,
        CpuDemoTeams = 4,
        Online = 5,
        Sandbox = 6,
        /// <summary>Debug mode only: P1 vs CPU with cheats (card search, F2–F7).</summary>
        CheatDuel = 7,
        /// <summary>Debug mode only: P1 + CPU teammate vs 2 CPU with cheats.</summary>
        CheatTeams = 8
    }

    public enum NetRole
    {
        Offline,
        Host,
        Client
    }

    /// <summary>One player slot. Slot i plays for team i % 2.</summary>
    public sealed class SlotSpec
    {
        public string Name;
        public int Team;
        public Color Color;
        public IInputSource Input;
    }

    /// <summary>
    /// Drives MatchStateMachine with fighters, card picks, map transitions and banners. On a network client
    /// it only mirrors host snapshots (Role == Client).
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }
        public const float SlideDistance = 90f;
        private const float SlideDuration = 0.7f;

        public MatchSettings Settings { get; private set; } = new MatchSettings();
        public MatchStateMachine Match { get; private set; }
        public readonly List<Fighter> Fighters = new List<Fighter>();
        public readonly List<string> History = new List<string>();
        public GameMode Mode { get; private set; }
        public NetRole Role { get; set; } = NetRole.Offline;
        public bool InMenu { get; private set; } = true;
        /// <summary>The slot this machine controls on a network client.</summary>
        public int LocalSlot { get; private set; }
        /// <summary>Replaces the default Esc behaviour (return to title) while a network session is active.</summary>
        public Action EscapeOverride;
        public IReadOnlyList<CardDefinition> Offer => _offer;
        public int OfferIndex { get; private set; }
        public Fighter Picker { get; private set; }
        public string Banner { get; private set; }
        public string SubBanner { get; private set; }
        public Color BannerColor { get; private set; } = Color.white;
        public MapRoot CurrentMap { get; private set; }
        public int SlidingToIndex { get; private set; } = -1;
        public bool AutoRematch { get; set; }
        /// <summary>Seconds between GameOver and an automatic rematch.</summary>
        public float RematchDelay { get; set; } = 4f;
        /// <summary>Running as a headless dedicated server.</summary>
        public bool DedicatedServer { get; set; }
        /// <summary>Client: replaces the host's GameOver hint (null keeps the server's text).</summary>
        public string MirrorGameOverText { get; set; }
        /// <summary>Shown under the winner banner when the match ended because players left.</summary>
        public string ForfeitText { get; private set; }
        public int MatchesCompleted { get; private set; }
        public int PointsPlayed { get; private set; }
        public int PicksCompleted { get; private set; }
        public float BattleTime { get; private set; }

        public event Action<MatchPhase, MatchPhase> PhaseChanged;

        private List<CardDefinition> _offer;
        private float _pickOpenedAt;
        private float _gameOverAt;
        private System.Random _rng = new System.Random(1);
        private MapRotation _rotation;
        private bool _deathPending;
        private Coroutine _routine;
        private bool _sliding;

        // Client mirror state.
        private bool _mirrorActive;
        private MatchPhase _mPhase;
        private readonly int[] _mPoints = new int[2];
        private readonly int[] _mRounds = new int[2];
        private int _mPointsToWin = 2;
        private int _mRoundsToWin = 5;
        private int _mWinning = -1;
        private int _mPickerRerolls;
        private readonly Dictionary<int, int> _rerolls = new Dictionary<int, int>();

        /// <summary>Rerolls the given fighter has left in this game.</summary>
        public int RerollsLeft(Fighter f) => f != null && _rerolls.TryGetValue(f.PlayerId, out int n) ? n : 0;

        /// <summary>Rerolls left for the current picker (mirrored on clients).</summary>
        public int PickerRerolls => Role == NetRole.Client ? _mPickerRerolls : RerollsLeft(Picker);

        private void ResetRerolls()
        {
            _rerolls.Clear();
            foreach (var f in Fighters) _rerolls[f.PlayerId] = Mathf.Max(0, Settings.rerollsAtStart);
        }

        /// <summary>Replaces one offered card with a new random one (costs one reroll of the picker).</summary>
        public bool Reroll(int index)
        {
            if (Role == NetRole.Client || Match == null || !IsPicking || index < 0 || index >= _offer.Count) return false;
            if (RerollsLeft(Picker) <= 0) return false;
            var pool = new List<CardDefinition>();
            foreach (var c in CardCatalog.Pool(Settings.cardPool))
                if (!_offer.Contains(c)) pool.Add(c);
            var drawn = CardRules.DrawOffer(pool, Picker.Cards, 1, _rng);
            if (drawn.Count == 0) return false;
            Log("reroll " + Picker.DisplayName + " " + _offer[index].Id + " -> " + drawn[0].Id);
            _offer[index] = drawn[0];
            _rerolls[Picker.PlayerId] = RerollsLeft(Picker) - 1;
            OfferIndex = index;
            Fx.Sound("reroll", Vector2.zero);
            return true;
        }

        /// <summary>Cheat matches (settings → 치트 대전, debug mode only).</summary>
        public bool IsCheatMatch => Role == NetRole.Offline && (Mode == GameMode.CheatDuel || Mode == GameMode.CheatTeams);

        /// <summary>UI request (mouse button under a card) from whoever is playing on this machine.</summary>
        public void RequestReroll(int index)
        {
            if (Role == NetRole.Client) { Online.NetClientSession.Active?.QueueReroll(index); return; }
            if (Picker != null && !Picker.IsBot && !(Picker.Input is Network.NetworkInput)) Reroll(index);
        }

        /// <summary>True when the current picker is controlled from this machine (UI shows the reroll buttons).</summary>
        public bool LocalPicker
        {
            get
            {
                if (Picker == null || ReplayMode) return false;
                if (Role == NetRole.Client) return Picker.PlayerId == LocalSlot;
                return !Picker.IsBot && Picker.Input != null && !(Picker.Input is Network.NetworkInput);
            }
        }
        private int _mirrorSlideTarget = -1;
        private Action<CardDefinition> _sandboxPicked;

        // ------------------------------------------------------------ view model (HUD)

        /// <summary>Free play without score (tutorial, debugging). Card picks still work through SandboxPick.</summary>
        public bool IsSandbox { get; private set; }
        public bool HasMatch => Role == NetRole.Client ? _mirrorActive : (Match != null || IsSandbox);
        public MatchPhase Phase => Role == NetRole.Client ? _mPhase
            : IsSandbox ? (_offer != null && Picker != null ? MatchPhase.InitialCardPick : MatchPhase.Battle)
            : (Match != null ? Match.Phase : MatchPhase.WaitingForPlayers);
        public int PointsToWinRound => Role == NetRole.Client ? _mPointsToWin : Settings.pointsToWinRound;
        public int RoundsToWinGame => Role == NetRole.Client ? _mRoundsToWin : Settings.roundsToWinGame;
        public int WinningTeam => Role == NetRole.Client ? _mWinning : (Match != null ? Match.WinningTeam : -1);
        public bool IsPicking => (Phase == MatchPhase.InitialCardPick || Phase == MatchPhase.LoserCardPick) && _offer != null && _offer.Count > 0 && Picker != null;

        public int TeamPoints(int team)
        {
            if (Role == NetRole.Client) return team >= 0 && team < 2 ? _mPoints[team] : 0;
            return Match != null && Match.Score != null && team < Match.Score.TeamCount ? Match.Score.Points(team) : 0;
        }

        public int TeamRounds(int team)
        {
            if (Role == NetRole.Client) return team >= 0 && team < 2 ? _mRounds[team] : 0;
            return Match != null && Match.Score != null && team < Match.Score.TeamCount ? Match.Score.Rounds(team) : 0;
        }

        public int TeamSize
        {
            get
            {
                int n = 0;
                foreach (var f in Fighters) if (f.TeamId == 0) n++;
                return Mathf.Max(1, n);
            }
        }

        /// <summary>Player name in 1v1, team name in 2v2.</summary>
        public string SideName(int team)
        {
            if (TeamSize > 1) return Palette.TeamName(team);
            var f = FighterOfTeam(team);
            return f != null ? f.DisplayName : Palette.TeamName(team);
        }

        public Color SideColor(int team)
        {
            if (TeamSize > 1) return Palette.TeamColor(team);
            var f = FighterOfTeam(team);
            return f != null ? f.Color : Palette.TeamColor(team);
        }

        private void Awake()
        {
            Instance = this;
            _rotation = new MapRotation(_rng);
            var backdrop = ContentDatabase.DefaultMap;
            if (backdrop != null) CurrentMap = MapRoot.Build(backdrop, Vector2.zero);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        // ------------------------------------------------------------ slots

        public static List<SlotSpec> BuildSlots(GameMode mode, System.Random rng)
        {
            var list = new List<SlotSpec>();
            IInputSource P1() => new DeviceInput(KeyboardBinding.PlayerOne(), true, 0, "P1");
            IInputSource P2() => new DeviceInput(KeyboardBinding.PlayerTwo(), false, 1, "P2");
            IInputSource Bot(string label) => new BotInput(rng.Next(), label);
            switch (mode)
            {
                case GameMode.LocalVersus:
                    Add(list, "P1", P1());
                    Add(list, "P2", P2());
                    break;
                case GameMode.LocalTeams:
                    Add(list, "P1", P1());
                    Add(list, "P2", P2());
                    for (int i = 2; i < 4; i++)
                    {
                        // Extra gamepads take the partner slots; otherwise a CPU teammate fills in.
                        if (Gamepad.all.Count > i) Add(list, "P" + (i + 1), new DeviceInput(null, false, i, "P" + (i + 1)));
                        else Add(list, "CPU " + (i - 1), Bot("CPU " + (i - 1)));
                    }
                    break;
                case GameMode.VersusCpu:
                case GameMode.CheatDuel:
                    Add(list, "P1", P1());
                    Add(list, "CPU", Bot("CPU"));
                    break;
                case GameMode.CheatTeams:
                    Add(list, "P1", P1());
                    Add(list, "CPU 상대 1", Bot("CPU 상대 1"));
                    Add(list, "CPU 팀원", Bot("CPU 팀원"));
                    Add(list, "CPU 상대 2", Bot("CPU 상대 2"));
                    break;
                case GameMode.CpuDemo:
                    Add(list, "CPU 1", Bot("CPU 1"));
                    Add(list, "CPU 2", Bot("CPU 2"));
                    break;
                case GameMode.CpuDemoTeams:
                    for (int i = 0; i < 4; i++) Add(list, "CPU " + (i + 1), Bot("CPU " + (i + 1)));
                    break;
            }
            return list;
        }

        private static void Add(List<SlotSpec> list, string name, IInputSource input)
        {
            int slot = list.Count;
            list.Add(new SlotSpec { Name = name, Team = slot % 2, Color = Palette.SlotColor(slot), Input = input });
        }

        // ------------------------------------------------------------ public API

        public void StartMatch(GameMode mode, MatchSettings settings = null, int? seed = null)
        {
            // Offline entry point (menus, CPU modes): never inherits a network role.
            Role = NetRole.Offline;
            ReplayMode = false;
            int s = seed ?? Environment.TickCount;
            StartWithSlots(mode, BuildSlots(mode, new System.Random(s ^ 0x5bd1e995)), settings, s);
        }

        public void StartWithSlots(GameMode mode, List<SlotSpec> slots, MatchSettings settings, int seed)
        {
            StopFlow();
            DestroyFighters();
            Projectile.ClearAll();
            Fx.ClearAll();
            Time.timeScale = 1f;
            Settings = settings ?? new MatchSettings();
            _rng = new System.Random(seed);
            _rotation = new MapRotation(_rng, MapCatalog.Pool(Settings.mapPool));
            Mode = mode;
            IsSandbox = false;
            InMenu = false;
            History.Clear();
            PointsPlayed = 0;
            PicksCompleted = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                var spec = slots[i];
                var f = Fighter.Create(i, spec.Team, spec.Color, spec.Name, spec.Input);
                f.BaseStats = Settings.stats;
                f.ResetForMatch();
                if (spec.Input is BotInput bot) bot.Self = f;
                f.Died += OnFighterDied;
                Fighters.Add(f);
            }

            Match = new MatchStateMachine(Settings);
            Match.PhaseChanged += OnPhaseChanged;
            foreach (var f in Fighters) Match.Join(f.PlayerId, f.TeamId);
            LoadMapImmediate(_rotation.Next());
            ForfeitText = null;
            ResetRerolls();
            Log("match start mode=" + mode + " players=" + Fighters.Count + " points=" + Settings.pointsToWinRound + " rounds=" + Settings.roundsToWinGame);
            Match.StartMatch();
        }

        /// <summary>Host: hand a disconnected player's fighter to the CPU.</summary>
        public void ReplaceInput(int slot, IInputSource input, string newName = null)
        {
            if (slot < 0 || slot >= Fighters.Count) return;
            var f = Fighters[slot];
            f.Input = input;
            if (input is BotInput bot) bot.Self = f;
            if (newName != null) f.SetDisplayName(newName);
        }

        /// <summary>Host: a player left (absent = true) or the same device rejoined (false). No CPU takes the slot.</summary>
        public void SetAbsent(int slot, bool absent)
        {
            if (slot < 0 || slot >= Fighters.Count) return;
            var f = Fighters[slot];
            f.Absent = absent;
            if (absent)
            {
                f.Input = null;
                f.SetPresent(false);
                if (Picker == f && Match != null) ConfirmPick(BotChoice());
                _deathPending = true;
            }
            Log("slot " + slot + (absent ? " left (absent)" : " rejoined"));
        }

        /// <summary>Ends the match immediately in favour of winningTeam.</summary>
        public void Forfeit(int winningTeam, string reason)
        {
            if (Match == null || Match.Phase == MatchPhase.GameOver) return;
            ForfeitText = reason;
            _offer = null;
            Picker = null;
            Log("forfeit winner=" + winningTeam + " (" + reason + ")");
            Match.Forfeit(winningTeam);
        }

        /// <summary>Team of the local player (-1 when only watching).</summary>
        public int LocalTeam
        {
            get
            {
                if (ReplayMode) return -1;
                if (Role == NetRole.Client) return LocalSlot >= 0 && LocalSlot < Fighters.Count ? Fighters[LocalSlot].TeamId : -1;
                if (Role == NetRole.Host) return DedicatedServer ? -1 : 0;
                if (Mode == GameMode.CpuDemo || Mode == GameMode.CpuDemoTeams) return -1;
                return Fighters.Count > 0 ? Fighters[0].TeamId : -1;
            }
        }

        /// <summary>Leaves the result screen: everyone goes back to the main menu (no rematch).</summary>
        public void ExitResults()
        {
            if (Role == NetRole.Client) { Online.NetClientSession.Active?.Leave(null); return; }
            if (Role == NetRole.Host && !DedicatedServer && Online.NetHostSession.Active != null) { Online.NetHostSession.Active.Close(); FrontEnd.Instance?.ShowTitle(); return; }
            ReturnToTitle();
        }

        public void Rematch()
        {
            if (Match == null) return;
            StopFlow();
            Time.timeScale = 1f;
            foreach (var f in Fighters)
            {
                f.ResetForMatch();
                f.SetPresent(false);
            }
            History.Clear();
            PointsPlayed = 0;
            PicksCompleted = 0;
            Match.ResetToLobby();
            LoadMapImmediate(_rotation.Next());
            ForfeitText = null;
            ResetRerolls();
            Log("rematch");
            Match.StartMatch();
        }

        public void ReturnToTitle()
        {
            StopFlow();
            Time.timeScale = 1f;
            DestroyFighters();
            Projectile.ClearAll();
            Fx.ClearAll();
            Match = null;
            _offer = null;
            Picker = null;
            Banner = null;
            SubBanner = null;
            EscapeOverride = null;
            _mirrorActive = false;
            IsSandbox = false;
            _sandboxPicked = null;
            _sliding = false;
            SlidingToIndex = -1;
            _mirrorSlideTarget = -1;
            if (CurrentMap != null) CurrentMap.transform.position = Vector3.zero;
            InMenu = true;
        }

        public void ConfirmPick(int index)
        {
            if (Role == NetRole.Client || !IsPicking || index < 0 || index >= _offer.Count) return;
            var card = _offer[index];
            var picker = Picker;
            if (IsSandbox)
            {
                picker.ApplyCard(card);
                _offer = null;
                Picker = null;
                var done = _sandboxPicked;
                _sandboxPicked = null;
                Log("sandbox pick " + picker.DisplayName + " " + card.Id);
                done?.Invoke(card);
                return;
            }
            picker.ApplyCard(card);
            PicksCompleted++;
            Fx.Sound("card_pick", Vector2.zero);
            History.Add(picker.DisplayName + " ← " + card.Title);
            Log("pick " + picker.DisplayName + " " + card.Id);
            _offer = null;
            Picker = null;
            Match.CompletePick(picker.PlayerId);
            if (Match.Phase == MatchPhase.InitialCardPick || Match.Phase == MatchPhase.LoserCardPick) OpenPick();
        }

        // ------------------------------------------------------------ loop

        private void Update()
        {
            var kb = Keyboard.current;
            if (!InMenu && kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (Presentation.CheatPanel.IsOpen) return; // the panel closes itself
                if (Presentation.QuitConfirm.IsOpen) { Presentation.QuitConfirm.Close(); return; }
                Action leave = EscapeOverride ?? ReturnToTitle;
                // Replays and the result screen leave directly; a running game asks first.
                if (ReplayMode || Phase == MatchPhase.GameOver || Presentation.QuitConfirm.Instance == null) leave();
                else Presentation.QuitConfirm.Open(leave);
                return;
            }
            if (InMenu || Role == NetRole.Client) return;
            if (IsSandbox)
            {
                if (IsPicking) UpdatePick();
                return;
            }
            if (Match == null) return;
            if (Match.Phase == MatchPhase.Battle)
            {
                BattleTime += Time.deltaTime;
                UpdateSuddenDeath(Time.deltaTime);
            }
            if (IsPicking) UpdatePick();
            if (Match.Phase == MatchPhase.GameOver) UpdateGameOver(kb);
        }

        /// <summary>Seconds of one point before sudden death starts (everyone takes growing damage so points always end).</summary>
        public const float SuddenDeathAfter = 45f;
        private float _suddenFx;

        private void UpdateSuddenDeath(float dt)
        {
            if (BattleTime < SuddenDeathAfter) return;
            float over = BattleTime - SuddenDeathAfter;
            if (over < dt * 1.5f) { Banner = "서든 데스"; SubBanner = "모두 점점 체력을 잃습니다"; BannerColor = new Color(1f, 0.35f, 0.25f); }
            else if (over > 1.6f && Banner == "서든 데스") { Banner = null; SubBanner = null; }
            float dps = 4f + over * 2f;
            _suddenFx -= dt;
            bool fx = _suddenFx <= 0f;
            if (fx) _suddenFx = 0.6f;
            foreach (var f in Fighters.ToArray())
            {
                if (f == null || f.IsDead || !f.IsPlaying) continue;
                f.TakeDamage(dps * dt, null, Vector2.zero, true, 0f, true);
                if (fx) Fx.Ring(f.Position, new Color(1f, 0.3f, 0.2f), 1.6f);
            }
        }

        private void LateUpdate()
        {
            if (!_deathPending || Match == null || Role == NetRole.Client) return;
            _deathPending = false;
            if (Match.Phase != MatchPhase.Battle) return;
            var alive = new HashSet<int>();
            foreach (var f in Fighters)
                if (f.CountsAsAlive) alive.Add(f.TeamId);
            var result = Match.ReportAliveTeams(alive);
            if (result.Kind != PointResultKind.Ignored) Log("result " + result);
            if (result.Kind == PointResultKind.Point) Fx.Sound("point", Vector2.zero);
            else if (result.Kind == PointResultKind.Round) Fx.Sound("round", Vector2.zero);
        }

        private void UpdatePick()
        {
            if (Picker == null) return;
            float open = Time.unscaledTime - _pickOpenedAt;
            if (Fighter.InputSuspended && !Picker.IsBot && !(Picker.Input is Network.NetworkInput)) return;
            if (Picker.IsBot || Picker.Absent || Picker.Input == null)
            {
                if (open > 0.9f) ConfirmPick(BotChoice());
                return;
            }
            if (open < 0.3f || Picker.Input == null) return;
            var frame = Picker.Input.Read(new InputContext { Position = Vector2.zero, Camera = Camera.main });
            int n = _offer.Count;
            if (frame.NavLeft) OfferIndex = (OfferIndex - 1 + n) % n;
            if (frame.NavRight) OfferIndex = (OfferIndex + 1) % n;
            if (frame.Reroll > 0 && Match != null) Reroll(frame.Reroll == 255 ? OfferIndex : frame.Reroll - 1);
            if (frame.ConfirmPressed) ConfirmPick(OfferIndex);
        }

        private int BotChoice()
        {
            int best = 0;
            double bestScore = double.MinValue;
            for (int i = 0; i < _offer.Count; i++)
            {
                double score = (int)_offer[i].Rarity + _rng.NextDouble() * 1.5;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        private void UpdateGameOver(Keyboard kb)
        {
            if (Time.unscaledTime - _gameOverAt < 1f) return;
            if (DedicatedServer) return; // the server goes back to its lobby on its own
            bool confirm = kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame || kb.periodKey.wasPressedThisFrame);
            foreach (var gp in Gamepad.all) confirm |= gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame;
            if (confirm && !AutoRematch) ExitResults();
        }

        // ------------------------------------------------------------ phases

        private void OnPhaseChanged(MatchPhase previous, MatchPhase next)
        {
            Log("phase " + previous + " -> " + next);
            PhaseChanged?.Invoke(previous, next);
            if (next == MatchPhase.RoundTransition && Settings.rerollsPerRound > 0)
                foreach (var f in Fighters) _rerolls[f.PlayerId] = RerollsLeft(f) + Settings.rerollsPerRound;
            switch (next)
            {
                case MatchPhase.InitialCardPick:
                case MatchPhase.LoserCardPick:
                    OpenPick();
                    break;
                case MatchPhase.Battle:
                    Run(BeginBattle());
                    break;
                case MatchPhase.PointTransition:
                case MatchPhase.RoundTransition:
                    Run(Transition());
                    break;
                case MatchPhase.GameOver:
                    Run(GameOverRoutine());
                    break;
            }
        }

        private void OpenPick()
        {
            int pickerId = Match.CurrentPicker;
            Picker = pickerId >= 0 && pickerId < Fighters.Count ? Fighters[pickerId] : null;
            foreach (var f in Fighters) f.SetPresent(false);
            Projectile.ClearAll();
            Banner = null;
            SubBanner = null;
            if (Picker == null) return;
            _offer = CardRules.DrawOffer(CardCatalog.Pool(Settings.cardPool), Picker.Cards, Settings.cardsPerPick, _rng);
            OfferIndex = _offer.Count / 2;
            _pickOpenedAt = Time.unscaledTime;
            if (_offer.Count == 0)
            {
                // Pool exhausted for this player: skip the pick (original behaviour unknown).
                var p = Picker;
                _offer = null;
                Picker = null;
                Match.CompletePick(p.PlayerId);
                if (Match.Phase == MatchPhase.InitialCardPick || Match.Phase == MatchPhase.LoserCardPick) OpenPick();
            }
        }

        private IEnumerator BeginBattle()
        {
            Projectile.ClearAll();
            Fx.ClearAll();
            CurrentMap.SetSimulated(true);
            var perTeam = new int[2];
            foreach (var f in Fighters)
            {
                int team = Mathf.Clamp(f.TeamId, 0, 1);
                if (f.Absent) { f.SetPresent(false); continue; }
                f.Revive(CurrentMap.SpawnPoint(f.TeamId, perTeam[team]++), true);
                f.SetPlaying(false);
            }
            BattleTime = 0f;
            Banner = "준비";
            BannerColor = Color.white;
            SubBanner = CurrentMap.Definition.Name;
            yield return new WaitForSecondsRealtime(0.55f);
            Banner = "싸워라!";
            Fx.Sound("fight", Vector2.zero);
            SubBanner = null;
            foreach (var f in Fighters) if (!f.Absent) f.SetPlaying(true);
            yield return new WaitForSecondsRealtime(0.45f);
            if (Banner == "싸워라!") Banner = null;
        }

        private IEnumerator Transition()
        {
            var result = Match.LastResult;
            PointsPlayed++;
            int team = result.WinnerTeam;
            string text = result.Kind == PointResultKind.Draw ? "무승부"
                : result.Kind == PointResultKind.Round ? SideName(team) + " 라운드 승리"
                : SideName(team) + " 포인트";
            History.Add(text);
            foreach (var f in Fighters) f.SetPlaying(false);

            Time.timeScale = 0.3f;
            yield return new WaitForSecondsRealtime(0.45f);
            Time.timeScale = 1f;
            Banner = text;
            BannerColor = team >= 0 ? SideColor(team) : Color.white;
            SubBanner = result.Kind == PointResultKind.Round ? "패배한 쪽이 카드를 고릅니다" : null;
            yield return new WaitForSecondsRealtime(1.1f);

            foreach (var f in Fighters) f.SetPresent(false);
            Projectile.ClearAll();
            Fx.ClearAll();
            yield return SlideToMap(_rotation.Next());
            Banner = null;
            SubBanner = null;
            _routine = null;
            Match.FinishTransition();
        }

        private IEnumerator GameOverRoutine()
        {
            int team = Match.WinningTeam;
            MatchesCompleted++;
            foreach (var f in Fighters) f.SetPlaying(false);
            History.Add(SideName(team) + " 매치 승리");
            Log("game over winner=" + SideName(team) + " cards=" + CardSummary());
            Time.timeScale = 0.3f;
            yield return new WaitForSecondsRealtime(0.5f);
            Time.timeScale = 1f;
            _gameOverAt = Time.unscaledTime;
            Banner = SideName(team) + " 승리!";
            BannerColor = SideColor(team);
            SubBanner = ForfeitText ?? "확인(Enter/Space): 메인 화면으로";
            if (AutoRematch)
            {
                // Test harness only (-demo / -autoplay command lines); players never get a rematch.
                yield return new WaitForSecondsRealtime(RematchDelay);
                _routine = null;
                Rematch();
            }
        }

        private IEnumerator SlideToMap(MapDefinition def)
        {
            _sliding = true;
            SlidingToIndex = def.Index;
            var old = CurrentMap;
            var next = MapRoot.Build(def, new Vector2(SlideDistance, 0f));
            next.SetSimulated(false);
            if (old != null) old.SetSimulated(false);
            float t = 0f;
            while (t < SlideDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / SlideDuration);
                if (old != null) old.transform.position = new Vector3(-SlideDistance * k, 0f, 0f);
                next.transform.position = new Vector3(SlideDistance * (1f - k), 0f, 0f);
                yield return null;
            }
            if (old != null) Destroy(old.gameObject);
            next.transform.position = Vector3.zero;
            Physics2D.SyncTransforms();
            CurrentMap = next;
            SlidingToIndex = -1;
            _sliding = false;
        }

        // ------------------------------------------------------------ network: host capture

        /// <summary>Host: fill a snapshot with the current visible match state.</summary>
        public void CaptureSnapshot(MatchSnapshot s)
        {
            s.Clear();
            s.Tick++;
            s.TimeScale = Time.timeScale;
            s.Phase = Phase;
            s.PointsToWin = PointsToWinRound;
            s.RoundsToWin = RoundsToWinGame;
            for (int t = 0; t < 2; t++) { s.Points[t] = TeamPoints(t); s.Rounds[t] = TeamRounds(t); }
            s.Picker = IsPicking ? Picker.PlayerId : -1;
            if (IsPicking) foreach (var c in _offer) s.Offer.Add((byte)c.Index);
            s.OfferIndex = OfferIndex;
            s.PickerRerolls = IsPicking ? RerollsLeft(Picker) : 0;
            s.Banner = Banner ?? "";
            s.SubBanner = SubBanner ?? "";
            s.BannerColor = BannerColor;
            s.MapIndex = CurrentMap != null ? CurrentMap.Definition.Index : -1;
            s.IncomingMap = SlidingToIndex;
            s.WinningTeam = WinningTeam;
            foreach (var f in Fighters) s.Fighters.Add(f.CaptureState());
            foreach (var p in Projectile.Active)
            {
                if (p == null) continue;
                s.Projectiles.Add(new ProjectileState
                {
                    Id = p.Id,
                    Position = p.Position,
                    Velocity = p.Velocity,
                    Radius = p.Radius,
                    Gravity = p.Gravity,
                    Owner = (sbyte)(p.Owner != null ? p.Owner.PlayerId : -1)
                });
            }
            if (CurrentMap != null && !_sliding)
                foreach (var rb in CurrentMap.Bodies)
                    if (rb != null) s.Boxes.Add(new BoxState { Position = rb.transform.localPosition, Rotation = rb.transform.localEulerAngles.z });
            if (Fx.Outbox != null)
            {
                s.Fx.AddRange(Fx.Outbox);
                Fx.Outbox.Clear();
            }
        }

        // ------------------------------------------------------------ network: client mirror

        public void BeginMirror(IReadOnlyList<string> names, int localSlot)
        {
            StopFlow();
            DestroyFighters();
            Projectile.ClearAll();
            Fx.ClearAll();
            Role = NetRole.Client;
            LocalSlot = localSlot;
            for (int i = 0; i < names.Count; i++)
            {
                var f = Fighter.Create(i, i % 2, Palette.SlotColor(i), names[i], null);
                f.MakePuppet();
                Fighters.Add(f);
            }
            History.Clear();
            _mirrorActive = true;
            _mirrorSlideTarget = -1;
            InMenu = false;
            Log("mirror start players=" + names.Count + " local=" + localSlot);
        }

        public void EndMirror()
        {
            ReturnToTitle();
        }

        /// <summary>Replay playback is driving the client mirror (Replay/ReplayPlayer.cs).</summary>
        public bool ReplayMode { get; set; }

        /// <summary>Jump to an arbitrary snapshot (replay seek): no map slide, no effects replayed.</summary>
        public void SeekMirror(MatchSnapshot s)
        {
            if (!_mirrorActive) return;
            StopFlow();
            int target = s.IncomingMap >= 0 ? s.IncomingMap : s.MapIndex;
            var maps = MapCatalog.All;
            if (target >= 0 && target < maps.Count && (CurrentMap == null || CurrentMap.Definition.Index != target))
            {
                LoadMapImmediate(maps[target]);
                CurrentMap.SetSimulated(false);
            }
            _mirrorSlideTarget = s.IncomingMap;
            var fx = new List<FxEvent>(s.Fx);
            s.Fx.Clear();
            ApplyMirror(s);
            s.Fx.AddRange(fx);
        }

        public void ApplyMirror(MatchSnapshot s)
        {
            if (!_mirrorActive) return;
            _mPhase = s.Phase;
            _mPointsToWin = s.PointsToWin;
            _mRoundsToWin = s.RoundsToWin;
            for (int t = 0; t < 2; t++) { _mPoints[t] = s.Points[t]; _mRounds[t] = s.Rounds[t]; }
            _mWinning = s.WinningTeam;
            _mPickerRerolls = s.PickerRerolls;
            Time.timeScale = Mathf.Clamp(s.TimeScale, 0.05f, 8f);

            Banner = string.IsNullOrEmpty(s.Banner) ? null : s.Banner;
            SubBanner = string.IsNullOrEmpty(s.SubBanner) ? null : s.SubBanner;
            if (s.Phase == MatchPhase.GameOver && MirrorGameOverText != null) SubBanner = MirrorGameOverText;
            BannerColor = s.BannerColor;

            if (s.Picker >= 0 && s.Picker < Fighters.Count && s.Offer.Count > 0)
            {
                if (_offer == null) _offer = new List<CardDefinition>();
                _offer.Clear();
                foreach (var idx in s.Offer)
                    if (idx < CardCatalog.All.Count) _offer.Add(CardCatalog.All[idx]);
                Picker = Fighters[s.Picker];
                OfferIndex = Mathf.Clamp(s.OfferIndex, 0, Mathf.Max(0, _offer.Count - 1));
            }
            else
            {
                _offer = null;
                Picker = null;
            }

            ApplyMirrorMap(s.MapIndex, s.IncomingMap);
            if (CurrentMap != null && !_sliding) CurrentMap.ApplyBoxes(s.Boxes);
            int n = Mathf.Min(Fighters.Count, s.Fighters.Count);
            for (int i = 0; i < n; i++) Fighters[i].ApplySnapshot(s.Fighters[i]);
            foreach (var e in s.Fx) Fx.Play(e);
        }

        private void ApplyMirrorMap(int mapIndex, int incoming)
        {
            var maps = MapCatalog.All;
            if (incoming >= 0 && incoming < maps.Count)
            {
                if (!_sliding && _mirrorSlideTarget != incoming && (CurrentMap == null || CurrentMap.Definition.Index != incoming))
                {
                    _mirrorSlideTarget = incoming;
                    Run(SlideToMap(maps[incoming]));
                }
                return;
            }
            _mirrorSlideTarget = -1;
            if (!_sliding && mapIndex >= 0 && mapIndex < maps.Count && (CurrentMap == null || CurrentMap.Definition.Index != mapIndex))
            {
                LoadMapImmediate(maps[mapIndex]);
                CurrentMap.SetSimulated(false);
            }
        }

        // ------------------------------------------------------------ helpers

        // ------------------------------------------------------------ sandbox (tutorial / free play)

        /// <summary>Free play on one map without score or card phases. Fighters spawn immediately.</summary>
        public void StartSandbox(MapDefinition map, List<SlotSpec> slots, BaseStats stats = null)
        {
            StopFlow();
            Role = NetRole.Offline;
            ReplayMode = false;
            DestroyFighters();
            Projectile.ClearAll();
            Fx.ClearAll();
            Time.timeScale = 1f;
            Settings = new MatchSettings();
            if (stats != null) Settings.stats = stats;
            Match = null;
            Mode = GameMode.Sandbox;
            IsSandbox = true;
            InMenu = false;
            History.Clear();
            _offer = null;
            Picker = null;
            Banner = null;
            SubBanner = null;
            _rng = new System.Random(Environment.TickCount);
            _rotation = new MapRotation(_rng, MapCatalog.Pool(null));
            for (int i = 0; i < slots.Count; i++)
            {
                var spec = slots[i];
                var f = Fighter.Create(i, spec.Team, spec.Color, spec.Name, spec.Input);
                f.BaseStats = Settings.stats;
                f.ResetForMatch();
                if (spec.Input is BotInput bot) bot.Self = f;
                Fighters.Add(f);
            }
            LoadMapImmediate(map ?? ContentDatabase.DefaultMap);
            CurrentMap.SetSimulated(true);
            for (int i = 0; i < Fighters.Count; i++) SandboxRespawn(Fighters[i]);
            Log("sandbox start map=" + CurrentMap.Definition.Id + " players=" + Fighters.Count);
        }

        public void SandboxRespawn(Fighter f)
        {
            if (f == null || CurrentMap == null) return;
            int index = 0;
            foreach (var other in Fighters)
            {
                if (other == f) break;
                if (other.TeamId == f.TeamId) index++;
            }
            f.Revive(CurrentMap.SpawnPoint(f.TeamId, index), true);
            f.SetPlaying(true);
        }

        /// <summary>Opens the regular card pick screen for one fighter; done is called with the chosen card.</summary>
        public void SandboxPick(Fighter picker, List<CardDefinition> offer, Action<CardDefinition> done)
        {
            if (!IsSandbox || picker == null || offer == null || offer.Count == 0) return;
            _offer = offer;
            Picker = picker;
            OfferIndex = offer.Count / 2;
            _pickOpenedAt = Time.unscaledTime;
            _sandboxPicked = done;
        }

        public void ShowBanner(string text, string sub, Color color)
        {
            Banner = text;
            SubBanner = sub;
            BannerColor = color;
        }

        // ------------------------------------------------------------ developer debug commands (offline only)

        public bool CanUseDebugCommands => Role == NetRole.Offline && !InMenu && HasMatch;

        /// <summary>Loads the next map from the rotation and respawns everybody (debug).</summary>
        public void DebugNextMap()
        {
            if (!CanUseDebugCommands || _sliding) return;
            Projectile.ClearAll();
            var next = _rotation.Next();
            if (CurrentMap != null && next == CurrentMap.Definition) next = _rotation.Next();
            LoadMapImmediate(next);
            CurrentMap.SetSimulated(true);
            foreach (var f in Fighters)
            {
                if (IsSandbox) SandboxRespawn(f);
                else if (!f.IsDead)
                {
                    f.Revive(CurrentMap.SpawnPoint(f.TeamId, 0), false);
                    f.SetPlaying(Phase == MatchPhase.Battle);
                }
            }
            Log("debug map " + next.Id + " (" + next.Source + ")");
        }

        public void DebugGiveCard(CardDefinition card)
        {
            if (!CanUseDebugCommands || card == null || Fighters.Count == 0) return;
            Fighters[0].ApplyCard(card);
            Log("debug give " + card.Id + " to " + Fighters[0].DisplayName);
        }

        /// <summary>Kills every fighter not on team 0 (ends the point in favour of P1's team).</summary>
        public void DebugKillEnemies()
        {
            if (!CanUseDebugCommands || Fighters.Count == 0) return;
            foreach (var f in Fighters.ToArray())
                if (f.TeamId != 0 && !f.IsDead) f.TakeDamage(100000f, Fighters[0], Vector2.up, true, 0f);
            Log("debug kill enemies");
        }

        public bool DebugToggleGodMode()
        {
            if (!CanUseDebugCommands || Fighters.Count == 0) return false;
            Fighters[0].Invulnerable = !Fighters[0].Invulnerable;
            Log("debug god mode " + Fighters[0].Invulnerable);
            return Fighters[0].Invulnerable;
        }

        private Fighter FighterOfTeam(int team)
        {
            foreach (var f in Fighters)
                if (f.TeamId == team) return f;
            return null;
        }

        private void OnFighterDied(Fighter victim, Fighter killer)
        {
            _deathPending = true;
            Log("death " + victim.DisplayName + (killer != null ? " by " + killer.DisplayName : " (edge)"));
        }

        private void LoadMapImmediate(MapDefinition def)
        {
            if (CurrentMap != null) Destroy(CurrentMap.gameObject);
            CurrentMap = MapRoot.Build(def, Vector2.zero);
            if (Role == NetRole.Client) CurrentMap.SetSimulated(false);
        }

        private void Run(IEnumerator routine)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(routine);
        }

        private void StopFlow()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            _deathPending = false;
            if (_sliding)
            {
                // A slide was interrupted: keep whichever map is current at the origin.
                _sliding = false;
                SlidingToIndex = -1;
                foreach (var m in FindObjectsByType<MapRoot>())
                    if (m != CurrentMap) Destroy(m.gameObject);
                if (CurrentMap != null) CurrentMap.transform.position = Vector3.zero;
            }
        }

        private void DestroyFighters()
        {
            foreach (var f in Fighters)
                if (f != null) Destroy(f.gameObject);
            Fighters.Clear();
        }

        public string CardSummary()
        {
            var parts = new List<string>();
            foreach (var f in Fighters)
            {
                var ids = new List<string>();
                foreach (var c in f.Cards) ids.Add(c.Id);
                parts.Add(f.DisplayName + "[" + string.Join(",", ids) + "]");
            }
            return string.Join(" ", parts);
        }

        private static void Log(string message) => Debug.Log("[Match] " + message);
    }
}

