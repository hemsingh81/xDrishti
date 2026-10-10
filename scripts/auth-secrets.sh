#!/usr/bin/env bash
# Stores (or changes) the owner's sign-in credentials in the macOS Keychain (service xdrishti/auth/<name>).
#   owner-email, owner-name  — typed at a normal prompt (Enter keeps the current value)
#   owner-password           — typed at a hidden prompt handled by `security` itself: it never appears in arguments,
#                              environment, files, shell history or logs. Run this in your own terminal; Claude never sees it.
# deploy/xd-up.sh copies the items into secret files for the containers; the stack creates the owner account on
# first start. To CHANGE the password of an existing owner: run this script, then `deploy/xd-up.sh --owner-reset`.
set -euo pipefail
PREFIX="xdrishti/auth"
ACCOUNT="xdrishti"

current() { security find-generic-password -a "$ACCOUNT" -s "$PREFIX/$1" -w 2>/dev/null || true; }

store_plain() { # <name> <label>
  local name="$1" label="$2" existing value
  existing="$(current "$name")"
  if [ -n "$existing" ]; then
    read -r -p "$label [$existing] (Enter keeps it): " value
    value="${value:-$existing}"
  else
    read -r -p "$label: " value
  fi
  [ -n "$value" ] || { echo "$label must not be empty." >&2; exit 1; }
  security add-generic-password -U -a "$ACCOUNT" -s "$PREFIX/$name" -w "$value" >/dev/null
}

echo "Owner account for xDrishti (the only account; there is no sign-up)."
store_plain owner-email "Sign-in e-mail"
store_plain owner-name "Display name"

if security find-generic-password -a "$ACCOUNT" -s "$PREFIX/owner-password" >/dev/null 2>&1; then
  read -r -p "A password is already stored. Change it? (y/N) " answer
  if [[ "${answer:-n}" =~ ^[Yy]$ ]]; then
    printf '\n→ New sign-in password (hidden)\n'
    security add-generic-password -U -a "$ACCOUNT" -s "$PREFIX/owner-password" -w
    echo "Stored. To apply it to the running stack: deploy/xd-up.sh --owner-reset"
  fi
else
  printf '\n→ Sign-in password (hidden)\n'
  security add-generic-password -U -a "$ACCOUNT" -s "$PREFIX/owner-password" -w
fi

echo
echo "Done. Next: deploy/xd-up.sh"
