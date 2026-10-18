#!/usr/bin/env bash
# Installs / updates the Metadream Shoot main hosting service in this folder and enables it at boot.
#   ./install.sh            -> system service (sudo) if possible, otherwise a systemd user service + linger
#   ./install.sh --user     -> force the user service
#   ./install.sh --check    -> only run the self-test and a health probe
set -euo pipefail
APP_DIR="$(cd "$(dirname "$0")" && pwd)"
UNIT=metadream-master
MODE="${1:-auto}"

command -v python3 >/dev/null || { echo "python3 is required"; exit 1; }
python3 -c "import sys; assert sys.version_info >= (3, 7), 'python 3.7+ required'"

echo "[install] self-test"
if python3 "$APP_DIR/test_master_server.py" >/dev/null 2>&1; then echo "[install] self-test ok"; else python3 "$APP_DIR/test_master_server.py"; exit 1; fi
if [ "$MODE" = "--check" ]; then curl -fsS "http://127.0.0.1:47801/health"; echo; exit 0; fi

render() { sed -e "s|@APP_DIR@|$APP_DIR|g" -e "s|@USER@|$(id -un)|g" "$1"; }

if [ "$MODE" != "--user" ] && sudo -n true 2>/dev/null; then
  echo "[install] system service /etc/systemd/system/$UNIT.service"
  render "$APP_DIR/metadream-master.service" | sudo tee "/etc/systemd/system/$UNIT.service" >/dev/null
  sudo systemctl daemon-reload
  sudo systemctl enable --now "$UNIT"
  sudo systemctl restart "$UNIT"
  sleep 1
  sudo systemctl --no-pager --lines=5 status "$UNIT" || true
else
  echo "[install] user service ~/.config/systemd/user/$UNIT.service (no passwordless sudo)"
  mkdir -p "$HOME/.config/systemd/user"
  render "$APP_DIR/metadream-master-user.service" > "$HOME/.config/systemd/user/$UNIT.service"
  systemctl --user daemon-reload
  systemctl --user enable --now "$UNIT"
  systemctl --user restart "$UNIT"
  # Without linger a user service only runs while the user is logged in; linger starts it at boot.
  loginctl enable-linger "$(id -un)" 2>/dev/null || sudo -n loginctl enable-linger "$(id -un)" 2>/dev/null \
    || echo "[install] WARNING: run 'sudo loginctl enable-linger $(id -un)' so the service starts at boot"
  sleep 1
  systemctl --user --no-pager --lines=5 status "$UNIT" || true
fi

echo "[install] health:"
curl -fsS "http://127.0.0.1:47801/health" && echo
echo "[install] done. UDP 47800 (game packets), HTTP 47801 (/health, /servers)"
