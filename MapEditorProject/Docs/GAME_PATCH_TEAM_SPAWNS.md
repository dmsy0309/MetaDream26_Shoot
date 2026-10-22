# 게임 수정 지시서 — 2대2 팀원 개별 스폰을 "준비" 상태부터 반영

대상: Metadream Shoot 게임 프로젝트 `~/UnityProjects/Metadream26_Shoot` (기준 커밋 `6f5e870`)
작성: Metadream Shoot Map Maker 쪽 분석 (게임 폴더는 읽기만 했음). 이 문서 범위 밖의 파일은 바꾸지 마세요.

## 1. 목표

맵 JSON에 `spawns[2]`(주황 팀 두 번째 플레이어)·`spawns[3]`(파랑 팀 두 번째 플레이어)가 있으면, 2대2에서 그 플레이어가 **포인트 시작 배치("준비" 배너) 순간부터** 그 위치에 서게 한다. 없으면 지금과 똑같이 동작한다.

## 2. 현재 동작과 근거

| 위치 | 내용 |
|---|---|
| `Assets/Reimplementation/Maps/MapRoot.cs:53-59` `SpawnPoint(team, indexInTeam)` | `spawns[team % spawns.Length]`만 쓰고, 두 번째 팀원은 `x`를 중앙 쪽으로 `2.6 × indexInTeam` 옮김. `spawns[2]` 이후는 배치에 쓰이지 않음 |
| `GameFlow.cs:620-633` `BeginBattle` | 포인트 시작 시 `perTeam[team]++` 순서로 `SpawnPoint(f.TeamId, index)`에 `Revive` 후 `SetPlaying(false)`, 0.55초 뒤 `SetPlaying(true)` |
| `GameFlow.cs:926-934` `SandboxRespawn` | 팀 안 순서(index)로 `SpawnPoint` 사용 (자유 테스트·튜토리얼) |
| `GameFlow.cs:973` `DebugNextMap` (F2) | 모든 플레이어에 `SpawnPoint(f.TeamId, 0)` — 팀원 순서를 무시함 |
| `Maps/Scripting/MapScriptApi.cs:42, 192` | 맵 스크립트·C# 동작은 `IsPlaying`인 플레이어만 다룸 → "준비" 0.55초 동안은 위치를 바꿀 수 없음 (그래서 게임 수정이 필요) |
| `Player/Fighter.cs:670-680` `PhoenixRevive` | 부활 카드(respawns)는 **죽은 자리**에서 부활 — 스폰 위치와 무관, 이번 수정 대상 아님 |
| `GameFlow.cs:283`, `Online/NetHostSession.cs:294, 518` | 슬롯 i 는 팀 `i % 2`. 팀 안 순서: 슬롯 0·2 = 주황 1·2, 슬롯 1·3 = 파랑 1·2 |
| 내장 맵 46개 (`Resources/Content/Maps/*.json`) | 모두 `spawns`가 정확히 2개 → 아래 수정으로 동작이 바뀌는 내장 맵 없음 |

## 3. 수정 내용

### 3.1 `Maps/MapRoot.cs` — `SpawnPoint` (필수)

```csharp
/// <summary>
/// Team spawn. The n-th teammate (n ≥ 1) uses spawns[team + 2n] when the map has it
/// (spawns[2] = orange #2, spawns[3] = blue #2); otherwise it steps 2.6 units toward the arena centre.
/// </summary>
public Vector2 SpawnPoint(int team, int indexInTeam = 0)
{
    var spawns = Definition.Spawns;
    int custom = team + 2 * indexInTeam;
    if (indexInTeam > 0 && team >= 0 && custom < spawns.Length)
        return (Vector2)transform.position + spawns[custom];
    Vector2 p = spawns[team % spawns.Length];
    if (indexInTeam > 0) p.x += (p.x < 0f ? 1f : -1f) * 2.6f * indexInTeam;
    return (Vector2)transform.position + p;
}
```

- `spawns`가 2개인 맵: 결과가 지금과 완전히 같아야 함.
- `spawns`가 3개인 맵: 주황 2만 `spawns[2]`, 파랑 2는 기존 규칙.
- `BeginBattle`·`SandboxRespawn`은 이미 팀 안 순서를 넘기므로 수정 불필요.

### 3.2 `GameFlow.cs` — `DebugNextMap` (권장, 디버그 F2 일관성)

`f.Revive(CurrentMap.SpawnPoint(f.TeamId, 0), false);` 대신 `BeginBattle`과 같은 팀 안 순서를 계산해 넘긴다:

```csharp
var perTeam = new int[2];
foreach (var f in Fighters)
{
    if (IsSandbox) { SandboxRespawn(f); continue; }
    int team = Mathf.Clamp(f.TeamId, 0, 1);
    int index = f.Absent ? 0 : perTeam[team]++;
    if (!f.IsDead)
    {
        f.Revive(CurrentMap.SpawnPoint(f.TeamId, index), false);
        f.SetPlaying(Phase == MatchPhase.Battle);
    }
}
```

### 3.3 `Content/ContentDatabase.cs` — 검증 (권장)

- 스폰 검사(겹침·경계·바닥)는 이미 모든 `spawns`에 적용되므로 그대로 둔다.
- `spawns.Length > 4`이면 경고 1줄: `"spawns[4] and later are unused (2 teams × 2 players)"`.

### 3.4 문서

- `Docs/CONTENT_JSON_GUIDE.md` 3장 표 `spawns` 행: "최소 2개. `spawns[0]` 주황 팀, `spawns[1]` 파랑 팀. 2대2 두 번째 플레이어는 `spawns[2]`(주황)·`spawns[3]`(파랑)이 있으면 그곳, 없으면 팀 스폰 옆 2.6".
- `Docs/EXTENDING.md` 4장 `spawns` 행과 `MapRoot.SpawnPoint` 설명을 같은 내용으로 갱신.
- `Docs/REVERSE_DEVELOPMENT_REPORT.md`에 "원작에 없는 개발 결정: 2대2 팀원 개별 스폰" 1줄.

### 3.5 바꾸지 않는 것

- 네트워크 형식: 위치는 스냅샷으로 전달되므로 `Protocol.Version` 변경 불필요 (클라이언트·리플레이는 호스트가 정한 위치를 그대로 받음).
- 콘텐츠 해시 계산, 리플레이 형식, 부활 카드(`PhoenixRevive`), 맵 스크립트 API.

## 4. 테스트 (추가)

1. EditMode `Tests/EditMode/GimmickTests.cs` 또는 새 파일:
   - spawns 2개 맵: `SpawnPoint(0,1)` = `spawns[0] + (2.6, 0)` (왼쪽 스폰 기준), `SpawnPoint(1,1)` = `spawns[1] − (2.6, 0)` — 기존과 동일.
   - spawns 4개 맵: `SpawnPoint(0,1) == spawns[2]`, `SpawnPoint(1,1) == spawns[3]`, `SpawnPoint(0,0) == spawns[0]`.
   - spawns 3개 맵: `SpawnPoint(0,1) == spawns[2]`, `SpawnPoint(1,1)`은 기존 규칙.
2. PlayMode: spawns 4개인 임시 맵(`ContentDatabase.ExternalDirectory` 사용)으로 `StartMatch(GameMode.CpuDemoTeams, settings{mapPool=[id]})` → 단계가 `Battle`이 된 직후(`SetPlaying(true)` 전, "준비" 배너) `Fighters[2].Position ≈ spawns[2]`, `Fighters[3].Position ≈ spawns[3]` (오차 0.3 이내).
3. 기존 EditMode·PlayMode 테스트 전부 통과, `BuiltinContentLoadsWithoutErrors` 통과.

## 5. 완료 기준

- [ ] spawns 4개 맵의 2대2 대전에서 "준비" 배너가 보이는 동안 팀원 두 명이 각자 스폰에 서 있다 (CPU 관전 `-demo2v2` 캡처로 확인).
- [ ] spawns 2개 맵(내장 46개)은 배치가 바뀌지 않는다.
- [ ] 자유 테스트·튜토리얼·F2 맵 넘기기에서도 같은 규칙.
- [ ] error CS 0개, EditMode·PlayMode 통과, 클라이언트·서버 빌드 갱신.

## 6. 맵 에디터 쪽 (참고, 게임 작업 아님)

- Map Maker는 지금 "싸워라!" 순간 순간이동하는 자동 스크립트 블록(`# [Map Maker] 2대2 팀원 개별 스폰`)으로 이 기능을 흉내 냅니다.
- 게임 수정 후 맵 에디터 프로젝트에서 `GameSync.SyncFromGame` → 빌드하면, 에디터가 게임의 `SpawnPoint`를 직접 시험해 기본 지원을 감지하고 자동 블록을 더 이상 넣지 않습니다(편집 시 기존 블록 제거).
- 이미 배포한 맵의 자동 블록은 같은 위치로 순간이동할 뿐이라 게임 수정 후에도 문제없이 동작합니다.

## 7. 알 수 없는 점

- 온라인에서 빈 슬롯(`Absent`)이 있으면 팀 안 순서가 당겨집니다(예: 슬롯 0이 비면 슬롯 2가 `spawns[0]`). 지금 규칙을 그대로 따르는 것이므로 의도와 다르면 별도 결정이 필요합니다.
