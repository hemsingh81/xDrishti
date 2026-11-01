#!/usr/bin/env bash
# The owner-run probe of the expired-options backfill (P14-03 criterion 9, ADR 0177): runs `dotnet XDrishti.Feed.dll --probe-backfill` inside the running xd-feed container
# (it holds the vault key the Data account's token needs). It makes AT MOST FIVE requests to POST api.dhan.co/v2/charts/rollingoption, prints what Dhan returned (status, code, keys,
# array lengths, the timestamp as sent and as read in India time, the strike against the spot, the IV range, the bars the parser keeps) and STORES NOTHING. It prints no token, client id
# or URL. Record the result in the table of ADR 0177, then accept it with PUT /api/options/backfill/probe-acceptance (your password); the backfill refuses to run before that.
#   scripts/xd-probe-backfill.sh
# Needs the stack running (deploy/xd-up.sh) and the Data account connected. Exit codes: 0 the probe ran, 2 it could not start.
set -euo pipefail
exec podman exec xdrishti-xd-feed-1 dotnet XDrishti.Feed.dll --probe-backfill
