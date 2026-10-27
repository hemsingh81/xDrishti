#!/usr/bin/env bash
# Downloads the model file pinned in ADR 0136 for the local assistant (P9) into data/models-llm (git-ignored), checks the size and the
# SHA-256 and refuses on any mismatch. Resumable (re-run after an interruption). Nothing is downloaded before you answer y.
#   scripts/fetch-model.sh            ask, download, verify
#   scripts/fetch-model.sh --verify   only verify a file that is already there (no download, no question)
# The two models of the P0-15 benchmark (Qwen3-30B-A3B, gpt-oss-20b) are fetched by scripts/llm-models.sh into
# ~/Library/Application Support/xDrishti/models; scripts/xd-ai.sh looks in both folders.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${XD_FETCH_DIR:-$ROOT/data/models-llm}"

# Pinned in docs/adr/0136 (read on huggingface.co, 2026-10-27). Third-party conversion of Qwen/Qwen3-30B-A3B-Instruct-2507 (Apache-2.0).
REPO="unsloth/Qwen3-30B-A3B-Instruct-2507-GGUF"
COMMIT="eea7b2be5805a5f151f8847ede8e5f9a9284bf77"
FILE="Qwen3-30B-A3B-Instruct-2507-Q4_K_M.gguf"
BYTES=18556686752
SHA256="6c997b8af17debdfb01d890214400ccbab00db6acc0ba8da5de1cc906c4774d0"
URL="https://huggingface.co/$REPO/resolve/$COMMIT/$FILE"

path="$DEST/$FILE"; part="$path.part"
size_of() { stat -f %z "$1" 2>/dev/null || stat -c %s "$1"; }
verify() { # verify <file>: size, then SHA-256
  [[ "$(size_of "$1")" == "$BYTES" ]] || { echo "size is $(size_of "$1"), expected $BYTES" >&2; return 1; }
  echo "$SHA256  $1" | shasum -a 256 -c - >/dev/null 2>&1 || { echo "SHA-256 differs from the pin" >&2; return 1; }
}

if [[ "${1:-}" == "--verify" ]]; then
  [[ -f "$path" ]] || { echo "not there: $path" >&2; exit 1; }
  verify "$path" && echo "ok: $FILE verified" && exit 0
  echo "REFUSED: $FILE does not match the pin" >&2; exit 1
fi
[[ $# -eq 0 ]] || { echo "usage: $0 [--verify]" >&2; exit 2; }

mkdir -p "$DEST"
if [[ -f "$path" ]] && verify "$path"; then echo "ok: $FILE already present and verified"; exit 0; fi

have=0; [[ -f "$part" ]] && have="$(size_of "$part")"
free_kb="$(df -k "$DEST" | awk 'NR==2 {print $4}')"
need=$(( (BYTES - have) / 1024 + 1048576 ))   # the rest of the file plus 1 GiB
cat <<INFO
About to download:
  file    $FILE  ($BYTES bytes, about $((BYTES / 1000000000)) GB; already have $have bytes)
  from    $URL
  to      $part, then $path
  check   size and SHA-256 $SHA256
  disk    $((free_kb / 1048576)) GiB free in $DEST
INFO
[[ "$free_kb" -ge "$need" ]] || { echo "Not enough free disk space." >&2; exit 1; }
read -r -p "Download now? [y/N] " answer
[[ "$answer" == "y" || "$answer" == "Y" ]] || { echo "Cancelled; nothing downloaded."; exit 0; }

# A partial file that is already too long cannot be resumed.
if [[ "$have" -gt "$BYTES" ]]; then rm -f "$part"; fi
# A complete .part (killed after the last byte, before the move) is verified as it is: the server would answer 416 to a resume.
if [[ "$have" -lt "$BYTES" ]]; then
  curl -fL --proto '=https' --proto-redir '=https' --retry 5 --retry-delay 5 -C - -o "$part" "$URL"
fi
if verify "$part"; then
  mv "$part" "$path"
  echo "ok: $FILE verified and ready in $DEST"
else
  rm -f "$part"
  echo "REFUSED: the downloaded file does not match the pin; it was deleted. Do not use it." >&2
  exit 1
fi
