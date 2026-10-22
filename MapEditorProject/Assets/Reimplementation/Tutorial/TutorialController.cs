using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Content;
using Metadream.Shoot.Core;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;
using static Metadream.Shoot.Presentation.UiKit;

namespace Metadream.Shoot.Tutorial
{
    /// <summary>Target dummy for the tutorial: idle, slow shooter, or a real CPU for the final duel.</summary>
    public sealed class DummyInput : IInputSource
    {
        public enum DummyMode { Idle, Shooter, Fighter }

        private readonly BotInput _bot = new BotInput(1234, "CPU");
        private float _timer = 1.5f;

        public string Label => "Dummy";
        public DummyMode Mode = DummyMode.Idle;
        public Fighter Self { get => _bot.Self; set => _bot.Self = value; }
        public Fighter Target;
        public float FireInterval = 1.8f;

        public InputFrame Read(in InputContext context)
        {
            if (Mode == DummyMode.Fighter) return _bot.Read(context);
            var f = new InputFrame();
            if (Self == null || Target == null) return f;
            Vector2 to = Target.Position - Self.Position;
            float t = to.magnitude / Mathf.Max(1f, Self.Stats.gun.ProjectileSpeed);
            Vector2 aim = to + Vector2.up * (0.5f * Projectile.MoveTransformGravity * Self.Stats.gun.gravity * t * t);
            f.Aim = aim.normalized;
            if (Mode == DummyMode.Shooter)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    _timer = FireInterval;
                    f.ShootPressed = true;
                }
            }
            return f;
        }
    }

    /// <summary>
    /// Step-by-step tutorial in a sandbox match: P1 (keyboard/mouse/pad) versus a training dummy on the
    /// tutorial-only map "training". Each step completes from real gameplay events.
    /// </summary>
    public sealed class TutorialController : MonoBehaviour
    {
        public enum Step { Move, Jump, Shoot, Reload, Block, Edge, Cards, Rules, Duel, Done }

        public const string MapId = "training";
        public const string PrefKey = "tutorial.completed";
        public static TutorialController Active { get; private set; }

        private Fighter _player;
        private Fighter _dummy;
        private DummyInput _dummyInput;
        private float _moved;
        private float _lastX;
        private int _jumps;
        private int _hits;
        private bool _sawReload;
        private int _blocksAtStart;
        private float _stepStarted;
        private float _successUntil;
        private string _successText;
        private float _dummyRespawnAt = -1f;
        private float _playerRespawnAt = -1f;
        private bool _pickOpened;
        private int _duelLosses;

        public Step Current { get; private set; } = Step.Move;
        public static bool Completed => PlayerPrefs.GetInt(PrefKey, 0) == 1;

        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
        }

        /// <summary>Starts (or restarts) the tutorial. Named Launch so it never shadows UiKit.Begin inside OnGUI.</summary>
        public static TutorialController Launch()
        {
            if (Active != null) Destroy(Active.gameObject);
            var go = new GameObject("Tutorial");
            Active = go.AddComponent<TutorialController>();
            return Active;
        }

        private void Start()
        {
            var flow = GameFlow.Instance;
            var map = ContentDatabase.FindMap(MapId) ?? ContentDatabase.DefaultMap;
            _dummyInput = new DummyInput();
            var slots = new List<SlotSpec>
            {
                new SlotSpec { Name = "P1", Team = 0, Color = Palette.PlayerOne, Input = new DeviceInput(KeyboardBinding.PlayerOne(), true, 0, "P1") },
                new SlotSpec { Name = "훈련용 더미", Team = 1, Color = Palette.PlayerTwo, Input = _dummyInput }
            };
            flow.StartSandbox(map, slots);
            flow.EscapeOverride = Exit;
            _player = flow.Fighters[0];
            _dummy = flow.Fighters[1];
            _dummyInput.Self = _dummy;
            _dummyInput.Target = _player;
            _player.Jumped += _ => { if (Current == Step.Jump) _jumps++; };
            _dummy.Damaged += (_, __) => { if (Current == Step.Shoot) _hits++; };
            _player.Died += (_, __) => _playerRespawnAt = Time.time + 1.2f;
            _dummy.Died += (_, __) => OnDummyDied();
            _lastX = _player.Position.x;
            Enter(Step.Move);
            Debug.Log("[Tutorial] start map=" + map.Id);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        private void Exit()
        {
            Debug.Log("[Tutorial] exit at " + Current);
            var flow = GameFlow.Instance;
            if (flow != null) flow.ReturnToTitle();
            Destroy(gameObject);
        }

        private void Enter(Step step)
        {
            Current = step;
            _stepStarted = Time.time;
            _pickOpened = false;
            if (_dummy != null && _dummyInput != null)
            {
                _dummyInput.Mode = step == Step.Block ? DummyInput.DummyMode.Shooter
                    : step == Step.Duel ? DummyInput.DummyMode.Fighter
                    : DummyInput.DummyMode.Idle;
            }
            if (step == Step.Block && _player != null) _blocksAtStart = _player.BlocksSucceeded;
            if (step == Step.Duel)
            {
                var flow = GameFlow.Instance;
                foreach (var f in flow.Fighters) flow.SandboxRespawn(f);
            }
            Debug.Log("[Tutorial] step " + step);
        }

        private void Complete(string message)
        {
            _successText = message;
            _successUntil = Time.time + 1.4f;
            if (Current == Step.Duel)
            {
                PlayerPrefs.SetInt(PrefKey, 1);
                PlayerPrefs.Save();
            }
            Enter(Current + 1);
        }

        private void OnDummyDied()
        {
            if (Current == Step.Duel) { Complete("CPU를 쓰러뜨렸습니다!"); return; }
            _dummyRespawnAt = Time.time + 1f;
        }

        private void Update()
        {
            var flow = GameFlow.Instance;
            if (flow == null || !flow.IsSandbox || _player == null) { if (flow != null && flow.InMenu) Destroy(gameObject); return; }

            if (_dummyRespawnAt > 0f && Time.time >= _dummyRespawnAt) { _dummyRespawnAt = -1f; flow.SandboxRespawn(_dummy); }
            if (_playerRespawnAt > 0f && Time.time >= _playerRespawnAt)
            {
                _playerRespawnAt = -1f;
                flow.SandboxRespawn(_player);
                if (Current == Step.Duel)
                {
                    _duelLosses++;
                    flow.SandboxRespawn(_dummy);
                    _successText = "다시 도전! 방어(G)로 탄을 되돌려 보세요";
                    _successUntil = Time.time + 2f;
                }
            }

            // Before the duel the tutorial is forgiving: health refills.
            if (Current < Step.Duel)
            {
                if (!_player.IsDead) _player.Heal(40f * Time.deltaTime);
                if (!_dummy.IsDead && Current != Step.Shoot) _dummy.Heal(1000f);
            }

            var kb = Keyboard.current;
            bool next = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            foreach (var gp in Gamepad.all) next |= gp.startButton.wasPressedThisFrame;

            switch (Current)
            {
                case Step.Move:
                    if (!_player.IsDead) { _moved += Mathf.Abs(_player.Position.x - _lastX); _lastX = _player.Position.x; }
                    if (_moved > 12f) Complete("좋아요!");
                    break;
                case Step.Jump:
                    if (_jumps >= 2) Complete("점프 완료!");
                    break;
                case Step.Shoot:
                    if (_hits >= 2) Complete("명중!");
                    else if (_dummy.Health < _dummy.MaxHealth * 0.3f) _dummy.Heal(1000f);
                    break;
                case Step.Reload:
                    if (_player.Ammo.isReloading) _sawReload = true;
                    else if (_sawReload) Complete("재장전 완료!");
                    break;
                case Step.Block:
                    if (_player.BlocksSucceeded > _blocksAtStart) Complete("방어 성공! 막힌 탄은 쏜 쪽으로 되돌아갑니다");
                    break;
                case Step.Edge:
                    Vector2 p = _player.Position;
                    if (Mathf.Abs(p.x) > Fighter.BoundsX || Mathf.Abs(p.y) > Fighter.BoundsY) Complete("경계에 닿으면 튕겨 나오며 피해 51을 받습니다");
                    else if (next && Time.time - _stepStarted > 1f) Complete("다음으로");
                    break;
                case Step.Cards:
                    if (!_pickOpened && Time.time - _stepStarted > 0.8f)
                    {
                        _pickOpened = true;
                        var offer = CardRules.DrawOffer(CardCatalog.Pool(null), _player.Cards, 3, new System.Random());
                        flow.SandboxPick(_player, offer, card => Complete("'" + card.Title + "' 카드가 적용됐습니다 (왼쪽 목록)"));
                    }
                    break;
                case Step.Rules:
                    if (next && Time.time - _stepStarted > 0.6f) Complete("이제 실전입니다");
                    break;
                case Step.Done:
                    if (next || Time.time - _stepStarted > 6f) Exit();
                    break;
            }
        }

        // ---------------------------------------------------------------- UI

        private static readonly string[] Titles =
        {
            "이동", "점프", "조준과 사격", "재장전", "방어", "화면 밖은 위험", "카드", "매치 규칙", "실전", "완료"
        };

        private string Body(GameFlow flow)
        {
            switch (Current)
            {
                case Step.Move: return "A / D (또는 게임패드 스틱)로 좌우로 움직여 보세요.  이동 " + Mathf.Min(12, Mathf.RoundToInt(_moved)) + " / 12";
                case Step.Jump: return "W 또는 Space(패드 A)로 두 번 점프하세요.  벽에 붙어 점프하면 반대쪽으로 튀어 오릅니다.  " + _jumps + " / 2";
                case Step.Shoot: return "마우스(또는 WASD)로 조준하고 좌클릭 또는 F로 쏘세요. 탄은 중력으로 떨어집니다.  명중 " + _hits + " / 2";
                case Step.Reload: return "탄창은 3발입니다. 모두 쏘면 자동으로 재장전돼요. 잠시 안 쏘고 있어도 다시 채워집니다.  탄을 다 써 보세요.";
                case Step.Block: return "더미가 쏘기 시작합니다. 탄이 닿기 직전에 G 또는 우클릭(패드 LT)으로 방어하세요. 방어 창은 0.3초, 쿨다운 4초입니다.";
                case Step.Edge: return "화면 가장자리는 위험 지역입니다. 아래로 떨어지거나 옆으로 나가 보세요. (Enter: 건너뛰기)";
                case Step.Cards: return "라운드를 지면 카드를 1장 고릅니다. ← / → 로 고르고 F(좌클릭은 안 됨) 또는 Space로 결정하세요.";
                case Step.Rules: return "2포인트 = 1라운드, 5라운드 선승. 포인트마다 맵이 바뀌고, 라운드를 진 쪽만 카드를 받습니다. 같은 팀 탄은 통과합니다.  (Enter: 계속)";
                case Step.Duel: return "CPU를 한 번 쓰러뜨리세요. 더 이상 체력이 회복되지 않습니다." + (_duelLosses > 0 ? "  (패배 " + _duelLosses + "회)" : "");
                default: return "튜토리얼을 마쳤습니다! 이제 CPU와 대전하거나 방을 만들어 친구와 플레이해 보세요.  (Enter)";
            }
        }

        private void OnGUI()
        {
            var flow = GameFlow.Instance;
            if (flow == null || !flow.IsSandbox || flow.IsPicking) return;
            UiKit.Begin();
            int index = Mathf.Min((int)Current, Titles.Length - 1);
            var panel = new Rect(W / 2 - 560, 18, 1120, 128);
            Fill(panel, new Color(0.05f, 0.05f, 0.08f, 0.82f));
            Fill(new Rect(panel.x, panel.yMax - 6, panel.width * index / (Titles.Length - 1f), 6), Palette.PlayerOne);
            Text(new Rect(panel.x + 24, panel.y + 10, 400, 40), "튜토리얼 " + (index + 1) + " / " + Titles.Length, new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft }, new Color(1, 1, 1, 0.6f));
            Shadowed(new Rect(panel.x, panel.y + 8, panel.width, 50), Titles[index], Mid, Palette.PlayerOne);
            Text(new Rect(panel.x + 30, panel.y + 58, panel.width - 60, 60), Body(flow), new GUIStyle(Small) { wordWrap = true }, Color.white);
            Text(new Rect(panel.xMax - 300, panel.y + 10, 280, 40), "Esc: 튜토리얼 종료", new GUIStyle(Tiny) { alignment = TextAnchor.MiddleRight }, new Color(1, 1, 1, 0.6f));
            if (_successText != null && Time.time < _successUntil)
            {
                Fill(new Rect(W / 2 - 420, 160, 840, 56), new Color(0.1f, 0.45f, 0.2f, 0.85f));
                Text(new Rect(W / 2 - 420, 160, 840, 56), _successText, Mid, Color.white);
            }
        }
    }
}

