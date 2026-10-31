# xd-exec-proxy (ADR 0172): the host-pinned forward proxy of the execution service. FROM Alpine by tag AND digest (the multi-arch index), tinyproxy at an exact package version.
# The allow-list is not baked in: compose mounts deploy/exec-proxy/allowlist.txt read-only and the entrypoint writes the config from it at every start.
FROM docker.io/library/alpine:3.22.2@sha256:4b7ce07002c69e8f3d704a9c5d6fd3053be500b7f1c69fc0d80990c2ad8dd412

RUN apk add --no-cache tinyproxy=1.11.2-r0

COPY --chmod=0555 entrypoint.sh /opt/xd/entrypoint.sh

USER tinyproxy
EXPOSE 8888
ENTRYPOINT ["/opt/xd/entrypoint.sh"]
