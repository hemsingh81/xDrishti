#!/bin/sh
# Hermes PoC entrypoint (P9-10): reads the two service credentials from the secret files into this process only (never into the data folder), rewrites the configuration from the baked copy,
# removes the places an agent could use to run code at the next start, then starts Hermes. The three folders are overridable for the shell test only.
# UNVERIFIED: the name of the command (`hermes`) and the environment variable the model key is read from (OPENAI_API_KEY is the usual OpenAI-compatible name).
# Known limit: the agent can edit /opt/data/config.yaml while it runs; the baked copy is restored at the next start only. Skills, memory, sessions and cron are kept for the weekly review.
set -eu
umask 077
SECRETS="${XD_SECRETS_DIR:-/run/secrets}"
BAKED="${XD_BAKED_DIR:-/opt/xd}"
DATA="${XD_DATA_DIR:-/opt/data}"
MCP_TOKEN="$(cat "$SECRETS/mcp_token_hermes")"
OPENAI_API_KEY="$(cat "$SECRETS/llm_api_key")"
XD_MCP_URL="${XD_MCP_URL:-http://xd-mcp:8080/mcp}"
export MCP_TOKEN OPENAI_API_KEY XD_MCP_URL
mkdir -p "$DATA"
rm -rf "$DATA/.env" "$DATA/plugins" "$DATA/hooks" "$DATA/.hooks"
install -m 0600 "$BAKED/config.yaml" "$DATA/config.yaml"
exec hermes "$@"
