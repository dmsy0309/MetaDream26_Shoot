# Metadream Shoot 메인 호스팅 서버 (서버 목록 서비스)

모든 게임 클라이언트와 게임 서버가 이 서비스에 패킷을 보내고, 서비스는 패킷을 보낸 쪽에 **현재 살아 있는 전체 서버 목록**을 돌려줍니다.
Python 3 표준 라이브러리만 사용하므로 pip 설치가 필요 없습니다.

| 포트 | 프로토콜 | 용도 |
|---|---|---|
| 47800 | UDP | 게임 패킷 (JSON 한 개 = 데이터그램 한 개) |
| 47801 | HTTP | `GET /health` (Uptime Kuma), `GET /servers` (목록 확인용) |

게임에 하드코딩된 주소: **공인 IP** `203.0.113.10:47800` (UDP, 서버 목록)과 `203.0.113.10:47801` (HTTP, 리플레이 업로드). `Assets/Reimplementation/Network/MasterServer.cs` 의 `MasterServerEndpoint.DefaultHost`. tailnet 주소(100.64.0.10)는 쓰지 않으므로 공유기의 포트 포워딩(UDP 47800, TCP 47801)이 유지돼야 합니다.

## 패킷

방을 만든 플레이어와 전용 서버는 5초마다 `register`, 방 목록 화면의 클라이언트는 3초마다 `query`, 방을 닫으면 `unregister`를 보냅니다.

```json
{"type":"register","game":"metadream-shoot","version":3,"ip":"192.168.0.12","ipExplicit":false,
 "port":7777,"players":1,"maxPlayers":2,"name":"Metadream 방","teamSize":1,"inMatch":false,
 "dedicated":false,"code":"60R0G-01Y71","contentHash":"3f2a9c0d11aa"}
```

- `ip`/`port`: 호스팅 중인 서버 주소, `players`/`maxPlayers`: 접속 인원 / 최대 인원

모든 패킷에 대한 응답 (전체 서버 목록):

```json
{"type":"list","game":"metadream-shoot","time":1760770000,"total":1,"servers":[
 {"address":"100.101.2.3","ip":"192.168.0.12","observedIp":"100.101.2.3","port":7777,"players":1,"maxPlayers":2,
  "name":"Metadream 방","teamSize":1,"inMatch":false,"dedicated":false,"code":"60R0G-01Y71","version":3,
  "contentHash":"3f2a9c0d11aa","age":1.2}]}
```

- `address`: 다른 플레이어가 접속할 주소. 기본은 서비스가 실제로 본 송신 IP(Tailnet이면 100.x)이고, 서버가 `publicAddress`를 지정했으면(`ipExplicit`) 그 값을 씁니다. 같은 기계(127.0.0.1)에서 온 패킷은 보고된 `ip`를 씁니다.
- 15초(`--ttl`) 동안 하트비트가 없으면 목록에서 빠집니다. 응답에는 최대 64개 서버가 들어갑니다.

## 리플레이 수신

- 게임의 방장/전용 서버가 게임마다 `POST /replays`로 보냅니다. 헤더 `X-Metadream-Key`(게임에 고정된 키), `X-Metadream-Meta`(base64 JSON: 생성 기기 이름·사용자·OS·기기 ID·IP, 방 이름, 참가자 닉네임·참가자 PC 이름·주소, 점수). 본문은 .mdreplay(gzip, 최대 64MB).
- 저장: `~/metadream-master/replays/<id>.mdreplay` + `replays/index.jsonl` (한 줄에 한 업로드, 보낸 IP 포함).
- 찾기: 브라우저에서 `http://100.64.0.10:47801/replays.html?q=닉네임` (닉네임·PC 이름·방 이름 검색, 다운로드 링크). JSON은 `/replays?q=`.
- 목록/다운로드는 tailnet(100.64.0.0/10)에서만 열리고, 그 밖에서는 `?token=`(서버의 `~/metadream-master/admin_token.txt`)이 필요합니다. 업로드는 키만 맞으면 받습니다.
- tailnet 밖(학교 PC 등): 게임은 tailnet 주소가 실패하면 `https://replays.example.invalid:10000` (Tailscale Funnel → 127.0.0.1:47801)로 보냅니다. Funnel 활성화: 서버에서 `sudo tailscale funnel --bg --https=10000 --yes http://127.0.0.1:47801`.

## 설치 (서버에서)

```bash
cd ~/metadream-master
./install.sh          # sudo 가능 → /etc/systemd/system/metadream-master.service, 아니면 user 서비스 + linger
./install.sh --check  # 자체 테스트 + /health 확인
journalctl -u metadream-master -f            # system 서비스 로그
journalctl --user -u metadream-master -f     # user 서비스 로그
```

수동 실행: `python3 master_server.py --verbose` · 자체 테스트: `python3 test_master_server.py`

## Uptime Kuma

- 모니터 종류 **HTTP(s) - Keyword**, URL `http://127.0.0.1:47801/health` (Kuma가 Docker 안이면 `http://100.64.0.10:47801/health`), 키워드 `"status": "ok"`, 간격 60초.
- 응답 예: `{"status": "ok", "servers": 2, "players": 3, "uptime": 3600, ...}`

