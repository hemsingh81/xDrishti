#!/usr/bin/env bash
# Regenerates the API contract (backend/contracts/XDrishti.Api.json) and the frontend's typed client from it.
# It also carries the SignalR hub (OpenAPI cannot describe one): the backend writes the hub's methods, events and message types
# into the contract (extension x-xdrishti-hubs, schemas) and frontend/scripts/gen-hub-contract.mjs writes
# frontend/src/shared/realtime/hubContract.generated.ts from it.
# The same way it carries the list of commands a notice may name (extension x-xdrishti-attention-commands, from AttentionCommands in the Application layer):
# frontend/scripts/gen-attention-commands.mjs writes frontend/src/shared/api/attentionCommands.generated.ts, the only commands the client sends (ADR 0030).
# Run after any endpoint, DTO or hub change, and commit the generated files with the change.
set -euo pipefail
source "$(dirname "$0")/_env.sh"
say "Generating OpenAPI contract"
(cd "$ROOT/backend" && dotnet build src/XDrishti.Api -p:OpenApiGenerateDocuments=true --nologo -v q)
say "Generating frontend types"
(cd "$ROOT/frontend" && npm run gen:api)
