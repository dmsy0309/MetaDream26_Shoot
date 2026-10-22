# ROUNDS 조작·선택·꾸미기 및 채팅 변경 기록

작업 기준: `d9d3d23`. 복구 태그 `backup/pre-rounds-parity-20261022`. 작업 브랜치 `codex/rounds-parity-chat`.

## 근거와 범위

2026-10-22 두 참조 대화 `Unity 6.6으로 Rounds 재현`(01a14cc2-88fa-73b3-91f9-07b724e75db6), `Unity 맵 에디터 제작`(01a158b8-014a-7b50-ae8b-69312491b003)의 사용자 요청과 완료 기록을 읽고 현재 코드와 대조했다. 기록에 있던 카드 제한 시간, 리롤, 체력 150/피해 50, 맵 스크립트, 팀 스폰, 리플레이, 서버 기능을 보존한다. 원본 게임 개발 폴더와 원본 맵 에디터 프로젝트는 읽기만 한다. 작업용 맵 에디터는 `MapEditorProject/`에 복사했다.

첨부 ROUNDS.app은 Steam 바로가기. 실제 대상은 Steam `ROUNDS.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll`, SHA-256 `4509e77d6af4d0a9934d39c664531585df0a740c9569b3838fc2e11fd9aa8f2a`, MVID `41353f7d-c882-4f28-954c-e1231269f7a0`이다. REA `inspect_managed_artifact`와 `inspect_managed_members`로 코드와 선언을 확인하고 REA CIL 디코더로 상세 분기를 읽었다. `ReverseAnalysis/evidence/parity-rea-members.json`은 member_refs 컬렉션을 제외한 증거 투영이며 원본 전체 Evidence와 동일한 파일이라고 주장하지 않는다.

UnityPy 1.25.4 + TypeTreeGeneratorAPI 0.0.10으로 Player 프리팹을 읽고 원시 바이트를 대조했다 (`parity-serialized.json`, `parity-motion-bytes.txt`). 재실행 도구는 `ReverseAnalysis/tools/extract-parity-assets.py`, `verify-motion-bytes.py`. 공유 에셋의 해시와 객체 ID도 기록했다. 생성된 타입 트리의 MonoBehaviour 헤더 PPtr 해석에는 정렬 문제가 있어 그 헤더를 근거로 사용하지 않았다. 이름/연결은 UnityPy 기본 헤더 파서로 확인하고, 이동 수치는 원시 오프셋 32부터 직접 대조했다.

## 구현

- PlayerMovement 실제 직렬화 값: force=17000, airControl=0.8, extraDrag=200. PlayerJump upForce=5500. Gravity force=1000, exponent=1.5. 이전의 임시 수치를 교체했다.
- PlayerJump.Update의 0.2초 내 점프 유지 추가 힘을 구현했다. 벽 점프 직후 0.15초 동안 재접촉으로 점프 상태를 즉시 덮어쓰지 않는다. 마우스 조준은 2.5초 뒤 이동 키 조준으로 자동 전환되지 않는다. 패드 오른쪽 스틱과 마우스의 마지막 사용에 따라 조준 장치를 바꾼다.
- CardChoice.DoPlayerSelect처럼 좌우 선택은 양 끝에서 멈춘다. 기존 순차 배분·확대·손·선택 후 카드 이동 애니메이션과 패배자 선택 규칙은 유지했다. 마우스 이동으로 강조, 카드 클릭 또는 Enter/기존 사격 키로 확정한다. 원격 마우스 선택도 서버 입력으로 보낸다. 리롤·기본 15초·방 설정은 유지한다. `picks=6`은 확인했지만 화면 카드 장수로 단정하지 않고 기존 방 설정을 유지한다.
- PlayerFace의 눈·입·장식1·장식2 및 오프셋, CharacterCreatorHandler의 얼굴 보관함 10칸 구조를 참고했다. 기존 머리/표정/장식 인덱스는 보존하고 입·장식2·위치 조절을 추가했다. 빈 보관함 클릭으로 저장, 채워진 칸 클릭으로 불러오기, Shift+클릭으로 덮어쓴다. 기존 외형 저장값은 자동으로 읽으며 새로운 값은 별도 JSON 키에 저장한다.
- 방 채팅: 대기실/경기/카드 선택 중 T로 열고 Enter 또는 전송 버튼으로 전송, Esc 닫기. 전투 중에는 Enter로도 연다. 로비 시작/카드 확정의 기존 Enter 입력은 보존한다. 호스트와 전용 서버가 연결된 플레이어 신원을 붙여 전체 참가자에게 전달한다. 160자, 0.75초 전송 간격, 최근 64개, 서식 태그를 실행하지 않는 일반 텍스트. 입력 중 로컬 이동·점프·사격·방어·카드 조작을 차단하며 온라인 경기 시간은 계속 흐른다. 채팅은 세션 메모리에만 저장하며 리플레이 파일 형식에는 추가하지 않았다.
- 프로토콜 v10. 서버와 참가자를 함께 갱신해야 한다. 외형 확장 데이터는 스냅샷 끝에 추가하므로 기존 v9 리플레이도 읽는다.

## 동일성의 한계

원작 CIL/프리팹 수치와 구조를 근거로 한 재구현이다. 원작 전체 충돌 시뮬레이터, 그래픽/표정 에셋, 네트워크 지연, 실제 플레이 체감이 완전히 같다는 뜻은 아니다. 맵 가장자리 밀어내기 수치 등 이번 요청 밖의 기존 판단은 유지했다. 원작 PlayerMovement.FixedUpdate의 wallDistance<0.7 분기는 실제 속도를 수정하지 않는 코드이므로 임의의 벽 고정 기능을 만들지 않았다.

## 복구

현재 폴더의 변경 전 소스는 `.Recovery/source-before.tar.gz`, 변경 전 배포 폴더는 `.Recovery/Builds-before/`, 기준 SHA는 `.Recovery/baseline-commit.txt`에 보존했다. Git 태그로 추적 파일 상태를 복원할 수 있다. 이 태그는 이번 작업이 추가한 MapEditorProject를 포함하지 않는다. 복구 요청 시 현재 작업을 별도 보관하고 이번 커밋들을 되돌린 다음 이전 Builds와 설치 백업을 복구한다. `Library`, `Temp`, `Logs`는 재생성 가능한 Unity 캐시/검증 결과다. 복구 전 사용자 후속 변경을 덮어쓰지 않도록 먼저 확인한다.
