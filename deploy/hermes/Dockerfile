# Hermes Agent PoC image (P9-10, ADR 0144). FROM the project's official image by tag AND digest, then only our config and entrypoint are added:
# no installer script, no package manager, no download in this file (checked by HermesComposeRulesTests).
# The digest below is a PLACEHOLDER (all zeros): UNVERIFIED. `scripts/hermes-poc.sh pin` (owner act, asks y/N before it pulls) reads the real digest from `podman image inspect` and writes it here; `build` and `up` refuse the placeholder.
# The tag 0.22.5 is the newest release named in the spec on 2026-10-24 (UNVERIFIED by this build: nothing was pulled).
FROM docker.io/nousresearch/hermes-agent:0.22.5@sha256:0000000000000000000000000000000000000000000000000000000000000000

COPY --chmod=0444 config.yaml /opt/xd/config.yaml
COPY --chmod=0555 entrypoint.sh /opt/xd/entrypoint.sh

# UID 10000 is the image's own user (spec D1, UNVERIFIED until check H-1).
USER 10000:10000
ENTRYPOINT ["/opt/xd/entrypoint.sh"]
