#!/usr/bin/env bash
# Copies the main hosting service to the hosting server (~/metadream-master), runs its self-test and
# (optionally) asks the Hermes agent there to enable it at boot and add it to Uptime Kuma.
#   ./deploy.sh            copy + self-test + start a test instance check
#   ./deploy.sh --hermes   ... and hand HERMES_TASK.md to hermes (gpt-6.1-sol, reasoning high, --yolo)
set -euo pipefail
TARGET="${MD_MASTER_SSH:-deploy-user@100.64.0.10}"
REMOTE_DIR="metadream-master"
HERMES_MODEL="${MD_HERMES_MODEL:-gpt-6.1-sol}"
cd "$(dirname "$0")"
SSH_OPTS=(-o BatchMode=yes -o ConnectTimeout=10)

echo "[deploy] -> $TARGET:~/$REMOTE_DIR"
ssh "${SSH_OPTS[@]}" "$TARGET" "mkdir -p ~/$REMOTE_DIR"
scp "${SSH_OPTS[@]}" -q master_server.py test_master_server.py install.sh metadream-master.service \
    metadream-master-user.service README.md HERMES_TASK.md "$TARGET:~/$REMOTE_DIR/"
ssh "${SSH_OPTS[@]}" "$TARGET" "cd ~/$REMOTE_DIR && chmod +x install.sh master_server.py test_master_server.py && python3 test_master_server.py 2>&1 | tail -3"

if [ "${1:-}" = "--hermes" ]; then
  echo "[deploy] handing boot + Uptime Kuma setup to hermes ($HERMES_MODEL, reasoning high, yolo)"
  # hermes has no per-query reasoning flag: set it in its config (agent.reasoning_effort), then run one-shot in YOLO mode.
  ssh -t "${SSH_OPTS[@]}" "$TARGET" "cd ~/$REMOTE_DIR && hermes config set agent.reasoning_effort high && hermes chat --yolo --model $HERMES_MODEL -q \"\$(cat HERMES_TASK.md)\""
fi

