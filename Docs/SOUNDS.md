# 효과음 수정 가이드

게임 코드는 효과음을 **이름(id)** 으로만 부릅니다. 소리 자체는 데이터라서 코드를 고치지 않고 바꿀 수 있습니다.

## 1. 바꾸는 방법 (쉬운 순서)

| 방법 | 위치 | 빌드 필요 |
|---|---|---|
| WAV 파일로 교체 | 게임 폴더의 `Content/Sounds/<id>.wav` (Windows는 exe 옆, macOS는 .app 옆) | 아니오 |
| 정의 수정/추가 | 게임 폴더의 `Content/Sounds/sounds.json` (같은 id만 덮어씀) | 아니오 |
| 기본 소리로 넣기 | `Assets/Reimplementation/Resources/Sounds/<id>.wav` (Unity가 오디오 에셋으로 가져옴) | 예 |
| 기본 정의 수정 | `Assets/Reimplementation/Resources/Content/Sounds/sounds.json` | 예 |

소리를 찾는 순서: `Resources/Sounds/<clip>` → `Content/Sounds/<clip>.wav` → JSON의 `layers`로 합성한 소리. (`clip`이 비어 있으면 id와 같은 이름)
WAV는 16비트 PCM을 권장합니다(8/24비트 PCM, 32비트 float도 읽음). 볼륨은 정의의 `volume` × 설정의 **효과음 볼륨**입니다.

## 2. sounds.json 형식

```json
{
  "sounds": [
    {
      "id": "shoot",
      "description": "총 발사",
      "clip": "",
      "volume": 0.45,
      "pitch": 1,
      "pitchJitter": 0.06,
      "cooldown": 0.02,
      "layers": [
        { "wave": "square", "freq": 900, "freqEnd": 180, "duration": 0.09, "volume": 0.6, "delay": 0, "attack": 0.004, "curve": 3 },
        { "wave": "noise",  "freq": 1,   "freqEnd": 1,   "duration": 0.06, "volume": 0.5 }
      ]
    }
  ]
}
```

| 키 | 의미 |
|---|---|
| `id` | 효과음 이름 (아래 표). 새 id는 코드에서 `Fx.Sound("id", 위치)` 또는 `Sfx.Play("id")`로 부름 |
| `clip` | 사용할 파일 이름 (확장자 없이). 비우면 id |
| `volume` | 0~1 |
| `pitch` / `pitchJitter` | 기본 음높이 배율 / 무작위 흔들림(0.05 = ±5%) |
| `cooldown` | 같은 소리가 다시 나기까지 최소 초 (연사 시 소음 방지) |
| `layers` | 파일이 없을 때 합성할 소리. 여러 층을 섞음 |
| `layers[].wave` | `sine`, `square`, `triangle`, `saw`, `noise` |
| `freq` → `freqEnd` | 시작 → 끝 주파수(Hz). 내려가면 "퓨", 올라가면 "뿅" |
| `duration`, `delay` | 길이와 시작 지연(초). 지연을 다르게 주면 멜로디(승리음 등) |
| `attack`, `curve` | 시작 페이드 인 초 / 감쇠 곡선(1 직선, 3 빠르게 사라짐) |

## 3. 효과음 목록

| id | 언제 | 온라인/리플레이에 전달 |
|---|---|---|
| `shoot` | 총 발사 | 예 |
| `hit` | 탄이 캐릭터에 명중 (피해량에 따라 크기) | 예 |
| `impact` / `bounce` | 탄이 벽에 맞고 사라짐 / 튕김 | 예 |
| `jump` | 점프 | 예 |
| `block` / `reflect` | 방어 발동 / 방어로 탄을 튕겨냄 | 예 |
| `reload` | 탄창을 다 써서 재장전 시작 | 예 |
| `death` / `edge` | 사망 / 경기장 경계 | 예 |
| `explosion` / `shockwave` | 폭발 효과 / 밀어내기·끌어당기기 | 예 |
| `pad` | 점프대 | 예 |
| `card_pick` / `reroll` | 카드 결정 / 카드 새로고침 | 예 |
| `card_deal` / `card_switch` | 카드 선택 화면에서 카드 한 장 배분 / 선택 이동 | 각자 재생 |
| `timer_tick` | 카드 선택 남은 시간 5초 이하에서 1초마다 | 각자 재생 |
| `fight` / `point` / `round` | 포인트 시작("싸워라!") / 포인트 / 라운드 승리 | 예 |
| `victory` / `defeat` | 결과 창 (내 팀 기준) | 각자 재생 |
| `ui_click` | 메뉴 버튼 클릭 | 각자 재생 |

## 4. 코드 구조

- `Audio/Sound.cs`: `SoundBank`(정의 로드·파일 찾기·합성), `WavReader`, `Sfx`(재생, 24채널 풀, 쿨다운, 좌우 위치).
- 게임플레이 소리는 `Presentation.Fx.Sound(id, 위치)`를 씁니다. 방장/서버에서 난 소리가 효과 이벤트(`FxType.Sound`, id의 24비트 해시)로 참가자와 리플레이에 전달됩니다.
- UI처럼 내 화면에서만 날 소리는 `Audio.Sfx.Play(id)`.
- 전용 서버(헤드리스)와 배치 모드에서는 소리를 내지 않습니다.
- EditMode 테스트 `EverySoundUsedByTheGameIsDefinedAndSynthesizes`가 위 목록의 id가 모두 정의돼 있는지 검사합니다. 새 id를 코드에 쓰면 이 목록과 sounds.json에 함께 추가하세요.

