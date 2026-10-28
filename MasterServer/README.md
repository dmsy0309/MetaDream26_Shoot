# Metadream Shoot 메인 호스팅 서버 (서버 목록 서비스)

모든 게임 클라이언트와 게임 서버가 이 서비스에 패킷을 보내고, 서비스는 패킷을 보낸 쪽에 **현재 살아 있는 전체 서버 목록**을 돌려줍니다.
Python 3 표준 라이브러리만 사용하므로 pip 설치가 필요 없습니다.

| 포트 | 프로토콜 | 용도 |
|---|---|---|
| 47800 | UDP | 게임 패킷 (JSON 한 개 = 데이터그램 한 개) |
| 47801 | HTTP | `GET /health` (Uptime Kuma), `GET /servers` (목록 확인용) |

게임의 기본 주소는 `127.0.0.1:47800`입니다. 운영 주소는 `-master host:port` 또는 `METADREAM_MASTER_HOST`로 지정합니다. 전용 서버는 로컬 `server.json`의 `masterHost`/`masterPort`도 지원합니다. 실제 IP·SSH 계정·Tailscale 호스트명은 Git에 넣지 않습니다. 배포할 때 `MD_MASTER_SSH` 환경변수로 SSH 대상을 지정합니다.

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

- 자동 업로드는 기본으로 꺼져 있습니다. 호스트에서 명시적으로 `METADREAM_REPLAY_UPLOAD=1`, HTTPS 주소 `METADREAM_REPLAY_UPLOAD_URL`, `METADREAM_REPLAY_UPLOAD_KEY`를 모두 설정해야 합니다. 전용 서버는 로컬 설정의 `uploadReplays`도 `true`여야 합니다.
- 서버에도 같은 `METADREAM_REPLAY_UPLOAD_KEY`를 환경변수/로컬 서비스 설정으로 지정합니다. 미설정이면 업로드를 거절합니다. 고정 공용 키는 제공하지 않습니다.
- HTTPS 리버스 프록시를 통해 `/replays`를 이 서비스의 로컬 HTTP 포트로 연결합니다. 클라이언트는 HTTP 전송이나 자동 재전송을 하지 않습니다.
- 전송 메타데이터는 방 이름·참가자가 직접 입력한 닉네임·게임 결과를 포함합니다. 컴퓨터 이름·OS 사용자명·기기 ID·내부 주소는 기록/업로드하지 않습니다. 예전 리플레이를 명시적으로 전송해도 기기 정보는 본문과 헤더에서 제거합니다.
- 저장: `~/metadream-master/replays/<id>.mdreplay` + `replays/index.jsonl`. 서버는 연결 처리에 필요한 송신 IP를 알 수 있으며, 수신 기록에는 송신 IP가 남으므로 접근·보존 정책을 운영자가 관리해야 합니다.
- 목록/다운로드는 기존 tailnet 접근 조건 또는 런타임 생성 `admin_token.txt`가 필요합니다. 이 토큰과 실제 리플레이는 Git에서 제외합니다. 개인 주소가 든 목록 URL은 로컬 운영 문서에만 보관합니다.
- 기존 서버의 저장 데이터를 이번 소스 변경이 자동으로 삭제하지는 않습니다. 자세한 범위는 `Docs/PRIVACY.md`를 참고합니다.

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

