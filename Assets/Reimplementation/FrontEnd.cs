using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot.Content;
using Metadream.Shoot.Network;
using Metadream.Shoot.Online;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;
using Metadream.Shoot.Tutorial;
using static Metadream.Shoot.Presentation.UiKit;

namespace Metadream.Shoot
{
    /// <summary>Title menu, settings, room hosting lobby, room browser/join (LAN + main hosting server) and the joined-room lobby.</summary>
    public sealed class FrontEnd : MonoBehaviour
    {
        private enum Page
        {
            Title,
            Join,
            Notice,
            Settings,
            Nickname,
            Replays
        }

        private struct Item
        {
            public string Label;
            public string Hint;
            public Action Run;
        }

        private struct JoinEntry
        {
            public string Label;
            public string Host;
            public int Port;
            public bool FromMaster;
            /// <summary>Non-null when joining is impossible (other version...).</summary>
            public string Blocked;
        }

        public static FrontEnd Instance { get; private set; }

        private Item[] _items;
        private Item[] _baseItems;
        private Item[] _debugItems;
        private Page _page = Page.Title;
        private int _index;
        private string _code = "";
        private string _notice;
        private string _joinError;
        private int _roomIndex;
        private int _settingIndex;
        private bool _focusField;
        private LanListener _listener;
        private MasterServerClient _master;
        private readonly List<JoinEntry> _entries = new List<JoinEntry>();
        private Action _deferred;
        private string _nickDraft = "";
        private Action _afterNickname;
        private List<Replay.ReplayData> _replays = new List<Replay.ReplayData>();
        private int _replayIndex;
        private string _replayPath = "";
        private string _replayMessage;

        private static GameFlow Flow => GameFlow.Instance;

        private void Awake()
        {
            Instance = this;
            _baseItems = new[]
            {
                new Item { Label = "튜토리얼", Hint = "이동 · 점프 · 사격 · 방어 · 카드 · 규칙 배우기", Run = StartTutorial },
                new Item { Label = "CPU와 대전", Hint = "1대1", Run = () => Flow.StartMatch(GameMode.VersusCpu) },
                new Item { Label = "CPU 관전 1대1", Hint = "데모", Run = () => Flow.StartMatch(GameMode.CpuDemo) },
                new Item { Label = "CPU 관전 2대2", Hint = "데모", Run = () => Flow.StartMatch(GameMode.CpuDemoTeams) },
                new Item { Label = "방 만들기", Hint = "온라인 1대1 / 2대2 호스트", Run = () => AskNickname(HostRoom) },
                new Item { Label = "방 참가", Hint = "서버 목록 · 같은 네트워크의 방 · 방 코드 · IP", Run = () => AskNickname(OpenJoin) },
                new Item { Label = "설정", Hint = "디버그 모드 · 전체 화면 · 서버 목록", Run = OpenSettings },
                new Item { Label = "게임 종료", Hint = "", Run = QuitGame },
            };
            var list = new List<Item>(_baseItems);
            list.Insert(6, new Item { Label = "리플레이", Hint = "디버그 모드 · 저장된 게임 재생 · 불러오기", Run = OpenReplaysPage });
            _debugItems = list.ToArray();
            _items = _baseItems;
        }

        private void RefreshItems()
        {
            var want = DevSettings.DebugMode ? _debugItems : _baseItems;
            if (_items == want) return;
            string current = _items != null && _index < _items.Length ? _items[_index].Label : null;
            _items = want;
            _index = Mathf.Max(0, Array.FindIndex(_items, it => it.Label == current));
        }

        // ------------------------------------------------------------ nickname

        /// <summary>Shows the nickname page (prefilled with the saved name) before hosting or joining.</summary>
        private void AskNickname(Action next)
        {
            _afterNickname = next;
            _nickDraft = Online.PlayerProfile.Nickname;
            _notice = null;
            _focusField = true;
            _page = Page.Nickname;
        }

        private void ConfirmNickname()
        {
            string n = Online.PlayerProfile.Sanitize(_nickDraft);
            if (n == null) { _notice = "닉네임을 입력하세요"; return; }
            Online.PlayerProfile.Nickname = n;
            _notice = null;
            _page = Page.Title;
            var next = _afterNickname;
            _afterNickname = null;
            next?.Invoke();
        }

        // ------------------------------------------------------------ replays (debug mode)

        public void OpenReplaysPage()
        {
            CloseListener();
            _page = Page.Replays;
            _replayIndex = 0;
            _replayMessage = null;
            RefreshReplays();
        }

        private void RefreshReplays()
        {
            try { _replays = Replay.ReplayStore.List(); }
            catch (Exception e) { _replays = new List<Replay.ReplayData>(); _replayMessage = "목록을 읽지 못했습니다: " + e.Message; }
            _replayIndex = Mathf.Clamp(_replayIndex, 0, Mathf.Max(0, _replays.Count - 1));
        }

        private void PlayReplay(int i)
        {
            if (i < 0 || i >= _replays.Count) return;
            try
            {
                var data = Replay.ReplayFile.Load(_replays[i].Path);
                _page = Page.Title;
                Replay.ReplayPlayer.Play(data);
            }
            catch (Exception e)
            {
                _replayMessage = "재생할 수 없습니다: " + e.Message;
            }
        }

        private void ImportReplay(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            try
            {
                string dest = Replay.ReplayStore.Import(source.Trim().Trim('"'));
                RefreshReplays();
                _replayIndex = Mathf.Max(0, _replays.FindIndex(r => r.Path == dest));
                _replayMessage = "가져옴: " + System.IO.Path.GetFileName(dest);
                _replayPath = "";
            }
            catch (Exception e)
            {
                _replayMessage = "가져오지 못했습니다: " + e.Message;
            }
        }

        private void OnDestroy()
        {
            CloseListener();
            if (Instance == this) Instance = null;
        }

        public void ShowNotice(string message)
        {
            CloseListener();
            _notice = message;
            _page = Page.Notice;
        }

        public void ShowTitle()
        {
            CloseListener();
            _page = Page.Title;
        }

        private void HostRoom()
        {
            var s = NetHostSession.Open(new HostOptions { TeamSize = 1, MasterServer = DevSettings.UseMasterServer }, out string error);
            if (s == null) ShowNotice(error);
        }

        private void StartTutorial()
        {
            CloseListener();
            TutorialController.Launch();
        }

        public void OpenSettingsPage() => OpenSettings();

        public void OpenJoinPage() => OpenJoin();

        private void OpenSettings()
        {
            _page = Page.Settings;
            _settingIndex = 0;
        }

        public static void QuitGame()
        {
            Debug.Log("[UI] quit");
            if (NetHostSession.Active != null) NetHostSession.Active.Close();
            if (NetClientSession.Active != null) NetClientSession.Active.Leave(null);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(0);
#endif
        }

        private void OpenJoin()
        {
            _page = Page.Join;
            _code = "";
            _joinError = null;
            _roomIndex = 0;
            _focusField = true;
            CloseListener();
            _listener = new LanListener();
            if (DevSettings.UseMasterServer) _master = new MasterServerClient();
        }

        private void CloseListener()
        {
            _listener?.Dispose();
            _listener = null;
            _master?.Dispose();
            _master = null;
            _entries.Clear();
        }

        /// <summary>Main hosting server list first, then LAN rooms that the master does not already list.</summary>
        private void RefreshEntries()
        {
            _entries.Clear();
            var codes = new HashSet<string>();
            if (_master != null)
                foreach (var s in _master.Servers)
                {
                    if (!string.IsNullOrEmpty(s.code)) codes.Add(s.code);
                    _entries.Add(new JoinEntry
                    {
                        Label = s.Label, Host = s.address, Port = s.port, FromMaster = true,
                        Blocked = !s.Compatible ? "다른 버전의 서버입니다 (서버 v" + s.version + ", 내 게임 v" + Protocol.Version + ")"
                            : !string.IsNullOrEmpty(s.contentHash) && s.contentHash != ContentDatabase.Hash ? "카드/맵 콘텐츠가 다른 서버입니다"
                            : null
                    });
                }
            if (_listener != null)
                foreach (var room in _listener.Rooms)
                {
                    if (codes.Contains(room.Code)) continue;
                    _entries.Add(new JoinEntry { Label = "[LAN] " + room.Label, Host = room.Host, Port = room.Port });
                }
        }

        public void JoinTarget(string host, int port, bool autoplay = false)
        {
            CloseListener();
            _page = Page.Title;
            NetClientSession.Join(host, port, autoplay);
        }

        // ------------------------------------------------------------ input

        private static bool Down(Keyboard kb, Key k) => kb != null && kb[k].wasPressedThisFrame;

        private void Update()
        {
            if (_deferred != null)
            {
                var a = _deferred;
                _deferred = null;
                a();
            }
            var flow = Flow;
            if (flow == null || !flow.InMenu) return;
            RefreshItems();
            var kb = Keyboard.current;
            bool padUp = false, padDown = false, padLeft = false, padRight = false, padOk = false, padBack = false;
            foreach (var gp in Gamepad.all)
            {
                padUp |= gp.dpad.up.wasPressedThisFrame;
                padDown |= gp.dpad.down.wasPressedThisFrame;
                padLeft |= gp.dpad.left.wasPressedThisFrame;
                padRight |= gp.dpad.right.wasPressedThisFrame;
                padOk |= gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame;
                padBack |= gp.buttonEast.wasPressedThisFrame;
            }
            bool enter = Down(kb, UnityEngine.InputSystem.Key.Enter) || Down(kb, UnityEngine.InputSystem.Key.NumpadEnter) || padOk;
            bool back = Down(kb, UnityEngine.InputSystem.Key.Escape) || padBack;
            bool arrowUp = Down(kb, UnityEngine.InputSystem.Key.UpArrow) || padUp;
            bool arrowDown = Down(kb, UnityEngine.InputSystem.Key.DownArrow) || padDown;

            var host = NetHostSession.Active;
            if (host != null)
            {
                bool l = Down(kb, UnityEngine.InputSystem.Key.LeftArrow) || Down(kb, UnityEngine.InputSystem.Key.A) || padLeft;
                bool r = Down(kb, UnityEngine.InputSystem.Key.RightArrow) || Down(kb, UnityEngine.InputSystem.Key.D) || padRight;
                if (l) host.SetTeamSize(1);
                if (r) host.SetTeamSize(2);
                if (enter || Down(kb, UnityEngine.InputSystem.Key.Space)) host.StartMatch();
                else if (back) { host.Close(); ShowTitle(); }
                return;
            }
            var client = NetClientSession.Active;
            if (client != null)
            {
                if (back) client.Leave(null);
                else if (client.IsLeader && client.Lobby != null && !client.Lobby.InMatch)
                {
                    if (enter || Down(kb, UnityEngine.InputSystem.Key.Space)) client.RequestStart();
                    if (Down(kb, UnityEngine.InputSystem.Key.LeftArrow) || Down(kb, UnityEngine.InputSystem.Key.A) || padLeft) client.RequestTeamSize(1);
                    if (Down(kb, UnityEngine.InputSystem.Key.RightArrow) || Down(kb, UnityEngine.InputSystem.Key.D) || padRight) client.RequestTeamSize(2);
                }
                return;
            }

            switch (_page)
            {
                case Page.Title:
                {
                    int n = _items.Length;
                    if (arrowUp || Down(kb, UnityEngine.InputSystem.Key.W)) _index = (_index - 1 + n) % n;
                    if (arrowDown || Down(kb, UnityEngine.InputSystem.Key.S)) _index = (_index + 1) % n;
                    for (int i = 0; i < n && i < 9; i++)
                        if (Down(kb, UnityEngine.InputSystem.Key.Digit1 + i)) { _index = i; enter = true; }
                    if (enter || Down(kb, UnityEngine.InputSystem.Key.Space) || Down(kb, UnityEngine.InputSystem.Key.F)) _items[_index].Run();
                    break;
                }
                case Page.Join:
                {
                    _listener?.Poll(Time.unscaledTime);
                    _master?.Query(Time.unscaledDeltaTime);
                    RefreshEntries();
                    int rooms = _entries.Count;
                    if (rooms > 0)
                    {
                        if (arrowUp) _roomIndex = (_roomIndex - 1 + rooms) % rooms;
                        if (arrowDown) _roomIndex = (_roomIndex + 1) % rooms;
                        _roomIndex = Mathf.Clamp(_roomIndex, 0, rooms - 1);
                    }
                    if (enter) TryJoin();
                    else if (back) ShowTitle();
                    break;
                }
                case Page.Notice:
                    if (enter || back || Down(kb, UnityEngine.InputSystem.Key.Space)) ShowTitle();
                    break;
                case Page.Nickname:
                    if (enter) ConfirmNickname();
                    else if (back) { _afterNickname = null; ShowTitle(); }
                    break;
                case Page.Replays:
                {
                    int n = _replays.Count;
                    if (n > 0 && arrowUp) _replayIndex = (_replayIndex - 1 + n) % n;
                    if (n > 0 && arrowDown) _replayIndex = (_replayIndex + 1) % n;
                    if (enter) { if (!string.IsNullOrWhiteSpace(_replayPath)) ImportReplay(_replayPath); else PlayReplay(_replayIndex); }
                    else if (back) ShowTitle();
                    break;
                }
                case Page.Settings:
                {
                    int n = SettingRows;
                    if (arrowUp || Down(kb, UnityEngine.InputSystem.Key.W)) _settingIndex = (_settingIndex - 1 + n) % n;
                    if (arrowDown || Down(kb, UnityEngine.InputSystem.Key.S)) _settingIndex = (_settingIndex + 1) % n;
                    bool side = Down(kb, UnityEngine.InputSystem.Key.LeftArrow) || Down(kb, UnityEngine.InputSystem.Key.RightArrow) || padLeft || padRight;
                    if (enter || side || Down(kb, UnityEngine.InputSystem.Key.Space)) ActivateSetting(_settingIndex);
                    else if (back) ShowTitle();
                    break;
                }
            }
        }

        // ------------------------------------------------------------ settings

        private const int SettingRows = 5;

        private string SettingLabel(int i)
        {
            switch (i)
            {
                case 0: return "개발자 디버그 모드";
                case 1: return "전체 화면";
                case 2: return "메인 서버 목록 사용";
                case 3: return "튜토리얼 기록 초기화";
                default: return "뒤로";
            }
        }

        private string SettingValue(int i)
        {
            switch (i)
            {
                case 0: return DevSettings.DebugMode ? "켜짐" : "꺼짐";
                case 1: return DevSettings.Fullscreen ? "켜짐" : "꺼짐";
                case 2: return DevSettings.UseMasterServer ? "켜짐" : "꺼짐";
                case 3: return TutorialController.Completed ? "완료함" : "미완료";
                default: return "";
            }
        }

        private string SettingHint(int i)
        {
            switch (i)
            {
                case 0: return "오버레이 · 히트박스/스폰 표시 · 콘텐츠 오류 목록 · F1–F7 개발자 명령";
                case 1: return "창 모드 1600×900 / 전체 화면";
                case 2: return "방을 만들면 메인 서버(" + MasterServerEndpoint.Host + ")에 등록하고, 방 참가에서 전체 서버 목록을 받아옵니다";
                case 3: return "다음 실행 때 튜토리얼 안내가 다시 표시됩니다";
                default: return "타이틀로 돌아가기 (Esc)";
            }
        }

        private void ActivateSetting(int i)
        {
            switch (i)
            {
                case 0: DevSettings.DebugMode = !DevSettings.DebugMode; break;
                case 1: DevSettings.Fullscreen = !DevSettings.Fullscreen; break;
                case 2: DevSettings.UseMasterServer = !DevSettings.UseMasterServer; break;
                case 3: TutorialController.ResetProgress(); break;
                default: ShowTitle(); break;
            }
        }

        private void TryJoin()
        {
            if (!string.IsNullOrWhiteSpace(_code))
            {
                if (RoomCode.TryParseTarget(_code, out string h, out int p)) JoinTarget(h, p);
                else _joinError = "방 코드(예: 60R0G-01Y71) 또는 IP(예: 192.168.0.12)를 입력하세요";
                return;
            }
            if (_entries.Count > 0)
            {
                JoinEntryAt(Mathf.Clamp(_roomIndex, 0, _entries.Count - 1));
                return;
            }
            _joinError = "방 목록이 비어 있습니다. 코드나 IP를 입력하세요";
        }

        private void JoinEntryAt(int i)
        {
            var e = _entries[i];
            if (e.Blocked != null) { _joinError = e.Blocked; return; }
            JoinTarget(e.Host, e.Port);
        }

        // ------------------------------------------------------------ drawing

        private void OnGUI()
        {
            var flow = Flow;
            if (flow == null || !flow.InMenu) return;
            Begin();
            Fill(new Rect(0, 0, W, H), new Color(0.07f, 0.07f, 0.09f, 0.86f));
            if (NetHostSession.Active != null) { DrawHost(NetHostSession.Active); return; }
            if (NetClientSession.Active != null) { DrawClient(NetClientSession.Active); return; }
            switch (_page)
            {
                case Page.Title: DrawTitle(); break;
                case Page.Join: DrawJoin(); break;
                case Page.Notice: DrawNotice(); break;
                case Page.Settings: DrawSettings(); break;
                case Page.Nickname: DrawNickname(); break;
                case Page.Replays: DrawReplays(); break;
            }
        }

        private void DrawNickname()
        {
            Shadowed(new Rect(0, 230, W, 80), "닉네임", Big, Palette.PlayerOne);
            Text(new Rect(0, 320, W, 34), "다른 플레이어와 리플레이 기록에 표시될 이름입니다 (최대 " + Online.PlayerProfile.MaxLength + "자)", Small, new Color(1, 1, 1, 0.7f));
            GUI.SetNextControlName("nick");
            _nickDraft = GUI.TextField(new Rect(W / 2 - 360, 380, 720, 80), _nickDraft ?? "", Online.PlayerProfile.MaxLength, Field);
            if (_focusField && Event.current.type == EventType.Repaint) { GUI.FocusControl("nick"); _focusField = false; }
            var ok = new Rect(W / 2 - 200, 500, 400, 66);
            Fill(ok, Palette.PlayerOne);
            Text(ok, "확인 (Enter)", Mid, Palette.Ink);
            if (Clicked(ok)) _deferred = ConfirmNickname;
            if (_notice != null) Text(new Rect(0, 590, W, 34), _notice, Small, new Color(1f, 0.6f, 0.5f));
            Text(new Rect(0, 650, W, 30), "이 컴퓨터: " + Online.PlayerProfile.DeviceName + " · Esc 취소", Small, new Color(1, 1, 1, 0.5f));
        }

        private void DrawReplays()
        {
            Shadowed(new Rect(0, 50, W, 80), "리플레이", Big, Palette.PlayerOne);
            Text(new Rect(0, 130, W, 30), "저장 폴더: " + Replay.ReplayStore.Folder, new GUIStyle(Small) { fontSize = 19 }, new Color(1, 1, 1, 0.6f));
            if (_replays.Count == 0)
                Text(new Rect(0, 230, W, 40), "저장된 리플레이가 없습니다. 온라인 게임(방 만들기·전용 서버)이 끝나면 자동으로 저장됩니다.", Small, new Color(1, 1, 1, 0.5f));
            int first = Mathf.Clamp(_replayIndex - 7, 0, Mathf.Max(0, _replays.Count - 8));
            for (int row = 0; row < 8 && first + row < _replays.Count; row++)
            {
                int i = first + row;
                var d = _replays[i];
                var m = d.Meta;
                bool sel = i == _replayIndex;
                var r = new Rect(W / 2 - 760, 175 + row * 62, 1520, 56);
                Fill(r, sel ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.12f));
                bool other = !string.IsNullOrEmpty(m.contentHash) && m.contentHash != Content.ContentDatabase.Hash;
                string label = m.createdLocal + "  ·  " + m.roomName + "  ·  " + m.teamSize + "대" + m.teamSize + "  ·  " + ((int)(m.duration / 60)) + "분 " + ((int)m.duration % 60) + "초  ·  " + m.PlayersLabel
                               + "  ·  기기 " + m.device.machineName + (other ? "  ·  [콘텐츠 다름]" : "");
                Text(new Rect(r.x + 20, r.y, r.width - 40, r.height), label, new GUIStyle(Small) { fontSize = 20, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip }, sel ? Palette.Ink : Color.white);
                if (Clicked(r)) { int k = i; if (_replayIndex == k) _deferred = () => PlayReplay(k); else _replayIndex = k; }
            }
            float y = 680;
            if (ReplayButton(new Rect(W / 2 - 760, y, 240, 62), "재생 (Enter)", true)) _deferred = () => PlayReplay(_replayIndex);
            if (ReplayButton(new Rect(W / 2 - 500, y, 300, 62), "불러오기 (파일 선택)", false)) _deferred = () => { var p = Replay.FileDialog.OpenReplay(); if (p != null) ImportReplay(p); else _replayMessage = "파일을 선택하지 않았습니다 (아래 칸에 경로를 직접 붙여넣어도 됩니다)"; };
            if (ReplayButton(new Rect(W / 2 - 180, y, 240, 62), "폴더 열기", false)) _deferred = () => { System.IO.Directory.CreateDirectory(Replay.ReplayStore.Folder); Application.OpenURL("file://" + Replay.ReplayStore.Folder); };
            if (ReplayButton(new Rect(W / 2 + 80, y, 200, 62), "새로고침", false)) _deferred = RefreshReplays;
            if (ReplayButton(new Rect(W / 2 + 300, y, 200, 62), "뒤로 (Esc)", false)) _deferred = ShowTitle;
            Text(new Rect(W / 2 - 760, 770, 700, 34), "외부 리플레이 파일 경로 (붙여넣고 Enter → 저장 폴더로 가져오기)", new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft, fontSize = 20 }, new Color(1, 1, 1, 0.6f));
            _replayPath = GUI.TextField(new Rect(W / 2 - 760, 808, 1260, 60), _replayPath ?? "", 400, new GUIStyle(Field) { fontSize = 22, alignment = TextAnchor.MiddleLeft });
            if (ReplayButton(new Rect(W / 2 + 520, 808, 240, 60), "가져오기", false)) _deferred = () => ImportReplay(_replayPath);
            if (_replayMessage != null) Text(new Rect(0, 890, W, 34), _replayMessage, Small, new Color(1f, 0.85f, 0.5f));
            Text(new Rect(0, 950, W, 30), "↑/↓ 선택 · Enter 재생 · 리플레이 중: Space 정지 · ←/→ 10초 · -/+ 배속 · 휠 줌 · 캐릭터 좌클릭 정보", Small, new Color(1, 1, 1, 0.55f));
        }

        private static bool ReplayButton(Rect r, string label, bool primary)
        {
            Fill(r, primary ? Palette.PlayerOne : new Color(1, 1, 1, 0.15f));
            Text(r, label, new GUIStyle(Small) { fontSize = 22 }, primary ? Palette.Ink : Color.white);
            return Clicked(r);
        }

        private void DrawTitle()
        {
            RefreshItems();
            Shadowed(new Rect(0, 90, W, 120), "METADREAM SHOOT", Title, Palette.PlayerOne);
            Text(new Rect(0, 212, W, 40), "1대1 · 2대2 카드 슈터 · ROUNDS 정적 분석 기반 재구현", Small, new Color(1, 1, 1, 0.75f));
            float step = _items.Length > 8 ? 57f : 64f;
            for (int i = 0; i < _items.Length; i++)
            {
                bool sel = i == _index;
                var r = new Rect(W / 2 - 380, 262 + i * step, 760, step - 8);
                Fill(r, sel ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.12f));
                // Label and hint get separate columns so wide fonts never overlap.
                Text(new Rect(r.x + 30, r.y, 290, r.height), (i + 1) + ".  " + _items[i].Label, new GUIStyle(Mid) { alignment = TextAnchor.MiddleLeft }, sel ? Palette.Ink : Color.white);
                string hint = i == 0 && !TutorialController.Completed ? "처음이라면 여기부터! 조작과 규칙 배우기" : _items[i].Hint;
                Text(new Rect(r.x + 330, r.y, r.width - 354, r.height), hint, new GUIStyle(Small) { alignment = TextAnchor.MiddleRight, fontSize = 22, clipping = TextClipping.Clip }, sel ? new Color(0, 0, 0, 0.55f) : new Color(1, 1, 1, 0.5f));
                if (Clicked(r)) { _index = i; int k = i; _deferred = () => _items[k].Run(); }
            }
            Text(new Rect(0, 790, W, 30), "↑/↓ 또는 W/S · Enter/Space 선택 · 숫자 1–" + Mathf.Min(9, _items.Length) + " 바로 선택 · 마우스 클릭", Small, new Color(1, 1, 1, 0.65f));
            Text(new Rect(0, 850, W, 30), "조작  " + KeyboardBinding.PlayerOne().Help, Small, Palette.PlayerOne);
            Text(new Rect(0, 890, W, 30), "게임패드: 스틱 이동/조준 · A 점프 · RT/X 사격 · LT/LB/RB 방어", Small, new Color(1, 1, 1, 0.65f));
            Text(new Rect(0, 950, W, 30), "규칙: 2포인트 = 1라운드, 5라운드 선승. 라운드를 진 팀만 카드를 고릅니다. 같은 팀 탄은 통과합니다.", Small, new Color(1, 1, 1, 0.5f));
            if (DevSettings.DebugMode)
                Text(new Rect(0, 1000, W, 30), "디버그 모드 켜짐 · 콘텐츠: 카드 " + ContentDatabase.Cards.Count + " · 맵 " + ContentDatabase.Maps.Count + " · 오류 " + ContentDatabase.ErrorCount + " · F5 다시 불러오기", Small, ContentDatabase.ErrorCount > 0 ? new Color(1f, 0.55f, 0.45f) : new Color(0.6f, 1f, 0.6f, 0.8f));
        }

        private void DrawSettings()
        {
            Shadowed(new Rect(0, 80, W, 80), "설정", Big, Palette.PlayerOne);
            for (int i = 0; i < SettingRows; i++)
            {
                bool sel = i == _settingIndex;
                var r = new Rect(W / 2 - 480, 200 + i * 92, 960, 80);
                Fill(r, sel ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.12f));
                Color ink = sel ? Palette.Ink : Color.white;
                Text(new Rect(r.x + 30, r.y + 4, 520, 42), SettingLabel(i), new GUIStyle(Mid) { alignment = TextAnchor.MiddleLeft }, ink);
                Text(new Rect(r.x + 30, r.y + 44, r.width - 60, 30), SettingHint(i), new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft, fontSize = 19, clipping = TextClipping.Clip }, sel ? new Color(0, 0, 0, 0.6f) : new Color(1, 1, 1, 0.5f));
                string v = SettingValue(i);
                if (v.Length > 0)
                {
                    bool on = v == "켜짐" || v == "완료함";
                    var pill = new Rect(r.xMax - 170, r.y + 14, 140, 34);
                    Fill(pill, on ? new Color(0.35f, 0.8f, 0.45f) : new Color(0.45f, 0.45f, 0.5f));
                    Text(pill, v, new GUIStyle(Small) { fontSize = 22 }, Color.white);
                }
                if (Clicked(r)) { _settingIndex = i; int k = i; _deferred = () => ActivateSetting(k); }
            }
            float y = 200 + SettingRows * 92 + 10;
            if (DevSettings.DebugMode)
            {
                var box = new Rect(W / 2 - 480, y, 960, 160);
                Fill(box, new Color(0, 0, 0, 0.45f));
                string ext = ContentDatabase.LoadedExternalDirectory ?? "없음 (실행 파일 옆 Content 폴더)";
                Text(new Rect(box.x + 20, box.y + 8, box.width - 40, 30), "콘텐츠: 카드 " + ContentDatabase.Cards.Count + " · 맵 " + ContentDatabase.Maps.Count + " · 오류 " + ContentDatabase.ErrorCount + " · 경고 " + ContentDatabase.WarningCount + " · 해시 " + ContentDatabase.Hash.Substring(0, Math.Min(12, ContentDatabase.Hash.Length)), new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft, fontSize = 20 }, Color.white);
                Text(new Rect(box.x + 20, box.y + 38, box.width - 40, 28), "외부 콘텐츠 폴더: " + ext, new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft, fontSize = 18, clipping = TextClipping.Clip }, new Color(1, 1, 1, 0.6f));
                int shown = 0;
                foreach (var issue in ContentDatabase.Issues)
                {
                    if (issue.Severity == ContentSeverity.Info || shown >= 3) continue;
                    Text(new Rect(box.x + 20, box.y + 68 + shown * 26, box.width - 40, 26), issue.ToString(), new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft, fontSize = 17, clipping = TextClipping.Clip }, issue.Severity == ContentSeverity.Error ? new Color(1f, 0.55f, 0.45f) : new Color(1f, 0.85f, 0.4f));
                    shown++;
                }
                if (shown == 0) Text(new Rect(box.x + 20, box.y + 68, box.width - 40, 26), "콘텐츠 오류 없음 · 게임 중 F1 패널 · F2 다음 맵 · F3 카드 지급 · F4 적 처치 · F5 리로드(타이틀) · F6 무적 · F7 슬로모션", new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft, fontSize = 18, clipping = TextClipping.Clip }, new Color(0.6f, 1f, 0.6f, 0.85f));
            }
            Text(new Rect(0, 1080 - 56, W, 30), "↑/↓ 선택 · Enter/Space/←→ 변경 · Esc 뒤로 · 버전 " + Application.version + " · 프로토콜 v" + Protocol.Version, Small, new Color(1, 1, 1, 0.55f));
        }

        private void DrawHost(NetHostSession host)
        {
            Shadowed(new Rect(0, 70, W, 80), "방 만들기", Big, Palette.PlayerOne);
            Text(new Rect(0, 160, W, 34), "방 코드", Small, new Color(1, 1, 1, 0.6f));
            Shadowed(new Rect(0, 192, W, 90), host.Code, Title, Color.white);
            Text(new Rect(0, 285, W, 34), "주소  " + host.Address + ":" + host.Port, Mid, new Color(1, 1, 1, 0.85f));
            Text(new Rect(0, 325, W, 30), "같은 네트워크의 친구가 '방 참가'에서 이 방을 고르거나 코드/주소를 입력하면 들어옵니다.", Small, new Color(1, 1, 1, 0.6f));

            for (int size = 1; size <= 2; size++)
            {
                bool sel = host.TeamSize == size;
                var r = new Rect(W / 2 - 250 + (size - 1) * 260, 380, 240, 60);
                Fill(r, sel ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.12f));
                Text(r, size + "대" + size, Mid, sel ? Palette.Ink : Color.white);
                if (Clicked(r)) { int k = size; _deferred = () => host.SetTeamSize(k); }
            }

            for (int t = 0; t < 2; t++)
            {
                float x = W / 2 + (t == 0 ? -520 : 40);
                Shadowed(new Rect(x, 470, 480, 40), Palette.TeamName(t), Mid, Palette.TeamColor(t));
                int row = 0;
                for (int i = t; i < host.SlotCount; i += 2)
                {
                    var r = new Rect(x, 520 + row * 70, 480, 60);
                    Fill(r, new Color(1, 1, 1, 0.1f));
                    Fill(new Rect(r.x, r.y, 8, r.height), Palette.SlotColor(i));
                    var kind = host.KindOf(i);
                    string label = kind == SlotKind.Host ? host.NameOf(i) + " (호스트)" : kind == SlotKind.Remote ? host.NameOf(i) + " (참가함)" : kind == SlotKind.Disconnected ? host.NameOf(i) + " (나감 · 같은 PC로 재접속 가능)" : "빈 자리 → 시작 시 CPU";
                    Text(new Rect(r.x + 24, r.y, r.width - 30, r.height), label, new GUIStyle(Mid) { alignment = TextAnchor.MiddleLeft, fontSize = 28 }, kind == SlotKind.Empty ? new Color(1, 1, 1, 0.45f) : Color.white);
                    row++;
                }
            }

            var start = new Rect(W / 2 - 330, 700, 420, 70);
            // Room settings: card rerolls
            {
                float y = 672;
                Text(new Rect(W / 2 - 520, y, 260, 46), "카드 새로고침", new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold }, Color.white);
                SettingStepper(new Rect(W / 2 - 280, y, 340, 46), "시작 시", host.Settings.rerollsAtStart, v => host.SetRerolls(v, host.Settings.rerollsPerRound));
                SettingStepper(new Rect(W / 2 + 90, y, 400, 46), "라운드마다 +", host.Settings.rerollsPerRound, v => host.SetRerolls(host.Settings.rerollsAtStart, v));
            }
            start = new Rect(W / 2 - 330, 740, 420, 70);
            Fill(start, Palette.PlayerOne);
            Text(start, "시작 (Enter)", Mid, Palette.Ink);
            if (Clicked(start)) _deferred = () => host.StartMatch();
            var close = new Rect(W / 2 + 110, 740, 220, 70);
            Fill(close, new Color(1, 1, 1, 0.15f));
            Text(close, "방 닫기 (Esc)", Small, Color.white);
            if (Clicked(close)) _deferred = () => { host.Close(); ShowTitle(); };
            Text(new Rect(0, 830, W, 30), "←/→ 1대1·2대2 전환 · 호스트는 P1 조작키로 플레이합니다", Small, new Color(1, 1, 1, 0.6f));
            Text(new Rect(0, 870, W, 30), "인터넷 너머의 친구와 하려면 공유기에서 TCP " + host.Port + " 포트 포워딩이 필요합니다. 처음 실행 시 방화벽 허용을 눌러 주세요.", Small, new Color(1, 1, 1, 0.45f));
        }

        /// <summary>"label  [−] value [+]" control; changes are deferred to Update like other buttons.</summary>
        private void SettingStepper(Rect r, string label, int value, Action<int> set)
        {
            Fill(r, new Color(1, 1, 1, 0.1f));
            Text(new Rect(r.x + 14, r.y, r.width - 170, r.height), label, new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft }, new Color(1, 1, 1, 0.8f));
            var minus = new Rect(r.xMax - 176, r.y + 4, 44, r.height - 8);
            var plus = new Rect(r.xMax - 50, r.y + 4, 44, r.height - 8);
            Fill(minus, new Color(1, 1, 1, 0.2f));
            Fill(plus, new Color(1, 1, 1, 0.2f));
            Text(minus, "−", Mid, Color.white);
            Text(plus, "+", Mid, Color.white);
            Text(new Rect(r.xMax - 132, r.y, 82, r.height), value + "회", new GUIStyle(Mid) { fontSize = 28 }, Palette.PlayerOne);
            if (Clicked(minus)) _deferred = () => set(value - 1);
            if (Clicked(plus)) _deferred = () => set(value + 1);
        }

        private void DrawClient(NetClientSession client)
        {
            if (client.State == NetClientSession.ClientState.Connecting || client.Lobby == null)
            {
                Shadowed(new Rect(0, 380, W, 80), "연결 중…", Big, Palette.PlayerTwo);
                Text(new Rect(0, 470, W, 40), client.Target, Mid, new Color(1, 1, 1, 0.8f));
                Text(new Rect(0, 560, W, 30), "Esc: 취소", Small, new Color(1, 1, 1, 0.6f));
                return;
            }
            var lobby = client.Lobby;
            Shadowed(new Rect(0, 90, W, 80), lobby.RoomName, Big, Palette.PlayerTwo);
            string kind = lobby.Dedicated ? "전용 서버" : "방 코드 " + lobby.Code;
            Text(new Rect(0, 180, W, 40), kind + " · " + lobby.TeamSize + "대" + lobby.TeamSize + " · " + lobby.PointsToWin + "포인트=1라운드, " + lobby.RoundsToWin + "라운드 선승", Mid, new Color(1, 1, 1, 0.8f));
            Text(new Rect(0, 222, W, 34), "카드 새로고침: 시작 " + lobby.RerollsAtStart + "회 · 라운드마다 +" + lobby.RerollsPerRound + "회", Small, new Color(1f, 0.85f, 0.4f));
            for (int t = 0; t < 2; t++)
            {
                float x = W / 2 + (t == 0 ? -520 : 40);
                Shadowed(new Rect(x, 280, 480, 40), Palette.TeamName(t), Mid, Palette.TeamColor(t));
                int row = 0;
                for (int i = t; i < lobby.Slots.Count; i += 2)
                {
                    var s = lobby.Slots[i];
                    var r = new Rect(x, 330 + row * 70, 480, 60);
                    bool me = i == client.MySlot;
                    Fill(r, me ? new Color(1, 1, 1, 0.25f) : new Color(1, 1, 1, 0.1f));
                    Fill(new Rect(r.x, r.y, 8, r.height), Palette.SlotColor(i));
                    string empty = lobby.FillWithCpu ? "빈 자리 → 시작 시 CPU" : "빈 자리";
                    string label = s.Kind == SlotKind.Empty ? empty : s.Kind == SlotKind.Disconnected ? s.Name + " (나감)" : s.Name + (i == lobby.LeaderSlot && lobby.Dedicated ? " ★방장" : "") + (me ? "  ← 나" : "");
                    Text(new Rect(r.x + 24, r.y, r.width - 30, r.height), label, new GUIStyle(Mid) { alignment = TextAnchor.MiddleLeft, fontSize = 28 }, s.Kind == SlotKind.Empty ? new Color(1, 1, 1, 0.45f) : Color.white);
                    row++;
                }
            }
            string status;
            if (lobby.InMatch) status = "게임에 들어가는 중…";
            else if (lobby.Countdown >= 0f) status = Mathf.CeilToInt(lobby.Countdown) + "초 후 시작";
            else if (client.IsLeader) status = lobby.FillWithCpu ? "방장입니다 · Enter: 지금 시작 · ←/→ 1대1·2대2" : "방장입니다 · ←/→ 1대1·2대2 · 방이 차면 시작할 수 있습니다";
            else status = lobby.Dedicated ? "다른 플레이어를 기다리는 중" : "호스트가 시작하기를 기다리는 중";
            Text(new Rect(0, 560, W, 40), status, Mid, Color.white);
            Text(new Rect(0, 620, W, 30), "조작: " + KeyboardBinding.PlayerOne().Help, Small, new Color(1, 1, 1, 0.65f));
            Text(new Rect(0, 660, W, 30), "Esc: 방 나가기", Small, new Color(1, 1, 1, 0.6f));
        }

        private void DrawJoin()
        {
            Shadowed(new Rect(0, 80, W, 80), "방 참가", Big, Palette.PlayerTwo);
            string masterLine;
            Color masterColor = new Color(1, 1, 1, 0.6f);
            if (_master == null) masterLine = "메인 서버 목록 꺼짐 (설정에서 켤 수 있음) · 같은 네트워크의 방만 표시";
            else if (_master.Error != null) { masterLine = "메인 서버 " + _master.Endpoint + " 오류: " + _master.Error; masterColor = new Color(1f, 0.6f, 0.5f); }
            else if (_master.RepliesReceived == 0) { masterLine = _master.Unreachable ? "메인 서버 " + _master.Endpoint + " 응답 없음 (Tailscale 연결 확인) · 같은 네트워크의 방만 표시" : "메인 서버 " + _master.Endpoint + " 에서 목록을 받는 중…"; if (_master.Unreachable) masterColor = new Color(1f, 0.75f, 0.4f); }
            else { masterLine = "메인 서버 " + _master.Endpoint + " 연결됨 · 서버 " + _master.Servers.Count + "개" + (_master.Unreachable ? " (최근 응답 없음)" : ""); masterColor = new Color(0.6f, 1f, 0.6f, 0.85f); }
            Text(new Rect(0, 176, W, 34), masterLine, Small, masterColor);
            if (_entries.Count == 0)
            {
                Text(new Rect(0, 230, W, 60), _listener != null && _listener.Error != null ? "LAN 방 검색을 시작하지 못했습니다: " + _listener.Error : "찾는 중… (누군가 '방 만들기'를 누르거나 전용 서버가 켜지면 여기에 나타납니다)", Small, new Color(1, 1, 1, 0.5f));
            }
            else
            {
                int first = Mathf.Clamp(_roomIndex - 5, 0, Mathf.Max(0, _entries.Count - 6));
                for (int row = 0; row < 6 && first + row < _entries.Count; row++)
                {
                    int i = first + row;
                    bool sel = i == _roomIndex && string.IsNullOrWhiteSpace(_code);
                    var r = new Rect(W / 2 - 560, 222 + row * 60, 1120, 54);
                    Fill(r, sel ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.12f));
                    var e = _entries[i];
                    Color ink = e.Blocked != null ? (sel ? new Color(0.4f, 0.2f, 0.2f) : new Color(1, 1, 1, 0.4f)) : sel ? Palette.Ink : Color.white;
                    Text(r, e.Label, new GUIStyle(Small) { fontSize = 23, clipping = TextClipping.Clip }, ink);
                    if (Clicked(r)) { _roomIndex = i; int k = i; _deferred = () => JoinEntryAt(k); }
                }
                if (_entries.Count > 6) Text(new Rect(0, 582, W, 24), (_roomIndex + 1) + " / " + _entries.Count, new GUIStyle(Small) { fontSize = 18 }, new Color(1, 1, 1, 0.5f));
            }
            Text(new Rect(0, 606, W, 34), "또는 방 코드 / IP 주소 입력", Small, new Color(1, 1, 1, 0.6f));
            GUI.SetNextControlName("code");
            _code = GUI.TextField(new Rect(W / 2 - 360, 645, 720, 76), _code ?? "", 40, Field);
            if (_focusField && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl("code");
                _focusField = false;
            }
            var join = new Rect(W / 2 - 200, 750, 400, 64);
            Fill(join, Palette.PlayerTwo);
            Text(join, "참가 (Enter)", Mid, Palette.Ink);
            if (Clicked(join)) _deferred = TryJoin;
            if (_joinError != null) Text(new Rect(0, 830, W, 34), _joinError, Small, new Color(1f, 0.6f, 0.5f));
            Text(new Rect(0, 880, W, 30), "↑/↓ 목록 선택 · 입력칸이 비어 있으면 선택한 방에 참가 · Esc 뒤로", Small, new Color(1, 1, 1, 0.55f));
        }

        private void DrawNotice()
        {
            Shadowed(new Rect(0, 380, W, 80), "알림", Big, Palette.PlayerOne);
            Text(new Rect(200, 480, W - 400, 120), _notice ?? "", new GUIStyle(Mid) { wordWrap = true }, Color.white);
            Text(new Rect(0, 640, W, 30), "Enter: 확인", Small, new Color(1, 1, 1, 0.6f));
        }
    }
}

