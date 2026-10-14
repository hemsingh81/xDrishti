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
