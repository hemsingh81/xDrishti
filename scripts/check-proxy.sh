#!/usr/bin/env bash
# Validates the reverse proxy's configuration (deploy/caddy/Caddyfile) with the Caddy that ships in the proxy image, when the
# image is available locally (built by deploy/xd-up.sh). The contents that matter - the SignalR hub /hubs/* proxied to xd-api
# before the SPA fallback - are asserted by the test CaddyfileTests in the backend's integration test project, which always runs.
set -euo pipefail
source "$(dirname "$0")/_env.sh"

image="localhost/xdrishti/proxy:${XD_VERSION:-0.1.0}"
if ! command -v podman >/dev/null 2>&1 || ! podman image exists "$image" 2>/dev/null; then
  say "Proxy config: skipped (no $image to run 'caddy validate' with; the Caddyfile tests still ran)"
  exit 0
fi
say "Proxy config: caddy validate"
podman run --rm -v "$ROOT/deploy/caddy/Caddyfile:/etc/caddy/Caddyfile:ro" "$image" \
  caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1 \
  || { echo "caddy validate failed for deploy/caddy/Caddyfile" >&2; exit 1; }

# The assistant's chat is a server-sent event stream (P9-03): through the real proxy it must arrive in pieces (a 2 s gap between two events is seen as
# a gap, so nothing buffered it) and must not be compressed, while other API responses still are. The upstream is a small Python server on the host's
# loopback (the container reaches it as host.containers.internal); the proxy runs in the proxy image with the real Caddyfile, only the upstream address replaced.
say "Proxy config: the chat stream is not buffered or compressed"
python3 -I - "$ROOT/deploy/caddy/Caddyfile" "$image" <<'PY' || { echo "the chat stream check through the proxy failed" >&2; exit 1; }
import http.client, http.server, os, socket, subprocess, sys, tempfile, threading, time

caddyfile, image = sys.argv[1], sys.argv[2]

def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]

class Upstream(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def log_message(self, *a): pass
    def do_GET(self):
        if self.path.startswith("/health"):
            self.send_response(200); self.send_header("Content-Length", "2"); self.end_headers(); self.wfile.write(b"ok"); return
        body = b'{"rows":[' + b",".join([b'{"name":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","value":1234567}'] * 200) + b"]}"
        self.send_response(200); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def do_POST(self):
        n = int(self.headers.get("Content-Length", "0")); self.rfile.read(n)
        self.send_response(200); self.send_header("Content-Type", "text/event-stream"); self.send_header("Transfer-Encoding", "chunked"); self.end_headers()
        def chunk(t):
            b = t.encode(); self.wfile.write(b"%x\r\n" % len(b) + b + b"\r\n"); self.wfile.flush()
        chunk("event: turn\ndata: {}\n\n"); time.sleep(2); chunk("event: done\ndata: {}\n\n"); self.wfile.write(b"0\r\n\r\n"); self.wfile.flush()

up_port, proxy_port = free_port(), free_port()
server = http.server.ThreadingHTTPServer(("127.0.0.1", up_port), Upstream)
threading.Thread(target=server.serve_forever, daemon=True).start()
text = open(caddyfile).read().replace("xd-api:8080", f"host.containers.internal:{up_port}")
with tempfile.NamedTemporaryFile("w", suffix=".Caddyfile", delete=False) as f:
    f.write(text); path = f.name
os.chmod(path, 0o644)
name = f"xd-proxy-check-{proxy_port}"
subprocess.run(["podman", "run", "--rm", "-d", "--name", name, "-p", f"127.0.0.1:{proxy_port}:8080", "-v", f"{path}:/etc/caddy/Caddyfile:ro", image], check=True, stdout=subprocess.DEVNULL)
try:
    for _ in range(50):
        try:
            c = http.client.HTTPConnection("127.0.0.1", proxy_port, timeout=5); c.request("GET", "/health/live"); r = c.getresponse(); r.read()
            if r.status == 200: break
        except OSError: pass
        time.sleep(0.2)
    else:
        raise SystemExit("the proxy did not start")
    c = http.client.HTTPConnection("127.0.0.1", proxy_port, timeout=20)
    started = time.monotonic()
    c.request("POST", "/api/assistant/chat", body=b"{}", headers={"Accept-Encoding": "gzip, zstd", "Content-Type": "application/json"})
    r = c.getresponse()
    encoding = r.getheader("Content-Encoding")
    first = r.fp.readline(); first_at = time.monotonic() - started
    while True:
        line = r.fp.readline()
        if line.startswith(b"event: done") or not line: break
    last_at = time.monotonic() - started
    if encoding: raise SystemExit(f"the chat stream was compressed ({encoding})")
    if first_at > 1.5: raise SystemExit(f"the first event came after {first_at:.1f} s: the stream was buffered")
    if last_at - first_at < 1.5: raise SystemExit("the events arrived together: the stream was buffered")
    c2 = http.client.HTTPConnection("127.0.0.1", proxy_port, timeout=10)
    c2.request("GET", "/api/other", headers={"Accept-Encoding": "gzip"}); r2 = c2.getresponse(); r2.read()
    if r2.getheader("Content-Encoding") != "gzip": raise SystemExit("other API responses are no longer compressed")
    print(f"chat stream ok: first event {first_at:.2f} s, second {last_at:.2f} s, not compressed; other responses gzip")
finally:
    subprocess.run(["podman", "rm", "-f", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    server.shutdown(); os.unlink(path)
PY
