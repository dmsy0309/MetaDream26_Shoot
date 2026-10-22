using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.MapEditor
{
    public sealed partial class MapEditorApp
    {
        public enum Modal { None, Open, SaveAs, Export, Playtest, GameRun, Settings, Help, Confirm, Script }
        private Modal _modal;
        public Modal CurrentModal => _modal;
        private Vector2 _leftScroll, _rightScroll, _modalScroll, _builtinScroll;
        private float _rightHeight = 1200f;
        private string _pathField = "";
        private string _confirmText;
        private Action _confirmYes;
        private bool _exportEditorData = true;
        private string _otherContentDir = "", _zipPath = "", _jsonPath = "";
        private readonly List<KeyValuePair<string, Color>> _exportLog = new List<KeyValuePair<string, Color>>();
        private GameLauncher.Mode _runMode = GameLauncher.Mode.VersusCpu;
        private static readonly Regex IssueRef = new Regex(@"\b(solids|boxes|spawns|movers|pads|hazards|winds|areas)\[(\d+)\]");
        private static readonly Regex ScriptLineRef = new Regex(@"script line (\d+)");
        private static readonly Regex BehaviorRef = new Regex(@"^behaviors\[(\d+)\]");

        public void OpenModal(Modal m)
        {
            CommitIfNeeded(true);
            _modal = m;
            _modalScroll = Vector2.zero;
            GUIUtility.keyboardControl = 0;
            string id = GameContent.SanitizeId(Doc.Data.id);
            if (m == Modal.SaveAs) _pathField = Doc.FilePath ?? Path.Combine(DefaultDir, id + ".json");
            if (m == Modal.Open) _pathField = "";
            if (m == Modal.Export)
            {
                _exportLog.Clear();
                if (string.IsNullOrEmpty(_zipPath) || !_zipPath.Contains(id)) _zipPath = Path.Combine(DefaultDir, id + "_map_package.zip");
                if (string.IsNullOrEmpty(_jsonPath) || !_jsonPath.Contains(id)) _jsonPath = Path.Combine(DefaultDir, id + ".json");
            }
            if (m == Modal.Script) OnOpenScript();
        }

        public void CloseModal()
        {
            _modal = Modal.None;
            GUIUtility.keyboardControl = 0;
        }

        private void OpenSaveAs() => OpenModal(Modal.SaveAs);

        private void GuardUnsaved(Action then)
        {
            if (!Unsaved) { then(); return; }
            AskDiscard("저장하지 않은 변경 사항이 있습니다. 버리고 계속할까요?", then);
        }

        private void AskDiscard(string text, Action discard)
        {
            _confirmText = text;
            _confirmYes = discard;
            _modal = Modal.Confirm;
        }

        // ------------------------------------------------------------------ frame

        private void OnGUI()
        {
            GUI.depth = -10;
            EUi.Begin();
            _textFocus = GUIUtility.keyboardControl != 0;
            if (_play.Running) { DrawPlaytestOverlay(); return; }
            var e = Event.current;
            if (e.type == EventType.MouseDown && _modal == Modal.None && ViewportGui.Contains(e.mousePosition)) GUIUtility.keyboardControl = 0;
            bool enabled = _modal == Modal.None;
            GUI.enabled = enabled;
            DrawViewportLabels();
            DrawLeftPanel();
            DrawRightPanel();
            DrawToolbar();
            DrawStatusBar();
            GUI.enabled = true;
            if (_modal != Modal.None) DrawModal();
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        // ------------------------------------------------------------------ toolbar / status

        private bool Tb(ref float x, string text, float w, bool on = false, bool enabled = true, GUIStyle style = null)
        {
            bool c = EUi.Btn(new Rect(x, 8, w, 32), text, on, enabled, style);
            x += w + 6;
            return c;
        }

        private void DrawToolbar()
        {
            EUi.Fill(new Rect(0, 0, EUi.W, TopBar), EUi.PanelDark);
            EUi.Fill(new Rect(0, TopBar - 1, EUi.W, 1), EUi.Line);
            float x = 10;
            if (Tb(ref x, "새 맵", 58)) NewMap();
            if (Tb(ref x, "열기", 52)) OpenModal(Modal.Open);
            if (Tb(ref x, "저장", 52)) Save();
            if (Tb(ref x, "다른 이름으로", 96)) OpenSaveAs();
            x += 8;
            if (Tb(ref x, "되돌리기", 70, false, _undo.Count > 0 || _pendingCommit)) Undo();
            if (Tb(ref x, "다시 실행", 70, false, _redo.Count > 0)) Redo();
            x += 8;
            if (Tb(ref x, "테스트 플레이 (F5)", 132, false, true, EUi.ButtonOn)) OpenModal(Modal.Playtest);
            if (Tb(ref x, "실제 게임으로 실행", 126)) OpenModal(Modal.GameRun);
            if (Tb(ref x, "게임에 적용 · 내보내기", 152, false, true, EUi.ButtonOn)) OpenModal(Modal.Export);
            int lines = ScriptLineCount, behaviors = Doc.Data.behaviors.Length;
            if (Tb(ref x, "맵 스크립트" + (lines + behaviors > 0 ? " (" + lines + "줄 · 동작 " + behaviors + ")" : ""), lines + behaviors > 0 ? 170 : 92, false, true, ScriptErrorCount > 0 ? EUi.ButtonBad : null)) OpenModal(Modal.Script);

            float right = EUi.W - 10;
            if (EUi.Btn(new Rect(right - 58, 8, 58, 32), "도움말")) OpenModal(Modal.Help);
            if (EUi.Btn(new Rect(right - 58 - 6 - 52, 8, 52, 32), "설정")) OpenModal(Modal.Settings);
            float labelX = x + 6, labelW = right - 128 - labelX;
            if (labelW > 80)
            {
                string file = Doc.FilePath != null ? Path.GetFileName(Doc.FilePath) : "(저장 안 됨)";
                GUI.Label(new Rect(labelX, 6, labelW, 20), (Unsaved ? "● " : "") + (string.IsNullOrEmpty(Doc.Data.name) ? Doc.Data.id : Doc.Data.name), EUi.Header);
                GUI.Label(new Rect(labelX, 24, labelW, 18), Doc.Data.id + " · " + file, EUi.Tiny);
            }
        }

        private void DrawStatusBar()
        {
            var r = new Rect(0, EUi.H - StatusBar, EUi.W, StatusBar);
            EUi.Fill(r, EUi.PanelDark);
            EUi.Fill(new Rect(0, r.y, EUi.W, 1), EUi.Line);
            string cursor = _cursorInView ? "x " + _cursorWorld.x.ToString("0.00") + "   y " + _cursorWorld.y.ToString("0.00") : "";
            GUI.Label(new Rect(10, r.y, 170, StatusBar), cursor, EUi.Small);
            string info = GameRuntimeInfo.Current.Short;
            var size = EUi.Tiny.CalcSize(new GUIContent(info));
            GUI.Label(new Rect(EUi.W - size.x - 12, r.y, size.x + 4, StatusBar), info, EUi.Tiny);
            if (_toast != null && Time.unscaledTime < _toastUntil)
            {
                var old = EUi.Small.normal.textColor;
                EUi.Small.normal.textColor = _toastColor;
                GUI.Label(new Rect(186, r.y, EUi.W - size.x - 210, StatusBar), _toast, EUi.Small);
                EUi.Small.normal.textColor = old;
            }
            else GUI.Label(new Rect(186, r.y, EUi.W - size.x - 210, StatusBar), ToolHint(_tool), EUi.Tiny);
        }

        private static string ToolHint(Tool t)
        {
            switch (t)
            {
                case Tool.Select: return "클릭: 선택 (겹치면 다시 클릭해 다음 것) · 끌기: 이동 · 흰 점: 크기 · 위쪽 원: 회전 · 빈 곳 끌기/우클릭: 화면 이동 · 휠: 확대";
                case Tool.DragSelect: return "드래그 선택: 빈 곳 좌클릭 끌기 = 여러 개 선택 (→ 완전히 들어간 것, ← 닿은 것) · Shift = 추가 · 선택한 것 끌기 = 함께 이동 · 우클릭 누른 채 끌기 = 화면 이동";
                case Tool.Spawn: return "클릭한 곳에 스폰 추가 (spawns[0] 주황 팀, spawns[1] 파랑 팀) · Shift: 도구 유지 · Esc: 선택 도구";
                case Tool.Area: return "트리거 영역: 충돌 없는 사각형. 맵 스크립트의 on enter/exit/inside 와 C# 동작의 area 로 씁니다 · 끌어서 크기 지정";
                default: return "끌어서 크기 지정 또는 클릭해 기본 크기로 추가 · Shift: 도구 유지 · Esc: 선택 도구";
            }
        }

        // ------------------------------------------------------------------ left panel

        private static readonly KeyValuePair<Tool, string>[] ToolButtons =
        {
            new KeyValuePair<Tool, string>(Tool.Select, "선택  V"),
            new KeyValuePair<Tool, string>(Tool.DragSelect, "드래그 선택  M"),
            new KeyValuePair<Tool, string>(Tool.Solid, "지형  1"),
            new KeyValuePair<Tool, string>(Tool.Box, "물리 상자  2"),
            new KeyValuePair<Tool, string>(Tool.Spawn, "스폰  3"),
            new KeyValuePair<Tool, string>(Tool.Mover, "움직이는 발판  4"),
            new KeyValuePair<Tool, string>(Tool.Pad, "점프대  5"),
            new KeyValuePair<Tool, string>(Tool.Hazard, "피해 구역  6"),
            new KeyValuePair<Tool, string>(Tool.Wind, "바람 구역  7"),
            new KeyValuePair<Tool, string>(Tool.Area, "트리거 영역  8")
        };

        private void DrawLeftPanel()
        {
            var panel = new Rect(0, TopBar, LeftPanel, EUi.H - TopBar - StatusBar);
            EUi.Fill(panel, EUi.PanelColor);
            EUi.Fill(new Rect(LeftPanel - 1, TopBar, 1, panel.height), EUi.Line);
            float x = 10, w = LeftPanel - 20, y = TopBar + 8;
            GUI.Label(new Rect(x, y, w, 20), "오브젝트 추가 · 도구", EUi.Header);
            y += 24;
            float bw = (w - 6) / 2f;
            for (int i = 0; i < ToolButtons.Length; i++)
            {
                var t = ToolButtons[i];
                var r = new Rect(x + (i % 2) * (bw + 6), y + (i / 2) * 34, bw, 30);
                if (EUi.Btn(r, "", _tool == t.Key)) SetTool(t.Key);
                bool swatch = KindOf(t.Key) != ObjKind.None;
                if (swatch) EUi.Fill(new Rect(r.x + 6, r.y + 10, 10, 10), KindColor(KindOf(t.Key)));
                else if (t.Key == Tool.DragSelect) EUi.Frame(new Rect(r.x + 6, r.y + 10, 10, 10), Color.white);
                GUI.Label(new Rect(r.x + (t.Key == Tool.Select ? 8 : 20), r.y, r.width - 22, r.height), t.Value, _tool == t.Key ? EUi.Label : EUi.Small);
            }
            y += (ToolButtons.Length + 1) / 2 * 34 + 4;

            GUI.Label(new Rect(x, y, w, 20), "보기", EUi.Header);
            y += 22;
            _showGrid = EUi.Toggle(new Rect(x, y, w / 2, 20), _showGrid, "격자 (G)");
            bool snap = EUi.Toggle(new Rect(x + w / 2, y, w / 2, 20), _snapOn, "스냅");
            if (snap != _snapOn) { _snapOn = snap; SaveSettings(); }
            y += 24;
            float[] steps = { 0.1f, 0.25f, 0.5f, 1f, 2f };
            float sw = (w - 4 * 4) / steps.Length;
            for (int i = 0; i < steps.Length; i++)
                if (EUi.Btn(new Rect(x + i * (sw + 4), y, sw, 22), MapDocument.Num(steps[i]), _snapOn && Mathf.Approximately(_snap, steps[i]))) { _snap = steps[i]; _snapOn = true; SaveSettings(); }
            y += 28;
            _showLabels = EUi.Toggle(new Rect(x, y, w / 2, 20), _showLabels, "이름표");
            _animateMovers = EUi.Toggle(new Rect(x + w / 2, y, w / 2, 20), _animateMovers, "발판 움직임");
            y += 24;
            if (EUi.Btn(new Rect(x, y, w, 24), "화면 맞춤 (F)")) FitView();
            y += 32;

            var all = new List<ObjRef>(Doc.All());
            GUI.Label(new Rect(x, y, w, 20), "오브젝트 " + all.Count + "개" + (_multi.Count > 1 ? " · " + _multi.Count + "개 선택" : ""), EUi.Header);
            y += 22;
            var view = new Rect(4, y, LeftPanel - 6, panel.yMax - y - 6);
            const float rowH = 20f;
            _leftScroll = GUI.BeginScrollView(view, _leftScroll, new Rect(0, 0, view.width - 16, all.Count * rowH + 4));
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                var s = Doc.GetShape(o);
                var r = new Rect(2, i * rowH, view.width - 20, rowH - 1);
                if (r.yMax < _leftScroll.y || r.y > _leftScroll.y + view.height) continue;
                string nm = Doc.GetName(o);
                string label = MapDocument.KindName(o.Kind) + " " + o.Index + (nm.Length > 0 ? " \"" + nm + "\"" : "") + "   " + MapDocument.Num(s.x) + ", " + MapDocument.Num(s.y)
                    + (MapDocument.IsRect(o.Kind) ? "  " + MapDocument.Num(s.w) + "×" + MapDocument.Num(s.h) : "");
                if (o.Kind == ObjKind.Spawn) label = "스폰 " + o.Index + " · " + MapDocument.SpawnRole(o.Index) + "   " + MapDocument.Num(s.x) + ", " + MapDocument.Num(s.y);
                if (GUI.Button(new Rect(r.x + 14, r.y, r.width - 14, r.height), label, IsSelected(o) ? EUi.ListItemOn : EUi.ListItem))
                {
                    if (Shift) ToggleSelected(o);
                    else { Select(o); if (_tool != Tool.DragSelect) SetTool(Tool.Select); }
                }
                EUi.Fill(new Rect(r.x + 3, r.y + 6, 8, 8), o.Kind == ObjKind.Spawn ? SpawnColor(o.Index) : KindColor(o.Kind));
            }
            GUI.EndScrollView();
        }

        // ------------------------------------------------------------------ right panel

        private void DrawRightPanel()
        {
            var panel = new Rect(EUi.W - RightPanel, TopBar, RightPanel, EUi.H - TopBar - StatusBar);
            EUi.Fill(panel, EUi.PanelColor);
            EUi.Fill(new Rect(panel.x, TopBar, 1, panel.height), EUi.Line);
            _rightScroll = GUI.BeginScrollView(panel, _rightScroll, new Rect(0, 0, RightPanel - 16, _rightHeight));
            float x = 12, w = RightPanel - 16 - 22, y = 10;
            DrawMapSection(x, w, ref y);
            DrawBackgroundSection(x, w, ref y);
            DrawObjectSection(x, w, ref y);
            DrawValidationSection(x, w, ref y);
            _rightHeight = y + 16;
            GUI.EndScrollView();
        }

        private void Section(string title, float x, float w, ref float y)
        {
            EUi.Divider(x, y, w);
            y += 8;
            GUI.Label(new Rect(x, y, w, 20), title, EUi.Header);
            y += 24;
        }

        private void Note(string text, GUIStyle style, float x, float w, ref float y)
        {
            float h = style.CalcHeight(new GUIContent(text), w);
            GUI.Label(new Rect(x, y, w, h), text, style);
            y += h + 4;
        }

        private void DrawMapSection(float x, float w, ref float y)
        {
            GUI.Label(new Rect(x, y, w, 20), "맵 정보", EUi.Header);
            y += 24;
            string id = EUi.TextRow("map_id", new Rect(x, y, w, 22), "id", Doc.Data.id);
            if (id != Doc.Data.id) { Doc.Data.id = id; Changed(); }
            y += 26;
            if (!GameContent.IsValidId(Doc.Data.id))
            {
                string fixedId = GameContent.SanitizeId(Doc.Data.id);
                GUI.Label(new Rect(x + 70, y, w - 140, 20), "소문자 영문·숫자·_ 만 → " + fixedId, EUi.Warn);
                if (EUi.Btn(new Rect(x + w - 64, y, 64, 20), "고치기")) { Doc.Data.id = fixedId; Changed(); GUIUtility.keyboardControl = 0; }
                y += 24;
            }
            else if (GameContent.IsBuiltinId(Doc.Data.id))
            {
                GUI.Label(new Rect(x + 70, y, w - 70, 18), "내장 맵과 같은 id: 적용하면 내장 맵을 덮어씀", EUi.Tiny);
                y += 20;
            }
            string name = EUi.TextRow("map_name", new Rect(x, y, w, 22), "이름", Doc.Data.name);
            if (name != Doc.Data.name) { Doc.Data.name = name; Changed(); }
            y += 26;
            string origin = EUi.TextRow("map_origin", new Rect(x, y, w, 22), "출처 메모", Doc.Data.origin);
            if (origin != Doc.Data.origin) { Doc.Data.origin = origin; Changed(); }
            y += 28;
            bool tut = EUi.Toggle(new Rect(x, y, w, 20), Doc.Data.tutorialOnly, "튜토리얼 전용 (일반 맵 순환에서 제외)");
            if (tut != Doc.Data.tutorialOnly) { Doc.Data.tutorialOnly = tut; Changed(); }
            y += 24;
            int dynamicCount = Doc.Count(ObjKind.Box) + Doc.Count(ObjKind.Mover);
            GUI.Label(new Rect(x, y, w, 18), "상자 + 발판 " + dynamicCount + " / 120 (온라인 동기화 한도)", dynamicCount > 120 ? EUi.Warn : EUi.Tiny);
            y += 26;
            DrawTeamSpawnSection(x, w, ref y);
        }

        private void DrawTeamSpawnSection(float x, float w, ref float y)
        {
            Section("2대2 스폰", x, w, ref y);
            bool on = Doc.TeamSpawnsEnabled;
            bool native = MapDocument.GameNativeTeamSpawns;
            bool next = EUi.Toggle(new Rect(x, y, w, 20), on, "팀원 개별 스폰 사용 (주황 2 · 파랑 2)");
            if (next != on) { SetTeamSpawns(next); Toast(next ? "2대2 팀원 개별 스폰을 켰습니다 (주황 2·파랑 2 스폰을 원하는 곳으로 옮기세요)" : "2대2 팀원 개별 스폰을 껐습니다 (게임 기본: 팀 스폰 옆 2.6)", EUi.TextDim); }
            y += 24;
            if (on)
            {
                for (int i = 2; i < 4; i++)
                {
                    bool exists = i < Doc.Count(ObjKind.Spawn);
                    GUI.Label(new Rect(x + 4, y, w - 90, 20), (exists ? "● " : "○ ") + MapDocument.SpawnRole(i) + (exists ? "  " + MapDocument.Num(Doc.Data.spawns[i].x) + ", " + MapDocument.Num(Doc.Data.spawns[i].y) : " — 없음 (게임 기본 위치)"), EUi.Small);
                    if (exists && EUi.Btn(new Rect(x + w - 80, y, 80, 20), "선택")) { Select(new ObjRef(ObjKind.Spawn, i)); SetTool(Tool.Select); }
                    y += 22;
                }
                if (Doc.Count(ObjKind.Spawn) < 4 && EUi.Btn(new Rect(x, y, w, 22), "빠진 팀원 스폰 추가")) { SetTeamSpawns(true); }
                if (Doc.Count(ObjKind.Spawn) < 4) y += 26;
                if (native) Note("게임이 팀원 스폰을 직접 지원합니다: '준비' 순간부터 플레이어 2·3이 이 위치에 섭니다 (스크립트 블록 없음).", EUi.Ok, x, w, ref y);
                else Note("현재 게임 런타임은 팀의 두 번째 플레이어를 팀 스폰 옆 2.6에 세웁니다. 그래서 에디터가 맵 스크립트에 '팀원 스폰' 블록을 자동으로 넣어, 대결이 시작되는 순간(\"싸워라!\") 플레이어 2·3을 이 위치로 옮깁니다. '준비' 0.55초 동안은 기본 위치에 보입니다 — 준비 상태부터 반영하려면 게임 수정이 필요합니다 (Docs/GAME_PATCH_TEAM_SPAWNS.md).", EUi.Area, x, w, ref y);
            }
            else Note("끄면 게임 기본 배치(팀원은 팀 스폰 옆 2.6, 흐린 원)를 씁니다. 스폰 도구로 세 번째·네 번째 스폰을 놓으면 자동으로 켜집니다.", EUi.Area, x, w, ref y);
            y += 4;
        }

        private static readonly KeyValuePair<string, string[]>[] BackgroundPresets =
        {
            new KeyValuePair<string, string[]>("밤하늘", new[] { "#0B1630", "#05070D" }),
            new KeyValuePair<string, string[]>("황혼", new[] { "#3A1E3F", "#120A16" }),
            new KeyValuePair<string, string[]>("용암", new[] { "#2A0D08", "#0E0503" }),
            new KeyValuePair<string, string[]>("숲", new[] { "#0E2418", "#050C08" }),
            new KeyValuePair<string, string[]>("심해", new[] { "#06263A", "#020910" })
        };

        private void DrawBackgroundSection(float x, float w, ref float y)
        {
            Section("배경", x, w, ref y);
            var bg = Doc.Background;
            bool en = EUi.Toggle(new Rect(x, y, w, 20), bg.enabled, "이 맵 전용 배경 사용");
            if (en != bg.enabled)
            {
                bg.enabled = en;
                if (en && bg.top == bg.bottom && MapBackground.ParseHex(bg.top, Color.black) == MapBackground.ParseHex("#10121A", Color.black))
                {
                    bg.top = MapBackground.ToHex(MapBackground.GameDefault * 1.6f);
                    bg.bottom = MapBackground.ToHex(MapBackground.GameDefault);
                }
                Changed();
            }
            y += 24;
            if (bg.enabled)
            {
                Color top = EUi.ColorRows("bg_top", ref y, x, w, "위쪽 색", bg.TopColor);
                string topHex = MapBackground.ToHex(top);
                if (topHex != MapBackground.ToHex(bg.TopColor)) { bg.top = topHex; Changed(); }
                Color bottom = EUi.ColorRows("bg_bottom", ref y, x, w, "아래쪽 색", bg.BottomColor);
                string bottomHex = MapBackground.ToHex(bottom);
                if (bottomHex != MapBackground.ToHex(bg.BottomColor)) { bg.bottom = bottomHex; Changed(); }
                float pw = (w - 4 * (BackgroundPresets.Length)) / (BackgroundPresets.Length + 1);
                for (int i = 0; i < BackgroundPresets.Length; i++)
                    if (EUi.Btn(new Rect(x + i * (pw + 4), y, pw, 22), BackgroundPresets[i].Key)) { bg.top = BackgroundPresets[i].Value[0]; bg.bottom = BackgroundPresets[i].Value[1]; Changed(); }
                if (EUi.Btn(new Rect(x + BackgroundPresets.Length * (pw + 4), y, pw, 22), "단색")) { bg.bottom = bg.top; Changed(); }
                y += 28;
            }
            else GUI.Label(new Rect(x, y, w, 18), "게임 기본 배경 " + MapBackground.ToHex(MapBackground.GameDefault), EUi.Tiny);
            if (!bg.enabled) y += 20;
            Note("현재 게임 빌드는 모든 맵에 같은 배경색(Palette.Background)을 쓰고 맵 파일의 배경을 읽지 않습니다. 이 배경은 에디터 미리보기와 맵 파일의 \"background\" 항목에만 저장되고, 게임 화면·테스트 플레이에는 아직 나타나지 않습니다.", EUi.Warn, x, w, ref y);
            y += 4;
        }

        private void DrawObjectSection(float x, float w, ref float y)
        {
            if (_multi.Count > 1)
            {
                Section(_multi.Count + "개 선택됨", x, w, ref y);
                var counts = new Dictionary<ObjKind, int>();
                foreach (var o in _multi) counts[o.Kind] = counts.TryGetValue(o.Kind, out int n) ? n + 1 : 1;
                var parts = new List<string>();
                foreach (var kv in counts) parts.Add(MapDocument.KindName(kv.Key) + " " + kv.Value);
                Note(string.Join(" · ", parts), EUi.Area, x, w, ref y);
                Note("맵에서 선택한 것 중 하나를 끌면 함께 움직입니다. 화살표 = 함께 이동, [ ] = 각자 회전, Delete = 삭제, Ctrl/⌘+D = 복제, Ctrl/⌘+C·V = 복사·붙여넣기. Shift+클릭으로 추가·제외.", EUi.Area, x, w, ref y);
                float hb = (w - 12) / 3f;
                if (EUi.Btn(new Rect(x, y, hb, 26), "복제")) DuplicateSelection();
                if (EUi.Btn(new Rect(x + hb + 6, y, hb, 26), "선택 해제")) Select(ObjRef.None);
                if (EUi.Btn(new Rect(x + 2 * (hb + 6), y, hb, 26), "모두 삭제", false, true, EUi.ButtonBad)) DeleteSelection();
                y += 34;
                return;
            }
            if (!Doc.Exists(_sel))
            {
                Section("선택한 오브젝트", x, w, ref y);
                Note("맵에서 오브젝트를 클릭하거나 왼쪽 목록에서 고르세요. 왼쪽 도구(1~7)로 새 오브젝트를 추가합니다.", EUi.Area, x, w, ref y);
                return;
            }
            Section(MapDocument.KindName(_sel.Kind) + "  " + MapDocument.JsonList(_sel.Kind) + "[" + _sel.Index + "]", x, w, ref y);
            string k = "o_" + _sel.Kind + "_" + _sel.Index + "_";
            if (MapDocument.CanBeNamed(_sel.Kind))
            {
                string oldName = Doc.GetName(_sel);
                string newName = EUi.TextRow(k + "name", new Rect(x, y, w, 22), "이름", oldName, 62);
                if (newName != oldName) { Doc.SetName(_sel, newName.Trim()); Changed(); }
                y += 24;
                string nameProblem = NameProblem(Doc.GetName(_sel));
                GUI.Label(new Rect(x + 62, y, w - 62, 16), nameProblem ?? (Doc.GetName(_sel).Length > 0 ? "맵 스크립트·C# 동작에서 이 이름으로 다룹니다" : "이름을 붙이면 맵 스크립트에서 다룰 수 있습니다 (선택)"), nameProblem != null ? EUi.Warn : EUi.Tiny);
                y += 20;
            }
            var s = Doc.GetShape(_sel);
            var ns = s;
            float half = (w - 8) / 2f;
            ns.x = EUi.Row(k + "x", new Rect(x, y, half, 22), "x (중심)", s.x, 62);
            ns.y = EUi.Row(k + "y", new Rect(x + half + 8, y, half, 22), "y (중심)", s.y, 62);
            y += 26;
            if (MapDocument.IsRect(_sel.Kind))
            {
                ns.w = EUi.Row(k + "w", new Rect(x, y, half, 22), "너비 w", s.w, 62);
                ns.h = EUi.Row(k + "h", new Rect(x + half + 8, y, half, 22), "높이 h", s.h, 62);
                y += 26;
                ns.r = EUi.Row(k + "r", new Rect(x, y, half, 22), "회전 r°", s.r, 62);
                if (EUi.Btn(new Rect(x + half + 8, y, half / 2 - 2, 22), "-15°")) ns.r = Mathf.Repeat(s.r - 15f + 180f, 360f) - 180f;
                if (EUi.Btn(new Rect(x + half + 8 + half / 2 + 2, y, half / 2 - 2, 22), "+15°")) ns.r = Mathf.Repeat(s.r + 15f + 180f, 360f) - 180f;
                y += 26;
                GUI.Label(new Rect(x, y, w, 18), "윗면 y " + MapDocument.Num((float)Math.Round(s.y + s.h / 2f, 3)) + " · 아랫면 y " + MapDocument.Num((float)Math.Round(s.y - s.h / 2f, 3)) + (s.r != 0f ? " (회전 전)" : ""), EUi.Tiny);
                y += 20;
            }
            if (ns.x != s.x || ns.y != s.y || ns.w != s.w || ns.h != s.h || ns.r != s.r) { Doc.SetShape(_sel, ns); Changed(); }

            switch (_sel.Kind)
            {
                case ObjKind.Spawn:
                {
                    int si = _sel.Index;
                    GUI.Label(new Rect(x, y, 62, 22), "역할", EUi.Small);
                    float rw = (w - 62 - 12) / 4f;
                    for (int role = 0; role < 4; role++)
                    {
                        bool can = role < Doc.Count(ObjKind.Spawn);
                        if (EUi.Btn(new Rect(x + 62 + role * (rw + 4), y, rw, 22), MapDocument.SpawnRole(role).Replace(" (2대2)", ""), si == role, can) && role != si)
                        {
                            Doc.SwapSpawns(si, role);
                            Select(new ObjRef(ObjKind.Spawn, role));
                            Changed();
                        }
                    }
                    y += 28;
                    bool team = Doc.TeamSpawnsEnabled;
                    string text = si == 0 ? "spawns[0] = 주황 팀 스폰 (플레이어 1). " + (team && Doc.Count(ObjKind.Spawn) > 2 ? "2대2 팀원은 '주황 2' 스폰에 섭니다." : "2대2 팀원은 중앙 쪽 2.6 옆(흐린 원)에 섭니다.")
                        : si == 1 ? "spawns[1] = 파랑 팀 스폰 (플레이어 2). " + (team && Doc.Count(ObjKind.Spawn) > 3 ? "2대2 팀원은 '파랑 2' 스폰에 섭니다." : "2대2 팀원은 중앙 쪽 2.6 옆(흐린 원)에 섭니다.")
                        : si < 4 ? MapDocument.SpawnRole(si) + ": 2대2에서 " + (si == 2 ? "주황" : "파랑") + " 팀 두 번째 플레이어(플레이어 " + (si + 1) + ")의 시작 위치. " + (!team ? "지금은 꺼져 있어 쓰이지 않습니다 → 위 '2대2 스폰'에서 켜세요." : MapDocument.GameNativeTeamSpawns ? "게임이 '준비' 순간부터 이 위치에 세웁니다." : "자동 생성 스크립트로 \"싸워라!\" 순간 적용됩니다.")
                        : "팀은 2개, 한 팀 최대 2명이라 spawns[4] 이후는 쓰이지 않습니다.";
                    Note(text + " 바닥 윗면보다 2~5 위에 두세요.", EUi.Area, x, w, ref y);
                    break;
                }
                case ObjKind.Box:
                    float mass = Mathf.Clamp(0.18f * s.w * s.h, 0.25f, 6f);
                    Note("게임 MapRoot 값: 질량 " + mass.ToString("0.##") + " (= 0.18 × 넓이, 0.25~6), 중력 배율 3, 마찰 0.25 · 탄성 0.05. 플레이어와 탄에 밀립니다.", EUi.Area, x, w, ref y);
                    break;
                case ObjKind.Pad:
                {
                    var z = Doc.ZoneAt(_sel);
                    float p = EUi.Row(k + "power", new Rect(x, y, w, 22), "발사 속도", z.power, 90);
                    if (p != z.power) { z.power = p; Changed(); }
                    y += 26;
                    Note("닿으면 발판의 위쪽(회전 방향)으로 이 속도까지 튕겨 올립니다 (0.35초 재사용 대기).", EUi.Area, x, w, ref y);
                    break;
                }
                case ObjKind.Hazard:
                {
                    var z = Doc.ZoneAt(_sel);
                    float d = EUi.Row(k + "dps", new Rect(x, y, w, 22), "초당 피해", z.dps, 90);
                    if (d != z.dps) { z.dps = d; Changed(); }
                    y += 26;
                    Note("용암·가시: 안에 있는 동안 계속 피해 (방어 불가). 기본 체력 100.", EUi.Area, x, w, ref y);
                    break;
                }
                case ObjKind.Wind:
                {
                    var z = Doc.ZoneAt(_sel);
                    float fx = EUi.Row(k + "fx", new Rect(x, y, half, 22), "가속 fx", z.fx, 62);
                    float fy = EUi.Row(k + "fy", new Rect(x + half + 8, y, half, 22), "가속 fy", z.fy, 62);
                    if (fx != z.fx || fy != z.fy) { z.fx = fx; z.fy = fy; Changed(); }
                    y += 26;
                    float bw3 = (w - 8) / 3f;
                    if (EUi.Btn(new Rect(x, y, bw3, 22), "상승 기류")) { z.fx = 0f; z.fy = 26f; Changed(); }
                    if (EUi.Btn(new Rect(x + bw3 + 4, y, bw3, 22), "← 바람")) { z.fx = -20f; z.fy = 0f; Changed(); }
                    if (EUi.Btn(new Rect(x + 2 * (bw3 + 4), y, bw3, 22), "바람 →")) { z.fx = 20f; z.fy = 0f; Changed(); }
                    y += 28;
                    Note("구역 안의 플레이어에게 (fx, fy) 가속도를 줍니다 (월드 방향, 회전과 무관).", EUi.Area, x, w, ref y);
                    break;
                }
                case ObjKind.Mover:
                    DrawMoverFields(k, x, w, ref y);
                    break;
                case ObjKind.Area:
                {
                    var a = Doc.AreaAt(_sel);
                    bool show = EUi.Toggle(new Rect(x, y, w, 20), a.show, "게임에서 보이기 (show) — 숨겨도 트리거는 동작");
                    if (show != a.show) { a.show = show; Changed(); }
                    y += 24;
                    var c = ColorUtility.TryParseHtmlString(a.color ?? "", out var parsed) ? parsed : new Color(1f, 1f, 1f, 0.15f);
                    EUi.Fill(new Rect(x, y + 2, 26, 18), c);
                    EUi.Frame(new Rect(x, y + 2, 26, 18), new Color(1, 1, 1, 0.3f));
                    string col = EUi.TextRow(k + "color", new Rect(x + 32, y, w - 32, 22), "색 #RGBA", a.color, 62);
                    if (col != a.color) { a.color = col.Trim(); Changed(); }
                    y += 26;
                    string[] presets = { "#40ff9060", "#ffd04060", "#ff505060", "#60a0ff60", "#ffffff26" };
                    float pw = (w - 4 * (presets.Length - 1)) / presets.Length;
                    for (int i = 0; i < presets.Length; i++)
                    {
                        var pr = new Rect(x + i * (pw + 4), y, pw, 18);
                        ColorUtility.TryParseHtmlString(presets[i], out var pc);
                        EUi.Fill(pr, new Color(pc.r, pc.g, pc.b, 0.85f));
                        if (GUI.Button(pr, GUIContent.none, GUIStyle.none)) { a.color = presets[i]; Changed(); }
                    }
                    y += 24;
                    Note("충돌 없는 사각형입니다. 맵 스크립트의 on enter / on exit / on inside 와 inside(player, 이름), C# 동작의 area 에서 이름으로 씁니다.", EUi.Area, x, w, ref y);
                    break;
                }
            }
            float bw = (w - 6) / 2f;
            if (EUi.Btn(new Rect(x, y, bw, 26), "복제 (Ctrl/⌘+D)")) DuplicateSelection();
            if (EUi.Btn(new Rect(x + bw + 6, y, bw, 26), "삭제 (Del)", false, true, EUi.ButtonBad)) DeleteSelection();
            y += 34;
        }

        private void DrawMoverFields(string k, float x, float w, ref float y)
        {
            var m = Doc.MoverAt(_sel);
            float half = (w - 8) / 2f;
            float speed = EUi.Row(k + "speed", new Rect(x, y, half, 22), "속도", m.speed, 50);
            float wait = EUi.Row(k + "wait", new Rect(x + half + 8, y, half, 22), "대기(초)", m.wait, 62);
            if (speed != m.speed || wait != m.wait) { m.speed = speed; m.wait = wait; Changed(); }
            y += 26;
            var preview = new MapMover { Path = Array.ConvertAll(m.path ?? new PointData[0], p => p != null ? new Vector2(p.x, p.y) : Vector2.zero), Speed = m.speed, Wait = m.wait };
            GUI.Label(new Rect(x, y, w, 18), "경로 " + m.path.Length + "점 · 한 바퀴 " + preview.CycleTime.ToString("0.0") + "초 (0 → 1 → … → 0 반복)", EUi.Tiny);
            y += 22;
            int remove = -1;
            for (int i = 0; i < m.path.Length; i++)
            {
                var p = m.path[i];
                if (p == null) continue;
                GUI.Label(new Rect(x, y, 34, 22), "점 " + i, EUi.Small);
                float fw = (w - 34 - 30 - 8) / 2f;
                float px = EUi.Row(k + "p" + i + "x", new Rect(x + 34, y, fw, 22), "x", p.x, 14);
                float py = EUi.Row(k + "p" + i + "y", new Rect(x + 34 + fw + 4, y, fw, 22), "y", p.y, 14);
                if (px != p.x || py != p.y) { p.x = px; p.y = py; Changed(); }
                if (EUi.Btn(new Rect(x + w - 26, y, 26, 22), "×", false, m.path.Length > 2)) remove = i;
                y += 25;
            }
            if (remove >= 0)
            {
                var list = new List<PointData>(m.path);
                list.RemoveAt(remove);
                m.path = list.ToArray();
                Changed();
                GUIUtility.keyboardControl = 0;
            }
            if (EUi.Btn(new Rect(x, y, w, 24), "+ 경로 점 추가"))
            {
                var last = m.path.Length > 0 && m.path[m.path.Length - 1] != null ? m.path[m.path.Length - 1] : new PointData();
                var list = new List<PointData>(m.path) { new PointData { x = last.x + 4f, y = last.y } };
                m.path = list.ToArray();
                Changed();
            }
            y += 30;
            Note("경로 점은 시작 위치(x, y)에서의 오프셋입니다. 맵에서 흰 점을 끌어 옮길 수 있습니다. 플레이어를 태우고 움직이며 온라인에서도 위치가 맞춰집니다.", EUi.Area, x, w, ref y);
        }

        private void DrawValidationSection(float x, float w, ref float y)
        {
            Section("검증 (게임 로더 ContentDatabase.ParseMap)", x, w, ref y);
            int errors = 0, warnings = 0;
            foreach (var i in _issues) { if (i.Severity == ContentSeverity.Error) errors++; else if (i.Severity == ContentSeverity.Warning) warnings++; }
            if (errors == 0) Note("게임이 이 맵을 불러옵니다" + (warnings > 0 ? " · 경고 " + warnings + "개" : " · 문제 없음"), EUi.Ok, x, w, ref y);
            else Note("오류 " + errors + "개: 게임이 이 맵을 불러오지 않습니다 (테스트 플레이·적용 불가)", EUi.Error, x, w, ref y);
            foreach (var issue in _issues)
            {
                if (issue.Severity == ContentSeverity.Info) continue;
                var style = issue.Severity == ContentSeverity.Error ? EUi.Error : EUi.Warn;
                string text = (issue.Severity == ContentSeverity.Error ? "오류 · " : "경고 · ") + issue.Message;
                float h = style.CalcHeight(new GUIContent(text), w - 8);
                var r = new Rect(x, y, w, h + 4);
                EUi.Fill(r, new Color(1f, 1f, 1f, 0.03f));
                GUI.Label(new Rect(x + 4, y + 2, w - 8, h), text, style);
                var match = IssueRef.Match(issue.Message);
                var scriptMatch = ScriptLineRef.Match(issue.Message);
                var behaviorMatch = BehaviorRef.Match(issue.Message);
                if (scriptMatch.Success || behaviorMatch.Success)
                {
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    {
                        OpenModal(Modal.Script);
                        if (scriptMatch.Success) { _scriptTab = 0; JumpToScriptLine(int.Parse(scriptMatch.Groups[1].Value)); }
                        else _scriptTab = 1;
                    }
                }
                else if (match.Success && GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    var o = new ObjRef(MapDocument.KindFromJsonList(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
                    if (Doc.Exists(o)) { Select(o); SetTool(Tool.Select); }
                }
                y += h + 8;
            }
        }

        // ------------------------------------------------------------------ viewport labels

        private void DrawViewportLabels()
        {
            var vp = ViewportGui;
            GUI.BeginClip(vp);
            Vector2 L(Vector2 world) => WorldToGui(world) - vp.position;
            if (_showLabels)
            {
                for (int i = 0; i < Doc.Count(ObjKind.Spawn); i++)
                {
                    var p = Doc.Data.spawns[i];
                    if (p == null) continue;
                    Vector2 g = L(new Vector2(p.x, p.y + 0.62f));
                    string t = MapDocument.SpawnRole(i) + (i >= 2 && i < 4 && !Doc.TeamSpawnsEnabled ? " · 꺼짐" : "");
                    Shadowed(new Rect(g.x - 70, g.y - 20, 140, 18), t, EUi.Center, i >= 2 && i < 4 && !Doc.TeamSpawnsEnabled ? EUi.TextDim : SpawnColor(i));
                }
                foreach (var o in Doc.All())
                {
                    if (o.Kind == ObjKind.Spawn) continue;
                    string nm = Doc.GetName(o);
                    if (o.Kind == ObjKind.Solid && nm.Length == 0) continue;
                    var s = Doc.GetShape(o);
                    Vector2 g = L(s.Center);
                    string t = nm.Length > 0 ? "\"" + nm + "\"" : MapDocument.KindName(o.Kind) + " " + o.Index;
                    if (o.Kind == ObjKind.Hazard) t += " · " + MapDocument.Num(Doc.ZoneAt(o).dps) + "/초";
                    if (o.Kind == ObjKind.Pad) t += " · " + MapDocument.Num(Doc.ZoneAt(o).power);
                    Shadowed(new Rect(g.x - 80, g.y - 9, 160, 18), t, EUi.Center, nm.Length > 0 ? new Color(0.75f, 1f, 0.8f, 0.95f) : new Color(1f, 1f, 1f, 0.8f));
                }
            }
            if (Doc.Exists(_sel))
            {
                var s = Doc.GetShape(_sel);
                if (MapDocument.IsRect(_sel.Kind))
                {
                    float lowest = float.MaxValue;
                    foreach (var c in DrawLayer.Corners(s.Center, s.Size, s.r)) lowest = Mathf.Min(lowest, c.y);
                    Vector2 g = L(new Vector2(s.x, lowest));
                    string t = MapDocument.Num(s.w) + " × " + MapDocument.Num(s.h) + (s.r != 0f ? " · " + MapDocument.Num(s.r) + "°" : "") + "  (" + MapDocument.Num(s.x) + ", " + MapDocument.Num(s.y) + ")";
                    Shadowed(new Rect(g.x - 120, g.y + 6, 240, 18), t, EUi.Center, new Color(0.75f, 0.85f, 1f));
                }
                if (_sel.Kind == ObjKind.Mover)
                {
                    var m = Doc.MoverAt(_sel);
                    for (int i = 0; i < m.path.Length; i++)
                    {
                        if (m.path[i] == null) continue;
                        Vector2 g = L(new Vector2(m.x + m.path[i].x, m.y + m.path[i].y));
                        Shadowed(new Rect(g.x + 8, g.y - 20, 40, 18), i.ToString(), EUi.Small, Color.white);
                    }
                }
            }
            if (_drag == DragMode.Create)
            {
                Vector2 p1 = Snap(_cursorWorld);
                Vector2 g = L(p1);
                string t = MapDocument.Num(Mathf.Abs(p1.x - _createStart.x)) + " × " + MapDocument.Num(Mathf.Abs(p1.y - _createStart.y));
                Shadowed(new Rect(g.x + 12, g.y + 8, 160, 18), t, EUi.Small, Color.white);
            }
            GUI.EndClip();
        }

        // ------------------------------------------------------------------ play-test overlay

        private void DrawPlaytestOverlay()
        {
            float w = EUi.W, h = EUi.H;
            EUi.Fill(new Rect(0, 0, w, 40), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(14, 0, w - 520, 40), "테스트 플레이 · " + Playtest.KindName(_play.Mode, _play.Against) + " · " + _play.MapName, EUi.Header);
            float x = w - 10;
            x -= 200;
            if (EUi.Btn(new Rect(x, 6, 200, 28), "에디터로 돌아가기 (Esc)", false, true, EUi.ButtonOn)) _play.Stop();
            x -= 136;
            if (EUi.Btn(new Rect(x, 6, 130, 28), "슬로모션 0.25× (T)", _play.SlowMotion)) _play.ToggleSlowMotion();
            if (_play.Flow != null && _play.Flow.IsSandbox)
            {
                x -= 106;
                if (EUi.Btn(new Rect(x, 6, 100, 28), "다시 스폰 (R)")) _play.RespawnAll();
            }
            var map = _play.Flow != null ? _play.Flow.CurrentMap : null;
            string status = map != null ? map.ScriptStatus() : null;
            if (!string.IsNullOrEmpty(status))
            {
                EUi.Fill(new Rect(0, 40, w, 22), new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(new Rect(14, 40, w - 28, 22), "맵 스크립트 (게임 해석기): " + status, status.Contains("STOPPED") ? EUi.Warn : EUi.Small);
            }
            EUi.Fill(new Rect(0, h - 38, w, 38), EUi.PanelDark);
            string help = _play.Mode == Playtest.Kind.CpuDemo || _play.Mode == Playtest.Kind.CpuDemoTeams ? "CPU끼리 대전 중 (관전)" : KeyboardBinding.PlayerOne().Help;
            GUI.Label(new Rect(14, h - 36, w - 28, 36), help + "   ·   게임 코드(Metadream.Shoot: GameFlow · Fighter · MapRoot · Projectile)로 실행 중", EUi.Small);
        }
    }
}
