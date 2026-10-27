#!/bin/sh
# Hermes PoC entrypoint (P9-10): reads the two service credentials from /run/secrets into this process only (never into /opt/data), rewrites the configuration from the baked copy,
# then starts Hermes. UNVERIFIED: the name of the command (`hermes`) and the environment variable the model key is read from (OPENAI_API_KEY is the usual OpenAI-compatible name).
set -eu
umask 077
MCP_TOKEN="$(cat /run/secrets/mcp_token_hermes)"
OPENAI_API_KEY="$(cat /run/secrets/llm_api_key)"
XD_MCP_URL="${XD_MCP_URL:-http://xd-mcp:8080/mcp}"
export MCP_TOKEN OPENAI_API_KEY XD_MCP_URL
mkdir -p /opt/data
cp /opt/xd/config.yaml /opt/data/config.yaml
exec hermes "$@"
