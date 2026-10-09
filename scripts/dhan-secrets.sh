#!/usr/bin/env bash
# Stores Dhan credentials for the P0-13 trial in the macOS Keychain (service xdrishti/dhan/main/<name>).
# Values are typed at hidden prompts handled by `security` itself — they never appear in arguments, environment,
# files, shell history or logs. Run it in your own terminal; Claude never sees these values.
set -euo pipefail
PREFIX="xdrishti/dhan/main"

store() { # <name> <what to enter>
  printf '\n→ %s\n' "$2"
  security add-generic-password -U -a xdrishti -s "$PREFIX/$1" -w
}

echo "Dhan credentials for the primary Data account (used only by the read-only trial)."
echo "Create the API key/secret at web.dhan.co → My Profile → Access DhanHQ APIs → API key (set any redirect URL you control)."
store client-id "Dhan client ID (dhanClientId)"
store api-key "API key (app_id)"
store api-secret "API secret (app_secret)"

echo
read -r -p "Also store PIN + TOTP secret to test the fully unattended token route? (y/N) " answer
if [[ "${answer:-n}" =~ ^[Yy]$ ]]; then
  echo "Note: this puts a second factor next to the password on this Mac. It is for the experiment; decide afterwards whether to keep it."
  store pin "Dhan 6-digit PIN"
  store totp-secret "TOTP secret (the base32 text shown when setting up the authenticator)"
fi
echo
echo "Stored. Next: scripts/dhan-trial.sh run"
