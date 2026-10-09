#!/usr/bin/env bash
# PreToolUse (Edit|Write|MultiEdit): refuse to write anything that looks like a real secret into the repo.
# Secrets live in the macOS Keychain and reach containers as files (ADR 0003) — never in source.
set -euo pipefail
input="$(cat)"
file="$(jq -r '.tool_input.file_path // ""' <<<"$input")"
content="$(jq -r '[.tool_input.content, .tool_input.new_string, (.tool_input.edits[]?.new_string)] | map(select(. != null)) | join("\n")' <<<"$input")"

pattern='-----BEGIN [A-Z ]*PRIVATE KEY-----|AKIA[0-9A-Z]{16}|(api[_-]?key|secret|token|password)[A-Za-z_]*["'"'"']?[[:space:]]*[:=][[:space:]]*["'"'"'][A-Za-z0-9+/_=-]{24,}["'"'"']'
if grep -Eiq -- "$pattern" <<<"$content"; then
  echo "Blocked: content written to ${file:-a file} looks like a secret. Secrets belong in the Keychain and reach" >&2
  echo "containers as files (ADR 0003). Reference the configuration key instead of the value." >&2
  exit 2
fi
