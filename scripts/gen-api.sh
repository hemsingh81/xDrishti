#!/usr/bin/env bash
# Regenerates the API contract (backend/contracts/XDrishti.Api.json) and the frontend's typed client from it.
# Run after any endpoint or DTO change, and commit both files with the change.
set -euo pipefail
source "$(dirname "$0")/_env.sh"
say "Generating OpenAPI contract"
(cd "$ROOT/backend" && dotnet build src/XDrishti.Api -p:OpenApiGenerateDocuments=true --nologo -v q)
say "Generating frontend types"
(cd "$ROOT/frontend" && npm run gen:api)
