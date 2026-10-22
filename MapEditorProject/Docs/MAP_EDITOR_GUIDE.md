> 2026-10-22 배포: 이 작업용 에디터 소스는 게임 복사본의 `MapEditorProject/`입니다. 현재 게임 런타임과 GameSync로 동기화되어 있으며, macOS 설치 위치는 `~/Applications/MetadreamShootMapMaker.app`입니다. 아래 예전 프로젝트 이름 대신 이 폴더에서 CLI를 실행하세요.

# Metadream Shoot Map Maker 사용 설명서

Metadream Shoot의 맵(JSON)을 그림으로 편집하고, 게임과 같은 코드로 테스트한 뒤, 게임에 바로 적용하는 프로그램입니다. macOS(`MetadreamShootMapMaker.app`)와 Windows(`MetadreamShootMapMaker.exe`)용이 있습니다.

## 1. 화면

| 위치 | 내용 |
|---|---|
| 위 도구 막대 | 새 맵 · 열기 · 저장 · 다른 이름으로 · 되돌리기/다시 · **테스트 플레이** · **실제 게임으로 실행** · **게임에 적용 · 내보내기** · 설정 · 도움말 |
| 왼쪽 | 오브젝트 추가 도구(1~7), 보기(격자·스냅·이름표·발판 움직임), 오브젝트 목록 |
| 가운데 | 맵. 노란 테두리(±35.56 × ±20)가 게임 화면이며 그 밖으로 나가면 사망합니다 |
| 오른쪽 | 맵 정보, 배경, 선택한 오브젝트 속성, **검증**(게임 로더 결과) |

## 2. 오브젝트

| 도구 | 게임 JSON | 속성 |
|---|---|---|
| 1 지형 | `solids` | x, y(중심), w, h, r(회전°) |
| 2 물리 상자 | `boxes` | x, y, w, h, r — 플레이어·탄에 밀림 (질량 = 0.18 × 넓이) |
| 3 스폰 | `spawns` | `spawns[0]` 주황 팀, `spawns[1]` 파랑 팀 (2대2 팀원은 중앙 쪽 2.6 옆) |
| 4 움직이는 발판 | `movers` | x, y, w, h, r, 경로(시작점 기준 오프셋, 2개 이상), 속도, 대기 |
| 5 점프대 | `pads` | x, y, w, h, r, 발사 속도 |
| 6 피해 구역 | `hazards` | x, y, w, h, r, 초당 피해 |
| 7 바람 구역 | `winds` | x, y, w, h, r, 가속 fx, fy |
| 8 트리거 영역 | `areas` | x, y, w, h, r, 보이기(show), 색 — 충돌 없음, 맵 스크립트용 |

모든 오브젝트(스폰 제외)에 `name`을 붙일 수 있고, 맵 스크립트와 C# 맵 동작은 이름으로 오브젝트를 다룹니다 (7장).

조작: 클릭 선택(겹치면 다시 클릭) · 끌기 이동 · 흰 점 크기 · 위쪽 원 회전(Shift 15°) · 빈 곳/우클릭/Space 끌기 화면 이동 · 휠 확대 · F 화면 맞춤 · 화살표 이동 · `[` `]` 회전 · Delete 삭제 · Ctrl/⌘+Z/D/C/V/S/O/N/E · F5 테스트 플레이. 발판의 경로 점은 맵에서 끌어 옮길 수 있습니다.

**드래그 선택 도구 (M):** 빈 곳을 좌클릭으로 끌면 상자 안의 오브젝트를 모두 선택합니다. 왼쪽→오른쪽으로 끌면 완전히 들어간 것만, 오른쪽→왼쪽으로 끌면 상자에 닿은 것까지 선택합니다. Shift를 누르면 기존 선택에 더합니다. 선택한 것 중 하나를 끌면 함께 움직이고, 우클릭을 누른 채 끌면 화면이 이동합니다. 어느 도구에서든 Shift+클릭(맵·목록)으로 추가·제외, Ctrl/⌘+A로 전체 선택하며, 화살표·Delete·Ctrl/⌘+D·C·V가 선택한 모든 오브젝트에 적용됩니다.

**2대2 스폰:** `spawns[0]` 주황 1, `spawns[1]` 파랑 1, `spawns[2]` 주황 2, `spawns[3]` 파랑 2 (스폰 색 = 플레이어 색). 스폰 도구로 세 번째·네 번째 스폰을 놓거나 오른쪽 "2대2 스폰"을 켜면 팀원이 각자의 스폰에서 시작합니다. 스폰 속성의 "역할" 버튼으로 순서를 바꿀 수 있습니다.

> 현재 게임(커밋 6f5e870)은 팀의 두 번째 플레이어를 팀 스폰 옆 2.6에 세우고 `spawns[2]` 이후를 배치에 쓰지 않습니다. 그래서 에디터가 맵 스크립트 끝에 자동 블록(`# [Map Maker] 2대2 팀원 개별 스폰` … `끝`)을 넣어, 각 포인트에서 대결이 시작되는 순간("싸워라!") 플레이어 2·3을 각자 스폰으로 옮깁니다. 부활 카드는 게임처럼 죽은 자리에서 부활합니다. 스폰을 옮기면 블록이 자동으로 갱신됩니다. "준비" 0.55초 동안은 기본 위치에 보입니다(스크립트는 움직이기 전의 플레이어를 다룰 수 없음).
>
> "준비" 순간부터 반영하려면 게임 수정이 필요합니다: [GAME_PATCH_TEAM_SPAWNS.md](GAME_PATCH_TEAM_SPAWNS.md). 수정된 게임을 GameSync 하면 에디터가 자동으로 감지해 블록을 넣지 않고(기존 블록은 편집 시 제거) `spawns[2]`·`spawns[3]`만 저장합니다.

## 3. 배경

오른쪽 "배경"에서 위/아래 색(그라데이션)과 프리셋을 고릅니다. 맵 파일에 `"background"` 항목으로 저장되고 에디터 화면에 보입니다.

> 현재 게임 빌드는 모든 맵에 같은 배경색(`Palette.Background`)을 쓰고 이 항목을 읽지 않습니다(알 수 없는 키는 무시되므로 맵은 그대로 불러옵니다). 그래서 게임 화면과 테스트 플레이에는 아직 나타나지 않습니다.

## 4. 게임과 똑같이 동작하는 이유

- 에디터 안의 `Assets/Reimplementation`은 게임 프로젝트의 런타임 코드(`Metadream.Shoot` 어셈블리)를 **바이트 단위로 그대로 복사**한 것입니다. Physics2D·고정 시간 간격(0.02초)·레이어 설정 파일도 같습니다. (설정 창과 상태 표시줄에 원본 커밋과 SHA-256 표시)
- 편집 화면의 맵은 게임의 `MapRoot`가 그립니다(같은 스프라이트·충돌체·질량).
- 검증은 게임의 `ContentDatabase.ParseMap`을 그대로 호출합니다. 오류가 0이면 게임도 이 맵을 불러옵니다.
- **테스트 플레이**는 게임의 `GameFlow`·`Fighter`·`Projectile`·`MapRoot` 코드로 실행합니다(자유 테스트 / CPU와 대전 / CPU 관전 1대1·2대2).
- **실제 게임으로 실행**은 설치된 게임 빌드 자체를 이 맵만으로 실행합니다. 게임 폴더는 바꾸지 않고, 다른 맵을 `"disabled": true`로 끈 에디터 전용 Content 폴더를 `-content`로 넘깁니다.

## 5. 게임에 적용 (내보내기)

"게임에 적용 · 내보내기" 창에서:

1. **게임에 바로 적용** — 게임 위치(`MetadreamShoot.app` 또는 `MetadreamShoot.exe`)를 고르면 실행 파일 옆 `Content/Maps/<id>.json`에 씁니다(macOS는 .app 옆). 기존 파일은 `.bak`으로 보관. 게임이 계산할 콘텐츠 해시도 보여 줍니다. 게임을 다시 시작하면 반영됩니다.
2. **전용 서버 · 다른 Content 폴더** — 서버 실행 파일이 있는 폴더 또는 Content 폴더.
3. **배포 패키지(.zip)** — `Content/Maps/<id>.json` + 설치 안내. 플레이어와 서버 실행 파일 옆에 풀면 됩니다. 온라인은 모두 같은 파일이어야 접속됩니다.
4. **JSON 파일로만 저장** — 게임에 내장하려면 게임 프로젝트의 `Assets/Reimplementation/Resources/Content/Maps/`에 넣고 다시 빌드합니다.

파일 이름은 id(소문자 영문·숫자·_)로 자동으로 맞춥니다. 같은 id면 내장 맵을 덮어씁니다.

## 6. 개발자용 (Unity CLI)

```bash
cd Metadream26_Shoot_MapMaker
# 게임이 바뀌었을 때: 런타임·물리 설정 다시 복사 (게임 프로젝트는 읽기만 함) → 검증
unity run . --no-tail -l /tmp/sync.log -- -executeMethod Metadream.MapEditor.EditorTools.GameSync.SyncFromGame -gameProject <게임 프로젝트>
unity run . --no-tail -l /tmp/verify.log -- -executeMethod Metadream.MapEditor.EditorTools.GameSync.Verify -gameProject <게임 프로젝트>
# 씬 생성, 테스트, 빌드
unity run . --no-tail -l /tmp/setup.log -- -executeMethod Metadream.MapEditor.EditorTools.MapEditorSetup.Setup
unity test . --mode EditMode --output /tmp/edit.xml
unity test . --mode PlayMode --output /tmp/play.xml
unity build . --target StandaloneOSX --execute-method Metadream.MapEditor.EditorTools.MapEditorSetup.BuildMac -o "$PWD/Builds/macOS/MetadreamShootMapMaker.app" --allow-dirty-build
unity build . --target StandaloneWindows64 --execute-method Metadream.MapEditor.EditorTools.MapEditorSetup.BuildWindows -o "$PWD/Builds/Windows/MetadreamShootMapMaker/MetadreamShootMapMaker.exe" --allow-dirty-build
```

`Assets/Reimplementation`은 직접 고치지 말고 GameSync로만 갱신합니다. 앱 실행 인자(검증·자동화): `-open <file>`, `-builtin <id>`, `-sample`, `-select movers:0`, `-modal export`, `-gamepath <path>`, `-installto <Content>`, `-package <zip>`, `-playtest sandbox|dummy|cpu1|teams|cpu|demo|demo2v2`, `-launchgame cpu|cheat|demo`, `-selftest`, `-capture <dir> -captureat 1,3`, `-autoquit <s>`.

로그: macOS `~/Library/Logs/Metadream/Metadream Shoot Map Maker/Player.log`, Windows `%USERPROFILE%\AppData\LocalLow\Metadream\Metadream Shoot Map Maker\Player.log`.

## 7. 맵 스크립트 · C# 맵 동작

게임의 맵 스크립트 기능(게임 프로젝트 `Docs/MAP_SCRIPTING.md`)을 그대로 편집합니다.

- **이름 붙이기:** 지형·상자·발판·점프대·피해 구역·바람·트리거 영역을 선택하고 오른쪽 패널 "이름"에 입력합니다(글자·숫자·_, 숫자로 시작 불가, 맵 안에서 유일). 복제·붙여넣기하면 `이름_2`처럼 자동으로 바뀝니다.
- **트리거 영역(도구 8, `areas`):** 충돌 없는 사각형. 게임에서 보이기(show)와 색(#RGBA)을 정합니다. 숨겨도 트리거는 동작하고, 에디터에서는 항상 점선으로 보입니다.
- **맵 스크립트 창(도구 막대 "맵 스크립트"):**
  - 줄 번호가 있는 코드 편집기. Tab = 공백 2칸.
  - 게임 해석기(`MdsProgram.Parse`)로 실시간 문법 검사. 오류 줄은 빨갛게 표시되고, 오류를 누르면 그 줄로 이동합니다. 오른쪽 "검증" 목록의 스크립트 오류를 눌러도 이 창이 열립니다.
  - 오른쪽 목록에서 이름 붙은 오브젝트, 이벤트(`on start`, `every 5`, `on enter 영역` …), 게임이 허용하는 모든 명령·함수, 예시(차오르는 용암, 운석, 포탑, 스위치 문)를 눌러 넣습니다.
- **C# 맵 동작 탭(`behaviors`):** 게임 빌드에 들어 있는 동작(`spin`, `blink`, `rise`, `turret`, `bounce_area` …)을 고르고 target/area 이름과 값을 정합니다. 검사는 게임의 `MapBehaviorRegistry`가 합니다. 새 종류는 게임 프로젝트에서 C# 파일로 만든 뒤 GameSync 로 가져옵니다.
- **실행:** 편집 화면에서는 스크립트·동작이 멈춰 있습니다. 테스트 플레이와 "실제 게임으로 실행"에서 게임의 해석기가 실행하며, 테스트 플레이 위쪽 줄에 실행 상태(실행 횟수, 변수 값, 멈춘 이유)가 나옵니다.
- 스크립트는 게임이 허용한 게임 명령만 실행합니다(파일·네트워크·C# 접근 없음). 저장 형식은 게임 맵 JSON 의 `"script"`(한 줄에 문자열 하나)와 `"behaviors"`입니다.

