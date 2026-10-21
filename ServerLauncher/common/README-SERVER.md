# Metadream Shoot 전용 서버

화면 없이 게임 서버만 돌리는 프로그램입니다. 서버를 켜 두면 플레이어들은 게임의 **방 참가**에서 접속합니다.
서버 자신은 플레이하지 않으며, 접속한 플레이어 중 첫 번째 사람이 **방장(★)**이 됩니다.

## 요구 사항

| 플랫폼 | 조건 |
|---|---|
| Linux | x86-64, glibc 기반 배포판 (Ubuntu 22.04 컨테이너에서 확인). IL2CPP 네이티브 빌드라 Mono/.NET 설치 불필요 |
| Windows | Windows 10/11 x64. 콘솔 프로그램으로 실행됨 |
| 공통 | 메모리 약 300MB, CPU 1코어 (60fps 시뮬레이션 기준), TCP 7777 인바운드, 메인 호스팅 서버(203.0.113.10, UDP 47800 / HTTP 47801)로 나가는 통신 |

게임 클라이언트와 서버는 같은 빌드(프로토콜 v3)이고, 카드/맵 콘텐츠도 같아야 합니다(콘텐츠 해시 비교).

## 폴더 구성

| 파일 | 설명 |
|---|---|
| `MetadreamShootServer.x86_64` / `MetadreamShootServer.exe` | 서버 실행 파일 (Linux x64 / Windows x64) |
| `server.json` | 설정 파일 |
| `start-server.sh` / `start-server.bat` | 시작 스크립트 |
| `metadream-shoot.service`, `Dockerfile` | (Linux) systemd 서비스 예시, 컨테이너 이미지 |
| `open-firewall.bat` | (Windows) 방화벽 허용 규칙 추가 (관리자 권한) |
| `NotoSansKR-OFL.txt` | 동봉 폰트 라이선스 |

## 실행

**Linux**

```sh
tar xzf MetadreamShootServer-Linux-x64.tar.gz
cd MetadreamShootServer
./start-server.sh
```

**Windows**: `start-server.bat`를 더블클릭합니다. 처음에는 `open-firewall.bat`를 관리자 권한으로 한 번 실행하거나, 방화벽 창에서 허용을 누릅니다.

실행 중인 창에 아래 명령을 입력할 수 있습니다.

| 명령 | 동작 |
|---|---|
| `status` | 인원, 단계, 점수, 포트, 방 코드 표시 |
| `start` | 지금 매치 시작 (빈 자리는 CPU) |
| `lobby` | 진행 중인 매치를 끝내고 대기실로 |
| `teamsize 1` / `teamsize 2` | 1대1 / 2대2 (대기실에서만) |
| `rounds N`, `points N` | 다음 매치의 승리 라운드 수 / 라운드당 포인트 |
| `countdown S` | 자동 시작 카운트다운 초 |
| `rules` | 현재 기본 게임 설정(rules) JSON 출력 |
| `master` | 메인 호스팅 서버 연결 상태 (보낸 패킷, 받은 응답, 목록에 보이는 서버 수) |
| `content` | 불러온 카드/맵 수, 콘텐츠 해시, 콘텐츠 오류 |
| `quit` | 서버 종료 (접속자에게 알림) |

## server.json

서버의 **기본 게임 설정은 `rules` 블록**에서 바꿉니다. 파일을 고친 뒤 서버를 다시 시작하면 적용됩니다.

| 키 | 기본값 | 의미 |
|---|---|---|
| `roomName` | "Metadream 서버" | 방 목록에 보이는 이름 |
| `port` | 7777 | 접속 TCP 포트 |
| `teamSize` | 1 | 1 = 1대1, 2 = 2대2 |
| `minPlayersToStart` | 2 | 이 인원이 모이면 카운트다운 후 자동 시작 (0 = 방장만 시작) |
| `startCountdown` | 10 | 자동 시작 카운트다운(초) |
| `fillWithCpu` | true | 빈 자리를 CPU로 채움. false면 방이 가득 차야 시작 |
| `allowMidJoin` | true | 진행 중인 매치의 CPU 자리로 중간 참가 허용 |
| `rematchDelay` | 8 | 매치 종료 후 결과 화면을 보여 주는 시간(초). 그 뒤 방이 대기실로 돌아가고 참가자는 메인 화면으로 이동합니다 (재대결 없음) |
| `lanBroadcast` | true | 같은 네트워크의 방 목록에 서버 표시 (UDP 47777) |
| `targetFrameRate` | 60 | 서버 시뮬레이션 프레임 상한 (CPU 사용량 조절) |
| `statusInterval` | 30 | 상태 로그 주기(초), 0이면 끔 |
| `masterServer` | true | 메인 호스팅 서버에 5초마다 IP·포트·접속 인원·최대 인원을 보내 모든 클라이언트의 서버 목록에 표시 |
| `masterHost` / `masterPort` | "203.0.113.10" / 47800 | 메인 호스팅 서버 주소(공인 IP, 보통 바꾸지 않음). 리플레이도 같은 주소의 HTTP 47801로 보냄 |
| `publicAddress` | "" | 플레이어가 접속할 주소를 직접 지정 (공인 IP·도메인). 비우면 메인 서버가 본 주소(Tailnet 100.x 등) |
| `contentDir` | "" | 추가 카드/맵 JSON 폴더 (상대 경로는 server.json 기준). 비우면 실행 파일 옆 `Content` 폴더(있을 때) |
| `debugLog` | false | 능력(효과) 발동 등 상세 로그 |
| `saveReplays` | true | 게임마다 리플레이를 실행 파일 옆 `Replays/*.mdreplay`로 저장 |
| `uploadReplays` | true | 저장한 리플레이를 생성 기기·참가자 닉네임·참가자 PC 이름과 함께 메인 호스팅 서버로 전송 (실패하면 다음 실행 때 재시도) |
| `rules.pointsToWinRound` / `rules.roundsToWinGame` | 2 / 5 | 2포인트 = 1라운드, 5라운드 선승 |
| `rules.cardsPerPick` | 5 | 카드 선택 화면에 보이는 카드 수 (1~10) |
| `rules.rerollsAtStart` | 3 | 게임 시작 시 플레이어마다 주는 카드 새로고침 횟수 (0~20) |
| `rules.rerollsPerRound` | 0 | 라운드가 끝날 때마다 모두에게 추가하는 새로고침 횟수 (0~10) |
| `rules.pickTimeLimit` | 15 | 카드 선택 제한 시간(초, 0~120, 0 = 무제한). 시간이 다 되면 선택 중인 카드를 고릅니다. 실행 인자 `-picktime`, 콘솔 `picktime N` |
| `rules.pickPhase` | true | false면 카드 선택 없이 계속 대결 |
| `rules.mapPool` | [] | 사용할 맵 id 목록 (예: `["plateau","cage"]`). 비우면 튜토리얼 맵을 뺀 전체 |
| `rules.cardPool` | [] | 등장할 카드 id 목록. 비우면 전체 |
| `rules.stats.maxHealth` | 150 | 시작 체력 (몸 크기는 그대로, 카드로 늘린 비율만큼 커짐) |
| `rules.stats.bulletDamage` / `bulletSpeed` / `bulletGravity` | 50 / 70 / 1 | 기본 탄 피해 · 속도 · 중력 배율 |
| `rules.stats.attackCooldown` / `maxAmmo` / `reloadTime` | 0.3 / 3 / 1.5 | 연사 간격(초) · 탄창 · 재장전(초) |
| `rules.stats.blockCooldown` | 4 | 방어 재사용 대기(초) |
| `rules.stats.movementSpeed` / `jumps` | 1 / 1 | 이동 속도 배율 · 점프 횟수 |
| `rules.playersNeededToStart` | 2 | 내부 매치 규칙용 (보통 그대로) |

잘못된 맵/카드 id는 시작 로그에 `CONFIG WARNING`으로 표시되고 무시됩니다. 범위를 벗어난 숫자는 안전한 값으로 보정됩니다.

예: 3라운드 선승, 체력 150, 탄창 5발, 맵 두 개만:

```json
"rules": { "roundsToWinGame": 3, "mapPool": ["plateau", "cage"], "stats": { "maxHealth": 200, "maxAmmo": 5 } }
```

(`rules` 안에서 생략한 값은 기본값이 됩니다.)

명령줄로 덮어쓸 수 있습니다: `-port 7800 -teamsize 2 -name "금요일 방" -minplayers 4 -countdown 15 -rounds 3 -points 2 -cards 3 -rematch 10 -fps 60 -status 60 -public 203.0.113.10 -master host:port -content ./Content -nofill -nomidjoin -nolan -nomaster -noreplay -noupload -debug`.
다른 설정 파일은 `-config 경로` 또는 환경 변수 `METADREAM_CONFIG`로 지정합니다.

## 플레이어 접속

- **서버 목록(메인 호스팅 서버)**: 게임 → 방 참가 → 목록 맨 위의 `[서버]` 항목 선택. 서버와 플레이어 모두 Tailscale(tailnet)에 연결돼 있으면 바로 보입니다.
- **같은 네트워크**: 게임 → 방 참가 → 목록에서 서버 선택.
- **인터넷**: 게임 → 방 참가 → `서버주소:7777` 입력 (예: `203.0.113.10:7777`). 서버 쪽 방화벽과 공유기에서 TCP 7777을 열어야 합니다. 클라우드 서버라면 보안 그룹에 TCP 7777 인바운드를 추가합니다.
- 서버와 게임의 버전(프로토콜)이나 카드/맵 콘텐츠가 다르면 접속이 거절됩니다. 같은 빌드, 같은 `Content` 폴더를 사용하세요.

## 동작 규칙

1. 대기실: 첫 접속자가 방장. 방장은 Enter로 즉시 시작, ←/→로 1대1·2대2 전환.
2. `minPlayersToStart`명이 모이면 카운트다운 후 자동 시작. 빈 자리는 CPU.
3. 매치가 끝나면 승리/패배 결과 화면. 참가자가 확인(Enter/Space)하거나 `rematchDelay`초가 지나면 참가자는 메인 화면으로 돌아가고 서버는 대기실로 돌아갑니다. 재대결은 없습니다.
4. 게임 중 나간 플레이어 자리는 봇이 채우지 않고 비워 둡니다. **같은 컴퓨터(PC 이름)**에서 다시 접속하면 그 자리로 돌아갑니다. 시작할 때 CPU였던 자리는 새 플레이어가 이어받을 수 있습니다.
5. 한 팀의 사람이 모두 나가면 남은 팀이 즉시 승리합니다.
6. 한 포인트가 45초를 넘으면 서든 데스(모두 점점 체력 감소)가 시작됩니다.
7. 모두 나가면 대기실로 돌아가 자원을 쓰지 않고 기다립니다.

## 운영 팁

- 로그는 표준 출력으로 나옵니다 (`-logFile -`). 파일로 남기려면 `./start-server.sh > server.log 2>&1` 또는 systemd의 journald를 사용합니다.
- 한 컴퓨터에서 여러 방을 운영하려면 폴더를 복사해 포트가 다른 `server.json`으로 각각 실행합니다.
- systemd·Docker로 실행할 때는 콘솔 입력이 없으므로 명령은 쓸 수 없고, 종료는 `systemctl stop` / `docker stop`으로 합니다.

