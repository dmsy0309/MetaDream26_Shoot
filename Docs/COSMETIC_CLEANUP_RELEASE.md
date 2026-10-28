# 꾸미기·카드 표시 정리 — 2026-10-22

## 작업 전 기준과 복구

- 기준 커밋: `cce4f79`.
- 복구 태그: `backup/pre-cosmetic-cleanup-20261022`.
- `.Recovery/cosmetic-before-cce4f79/`에 모든 추적 소스, 이전 게임/맵 에디터 빌드 폴더, 설치되어 있던 macOS 앱 두 개를 보존했다.
- 이전 작업의 복구 태그와 백업도 유지했다. 복구 지시가 오면 이후 작업을 보존한 뒤 이 기준의 소스·빌드·설치 앱으로 되돌린다.

## 변경 범위

- 기존 직접 그린 머리 장식·표정·입·얼굴 장식의 선택 목록과 렌더링 코드를 제거했다. 추출한 눈 85개, 입 58개, 장식 54개만 제공한다. 장식의 `없음` 항목도 원본 카탈로그의 투명 항목이다.
- `Resources/CharacterArt/names-ko.json`에 실제 모양을 기준으로 197개 항목의 한글 이름을 지정했다. 공식 한국어 번역명이 아니라 이 프로젝트의 표시 이름이다. 각 범주 안에서 이름은 서로 다르다.
- 꾸미기 탭은 눈·장식 1·입·장식 2로 구성하며 무작위 선택, 위치 드래그·슬라이더, 팀 색 미리보기, 현재 외형의 자동 저장은 유지했다.
- 얼굴 보관함 UI와 저장·불러오기 API를 제거했다. 기존 `portrait.*` 환경설정 데이터는 복구 가능성을 위해 삭제하지 않고 더 이상 사용하지 않는다.
- 총 위 숫자·탄약 점·재장전 원 렌더러를 제거했다. 캐릭터의 기존 체력바/HUD, 탄약 계산, 재장전 규칙과 총 반동은 변경하지 않았다.
- 카드 오른쪽 위의 `ROUNDS` 배지를 제거했다. 추격·광휘·잔해·독구름 카드의 설명에서 원작과의 비교 괄호 문구만 제거했다. 모든 카드 JSON을 작업 전과 비교해 변경 필드가 이 4개 `description`뿐임을 확인했다. 카드 ID·희귀도·능력치·효과·그림·선택 규칙은 유지했다.

## 저장 및 통신 호환성

추출한 항목의 기존 ID(눈 8–92, 입 8–65, 장식 7–60)를 유지한다. 삭제한 구형 선택이나 범위를 벗어난 값은 추출 카탈로그의 기본 항목으로 처리한다. 구형 Hat 바이트는 기존 통신/저장 형식의 예약 필드로 0을 유지하며, 더 이상 장식을 표현하지 않는다. 새 이름과 선택 UI는 ID를 재배열하지 않는다.

바이트 형식을 바꾸지 않아 프로토콜 v10은 유지한다. 카드 설명 변경으로 콘텐츠 해시는 달라지므로 게임과 서버를 이번 빌드로 함께 배포한다. 맵 에디터의 공용 코드는 GameSync로 동기화하고 Verify로 검사한다.


## 최종 검증과 설치

- 게임 EditMode 87/87, PlayMode 20/20, 맵 에디터 EditMode 29/29, PlayMode 9/9: 총 145/145 통과.
- macOS 게임·Windows 게임·Windows 서버·Linux 서버·macOS 맵 에디터·Windows 맵 에디터: 6개 빌드 모두 Succeeded, errors=0, 새 C# 컴파일 오류 없음.
- macOS 게임 `~/Applications/MetadreamShoot.app`, 맵 에디터 `~/Applications/MetadreamShootMapMaker.app` 설치 완료. 설치 파일 205개와 204개를 빌드와 SHA-256으로 대조했다.
- 설치본 꾸미기 화면에서 한글 이름, 4개 탭, 얼굴 보관함 제거, 총 위 표시 제거를 확인했다. 잔해 카드 화면에서 오른쪽 위 배지와 비교 설명 제거를 확인했다.
- 2대2 카드 선택·전투, 맵 에디터 자체 검사 PASS(기본 맵 46/46), 내부 CPU 테스트 플레이, 두 로컬 TCP 클라이언트의 한글 채팅 전달을 검증했다.
- 콘텐츠 해시: `73ff3bf6ad97`. 맵 에디터에 게임 런타임 `a7d01bc`의 926개 파일을 동기화하고 Verify에서 동일성을 확인했다.
- Windows/Linux는 크로스 빌드 및 실행 파일 형식·압축 무결성 검증까지 완료했다. 해당 OS에서 직접 실행하지는 않았다.
- 백업의 추적 파일 2,231개 목록과 복구 태그를 확인했고, 이번에 수정한 기존 소스 파일은 백업과 기준 커밋의 바이트가 일치했다.

상세 결과: `Docs/COSMETIC_BUILD_VALIDATION_20261022.json`. 테스트 XML: `Docs/cosmetic-validation/`. 배포 압축 파일의 크기·SHA-256: `Docs/COSMETIC_RELEASE_PACKAGES_20261022.json`.

| 대상 | 실행 폴더 | 배포 압축 파일 |
| --- | --- | --- |
| Windows 게임 | `Builds/Windows/MetadreamShoot/` | `Builds/Windows/MetadreamShoot-Windows-x64.zip` |
| Windows 서버 | `Builds/Server/Windows/MetadreamShootServer/` | `Builds/Server/MetadreamShootServer-Windows-x64.zip` |
| Linux 서버 | `Builds/Server/Linux/MetadreamShootServer/` | `Builds/Server/MetadreamShootServer-Linux-x64.tar.gz` |
| Windows 맵 에디터 | `MapEditorProject/Builds/Windows/MetadreamShootMapMaker/` | `MapEditorProject/Builds/Windows/MetadreamShootMapMaker-Windows-x64.zip` |

검증 캡처는 개인정보 보호를 위해 로컬에만 보관합니다.

검증 캡처는 개인정보 보호를 위해 로컬에만 보관합니다.
