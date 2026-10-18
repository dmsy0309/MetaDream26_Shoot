# Hermes 작업 지시 2: 메인 호스팅 서버 HTTP를 tailnet 밖에서도 접근 가능하게

배경: ~/metadream-master 의 metadream-master 서비스(user systemd 서비스, python master_server.py)는
UDP 47800(서버 목록, tailnet 전용 유지)과 HTTP 47801(/health, /servers, 곧 추가될 POST /replays 리플레이 업로드)을 제공합니다.
학교 컴퓨터 등 tailnet 밖의 게임이 리플레이 파일을 HTTP로 업로드할 수 있어야 합니다.

해야 할 일:
1. HTTP 47801 을 인터넷(tailnet 밖)에서 HTTPS로 접근할 수 있게 하세요. 우선순위:
   (a) Tailscale Funnel (예: tailscale funnel --bg 47801 또는 특정 경로/포트 매핑). 필요하면 operator 권한 설정 등 방법을 찾아 진행하세요.
   (b) Funnel 이 불가능하면 이 서버에 이미 있는 다른 공개 수단(리버스 프록시, cloudflared 등)을 사용하세요.
   기존 Funnel/Serve 설정이나 다른 서비스가 있다면 깨뜨리지 말고 공존시키세요 (예: 다른 HTTPS 포트 8443/10000 사용 또는 경로 기반 매핑).
2. 재부팅 후에도 공개 설정이 유지되도록 하세요.
3. tailnet 밖에서 접근되는지 확인하세요 (예: 공개 URL로 curl https://<공개주소>/health, 가능하면 tailnet 을 거치지 않는 경로로).
4. 결과를 ~/metadream-master/HERMES_RESULT2.md 에 적어 주세요. 반드시 첫 줄에 "PUBLIC_URL=<https://... 47801 서비스로 연결되는 기본 URL, 끝에 / 없이>" 형식으로 적고, 이어서 사용한 방법, 유지 방법, 확인 결과를 적어 주세요.

주의: master_server.py 는 수정하지 마세요(곧 개발자가 업데이트합니다). UDP 47800 은 공개하지 않아도 됩니다. 다른 서비스는 건드리지 마세요.

