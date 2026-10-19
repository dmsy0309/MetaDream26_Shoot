# Metadream Shoot — ROUNDS 리버싱 기반 재구현 개발 보고서

작성일: 2026-10-18 (Asia/Seoul) · Unity 6000.6.6f1 (6.6) · URP 2D · Input System 1.20
지시서: `~/Documents/ChatGPT/REA/ROUNDS_REVERSE_DEVELOPMENT.md`

## 1. 결과 요약

| 단계 (지시서 §8) | 상태 | 비고 |
|---|---|---|
| A. 동작 명세 보완 | 부분 완료 | 코드 경로 추가 추적 완료. 직렬화 자산은 타입 트리가 없어 미복원 (UNKNOWN-01/03/06 유지) |
| B. 로컬 2인 전투 | 완료 | 이동·점프·벽 점프·조준·사격·재장전·방어·피격·사망·경계 처리 |
| C. 매치·카드 루프 | 완료 | 포인트/라운드 분리, 초기 선택, 라운드 패배자만 선택, 매치 종료, 재대결 |
| D. 맵·전투 효과 | 완료(자체 콘텐츠) | 자체 맵 6종, 맵 슬라이드 전환, 상자 물리, 반사/다중탄/흡혈/재생/부활/관통 등 카드 19종 |
| E. 온라인 | 완료(자체 프로토콜) | 방 만들기/참가, LAN 방 검색, 방 코드, 호스트 권위 + 30Hz 스냅샷. 원작 Photon 서비스와는 호환되지 않음 (§10) |

원작의 그래픽·음원·이름·카드/맵 데이터는 사용하지 않았다. 모든 그래픽은 코드로 생성한 도형이다.

## 10. 추가 개발 (2026-10-18 2차): 2대2 · 방 만들기/참가 · Windows 빌드

### 10.1 2대2

- 슬롯 i는 팀 i % 2. 매치 규칙(`MatchStateMachine`)은 원래 팀 단위라 그대로 사용한다. 초기 선택은 4명 전원, 라운드 패배 시 진 팀 2명이 순서대로 고른다.
- 모드: 로컬 2대2 (P1·P2 + CPU 팀원, 게임패드 3·4가 연결되면 사람), CPU 관전 2대2, 온라인 2대2.
- **개발 결정**: 아군 탄은 통과한다(아군 피해 없음). 반사된 자기 탄은 여전히 자신에게 맞는다. 팀원은 팀 스폰에서 중앙 쪽으로 2.6 떨어져 시작한다.
- 점수·배너는 팀 이름(주황 팀/파랑 팀)으로, 카드 목록은 팀별·플레이어별로 표시한다.

### 10.2 방 만들기 / 참가

| 항목 | 구현 |
|---|---|
| 구조 | 호스트 권위형. 호스트가 전체 시뮬레이션을 돌리고, 참가자는 입력만 보내고 상태를 받아 그린다 (`Online/NetHostSession.cs`, `Online/NetClientSession.cs`) |
| 전송 | TCP (Nagle 끔), 길이 접두 프레임, 메인 스레드 폴링 (`Network/NetTransport.cs`) |
| 주기 | 스냅샷 30Hz (원작 `NetworkSettings.Start`의 SerializationRate=30과 같은 값), 입력 최대 60Hz |
| 스냅샷 | 단계·점수·선택 카드·배너·맵(전환 포함)·플레이어 상태·탄·상자·이펙트 이벤트 (`Network/NetProtocol.cs`) |
| 입력 | 연속값은 최신, 눌림 이벤트는 다음 호스트 프레임까지 누적 → 누락 없음 |
| 방 검색 | UDP 브로드캐스트 47777 포트, 1초 간격 |
| 방 코드 | IPv4 + 포트 48비트를 Crockford base32 10자 (`XXXXX-XXXXX`). 코드, `IP`, `IP:포트` 모두 입력 가능 |
| 포트 | TCP 7777 (사용 중이면 7778~7786) |
| 끊김 | 게임 중 참가자가 나가면 그 자리를 CPU가 이어받음. 호스트가 나가면 참가자는 알림 후 타이틀로 |
| 제한 | 게임 진행 중에는 새 참가 거절. 호스트 대기실에서 1대1↔2대2 전환, 빈 자리는 시작 시 CPU |

원작 직렬화 필드 순서를 따른 `PlayerSyncPackage`(1차)는 테스트용으로 남겨 두었고, 실제 대전은 위 자체 스냅샷을 쓴다. 원작 서비스와의 접속 호환은 목표가 아니다.

### 10.3 Windows

- `unity build --target StandaloneWindows64 --execute-method Metadream.Shoot.EditorTools.ProjectSetup.BuildWindows` (Mono 백엔드; macOS에서는 Windows용 IL2CPP 교차 빌드 불가).
- 결과: `Builds/Windows/MetadreamShoot/MetadreamShoot.exe` (PE32+ x86-64), 배포용 `Builds/Windows/MetadreamShoot-Windows-x64.zip` (39MB, 빌드 백업 폴더 제외).
- Windows 실기 실행은 이 Mac에서 검증하지 못했다.

### 10.4 검증

| 검증 | 결과 |
|---|---|
| EditMode | 39/39 — 방 코드, 접속 대상 파싱, 프레임 분할 수신, 입력/스냅샷/대기실 직렬화, 입력 누적, **실제 TCP 루프백 연결·송수신·끊김 감지**, 2대2 패배 팀 선택 |
| PlayMode | 4/4 — 1대1·2대2 CPU 매치 완주(카드 수 불변식), 피격·방어 반전, 아군 탄 통과 |
| 2프로세스 네트워크 | macOS 빌드 2개(호스트 2대2 + 참가자, 양쪽 자동 플레이)를 localhost로 연결: 약 60초간 스냅샷 1,653개 / 입력 3,303개 전부 수신, 7포인트·카드 8회(참가자 선택 포함), 참가자 쪽 점수·카드가 호스트와 일치 |
| 화면 | Editor를 참가자로 빌드 호스트에 접속: 방 만들기 대기실, LAN 방 목록, 참가 대기실, 원격 카드 선택, 2대2 전투 캡처 (`Docs/screenshots/09~14`), 콘솔 오류·경고 0 |
| 빌드 | macOS 성공(79MB), Windows x64 성공(106MB) |

### 10.5 알려진 한계

- 인터넷 너머 대전은 호스트 공유기의 TCP 7777 포트 포워딩이 필요하다(중계 서버 없음). 같은 네트워크(LAN)에서는 바로 된다.
- 참가자 화면은 호스트 상태를 받아 그리므로 왕복 지연만큼 조작감이 늦다(예측 보정 없음). LAN 기준으로 설계했다.
- 첫 실행 시 macOS/Windows 방화벽 허용 창이 뜬다. 서명되지 않은 앱이라 macOS에서는 처음에 우클릭 → 열기가 필요할 수 있다.
- 같은 컴퓨터에서 참가자 두 명이 방 검색을 동시에 쓰면 UDP 포트 공유가 OS에 따라 실패할 수 있다(코드 입력으로는 참가 가능).

### 10.6 한글 폰트 (Windows 깨짐 수정)

- 원인: UI가 OS 설치 폰트를 이름으로 찾는 `Font.CreateDynamicFontFromOSFont`에 의존해, Windows에서 한글 글리프를 가진 폰트를 잡지 못하면 글자가 깨졌다.
- 수정: Noto Sans KR Regular/Bold(SIL OFL 1.1, notofonts/noto-cjk SubsetOTF)를 `Assets/Reimplementation/Resources/Fonts/`에 동봉하고 `UiKit`이 이를 우선 사용한다. OS 폰트는 동봉 폰트를 읽지 못할 때만 쓴다. 굵은 글씨는 Bold 폰트 파일을 사용한다.
- 라이선스: `Licenses/NotoSansKR-OFL.txt`를 빌드할 때 Windows는 exe 옆, macOS는 `Contents/Resources`에 복사한다.
- 검증: 빌드 로그 `[UI] font=NotoSansKR-Regular bold=NotoSansKR-Bold`, Windows 데이터(`resources.assets` 9.4MB)에 폰트 포함 확인, macOS 빌드 화면 캡처(`-capture <폴더>` 옵션 추가). 폰트 폭 변화로 겹친 타이틀 메뉴 설명 문구를 별도 열로 분리했다.
- Windows 실기 화면은 이 Mac에서 확인하지 못했다. Windows에서 `MetadreamShoot.exe -capture C:\\temp\\cap`로 실행하면 2·5·9초 화면이 저장된다.

### 10.7 전용 서버 (Linux / Windows)

- 구성: `Online/DedicatedServer.cs`(설정·자동 시작·재대결·빈 서버 관리·콘솔 명령) + `NetHostSession` 전용 모드. Dedicated Server 빌드(`UNITY_SERVER`) 또는 `-server` 인자로 메뉴/HUD 없이 실행된다.
- 동작: 서버는 플레이하지 않고 모든 자리를 참가자에게 연다. 첫 참가자가 방장(즉시 시작, 1대1·2대2 전환). `minPlayersToStart`명이 모이면 카운트다운 후 자동 시작, 빈 자리는 CPU. 나간 자리는 CPU가 이어받고 새 참가자가 그 자리로 중간 참가 가능. 매치 후 자동 재대결, 모두 나가면 대기실.
- 프로토콜 v2: 대기실 정보에 전용 서버 여부·방장·카운트다운·규칙 추가, StartRequest/TeamSizeRequest 메시지 추가. 참가 확정(Welcome) 전 연결에는 스냅샷을 보내지 않는다.
- 구동기: `ServerLauncher/`의 `server.json`, `start-server.sh`, `start-server.bat`, `open-firewall.bat`, `metadream-shoot.service`(systemd), `Dockerfile`, `README-SERVER.md`가 빌드 때 출력 폴더로 복사된다.
- 빌드: `ProjectSetup.BuildServerLinux`(IL2CPP, `com.unity.toolchain.macos-arm64-linux` 1.1.0으로 Mac에서 교차 컴파일; `-mono`로 Mono 가능), `ProjectSetup.BuildServerWindows`(Mono, 콘솔 exe).
- 배포본: `Builds/Server/MetadreamShootServer-Linux-x64.tar.gz`(39MB), `Builds/Server/MetadreamShootServer-Windows-x64.zip`(39MB).
- 검증: Docker(Ubuntu 22.04, x86-64 에뮬레이션)에서 IL2CPP 서버 실행 → macOS 클라이언트 2개로 자동 시작·매치·이탈 시 CPU 대체·재대결·전원 이탈 시 대기실, 방장 즉시 시작, 진행 중 CPU 자리 중간 참가, 콘솔 `status/lobby/teamsize/quit`, SIGTERM 정상 종료를 확인. 압축 해제한 배포본으로 재확인. EditMode 42/42, PlayMode 4/4.
- 발견: Mono Linux 서버는 Apple Silicon의 x86-64 에뮬레이션(Rosetta와 QEMU 모두)에서 Mono JIT 단정(`x86-codegen.h:410`)으로 기동 실패. JIT가 없는 IL2CPP로 전환해 해결. 실제 x86-64 Linux에서 Mono가 실패하는지는 확인하지 않았다.
- 미검증: Windows 전용 서버는 이 Mac에서 실행할 수 없어 빌드(PE32+ console)와 구성만 확인했다. 서버 로직은 Linux 서버와 같은 코드다.

### 10.8 튜토리얼 · 설정/디버그 모드 · 콘텐츠 모듈화 · 메인 호스팅 서버 (2026-10-18 3차)

- 메뉴: 로컬 1대1·로컬 2대2 삭제. 튜토리얼, CPU와 대전, CPU 관전 1대1/2대2, 방 만들기, 방 참가, 설정, 게임 종료(8개).
- 튜토리얼(`Tutorial/TutorialController.cs`): 전용 맵 `training`에서 이동 → 점프 → 사격(더미 명중) → 재장전 → 방어(더미가 천천히 사격) → 낙사/경계 → 카드 선택 → 규칙 설명 → CPU 실전 → 완료 10단계. 완료 기록은 PlayerPrefs `tutorial.completed`. 원작 ROUNDS에는 이런 튜토리얼이 없으며 이 재구현의 임의 콘텐츠다.
- 콘텐츠 모듈화: 카드 22장(기존 19 + 폭발탄·충격파 방어·독탄)과 맵 7개(훈련장 포함)를 `Resources/Content/{Cards,Maps}/*.json`으로 분리, `Content/ContentDatabase.cs`가 읽고 검증한다. 실행 파일 옆 `Content` 폴더로 빌드 없이 추가/덮어쓰기. 새 능력은 `[CardEffect("id")]` 클래스 하나(`Abilities/`)로 추가하고 카드 JSON `effects`에서 참조. 새 3장의 효과(폭발, 방어 충격파, 중독)는 원작 카드가 아니다.
- 문서: `Docs/EXTENDING.md`(AI 에이전트용 맵/카드/능력 추가 가이드), 루트 `AGENTS.md`.
- 개발자 디버그 모드: 설정 또는 `-debug`. 패널(F1), 월드 표시(경계·지형·상자·스폰·속도/조준·탄), 콘텐츠 오류 보고, F2 다음 맵 · F3 카드 지급 · F4 적 처치 · F5 콘텐츠 리로드 · F6 무적 · F7 슬로모션(오프라인 전용).
- 프로토콜 v3: Hello에 콘텐츠 해시를 실어 다르면 거절(카드 번호/맵 번호 불일치 방지).
- 전용 서버 `server.json`: `rules`(포인트·라운드·카드 수·카드 선택 여부·맵 풀·카드 풀·시작 능력치 `stats`) + `masterServer/masterHost/masterPort/publicAddress/contentDir/debugLog`. 범위 밖 값 보정, 알 수 없는 맵/카드 id는 `CONFIG WARNING`. 콘솔 `rules/master/content` 명령 추가.
- 메인 호스팅 서버: `MasterServer/master_server.py`(Python 표준 라이브러리, UDP 47800 JSON, HTTP 47801 `/health`·`/servers`, 15초 TTL). 방장과 전용 서버는 5초마다 IP·포트·접속 인원·최대 인원 등을 `register`, 방 참가 화면은 3초마다 `query`하고, 서비스는 패킷을 보낸 쪽에 전체 서버 목록을 돌려준다. 주소 `100.64.0.10:47800`은 `Network/MasterServer.cs`에 하드코딩. 설치/부팅 등록: `install.sh`(systemd), 배포: `deploy.sh`.
- 발견한 버그: 튜토리얼 `OnGUI`의 `Begin()` 호출이 `UiKit.Begin`이 아니라 같은 이름의 정적 메서드 `TutorialController.Begin`으로 해석되어 매 프레임 튜토리얼이 재시작됐다(배치 모드 테스트에서는 OnGUI가 돌지 않아 통과). 진입점을 `Launch()`로 바꿔 해결하고 빌드 화면으로 재확인.

- 검증: EditMode 50/50, PlayMode 6/6, 마스터 서비스 자체 테스트 5/5. 로컬 마스터로 macOS 방장 등록·목록 수신·방 닫힘 해제, Docker(Ubuntu 22.04) IL2CPP 서버 등록(인원 0/2 → 2/2·게임 중 반영) 후 클라이언트 2명 매치·전원 이탈 시 대기실, 외부 카드 1장을 넣은 클라이언트의 콘텐츠 불일치 거절을 확인. 화면: `Docs/screenshots/v3-*.png`.
- 미완료: 실제 메인 호스팅 서버(100.64.0.10) 배포와 Hermes 지시는 SSH 공개키 인증이 막혀 보류(이 Mac에 키가 없었음). `MasterServer/deploy.sh --hermes`로 진행한다.

### 10.9 닉네임 · 리플레이 · JSON 가이드 (2026-10-18 4차)

- 닉네임: 방 만들기/방 참가 전에 닉네임 화면(최대 12자, 저장됨). 프로토콜 v4: Hello에 닉네임과 PC 이름(`Environment.MachineName`)을 싣고, 방장이 슬롯 이름으로 사용(중복 시 숫자 붙임).
- 리플레이 녹화: 방장(P2P) 또는 전용 서버가 클라이언트로 보내는 30 Hz 스냅샷을 그대로 기록(`Replay/ReplayRecorder`), 게임마다 `Replays/<날짜>_<방>.mdreplay`(gzip) 저장 후 메인 호스팅 서버 `POST /replays`로 전송. 메타데이터: 생성 기기(이름·사용자·OS·기기 ID·IP), 참가자 닉네임·종류·PC 이름·주소, 점수. 업로드 실패 시 `.pending` 표시를 남기고 다음 실행 때 재시도. tailnet 주소 실패 시 공개 주소(Funnel)로 재시도.
- 재생: 디버그 모드에서 타이틀에 "리플레이" 항목. 목록 재생, 파일 선택 창(Windows 공용 대화상자, macOS AppleScript, Linux zenity) 또는 경로 붙여넣기로 외부 파일을 `Replays` 폴더로 가져와 재생. 재생은 온라인 클라이언트와 같은 미러 경로(`GameFlow.ApplyMirror`)를 써서 화면이 게임과 같다. UI: 정지, 0.25~16배속, ±10초, 진행 바 클릭/드래그 이동(점수 변화 표시), 휠/버튼 줌, 우클릭 드래그 이동, 캐릭터 좌클릭 정보(닉네임·PC·체력·탄·방어·카드).
- 메인 호스팅 서버: 리플레이 수신·검색(`/replays.html?q=`)·다운로드. tailnet 밖 공개는 Hermes가 Tailscale Funnel(HTTPS 10000)로 시도했으나 sudo 권한이 없어 관리자 명령 1회가 남음.
- 문서: `Docs/CONTENT_JSON_GUIDE.md`(맵/스킬 JSON 작성 가이드).

### 10.10 원작 카드·맵, 기믹, 이탈 규칙, 그래픽 (2026-10-18 5차)

- git: 프로젝트를 git 저장소로 만들고 기준 커밋(`05b1460`) 뒤에 작업 단위로 커밋. `Library/Builds/Logs/UserSettings` 등은 `.gitignore`.
- 원작 콘텐츠 추출: ROUNDS 에셋(Unity 2022.3.34f1)에는 타입 트리가 없어서 UnityPy + TypeTreeGenerator(게임 `Managed/*.dll`)로 MonoBehaviour를 해석. CardInfo 79개(카드 이름·설명·희귀도·카드 수치 줄)와 같은 프리팹의 Gun/CharacterStatModifiers/Block 값을 modifiers로 변환해 `rounds_*.json` 69장 생성(미완성·재현 불가 10장 제외). Map 씬 70개 중 BoxCollider2D/Rigidbody2D/SpawnPoint(TEAMID)/MoveSequence로 표현 가능한 33개를 `rounds_NN.json`으로 변환(로프·파괴 상자·물리 상자 60개 초과 맵 제외, 경기장 밖 장식 제거).
- 카드 기믹: `Abilities/GimmickEffects.cs`에 효과 20종(유도, 성장, 감속/기절, 밀치기, 시한폭탄, 체력 소모 사격, 감속/흡수 오라, 방어 시 폭발·끌어당김·감속·회복·순간이동·돌진·재장전·강화, 탄 소진 시 방어, 피해 시 재장전·방어 초기화·가속). 모두 카드 JSON `effects`로 설정.
- 맵 기믹: 회전(`r`), 움직이는 발판(`movers`, 박스 스냅샷으로 온라인 동기화), 점프대(`pads`), 피해 구역(`hazards`), 바람(`winds`). 예시 맵 3개(용암 승강장·바람 협곡·가시 구덩이). 원작 MoveSequence는 movers로 근사(속도 6, timeAtPos → wait).
- 서든 데스: 한 포인트 45초 초과 시 모두 점점 체력 감소(복잡한 원작 맵에서 CPU 대전이 끝나지 않던 문제 해결).
- 이탈 규칙(프로토콜 v5): 게임 중 나간 자리는 봇 없이 비워 두고(SlotKind.Disconnected), 같은 PC 이름으로 다시 접속하면 그 자리로 복귀. 한 팀의 사람이 모두 나가면 남은 팀이 즉시 승리(MatchStateMachine.Forfeit).
- 재대결 제거: 결과 창(승리/패배, 팀별 참가자·카드 수)에서 확인하면 메인 화면으로. 방장은 방을 닫고, 참가자는 연결을 끊고, 전용 서버는 `rematchDelay`초 뒤 대기실로. (`-demo` 등 테스트용 명령줄에서만 자동 재대결 유지)
- 리플레이: 디버그 UI 토글(버튼/Tab), 줌 후 WASD(Shift 빠르게) 자유 이동.
- 그래픽: 캐릭터 외곽선·하이라이트·조준을 따라가는 눈·깜빡임·저체력 경고·감속/중독 색, 총구 섬광, 탄 광채·코어·긴 꼬리, 명중 섬광, 사망 폭발(링·플래시·흔들림), 회전 파편. 카드: 프레임·희귀도 띠·문양(영문 머리글자)·효과 표시점·ROUNDS 배지·선택 카드 빛남. FxType에 Muzzle/Death 추가.
- 검증: EditMode 57/57, PlayMode 6/6. 실제 빌드로 2대2 방장+참가자 3명: 이탈 시 봇 없음 → 같은 PC 재접속 복귀 → 한 팀 전원 이탈 시 즉시 승리 → 참가자 결과 확인 후 메인 화면을 확인.

### 10.11 공인 IP 메인 서버 · 상자 물리 · 카드 새로고침 (2026-10-19)

- 메인 서버 주소를 tailnet(100.64.0.10)이 아닌 공인 IP `203.0.113.10`로 하드코딩(서버 목록 UDP 47800, 리플레이 HTTP 47801). 공유기 포트 포워딩으로 두 포트가 외부에서 응답하는 것을 확인.
- 상자: 질량 2·w·h → 0.18·w·h(0.25~6), 마찰 0.5 → 0.25, 감쇠 감소, 탄 충격 0.05 → 0.09·피해, 캐릭터가 이동 방향의 상자를 밀어내는 힘 추가.
- 카드 새로고침(프로토콜 v6): 카드마다 아래 새로고침 버튼, R/패드 Y는 선택 카드. 하단에 남은 횟수. 규칙 `rerollsAtStart`(기본 3)·`rerollsPerRound`(기본 0), 방 대기실에서 −/+로 변경, 전용 서버는 `server.json rules`·`-rerolls`·콘솔 `rerolls`. 입력 패킷에 새로고침 바이트, 스냅샷 끝에 남은 횟수(이전 리플레이는 0으로 읽음), 대기실 정보 끝에 설정값.

## 2. 이번 세션의 REA 증거

| ID | 내용 |
|---|---|
| `ev_a8c422c0…2086e` | `inspect_managed_artifact` — SHA-256 `4509e77d…aa8f2a`, MVID `41353f7d-…`, unity-mono/CIL/AnyCPU (지시서 E01과 동일 해시) |
| `ev_71cc655c…cb3b8` | `inspect_managed_members` (22개 타입 선택), coverage complete |
| `ReverseAnalysis/evidence/rea-session-evidence.json` | 위 기록을 `export_evidence_bundle`로 저장 (4 records) |
| `ReverseAnalysis/evidence/extra-il-*.txt` | 지시서 E02 번들 + 설치된 REA CIL 디코더로 추가 디코딩한 보조 자료 (`ReverseAnalysis/tools/decode-extra-il.mjs`) |

설치본 DLL 해시가 지시서와 같아 기존 E01/E02를 재사용했다. 자산 파일 헤더(`level0` 오프셋 0x40)의 type tree 플래그가 0이어서 범용 자산 파서로 MonoBehaviour 직렬화 값을 읽을 수 없음을 확인했다.

## 3. 새로 확정한 동작 (관찰, CIL 토큰)

- **사격 쿨다운**: `Gun.Update`에서 `sinceAttack += dt * attackSpeedMultiplier`, `Gun.Attack`은 `sinceAttack >= usedCooldown`일 때만 발사. `attackSpeed`는 초 단위 쿨다운 (`0x06000294`, `0x06000298`).
- **탄약 소비**: `FireBurst`가 투사체마다 `BulletInit(useAmmo)` → `GunAmmo.Shoot`을 호출 → **투사체 1발당 탄약 1** (`<FireBurst>d__93.MoveNext`, `0x0600029d`). 버스트는 `clamp(bursts,1,100)`회, `timeBetweenBullets` 간격.
- **자동 재장전**: 탄창이 비면 `reloadCounter < 0`에서 충전, 부분 탄창은 `freeReloadCounter > ReloadTime`이고 `automaticReload`면 충전 (`0x06000791`).
- **방어 성공**: `TryBlockMe`가 `sinceBlock < 0.3`이면 `Block.blocked` 호출 → 탄 속도 ×(-1), 발사자를 피격 제외 목록에서 제거(되돌아온 탄에 맞을 수 있음), 방어자를 0.5초 면역(`holdPlayerFor`) (`0x0600006e`, `0x0600006c`, `0x06000463`).
- **추가 중력**: `Gravity.FixedUpdate` — `airTime = min(sinceGrounded, sinceWallGrab)`, `down * pow(airTime, exponent) * gravityForce` (Force 모드) (`0x06000286`).
- **힘 적용**: `PlayerVelocity.AddForce` Force 모드 ×0.02, Impulse ×1, 이후 질량으로 나눔 (`0x060008d1`). 이동 드래그 `v -= v·0.001·extraDrag·multiplier` (`0x06000428`).
- **경계**: 화면 경계 x ±35.56, y ±20. 밖에 있으면 0.1초마다 안쪽으로 밀고 피해 51, 방어 중이면 피해 없이 더 강하게 밀어냄 (`OutOfBoundsHandler.LateUpdate`).
- **넉백 공중 시간**: 위쪽 힘은 `sinceGrounded -= force.y·0.003`, `[-0.5, 100]` clamp (`HealthHandler.TakeForce` `0x060002af`).
- **점프 횟수 특이점**: `PlayerJump.Jump` CIL을 문자 그대로 읽으면 비-벽 점프마다 `currentJumps = jumps`로 되돌린다. 추가 점프 카드에서 무한 점프가 되는 해석이라 **표준 방식(지상에서만 회복)으로 구현**하고 UNKNOWN-02에 남겼다.

## 4. 구현 대응표

| 원작 영역 | 재구현 | 근거 수준 |
|---|---|---|
| `GM_ArmsRace` 점수·전환 | `Core/MatchRules.cs` (`MatchStateMachine`) | 관찰 + 상태 이름은 개발 결정 |
| `HealthHandler`, `Block`, `GunAmmo`, `Gun.Attack` | `Combat/CombatRules.cs` | 관찰 |
| `PlayerMovement/Jump/Velocity`, `Gravity` | `Player/MotionModel.cs`, `Player/Fighter.cs` | 공식 관찰, 계수는 임시값 |
| `MoveTransform`, `ProjectileHit`, `RayHitReflect` | `Combat/Projectile.cs` | 관찰 + 반사 후 자기 피격은 개발 결정 |
| `CardChoice`, `ApplyCardStats`, `ConfigureMassAndSize` | `Cards/CardRules.cs`, `Cards/StatComposer.cs` | 관찰 |
| 카드 목록 | `Cards/CardCatalog.cs` (자체 19종) | 개발 콘텐츠 |
| `MapManager`, 맵 | `Maps/*` (자체 6종, 슬라이드 전환) | 개발 콘텐츠 |
| `SyncPlayerMovement`, `SyncProjectile` 직렬화 | `Network/Network.cs` | 필드 순서·Δt clamp 관찰 |
| `GeneralInput` | `Player/InputSources.cs` | 개발 결정 (키 배치) |

## 5. 임시값 (원작 확인값 아님)

| 값 | 사용값 | 이유 |
|---|---|---|
| 이동 force / extraDrag | 5200 / 80 | 지상 최고속 ≈ 12 u/s가 되도록 시뮬레이션 |
| 점프 upForce | 2200 | 정점 ≈ 5.7 u, 체공 ≈ 1.2 s |
| gravityForce / exponent | 25 / 1.2 | 추가 중력 형태만 원작 공식 |
| 투사체 기본 피해 / 속도 | 55 / 70 u/s | 기본 체력 100에서 2발 처치 |
| 공격 쿨다운 | 0.3 s | — |
| 방어 쿨다운 | 4 s | — |
| 기본 점프 횟수 | 1 | — |
| 경계 밀어내기 | 45 (방어 시 70) u/s | 원작 상수 200/400은 미확인 드래그와 짝이라 그대로 쓰면 화면을 가로지름 |
| 카드 제시 수 | 5 | `CardChoice.picks=6`은 제시 수가 아님 (UNKNOWN-03) |

## 6. 개발 결정

- 충돌 해소는 Unity 2D 물리 솔버에 맡기고, 속도 갱신(중력·추가 중력·이동·드래그·점프·넉백)은 위 공식으로 직접 계산한다. Rigidbody2D 기본 중력은 쓰지 않는다 (`gravityScale = 0`).
- 동시 사망(살아있는 팀 0): 점수 없이 같은 포인트를 다시 진행 (UNKNOWN-04).
- 카드 후보가 고갈되면 제시 수를 줄이고, 0장이면 선택을 건너뛴다.
- 다중 투사체 각도는 균등 부채꼴 (`getShootRotation` 미복원).
- 반사된 탄은 발사자에게도 맞는다 (방어 반전의 관찰된 동작을 반사에도 적용).
- 맵 순서는 셔플 순환.
- 매치 종료 라운드에서는 카드 선택 없이 GameOver로 간다 (상태 머신 해석과 일치).

## 7. 검증 결과

| 검증 | 방법 | 결과 |
|---|---|---|
| 컴파일 | `unity run` batchmode | 오류 0 |
| 규칙 단위 테스트 | `unity test --mode EditMode` | 31/31 통과 (지시서 §9 표의 포인트·라운드·선택 대상·방어 0.299/0.300/0.301·쿨다운 2.5s·재장전 1.0s·탄약 [1,90]·HP 경계·추첨 경계·카테고리 제약·순서 의존·중복 이벤트·직렬화 순서) |
| 매치 시나리오 | `unity test --mode PlayMode` | 2/2 통과 — CPU 대 CPU 매치 완주 후 "초기 1장 + 마지막 라운드를 제외한 패배 라운드당 1장" 불변식, 피격·방어 반전 |
| 에디터 Play 모드 | `unity open` + `unity command` (play/eval/capture/console) | 타이틀·카드 선택·전투·포인트·맵 전환 화면 확인, 콘솔 오류·경고 0 |
| 사람 입력 경로 | Input System 이벤트 주입 | 메뉴, 카드 이동/확정(P1·P2), 이동 11.6 u/s, 점프 13.1 u/s, 사격(탄약 3→2), 방어 창, P2 사격 확인 |
| macOS 빌드 | `unity build --execute-method ProjectSetup.BuildMac` | 성공, 76 MB |
| 빌드 스모크 | `-demo -quick -autoquit 40 -timescale 3` | 7포인트·카드 4회, 예외 0, 정상 종료 |

스크린샷: `Docs/screenshots/`.
테스트 통과는 규칙 구현의 검증이며, 원작과의 화면·물리 감각·온라인 동등성을 뜻하지 않는다.

## 8. 실행 방법

- 앱: `Builds/macOS/MetadreamShoot.app` 실행 → 1 로컬 2인 / 2 CPU와 대전 / 3 CPU 관전.
- 데모: `MetadreamShoot.app/Contents/MacOS/Metadream\ Shoot -demo` (`-quick`, `-seed N`, `-timescale X`, `-autoquit 초`).
- 에디터: `Assets/Scenes/Arena.unity` 열고 Play. 씬 재생성은 메뉴 Metadream ▸ Rebuild Arena Scene.
- 조작: P1 A/D 이동 · W/Space 점프 · WASD 또는 마우스 조준 · F/좌클릭 사격 · G/우클릭/LShift 방어. P2 ←/→ · ↑ 점프 · 방향키 조준 · `.`/Num1 사격 · `/`/Num2 방어. 게임패드 1→P1, 2→P2. Esc 타이틀.
- 테스트: `unity test . --mode EditMode`, `unity test . --mode PlayMode`.
- 빌드: `unity build . --target StandaloneOSX --execute-method Metadream.Shoot.EditorTools.ProjectSetup.BuildMac -o Builds/macOS/MetadreamShoot.app`.

## 9. 남은 미확인 항목

| ID | 상태 | 이번 작업 후 |
|---|---|---|
| UNKNOWN-01 직렬화 기본값 | 미확인 | type tree 없음 확인. 필드 선언 순서 기반 전용 파서가 다음 단계 |
| UNKNOWN-02 이동 감각 | 부분 확인 | 공식 확정, 계수 임시값. 점프 횟수 CIL 특이점 기록 |
| UNKNOWN-03 카드 제시 수·목록 | 미확인 | 자체 카드로 대체 |
| UNKNOWN-04 동시 사망 | 부분 확인 | 무승부 재진행으로 결정 |
| UNKNOWN-05 탄약/방어 우선순위 | 확인 | 투사체당 탄약 1, 방어 판정이 피해보다 먼저 |
| UNKNOWN-06 맵 | 미확인 | 경계 크기만 확인, 맵은 자체 제작 |
| UNKNOWN-07 네트워크 권한 | 미확인 | 온라인 미구현 |
| UNKNOWN-08 UI·연출 | 미확인 | 자체 HUD |
| UNKNOWN-09 부가 기능 | 미착수 | 설정·꾸미기·현지화 없음 |

