# 맵 스크립트 가이드 — 맵 고유 기믹 만들기

맵 하나에서만 동작하는 기믹(차오르는 용암, 스위치로 열리는 문, 맞히면 멈추는 포탑 …)을 만드는 방법은 두 가지입니다.

| | ① 맵 스크립트 (`"script"`) | ② C# 맵 동작 (`"behaviors"`) |
|---|---|---|
| 코드 위치 | 맵 JSON 안 (줄 단위 문자열 배열) | `Assets/Reimplementation/Maps/Scripting/*.cs` |
| 빌드 | **필요 없음** — 게임 폴더 `Content/Maps/<id>.json`만 바꾸면 됨 | 필요 |
| 할 수 있는 일 | 아래 명령 목록(게임 동작)만 | C#이지만 `Api`(MapScriptApi)를 통해서만 맵을 바꿈 |
| 잘 맞는 경우 | 맵마다 다른 규칙·연출·트리거 | 여러 맵에서 재사용할 정형화된 기믹(회전, 깜빡임, 포탑) |

둘 다 **방장·전용 서버·오프라인 게임에서만 실행**됩니다. 참가자와 리플레이는 결과(이름 붙은 오브젝트의 위치·회전·크기·보임·색, HUD 문구, 배너, 효과, 소리, 피해)를 스냅샷으로 받습니다(프로토콜 v8). 그래서 모든 사람의 화면이 같고, 리플레이도 실제 게임과 같습니다.

예시 맵: `script_rising_lava`(차오르는 용암), `script_switch_gate`(스위치 성문), `script_turret_core`(포탑 코어 — C# 동작 + 스크립트).

---

## 1. 오브젝트에 이름 붙이기 · 트리거 영역

스크립트는 **`"name"`이 있는 오브젝트만** 다룰 수 있습니다. `solids`, `boxes`, `movers`, `pads`, `hazards`, `winds` 항목 어디에나 `"name"`을 붙일 수 있습니다.

```json
"solids":  [ { "name": "gate", "x": 0, "y": -1, "w": 2, "h": 19 } ],
"hazards": [ { "name": "lava", "x": 0, "y": -18.5, "w": 72, "h": 3, "dps": 40 } ],
"areas":   [ { "name": "plate_left", "x": -29, "y": -10, "w": 4, "h": 1, "show": true, "color": "#40ff9060" } ]
```

- 이름 규칙: 영문/한글 글자·숫자·`_`, 숫자로 시작 불가, 맵 안에서 중복 불가, 명령 이름(`move` 등)·예약어 사용 불가. 맵당 최대 64개.
- `"areas"`: **보이지 않는 트리거 사각형**(충돌 없음). `"show": true`면 반투명하게 그려지고 `"color"`로 색을 지정합니다. 숨겨도(`hide`) 트리거는 계속 동작합니다.
- 이름 붙은 고정 지형(`solids`)은 움직일 수 있게 만들어지며, 위에 선 플레이어를 함께 실어 나릅니다.
- `hide`한 지형·상자·발판은 충돌도 사라집니다. 숨긴 `pads/hazards/winds`는 효과가 꺼집니다.

## 2. 맵 스크립트 (`"script"`)

맵 JSON에 `"script"`를 **한 줄에 문자열 하나**로 적습니다. 따옴표는 `\"`로 씁니다.

```json
"script": [
  "var level = 0",
  "every 10",
  "  level = level + 1",
  "  move_by(lava, 0, 1.5, 2)",
  "  resize(lava, lava.w, lava.h + 3, 2)",
  "  banner(\"용암이 차오릅니다! \" + level + \"단계\", 1.5, \"#ff7040\")",
  "end"
]
```

### 2.1 문법

- 한 줄에 문장 하나. 들여쓰기는 자유(읽기 좋게). `#` 또는 `//` 뒤는 주석.
- 맨 바깥에는 **변수 선언**과 **이벤트 블록**만 올 수 있습니다. 블록은 `end`로 닫습니다.
- 값: 숫자(`3`, `1.5`), 글자(`"text"`), `true`/`false`(1/0), `none`, 오브젝트 이름, `player`.
- 연산: `+ - * / %`, 비교 `== != < <= > >=`, `and or not`, 괄호. 글자에 `+`하면 이어 붙입니다(`"단계 " + level`).
- 변수는 맨 위에서 `var 이름 = 값`으로 선언해야 쓸 수 있습니다(오타를 잡기 위해). 모든 변수는 맵(한 포인트) 동안 유지되고, 다음 포인트의 새 맵에서는 처음부터 다시 시작합니다.

```
var count = 0                 # 변수 선언 (맨 위)

on start ... end              # 맵이 시작될 때 1번
on tick ... end               # 물리 프레임마다 (dt = 프레임 시간)
every 5 ... end               # 5초마다 (5, 10, 15 …초)
after 30 ... end              # 30초에 1번
on enter 영역 ... end          # 플레이어가 들어올 때 (player = 그 플레이어)
on exit 영역 ... end           # 나갈 때 (죽거나 사라져도)
on inside 영역 ... end         # 안에 있는 동안 매 프레임 (dt 사용)
on death ... end              # 누군가 죽었을 때 (player = 죽은 사람)
on shot 오브젝트 ... end        # 탄이 이름 붙은 오브젝트에 맞았을 때 (player = 쏜 사람 또는 none, event_x/event_y = 맞은 곳)

if 조건 / elif 조건 / else / end
repeat 3 ... end              # 최대 1000번
foreach player ... end        # 살아 있는 모든 플레이어에 대해 (player = 각 플레이어)
stop                          # 이 블록 실행을 끝냄
문.y = 5                      # 오브젝트 속성 바꾸기 (즉시)
```

- `enter/exit/inside`의 영역은 `areas`뿐 아니라 이름 붙은 어떤 오브젝트든 됩니다(숨긴 지형·구역은 제외).
- 내장 값: `time`(맵 시작 후 초), `dt`, `event_x`, `event_y`, `player`(플레이어 이벤트와 `foreach` 안에서만).

### 2.2 속성

| 대상 | 읽기 | 쓰기 (`대상.속성 = 값`) |
|---|---|---|
| 오브젝트 | `x y rotation w h visible power dps fx fy name` | `x y rotation w h visible power dps fx fy` |
| 플레이어 | `x y vx vy hp maxhp team id alive grounded name` | 없음 (명령 사용) |

`power`=점프대 힘, `dps`=피해 구역 초당 피해, `fx/fy`=바람 가속도. 좌표는 맵 JSON과 같은 좌표계입니다.

### 2.3 명령 (게임 동작만 — 이것 말고는 아무것도 할 수 없습니다)

| 명령 | 설명 |
|---|---|
| `move(obj, x, y, 초?)` / `move_by(obj, dx, dy, 초?)` | 위치로 이동 / 만큼 이동. 초를 주면 부드럽게. 상자는 순간이동. 발판은 경로의 기준점이 이동 |
| `rotate(obj, 도, 초?)` / `rotate_by(obj, 도, 초?)` | 회전 (반시계 +) |
| `resize(obj, w, h, 초?)` | 크기 변경 |
| `show(obj)` `hide(obj)` `toggle(obj)` | 보이기/숨기기(충돌·효과 포함) |
| `color(obj, "#ff8800")` / `color(obj, r, g, b, a?)` / `reset_color(obj)` | 색 (0~1 또는 #hex, 이름 `"red"`도 가능) |
| `damage(p, 양)` `heal(p, 양)` `kill(p)` | 피해(방어 불가)/회복/처치 |
| `push(p, vx, vy)` `teleport(p, x, y)` | 밀기(속도 더하기)/순간이동 |
| `slow(p, 배율, 초)` `poison(p, 초당피해, 초)` | 이동 속도 배율 / 지속 피해 |
| `explode(x, y, 반경, 피해, 넉백?)` | 폭발 (모든 플레이어에게, 방어 불가) |
| `shockwave(x, y, 반경, 힘)` | 밀어내기만 |
| `shoot(x, y, dx, dy, 속도?, 피해?, 중력?)` | 주인 없는 탄 발사 (모두에게 맞음, 방어 가능). 맵당 동시에 최대 60발. 기본 속도 40, 피해 20, 중력 0 |
| `effect("ring"\|"burst"\|"spark"\|"shake"\|"death", x, y, 크기?, "#색"?)` | 시각 효과 |
| `sound("id")` / `sound("id", x, y)` | 효과음 ([SOUNDS.md](SOUNDS.md)의 id) |
| `banner("글", 초?, "#색"?)` | 화면 가운데 큰 글씨 |
| `hud("글")` | 점수 아래 상시 안내 줄 (`""`이면 지움) |
| `log("글")` | 개발자 디버그 로그에만 출력 |

모든 숫자는 안전한 범위로 잘립니다(좌표 ±200, 피해 ≤ 10000, 시간 ≤ 600초 등). `p`가 `none`이면(예: `nearest`가 아무도 못 찾음) 아무 일도 하지 않습니다.

### 2.4 함수 (값을 돌려줌)

`random(a, b)` `randint(a, b)` `chance(0.3)` `min` `max` `abs` `floor` `ceil` `round` `sqrt` `sin(도)` `cos(도)` `atan2(y, x)` `clamp(v, a, b)` `dist(x1, y1, x2, y2)` `players()` `alive(팀)` `nearest(x, y)` `random_player()` `inside(p, obj)` `text(값, 소수자리?)`

### 2.5 안전 장치

- 스크립트는 **게임이 제공하는 명령만** 부를 수 있습니다. 파일·네트워크·다른 프로그램·C# 코드에는 접근할 수 없습니다(자체 해석기, 리플렉션 없음).
- 무한 반복 방지: 블록 한 번 실행에 5000단계, 한 프레임에 30000단계까지. `repeat`는 최대 1000번, `every`는 최소 0.05초.
- 실행 중 오류(0으로 나누기, 숫자 자리에 글자 등)나 단계 초과가 나면 **그 맵의 스크립트만 멈추고 게임은 계속**됩니다. 이유와 줄 번호는 디버그 오버레이에 `script STOPPED script line N: …`으로 나옵니다.
- 문법 오류는 콘텐츠를 불러올 때 잡혀서 그 맵은 불러오지 않습니다(설정 → 디버그 모드의 콘텐츠 오류 목록, 오버레이 빨간 줄).

### 2.6 예시

**스위치 문** (`script_switch_gate` 요약)

```
var open_until = 0
var opened = false
on enter plate_left
  open_until = max(open_until, time + 4)
end
on shot bell
  open_until = max(open_until, time + 6)
end
on tick
  if time < open_until and not opened
    opened = true
    move(gate, 0, -20, 0.6)
  elif time >= open_until and opened
    opened = false
    move(gate, 0, -1, 0.6)
  end
end
```

**떨어지는 운석**

```
var mx = 0
every 3
  mx = random(-30, 30)
  effect("ring", mx, -12, 3, "#ff5030")
  explode(mx, -12, 3, 20, 25)
  sound("explosion", mx, -12)
end
```

**가장 가까운 사람에게 포탑 발사**

```
var target = none
every 2
  target = nearest(0, 10)
  if target != none
    shoot(0, 8, target.x, target.y - 8, 45, 15)
  end
end
```

## 3. C# 맵 동작 (`"behaviors"`)

```json
"behaviors": [
  { "type": "turret", "target": "core", "interval": 2.5, "delay": 1.5, "speed": 38, "damage": 18 },
  { "type": "spin",   "target": "blade", "speed": 40 },
  { "type": "blink",  "target": "bridge", "interval": 4, "sound": "block" }
]
```

공통 필드: `type target area speed amount interval delay duration damage radius x y text sound` (안 쓰는 필드는 무시).

| type | 동작 |
|---|---|
| `spin` | target을 초당 speed도(기본 45) 회전 |
| `blink` | interval초마다 target 보이기/숨기기 (delay 후 시작, sound) |
| `rise` | delay초 후 target을 초당 speed만큼 위로, 최대 amount (음수 speed = 아래로) |
| `turret` | target(없으면 x, y)에서 interval초마다 가장 가까운 플레이어에게 발사 (speed, damage). target을 숨기면 멈춤 |
| `bounce_area` | area에 들어온 플레이어를 위로 speed(기본 40) 튕김 |

### 새 C# 동작 만들기 (파일 하나)

`Assets/Reimplementation/Maps/Scripting/`에 파일을 추가합니다. 등록은 자동입니다.

```csharp
using UnityEngine;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Maps.Scripting
{
    [MapBehavior("gravity_flip", "area 안에 있는 동안 위로 끌어올림 (amount = 가속도)")]
    public sealed class GravityFlipBehavior : MapBehavior
    {
        public override bool NeedsArea => true;                       // 불러올 때 "area" 필수 검사
        public override string Validate() => Spec.amount > 0f ? null : "gravity_flip needs amount > 0";

        public override void OnTick(float dt)
        {
            foreach (var f in Api.Players)
                if (Area.Contains(Api.ToLocal(f.Position))) Api.Push(f, new Vector2(0f, Spec.amount * dt));
        }
    }
}
```

- 훅: `OnStart`, `OnTick(dt)`, `OnEnter(area, fighter)`, `OnExit`, `OnDeath(fighter)`, `OnShot(obj, projectile, point)`.
- 맵을 바꿀 때는 **반드시 `Api`(MapScriptApi)** 를 쓰세요. Api 밖에서 Transform 등을 직접 바꾸면 참가자·리플레이에 전달되지 않습니다.
- 예외가 나면 그 동작만 제거되고 게임은 계속됩니다(오버레이 `behaviors … STOPPED`).
- 스크립트 명령을 새로 추가하려면 `MdsLibrary.cs`의 `Signatures`와 `Call`에 한 줄씩, 필요한 기능은 `MapScriptApi`에 추가하고 이 문서 표를 갱신합니다.

## 4. 디버깅

1. 설정에서 **디버그 모드**를 켭니다.
2. 콘텐츠 오류: 설정 화면/오버레이에 `[오류] Maps/<id>.json: script line 7: unknown name 'lavaa' … → "move_by(lavaa, 0, 1)"` 처럼 **줄 번호와 그 줄의 내용**이 나옵니다.
3. 콘텐츠 도감 → 맵: 스크립트 줄 수, 핸들러 수, C# 동작 수, 이름 붙은 오브젝트 수를 확인하고 "이 맵에서 치트 대전"으로 바로 실행합니다.
4. 실행 중 오버레이 둘째 줄: `script 31 lines · 4 handlers · runs 120 · level=2 next_rise=30 · t=12.3 · hud "…"`. 멈추면 `script STOPPED script line N: 이유`.
5. `log("…")`는 디버그 모드에서만 로그 창에 찍힙니다. F5로 콘텐츠를 다시 불러올 수 있습니다(타이틀).

## 5. 자주 나는 오류

| 메시지 | 원인 / 해결 |
|---|---|
| `unknown name 'x'` | 변수를 `var x = 0`으로 맨 위에 선언하지 않았거나, 오브젝트에 `"name"`이 없음 |
| `block is missing its 'end'` | `if/repeat/foreach/on …` 블록을 `end`로 닫지 않음 |
| `'player' only exists in …` | `on start/tick/every/after`에서 `player` 사용 → `foreach player`로 감싸기 |
| `'explode' takes 4–5 arguments` | 인자 개수 확인 (2.3 표) |
| `returns a value` | `abs(3)`처럼 값을 버리는 줄 → `x = abs(3)` |
| `too many steps` | `repeat` 중첩·횟수가 너무 큼 |
| `… needs a number but got none` | `nearest()` 결과가 none일 수 있음 → `if target != none` |

