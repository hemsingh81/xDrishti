#!/usr/bin/env bash
# Runs the API on the host (http://localhost:5080, hot reload) against the containerised database (localhost:5433).
# Start the stack first with deploy/xd-up.sh. Then run the web app with:  cd frontend && XD_API_URL=http://localhost:5080 npm run dev
set -euo pipefail
source "$(dirname "$0")/_env.sh"
export ASPNETCORE_ENVIRONMENT=Development
Database__Password="$(security find-generic-password -a xdrishti -s xdrishti/db/app -w)"
export Database__Password
export Serilog__WriteTo__1__Name=Seq Serilog__WriteTo__1__Args__serverUrl=http://localhost:5341
cd "$ROOT/backend"
exec dotnet watch --project src/XDrishti.Api/XDrishti.Api.csproj
