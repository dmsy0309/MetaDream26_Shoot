#!/usr/bin/env bash
# Metadream Shoot dedicated server launcher (Linux x64)
#   ./start-server.sh                      # server.json in this folder
#   ./start-server.sh -teamsize 2 -port 7800
#   METADREAM_CONFIG=/etc/metadream/server.json ./start-server.sh
set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"
CONFIG="${METADREAM_CONFIG:-server.json}"
BIN=./MetadreamShootServer.x86_64
if [ ! -x "$BIN" ]; then chmod +x "$BIN"; fi
exec "$BIN" -batchmode -nographics -logFile - -server -config "$CONFIG" "$@"
