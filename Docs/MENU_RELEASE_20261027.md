# 메인 메뉴·서버 목록 표시 정리 — 2026-10-27

## 변경과 복구

기준 커밋은 `0c4037e`, 복구 태그는 `backup/pre-menu-cleanup-20261027`이다. `.Recovery/menu-before-0c4037e/`에 모든 추적 소스, 게임/맵 에디터 빌드, 설치된 macOS 앱 두 개를 보존했다.

- 메인 화면의 `1대1 · 2대2 카드 슈터 · ROUNDS 정적 분석 기반 재구현` 안내문을 제거했다.
- 일반/디버그 메인 메뉴의 오른쪽 툴팁과 해당 데이터 필드를 제거했다. 메뉴 순서, 실행 동작, 단축키와 하단 조작 안내는 유지했다.
- 메인 서버 목록과 LAN 서버 목록의 주소 부분을 `포트 7777` 형식으로 변경했다. 서버 이름, 게임 인원/모드/진행 상태, 호환성 표시는 유지했다.
- 서버의 실제 주소와 포트, 패킷·접속·검색 로직, 직접 주소 입력은 변경하지 않았다. 프로토콜은 v10이고 콘텐츠 해시도 유지된다.

게임 런타임 커밋: `cfc5fe1`. 맵 에디터의 공용 코드는 GameSync로 갱신한다.


## 검증과 배포

- 게임 EditMode 87/87, PlayMode 20/20, 맵 에디터 EditMode 29/29, PlayMode 9/9: 총 145/145 통과.
- macOS/Windows 게임, Windows/Linux 전용 서버, macOS/Windows 맵 에디터: 6개 빌드 모두 Succeeded, errors=0, C# 컴파일 오류 없음.
- macOS 게임과 맵 에디터를 설치하고 각각 205개·204개 파일을 빌드와 SHA-256으로 대조했다.
- 설치본 메인 화면에서 안내문과 오른쪽 메뉴 툴팁이 사라진 것을 확인했다. 로컬 메인 서버와 별도 LAN 서버를 띄워 두 목록 행이 각각 `포트 18890`, `포트 18891`만 표시하는 것을 확인했다.
- 해당 서버에 두 로컬 TCP 클라이언트가 접속해 한글 채팅을 수신했다. 시험용 서버/메인 서버/클라이언트 프로세스는 검증 후 종료했다.
- 맵 에디터 자체 검사 PASS, 내장 맵 46/46 검증 통과. 게임 런타임 `cfc5fe1`의 926개 파일을 GameSync로 동기화하고 동일성을 확인했다.
- Windows/Linux는 이 Mac에서 크로스 빌드와 파일 형식·압축 무결성까지 검증했다. 해당 OS에서 직접 실행하지는 않았다.
- 복구 백업의 추적 파일 2,252개와 태그를 확인하고 변경 소스 3개의 백업이 기준 커밋과 같은 바이트임을 확인했다.

검증 기록: `Docs/MENU_BUILD_VALIDATION_20261027.json`. 테스트 XML: `Docs/menu-validation-20261027/`. 배포 파일 크기·SHA-256: `Docs/MENU_RELEASE_PACKAGES_20261027.json`.

| 대상 | 빌드 폴더 | 배포 파일 |
| --- | --- | --- |
| Windows 게임 | `Builds/Windows/MetadreamShoot/` | `Builds/Windows/MetadreamShoot-Windows-x64.zip` |
| Windows 서버 | `Builds/Server/Windows/MetadreamShootServer/` | `Builds/Server/MetadreamShootServer-Windows-x64.zip` |
| Linux 서버 | `Builds/Server/Linux/MetadreamShootServer/` | `Builds/Server/MetadreamShootServer-Linux-x64.tar.gz` |
| Windows 맵 에디터 | `MapEditorProject/Builds/Windows/MetadreamShootMapMaker/` | `MapEditorProject/Builds/Windows/MetadreamShootMapMaker-Windows-x64.zip` |

검증 캡처는 개인정보 보호를 위해 로컬에만 보관합니다.

검증 캡처는 개인정보 보호를 위해 로컬에만 보관합니다.
