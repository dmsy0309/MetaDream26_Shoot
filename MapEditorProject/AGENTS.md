# AGENTS.md

Metadream Shoot Map Maker — Metadream Shoot(Unity 6000.6.6f1) 맵 JSON 편집기.

- 사용법·빌드·CLI: **Docs/MAP_EDITOR_GUIDE.md**
- 에디터 코드: `Assets/MapEditor/` (Runtime = 앱, Editor = 씬/빌드/GameSync, Tests).
- `Assets/Reimplementation/`, `Assets/Settings/`, 물리 관련 `ProjectSettings/*`는 게임 프로젝트의 복사본입니다. 직접 수정하지 말고 `GameSync.SyncFromGame`으로만 갱신하고 `GameSync.Verify`로 동일성을 확인합니다. 게임 프로젝트는 읽기만 합니다.
- 맵 검증·로딩·물리·테스트 플레이는 게임 코드(ContentDatabase.ParseMap, MapRoot, GameFlow)를 호출합니다. 게임 규칙을 에디터에 다시 구현하지 않습니다.
- 씬은 `MapEditorSetup.Setup`으로 생성합니다. 변경 후 error CS 0개, EditMode·PlayMode 테스트 통과를 확인합니다.
