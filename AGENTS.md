# AGENTS.md

Metadream Shoot — ROUNDS를 정적 분석(REA)해 Unity 6000.6.6f1로 재구현한 1대1/2대2 카드 슈터.

- 맵/카드/능력 추가·수정: **Docs/CONTENT_JSON_GUIDE.md** (JSON 작성법·템플릿·검증 메시지)와 **Docs/EXTENDING.md** (효과 클래스, 디버그 모드, 테스트/빌드)를 먼저 읽으세요.
- 리플레이: Replay/ (ReplayFile 형식, ReplayRecorder 방장/서버 녹화, ReplayUploader → 메인 서버 POST /replays, ReplayPlayer 재생 UI).
- 전용 서버 설정(server.json, rules): ServerLauncher/common/README-SERVER.md
- 메인 호스팅 서버(서버 목록 서비스): MasterServer/README.md
- 원작 분석 근거와 결정 기록: Docs/REVERSE_DEVELOPMENT_REPORT.md

규칙: 씬/프리팹을 손으로 편집하지 말고 Editor/ProjectSetup.cs 로 생성합니다. 변경 후 컴파일(error CS 0개), EditMode·PlayMode 테스트 통과를 확인합니다. 네트워크 형식을 바꾸면 Network/NetProtocol.cs 의 Protocol.Version 을 올립니다.

