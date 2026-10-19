# Metadream Shoot 확장 가이드 (맵 · 카드 · 능력) — AI 에이전트용

이 문서만 읽고 새 맵, 새 카드, 새 능력(효과)을 추가·수정하고 검증할 수 있도록 작성했습니다. 사람도 그대로 따라 하면 됩니다.
원작 분석 근거는 `Docs/REVERSE_DEVELOPMENT_REPORT.md`에 있습니다. 이 문서는 "어떻게 고치는가"만 다룹니다.

## 0. 먼저 알아 둘 것

| 항목 | 값 |
|---|---|
| 엔진 | Unity 6000.6.6f1, URP 2D, Input System, IMGUI(UI) |
| 런타임 코드 | `Assets/Reimplementation/` (어셈블리 `Metadream.Shoot`, 네임스페이스 `Metadream.Shoot.*`) |
| 콘텐츠(데이터) | `Assets/Reimplementation/Resources/Content/Cards/*.json`, `.../Content/Maps/*.json` |
| 능력 코드 | `Assets/Reimplementation/Abilities/` (`CardEffect.cs` = 기반, `BuiltinEffects.cs` = 예시) |
| 로더/검증 | `Assets/Reimplementation/Content/ContentDatabase.cs` |
| 테스트 | `Assets/Reimplementation/Tests/EditMode`, `Tests/PlayMode` |
| 씬 | 코드로 생성 (`Editor/ProjectSetup.cs`의 `Setup`). 씬/프리팹을 손으로 편집하지 않습니다 |

규칙:

1. **카드와 맵은 데이터(JSON)**입니다. C# 수정 없이 JSON 파일 하나로 추가합니다.
2. **새로운 동작(폭발, 중독 같은)은 능력(Effect) 클래스** 하나로 추가하고, 카드 JSON의 `effects`에서 id로 참조합니다.
3. 게임 로직은 **권한 있는 시뮬레이션(오프라인 게임, 방장, 전용 서버)에서만** 돕니다. 네트워크 클라이언트는 스냅샷만 그립니다. 능력 코드에서 시각 효과는 반드시 `Presentation.Fx.*`를 써야 클라이언트에도 보입니다.
4. 클라이언트와 서버의 콘텐츠가 다르면 접속이 거절됩니다(프로토콜 v3, `ContentDatabase.Hash` 비교). 콘텐츠를 바꾸면 클라이언트와 서버를 함께 배포합니다.
5. id는 소문자 `snake_case`, **파일 이름 = id** 입니다 (`Cards/toxic_rounds.json` → `"id": "toxic_rounds"`).

## 1. 콘텐츠를 넣는 두 곳

| 위치 | 용도 | 다시 빌드? |
|---|---|---|
| `Assets/Reimplementation/Resources/Content/{Cards,Maps}/<id>.json` | 게임에 내장되는 기본 콘텐츠 | 예 |
| 실행 파일 옆 `Content/{Cards,Maps}/<id>.json` (macOS는 `.app` 옆) | 빌드 없이 추가/덮어쓰기(모드). 같은 파일 이름이면 내장 콘텐츠를 덮어씀 | 아니오 |

- 명령줄 `-content <폴더>`로 다른 폴더를 지정할 수 있습니다. 전용 서버는 `server.json`의 `contentDir`.
- 에디터에서는 외부 폴더를 읽지 않습니다(`-content` 또는 `ContentDatabase.ExternalDirectory`를 지정한 경우 제외).
- 비활성화: JSON에 `"disabled": true`.

## 2. 카드 추가 / 수정

### 2.1 최소 예시

```json
{
    "id": "heavy_rounds",
    "title": "무거운 탄",
    "subtitle": "HEAVY ROUNDS",
    "description": "느리지만 아프다.",
    "rarity": "Common",
    "allowMultiple": true,
    "categories": ["gun"],
    "blacklistedCategories": [],
    "modifiers": { "damage": 1.4, "projectileSpeed": 0.7 },
    "effects": [],
    "lines": [
        { "label": "피해", "amount": "+40%", "positive": true },
        { "label": "탄속", "amount": "-30%", "positive": false }
    ]
}
```

### 2.2 필드

| 필드 | 타입 | 의미 |
|---|---|---|
| `id` | string | 고유 id = 파일 이름 |
| `title` / `subtitle` / `description` | string | 카드 앞면 문구 (한글 가능, 폰트 내장) |
| `rarity` | `Common` \| `Uncommon` \| `Rare` | 등장 가중치 10 / 4 / 1 |
| `allowMultiple` | bool | false면 이미 가진 카드는 다시 안 나옴 |
| `categories` | string[] | 이 카드의 분류 |
| `blacklistedCategories` | string[] | 이 분류의 카드를 가진 플레이어에게는 이 카드가 안 나옴 |
| `modifiers` | object | 능력치 변경 (아래 표). 생략한 값은 "변화 없음" |
| `effects` | EffectSpec[] | 능력(효과) 목록 (3장) |
| `lines` | {label, amount, positive}[] | 카드 앞면 수치 줄. 표시 전용, 실제 값과 맞춰 적을 것 |

### 2.3 `modifiers` 의미 (`Cards/StatComposer.cs`)

**곱하기(기본 1, 반드시 > 0)**: `health`, `size`, `movementSpeed`, `jump`, `reloadTimeMultiplier`, `blockCdMultiplier`, `damage`, `attackSpeed`, `projectileSpeed`, `gravity`(≥ 0), `speedMOnBounce`, `dmgMOnBounce`, `knockback`, `projectileSize`.

**더하기(기본 0)**: `ammo`(탄창, 결과는 1~99로 제한), `reloadTimeAdd`(초), `numberOfJumps`, `lifeSteal`(0.5 = 50%), `regen`(초당 회복), `respawns`, `blockCdAdd`(초), `additionalBlocks`, `blockHealing`, `numberOfProjectiles`(0~20), `reflects`(튕김 횟수), `bursts`(0~20), `spread`, `timeBetweenBullets`.

**켜기(bool)**: `unblockable`, `ignoreWalls`.

주의:

- `attackSpeed`는 원작처럼 **공격 간격 배율**입니다. 0.5면 두 배 빠르게 쏩니다.
- `damage`와 `numberOfProjectiles`를 함께 쓰면 원작 공식으로 피해가 나뉩니다: `damage = max(0.25, G·(1 − r·(1 − D)))`, `r = K/(K+N)`.
- `health`는 몸 크기와 질량도 바꿉니다 (`ConfigureMassAndSize`).
- 서버 운영자는 `server.json`의 `rules.stats`로 **시작 능력치**를, `rules.cardPool`로 **등장 카드**를 바꿀 수 있습니다. 카드 JSON의 배율은 그 위에 곱해집니다.

## 3. 능력(효과) 추가 / 수정

### 3.1 기존 능력을 카드에서 쓰기

```json
"effects": [ { "type": "poison_on_hit", "damage": 8, "duration": 3 } ]
```

| type | 매개변수 | 동작 |
|---|---|---|
| `explosion_on_hit` | `radius`(>0), `damage`(≥0), `amount`=넉백 | 내 탄이 적에게 맞거나 지형에서 소멸한 지점에서 폭발 (거리별 50%까지 감쇠, 같은 팀 무시) |
| `shockwave_on_block` | `radius`(>0), `amount`(>0)=밀어내는 속도 | 방어할 때 주변 적을 밀어냄 |
| `poison_on_hit` | `damage`(>0)=초당 피해, `duration`(>0) | 내 탄에 맞은 적을 중독 (중첩 시 갱신) |

`EffectSpec`의 공통 필드: `type, radius, damage, amount, duration, chance(기본 1), interval`. 의미는 효과마다 정하고, 효과 클래스의 주석과 `[CardEffect]` 요약에 적습니다.

### 3.2 새 능력 만들기 (C# 파일 하나)

`Assets/Reimplementation/Abilities/`에 파일을 만듭니다. 리플렉션으로 자동 등록되므로 다른 곳을 고칠 필요가 없습니다.

```csharp
using UnityEngine;
using UnityEngine.Scripting;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Abilities
{
    /// <summary>chance = 발동 확률, amount = 회복량. 적을 맞히면 확률적으로 체력을 회복한다.</summary>
    [Preserve, CardEffect("heal_on_hit", "명중 시 확률 회복: chance, amount")]
    public sealed class HealOnHit : CardEffect
    {
        public override string Validate()
        {
            if (!(Spec.amount > 0f)) return "heal_on_hit needs amount > 0";
            if (Spec.chance <= 0f || Spec.chance > 1f) return "heal_on_hit needs 0 < chance <= 1";
            return null;
        }

        public override void OnHitFighter(Projectile projectile, Fighter target)
        {
            if (Random.value > Spec.chance) return;
            Owner.Heal(Spec.amount);
            Presentation.Fx.Ring(Owner.Position, Owner.Color, 2f);
            DevLog.Verbose("[Effect] heal_on_hit +" + Spec.amount + " " + Owner.DisplayName);
        }
    }
}
```

훅 (`CardEffect`의 가상 메서드, 모두 권한 있는 시뮬레이션에서만 호출):

| 훅 | 호출 시점 |
|---|---|
| `Validate()` | 콘텐츠 로드 시 매개변수 검사. 오류 문자열 또는 null |
| `OnAttach()` / `OnDetach()` | 카드 획득 / 매치 리셋 |
| `OnTick(dt)` | 살아 있고 플레이 중일 때 매 프레임 |
| `OnRevive()` | 부활할 때 |
| `OnShoot(projectile)` | 내 탄이 생성될 때 (`projectile`의 값 수정 가능) |
| `OnHitFighter(projectile, target)` | 내 탄이 적에게 피해를 준 직후 |
| `OnHitSurface(projectile, point, normal, consumed)` | 내 탄이 지형/상자에 닿을 때. `consumed` = 탄이 사라짐 |
| `OnBlock()` | 방어 입력이 성공했을 때 |
| `OnBlockedProjectile(projectile)` | 날아온 탄을 막았을 때 |
| `OnDamaged(amount, source)` / `OnDealtDamage(target, amount)` | 피해를 받음 / 줌 |

쓸 수 있는 도구: `Owner`(Fighter: `Position, TeamId, Health, Heal, TakeDamage, AddImpulse, AddDamageOverTime, Stats`), `Explosion.Detonate(center, radius, damage, source, knockback)`, `Explosion.Push(center, radius, strength, source)`, `Fighter.Active`(살아 있는 전체 목록), `Presentation.Fx.Ring/Burst/Shake`, `DevLog.Verbose`(디버그 모드에서만 출력).

반드시 지킬 것:

- `[Preserve]`와 `[CardEffect("id", "요약")]`를 붙입니다 (Linux 서버 IL2CPP 빌드에서 제거되지 않게. `link.xml`도 어셈블리 전체를 보존).
- 인스턴스는 **카드 1장 = 효과 1개**로 플레이어마다 따로 만들어집니다. 상태는 인스턴스 필드에 두고 `static` 상태를 쓰지 않습니다.
- 매개변수가 더 필요하면 `Cards/CardRules.cs`의 `EffectSpec`에 필드를 추가합니다(모든 효과가 공유). 기존 필드 의미는 바꾸지 않습니다.
- 클라이언트에 새 모양의 시각 정보가 필요하면 `Fx` 이벤트로 표현합니다. 네트워크 스냅샷 형식(`Network/NetProtocol.cs`)을 바꾸면 `Protocol.Version`을 올립니다.

## 4. 맵 추가 / 수정

```json
{
    "id": "bridge_gap",
    "name": "끊어진 다리",
    "tutorialOnly": false,
    "solids": [
        { "x": -20, "y": -12, "w": 22, "h": 4 },
        { "x":  20, "y": -12, "w": 22, "h": 4 },
        { "x":   0, "y":  -2, "w": 8,  "h": 1.2 }
    ],
    "boxes": [ { "x": 0, "y": 0, "w": 2, "h": 2 } ],
    "spawns": [ { "x": -20, "y": -8 }, { "x": 20, "y": -8 } ]
}
```

| 필드 | 의미 |
|---|---|
| `solids` | 고정 지형 사각형. `x, y` = 중심, `w, h` = 크기 (월드 단위) |
| `boxes` | 움직이는 물리 상자 (플레이어와 탄이 밀 수 있음) |
| `spawns` | `spawns[0]` = 팀 0, `spawns[1]` = 팀 1. 2대2에서 같은 팀 두 번째 사람은 옆으로 비켜서 등장 (`MapRoot.SpawnPoint`) |
| `tutorialOnly` | true면 일반 로테이션에서 제외 (튜토리얼 맵 `training`) |

좌표계: 원점 = 화면 중앙, 위가 +y. 경기장 경계(벗어나면 사망) = x ±35.56, y ±20. 플레이어 지름 ≈ 1.24. 점프로 오를 수 있는 높이는 기존 맵(`steps`, `twin_towers`)의 발판 간격을 참고하고, 디버그 모드에서 직접 확인합니다.

검증 (`ContentDatabase.ParseMap`):

- **오류(맵 로드 안 됨)**: `solids` 없음, 스폰 2개 미만, `w/h ≤ 0`, 스폰이 경계 밖 또는 가장자리 1.5 이내, 스폰이 지형과 겹침.
- **경고(로드는 됨)**: 이름 없음, 지형이 경기장 밖, 스폰 아래에 지형이 없음(바로 추락).

서버에서 특정 맵만 쓰려면 `server.json`의 `"rules": { "mapPool": ["bridge_gap", "plateau"] }`.

## 5. 개발자 디버그 모드

켜는 방법: 게임 → **설정** → **개발자 디버그 모드** (저장됨), 또는 실행 인자 `-debug`(이번 실행만). 전용 서버는 `"debugLog": true` 또는 `-debug`.

| 기능 | 설명 |
|---|---|
| 패널 (F1) | FPS, 역할(오프라인/방장/클라이언트), 매치 단계, 맵 id, 콘텐츠 수·해시·오류 수, 플레이어별 팀·체력·탄·속도·무적(GOD)·중독(POISON)·효과 목록(fx[...]), 탄 개수, 콘텐츠 오류 문장, 최근 경고/오류 로그 |
| 월드 표시 | 경기장 경계(노란색), 지형(하늘색)/상자(주황색) 외곽선과 번호, 스폰 위치와 팀 번호, 플레이어 속도·조준 방향 선과 접지 여부, 탄 크기 원 |
| 콘텐츠 보고 | 설정 화면과 타이틀에 카드/맵 수, 오류/경고 수, 콘텐츠 해시, 외부 폴더, 오류 문장 |
| F2 | 다음 맵으로 즉시 이동 (맵 확인용) |
| F3 | P1에게 다음 카드 지급 (모든 카드를 차례로) |
| F4 | 적 전멸 (라운드 진행 확인) |
| F5 | 콘텐츠 JSON 다시 불러오기 (타이틀에서만, 재시작 없이 외부 Content 폴더 수정 반영) |
| F6 | P1 무적 |
| F7 | 슬로모션 0.25배 |
| 로그 | `[Content]`(로드/검증), `[Effect]`(효과 발동, `DevLog.Verbose`), `[Match]`, `[Net]` |

F2~F7은 **오프라인 게임(튜토리얼, CPU 대전, 관전)**에서만 동작합니다. 온라인 대전에서는 공정성을 위해 막혀 있습니다.

로그 위치: macOS `~/Library/Logs/Metadream/Metadream Shoot/Player.log`, Windows `%USERPROFILE%\AppData\LocalLow\Metadream\Metadream Shoot\Player.log`, 에디터 `~/Library/Logs/Unity/Editor.log`.

### 디버깅 순서 (새 콘텐츠가 안 보이거나 오류가 날 때)

1. 설정에서 디버그 모드를 켜고 타이틀의 콘텐츠 줄을 봅니다. 오류 수가 0이 아니면 설정 화면의 오류 문장(파일 경로 + 원인)을 고칩니다.
2. 외부 `Content` 폴더를 고친 경우 타이틀에서 F5 → 다시 확인.
3. 맵: CPU 대전 시작 → F2로 해당 맵까지 넘기고, 스폰 표시와 지형 외곽선을 확인합니다.
4. 카드/효과: CPU 대전 → F3으로 카드를 받고, 패널의 "효과" 목록과 `[Effect]` 로그로 발동을 확인합니다. F6(무적), F7(슬로모션)으로 관찰.
5. 온라인 접속이 "카드/맵 콘텐츠가 서버와 다릅니다"로 거절되면 클라이언트와 서버의 `Content` 폴더와 빌드를 맞춥니다 (설정 화면 해시 비교).

## 6. 테스트와 빌드

명령은 프로젝트 루트에서 실행합니다 (Unity CLI `unity` 1.0.0-beta.13 기준).

```bash
# 컴파일 + 씬 생성
unity run . --timeout 900 --no-tail -l /tmp/setup.log -- -executeMethod Metadream.Shoot.EditorTools.ProjectSetup.Setup
grep -E "error CS" /tmp/setup.log
# 테스트
unity test . --mode EditMode --output /tmp/edit.xml --timeout 900
unity test . --mode PlayMode --output /tmp/play.xml --timeout 900
# 빌드
unity build . --target StandaloneOSX         --execute-method Metadream.Shoot.EditorTools.ProjectSetup.BuildMac          -o "$PWD/Builds/macOS"   -l /tmp/b.log --no-tail
unity build . --target StandaloneWindows64   --execute-method Metadream.Shoot.EditorTools.ProjectSetup.BuildWindows      -o "$PWD/Builds/Windows" -l /tmp/b.log --no-tail
unity build . --target StandaloneLinux64     --execute-method Metadream.Shoot.EditorTools.ProjectSetup.BuildServerLinux  -o "$PWD/Builds/Server/Linux"   -l /tmp/b.log --no-tail
unity build . --target StandaloneWindows64   --execute-method Metadream.Shoot.EditorTools.ProjectSetup.BuildServerWindows -o "$PWD/Builds/Server/Windows" -l /tmp/b.log --no-tail
```

자동으로 검사되는 것 (`Tests/EditMode/ContentTests.cs`): 내장 콘텐츠 오류 0개, 효과 등록, 잘못된 카드/맵 거절, 맵/카드 풀, 서버 rules 보정, 마스터 서버 패킷. 새 콘텐츠를 넣고 **`BuiltinContentLoadsWithoutErrors`가 통과**하면 데이터 형식은 맞습니다.

새 능력을 만들면 PlayMode 테스트를 하나 추가합니다 (`Tests/PlayMode/TutorialAndEffectPlayModeTests.cs`의 `ExplosionAndPoisonHurtEnemiesOnly` 참고: `flow.StartSandbox`로 두 명을 세우고 훅을 직접 부르거나 효과를 발동시켜 체력 변화를 검사).

## 7. 작업 체크리스트 (에이전트용)

1. JSON 추가/수정 → 파일 이름 = id 확인.
2. 새 효과가 필요하면 `Abilities/<Name>.cs` 작성 (`[Preserve, CardEffect]`, `Validate`, 훅) → 카드 JSON `effects`에서 참조.
3. 컴파일 → `error CS` 0개.
4. EditMode + PlayMode 테스트 통과.
5. `-debug`로 실행해 F2/F3로 눈으로 확인 (필요하면 `-capture <폴더>`로 스크린샷).
6. 클라이언트 + 서버를 함께 빌드/배포 (콘텐츠 해시가 같아야 접속 가능).
7. `Docs/REVERSE_DEVELOPMENT_REPORT.md`에 원작과 다른 임의 콘텐츠임을 한 줄 기록.

## 8. 함정

- `JsonUtility`는 주석, 딕셔너리, 최상위 배열, `null` 기본값을 지원하지 않습니다. 오타 난 키는 **조용히 무시**되므로 값이 안 바뀌면 키 철자를 먼저 의심합니다.
- 카드 순서(네트워크 번호)는 id 정렬 순서입니다. id를 바꾸면 번호가 바뀌므로 클라이언트/서버를 함께 배포합니다.
- 튜토리얼(`Tutorial/TutorialController.cs`)은 `training` 맵에 의존하고, 카드 단계에서는 전체 카드 풀에서 3장을 제시합니다. `training`의 지형/스폰을 바꾸면 튜토리얼을 직접 한 번 해 봅니다 (`-tutorial` 인자로 바로 시작).
- 마스터 서버 주소는 `Network/MasterServer.cs`의 `MasterServerEndpoint.DefaultHost`(공인 IP 203.0.113.10:47800)에 하드코딩되어 있습니다. 서비스 코드는 `MasterServer/` 폴더입니다.

