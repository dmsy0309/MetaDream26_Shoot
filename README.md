# Metadream Shoot

**Unity 6000.6.6f1로 만든 1대1·2대2 카드 슈터와 전용 맵 편집기**입니다. ROUNDS의 정적 분석 자료를 바탕으로 대전 흐름과 카드 효과 등을 재구현하고, 온라인 대전·전용 서버·JSON 콘텐츠 확장 기능을 더했습니다. 분석으로 확인한 사실과 이 프로젝트에서 정한 동작은 [역개발 보고서](Docs/REVERSE_DEVELOPMENT_REPORT.md)에 구분해 기록합니다.

## 프로젝트 구성

- 로컬 2인 대전, CPU 대전·관전, 온라인 1대1·2대2 대전.
- 카드 선택·새로고침·제한 시간, 캐릭터 꾸미기, 방 채팅, 튜토리얼, 리플레이.
- 맵·카드·효과음 외부 콘텐츠 적용, 맵 스크립트와 개발자용 콘텐츠 도감·치트 대전.
- 게임의 로더·물리·대전 코드를 공유하는 시각적 맵 편집기.
- 화면 없이 실행하는 게임 전용 서버와 Python 서버 목록 서비스.

| 경로 | 역할 |
|---|---|
| `Assets/`, `Packages/`, `ProjectSettings/` | 게임 Unity 프로젝트의 루트 |
| `Assets/Reimplementation/` | 대전·네트워크·카드·맵·화면·리플레이 구현 |
| `Assets/Reimplementation/Resources/Content/` | 내장 카드·맵·효과음 정의 |
| `MapEditorProject/` | 별도로 여는 맵 편집기 Unity 프로젝트 |
| `MasterServer/` | Python 서버 목록·상태 확인·선택적 리플레이 수신 서비스 |
| `ServerLauncher/` | 전용 서버 설정과 Linux·Windows 실행 스크립트 |
| `Docs/` | 콘텐츠 작성법, 확장 규칙, 분석·검증 기록 |
| `ReverseAnalysis/` | 원작 분석 자료와 선택적으로 사용하는 재분석 도구 |
| `Tools/` | 로컬 빌드·패키징·진단 자동화 |

## 1. 소스를 설치하고 Unity에서 실행하기

### 준비물

- Git과 이 비공개 저장소에 접근할 수 있는 GitHub 계정.
- Unity Hub와 **Unity Editor 6000.6.6f1**. Hub에서 Editor를 실행할 수 있는 로그인·라이선스 상태를 준비합니다. 정확한 버전은 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt)를 기준으로 합니다.
- 빌드하려는 플랫폼의 Build Support 모듈. Windows 플레이어·서버는 Windows용 모듈, Linux 전용 서버는 Linux Dedicated Server/IL2CPP 관련 모듈과 해당 호스트의 툴체인이 필요합니다.

URP, Input System, Test Framework 등의 의존성은 [Packages/manifest.json](Packages/manifest.json)에 고정되어 있습니다. 첫 실행 시 Unity Package Manager가 설치합니다. 게임 실행을 위해 npm이나 Python 패키지를 설치할 필요는 없습니다. Python 3는 서버 목록 서비스를 직접 운영할 때 사용하며, 해당 서비스는 표준 라이브러리만 사용합니다.

```sh
git clone https://github.com/dmsy0309/MetaDream26_Shoot.git
cd MetaDream26_Shoot
```

1. Unity Hub에서 **저장소 루트 폴더**를 기존 프로젝트로 추가합니다. `Assets/` 폴더 자체를 프로젝트로 선택하지 않습니다.
2. 6000.6.6f1로 열고 패키지 복원·에셋 임포트·컴파일이 끝날 때까지 기다립니다.
3. `Assets/Scenes/Arena.unity`를 열고 Play를 누릅니다. 메뉴에서 튜토리얼이나 CPU 대전으로 기본 동작을 확인합니다.
4. 씬을 다시 생성해야 하면 `Metadream > Rebuild Arena Scene` 메뉴를 사용합니다. 이 작업은 현재 씬을 교체하므로 편집 중인 내용을 먼저 저장합니다.

`Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Builds/` 등은 로컬에서 생성되며 저장소에 포함되지 않습니다. clone 직후 실행 파일이 없어도 정상입니다. 플레이어는 아래 빌드 절차로 생성합니다.

### 기본 조작

| 입력 | 조작 |
|---|---|
| 1P 키보드·마우스 | A/D 이동, W/Space 점프, WASD/마우스 조준, F/좌클릭 사격, G/우클릭 방어 |
| 로컬 2P 키보드 | ←/→ 이동, ↑ 점프, 방향키 조준, `.` 또는 Num1 사격, `/` 또는 Num2 방어 |
| 게임패드 | 스틱 이동·조준, A 점프, RT/X 사격, LT/LB/RB 방어 |
| 메뉴 | 방향키 또는 W/S 이동, Enter/Space 선택, 마우스 클릭 |

## 2. 실행 파일을 빌드하고 배치하기

Unity 상단 메뉴의 빌드 항목을 사용합니다. 출력 경로는 각 프로젝트 기준입니다.

| 프로젝트 | 메뉴 | 기본 출력 |
|---|---|---|
| 게임 | `Metadream > Build macOS` | `Builds/macOS/MetadreamShoot.app` |
| 게임 | `Metadream > Build Windows` | `Builds/Windows/MetadreamShoot/MetadreamShoot.exe` |
| 게임 | `Metadream > Build Dedicated Server (Linux)` | `Builds/Server/Linux/MetadreamShootServer/MetadreamShootServer.x86_64` |
| 게임 | `Metadream > Build Dedicated Server (Windows)` | `Builds/Server/Windows/MetadreamShootServer/MetadreamShootServer.exe` |
| 맵 편집기 | `Metadream MapMaker > Build macOS` | `MapEditorProject/Builds/macOS/MetadreamShootMapMaker.app` |
| 맵 편집기 | `Metadream MapMaker > Build Windows` | `MapEditorProject/Builds/Windows/MetadreamShootMapMaker/MetadreamShootMapMaker.exe` |

맵 편집기 행의 경로는 저장소 루트에서 본 위치입니다. Windows 빌드는 Mono, Linux 전용 서버는 기본 IL2CPP입니다. 원격 저장소의 소스를 갱신해도 기존 설치본이 자동으로 바뀌지는 않습니다. 소스를 변경한 경우 대상 플랫폼을 다시 빌드해 교체합니다.

배포본을 설치할 때는 빌드 출력의 파일 구성을 유지합니다. Windows는 `.exe` 옆의 `_Data` 폴더·DLL 등도 함께 복사하고, macOS는 `.app` 번들 내부를 유지합니다. 업데이트 전에 사용 중인 외부 `Content/`와 로컬 설정·리플레이를 백업합니다.

### 명령줄 빌드 예시

아래 명령은 **저장소 루트**에서 실행합니다. 같은 프로젝트가 Unity Editor에 열려 있으면 배치 실행하지 않습니다. Unity 실행 파일 경로는 설치 환경에 맞춥니다.

macOS:

```sh
METADREAM_UNITY="/Applications/Unity/Hub/Editor/6000.6.6f1/Unity.app/Contents/MacOS/Unity"
mkdir -p Logs
"$METADREAM_UNITY" -batchmode -quit -projectPath "$PWD" \
  -buildTarget OSXUniversal \
  -executeMethod Metadream.Shoot.EditorTools.ProjectSetup.BuildMac \
  -logFile "$PWD/Logs/build-macos.log"
```

Windows PowerShell:

```powershell
$MetadreamUnity = "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe"
New-Item -ItemType Directory -Force Logs | Out-Null
& $MetadreamUnity -batchmode -quit -projectPath (Get-Location).Path `
  -buildTarget Win64 `
  -executeMethod Metadream.Shoot.EditorTools.ProjectSetup.BuildWindows `
  -logFile "$PWD/Logs/build-windows.log"
```

Linux/Windows 전용 서버의 빌드 메서드는 각각 `ProjectSetup.BuildServerLinux`, `ProjectSetup.BuildServerWindows`입니다. 맵 편집기는 `-projectPath`를 `MapEditorProject`로 지정하고 `Metadream.MapEditor.EditorTools.MapEditorSetup.BuildMac` 또는 `BuildWindows`를 사용합니다. 구현과 지원 옵션은 [ProjectSetup.cs](Assets/Reimplementation/Editor/ProjectSetup.cs), [MapEditorSetup.cs](MapEditorProject/Assets/MapEditor/Editor/MapEditorSetup.cs)를 참고합니다.

## 3. 설치 후 맵·카드 변경 사항 적용하기

### 빌드 없이 외부 JSON 적용

실행 파일 옆에 다음 구조로 콘텐츠를 배치하고 게임을 다시 시작합니다. macOS는 `.app` 번들 **옆**에 `Content/`를 둡니다.

```text
게임이 있는 폴더/
├── MetadreamShoot.app 또는 MetadreamShoot.exe
└── Content/
    ├── Maps/
    │   └── my_map.json
    ├── Cards/
    │   └── my_card.json
    ├── CardArt/
    │   └── my_card.png
    └── Sounds/
        └── ...
```

- JSON 파일 이름과 `id`를 맞춥니다. 같은 파일 이름의 외부 콘텐츠는 내장 항목을 덮어씁니다.
- `"disabled": true`로 항목을 비활성화할 수 있습니다.
- 별도 폴더는 게임 실행 인자 `-content 경로` 또는 전용 서버의 `contentDir`로 지정합니다.
- **Unity Editor에서는 외부 `Content/`를 자동으로 읽지 않습니다.** 내장 Resources를 수정하거나, 외부 경로를 명시해 테스트합니다.
- 온라인 참가자와 서버에 **동일한 콘텐츠**를 적용합니다. 프로토콜 버전 또는 콘텐츠 해시가 다르면 접속이 거절됩니다.
- 원래 상태로 돌아가려면 게임을 종료하고 추가한 외부 파일을 백업 위치로 옮긴 뒤 재시작합니다.

내장 콘텐츠로 배포하려면 `Assets/Reimplementation/Resources/Content/Maps/` 또는 `Cards/`에 JSON을 추가하고 다시 빌드합니다. 새로운 C# 효과·게임 규칙 변경도 재빌드가 필요합니다. [JSON 작성 가이드](Docs/CONTENT_JSON_GUIDE.md)와 [확장 가이드](Docs/EXTENDING.md)를 먼저 읽으세요.

### 맵 편집기로 적용

1. Unity Hub에서 `MapEditorProject/`를 **별도 프로젝트**로 열고 `Assets/Scenes/MapEditor.unity`를 실행하거나, 빌드한 맵 편집기를 실행합니다.
2. 맵을 만들거나 열어 검증 오류를 확인하고 **테스트 플레이**를 실행합니다.
3. **게임에 적용 · 내보내기**에서 게임 실행 파일을 선택하면 실행 파일 옆 `Content/Maps/<id>.json`에 적용합니다. 기존 파일은 `.bak`으로 보관합니다.
4. 다른 PC나 서버에는 `Content/Maps/<id>.json` 구조가 든 배포 패키지를 같은 위치에 적용하고 게임을 재시작합니다.

자세한 사용법은 [맵 편집기 안내](MapEditorProject/Docs/MAP_EDITOR_GUIDE.md)를 참고합니다. 기존 안내의 과거 프로젝트 이름 대신 이 저장소의 `MapEditorProject/`를 기준으로 작업합니다.

## 4. 온라인 대전과 서버 운영

게임 전용 서버와 서버 목록 서비스는 역할이 다릅니다. 전용 서버는 실제 대전을 처리하고, Python 메인 서버는 접속할 서버의 주소를 안내합니다. 서버 목록 서비스가 게임 트래픽을 중계하지는 않습니다.

| 포트 | 용도 |
|---|---|
| TCP 7777 | 기본 게임 접속 포트. 전용 서버 설정으로 변경 가능 |
| UDP 47777 | 같은 네트워크에서 방을 찾는 LAN 브로드캐스트 |
| UDP 47800 | Python 메인 서버의 서버 목록 서비스 |
| HTTP 47801 | 메인 서버 상태·목록 API. 선택적 리플레이 수신의 내부 서비스 포트 |

같은 네트워크에서는 **방 만들기 / 방 참가**를 사용하거나 `서버주소:포트`로 직접 접속합니다. 인터넷 대전은 게임 호스트까지 실제 TCP 연결이 가능해야 하므로 서버 측 방화벽·공유기·네트워크 조건을 확인합니다.

### 서버 목록 서비스

프로젝트의 기본 메인 서버 주소는 **`127.0.0.1:47800`**입니다. 운영 서버 주소를 저장소에 내장하지 않습니다. 같은 PC에서 시험할 때는 다음과 같이 실행합니다.

```sh
python3 MasterServer/master_server.py --host 127.0.0.1
```

다른 PC의 서비스를 사용하려면 게임과 전용 서버에 `-master host:port` 또는 `METADREAM_MASTER_HOST`를 지정합니다. 전용 서버의 로컬 JSON 설정에서도 `masterHost`/`masterPort`를 지정할 수 있습니다. 실제 주소와 SSH 계정은 로컬 설정으로 관리합니다. [메인 서버 안내](MasterServer/README.md)를 참고하세요.

### 전용 게임 서버

전용 서버를 빌드하면 플랫폼별 시작 스크립트와 기본 `server.json`이 실행 파일 옆에 복사됩니다. 처음 설정할 때 `server.local.json`으로 복사해 방 이름·포트·게임 규칙 등을 바꾸고, 해당 파일을 지정해 실행합니다. 이미 로컬 설정이 있으면 예제의 복사 단계로 덮어쓰지 않습니다.

Linux 빌드 폴더에서:

```sh
cp server.json server.local.json
METADREAM_CONFIG=server.local.json ./start-server.sh
```

Windows 빌드 폴더의 PowerShell에서:

```powershell
Copy-Item server.json server.local.json
$env:METADREAM_CONFIG = "server.local.json"
.\start-server.bat
```

`-config server.local.json`으로도 지정할 수 있습니다. 서버 콘솔의 `status`, `content`, `master`로 상태를 확인합니다. [전용 서버 안내](ServerLauncher/common/README-SERVER.md)에 설정 항목과 운영 명령이 있습니다. 현재 프로토콜 값은 [NetProtocol.cs](Assets/Reimplementation/Network/NetProtocol.cs)의 `Protocol.Version`이며 **v10**입니다. 세부 문서의 과거 버전·검증 기록과 구분합니다.

### 개인정보와 리플레이

로컬 리플레이 저장은 지원하며 **자동 업로드는 기본으로 꺼져 있습니다**. 컴퓨터 이름·OS 사용자명·기기 고유 ID·내부 IP는 리플레이 기기 정보로 수집하지 않습니다. 닉네임·방 이름·채팅처럼 직접 입력한 정보는 게임 데이터에 포함될 수 있습니다.

리플레이 업로드를 사용하려면 명시적 활성화, HTTPS URL, 별도 전송 키가 모두 필요합니다. 전용 서버는 로컬 설정의 `uploadReplays`도 활성화해야 합니다. 기존 대기 파일은 자동 재전송하지 않습니다. 설정과 보존 범위는 [PRIVACY.md](Docs/PRIVACY.md)를 따릅니다.

## 5. 사람의 지시를 받아 작업하는 AI 에이전트용 안내

### 작업 시작

1. 사람의 요청이 소스 설치·코드 변경·외부 콘텐츠 적용·빌드·실제 서버 배포 중 어디까지인지 확인하고, 해당 범위에서 작업합니다. 이 README만으로 운영 서버 주소나 인증정보를 추측하지 않습니다.
2. 루트 [AGENTS.md](AGENTS.md)를 읽습니다. 맵 편집기를 작업하면 [MapEditorProject/AGENTS.md](MapEditorProject/AGENTS.md)도 읽습니다.
3. `git status`와 Unity 버전, 열려 있는 Editor, 대상 플랫폼 모듈을 확인합니다. 기존 사용자 변경을 보존하고, 중요한 변경 전에 Git에 올리지 않는 로컬 복구 자료를 만듭니다.
4. 설치된 Unity 실행 파일을 찾아 사용합니다. 세부 문서의 `unity run/test/build`는 별도 래퍼를 전제로 할 수 있습니다. 아래 표준 Unity CLI 예시는 래퍼 없이 사용할 수 있습니다.

### 변경 위치와 규칙

| 요청 | 먼저 읽을 문서·지켜야 할 규칙 |
|---|---|
| 맵·카드 추가, 수치 변경 | [CONTENT_JSON_GUIDE.md](Docs/CONTENT_JSON_GUIDE.md), [EXTENDING.md](Docs/EXTENDING.md). 데이터로 가능한 변경은 JSON에 작성 |
| 새로운 효과·게임 규칙 | `Assets/Reimplementation/`에서 수정. 클라이언트·서버·리플레이 영향을 확인 |
| 맵 전용 동작 | [MAP_SCRIPTING.md](Docs/MAP_SCRIPTING.md). 이름·영역·스크립트·C# 동작의 검증 규칙 확인 |
| 카드 이미지·효과음 | [CARD_ART.md](Docs/CARD_ART.md), [SOUNDS.md](Docs/SOUNDS.md) |
| 캐릭터 꾸미기 | `Player/CharacterLook.cs`, `CharacterFace.cs`. 옵션은 배열 끝에 추가해 저장 데이터 호환 유지 |
| 씬·프리팹 | YAML을 손으로 편집하지 않고 게임은 `ProjectSetup.Setup`, 맵 편집기는 `MapEditorSetup.Setup`으로 생성 |
| 네트워크 직렬화 | 실제 패킷 형식을 바꾸면 `Network/NetProtocol.cs`의 `Protocol.Version` 증가. 주소·화면 문구 변경과 구분 |
| 맵 편집기 공유 런타임 | `GameSync.SyncFromGame`으로 갱신. 맵 편집기만 작업할 때 원본 게임은 읽기만 함. 게임 자체 수정이 요청된 경우에만 게임 쪽에서 수정하고, 에디터 복사본을 따로 수정하거나 게임 규칙을 재구현하지 않음 |
| 개인정보·배포 설정 | [PRIVACY.md](Docs/PRIVACY.md). 실제 IP·개인 경로·이메일·키·원본 캡처를 커밋하지 않음 |

원작 분석을 다시 수행할 때만 `ReverseAnalysis/tools/`와 선택적 REA·UnityPy 환경이 필요합니다. `ROUNDS_DATA`, `ROUNDS_ASSEMBLY`, `REA_MODULE_ROOT`, `REA_EVIDENCE_BUNDLE` 등의 경로는 사용자의 로컬 환경에 맞춰 지정하고, 결과를 공유하기 전에 개인 경로를 익명화합니다.

### 게임과 맵 편집기 동기화

공유 런타임은 게임 프로젝트가 기준입니다. 아래 명령은 두 프로젝트의 Editor를 닫은 뒤 저장소 루트에서 실행합니다. `METADREAM_UNITY`는 위 예시처럼 설치된 Unity 실행 파일로 지정합니다.

```sh
mkdir -p MapEditorProject/Logs
"$METADREAM_UNITY" -batchmode -nographics -quit \
  -projectPath "$PWD/MapEditorProject" -gameProject "$PWD" \
  -executeMethod Metadream.MapEditor.EditorTools.GameSync.SyncFromGame \
  -logFile "$PWD/MapEditorProject/Logs/sync.log"
"$METADREAM_UNITY" -batchmode -nographics -quit \
  -projectPath "$PWD/MapEditorProject" -gameProject "$PWD" \
  -executeMethod Metadream.MapEditor.EditorTools.GameSync.Verify \
  -logFile "$PWD/MapEditorProject/Logs/verify.log"
```

`SyncFromGame`은 게임의 작업 트리를 복사하고, `Verify`는 게임의 **커밋된 HEAD**와 비교합니다. 게임 런타임을 수정했다면 사람이 요청한 변경을 검토·커밋한 상태를 기준으로 동기화·검증합니다. 출력의 `IDENTICAL`, 런타임·물리 설정 해시와 동기화 manifest를 확인합니다.

### 검증과 완료 보고

게임 프로젝트가 닫힌 상태에서 저장소 루트에서 실행하는 macOS 예시입니다. PlayMode에서는 렌더링이 필요한 테스트가 있으므로 `-nographics`를 붙이지 않습니다. 테스트 실행에는 `-quit`도 붙이지 않습니다.

```sh
mkdir -p Logs
"$METADREAM_UNITY" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/Logs/EditMode.xml" -logFile "$PWD/Logs/EditMode.log"
"$METADREAM_UNITY" -batchmode -projectPath "$PWD" \
  -runTests -testPlatform PlayMode \
  -testResults "$PWD/Logs/PlayMode.xml" -logFile "$PWD/Logs/PlayMode.log"
```

맵 편집기도 `-projectPath`를 `MapEditorProject`로 바꾸고 결과·로그를 `MapEditorProject/Logs/`에 지정해 두 모드를 검사합니다. 메인 서버를 변경했다면 다음 테스트도 실행합니다.

```sh
python3 MasterServer/test_master_server.py
```

- 로그의 C# 컴파일 오류 0개와 NUnit XML의 실제 실행 수·실패 0개를 확인합니다. 종료 코드만으로 성공을 판단하지 않습니다.
- 외부 콘텐츠 적용 후 콘텐츠 오류와 해시를 확인합니다. 온라인 변경은 실제 호스트·클라이언트 연결로 검증합니다.
- 대상 빌드를 생성한 뒤 실제 실행과 적용 결과를 확인합니다. 빌드 성공·실행 성공·다른 PC 접속 성공을 각각 보고합니다.
- 변경 내용, 적용한 파일 위치, 테스트 결과, 실행한 플랫폼, 확인하지 못한 환경, 복구 방법을 사람에게 전달합니다.
- 커밋 전 `git diff --check`, 변경 목록, 개인정보·인증정보 포함 여부를 확인합니다. 로그·캡처·`.Recovery/`·실제 로컬 설정을 함께 올리지 않습니다. 커밋 이메일에는 GitHub noreply 주소를 사용합니다.

[2026-10-28 개인정보 보호 변경 검증 기록](Docs/PRIVACY_CLEANUP_VALIDATION_20261028.json)에는 게임 EditMode 96개·PlayMode 20개, 맵 편집기 EditMode 29개·PlayMode 9개, 서버 6개 테스트 통과가 기록돼 있습니다. 해당 소스와 환경의 기록이므로 이후 변경이나 다른 플랫폼은 다시 검증합니다.

## 6. 문제를 만났을 때

| 증상 | 먼저 확인할 것 |
|---|---|
| clone할 수 없음 | 비공개 저장소 접근 권한과 GitHub 로그인 |
| 프로젝트가 열리지 않거나 컴파일 실패 | Unity 6000.6.6f1, 패키지 복원, 로그의 첫 컴파일 오류 |
| 플랫폼 빌드 메뉴에서 실패 | 대상 Build Support·서버 모듈·IL2CPP 툴체인 설치 여부 |
| 외부 JSON이 반영되지 않음 | 실행 파일 옆 Content 위치, 파일 이름/id, Editor의 외부 폴더 처리, 재시작 여부 |
| 온라인 접속 거절 | 프로토콜 버전과 카드·맵 콘텐츠 해시 일치 여부 |
| 서버 목록이 비어 있음 | 로컬 기본 메인 서버 주소, 실제 운영 주소 지정, UDP 47800 접근 및 서버 등록 상태 |
| 목록에는 있지만 접속 실패 | 게임 TCP 포트 접근, 방화벽·포트 포워딩·네트워크 격리 |
| 맵 편집기와 게임 결과가 다름 | 올바른 게임 프로젝트를 GameSync했는지, 커밋된 HEAD 및 런타임·물리 해시가 일치하는지 |

폰트의 동봉 라이선스는 [Noto Sans KR OFL](Assets/Reimplementation/Licenses/NotoSansKR-OFL.txt)을 참고합니다. 세부 문서의 오래된 배포 경로·버전·검증 결과는 과거 기록이며 현재 코드와 로컬 실행 환경을 기준으로 적용합니다.
