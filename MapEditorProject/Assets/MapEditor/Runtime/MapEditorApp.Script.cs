using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps.Scripting;

namespace Metadream.MapEditor
{
    /// <summary>
    /// Map script ("script") and C# map behavior ("behaviors") editing. Checking uses the game's own parser
    /// (MdsProgram.Parse) and behavior registry; scripts run only in play tests and the real game, through the
    /// game's sandboxed interpreter — the editor never executes them.
    /// </summary>
    public sealed partial class MapEditorApp
    {
        private int _scriptTab;
        private string _scriptText = "";
        private Vector2 _codeScroll, _refScroll, _behaviorScroll;
        private int _caret = -1, _pendingCaret = -1;
        private float _behaviorHeight = 400f;
        private string _parsedKey;
        private MdsProgram _parsed;
        private readonly List<MdsError> _scriptErrors = new List<MdsError>();
        private float _codeWidth = 600f;

        private static readonly HashSet<string> ReservedWords = new HashSet<string>
        {
            "on", "every", "after", "var", "if", "elif", "else", "end", "repeat", "foreach", "stop", "and", "or", "not",
            "true", "false", "none", "player", "time", "dt", "event_x", "event_y", "start", "tick", "enter", "exit", "inside", "death", "shot"
        };

        /// <summary>Korean one-liners for the reference list (the signatures themselves come from the game's MdsLibrary).</summary>
        private static readonly Dictionary<string, string> CommandKo = new Dictionary<string, string>
        {
            { "move", "위치로 이동 (초 = 부드럽게)" }, { "move_by", "만큼 이동" }, { "rotate", "회전 (반시계 +)" }, { "rotate_by", "만큼 회전" },
            { "resize", "크기 변경" }, { "show", "보이기 (충돌·효과 켜짐)" }, { "hide", "숨기기 (충돌·효과 꺼짐)" }, { "toggle", "보이기/숨기기 전환" },
            { "color", "색 바꾸기" }, { "reset_color", "원래 색" }, { "damage", "피해 (방어 불가)" }, { "heal", "회복" }, { "push", "속도 더하기" },
            { "teleport", "순간이동" }, { "kill", "처치" }, { "slow", "이동 속도 배율" }, { "poison", "지속 피해" }, { "explode", "폭발" },
            { "shockwave", "밀어내기" }, { "shoot", "주인 없는 탄 발사 (맵당 60발)" }, { "effect", "시각 효과" }, { "sound", "효과음" },
            { "banner", "화면 가운데 큰 글씨" }, { "hud", "점수 아래 안내 줄" }, { "log", "디버그 로그" },
            { "random", "a~b 실수" }, { "randint", "a~b 정수" }, { "chance", "확률로 true" }, { "players", "살아 있는 인원" }, { "alive", "팀의 생존 인원" },
            { "nearest", "가장 가까운 플레이어" }, { "random_player", "무작위 플레이어" }, { "inside", "영역 안인가" }, { "text", "숫자 → 글자" },
            { "dist", "두 점 거리" }, { "clamp", "범위로 자르기" }, { "atan2", "각도(도)" }
        };

        private static readonly KeyValuePair<string, string>[] Examples =
        {
            new KeyValuePair<string, string>("차오르는 용암 (이름 lava 필요)",
                "var level = 0\nevery 10\n  level = level + 1\n  move_by(lava, 0, 1.5, 2)\n  banner(\"용암이 차오릅니다! \" + level + \"단계\", 1.5, \"#ff7040\")\nend"),
            new KeyValuePair<string, string>("떨어지는 운석",
                "var mx = 0\nevery 3\n  mx = random(-30, 30)\n  effect(\"ring\", mx, -12, 3, \"#ff5030\")\n  explode(mx, -12, 3, 20, 25)\n  sound(\"explosion\", mx, -12)\nend"),
            new KeyValuePair<string, string>("가까운 사람에게 포탑 발사",
                "var target = none\nevery 2\n  target = nearest(0, 10)\n  if target != none\n    shoot(0, 8, target.x, target.y - 8, 45, 15)\n  end\nend"),
            new KeyValuePair<string, string>("영역에 들어오면 발판 열기 (이름 plate, gate 필요)",
                "var open_until = 0\nvar opened = false\non enter plate\n  open_until = max(open_until, time + 4)\nend\non tick\n  if time < open_until and not opened\n    opened = true\n    move_by(gate, 0, -10, 0.6)\n  elif time >= open_until and opened\n    opened = false\n    move_by(gate, 0, 10, 0.6)\n  end\nend"),
            new KeyValuePair<string, string>("시작 안내 문구",
                "on start\n  hud(\"맵 안내 문구\")\n  banner(\"시작!\", 1.2)\nend")
        };

        public int ScriptLineCount
        {
            get
            {
                int n = 0;
                foreach (var l in Doc.Data.script) if (!string.IsNullOrWhiteSpace(l)) n++;
                return n;
            }
        }

        public int ScriptErrorCount
        {
            get
            {
                int n = 0;
                foreach (var i in _issues)
                    if (i.Severity == ContentSeverity.Error && (i.Message.Contains("script line") || i.Message.StartsWith("behaviors[") || i.Message.Contains("].name"))) n++;
                return n;
            }
        }

        /// <summary>Inline hint only; the game's ParseMap stays the authority (shown in the validation list).</summary>
        private string NameProblem(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (char.IsDigit(name[0])) return "숫자로 시작할 수 없습니다";
            foreach (char c in name) if (!char.IsLetterOrDigit(c) && c != '_') return "글자·숫자·_ 만 쓸 수 있습니다";
            if (ReservedWords.Contains(name) || MdsLibrary.Signatures.ContainsKey(name)) return "'" + name + "'은(는) 스크립트 예약어입니다";
            int count = 0;
            foreach (var o in Doc.All()) if (Doc.GetName(o) == name) count++;
            return count > 1 ? "같은 이름이 이미 있습니다 (맵 안에서 유일해야 함)" : null;
        }

        private void OnOpenScript()
        {
            _scriptText = string.Join("\n", Doc.Data.script);
            _caret = -1;
            _pendingCaret = -1;
            _parsedKey = null;
        }

        public string ScriptText => _scriptText;

        /// <summary>Replaces the map script (editor text → "script" lines). Trailing blank lines are dropped.</summary>
        public void SetScriptText(string text)
        {
            _scriptText = (text ?? "").Replace("\r", "");
            var lines = new List<string>(_scriptText.Split('\n'));
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1])) lines.RemoveAt(lines.Count - 1);
            Doc.Data.script = lines.ToArray();
            Changed();
        }

        private void ParseScriptIfNeeded()
        {
            var names = Doc.NamesInGameOrder();
            string key = _scriptText + "\u0001" + string.Join(",", names);
            if (key == _parsedKey) return;
            _parsedKey = key;
            _scriptErrors.Clear();
            _parsed = null;
            var lines = _scriptText.Replace("\r", "").Split('\n');
            if (string.IsNullOrWhiteSpace(_scriptText)) return;
            try { _parsed = MdsProgram.Parse(lines, names, _scriptErrors); }
            catch (Exception e) { _scriptErrors.Add(new MdsError(0, e.Message)); }
            float widest = 200f;
            foreach (var l in lines) widest = Mathf.Max(widest, EUi.Code.CalcSize(new GUIContent(l)).x);
            _codeWidth = widest + 60f;
        }

        private void JumpToScriptLine(int line)
        {
            int index = 0, current = 1;
            while (current < line && index < _scriptText.Length)
            {
                int nl = _scriptText.IndexOf('\n', index);
                if (nl < 0) break;
                index = nl + 1;
                current++;
            }
            _pendingCaret = index;
            _codeScroll.y = Mathf.Max(0f, (line - 4) * EUi.Code.lineHeight);
        }

        private void InsertScript(string snippet, bool asBlock)
        {
            int at = _caret >= 0 && _caret <= _scriptText.Length ? _caret : _scriptText.Length;
            string s = snippet;
            if (asBlock)
            {
                at = _scriptText.Length;
                s = (_scriptText.Length > 0 && !_scriptText.EndsWith("\n") ? "\n\n" : _scriptText.Length > 0 ? "\n" : "") + snippet + "\n";
            }
            SetScriptText(_scriptText.Insert(at, s));
            _pendingCaret = at + s.Length;
        }

        // ------------------------------------------------------------------ window

        private void DrawScriptWindow()
        {
            var r = ModalRect(EUi.W - 60, EUi.H - 50);
            if (Window(r, "맵 스크립트 · C# 맵 동작")) { CloseModal(); return; }
            float tx = r.x + 300;
            if (EUi.Btn(new Rect(tx, r.y + 14, 150, 26), "맵 스크립트 (" + ScriptLineCount + "줄)", _scriptTab == 0)) _scriptTab = 0;
            if (EUi.Btn(new Rect(tx + 156, r.y + 14, 150, 26), "C# 맵 동작 (" + Doc.Data.behaviors.Length + ")", _scriptTab == 1)) _scriptTab = 1;
            if (EUi.Btn(new Rect(r.xMax - 82 - 170, r.y + 14, 160, 26), "테스트 플레이 (F5)", false, GameAccepts, EUi.ButtonOn)) StartPlaytest(_ptKind, _ptOpponent);
            var body = new Rect(r.x + 18, r.y + 52, r.width - 36, r.height - 66);
            if (_scriptTab == 0) DrawScriptTab(body);
            else DrawBehaviorTab(body);
        }

        private void DrawScriptTab(Rect body)
        {
            ParseScriptIfNeeded();
            float refW = Mathf.Clamp(body.width * 0.34f, 300f, 460f);
            var codeArea = new Rect(body.x, body.y, body.width - refW - 12, body.height - 150);
            var errorArea = new Rect(body.x, codeArea.yMax + 8, codeArea.width, body.yMax - codeArea.yMax - 8);
            DrawCodeEditor(codeArea);
            DrawScriptErrors(errorArea);
            DrawReference(new Rect(codeArea.xMax + 12, body.y, refW, body.height));
        }

        private void DrawCodeEditor(Rect area)
        {
            EUi.Fill(area, new Color(0.045f, 0.05f, 0.065f));
            EUi.Frame(area, new Color(1f, 1f, 1f, 0.12f));
            var style = EUi.Code;
            float lineH = style.lineHeight;
            int lineCount = 1;
            foreach (char c in _scriptText) if (c == '\n') lineCount++;
            const float gutter = 44f;
            float contentH = Mathf.Max(area.height - 16f, lineCount * lineH + style.padding.vertical + area.height * 0.5f);
            float contentW = Mathf.Max(area.width - 16f, _codeWidth + gutter);

            var errorLines = new HashSet<int>();
            foreach (var e in _scriptErrors) errorLines.Add(e.Line);

            const string control = "script_code";
            var ev = Event.current;
            bool focused = GUI.GetNameOfFocusedControl() == control;
            if (focused && (ev.type == EventType.KeyDown) && (ev.keyCode == KeyCode.Tab || ev.character == '\t'))
            {
                var te0 = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                int at = Mathf.Clamp(te0.cursorIndex, 0, _scriptText.Length);
                if (ev.keyCode == KeyCode.Tab) { SetScriptText(_scriptText.Insert(at, "  ")); _pendingCaret = at + 2; }
                ev.Use();
            }

            _codeScroll = GUI.BeginScrollView(area, _codeScroll, new Rect(0, 0, contentW, contentH));
            EUi.Fill(new Rect(0, 0, gutter - 4, contentH), new Color(0f, 0f, 0f, 0.25f));
            int first = Mathf.Max(0, Mathf.FloorToInt(_codeScroll.y / lineH) - 1);
            int last = Mathf.Min(lineCount, first + Mathf.CeilToInt(area.height / lineH) + 3);
            for (int i = first; i < last; i++)
            {
                float ly = style.padding.top + i * lineH;
                if (errorLines.Contains(i + 1))
                {
                    EUi.Fill(new Rect(0, ly, contentW, lineH), new Color(1f, 0.25f, 0.2f, 0.16f));
                    EUi.Fill(new Rect(0, ly, 3, lineH), EUi.ErrorColor);
                }
                GUI.Label(new Rect(0, ly, gutter - 10, lineH), (i + 1).ToString(), EUi.Gutter);
            }
            GUI.SetNextControlName(control);
            string edited = GUI.TextArea(new Rect(gutter, 0, contentW - gutter, contentH), _scriptText, style);
            if (edited != _scriptText) SetScriptText(edited);
            if (GUI.GetNameOfFocusedControl() == control)
            {
                var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                if (_pendingCaret >= 0 && ev.type == EventType.Repaint)
                {
                    te.text = _scriptText;
                    te.cursorIndex = te.selectIndex = Mathf.Clamp(_pendingCaret, 0, _scriptText.Length);
                    _pendingCaret = -1;
                }
                _caret = te.cursorIndex;
                if (ev.type == EventType.KeyDown || ev.type == EventType.Repaint)
                {
                    int caretLine = 0;
                    for (int i = 0; i < Mathf.Min(_caret, _scriptText.Length); i++) if (_scriptText[i] == '\n') caretLine++;
                    float cy = style.padding.top + caretLine * lineH;
                    if (cy < _codeScroll.y) _codeScroll.y = cy;
                    else if (cy + lineH > _codeScroll.y + area.height - 20f) _codeScroll.y = cy + lineH - area.height + 20f;
                }
            }
            else if (_pendingCaret >= 0 && ev.type == EventType.Repaint)
            {
                GUI.FocusControl(control);
            }
            GUI.EndScrollView();
            if (string.IsNullOrEmpty(_scriptText))
                GUI.Label(new Rect(area.x + gutter + 10, area.y + 8, area.width - gutter - 20, 40),
                    "여기에 맵 스크립트를 씁니다. 오른쪽에서 이벤트·명령·예시를 눌러 넣을 수 있습니다.\n예: on start / every 5 / on enter 영역이름 … end", EUi.Area);
        }

        private void DrawScriptErrors(Rect area)
        {
            EUi.Fill(area, EUi.PanelDark);
            float x = area.x + 10, w = area.width - 20, y = area.y + 6;
            if (string.IsNullOrWhiteSpace(_scriptText))
            {
                Note("스크립트 없음. 비워 두면 맵 JSON 에 \"script\" 항목을 쓰지 않습니다.", EUi.Area, x, w, ref y);
            }
            else if (_scriptErrors.Count == 0 && _parsed != null)
            {
                Note("게임 해석기 문법 검사 통과 · 이벤트 " + _parsed.Handlers.Count + "개 · 변수 " + _parsed.VarNames.Length + "개" + (_parsed.VarNames.Length > 0 ? " (" + string.Join(", ", _parsed.VarNames) + ")" : "")
                     + (_parsed.Handlers.Count == 0 ? " · 경고: 이벤트(on start / every / on enter …)가 없어 아무 일도 하지 않습니다" : ""), _parsed.Handlers.Count == 0 ? EUi.Warn : EUi.Ok, x, w, ref y);
                Note("스크립트는 테스트 플레이와 실제 게임에서만 실행됩니다 (편집 화면에서는 멈춰 있음). 실행 상태와 멈춘 이유는 테스트 플레이 위쪽 줄에 표시됩니다.", EUi.Area, x, w, ref y);
            }
            else
            {
                Note("문법 오류 " + _scriptErrors.Count + "개 — 게임이 이 맵을 불러오지 않습니다. 줄을 누르면 그 줄로 이동합니다.", EUi.Error, x, w, ref y);
                var lines = _scriptText.Split('\n');
                foreach (var e in _scriptErrors)
                {
                    if (y > area.yMax - 18) break;
                    string src = e.Line >= 1 && e.Line <= lines.Length ? "  → \"" + lines[e.Line - 1].Trim() + "\"" : "";
                    var row = new Rect(x, y, w, 18);
                    GUI.Label(row, "줄 " + e.Line + ": " + e.Message + src, EUi.Error);
                    if (GUI.Button(row, GUIContent.none, GUIStyle.none)) JumpToScriptLine(e.Line);
                    y += 19;
                }
            }
        }

        private bool Chips(ref float x, ref float y, float left, float right, string label, bool on = false)
        {
            float cw = EUi.Chip.CalcSize(new GUIContent(label)).x + 4;
            if (x + cw > right && x > left) { x = left; y += 22; }
            bool c = GUI.Button(new Rect(x, y, cw, 20), label, on ? EUi.ChipOn : EUi.Chip);
            x += cw + 4;
            return c;
        }

        private float _refHeight = 1200f;

        private void DrawReference(Rect area)
        {
            EUi.Fill(area, EUi.PanelDark);
            _refScroll = GUI.BeginScrollView(area, _refScroll, new Rect(0, 0, area.width - 16, _refHeight));
            float left = 10, right = area.width - 24, w = right - left, y = 8;
            var names = Doc.NamesInGameOrder();

            GUI.Label(new Rect(left, y, w, 20), "이름 붙은 오브젝트 " + names.Count + "개", EUi.Header);
            y += 22;
            if (names.Count == 0) Note("오브젝트를 선택하고 오른쪽 패널에서 '이름'을 입력하면 스크립트에서 쓸 수 있습니다. 트리거 영역(도구 8)은 자동으로 이름이 붙습니다.", EUi.Area, left, w, ref y);
            else
            {
                float cx = left;
                foreach (var n in names)
                {
                    var o = Doc.FindByName(n);
                    if (Chips(ref cx, ref y, left, right, n + " · " + MapDocument.KindName(o.Kind))) InsertScript(n, false);
                }
                y += 26;
                Note("속성: 이름.x  .y  .rotation  .w  .h  .visible  .power  .dps  .fx  .fy", EUi.Tiny, left, w, ref y);
            }

            y += 4;
            GUI.Label(new Rect(left, y, w, 20), "이벤트 (블록은 end 로 닫음)", EUi.Header);
            y += 22;
            string area0 = null, any0 = names.Count > 0 ? names[0] : "이름";
            foreach (var n in names) if (Doc.FindByName(n).Kind == ObjKind.Area) { area0 = n; break; }
            area0 = area0 ?? any0;
            var events = new[]
            {
                new KeyValuePair<string, string>("on start", "on start\n  \nend"),
                new KeyValuePair<string, string>("on tick", "on tick\n  \nend"),
                new KeyValuePair<string, string>("every 5", "every 5\n  \nend"),
                new KeyValuePair<string, string>("after 30", "after 30\n  \nend"),
                new KeyValuePair<string, string>("on enter " + area0, "on enter " + area0 + "\n  \nend"),
                new KeyValuePair<string, string>("on exit " + area0, "on exit " + area0 + "\n  \nend"),
                new KeyValuePair<string, string>("on inside " + area0, "on inside " + area0 + "\n  \nend"),
                new KeyValuePair<string, string>("on death", "on death\n  \nend"),
                new KeyValuePair<string, string>("on shot " + any0, "on shot " + any0 + "\n  \nend"),
                new KeyValuePair<string, string>("var 이름 = 0", "var count = 0"),
                new KeyValuePair<string, string>("if … end", "if count > 0\n  \nend"),
                new KeyValuePair<string, string>("repeat 3", "repeat 3\n  \nend"),
                new KeyValuePair<string, string>("foreach player", "foreach player\n  \nend")
            };
            float ex = left;
            foreach (var e in events)
                if (Chips(ref ex, ref y, left, right, e.Key)) InsertScript(e.Value, e.Key.StartsWith("var") ? false : _caret < 0);
            y += 26;
            Note("내장 값: time · dt · player (플레이어 이벤트·foreach 안) · event_x · event_y · player.x .y .vx .vy .hp .team .alive", EUi.Tiny, left, w, ref y);

            y += 4;
            GUI.Label(new Rect(left, y, w, 20), "명령 (게임 동작만 실행 가능)", EUi.Header);
            y += 22;
            var commands = new List<string>();
            var functions = new List<string>();
            foreach (var kv in MdsLibrary.Signatures) (kv.Value.Command ? commands : functions).Add(kv.Key);
            foreach (var name in commands) y = ReferenceRow(name, left, w, y);
            y += 6;
            GUI.Label(new Rect(left, y, w, 20), "함수 (값을 돌려줌)", EUi.Header);
            y += 22;
            foreach (var name in functions) y = ReferenceRow(name, left, w, y);

            y += 6;
            GUI.Label(new Rect(left, y, w, 20), "예시 넣기 (끝에 추가)", EUi.Header);
            y += 22;
            foreach (var e in Examples)
            {
                if (EUi.Btn(new Rect(left, y, w, 22), e.Key)) InsertScript(e.Value, true);
                y += 25;
            }
            y += 6;
            Note("전체 문법·명령표: 게임 프로젝트 Docs/MAP_SCRIPTING.md. 모든 숫자는 게임이 안전한 범위로 자르고, 오류·무한 반복이면 그 맵 스크립트만 멈춥니다.", EUi.Tiny, left, w, ref y);
            _refHeight = y + 10;
            GUI.EndScrollView();
        }

        private float ReferenceRow(string name, float left, float w, float y)
        {
            var sig = MdsLibrary.Signatures[name];
            string usage = sig.Usage;
            string insert = usage;
            int cut = insert.IndexOf(" —", StringComparison.Ordinal);
            if (cut < 0) cut = insert.IndexOf(" →", StringComparison.Ordinal);
            if (cut < 0) cut = insert.IndexOf(" or ", StringComparison.Ordinal);
            if (cut > 0) insert = insert.Substring(0, cut);
            string ko = CommandKo.TryGetValue(name, out var k) ? k : "";
            var row = new Rect(left, y, w, 20);
            if (GUI.Button(row, "", EUi.ListItem)) InsertScript(insert, false);
            GUI.Label(new Rect(left + 4, y, w * 0.62f, 20), usage, EUi.Small);
            GUI.Label(new Rect(left + w * 0.62f + 6, y, w * 0.38f - 6, 20), ko, EUi.Tiny);
            return y + 21;
        }

        // ------------------------------------------------------------------ C# behaviors

        private void DrawBehaviorTab(Rect body)
        {
            var view = new Rect(body.x, body.y, body.width, body.height);
            _behaviorScroll = GUI.BeginScrollView(view, _behaviorScroll, new Rect(0, 0, body.width - 16, _behaviorHeight));
            float left = 4, w = body.width - 30, y = 4;
            Note("게임 빌드에 들어 있는 C# 맵 동작([MapBehavior] 클래스)을 이 맵에 붙입니다. 종류·검사는 게임의 MapBehaviorRegistry 를 그대로 씁니다. 새 종류는 게임 프로젝트에서 C# 파일로 만든 뒤 GameSync 로 에디터에 가져옵니다.", EUi.Area, left, w, ref y);
            var names = Doc.NamesInGameOrder();
            var ids = new List<string>(MapBehaviorRegistry.Ids);
            ids.Sort(StringComparer.Ordinal);
            var list = new List<MapBehaviorSpec>(Doc.Data.behaviors);
            int remove = -1, up = -1;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b == null) continue;
                float top = y;
                EUi.Fill(new Rect(left, y, w, 4), EUi.Accent * new Color(1, 1, 1, 0.5f));
                y += 10;
                GUI.Label(new Rect(left + 8, y, 300, 22), "behaviors[" + i + "]  " + b.type, EUi.Header);
                if (EUi.Btn(new Rect(left + w - 70, y, 70, 22), "삭제", false, true, EUi.ButtonBad)) remove = i;
                if (EUi.Btn(new Rect(left + w - 70 - 76, y, 70, 22), "위로", false, i > 0)) up = i;
                y += 28;
                GUI.Label(new Rect(left + 8, y, 70, 20), "종류", EUi.Small);
                float cx = left + 80;
                foreach (var id in ids)
                    if (Chips(ref cx, ref y, left + 80, left + w, id, b.type == id) && b.type != id) { b.type = id; Changed(); }
                y += 24;
                Note(MapBehaviorRegistry.SummaryOf(b.type), EUi.Area, left + 80, w - 80, ref y);
                y = NameChips(b, true, names, left, w, y);
                y = NameChips(b, false, names, left, w, y);
                float cw = (w - 16) / 3f;
                string k = "beh_" + i + "_";
                float[] vals = { b.speed, b.amount, b.interval, b.delay, b.duration, b.damage, b.radius, b.x, b.y };
                string[] labels = { "speed", "amount", "interval", "delay", "duration", "damage", "radius", "x", "y" };
                for (int f = 0; f < vals.Length; f++)
                {
                    var rr = new Rect(left + 8 + (f % 3) * (cw + 4), y + (f / 3) * 26, cw, 22);
                    float nv = EUi.Row(k + labels[f], rr, labels[f], vals[f], 66);
                    if (nv != vals[f]) { vals[f] = nv; Changed(); }
                }
                b.speed = vals[0]; b.amount = vals[1]; b.interval = vals[2]; b.delay = vals[3]; b.duration = vals[4]; b.damage = vals[5]; b.radius = vals[6]; b.x = vals[7]; b.y = vals[8];
                y += 3 * 26 + 2;
                string text = EUi.TextRow(k + "text", new Rect(left + 8, y, cw * 1.5f, 22), "text", b.text, 66);
                if (text != b.text) { b.text = text; Changed(); }
                string sound = EUi.TextRow(k + "sound", new Rect(left + 16 + cw * 1.5f, y, cw * 1.5f, 22), "sound", b.sound, 66);
                if (sound != b.sound) { b.sound = sound; Changed(); }
                y += 28;
                string err = MapBehaviorRegistry.Validate(b, names);
                Note(err == null ? "게임 검사 통과 (안 쓰는 값은 무시됩니다)" : "오류: " + err, err == null ? EUi.Ok : EUi.Error, left + 8, w - 16, ref y);
                EUi.Frame(new Rect(left, top, w, y - top + 4), new Color(1f, 1f, 1f, 0.08f));
                y += 14;
            }
            if (remove >= 0) { list.RemoveAt(remove); Doc.Data.behaviors = list.ToArray(); Changed(); GUIUtility.keyboardControl = 0; }
            else if (up > 0) { (list[up - 1], list[up]) = (list[up], list[up - 1]); Doc.Data.behaviors = list.ToArray(); Changed(); GUIUtility.keyboardControl = 0; }
            if (EUi.Btn(new Rect(left, y, 220, 30), "+ C# 맵 동작 추가", false, ids.Count > 0, EUi.ButtonOn))
            {
                list.Add(new MapBehaviorSpec { type = ids.Contains("spin") ? "spin" : ids[0], target = names.Count > 0 ? names[0] : "" });
                Doc.Data.behaviors = list.ToArray();
                Changed();
            }
            y += 40;
            _behaviorHeight = y + 10;
            GUI.EndScrollView();
        }

        private float NameChips(MapBehaviorSpec b, bool target, List<string> names, float left, float w, float y)
        {
            GUI.Label(new Rect(left + 8, y, 70, 20), target ? "target" : "area", EUi.Small);
            string current = target ? b.target : b.area;
            float cx = left + 80;
            if (Chips(ref cx, ref y, left + 80, left + w, "(없음)", string.IsNullOrEmpty(current)) && !string.IsNullOrEmpty(current)) { Set(""); }
            foreach (var n in names)
                if (Chips(ref cx, ref y, left + 80, left + w, n, current == n) && current != n) Set(n);
            if (!string.IsNullOrEmpty(current) && !names.Contains(current) && Chips(ref cx, ref y, left + 80, left + w, current + " (없는 이름)", true)) Set("");
            return y + 26;

            void Set(string v)
            {
                if (target) b.target = v; else b.area = v;
                Changed();
            }
        }
    }
}
