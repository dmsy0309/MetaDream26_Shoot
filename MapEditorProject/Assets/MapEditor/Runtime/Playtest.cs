using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot;
using Metadream.Shoot.Content;
using Metadream.Shoot.Core;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.MapEditor
{
    /// <summary>Opponent that never moves (aim left), for measuring jumps and shots.</summary>
    public sealed class IdleInput : IInputSource
    {
        public string Label => "허수아비";
        public InputFrame Read(in InputContext context) => new InputFrame { Aim = Vector2.left };
    }

    /// <summary>
    /// Runs the map inside the game's own GameFlow / Fighter / MapRoot / Projectile code (the synced
    /// Metadream.Shoot assembly) with the game's content loader. Nothing here re-implements game rules or physics.
    /// </summary>
    public sealed class Playtest
    {
        public enum Kind { Sandbox, VersusCpu, CpuDemo, CpuDemoTeams }
        public enum Opponent { None, Dummy, Cpu, Teams }

        public bool Running => _flow != null;
        public Kind Mode { get; private set; }
        public Opponent Against { get; private set; }
        public string MapName { get; private set; } = "";
        public GameFlow Flow => _flow;
        public bool SlowMotion { get; private set; }

        private GameObject _root;
        private GameFlow _flow;
        private CameraRig _rig;
        private Camera _cam;
        private Rect _camRect;
        private Vector3 _camPos;
        private float _camSize;
        private Color _camColor;
        private Action _onStopped;
        private readonly Dictionary<Fighter, float> _deadSince = new Dictionary<Fighter, float>();

        public static string KindName(Kind k, Opponent o)
        {
            switch (k)
            {
                case Kind.VersusCpu: return "CPU와 대전 (실제 규칙)";
                case Kind.CpuDemo: return "CPU 관전 1대1";
                case Kind.CpuDemoTeams: return "CPU 관전 2대2";
                default:
                    return "자유 테스트" + (o == Opponent.Dummy ? " · 허수아비" : o == Opponent.Cpu ? " · CPU" : o == Opponent.Teams ? " · 2대2" : "");
            }
        }

        /// <summary>Returns an error message, or null when the play test started.</summary>
        public string Start(MapDocument doc, Kind kind, Opponent opponent, int roundsToWin, string gameContentDir, Camera cam, Action onStopped)
        {
            if (Running) Stop();
            var issues = GameContent.Validate(doc, true, out var parsed);
            if (parsed == null)
                return "게임이 이 맵을 불러오지 않습니다 (검증 오류 " + GameContent.ErrorCount(issues) + "개). 오른쪽 '검증' 목록을 확인하세요.";
            string id = GameContent.SanitizeId(doc.Data.id);
            string dir = GameContent.PreparePlaytestContent(doc, gameContentDir);
            ContentDatabase.ExternalDirectory = dir;
            ContentDatabase.Reload();
            var def = ContentDatabase.FindMap(id);
            if (def == null) return "게임 콘텐츠 로더가 맵 '" + id + "'을 불러오지 못했습니다.";

            Mode = kind;
            Against = opponent;
            MapName = def.Name;
            _onStopped = onStopped;
            _cam = cam;
            _camRect = cam.rect;
            _camPos = cam.transform.position;
            _camSize = cam.orthographicSize;
            _camColor = cam.backgroundColor;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            CameraRig.Zoom = 1f;
            _rig = cam.gameObject.AddComponent<CameraRig>();

            // Same frame settings as GameBootstrap.Awake.
            Application.targetFrameRate = 120;
            Time.fixedDeltaTime = 0.02f;
            Time.timeScale = 1f;
            SlowMotion = false;

            _root = new GameObject("Playtest");
            _flow = _root.AddComponent<GameFlow>();
            _root.AddComponent<GameHud>();
            _deadSince.Clear();

            int seed = Environment.TickCount;
            var settings = new MatchSettings { mapPool = new[] { id }, roundsToWinGame = Mathf.Clamp(roundsToWin, 1, 9) };
            switch (kind)
            {
                case Kind.Sandbox:
                    _flow.StartSandbox(def, SandboxSlots(opponent, seed));
                    break;
                case Kind.VersusCpu:
                    _flow.StartMatch(GameMode.VersusCpu, settings, seed);
                    break;
                case Kind.CpuDemo:
                    _flow.StartMatch(GameMode.CpuDemo, settings, seed);
                    break;
                case Kind.CpuDemoTeams:
                    _flow.StartMatch(GameMode.CpuDemoTeams, settings, seed);
                    break;
            }
            _flow.EscapeOverride = Stop;
            Debug.Log("[MapEditor] playtest start " + kind + " opponent=" + opponent + " map=" + id + " content=" + dir);
            return null;
        }

        private static List<SlotSpec> SandboxSlots(Opponent opponent, int seed)
        {
            var list = new List<SlotSpec>();
            void Add(string name, IInputSource input)
            {
                int slot = list.Count;
                list.Add(new SlotSpec { Name = name, Team = slot % 2, Color = Palette.SlotColor(slot), Input = input });
            }
            Add("P1", new DeviceInput(KeyboardBinding.PlayerOne(), true, 0, "P1"));
            switch (opponent)
            {
                case Opponent.Dummy: Add("허수아비", new IdleInput()); break;
                case Opponent.Cpu: Add("CPU", new BotInput(seed, "CPU")); break;
                case Opponent.Teams:
                    Add("CPU 상대 1", new BotInput(seed + 1, "CPU 상대 1"));
                    Add("CPU 팀원", new BotInput(seed + 2, "CPU 팀원"));
                    Add("CPU 상대 2", new BotInput(seed + 3, "CPU 상대 2"));
                    break;
            }
            return list;
        }

        public void Update()
        {
            if (_flow == null) return;
            if (_flow.InMenu) { Stop(); return; }
            if (_flow.EscapeOverride == null) _flow.EscapeOverride = Stop;
            var kb = Keyboard.current;
            if (_flow.IsSandbox)
            {
                foreach (var f in _flow.Fighters)
                {
                    if (f == null) continue;
                    if (!f.IsDead) { _deadSince.Remove(f); continue; }
                    if (!_deadSince.TryGetValue(f, out float t)) _deadSince[f] = Time.unscaledTime;
                    else if (Time.unscaledTime - t > 1.0f) { _flow.SandboxRespawn(f); _deadSince.Remove(f); }
                }
                if (kb != null && kb.rKey.wasPressedThisFrame && GUIUtility.keyboardControl == 0) RespawnAll();
            }
            if (kb != null && kb.tKey.wasPressedThisFrame) ToggleSlowMotion();
            if (SlowMotion && Time.timeScale >= 0.99f) Time.timeScale = 0.25f;
        }

        public void RespawnAll()
        {
            if (_flow == null || !_flow.IsSandbox) return;
            foreach (var f in _flow.Fighters) if (f != null) _flow.SandboxRespawn(f);
            _deadSince.Clear();
        }

        public void ToggleSlowMotion()
        {
            SlowMotion = !SlowMotion;
            Time.timeScale = SlowMotion ? 0.25f : 1f;
        }

        public void Stop()
        {
            if (_flow == null) return;
            var flow = _flow;
            _flow = null;
            flow.EscapeOverride = null;
            flow.ReturnToTitle();
            if (flow.CurrentMap != null) UnityEngine.Object.Destroy(flow.CurrentMap.gameObject);
            UnityEngine.Object.Destroy(_root);
            if (_rig != null) UnityEngine.Object.Destroy(_rig);
            Time.timeScale = 1f;
            SlowMotion = false;
            if (_cam != null)
            {
                _cam.rect = _camRect;
                _cam.transform.position = _camPos;
                _cam.orthographicSize = _camSize;
                _cam.backgroundColor = _camColor;
            }
            Debug.Log("[MapEditor] playtest stop");
            var done = _onStopped;
            _onStopped = null;
            done?.Invoke();
        }
    }
}
