using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Player;

namespace Metadream.MapEditor
{
    public sealed partial class MapEditorApp
    {
        private Rect ModalRect(float w, float h)
        {
            w = Mathf.Min(w, EUi.W - 40);
            h = Mathf.Min(h, EUi.H - 40);
            return new Rect((EUi.W - w) / 2f, (EUi.H - h) / 2f, w, h);
        }

        private void DrawModal()
        {
            EUi.Fill(new Rect(0, 0, EUi.W, EUi.H), new Color(0f, 0f, 0f, 0.6f));
            switch (_modal)
            {
                case Modal.Open: DrawOpen(); break;
                case Modal.SaveAs: DrawSaveAs(); break;
                case Modal.Export: DrawExport(); break;
                case Modal.Playtest: DrawPlaytestSetup(); break;
                case Modal.GameRun: DrawGameRun(); break;
                case Modal.Settings: DrawSettings(); break;
                case Modal.Help: DrawHelp(); break;
                case Modal.Confirm: DrawConfirm(); break;
                case Modal.Script: DrawScriptWindow(); break;
            }
        }

        private bool Window(Rect r, string title)
        {
            EUi.Fill(r, EUi.PanelColor);
            EUi.Frame(r, new Color(1f, 1f, 1f, 0.14f));
            GUI.Label(new Rect(r.x + 18, r.y + 12, r.width - 120, 28), title, EUi.Title);
            return EUi.Btn(new Rect(r.xMax - 82, r.y + 14, 68, 26), "닫기 (Esc)");
        }

        private void Log(string text, Color color)
        {
            _exportLog.Add(new KeyValuePair<string, Color>(text, color));
            Debug.Log("[MapEditor] " + text);
        }

        /// <summary>Path text field + "찾아보기" button; returns the (possibly browsed) path.</summary>
        private string PathRow(string key, Rect r, string label, string value, Func<string> browse)
        {
            GUI.Label(new Rect(r.x, r.y, 96, r.height), label, EUi.Small);
            string v = EUi.Text(key, new Rect(r.x + 96, r.y, r.width - 96 - 92, r.height), value);
            if (EUi.Btn(new Rect(r.xMax - 86, r.y, 86, r.height), "찾아보기..."))
            {
                string picked = browse();
                if (!string.IsNullOrEmpty(picked)) { v = picked; GUIUtility.keyboardControl = 0; }
            }
            return v;
        }

        // ------------------------------------------------------------------ open / save

        private void DrawOpen()
        {
            var r = ModalRect(700, 640);
            if (Window(r, "맵 열기")) { CloseModal(); return; }
            float x = r.x + 18, w = r.width - 36, y = r.y + 54;
            _pathField = PathRow("open_path", new Rect(x, y, w - 76, 26), "JSON 파일", _pathField, () => NativeDialogs.OpenFile("맵 JSON 파일 열기", "json"));
            if (EUi.Btn(new Rect(x + w - 70, y, 70, 26), "열기", false, _pathField.Trim().Length > 0, EUi.ButtonOn))
            {
                string p = _pathField;
                GuardUnsaved(() => { if (OpenFile(p)) CloseModal(); });
            }
            y += 34;
            GUI.Label(new Rect(x, y, w, 18), "게임 맵 JSON(내장 Resources/Content/Maps, 게임 폴더 Content/Maps)과 이 에디터가 저장한 파일을 열 수 있습니다.", EUi.Tiny);
            y += 24;
            if (File.Exists(AutosavePath))
            {
                if (EUi.Btn(new Rect(x, y, w, 26), "자동 저장본 복구 (" + File.GetLastWriteTime(AutosavePath).ToString("MM-dd HH:mm") + ")"))
                {
                    GuardUnsaved(() =>
                    {
                        try
                        {
                            var doc = MapDocument.FromJson(File.ReadAllText(AutosavePath));
                            SetDocument(doc, false);
                            _savedJson = "";
                            CloseModal();
                            Toast("자동 저장본을 복구했습니다. 저장해 주세요.", EUi.WarnColor);
                        }
                        catch (Exception e) { Toast("복구 실패: " + e.Message, EUi.ErrorColor); }
                    });
                }
                y += 32;
            }
            GUI.Label(new Rect(x, y, w, 20), "최근 파일", EUi.Header);
            y += 22;
            int shown = 0;
            foreach (var p in _recent.ToArray())
            {
                if (!File.Exists(p)) continue;
                if (GUI.Button(new Rect(x, y, w, 20), p, EUi.ListItem)) { string path = p; GuardUnsaved(() => { if (OpenFile(path)) CloseModal(); }); }
                y += 21;
                if (++shown >= 5) break;
            }
            if (shown == 0) { GUI.Label(new Rect(x, y, w, 20), "없음", EUi.Tiny); y += 20; }
            y += 8;
            var builtins = new List<string>(GameContent.BuiltinMaps.Keys);
            builtins.Sort(StringComparer.Ordinal);
            GUI.Label(new Rect(x, y, w, 20), "게임 내장 맵 " + builtins.Count + "개 (템플릿으로 열기)", EUi.Header);
            y += 22;
            var view = new Rect(x, y, w, r.yMax - y - 14);
            _builtinScroll = GUI.BeginScrollView(view, _builtinScroll, new Rect(0, 0, w - 16, builtins.Count * 22));
            for (int i = 0; i < builtins.Count; i++)
            {
                string id = builtins[i];
                var d = GameContent.BuiltinMaps[id];
                string label = (d != null && !string.IsNullOrEmpty(d.name) ? d.name : id) + "   ·   " + id + (d != null && d.tutorialOnly ? "   · 튜토리얼" : "")
                    + (d != null && !string.IsNullOrEmpty(d.origin) ? "   · " + d.origin : "");
                if (GUI.Button(new Rect(0, i * 22, w - 18, 21), label, EUi.ListItem)) { string pick = id; GuardUnsaved(() => { if (OpenBuiltin(pick)) CloseModal(); }); }
            }
            GUI.EndScrollView();
        }

        private void DrawSaveAs()
        {
            var r = ModalRect(720, 230);
            if (Window(r, "다른 이름으로 저장")) { CloseModal(); return; }
            float x = r.x + 18, w = r.width - 36, y = r.y + 58;
            string id = GameContent.SanitizeId(Doc.Data.id);
            _pathField = PathRow("save_path", new Rect(x, y, w, 26), "저장 위치", _pathField, () => NativeDialogs.SaveFile("맵 저장", id + ".json", DefaultDir));
            y += 36;
            Note("파일은 게임 맵 JSON 형식(UTF-8)입니다. 게임 Content 폴더에 넣을 때는 '게임에 적용 · 내보내기'를 쓰면 파일 이름을 id(" + id + ".json)로 맞춰 줍니다.", EUi.Area, x, w, ref y);
            if (EUi.Btn(new Rect(r.xMax - 18 - 120, r.yMax - 46, 120, 30), "저장", false, _pathField.Trim().Length > 0, EUi.ButtonOn))
                if (SaveTo(_pathField)) CloseModal();
        }

        // ------------------------------------------------------------------ export / apply

        private void DrawExport()
        {
            var r = ModalRect(820, 760);
            if (Window(r, "게임에 적용 · 내보내기")) { CloseModal(); return; }
            float x = r.x + 18, w = r.width - 36, y = r.y + 52;
            string id = GameContent.SanitizeId(Doc.Data.id);
            int errors = GameContent.ErrorCount(_issues);
            if (!GameContent.IsValidId(Doc.Data.id))
            {
                Note("id '" + Doc.Data.id + "'는 파일 이름으로 쓸 수 없습니다 → '" + id + "'로 고쳐야 적용됩니다.", EUi.Error, x, w - 110, ref y);
                if (EUi.Btn(new Rect(r.xMax - 18 - 100, y - 24, 100, 22), "id 고치기")) { Doc.Data.id = id; Changed(); CommitIfNeeded(true); Revalidate(); }
            }
            if (errors == 0) Note("게임 로더 검증 통과 → 파일 이름 " + id + ".json" + (GameContent.IsBuiltinId(id) ? " · 같은 이름의 내장 맵을 덮어씁니다" : " · 새 맵으로 추가됩니다"), EUi.Ok, x, w, ref y);
            else Note("검증 오류 " + errors + "개: 게임이 이 맵을 불러오지 않으므로 적용할 수 없습니다. 오른쪽 '검증' 목록을 먼저 고치세요.", EUi.Error, x, w, ref y);
            _exportEditorData = EUi.Toggle(new Rect(x, y, w, 20), _exportEditorData, "에디터 전용 배경(background) 정보도 파일에 넣기 — 게임은 무시하며 다시 열 때 배경이 유지됩니다");
            y += 28;
            bool ok = errors == 0;

            Section("1. 게임에 바로 적용 (게임 실행 파일 옆 Content/Maps)", x, w, ref y);
            string gp = PathRow("export_game", new Rect(x, y, w, 26), "게임 위치", GamePath, NativeDialogs.ChooseGame);
            if (gp != GamePath) { GamePath = gp.Trim(); SaveSettings(); }
            y += 30;
            string content = GameContentDir;
            bool gameFound = GameLauncher.FindExecutable(GamePath) != null;
            GUI.Label(new Rect(x + 96, y, w - 96, 18), content == null ? "게임 앱(.app)·실행 파일(.exe) 또는 그 폴더를 지정하세요" : "설치 위치: " + Path.Combine(content, "Maps", id + ".json") + (gameFound ? "" : "  (실행 파일을 찾지 못함)"), EUi.Tiny);
            y += 24;
            float bw = (w - 12) / 3f;
            if (EUi.Btn(new Rect(x, y, bw, 30), "게임에 적용", false, ok && content != null, EUi.ButtonOn)) InstallTo(content, "게임");
            if (EUi.Btn(new Rect(x + bw + 6, y, bw, 30), "적용 후 이 맵만으로 실행", false, ok && gameFound))
            {
                InstallTo(content, "게임");
                string err = LaunchRealGame(GameLauncher.Mode.VersusCpu);
                Log(err ?? "실제 게임을 이 맵만으로 실행했습니다 (CPU와 대전, -content " + GameContent.GameTestContentDir + ")", err == null ? EUi.OkColor : EUi.ErrorColor);
            }
            if (EUi.Btn(new Rect(x + 2 * (bw + 6), y, bw, 30), "게임 실행 (모든 맵)", false, gameFound))
            {
                string err = GameLauncher.Launch(GamePath, null, GameLauncher.Mode.VersusCpu);
                Log(err ?? "게임을 실행했습니다 (Content 폴더 포함 전체 맵 순환; 디버그 모드 F2로 맵 넘기기)", err == null ? EUi.OkColor : EUi.ErrorColor);
            }
            y += 40;

            Section("2. 전용 서버 · 다른 Content 폴더", x, w, ref y);
            _otherContentDir = PathRow("export_other", new Rect(x, y, w - 96, 26), "폴더", _otherContentDir, () => NativeDialogs.ChooseFolder("서버 실행 파일이 있는 폴더 또는 Content 폴더 선택"));
            if (EUi.Btn(new Rect(x + w - 90, y, 90, 26), "적용", false, ok && _otherContentDir.Trim().Length > 0)) InstallTo(GameContent.ContentDirForGame(_otherContentDir), "폴더");
            y += 30;
            GUI.Label(new Rect(x + 96, y, w - 96, 18), "서버 실행 파일이 있는 폴더를 고르면 그 안의 Content/Maps 에, Content 폴더를 고르면 그 Maps 에 씁니다.", EUi.Tiny);
            y += 26;

            Section("3. 배포 패키지 (.zip) — 플레이어·서버에 같은 파일 배포", x, w, ref y);
            _zipPath = PathRow("export_zip", new Rect(x, y, w - 96, 26), "zip 파일", _zipPath, () => NativeDialogs.SaveFile("배포 패키지 저장", id + "_map_package.zip", DefaultDir));
            if (EUi.Btn(new Rect(x + w - 90, y, 90, 26), "만들기", false, ok && _zipPath.Trim().Length > 0))
            {
                try
                {
                    string zip = _zipPath.Trim();
                    if (!zip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip += ".zip";
                    GameContent.BuildPackage(Doc, zip, _exportEditorData);
                    Log("패키지를 만들었습니다: " + zip + "  (Content/Maps/" + id + ".json + 설치 안내). 게임/서버 실행 파일 옆에 압축을 풀면 됩니다.", EUi.OkColor);
                }
                catch (Exception e) { Log("패키지 실패: " + e.Message, EUi.ErrorColor); }
            }
            y += 34;

            Section("4. JSON 파일로만 저장 (내장 맵으로 넣을 때: 게임 프로젝트 Resources/Content/Maps)", x, w, ref y);
            _jsonPath = PathRow("export_json", new Rect(x, y, w - 96, 26), "JSON 파일", _jsonPath, () => NativeDialogs.SaveFile("맵 JSON 내보내기", id + ".json", DefaultDir));
            if (EUi.Btn(new Rect(x + w - 90, y, 90, 26), "저장", false, ok && _jsonPath.Trim().Length > 0))
            {
                try
                {
                    string p = _jsonPath.Trim();
                    if (Directory.Exists(p)) p = Path.Combine(p, id + ".json");
                    GameContent.WriteMapFile(Doc, p, _exportEditorData);
                    Log("저장했습니다: " + p + (Path.GetFileNameWithoutExtension(p) != id ? "  (주의: 게임은 파일 이름 = id 를 기대합니다)" : ""), EUi.OkColor);
                }
                catch (Exception e) { Log("저장 실패: " + e.Message, EUi.ErrorColor); }
            }
            y += 36;

            var logRect = new Rect(x, y, w, r.yMax - y - 14);
            EUi.Fill(logRect, EUi.PanelDark);
            float ly = logRect.y + 6;
            if (_exportLog.Count == 0) GUI.Label(new Rect(x + 8, ly, w - 16, 18), "결과가 여기에 표시됩니다.", EUi.Tiny);
            for (int i = Mathf.Max(0, _exportLog.Count - 8); i < _exportLog.Count; i++)
            {
                var st = new GUIStyle(EUi.Area) { normal = { textColor = _exportLog[i].Value } };
                float h = st.CalcHeight(new GUIContent(_exportLog[i].Key), w - 16);
                if (ly + h > logRect.yMax) break;
                GUI.Label(new Rect(x + 8, ly, w - 16, h), _exportLog[i].Key, st);
                ly += h + 3;
            }
        }

        private void InstallTo(string contentDir, string what)
        {
            try
            {
                if (string.IsNullOrEmpty(contentDir)) { Log("Content 폴더를 정할 수 없습니다. 위치를 확인하세요.", EUi.ErrorColor); return; }
                CommitIfNeeded(true);
                string path = GameContent.InstallToContentDir(Doc, contentDir, _exportEditorData, out bool replaced);
                string id = GameContent.SanitizeId(Doc.Data.id);
                string hash = GameContent.ContentHashWith(contentDir, out int playable, out var issues);
                bool loaded = true;
                foreach (var i in issues)
                    if (i.Severity == ContentSeverity.Error && i.Source == path) { loaded = false; Log("게임 로더 오류: " + i.Message, EUi.ErrorColor); }
                Log((what == "게임" ? "게임에 적용했습니다: " : "적용했습니다: ") + path + (replaced ? " (기존 파일은 .bak 으로 보관)" : "") + (GameContent.IsBuiltinId(id) ? " · 내장 맵 '" + id + "' 덮어씀" : ""), loaded ? EUi.OkColor : EUi.ErrorColor);
                Log("이 Content 폴더로 게임이 계산할 콘텐츠 해시: " + hash + " · 플레이 가능한 맵 " + playable + "개 (에디터의 게임 런타임 기준). 온라인은 서버와 모든 참가자의 해시가 같아야 접속됩니다.", EUi.TextDim);
                Log("게임을 다시 시작하면 반영됩니다 (디버그 모드 타이틀에서는 F5 로 다시 불러오기).", EUi.TextDim);
            }
            catch (Exception e) { Log("적용 실패: " + e.Message, EUi.ErrorColor); }
        }

        // ------------------------------------------------------------------ play test / game run

        private void DrawPlaytestSetup()
        {
            var r = ModalRect(700, 540);
            if (Window(r, "테스트 플레이")) { CloseModal(); return; }
            float x = r.x + 18, w = r.width - 36, y = r.y + 52;
            Note("게임의 실제 코드(GameFlow · Fighter · MapRoot · Projectile, 같은 Physics2D 설정과 0.02초 고정 스텝)로 이 맵을 실행합니다. 맵은 게임 콘텐츠 로더를 거쳐 불러옵니다. Esc 로 에디터에 돌아옵니다.", EUi.Area, x, w, ref y);
            y += 4;
            var kinds = new[] { Playtest.Kind.Sandbox, Playtest.Kind.VersusCpu, Playtest.Kind.CpuDemo, Playtest.Kind.CpuDemoTeams };
            string[] desc =
            {
                "자유 테스트 — 점수·카드 없이 바로 움직여 보기, 죽으면 1초 뒤 다시 스폰",
                "CPU와 대전 — 게임과 같은 규칙(포인트·라운드·카드 선택·서든 데스), 이 맵만 사용",
                "CPU 관전 1대1 — CPU 두 명이 이 맵에서 대전",
                "CPU 관전 2대2 — CPU 네 명이 팀 대전 (2대2 스폰 확인)"
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                if (EUi.Btn(new Rect(x, y, w, 28), desc[i], _ptKind == kinds[i])) _ptKind = kinds[i];
                y += 32;
            }
            y += 6;
            if (_ptKind == Playtest.Kind.Sandbox)
            {
                GUI.Label(new Rect(x, y, 80, 26), "상대", EUi.Small);
                var opps = new[] { Playtest.Opponent.None, Playtest.Opponent.Dummy, Playtest.Opponent.Cpu, Playtest.Opponent.Teams };
                string[] names = { "없음", "허수아비", "CPU", "2대2 (CPU 3명)" };
                float bw = (w - 80 - 18) / 4f;
                for (int i = 0; i < opps.Length; i++)
                    if (EUi.Btn(new Rect(x + 80 + i * (bw + 6), y, bw, 26), names[i], _ptOpponent == opps[i])) _ptOpponent = opps[i];
                y += 34;
            }
            else if (_ptKind == Playtest.Kind.VersusCpu)
            {
                GUI.Label(new Rect(x, y, 80, 26), "승리 라운드", EUi.Small);
                int[] rounds = { 1, 2, 3, 5 };
                for (int i = 0; i < rounds.Length; i++)
                    if (EUi.Btn(new Rect(x + 80 + i * 66, y, 60, 26), rounds[i] + (rounds[i] == 5 ? " (기본)" : ""), _ptRounds == rounds[i])) _ptRounds = rounds[i];
                y += 34;
            }
            string gc = GameContentDir;
            _ptUseGameContent = EUi.Toggle(new Rect(x, y, w, 20), _ptUseGameContent, "게임 폴더 Content 의 카드·맵도 함께 불러오기 (게임과 같은 환경)");
            y += 22;
            GUI.Label(new Rect(x + 20, y, w - 20, 18), gc != null && Directory.Exists(gc) ? gc : "게임 Content 폴더 없음 → 내장 콘텐츠만 사용", EUi.Tiny);
            y += 28;
            Note("조작: " + KeyboardBinding.PlayerOne().Help + " · 슬로모션 T · 다시 스폰 R (자유 테스트)", EUi.Area, x, w, ref y);
            if (!GameAccepts) Note("검증 오류가 있어 시작할 수 없습니다.", EUi.Error, x, w, ref y);
            if (EUi.Btn(new Rect(r.xMax - 18 - 160, r.yMax - 48, 160, 32), "시작 (F5)", false, GameAccepts, EUi.ButtonOn)) StartPlaytest(_ptKind, _ptOpponent);
        }

        private void DrawGameRun()
        {
            var r = ModalRect(760, 430);
            if (Window(r, "실제 게임으로 실행")) { CloseModal(); return; }
            float x = r.x + 18, w = r.width - 36, y = r.y + 52;
            Note("설치된 Metadream Shoot 빌드를 이 맵만으로 실행합니다. 게임 폴더는 바꾸지 않고, 에디터 전용 Content 폴더(다른 맵은 \"disabled\" 덮어쓰기)를 -content 로 넘깁니다.", EUi.Area, x, w, ref y);
            string gp = PathRow("run_game", new Rect(x, y, w, 26), "게임 위치", GamePath, NativeDialogs.ChooseGame);
            if (gp != GamePath) { GamePath = gp.Trim(); SaveSettings(); }
            y += 30;
            string exe = GameLauncher.FindExecutable(GamePath);
            GUI.Label(new Rect(x + 96, y, w - 96, 18), exe != null ? "실행 파일: " + exe : "MetadreamShoot.app / MetadreamShoot.exe 를 지정하세요", exe != null ? EUi.Tiny : EUi.Warn);
            y += 28;
            var modes = new[] { GameLauncher.Mode.VersusCpu, GameLauncher.Mode.CheatDuel, GameLauncher.Mode.CpuDemo };
            string[] names = { "CPU와 대전", "치트 대전 (디버그: F2~F8, 카드 검색)", "CPU 관전" };
            float bw = (w - 12) / 3f;
            for (int i = 0; i < modes.Length; i++)
                if (EUi.Btn(new Rect(x + i * (bw + 6), y, bw, 28), names[i], _runMode == modes[i])) _runMode = modes[i];
            y += 38;
            GUI.Label(new Rect(x, y, w, 18), "Content 폴더: " + GameContent.GameTestContentDir, EUi.Tiny);
            y += 26;
            if (!GameAccepts) Note("검증 오류가 있어 실행할 수 없습니다.", EUi.Error, x, w, ref y);
            if (EUi.Btn(new Rect(r.xMax - 18 - 160, r.yMax - 48, 160, 32), "실행", false, GameAccepts && exe != null, EUi.ButtonOn))
            {
                string err = LaunchRealGame(_runMode);
                if (err != null) Toast(err, EUi.ErrorColor);
                else { Toast("실제 게임을 이 맵만으로 실행했습니다", EUi.OkColor); CloseModal(); }
            }
        }

        // ------------------------------------------------------------------ settings / help / confirm

        private void DrawSettings()
        {
            var r = ModalRect(760, 430);
            if (Window(r, "설정")) { CloseModal(); return; }
            float x = r.x + 18, w = r.width - 36, y = r.y + 56;
            string gp = PathRow("settings_game", new Rect(x, y, w, 26), "게임 위치", GamePath, NativeDialogs.ChooseGame);
            if (gp != GamePath) { GamePath = gp.Trim(); SaveSettings(); }
            y += 32;
            if (EUi.Btn(new Rect(x + 96, y, 140, 24), "자동으로 찾기"))
            {
                string found = DetectGame();
                if (found != null) { GamePath = found; SaveSettings(); Toast("게임을 찾았습니다: " + found, EUi.OkColor); }
                else Toast("에디터 옆이나 동기화한 게임 프로젝트의 Builds 폴더에서 게임을 찾지 못했습니다", EUi.WarnColor);
            }
            y += 36;
            var info = GameRuntimeInfo.Current;
            Section("게임 런타임 (물리·규칙 코드의 출처)", x, w, ref y);
            Note("이 에디터의 Metadream.Shoot 어셈블리와 Physics2D·Time·레이어 설정은 아래 게임 프로젝트에서 그대로 복사했습니다(GameSync). 게임을 수정했다면 에디터 프로젝트에서 GameSync 로 다시 동기화한 뒤 빌드하세요.", EUi.Area, x, w, ref y);
            GUI.Label(new Rect(x, y, w, 18), "원본: " + info.sourceProject, EUi.Tiny); y += 18;
            GUI.Label(new Rect(x, y, w, 18), "커밋: " + info.sourceCommit + " · 동기화: " + info.syncedAt + " · Unity " + info.unityVersion, EUi.Tiny); y += 18;
            GUI.Label(new Rect(x, y, w, 18), "런타임 파일 " + info.fileCount + "개 SHA-256 " + info.runtimeSha256, EUi.Tiny); y += 18;
            GUI.Label(new Rect(x, y, w, 18), "물리 설정 SHA-256 " + info.settingsSha256, EUi.Tiny); y += 26;
            GUI.Label(new Rect(x, y, w, 18), "에디터 데이터: " + Application.persistentDataPath, EUi.Tiny);
        }

        private const string HelpText =
            "기본 조작\n" +
            "· 클릭: 선택 (겹친 곳은 다시 클릭하면 다음 오브젝트) · 끌기: 이동 · 흰 사각 점: 크기 · 위쪽 원: 회전 (Shift = 15° 단위)\n" +
            "· 드래그 선택 도구(M): 빈 곳을 좌클릭으로 끌어 여러 개 선택 (왼→오른쪽 = 완전히 들어간 것, 오른→왼쪽 = 닿은 것) · Shift = 추가 · 선택한 것을 끌면 함께 이동 · 우클릭 누른 채 끌기 = 화면 이동\n" +
            "· 여러 개 선택: Shift+클릭(맵·목록), Ctrl/⌘+A 전체. 화살표·Delete·Ctrl/⌘+D·C·V 가 선택한 모든 오브젝트에 적용됩니다.\n" +
            "· 빈 곳 끌기 / 우클릭 끌기 / Space + 끌기: 화면 이동 · 휠: 확대·축소 · F: 화면 맞춤 · G: 격자\n" +
            "· 1 지형 · 2 물리 상자 · 3 스폰 · 4 움직이는 발판 · 5 점프대 · 6 피해 구역 · 7 바람 · 8 트리거 영역 · V/Esc 선택 도구\n" +
            "· 화살표: 스냅 간격만큼 이동 (Shift ×5) · [ ]: 15°/5° 회전 · Delete: 삭제\n" +
            "· Ctrl/⌘ + Z 되돌리기 · Shift+Z/Y 다시 · D 복제 · C/V 복사·붙여넣기 · S 저장 · Shift+S 다른 이름 · O 열기 · N 새 맵 · E 내보내기 · F5 테스트 플레이\n\n" +
            "게임과 같게 동작하는 이유\n" +
            "· 맵은 게임의 MapRoot 가 그리고(같은 스프라이트·충돌체), 게임의 ContentDatabase.ParseMap 이 검증하며, 테스트 플레이는 게임의 GameFlow·Fighter·Projectile 코드가 실행합니다. 이 코드와 Physics2D·Time·레이어 설정은 게임 프로젝트에서 바이트 단위로 복사한 것입니다 (설정 > 게임 런타임).\n" +
            "· '실제 게임으로 실행'은 설치된 게임 빌드 자체를 이 맵만으로 실행합니다.\n\n" +
            "좌표\n" +
            "· 원점은 화면 중앙, 위가 +y. 노란 테두리(±35.56 × ±20) 밖으로 나가면 사망. x, y 는 중심, w, h 는 크기. 플레이어 지름 ≈ 1.24.\n\n" +
            "맵 스크립트 (도구 막대 '맵 스크립트')\n" +
            "2대2 스폰\n" +
            "· 스폰 0 = 주황 1, 1 = 파랑 1, 2 = 주황 2, 3 = 파랑 2. 세 번째·네 번째 스폰을 놓거나 오른쪽 '2대2 스폰'을 켜면 팀원이 각자 스폰에 섭니다.\n" +
            "· 현재 게임은 팀원을 팀 스폰 옆 2.6에 세우므로, 에디터가 맵 스크립트에 자동 블록(teleport)을 넣어 \"싸워라!\" 순간 옮깁니다('준비' 0.55초는 기본 위치). 스폰을 옮기면 블록도 자동으로 갱신됩니다.\n" +
            "· 게임이 Docs/GAME_PATCH_TEAM_SPAWNS.md 대로 수정되고 GameSync 하면, 에디터가 이를 감지해 블록 없이 '준비' 순간부터 반영되게 저장합니다.\n\n" +
            "· 오브젝트에 '이름'을 붙이면 맵 스크립트(\"script\")와 C# 맵 동작(\"behaviors\")에서 다룰 수 있습니다. 트리거 영역(8)은 충돌 없는 사각형으로 on enter/exit/inside 에 씁니다.\n" +
            "· 스크립트 창: 줄 번호, 게임 해석기로 실시간 문법 검사(오류 줄 빨간 표시), 오른쪽 목록에서 이름·이벤트·명령·예시를 눌러 넣기. Tab = 공백 2칸.\n" +
            "· 스크립트는 게임 명령만 실행하며(파일·네트워크·C# 접근 없음) 편집 화면에서는 멈춰 있고, 테스트 플레이와 실제 게임에서 게임의 해석기가 실행합니다. 실행 상태는 테스트 플레이 위쪽 줄에 나옵니다.\n" +
            "· 문법·명령 전체: 게임 프로젝트 Docs/MAP_SCRIPTING.md\n\n" +
            "현재 게임 구조상 제한\n" +
            "· 배경: 게임은 모든 맵에 같은 배경색을 써서, 에디터의 배경은 미리보기와 파일의 background 항목에만 저장됩니다.\n" +
            "· 움직이는 발판·점프대·피해 구역·바람 같은 게임 내장 기믹은 모두 편집할 수 있습니다.\n" +
            "· 로프·파괴되는 상자 등 게임에 없는 오브젝트는 만들 수 없습니다. 상자 + 발판은 최대 120개.";

        private void DrawHelp()
        {
            var r = ModalRect(860, 640);
            if (Window(r, "도움말")) { CloseModal(); return; }
            float x = r.x + 18, w = r.width - 36, y = r.y + 54;
            var view = new Rect(x, y, w, r.yMax - y - 14);
            float h = EUi.Area.CalcHeight(new GUIContent(HelpText), w - 20);
            _modalScroll = GUI.BeginScrollView(view, _modalScroll, new Rect(0, 0, w - 18, h + 10));
            var style = new GUIStyle(EUi.Area) { fontSize = 13, normal = { textColor = new Color(0.85f, 0.87f, 0.92f) } };
            GUI.Label(new Rect(0, 0, w - 20, style.CalcHeight(new GUIContent(HelpText), w - 20)), HelpText, style);
            GUI.EndScrollView();
        }

        private void DrawConfirm()
        {
            var r = ModalRect(520, 190);
            EUi.Fill(r, EUi.PanelColor);
            EUi.Frame(r, new Color(1f, 1f, 1f, 0.14f));
            float y = r.y + 22;
            Note(_confirmText ?? "", EUi.Label, r.x + 20, r.width - 40, ref y);
            float bw = 140;
            if (EUi.Btn(new Rect(r.xMax - 20 - bw, r.yMax - 50, bw, 32), "취소")) { _modal = Modal.None; return; }
            if (!string.IsNullOrEmpty(Doc.FilePath) && EUi.Btn(new Rect(r.xMax - 20 - 2 * bw - 8, r.yMax - 50, bw, 32), "저장 후 계속", false, true, EUi.ButtonOn))
            {
                var then = _confirmYes;
                _modal = Modal.None;
                if (SaveTo(Doc.FilePath)) then?.Invoke();
                return;
            }
            if (EUi.Btn(new Rect(r.x + 20, r.yMax - 50, bw, 32), "버리고 계속", false, true, EUi.ButtonBad))
            {
                var then = _confirmYes;
                _modal = Modal.None;
                _savedJson = _committed = Doc.ToJson(true);
                _pendingCommit = false;
                then?.Invoke();
            }
        }
    }
}
