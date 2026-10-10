#!/usr/bin/env bash
# One-time setup for the P0-15 LLM benchmark (idempotent): native llama.cpp b11530 (SHA-256 verified) and the pinned RamaLama image.
# Models are downloaded separately with scripts/llm-models.sh. Nothing needs sudo.
set -euo pipefail
SUPPORT="$HOME/Library/Application Support/xDrishti"
LLAMA="$SUPPORT/llm/llama.cpp-b11530"
TARBALL="llama-b11530-bin-macos-arm64.tar.gz"
SHA="547b15ee63b09438d2fcc75acd8d5b10336f7f16bf686c774ccd24c91c6f015b"
IMAGE="quay.io/ramalama/ramalama@sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e"

if [[ ! -x "$LLAMA/llama-server" ]]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  echo "↓ llama.cpp b11530 (12 MB, GitHub release asset)"
  curl -fsSL -o "$tmp/$TARBALL" "https://github.com/ggml-org/llama.cpp/releases/download/b11530/$TARBALL"
  echo "$SHA  $tmp/$TARBALL" | shasum -a 256 -c -
  mkdir -p "$LLAMA" && tar -xzf "$tmp/$TARBALL" -C "$LLAMA" --strip-components=1
fi
echo "✓ llama.cpp: $("$LLAMA/llama-server" --version 2>&1 | head -1)"

if ! podman image exists "$IMAGE"; then
  echo "↓ $IMAGE (≈ 1 GB) into the Podman machine"
  podman pull "$IMAGE"
fi
echo "✓ image present: $IMAGE"
