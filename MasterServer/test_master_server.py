#!/usr/bin/env python3
"""Self-test: python3 test_master_server.py  (unit tests + a live UDP/HTTP round trip on random ports)."""
import json
import base64
import gzip
import os
import socket
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import master_server as ms  # noqa: E402


class RegistryTests(unittest.TestCase):
    def test_register_list_expire(self):
        r = ms.Registry(ttl=15)
        reply = r.handle({"type": "register", "ip": "10.0.0.5", "port": 7777, "players": 1, "maxPlayers": 4, "name": "A"},
                         "100.64.0.9", now=100)
        self.assertEqual(reply["total"], 1)
        s = reply["servers"][0]
        self.assertEqual(s["address"], "100.64.0.9")  # observed address wins
        self.assertEqual(s["ip"], "10.0.0.5")
        self.assertEqual((s["players"], s["maxPlayers"]), (1, 4))
        self.assertEqual(r.handle({"type": "query"}, "100.64.0.10", now=110)["total"], 1)
        self.assertEqual(r.handle({"type": "query"}, "100.64.0.10", now=116)["total"], 0)  # expired

    def test_heartbeat_updates_in_place(self):
        r = ms.Registry(ttl=15)
        r.handle({"type": "register", "port": 7777, "players": 1, "maxPlayers": 2}, "1.2.3.4", now=1)
        reply = r.handle({"type": "register", "port": 7777, "players": 2, "maxPlayers": 2}, "1.2.3.4", now=6)
        self.assertEqual(reply["total"], 1)
        self.assertEqual(reply["servers"][0]["players"], 2)

    def test_explicit_and_loopback(self):
        r = ms.Registry(ttl=15)
        r.handle({"type": "register", "ip": "203.0.113.7", "ipExplicit": True, "port": 7777}, "100.64.0.9", now=1)
        r.handle({"type": "register", "ip": "192.168.0.3", "port": 7778}, "127.0.0.1", now=1)
        addrs = sorted(s["address"] for s in r.handle({"type": "query"}, "x", now=2)["servers"])
        self.assertEqual(addrs, ["192.168.0.3", "203.0.113.7"])

    def test_unregister_and_sanitize(self):
        r = ms.Registry(ttl=15)
        r.handle({"type": "register", "port": 7777, "players": 999, "name": "x" * 500}, "1.2.3.4", now=1)
        s = r.handle({"type": "query"}, "x", now=1)["servers"][0]
        self.assertEqual(s["players"], 64)
        self.assertEqual(len(s["name"]), 48)
        self.assertEqual(r.handle({"type": "unregister", "port": 7777}, "1.2.3.4", now=2)["total"], 0)


def free_port(kind):
    s = socket.socket(socket.AF_INET, kind)
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p


class LiveTest(unittest.TestCase):
    def test_upload_disabled_without_configured_key(self):
        udp, http = free_port(socket.SOCK_DGRAM), free_port(socket.SOCK_STREAM)
        with tempfile.TemporaryDirectory() as tmp:
            child_env = dict(os.environ, METADREAM_REPLAY_UPLOAD_KEY="")
            proc = subprocess.Popen([sys.executable, str(Path(__file__).parent / "master_server.py"),
                                     "--host", "127.0.0.1", "--port", str(udp), "--http-port", str(http),
                                     "--replay-dir", os.path.join(tmp, "replays")], env=child_env)
            try:
                base = "http://127.0.0.1:%d" % http
                for attempt in range(40):
                    try:
                        with urllib.request.urlopen(base + "/health", timeout=0.2) as response:
                            self.assertEqual(response.status, 200)
                        break
                    except urllib.error.URLError:
                        if attempt == 39: raise
                        time.sleep(0.05)
                for supplied_key in ("", ms.secrets.token_urlsafe(24)):
                    req = urllib.request.Request(base + "/replays", data=gzip.compress(b"MDREPLAY"), method="POST",
                                                 headers={"X-Metadream-Key": supplied_key})
                    with self.assertRaises(urllib.error.HTTPError) as rejected:
                        urllib.request.urlopen(req, timeout=2)
                    self.assertEqual(rejected.exception.code, 403)
                self.assertEqual(list(Path(tmp, "replays").glob("*.mdreplay")), [])
            finally:
                proc.terminate()
                proc.wait(5)

    def test_round_trip(self):
        udp, http = free_port(socket.SOCK_DGRAM), free_port(socket.SOCK_STREAM)
        tmp = tempfile.mkdtemp()
        upload_key = ms.secrets.token_urlsafe(24)
        child_env = dict(os.environ, METADREAM_REPLAY_UPLOAD_KEY=upload_key)
        proc = subprocess.Popen([sys.executable, str(Path(__file__).parent / "master_server.py"),
                                 "--host", "127.0.0.1", "--port", str(udp), "--http-port", str(http),
                                 "--replay-dir", os.path.join(tmp, "replays")], env=child_env)
        try:
            time.sleep(0.8)
            c = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            c.settimeout(2)
            c.sendto(json.dumps({"type": "register", "game": "metadream-shoot", "ip": "192.168.1.2", "port": 7777,
                                 "players": 2, "maxPlayers": 4}).encode(), ("127.0.0.1", udp))
            reply = json.loads(c.recvfrom(65535)[0])
            self.assertEqual(reply["servers"][0]["address"], "192.168.1.2")
            c.sendto(b"garbage", ("127.0.0.1", udp))
            c.sendto(json.dumps({"type": "query"}).encode(), ("127.0.0.1", udp))
            self.assertEqual(json.loads(c.recvfrom(65535)[0])["total"], 1)
            health = json.loads(urllib.request.urlopen("http://127.0.0.1:%d/health" % http, timeout=2).read())
            self.assertEqual(health["status"], "ok")
            self.assertEqual(health["servers"], 1)
            self.assertEqual(health["rejected"], 1)
            # replay upload + listing
            meta = {"players": [{"nickname": "김철수", "device": "LAB-PC-07"}], "device": {"machineName": "LAB-PC-01"}}
            req = urllib.request.Request("http://127.0.0.1:%d/replays" % http, data=gzip.compress(b"MDREPLAY"), method="POST",
                                         headers={"X-Metadream-Key": upload_key,
                                                  "X-Metadream-Meta": base64.b64encode(json.dumps(meta).encode()).decode()})
            rid = json.loads(urllib.request.urlopen(req, timeout=2).read())["id"]
            with self.assertRaises(urllib.error.HTTPError):  # not tailnet, no token
                urllib.request.urlopen("http://127.0.0.1:%d/replays" % http, timeout=2)
            token = Path(tmp, "admin_token.txt").read_text().strip()
            found = json.loads(urllib.request.urlopen("http://127.0.0.1:%d/replays?q=%s&token=%s" % (http, urllib.parse.quote("철수"), token), timeout=2).read())
            self.assertEqual(found["replays"][0]["id"], rid)
            self.assertEqual(found["replays"][0]["meta"]["players"][0]["device"], "LAB-PC-07")
            blob = urllib.request.urlopen("http://127.0.0.1:%d/replays/%s?token=%s" % (http, rid, token), timeout=2).read()
            self.assertEqual(gzip.decompress(blob), b"MDREPLAY")
            bad = urllib.request.Request("http://127.0.0.1:%d/replays" % http, data=b"x", method="POST")
            with self.assertRaises(urllib.error.HTTPError):
                urllib.request.urlopen(bad, timeout=2)
        finally:
            if "c" in locals(): c.close()
            proc.terminate()
            proc.wait(5)


if __name__ == "__main__":
    unittest.main(verbosity=2)
