# 캐릭터 외형 및 원작 꾸미기 복원 — 2026-10-22

## 작업과 복구 기준

이번 작업 직전 SHA는 `3f08c13`, 태그는 `backup/pre-character-model-20261022`다. 작업 전 추적 소스, 게임/맵 에디터 빌드 폴더, 설치된 macOS 앱 두 개를 `.Recovery/character-before-3f08c13/`에 보존했다. 이전 작업의 `d9d3d23` 백업도 그대로 남아 있다. 복구를 요청받으면 후속 변경을 별도 보존한 뒤 이번 변경만 되돌리고 해당 앱/배포 폴더를 복원한다. 원본 개발 폴더는 수정하지 않았다.

## 원작 분석 근거

- 실제 대상: Steam ROUNDS.app의 `Assembly-CSharp.dll`, SHA-256 `4509e77d6af4d0a9934d39c664531585df0a740c9569b3838fc2e11fd9aa8f2a`, MVID `41353f7d-c882-4f28-954c-e1231269f7a0`.
- REA 관리 코드 멤버 증거: `ev_7048dfb3e1133012726ab45a6dd0f9f63e797170b369337ddee0d4a2cf68bb47`, `ev_a1567aa68694a2f4c9d56746a9b69f63a392b4fe5dc4ca635b3bd3af10f32367`. 전체 번들 `ReverseAnalysis/evidence/character-rea-bundle.json`. REA 디코더의 상세 CIL은 `character-il.txt`, `character-rig-il.txt`.
- UnityPy로 Player #2724, WeaponBase #2586 및 캐릭터 메뉴의 실제 직렬화 객체를 읽었다. MonoBehaviour의 GameObject/Script 연결은 기본 헤더 파서를 이용했고, 생성 타입 트리의 잘못 정렬된 m_Script 헤더는 근거로 쓰지 않았다. 사용자 필드와 네이티브 Transform/SpriteRenderer/Sprite 데이터를 대조했다.
- `CharacterCreatorItemLoader`(level0 #3707)의 원래 배열: 눈 85개, 입 58개, 장식 54개. 모든 항목과 자식 렌더러를 따라 246개 스프라이트를 추출했다. PNG는 패킹된 원본에서 잘려 있던 여백을 textureRectOffset으로 복원했으며 원래 pivot, pixelsPerUnit, 색, 배율, 자식 좌표, 렌더 순서를 보존한다.
- 원본 에셋 출처와 해시는 `character-art-extraction.json`, 재추출 도구는 `ReverseAnalysis/tools/extract-character-art.py`다. 하트/눈물/고리의 3개 Animator 클립은 스트림의 다항식 계수와 상수 채널을 추출해 재생한다. 원작 그래픽은 사용자가 제공한 로컬 게임 파일에서 가져왔다.
- 몸체는 스프라이트 합성이고 팔다리는 `LegRenderer`의 10개 선분(두께 약 0.1) 구조다. `IkLeg`는 바닥을 탐색한다. `IKArmMove`는 조준 쪽 손을 무기에 붙이고 `FollowInactiveHand`는 반대 손에 ShieldStone을 붙인다. 왼쪽 조준 시 화면상의 양손 역할이 뒤집힌다.
- WeaponBase의 총열 0.640386×0.150281, 손잡이 0.352469×0.150281 크기와 배치를 사용한다. ShieldStone은 Circle 스프라이트의 0.05배 크기다. GunAmmo는 총 위 탄약 아이콘을 남은 수만큼 활성화하고 재장전 링을 채운다.

## 구현 범위

`CharacterModel`을 Fighter와 CharacterPreview가 공통 사용한다. 둥근 몸체·어두운 외곽선·팔 2개·다리 2개·손의 총/방패를 표시하고 이동/공중/조준/사격/방어 상태를 시각적으로 반영한다. 발의 바닥 탐색은 표시만 바꾸며 물리 힘이나 충돌체를 추가하지 않는다. 총 위에 남은 탄약 아이콘과 숫자, 재장전 진행 링을 표시한다. 탄약 표시는 기존 AmmoState와 네트워크 스냅샷의 값을 읽는다.

꾸미기 목록은 기존 인덱스를 바꾸지 않고 뒤에 원작 목록을 추가한다. 눈은 8부터, 입은 8부터, 장식은 7부터 원작 배열 순서와 일치한다. 기본 표정은 원작 기본 얼굴을 사용하고, 그 외 기존 표정/머리 옵션도 보존한다. 아이콘 스크롤 목록으로 모든 항목을 선택하며 미리보기 드래그와 기존 슬라이더로 위치를 조절한다. 기존 저장 슬롯·장식 2개·온라인/리플레이 동기화는 유지한다. 세션 전용 `-look HAT,EYE,ACCESSORY,MOUTH,ACCESSORY2`로 저장 설정을 바꾸지 않고 검사할 수 있다.

네트워크 바이트 형식은 바꾸지 않아 프로토콜 v10을 유지한다. 새 외형 인덱스를 제대로 표시하려면 클라이언트와 서버를 이번 빌드로 함께 갱신해야 한다. 게임 규칙, 카드, 맵, 입력, 채팅, 기존 HUD, 전용 서버 운영 기능은 변경 대상이 아니다. 맵 에디터는 GameSync로 게임 런타임을 동기화한다.

## 동일성의 범위

원작의 실제 장식 그림과 합성 수치는 추출해 사용했다. 팔다리·손·총의 동작은 기존 게임 시뮬레이션 위에 구현한 시각 리그다. 원작의 별도 무기 Rigidbody/전체 IK 시뮬레이터나 조명·SpriteMask 시스템을 통째로 이식하지 않았다. 기존 피격 판정과 탄환 생성 위치를 바꾸지 않았으며, 원작 실행 화면과 모든 프레임의 픽셀/물리 결과가 완전히 같다고 검증한 것은 아니다. 숫자 탄약 표시는 사용자가 요청한 식별성을 위해 아이콘 위에 함께 표시한다.

## 실제 화면 검증에서 반영한 보정

- 위쪽 조준 시 총 위 숫자가 잘리지 않도록 미리보기 카메라와 드래그 변환 비율을 보정했다. `PreviewAmmoRemainsInsideFrameWhenAimingUp`이 텍스트 경계의 카메라 프레임 포함 여부를 검사한다.
- Unity 직렬화 깊이 경고를 피하도록 장식 트리의 자식 162개를 인덱스 배열로 저장한다. 변경 전후 197개 항목의 모든 그림, Transform, 애니메이션 계수를 재귀 복원해 동일함을 확인했다.
- 스크롤 영역에서 GUI 변환 행렬을 변경하면 썸네일이 잘리는 문제를 수정했다. 설치본에서 눈/표정·장식 탭을 직접 조작해 원작 목록 썸네일과 선택 미리보기를 확인했다.
- 최종 게임 런타임 커밋: `b1b75db`. 맵 에디터는 이 커밋의 런타임 파일 924개를 GameSync로 복사하고 Verify를 통과했다.


## 최종 검증 및 배포

- 게임 EditMode 87/87, PlayMode 20/20, 맵 에디터 EditMode 29/29, PlayMode 9/9: 총 145/145 통과. 결과 XML은 `Docs/character-validation/`.
- macOS 게임, Windows 게임, Linux 전용 서버, Windows 전용 서버, macOS 맵 에디터, Windows 맵 에디터: 6개 빌드 모두 Succeeded, errors=0. 중간 Windows 서버 빌드 1회가 Unity Analytics 네이티브 작업에서 충돌했으나 최종 재실행은 정상 종료했다.
- macOS 게임은 `~/Applications/MetadreamShoot.app`, 맵 에디터는 `~/Applications/MetadreamShootMapMaker.app`에 설치했다. 게임 205개·에디터 204개 파일을 빌드와 SHA-256으로 대조했다.
- 설치본에서 꾸미기 탭/썸네일, 2대2 카드 선택·전투, 맵 에디터 자체 검사 PASS(기본 맵 46/46), 내부 CPU 테스트 플레이를 검증했다. 로컬 서버에 TCP 클라이언트 2개를 접속해 한글 채팅과 발신자 표시의 정상 전달을 확인했다.
- Windows/Linux 결과는 이 Mac에서의 크로스 빌드·파일 형식·압축 무결성 검증이다. 해당 OS에서 직접 실행한 증거는 없다.
- 상세 검증: `Docs/CHARACTER_BUILD_VALIDATION_20261022.json`. 배포 파일 크기와 SHA-256: `Docs/CHARACTER_RELEASE_PACKAGES_20261022.json`.

| 대상 | 실행 폴더 | 배포 압축 파일 |
| --- | --- | --- |
| Windows 게임 | `Builds/Windows/MetadreamShoot/` | `Builds/Windows/MetadreamShoot-Windows-x64.zip` |
| Windows 서버 | `Builds/Server/Windows/MetadreamShootServer/` | `Builds/Server/MetadreamShootServer-Windows-x64.zip` |
| Linux 서버 | `Builds/Server/Linux/MetadreamShootServer/` | `Builds/Server/MetadreamShootServer-Linux-x64.tar.gz` |
| Windows 맵 에디터 | `MapEditorProject/Builds/Windows/MetadreamShootMapMaker/` | `MapEditorProject/Builds/Windows/MetadreamShootMapMaker-Windows-x64.zip` |

![복원한 캐릭터 미리보기](screenshots/character-previews/look-1.png)

설치본 화면은 `Docs/screenshots/character-installed-look/`, `character-installed-battle/`, `character-installed-editor/`, `character-editor-playtest/`에 있다.
