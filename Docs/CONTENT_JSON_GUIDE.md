# 맵 · 스킬(카드) JSON 작성 가이드

새 맵과 새 스킬(카드)은 **JSON 파일 하나**로 추가합니다. C# 코드는 새로운 *동작*(효과)이 필요할 때만 씁니다 ([EXTENDING.md](EXTENDING.md) 3.2).
이 문서는 JSON만 다룹니다. 바로 복사해 쓸 수 있는 템플릿은 맨 아래에 있습니다.

## 1. 파일을 두는 곳

| 위치 | 언제 | 빌드 필요 |
|---|---|---|
| `Assets/Reimplementation/Resources/Content/Cards/<id>.json` · `.../Maps/<id>.json` | 게임에 기본 포함 | 예 |
| 게임 폴더의 `Content/Cards/<id>.json` · `Content/Maps/<id>.json` (Windows: exe 옆, macOS: .app 옆, 서버: 실행 파일 옆) | 빌드 없이 추가·덮어쓰기 | 아니오 |

- **파일 이름 = id** (소문자 영문·숫자·밑줄, 예 `ice_rounds.json` → `"id": "ice_rounds"`).
- 같은 이름의 파일을 `Content` 폴더에 두면 내장 카드/맵을 덮어씁니다. `"disabled": true`로 끌 수 있습니다.
- 온라인 게임은 **방장/서버와 참가자의 콘텐츠가 똑같아야** 접속됩니다(다르면 "카드/맵 콘텐츠가 서버와 다릅니다"). 같은 `Content` 폴더를 모두에게 나눠 주세요.
- 인코딩은 UTF-8. 주석(`//`)과 마지막 쉼표는 쓸 수 없습니다. **오타 난 키는 조용히 무시**되니 값이 안 바뀌면 철자부터 확인하세요.

## 2. 스킬(카드) JSON

```json
{
    "id": "ice_rounds",
    "title": "얼음탄",
    "subtitle": "ICE ROUNDS",
    "description": "맞은 적이 잠시 얼어붙듯 느려진다.",
    "rarity": "Uncommon",
    "allowMultiple": true,
    "categories": ["gun"],
    "blacklistedCategories": [],
    "modifiers": { "damage": 0.9, "projectileSpeed": 1.2 },
    "effects": [ { "type": "poison_on_hit", "damage": 4, "duration": 3 } ],
    "lines": [
        { "label": "탄속", "amount": "+20%", "positive": true },
        { "label": "중독", "amount": "4/초", "positive": true },
        { "label": "피해", "amount": "-10%", "positive": false }
    ]
}
```

### 2.1 기본 필드

| 키 | 타입 | 필수 | 기본값 | 설명 |
|---|---|---|---|---|
| `id` | 문자열 | 예 | 파일 이름 | 고유 id. 파일 이름과 같게 |
| `title` | 문자열 | 예 | | 카드 이름 (한글 가능) |
| `subtitle` / `description` | 문자열 | | "" | 부제 / 설명 |
| `rarity` | `Common` · `Uncommon` · `Rare` | | Common | 등장 가중치 10 : 4 : 1 |
| `allowMultiple` | true/false | | true | false면 이미 가진 카드는 다시 안 나옴 |
| `categories` | 문자열 배열 | | [] | 분류 이름 (자유롭게) |
| `blacklistedCategories` | 문자열 배열 | | [] | 이 분류의 카드를 가진 사람에게는 이 카드가 안 나옴 |
| `modifiers` | 객체 | | 변화 없음 | 능력치 변경 (2.2) |
| `effects` | 배열 | | [] | 특수 효과 (2.3) |
| `lines` | 배열 | 권장 | [] | 카드 앞면 수치 줄 `{label, amount, positive}`. **표시 전용**이므로 실제 값과 맞춰 적기 |
| `disabled` | true/false | | false | true면 불러오지 않음 |

### 2.2 `modifiers` (생략한 키 = 변화 없음)

**곱하기** — 기본 1, 반드시 0보다 큼 (1.2 = +20%, 0.8 = -20%)

| 키 | 의미 |
|---|---|
| `health` | 최대 체력 (몸 크기·무게도 같이 커짐) |
| `size` | 몸 크기 |
| `movementSpeed` · `jump` | 이동 속도 · 점프 힘 |
| `damage` | 탄 피해 (산탄과 함께 쓰면 탄 수로 나뉨) |
| `attackSpeed` | **연사 간격** 배율 — 0.5면 두 배 빠르게 쏨 |
| `projectileSpeed` · `projectileSize` | 탄속 · 탄 크기 |
| `gravity` | 탄 중력 (0 허용 = 직선) |
| `reloadTimeMultiplier` · `blockCdMultiplier` | 재장전 시간 · 방어 대기 시간 (작을수록 빠름) |
| `knockback` · `speedMOnBounce` · `dmgMOnBounce` | 넉백 · 튕길 때 속도/피해 배율 |

**더하기** — 기본 0

| 키 | 의미 | 범위 |
|---|---|---|
| `ammo` | 탄창 +N (결과 1~99) | |
| `reloadTimeAdd` · `blockCdAdd` | 재장전/방어 대기 +초 | |
| `numberOfJumps` | 추가 점프 횟수 | |
| `lifeSteal` | 흡혈 비율 (0.5 = 피해의 50%) | 0~5 권장 |
| `regen` | 초당 회복 | |
| `respawns` | 한 포인트 안에서 부활 횟수 | |
| `additionalBlocks` · `blockHealing` | 추가 방어 · 방어 시 회복 | |
| `numberOfProjectiles` | 한 번에 추가로 쏘는 탄 수 | 0~20 |
| `bursts` · `timeBetweenBullets` | 연발 횟수 · 연발 간격(초) | bursts 0~20 |
| `reflects` | 벽 튕김 횟수 | |
| `spread` | 탄 퍼짐 | |

**켜기** — `unblockable`(방어 불가 탄), `ignoreWalls`(벽 관통): `true`

### 2.3 `effects` — 특수 효과

```json
"effects": [ { "type": "explosion_on_hit", "radius": 3.5, "damage": 25, "amount": 8 } ]
```

| type | 필요한 값 | 동작 |
|---|---|---|
| `explosion_on_hit` | `radius` > 0, `damage` ≥ 0, `amount` = 넉백 | 내 탄이 적에 맞거나 지형에서 사라진 곳에서 폭발. 가장자리는 피해 50%. 같은 팀은 안 맞음 |
| `shockwave_on_block` | `radius` > 0, `amount` > 0 (밀어내는 속도) | 방어할 때 주변 적을 밀어냄 |
| `poison_on_hit` | `damage` > 0 (초당), `duration` > 0 (초) | 내 탄에 맞은 적을 중독 |

공통 키: `type, radius, damage, amount, duration, chance`(기본 1), `interval`. 효과마다 쓰는 키만 의미가 있습니다.
목록에 없는 동작이 필요하면 C# 효과 클래스를 하나 추가합니다 ([EXTENDING.md](EXTENDING.md) 3.2). 그 뒤에는 JSON에서 새 `type`을 똑같이 씁니다.

### 2.4 밸런스 참고 (기본 능력치)

체력 100 · 탄 피해 55 · 탄속 70 · 연사 간격 0.3초 · 탄창 3 · 재장전 1.5초 · 방어 대기 4초 · 점프 1회.
→ 기본 탄 2발이면 쓰러집니다. `damage` 1.82 이상(55 × 1.82 ≥ 100)이면 한 발에 쓰러지므로 강한 단점과 묶으세요.
서버 운영자는 `server.json`의 `rules.stats`로 이 기본값을, `rules.cardPool`로 등장 카드를 바꿀 수 있습니다.

## 3. 맵 JSON

```json
{
    "id": "twin_bridges",
    "name": "쌍둥이 다리",
    "tutorialOnly": false,
    "solids": [
        { "x": -22, "y": -14, "w": 20, "h": 6 },
        { "x":  22, "y": -14, "w": 20, "h": 6 },
        { "x":   0, "y":  -6, "w": 10, "h": 1.2 },
        { "x":   0, "y":   6, "w": 6,  "h": 1.2 }
    ],
    "boxes": [ { "x": 0, "y": -3, "w": 2, "h": 2 } ],
    "spawns": [ { "x": -22, "y": -9 }, { "x": 22, "y": -9 } ]
}
```

| 키 | 설명 |
|---|---|
| `id` / `name` | 파일 이름과 같은 id / 화면에 보이는 이름 |
| `solids` | 고정 지형 사각형 목록. `x, y` = **중심**, `w, h` = 너비/높이 (> 0) |
| `boxes` | 움직이는 물리 상자 (밀 수 있음, 탄에 맞으면 움직임) |
| `spawns` | 최소 2개. `spawns[0]` = 주황 팀, `spawns[1]` = 파랑 팀. 2대2에서 팀원은 같은 위치 옆에 섬 |
| `tutorialOnly` | true면 일반 게임 맵 순환에서 제외 |
| `disabled` | true면 불러오지 않음 |

### 3.1 좌표계

```
          y = +20 (위쪽 경계)
             ┌──────────────────────────────┐
x = -35.56   │            (0,0)             │   x = +35.56
             │              ·               │
             └──────────────────────────────┘
          y = -20 (아래쪽 경계, 떨어지면 사망)
```

- 원점은 화면 중앙, 위쪽이 +y. 경계 밖으로 나가면 사망합니다.
- 플레이어 지름 ≈ 1.24. 스폰은 **바닥 지형 윗면보다 2~5 위**에 두세요 (예: 윗면 y = -11이면 스폰 y = -9).
- 지형 윗면 = `y + h/2`, 아랫면 = `y - h/2`.
- 점프로 올라갈 수 있는 발판 간격은 기존 맵(`steps`, `twin_towers`)을 참고하고 디버그 모드로 직접 확인하세요.

## 4. 검증 메시지와 고치는 법

게임은 불러올 때 JSON을 검사합니다. **오류**가 있는 파일은 불러오지 않고, **경고**는 불러오되 알려줍니다.
설정 → 개발자 디버그 모드를 켜면 타이틀/설정 화면에 오류 수와 문장이 보이고, 타이틀에서 **F5**로 다시 불러올 수 있습니다.

| 메시지 (일부) | 원인 | 고치는 법 |
|---|---|---|
| `invalid JSON` | 문법 오류 (쉼표, 따옴표, 주석) | JSON 검사기로 확인 |
| `id '...' differs from file name` (경고) | id와 파일 이름 다름 | 둘을 같게 |
| `"title" is empty` | 카드 이름 없음 | `title` 추가 |
| `unknown rarity` | 등급 철자 | Common / Uncommon / Rare |
| `modifiers.X must be > 0` | 곱하기 값이 0 이하 | 1 = 변화 없음, 0.5 = 절반 |
| `effects[0]: unknown effect type` | 없는 효과 이름 | 2.3 표의 type 사용 |
| `effects[0]: ... needs radius > 0` | 효과에 필요한 값 누락 | 2.3 표의 필요한 값 채우기 |
| `no "lines"` (경고) | 카드 앞면 수치 없음 | `lines` 추가 |
| `needs at least 2 spawns` | 스폰 부족 | 팀마다 하나씩 |
| `spawns[i] overlaps solids[j]` | 스폰이 지형 안 | 스폰을 지형 위로 |
| `spawns[i] ... outside or too close to the arena edge` | 경계 밖/가까움 | 경계에서 1.5 이상 안쪽 |
| `spawns[i] has no solid below it` (경고) | 스폰 아래 바닥 없음 | 아래에 지형 추가 |
| `... completely outside the arena` (경고) | 지형이 화면 밖 | 좌표 확인 |
| `duplicate card/map id` | 같은 id 두 개 | id 변경 |

## 5. 확인 순서

1. JSON 저장 → 게임 실행 (외부 `Content` 폴더라면 빌드 불필요).
2. 설정 → **개발자 디버그 모드** 켜기 → 타이틀 아래 "콘텐츠: 카드 N · 맵 N · 오류 0" 확인.
3. **CPU와 대전** 시작 → 맵은 **F2**로 넘기며 확인(지형 외곽선·스폰 위치 표시), 카드는 **F3**으로 받아 확인(패널의 `fx[...]`에 효과 표시). **F6** 무적, **F7** 슬로모션.
4. 내장 콘텐츠로 넣었다면 EditMode 테스트 `BuiltinContentLoadsWithoutErrors`가 통과하는지 확인.
5. 온라인으로 쓸 때는 서버와 모든 참가자에게 같은 파일을 배포.

## 6. 템플릿

**카드**

```json
{
    "id": "my_card",
    "title": "카드 이름",
    "subtitle": "MY CARD",
    "description": "설명",
    "rarity": "Common",
    "allowMultiple": true,
    "categories": [],
    "blacklistedCategories": [],
    "modifiers": {},
    "effects": [],
    "lines": [ { "label": "능력치", "amount": "+0%", "positive": true } ]
}
```

**맵**

```json
{
    "id": "my_map",
    "name": "맵 이름",
    "tutorialOnly": false,
    "solids": [ { "x": 0, "y": -14, "w": 50, "h": 6 } ],
    "boxes": [],
    "spawns": [ { "x": -15, "y": -9 }, { "x": 15, "y": -9 } ]
}
```

