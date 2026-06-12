# 2v2 슈팅 네트워킹 예제

이 예제는 `Netcode for GameObjects`를 사용한 최소 서버 권위형 멀티플레이 프로토타입입니다.

## 패키지

`Packages/manifest.json`에 아래 패키지가 추가되어 있습니다.

```json
"com.unity.netcode.gameobjects": "2.11.2"
```

Unity가 프로젝트를 다시 열거나 패키지를 새로고침할 때 해당 패키지를 resolve합니다.

## 씬 설정

1. 빈 GameObject를 만들고 이름을 `Network Bootstrap`으로 지정합니다.
2. `NetworkGameBootstrap` 컴포넌트를 추가합니다.
3. `NetworkManager` GameObject를 직접 만들거나, `NetworkGameBootstrap`이 런타임에 생성하도록 둡니다.
4. `NetworkManager`를 직접 만들었다면 `UnityTransport` 컴포넌트를 추가합니다.
5. `NetworkManager` 컴포넌트의 `Network Transport` 필드에 `UnityTransport`가 할당되어 있는지 확인합니다.

`NetworkGameBootstrap`은 실행 시 `UnityTransport`가 없거나 연결되지 않은 경우 자동으로 보정합니다.

## 플레이어 프리팹

플레이어 프리팹을 만들고 아래 컴포넌트를 추가합니다.

- `NetworkObject`
- `NetworkPlayerShooter2D`
- `Collider2D`
- 선택 사항: `SpriteRenderer`

이 프리팹을 씬의 `NetworkManager` GameObject에 있는 `NetworkManager` 컴포넌트의 `Default Player Prefab` 또는 `Player Prefab` 필드에 할당합니다.

## 투사체 프리팹

투사체 프리팹을 만들고 아래 컴포넌트를 추가합니다.

- `NetworkObject`
- `NetworkProjectile2D`
- 선택 사항: `SpriteRenderer`

설정 순서:

1. Project 창에서 투사체 GameObject를 Prefab으로 만듭니다.
2. 씬의 `NetworkManager`를 선택합니다.
3. `NetworkManager` 컴포넌트의 `Network Prefabs` 또는 `Network Prefabs Lists`에 투사체 프리팹을 등록합니다.
4. 플레이어 프리팹을 엽니다.
5. `NetworkPlayerShooter2D` 컴포넌트의 `Projectile Prefab` 필드에 같은 투사체 프리팹을 할당합니다.

`Projectile prefab is not assigned.` 경고가 뜨면 5번 설정이 빠진 것입니다.
투사체가 네트워크에서 보이지 않거나 스폰 오류가 뜨면 3번 등록이 빠진 것입니다.

## 테스트 흐름

1. Unity 에디터에서 Play를 누릅니다.
2. 화면 왼쪽 위의 네트워크 UI에서 `Host`를 클릭합니다.
3. 두 번째 클라이언트 빌드나 Multiplayer Play Mode의 다른 에디터 인스턴스를 실행합니다.
4. 두 번째 인스턴스에서 `Client`를 클릭합니다.
5. `WASD` 또는 방향키로 이동합니다.
6. 마우스로 조준하고 좌클릭을 누르고 있으면 발사합니다.

## 권한 구조

클라이언트는 이동, 조준, 발사 입력만 서버로 보냅니다.
호스트/서버는 플레이어 이동, 투사체 생성, 데미지 적용, 리스폰을 최종 처리합니다.

이 구조는 중요한 게임 상태를 서버 권위형으로 유지하면서, 나중에 클라이언트 예측이나 지연 보정 같은 기능을 추가할 여지를 남겨둡니다.
