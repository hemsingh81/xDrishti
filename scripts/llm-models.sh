#!/usr/bin/env bash
# Downloads the pinned LLM benchmark models (P0-15) into ~/Library/Application Support/xDrishti/models and verifies SHA-256.
# Resumable: re-run after an interruption. Sources are public, ungated Hugging Face repositories pinned to a commit.
#   scripts/llm-models.sh                 all models (≈ 44.8 GB), one after another
#   scripts/llm-models.sh main dense      only the named models (embed small dense gptoss main); run several in parallel to use more bandwidth
set -euo pipefail
DEST="$HOME/Library/Application Support/xDrishti/models"
mkdir -p "$DEST"

# name | repo | commit | file | bytes | sha256
MODELS=(
  "embed|nomic-ai/nomic-embed-text-v1.5-GGUF|0188c9bf409793f810680a5a431e7b899c46104c|nomic-embed-text-v1.5.Q8_0.gguf|146146432|3e24342164b3d94991ba9692fdc0dd08e3fd7362e0aacc396a9a5c54a544c3b7"
  "small|Qwen/Qwen3-8B-GGUF|7c41481f57cb95916b40956ab2f0b139b296d974|Qwen3-8B-Q4_K_M.gguf|5027783488|d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785"
  "dense|Qwen/Qwen3-14B-GGUF|530227a7d994db8eca5ab5ced2fb692b614357fd|Qwen3-14B-Q4_K_M.gguf|9001752960|500a8806e85ee9c83f3ae08420295592451379b4f8cf2d0f41c15dffeb6b81f0"
  "gptoss|ggml-org/gpt-oss-20b-GGUF|ef9b12f2ff56c69cf32153a02784e7a3c88bf524|gpt-oss-20b-MXFP4.gguf|12109566624|27cd6c432c7672cb812a92f611cf3ba7bbc35928262bb1e1253ff4ee6ae35901"
  "main|Qwen/Qwen3-30B-A3B-GGUF|e4d4bafdfb96a411a163846265362aceb0b9c63a|Qwen3-30B-A3B-Q4_K_M.gguf|18556685824|0d003f6662faee786ed5da3e31b29c978de5ae5d275c8794c606a7f3c01aa8f5"
)

for wanted in "$@"; do
  case "$wanted" in embed|small|dense|gptoss|main) ;; *) echo "unknown model name '$wanted' (embed small dense gptoss main)" >&2; exit 2 ;; esac
done

for entry in "${MODELS[@]}"; do
  IFS='|' read -r name repo commit file bytes sha <<<"$entry"
  if [[ $# -gt 0 && " $* " != *" $name "* ]]; then continue; fi
  path="$DEST/$file"
  if [[ -f "$path" && "$(stat -f %z "$path")" == "$bytes" ]] && echo "$sha  $path" | shasum -a 256 -c - >/dev/null 2>&1; then
    echo "✓ $file already present and verified"; continue
  fi
  echo "↓ $file ($((bytes / 1000000)) MB) from $repo@${commit:0:12}"
  # A complete-but-corrupt file makes `curl -C -` fail with 416: drop it and start over.
  if [[ -f "$path" && "$(stat -f %z "$path")" -ge "$bytes" ]]; then rm -f "$path"; fi
  curl -fL --retry 5 --retry-delay 5 -C - -o "$path" "https://huggingface.co/$repo/resolve/$commit/$file"
  echo "$sha  $path" | shasum -a 256 -c - || { echo "✗ checksum mismatch for $file — deleting"; rm -f "$path"; exit 1; }
  echo "✓ $file verified"
done
echo "All requested models downloaded and verified."
