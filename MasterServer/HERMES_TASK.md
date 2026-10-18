# Hermes 작업 지시 (메인 호스팅 서버)

이 폴더(~/metadream-master)에는 Metadream Shoot 게임의 "메인 호스팅 서버(서버 목록 서비스)"가 들어 있습니다.
UDP 47800으로 게임 클라이언트/서버의 패킷을 받고, HTTP 47801에서 /health 를 제공합니다. 자세한 내용은 README.md 를 보세요.

해야 할 일:

1. ~/metadream-master/install.sh 를 실행해서 서비스를 systemd 에 등록하고, PC(서버) 부팅 시 자동으로 시작되도록 활성화하세요.
   - sudo 가 가능하면 system 서비스(metadream-master.service, enable --now)로, 불가능하면 user 서비스 + loginctl enable-linger 로 설정하세요.
   - systemctl is-enabled / is-active 로 부팅 시 활성화와 현재 실행을 확인하세요.
2. 이 서버의 Uptime Kuma 에 모니터를 추가하세요.
   - 이름: "Metadream Shoot 메인 서버"
   - 종류: HTTP(s) - Keyword, URL: http://127.0.0.1:47801/health (Uptime Kuma 가 Docker 컨테이너면 컨테이너에서 접근 가능한 주소, 예: http://100.64.0.10:47801/health)
   - 키워드: "status": "ok", 확인 간격 60초, 재시도 2회
   - 기존 알림 채널이 있으면 같은 채널을 연결하세요.
3. 방화벽(ufw 등)이 켜져 있으면 tailscale0 인터페이스에서 UDP 47800, TCP 47801 을 허용하세요.
4. 끝나면 결과를 ~/metadream-master/HERMES_RESULT.md 에 적어 주세요: 서비스 종류(system/user), is-enabled/is-active 결과, curl http://127.0.0.1:47801/health 출력, Uptime Kuma 모니터 이름/URL/상태, 변경한 방화벽 규칙.

주의: master_server.py 코드는 수정하지 마세요. 다른 서비스나 기존 Uptime Kuma 모니터는 건드리지 마세요.

