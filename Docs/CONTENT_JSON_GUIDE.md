# 맵 · 스킬(카드) JSON 작성 가이드

새 맵과 새 스킬(카드)은 **JSON 파일 하나**로 추가합니다. C# 코드는 새로운 *동작*(효과)이 필요할 때만 씁니다 ([EXTENDING.md](EXTENDING.md) 3.2).
이 문서는 JSON만 다룹니다. 바로 복사해 쓸 수 있는 템플릿은 맨 아래에 있습니다.
맵 하나에서만 동작하는 기믹(스크립트 `"script"`, C# 맵 동작 `"behaviors"`, 트리거 영역 `"areas"`, 오브젝트 `"name"`)은 [MAP_SCRIPTING.md](MAP_SCRIPTING.md)를 보세요.

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


## 7. 기믹 (v5 추가)

### 7.1 카드 효과 전체 목록

`chance`(0~1, 기본 1)는 발동 확률이며 지원하는 효과에만 의미가 있습니다. 한 카드에 효과를 여러 개 넣을 수 있습니다.

| type | 값 | 동작 | 예시 카드 |
|---|---|---|---|
| `explosion_on_hit` | radius, damage, amount(넉백) | 명중/착탄 지점 폭발 | 폭발탄 |
| `shockwave_on_block` | radius, amount | 방어 시 밀어내기 | 충격파 |
| `poison_on_hit` | damage(초당), duration | 명중 시 중독 | 독, 기생충 |
| `homing` | amount(초당 회전 각도, 기본 240), radius(탐지 거리) | 탄이 가까운 적을 따라감 | 유도탄 |
| `grow_damage` | amount(초당 +비율) | 날아갈수록 피해 증가 | 성장 |
| `slow_on_hit` | amount(감속 0~0.95), duration, chance | 명중 시 감속/기절 | 냉기탄, 현혹, 속박 |
| `push_on_hit` | amount(속도) | 명중 시 탄 방향으로 밀어냄 | 추진기, 강타 |
| `timed_bomb` | duration(지연), radius, damage, amount | 착탄 지점 시한폭탄 | 시한 폭발 |
| `health_cost_on_shoot` | damage(발당) | 쏠 때 체력 소모 | 악마의 계약 |
| `slow_aura` | radius, amount | 주변 적 지속 감속 | 냉기 |
| `drain_aura` | radius, damage(초당), amount(회복 비율) | 주변 적 지속 피해·흡수 | 흡혈귀, 광휘 |
| `blast_on_block` | radius, damage, amount, chance | 방어 시 주변 폭발 | 압도, 톱날, 폭탄 투하 |
| `pull_on_block` | radius, amount | 방어 시 끌어당김 | 블랙홀, 내파 |
| `slow_on_block` | radius, amount, duration | 방어 시 주변 감속 | 서리 강타, EMP, 침묵 |
| `heal_on_block` | amount | 방어 시 회복 | 치유의 장 |
| `teleport_on_block` | amount(거리) | 방어 시 조준 방향 순간이동(벽 앞에서 멈춤) | 순간이동 |
| `dash_on_block` | amount(속도) | 방어 시 돌진 | 방패 돌격 |
| `reload_on_block` | - | 방어 시 즉시 재장전 | 전술 재장전 |
| `empower_on_block` | amount(피해 배율), duration(0 = 다음 탄까지) | 방어 후 다음 탄 강화 | 강화 |
| `block_on_empty` | - | 마지막 탄을 쏘면 자동 방어 | 방패 올려 |
| `reload_on_damage` | - | 피해를 주면 즉시 재장전 | 약탈자 |
| `block_on_damage` | - | 피해를 주면 방어 대기 초기화 | 재충전 |
| `speed_after_damage` | amount(+비율), duration | 피해를 주면 잠시 빨라짐 | 피의 맛 |

예 — 방어하면 끌어당긴 뒤 기절:

```json
"effects": [
    { "type": "pull_on_block", "radius": 14, "amount": 30 },
    { "type": "slow_on_block", "radius": 6, "amount": 0.9, "duration": 1.2 }
]
```

### 7.2 맵 기믹

| 키 | 필드 | 동작 |
|---|---|---|
| `solids`/`boxes`의 `r` | 각도(도, 반시계) | 기울어진 지형·상자 |
| `movers` | `x, y, w, h, r`, `path`(시작점 기준 오프셋 배열, 2개 이상), `speed`(기본 6), `wait`(각 지점 대기 초, 기본 1) | 경로를 반복해서 오가는 움직이는 발판. 온라인에서도 위치가 맞춰짐 |
| `pads` | `x, y, w, h, r`, `power`(발사 속도, 기본 32) | 점프대. 회전하면 그 방향으로 튕김 |
| `hazards` | `x, y, w, h, r`, `dps`(초당 피해, 기본 40) | 용암·가시 같은 피해 구역 (방어 불가) |
| `winds` | `x, y, w, h, r`, `fx, fy`(가속도) | 바람·상승 기류 구역 |
| `origin` | 문자열 | 출처 메모 (예: `"ROUNDS level12"`) |

```json
"movers":  [ { "x": -9, "y": -12, "w": 7, "h": 1.2, "path": [ {"x":0,"y":0}, {"x":0,"y":13} ], "speed": 5, "wait": 1 } ],
"pads":    [ { "x": -30, "y": -10.2, "w": 3, "h": 0.6, "power": 34 } ],
"hazards": [ { "x": 0, "y": -18.5, "w": 72, "h": 3, "dps": 60 } ],
"winds":   [ { "x": -17, "y": 0, "w": 10, "h": 24, "fx": 0, "fy": 26 } ]
```

검증: 기믹 영역은 `w, h > 0`, 점프대 `power > 0`, 피해 구역 `dps > 0`, 움직이는 발판은 `path` 2개 이상·`speed > 0`, `boxes + movers`는 최대 120개(온라인 스냅샷 크기).
예시 맵: `gimmick_lava_lifts`(용암 승강장), `gimmick_wind_canyon`(바람 협곡), `gimmick_spike_pit`(가시 구덩이).

### 7.3 원작 ROUNDS 콘텐츠

- `rounds_*.json` 카드 69장과 맵 33개는 원작 에셋(CardInfo/Gun/CharacterStatModifiers/Block, Map 씬의 BoxCollider2D·Rigidbody2D·SpawnPoint·MoveSequence)에서 변환했습니다. 능력치는 원작 값, 특수 효과는 위 효과로 근사합니다(설명에 "원작:"으로 차이를 적음).
- 원작 카드 중 10장(DIVINE COUNTDOWN 미완성, AMMO REG, SNEAKY, RADAR SHOT, BRAWLER, DECAY, PRISTINE PERSEVERANCE, REMOTE, ABYSSAL COUNTDOWN)과 로프·파괴 상자·물리 상자 60개 초과 맵은 아직 재현하지 않았습니다.
- 한 포인트가 45초를 넘으면 **서든 데스**(모두 점점 체력 감소)가 시작돼 복잡한 맵에서도 포인트가 끝납니다.

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
| `image` | 문자열 | | 카드 id | 카드 그림 파일 이름 (`Content/CardArt/<이름>.png`). 규격 260×112(권장 1040×448)은 [CARD_ART.md](CARD_ART.md) |
| `imageMode` | `fill` · `icon` | | fill | fill = 그림 영역 전체, icon = 가운데 원 안 80×80(투명 PNG 권장 320×320) |
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

체력 150 · 탄 피해 50 · 탄속 70 · 연사 간격 0.3초 · 탄창 3 · 재장전 1.5초 · 방어 대기 4초 · 점프 1회. (2026-10-22 조정: 원작 관찰값 체력 100·피해 55에서 변경)
→ 기본 탄 3발이면 쓰러집니다. `damage` 1.5 이상(50 × 1.5 = 75, 두 발 150)이면 두 발, 3.0 이상이면 한 발에 쓰러지므로 강한 단점과 묶으세요.
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
| `spawns` | 최소 2개. `spawns[0]` = 주황 팀, `spawns[1]` = 파랑 팀. 2대2 두 번째 플레이어는 `spawns[2]`(주황)·`spawns[3]`(파랑)이 있으면 그곳("준비" 배치부터), 없으면 팀 스폰에서 중앙 쪽으로 2.6 옆. `spawns[4]` 이후는 쓰이지 않음(경고) |
| `tutorialOnly` | true면 일반 게임 맵 순환에서 제외 |
| `disabled` | true면 불러오지 않음 |
| `name` (각 오브젝트) | 맵 스크립트에서 부를 이름 (선택). [MAP_SCRIPTING.md](MAP_SCRIPTING.md) |
| `areas` | 보이지 않는 트리거 사각형 `{name, x, y, w, h, r, show, color}` |
| `script` | 맵 스크립트 (문자열 배열, 한 줄 = 한 문장). 빌드 없이 수정 가능 |
| `behaviors` | C# 맵 동작 `[{ "type": "spin", "target": "blade", "speed": 40 }]` |

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

