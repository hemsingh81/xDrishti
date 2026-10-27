#!/usr/bin/env bash
# Removes leaked Testcontainers (label org.testcontainers=true, and ONLY that label) left by killed test runs.
# Safe rule: only containers older than XD_TEST_CONTAINER_MAX_AGE_MIN minutes (default 60, far beyond one gate run), so a
# concurrent run in another shell keeps its containers. Never fails the caller; skips with a warning without podman.
MAX_AGE_MIN="${XD_TEST_CONTAINER_MAX_AGE_MIN:-60}"
command -v podman >/dev/null 2>&1 || { echo "warn: podman not found, skipping test-container cleanup" >&2; exit 0; }
now=$(date +%s)
# JSON gives the creation time as epoch seconds; one "<id> <epoch>" line per labelled container.
podman ps -a --filter label=org.testcontainers=true --format json 2>/dev/null \
  | python3 -c 'import json,sys; [print(c["Id"], c["Created"]) for c in json.load(sys.stdin) or []]' 2>/dev/null \
  | while read -r id created; do
  [ -n "$id" ] || continue
  if [ $(( (now - created) / 60 )) -ge "$MAX_AGE_MIN" ]; then
    echo "removing leaked test container $id"
    podman rm -f -v "$id" >/dev/null 2>&1 || echo "warn: could not remove $id" >&2
  fi
done
exit 0
