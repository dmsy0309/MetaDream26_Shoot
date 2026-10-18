using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Network;
using Metadream.Shoot.Presentation;
using static Metadream.Shoot.Presentation.UiKit;

namespace Metadream.Shoot.Replay
{
    /// <summary>
    /// Plays a .mdreplay through the same client mirror used by online clients, so the picture matches the game.
    /// Controls: Space pause · ←/→ 10 s · -/+ speed (0.25–16×) · wheel zoom · right-drag pan · left-click a fighter
    /// for details · click/drag the progress bar to seek · Esc exit.
    /// </summary>
    public sealed class ReplayPlayer : MonoBehaviour
    {
        public static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f, 8f, 16f };
        private const float BarHeight = 150f;
        private static readonly Rect InfoRect = new Rect(1920 - 480, 250, 460, 470);

        public static ReplayPlayer Active { get; private set; }

        private ReplayData _data;
        private readonly MatchSnapshot _snap = new MatchSnapshot();
        private readonly Dictionary<int, ProjectileProxy> _proxies = new Dictionary<int, ProjectileProxy>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _stale = new List<int>();
        private readonly List<KeyValuePair<float, bool>> _marks = new List<KeyValuePair<float, bool>>();
        private int _frame = -1;
        private float _time;
        private int _speed = 2;
        private bool _paused;
        private int _selected = -1;
        private float _zoom = 1f;
        private Vector2 _focus;
        private bool _dragBar;
        private bool _panning;
        private Vector2 _panLast;
        private bool _manualCamera;

        public float CurrentTime => _time;
        public float Duration => _data.Duration;
        public bool Paused => _paused;
        public float Speed => Speeds[_speed];
        public int FrameIndex => _frame;
        public int Selected => _selected;

        public static ReplayPlayer Play(ReplayData data)
        {
            if (data == null || data.Frames.Count == 0) throw new InvalidDataException("빈 리플레이입니다");
            if (Active != null) Active.Exit(false);
            var go = new GameObject("ReplayPlayer");
            var p = go.AddComponent<ReplayPlayer>();
            Active = p;
            p._data = data;
            p.Begin();
            return p;
        }

        private void Begin()
        {
            Decode(0);
            var names = new List<string>();
            for (int i = 0; i < _snap.Fighters.Count; i++)
            {
                var info = PlayerInfo(i);
                names.Add(info != null && !string.IsNullOrEmpty(info.nickname) ? info.nickname : "P" + (i + 1));
            }
            var flow = GameFlow.Instance;
            flow.BeginMirror(names, -1);
            flow.ReplayMode = true;
            flow.MirrorGameOverText = null;
            flow.EscapeOverride = () => Exit(true);
            BuildMarks();
            Seek(0f);
            Debug.Log("[Replay] play " + Path.GetFileName(_data.Path) + " frames=" + _data.Frames.Count + " duration=" + Duration.ToString("0.0") + " players=" + _data.Meta.PlayersLabel);
        }

        private ReplayPlayerInfo PlayerInfo(int slot)
        {
            foreach (var p in _data.Meta.players) if (p.slot == slot) return p;
            return null;
        }

        /// <summary>Score/round changes become ticks on the progress bar (read from fixed snapshot offsets).</summary>
        private void BuildMarks()
        {
            int last = -1;
            int lastRounds = -1;
            foreach (var f in _data.Frames)
            {
                if (f.Data.Length < 15) continue;
                int rounds = f.Data[12] + f.Data[14];
                int key = f.Data[11] + f.Data[13] + rounds * 100;
                if (last >= 0 && key != last) _marks.Add(new KeyValuePair<float, bool>(f.Time, rounds != lastRounds));
                last = key;
                lastRounds = rounds;
            }
        }

        // ------------------------------------------------------------ playback

        private int IndexAt(float t)
        {
            var frames = _data.Frames;
            int lo = 0, hi = frames.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (frames[mid].Time <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        private void Decode(int index)
        {
            using (var r = new BinaryReader(new MemoryStream(_data.Frames[index].Data))) _snap.Read(r);
        }

        public void Seek(float t)
        {
            _time = Mathf.Clamp(t, 0f, Duration);
            _frame = IndexAt(_time);
            Decode(_frame);
            Fx.ClearAll();
            GameFlow.Instance.SeekMirror(_snap);
            SyncProjectiles();
        }

        private void Step(int index, bool playFx)
        {
            _frame = index;
            Decode(index);
            if (!playFx) _snap.Fx.Clear();
            GameFlow.Instance.ApplyMirror(_snap);
            SyncProjectiles();
        }

        public void TogglePause() => _paused = !_paused;

        public void SetSpeedIndex(int i) => _speed = Mathf.Clamp(i, 0, Speeds.Length - 1);

        public void SetZoom(float zoom, Vector2? anchor = null)
        {
            float old = _zoom;
            _zoom = Mathf.Clamp(zoom, 1f, 5f);
            if (anchor.HasValue && _zoom > 1f) _focus += (anchor.Value - _focus) * (1f - old / _zoom);
            if (_zoom <= 1.001f) _focus = Vector2.zero;
        }

        public void Select(int slot)
        {
            _selected = slot;
            _manualCamera = false;
        }

        public void ToggleDebugUi() => DebugOverlay.Suppressed = !DebugOverlay.Suppressed;

        private void Update()
        {
            var flow = GameFlow.Instance;
            if (flow == null || !flow.ReplayMode) { Exit(false); return; }
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) TogglePause();
                if (kb.leftArrowKey.wasPressedThisFrame) Seek(_time - 10f);
                if (kb.rightArrowKey.wasPressedThisFrame) Seek(_time + 10f);
                if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) SetSpeedIndex(_speed - 1);
                if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) SetSpeedIndex(_speed + 1);
                if (kb.homeKey.wasPressedThisFrame) Seek(0f);
                if (kb.tabKey.wasPressedThisFrame) ToggleDebugUi();
                // Free camera after zooming: WASD pans (and stops following the selected fighter).
                Vector2 pan = Vector2.zero;
                if (kb.wKey.isPressed) pan.y += 1f;
                if (kb.sKey.isPressed) pan.y -= 1f;
                if (kb.aKey.isPressed) pan.x -= 1f;
                if (kb.dKey.isPressed) pan.x += 1f;
                if (pan != Vector2.zero && _zoom > 1.01f)
                {
                    _manualCamera = true;
                    float speed = (kb.leftShiftKey.isPressed ? 60f : 28f) / _zoom;
                    _focus += pan.normalized * speed * Time.unscaledDeltaTime;
                }
            }
            var cam = Camera.main;
            if (mouse != null && cam != null)
            {
                Vector2 screen = mouse.position.ReadValue();
                Vector2 gui = new Vector2(screen.x * W / Mathf.Max(1, Screen.width), H - screen.y * H / Mathf.Max(1, Screen.height));
                bool overUi = gui.y > H - BarHeight || (_selected >= 0 && InfoRect.Contains(gui));
                Vector2 world = cam.ScreenToWorldPoint(screen);
                float wheel = mouse.scroll.ReadValue().y;
                if (!overUi && Mathf.Abs(wheel) > 0.01f) SetZoom(_zoom * (wheel > 0 ? 1.15f : 1f / 1.15f), world);
                if (mouse.leftButton.wasPressedThisFrame && !overUi) Select(FighterAt(world));
                if (mouse.rightButton.wasPressedThisFrame && !overUi) { _panning = true; _panLast = world; }
                if (!mouse.rightButton.isPressed) _panning = false;
                if (_panning && _zoom > 1f) { _focus -= world - _panLast; _manualCamera = true; }
            }

            if (!_paused)
            {
                _time += Time.unscaledDeltaTime * Speed;
                if (_time >= Duration) { _time = Duration; _paused = true; }
            }
            int target = IndexAt(_time);
            if (target < _frame || target - _frame > 90) Seek(_time);
            else
                while (_frame < target)
                {
                    int next = _frame + 1;
                    Step(next, target - next < 8);
                }
            Time.timeScale = _paused ? 0f : Mathf.Clamp(_snap.TimeScale * Speed, 0.01f, 100f);

            if (_selected >= 0 && _selected < flow.Fighters.Count && _zoom > 1.01f && !_panning && !_manualCamera)
                _focus = Vector2.Lerp(_focus, flow.Fighters[_selected].Position, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            float maxX = 35.56f * (1f - 1f / _zoom), maxY = 20f * (1f - 1f / _zoom);
            _focus = new Vector2(Mathf.Clamp(_focus.x, -maxX, maxX), Mathf.Clamp(_focus.y, -maxY, maxY));
            CameraRig.Zoom = _zoom;
            CameraRig.Focus = _focus;
        }

        private int FighterAt(Vector2 world)
        {
            var flow = GameFlow.Instance;
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < flow.Fighters.Count; i++)
            {
                var f = flow.Fighters[i];
                if (f == null || !f.IsVisible) continue;
                float d = Vector2.Distance(f.Position, world);
                if (d < f.Radius + 1.2f && d < bestDist) { best = i; bestDist = d; }
            }
            return best;
        }

        private void SyncProjectiles()
        {
            var flow = GameFlow.Instance;
            _seen.Clear();
            foreach (var s in _snap.Projectiles)
            {
                _seen.Add(s.Id);
                if (_proxies.TryGetValue(s.Id, out var proxy) && proxy != null) proxy.Set(s);
                else
                {
                    Color c = s.Owner >= 0 && s.Owner < flow.Fighters.Count ? flow.Fighters[s.Owner].Color : Color.white;
                    _proxies[s.Id] = ProjectileProxy.Create(s, c);
                }
            }
            _stale.Clear();
            foreach (var kv in _proxies) if (!_seen.Contains(kv.Key)) _stale.Add(kv.Key);
            foreach (var id in _stale)
            {
                if (_proxies[id] != null) Destroy(_proxies[id].gameObject);
                _proxies.Remove(id);
            }
        }

        public void Exit(bool showList)
        {
            if (Active == this) Active = null;
            foreach (var p in _proxies.Values) if (p != null) Destroy(p.gameObject);
            _proxies.Clear();
            CameraRig.Zoom = 1f;
            CameraRig.Focus = Vector2.zero;
            DebugOverlay.Suppressed = false;
            Time.timeScale = 1f;
            var flow = GameFlow.Instance;
            if (flow != null && flow.ReplayMode)
            {
                flow.ReplayMode = false;
                flow.EndMirror();
                flow.Role = NetRole.Offline;
            }
            if (showList) FrontEnd.Instance?.OpenReplaysPage();
            Destroy(gameObject);
        }

        // ------------------------------------------------------------ UI

        private static string Clock(float t) => ((int)(t / 60f)).ToString("00") + ":" + ((int)t % 60).ToString("00");

        private bool Button(Rect r, string label, bool active = false)
        {
            Fill(r, active ? new Color(1f, 0.8f, 0.3f, 0.95f) : new Color(1, 1, 1, 0.16f));
            Text(r, label, new GUIStyle(Small) { fontSize = 22 }, active ? Palette.Ink : Color.white);
            return Clicked(r);
        }

        private void OnGUI()
        {
            var flow = GameFlow.Instance;
            if (flow == null || !flow.ReplayMode || _data == null) return;
            UiKit.Begin();
            GUI.depth = -50;
            var bar = new Rect(0, H - BarHeight, W, BarHeight);
            Fill(bar, new Color(0.05f, 0.05f, 0.08f, 0.88f));

            // progress bar
            var track = new Rect(150, H - BarHeight + 16, W - 300, 26);
            Fill(track, new Color(1, 1, 1, 0.15f));
            float k = Duration > 0f ? _time / Duration : 0f;
            Fill(new Rect(track.x, track.y, track.width * k, track.height), Palette.PlayerOne);
            foreach (var m in _marks)
            {
                float mx = track.x + track.width * (m.Key / Mathf.Max(0.01f, Duration));
                Fill(new Rect(mx - (m.Value ? 2 : 1), track.y - (m.Value ? 6 : 2), m.Value ? 4 : 2, track.height + (m.Value ? 12 : 4)), m.Value ? new Color(1f, 0.9f, 0.3f) : new Color(1, 1, 1, 0.55f));
            }
            Fill(new Rect(track.x + track.width * k - 4, track.y - 6, 8, track.height + 12), Color.white);
            Text(new Rect(10, track.y - 4, 130, 34), Clock(_time), new GUIStyle(Small) { alignment = TextAnchor.MiddleRight }, Color.white);
            Text(new Rect(track.xMax + 10, track.y - 4, 130, 34), Clock(Duration), new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft }, new Color(1, 1, 1, 0.7f));
            var e = Event.current;
            var hit = new Rect(track.x, track.y - 10, track.width, track.height + 20);
            if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition)) { _dragBar = true; e.Use(); }
            if (_dragBar && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag || e.type == EventType.Used))
                Seek(Duration * Mathf.Clamp01((e.mousePosition.x - track.x) / track.width));
            if (e.rawType == EventType.MouseUp) _dragBar = false;

            // buttons
            float y = H - 98, h = 50, x = 150;
            if (Button(new Rect(x, y, 90, h), "처음")) _deferred = () => Seek(0f); x += 100;
            if (Button(new Rect(x, y, 120, h), "◀◀ 10초")) _deferred = () => Seek(_time - 10f); x += 130;
            if (Button(new Rect(x, y, 120, h), _paused ? "▶ 재생" : "❚❚ 정지", _paused)) _deferred = TogglePause; x += 130;
            if (Button(new Rect(x, y, 120, h), "10초 ▶▶")) _deferred = () => Seek(_time + 10f); x += 150;
            Text(new Rect(x, y, 60, h), "배속", Small, new Color(1, 1, 1, 0.7f)); x += 60;
            for (int i = 0; i < Speeds.Length; i++)
            {
                int idx = i;
                string label = Speeds[i] < 1f ? Speeds[i].ToString("0.##") + "×" : ((int)Speeds[i]) + "×";
                if (Button(new Rect(x, y, 66, h), label, i == _speed)) _deferred = () => SetSpeedIndex(idx);
                x += 70;
            }
            x += 20;
            Text(new Rect(x, y, 50, h), "줌", Small, new Color(1, 1, 1, 0.7f)); x += 50;
            if (Button(new Rect(x, y, 50, h), "－")) _deferred = () => SetZoom(_zoom / 1.25f, _focus); x += 56;
            if (Button(new Rect(x, y, 86, h), _zoom.ToString("0.0") + "×", _zoom > 1.01f)) _deferred = () => SetZoom(1f); x += 92;
            if (Button(new Rect(x, y, 50, h), "＋")) _deferred = () => SetZoom(_zoom * 1.25f, _selected >= 0 && _selected < flow.Fighters.Count ? flow.Fighters[_selected].Position : _focus);
            if (Button(new Rect(W - 440, y, 140, h), "디버그 UI", !DebugOverlay.Suppressed)) _deferred = ToggleDebugUi;
            if (Button(new Rect(W - 290, y, 140, h), "종료 (Esc)")) _deferred = () => Exit(true);
            Text(new Rect(0, H - 40, W, 34), "Space 정지 · ←/→ 10초 · -/+ 배속 · 진행 바 클릭/드래그 · 휠 줌 · 줌 후 WASD(Shift 빠르게)/우클릭 드래그로 화면 이동 · 캐릭터 좌클릭 정보·추적 · Tab 디버그 UI", new GUIStyle(Tiny) { alignment = TextAnchor.MiddleCenter, fontSize = 17 }, new Color(1, 1, 1, 0.55f));

            // header
            var m0 = _data.Meta;
            Shadowed(new Rect(20, H - BarHeight - 44, 1300, 36), "리플레이 · " + m0.roomName + " · " + m0.createdLocal + " · 생성 기기 " + m0.device.machineName + (m0.dedicated ? " (전용 서버)" : " (방장)") + " · " + Path.GetFileName(_data.Path), new GUIStyle(Tiny) { fontSize = 18 }, new Color(1, 1, 1, 0.85f));
            if (!string.IsNullOrEmpty(m0.contentHash) && m0.contentHash != Content.ContentDatabase.Hash)
                Shadowed(new Rect(20, H - BarHeight - 74, 1300, 30), "주의: 이 리플레이는 다른 카드/맵 콘텐츠로 만들어졌습니다 (카드 이름이 다르게 보일 수 있음)", new GUIStyle(Tiny) { fontSize = 18 }, new Color(1f, 0.6f, 0.4f));

            DrawInfo(flow);
            if (_deferred != null && e.type == EventType.Repaint) { var a = _deferred; _deferred = null; a(); }
        }

        private Action _deferred;

        private void DrawInfo(GameFlow flow)
        {
            if (_selected < 0 || _selected >= flow.Fighters.Count || _selected >= _snap.Fighters.Count) return;
            var f = flow.Fighters[_selected];
            var s = _snap.Fighters[_selected];
            var p = PlayerInfo(_selected);
            var r = InfoRect;
            Fill(r, new Color(0.05f, 0.05f, 0.08f, 0.85f));
            Fill(new Rect(r.x, r.y, 8, r.height), f.Color);
            Shadowed(new Rect(r.x + 24, r.y + 10, r.width - 70, 44), f.DisplayName, new GUIStyle(Mid) { alignment = TextAnchor.MiddleLeft }, f.Color);
            if (Clicked(new Rect(r.xMax - 50, r.y + 10, 40, 40))) _deferred = () => Select(-1);
            Text(new Rect(r.xMax - 50, r.y + 10, 40, 40), "✕", Small, Color.white);
            var lines = new List<string>
            {
                Palette.TeamName(f.TeamId) + " · 슬롯 " + (_selected + 1) + " · " + (p == null ? "?" : p.kind == "cpu" ? "CPU" : p.kind == "host" ? "방장" : "참가자"),
                "기기: " + (p != null && !string.IsNullOrEmpty(p.device) ? p.device : "-") + (p != null && !string.IsNullOrEmpty(p.address) ? "  (" + p.address + ")" : ""),
                "체력: " + Mathf.Max(0f, s.Health).ToString("0") + " / " + s.MaxHealth.ToString("0") + (s.Dead ? "  · 사망" : ""),
                "탄: " + s.Ammo + " / " + s.MaxAmmo + (s.Reloading ? "  · 재장전 " + Mathf.RoundToInt(s.ReloadProgress * 100f) + "%" : ""),
                "방어: " + (s.BlockReady >= 0.999f ? "준비됨" : Mathf.RoundToInt(s.BlockReady * 100f) + "%"),
                "상태: " + (s.Grounded ? "지면" : "공중") + " · 속도 " + s.Velocity.magnitude.ToString("0.0") + " · 위치 (" + s.Position.x.ToString("0.0") + ", " + s.Position.y.ToString("0.0") + ")",
                "카드:"
            };
            if (s.Cards != null && s.Cards.Length > 0)
                foreach (var c in s.Cards) lines.Add("  · " + (c < CardCatalog.All.Count ? CardCatalog.All[c].Title : "#" + c));
            else lines.Add("  (없음)");
            for (int i = 0; i < lines.Count && i < 15; i++)
                Text(new Rect(r.x + 24, r.y + 64 + i * 26, r.width - 36, 26), lines[i], new GUIStyle(Tiny) { fontSize = 18, clipping = TextClipping.Clip }, Color.white);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }
    }
}
