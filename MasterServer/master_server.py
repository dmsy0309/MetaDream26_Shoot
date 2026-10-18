#!/usr/bin/env python3
"""Metadream Shoot main hosting (master / server list) service.

Only the Python 3 standard library is used, so it runs on any Linux box without pip.

UDP  :47800  game packets (one JSON object per datagram)
    -> {"type":"register","game":"metadream-shoot","version":3,"ip":"192.168.0.12","port":7777,
        "players":1,"maxPlayers":2,"name":"...","teamSize":1,"inMatch":false,"dedicated":false,
        "code":"...","ipExplicit":false}
    -> {"type":"query"}                      (clients that only browse)
    -> {"type":"unregister","ip":...,"port":...}
    <- every packet is answered with {"type":"list","time":...,"servers":[...]} (all live servers)
HTTP :47801  GET /health  -> 200 {"status":"ok",...}   (Uptime Kuma HTTP monitor)
             GET /servers -> 200 {"servers":[...]}      (same list as the UDP reply)
             POST /replays            replay upload from game hosts/servers (header X-Metadream-Key,
                                      X-Metadream-Meta = base64(JSON: device, players' nicknames ...))
             GET /replays[?q=name]    replay index (newest first), GET /replays/<id> download,
             GET /replays.html        searchable table for people. Listing/download only from the
                                      tailnet (100.64.0.0/10) or with ?token=<admin_token.txt>.

A registered server disappears when it stops sending heartbeats for --ttl seconds (default 15).
"""
import argparse
import base64
import html
import ipaddress
import json
import logging
import os
import re
import secrets
import signal
import socket
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

GAME_ID = "metadream-shoot"
MAX_PACKET = 4096
MAX_SERVERS = 512
MAX_REPLY_SERVERS = 64
STR_LIMITS = {"name": 48, "code": 24, "ip": 64, "contentHash": 80}
UPLOAD_KEY = "metadream-shoot-replay-v1"  # same constant as ReplayUploader.Key in the game
MAX_REPLAY_BYTES = 64 * 1024 * 1024
TAILNET = ipaddress.ip_network("100.64.0.0/10")

log = logging.getLogger("metadream-master")


def _int(v, lo, hi, default=0):
    try:
        v = int(v)
    except (TypeError, ValueError):
        return default
    return max(lo, min(hi, v))


def _str(v, limit):
    if not isinstance(v, str):
        return ""
    return "".join(ch for ch in v if ch.isprintable())[:limit]


def _is_loopback(ip):
    return ip.startswith("127.") or ip in ("::1", "localhost")


class Registry:
    def __init__(self, ttl):
        self.ttl = ttl
        self.lock = threading.Lock()
        self.servers = {}
        self.started = time.time()
        self.packets = 0
        self.rejected = 0
        self.last_packet = 0.0

    def _expire(self, now):
        dead = [k for k, s in self.servers.items() if now - s["lastSeen"] > self.ttl]
        for k in dead:
            s = self.servers.pop(k)
            log.info("expired %s (%s)", k, s["name"])

    def handle(self, packet, observed_ip, now=None):
        """Apply one packet and return the reply (always the full server list)."""
        now = time.time() if now is None else now
        with self.lock:
            self.packets += 1
            self.last_packet = now
            self._expire(now)
            kind = packet.get("type", "query")
            reported = _str(packet.get("ip"), STR_LIMITS["ip"])
            port = _int(packet.get("port"), 0, 65535)
            # Address other players should connect to: what we saw on the wire, unless the server
            # pinned a public address (ipExplicit) or reached us over loopback (same machine).
            if reported and (packet.get("ipExplicit") or _is_loopback(observed_ip)):
                address = reported
            else:
                address = observed_ip
            key = "%s:%d" % (address, port)
            if kind == "register" and port > 0:
                if key not in self.servers and len(self.servers) >= MAX_SERVERS:
                    self.rejected += 1
                else:
                    entry = self.servers.get(key)
                    if entry is None:
                        log.info("register %s (%s) players=%s/%s", key, _str(packet.get("name"), 48),
                                 packet.get("players"), packet.get("maxPlayers"))
                        entry = self.servers[key] = {"firstSeen": now}
                    entry.update({
                        "address": address,
                        "ip": reported or observed_ip,
                        "observedIp": observed_ip,
                        "port": port,
                        "players": _int(packet.get("players"), 0, 64),
                        "maxPlayers": _int(packet.get("maxPlayers"), 0, 64),
                        "name": _str(packet.get("name"), STR_LIMITS["name"]) or "Metadream 방",
                        "teamSize": _int(packet.get("teamSize"), 1, 8, 1),
                        "inMatch": bool(packet.get("inMatch")),
                        "dedicated": bool(packet.get("dedicated")),
                        "code": _str(packet.get("code"), STR_LIMITS["code"]),
                        "version": _int(packet.get("version"), 0, 1 << 30),
                        "contentHash": _str(packet.get("contentHash"), STR_LIMITS["contentHash"]),
                        "lastSeen": now,
                    })
            elif kind == "unregister" and key in self.servers:
                log.info("unregister %s", key)
                del self.servers[key]
            return self._reply(now)

    def _reply(self, now):
        items = sorted(self.servers.values(), key=lambda s: (not s["dedicated"], -s["players"], s["name"]))
        out = []
        for s in items[:MAX_REPLY_SERVERS]:
            e = {k: v for k, v in s.items() if k not in ("firstSeen", "lastSeen")}
            e["age"] = round(now - s["lastSeen"], 1)
            out.append(e)
        return {"type": "list", "game": GAME_ID, "time": int(now), "total": len(self.servers), "servers": out}

    def snapshot(self):
        with self.lock:
            now = time.time()
            self._expire(now)
            return self._reply(now)

    def health(self):
        with self.lock:
            now = time.time()
            self._expire(now)
            return {
                "status": "ok",
                "game": GAME_ID,
                "servers": len(self.servers),
                "players": sum(s["players"] for s in self.servers.values()),
                "packets": self.packets,
                "rejected": self.rejected,
                "uptime": int(now - self.started),
                "lastPacketAgo": round(now - self.last_packet, 1) if self.last_packet else None,
            }


def serve_udp(registry, sock, stop):
    while not stop.is_set():
        try:
            data, addr = sock.recvfrom(MAX_PACKET)
        except socket.timeout:
            continue
        except OSError as exc:
            log.warning("udp recv error: %s", exc)
            continue
        try:
            packet = json.loads(data.decode("utf-8"))
            if not isinstance(packet, dict) or packet.get("game", GAME_ID) != GAME_ID:
                raise ValueError("not a metadream-shoot packet")
        except Exception as exc:  # malformed input never kills the service
            registry.rejected += 1
            log.debug("bad packet from %s: %s", addr, exc)
            continue
        reply = registry.handle(packet, addr[0])
        try:
            sock.sendto(json.dumps(reply, separators=(",", ":"), ensure_ascii=False).encode("utf-8"), addr)
        except OSError as exc:
            log.warning("udp send error to %s: %s", addr, exc)


def make_http_handler(registry):
    return make_http_handler_with(registry, None)


class ReplayStore:
    """replays/<id>.mdreplay + replays/index.jsonl (one JSON object per upload)."""

    def __init__(self, folder):
        self.folder = folder
        os.makedirs(folder, exist_ok=True)
        self.index_path = os.path.join(folder, "index.jsonl")
        self.lock = threading.Lock()
        token_path = os.path.join(os.path.dirname(os.path.abspath(folder)), "admin_token.txt")
        if not os.path.exists(token_path):
            with open(token_path, "w") as f:
                f.write(secrets.token_urlsafe(18))
            os.chmod(token_path, 0o600)
        with open(token_path) as f:
            self.token = f.read().strip()

    def save(self, data, meta, source_ip):
        rid = time.strftime("%Y%m%d-%H%M%S") + "-" + secrets.token_hex(3)
        path = os.path.join(self.folder, rid + ".mdreplay")
        with open(path, "wb") as f:
            f.write(data)
        entry = {"id": rid, "receivedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "size": len(data),
                 "sourceIp": source_ip, "meta": meta}
        with self.lock, open(self.index_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")
        log.info("replay %s stored (%d bytes) from %s players=%s", rid, len(data), source_ip,
                 [p.get("nickname") for p in (meta.get("players") or []) if isinstance(p, dict)])
        return rid

    def entries(self, query=""):
        out = []
        if os.path.exists(self.index_path):
            with open(self.index_path, encoding="utf-8") as f:
                for line in f:
                    try:
                        out.append(json.loads(line))
                    except ValueError:
                        continue
        out.reverse()
        q = (query or "").strip().lower()
        if q:
            out = [e for e in out if q in json.dumps(e, ensure_ascii=False).lower()]
        return out

    def path_of(self, rid):
        if not re.fullmatch(r"[0-9A-Za-z\-]{6,64}", rid or ""):
            return None
        p = os.path.join(self.folder, rid + ".mdreplay")
        return p if os.path.exists(p) else None


def make_http_handler_with(registry, replays):
    class Handler(BaseHTTPRequestHandler):
        server_version = "MetadreamMaster/1.0"

        def _client_ip(self):
            ip = self.client_address[0]
            if ip.startswith("127.") or ip == "::1":  # behind tailscale funnel / reverse proxy
                fwd = self.headers.get("X-Forwarded-For", "")
                if fwd:
                    return fwd.split(",")[0].strip()
            return ip

        def _trusted(self, query):
            ip = self.client_address[0]
            try:
                if ipaddress.ip_address(ip) in TAILNET:
                    return True
            except ValueError:
                pass
            return replays is not None and query.get("token", [""])[0] == replays.token

        def _json(self, code, obj):
            body = json.dumps(obj, ensure_ascii=False, indent=1).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            url = urlparse(self.path)
            path = url.path.rstrip("/") or "/"
            query = parse_qs(url.query)
            if path in ("/", "/health", "/healthz"):
                self._json(200, registry.health())
            elif path == "/servers":
                self._json(200, registry.snapshot())
            elif replays is not None and path.startswith("/replays"):
                if not self._trusted(query):
                    self._json(403, {"error": "tailnet or ?token= required"})
                elif path == "/replays":
                    self._json(200, {"replays": replays.entries(query.get("q", [""])[0])})
                elif path == "/replays.html":
                    self._html(replays.entries(query.get("q", [""])[0]), query)
                else:
                    p = replays.path_of(path[len("/replays/"):].replace(".mdreplay", ""))
                    if p is None:
                        self._json(404, {"error": "no such replay"})
                    else:
                        with open(p, "rb") as f:
                            data = f.read()
                        self.send_response(200)
                        self.send_header("Content-Type", "application/octet-stream")
                        self.send_header("Content-Disposition", "attachment; filename=\"%s\"" % os.path.basename(p))
                        self.send_header("Content-Length", str(len(data)))
                        self.end_headers()
                        self.wfile.write(data)
            else:
                self._json(404, {"error": "not found"})

        def do_POST(self):
            path = urlparse(self.path).path.rstrip("/")
            if replays is None or path != "/replays":
                self._json(404, {"error": "not found"})
                return
            if self.headers.get("X-Metadream-Key") != UPLOAD_KEY:
                self._json(403, {"error": "bad key"})
                return
            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                length = 0
            if length <= 0 or length > MAX_REPLAY_BYTES:
                self._json(413, {"error": "size must be 1..%d bytes" % MAX_REPLAY_BYTES})
                return
            data = self.rfile.read(length)
            if data[:2] != b"\x1f\x8b":
                self._json(400, {"error": "not a replay file (gzip expected)"})
                return
            try:
                meta = json.loads(base64.b64decode(self.headers.get("X-Metadream-Meta", "")).decode("utf-8") or "{}")
                if not isinstance(meta, dict):
                    meta = {}
            except Exception:
                meta = {}
            rid = replays.save(data, meta, self._client_ip())
            self._json(200, {"ok": True, "id": rid})

        def _html(self, entries, query):
            q = html.escape(query.get("q", [""])[0])
            tok = html.escape(query.get("token", [""])[0])
            rows = []
            for e in entries[:500]:
                m = e.get("meta") or {}
                dev = m.get("device") or {}
                players = ", ".join("%s (%s)" % (p.get("nickname", "?"), p.get("device") or p.get("kind", ""))
                                    for p in (m.get("players") or []) if isinstance(p, dict))
                link = "/replays/%s?token=%s" % (e["id"], tok) if tok else "/replays/%s" % e["id"]
                rows.append("<tr><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td><a href='%s'>다운로드</a></td></tr>" % (
                    html.escape(m.get("createdLocal", e.get("receivedUtc", ""))), html.escape(str(m.get("roomName", ""))),
                    html.escape(players), html.escape("%s / %s" % (dev.get("machineName", "?"), dev.get("localIp", ""))),
                    html.escape(e.get("sourceIp", "")), link))
            body = ("<!doctype html><meta charset='utf-8'><title>Metadream Shoot 리플레이</title>"
                    "<style>body{font-family:sans-serif;margin:24px}td,th{border-bottom:1px solid #ddd;padding:6px 10px;text-align:left}</style>"
                    "<h2>Metadream Shoot 리플레이 (%d개)</h2><form><input name=q value='%s' placeholder='닉네임 · 기기 이름 검색'>"
                    "<input type=hidden name=token value='%s'><button>검색</button></form><table><tr><th>생성 시각</th><th>방</th>"
                    "<th>참가자 (기기)</th><th>생성 기기</th><th>보낸 IP</th><th></th></tr>%s</table>") % (len(entries), q, tok, "".join(rows))
            data = body.encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, fmt, *args):
            log.debug("http %s - %s", self.address_string(), fmt % args)

    return Handler


def main(argv=None):
    p = argparse.ArgumentParser(description="Metadream Shoot main hosting (server list) service")
    p.add_argument("--host", default=os.environ.get("MD_MASTER_HOST", "0.0.0.0"))
    p.add_argument("--port", type=int, default=int(os.environ.get("MD_MASTER_PORT", "47800")), help="UDP game port")
    p.add_argument("--http-port", type=int, default=int(os.environ.get("MD_MASTER_HTTP_PORT", "47801")),
                   help="HTTP health port (0 = off)")
    p.add_argument("--ttl", type=float, default=float(os.environ.get("MD_MASTER_TTL", "15")),
                   help="seconds before a silent server is dropped")
    p.add_argument("--replay-dir", default=os.environ.get("MD_MASTER_REPLAYS",
                   os.path.join(os.path.dirname(os.path.abspath(__file__)), "replays")))
    p.add_argument("--verbose", action="store_true")
    args = p.parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO,
                        format="%(asctime)s %(levelname)s %(message)s", stream=sys.stdout)

    registry = Registry(args.ttl)
    stop = threading.Event()
    signal.signal(signal.SIGTERM, lambda *_: stop.set())
    signal.signal(signal.SIGINT, lambda *_: stop.set())

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((args.host, args.port))
    sock.settimeout(0.5)
    log.info("UDP listening on %s:%d (ttl %.0fs)", args.host, args.port, args.ttl)

    httpd = None
    if args.http_port:
        replays = ReplayStore(args.replay_dir)
        httpd = ThreadingHTTPServer((args.host, args.http_port), make_http_handler_with(registry, replays))
        httpd.daemon_threads = True
        threading.Thread(target=httpd.serve_forever, name="http", daemon=True).start()
        log.info("HTTP on %s:%d (/health, /servers, /replays -> %s)", args.host, args.http_port, args.replay_dir)

    udp = threading.Thread(target=serve_udp, args=(registry, sock, stop), name="udp", daemon=True)
    udp.start()
    while not stop.is_set() and udp.is_alive():
        stop.wait(1.0)
    stop.set()
    if httpd:
        httpd.shutdown()
    udp.join(2)
    sock.close()
    log.info("stopped")
    return 0


if __name__ == "__main__":
    sys.exit(main())
