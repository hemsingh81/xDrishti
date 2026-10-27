#!/usr/bin/env bash
# The `xd` command line (P5-07, ADR 0061): a thin wrapper over the `xd` verb of the Api host, like simulate.sh.
#   scripts/xd.sh strategy validate my.yaml
#   scripts/xd.sh strategy test my.yaml --symbols 'RELIANCE,TCS' [--method quick|full] [--from 2025-01-01 --to 2025-12-31]
#   scripts/xd.sh learn cells --as-of 2026-04-30 --run RUNKEY:intraday:FIXED_RR   (P6-01, ADR 0064: cell statistics snapshot)
#   scripts/xd.sh learn features --run RUNKEY   (P6-04, ADR 0066: decision-time features of a stored run)
#   scripts/xd.sh learn training-set --run RUNKEY:intraday:FIXED_RR   (P6-04: labels joined to features, record kept)
#   scripts/xd.sh learn model --run RUNKEY:intraday:FIXED_RR   (P6-07, ADR 0070: train, validate against the baselines, register; files in data/models)
#   scripts/xd.sh learn designate --model 1 --role champion --reason "..."   (only an Eligible model can be champion)
#   scripts/xd.sh learn experiment --baseline RUNKEY --candidate RUNKEY --knob NAME --kind slippage --from 8 --to 12   (P6-11, ADR 0072: judge a knob change between two stored runs, recorded)
#   scripts/xd.sh learn phase-check [--replay-seconds X]   (P6-18, ADR 0080: the P6 exit criteria from stored records, MET / NOT MET / NOT EVALUABLE, limits printed)
#   scripts/xd.sh plan run [--date YYYY-MM-DD] [--dry-run]   (P7-08, ADR 0086: the fail-closed nightly pipeline now; a failing gate publishes nothing and exits 1; --dry-run writes nothing)
#   scripts/xd.sh assistant draft "<idea>" [--show]   (P9-07b: asks the model for a strategy DRAFT, text only; confirming is the owner's act on the Assistant screen)
#   scripts/xd.sh assistant propose [--kind K] [--cell KEY] [--dry-run]   (P9-08: asks the model for up to three hypothesis or basket proposals; the engine checks them; proposals only, the owner decides on the Suggestions screen)
#   scripts/xd.sh assistant briefing [--kind daily|weekly] [--subject S] [--store] [--new-version]   (P9-06, ADR 0140: the briefing or weekly review text; prints the fixed-format text, --store keeps it; the model phrases only under both switches)
#   scripts/xd.sh eval compare|accept|show ...   (P9-09, ADR 0143: the assistant eval; `run` and `grade` need the model and the eval tool server inside the stack: use scripts/xd-eval.sh, which runs this verb in a one-shot container; `accept` is the owner's act)
#   scripts/xd.sh plan phase-check [--through YYYY-MM-DD]   (P7-18, ADR 0097: the P7 exit criteria from stored plan runs, MET / NOT MET / NOT EVALUABLE, limits printed; read only)
#   scripts/xd.sh reports phase-check   (P8-17, ADR 0126: the P8 exit criteria from stored runs, MET / NOT MET / NOT EVALUABLE, limits printed; read only)
#   scripts/xd.sh charge-sheet show [--sheet ID]   (P8-23, ADR 0127: whether the owner's charge-sheet confirmation stands; read only; confirm and revoke are the owner's act with a fresh password on the Settings screen, the verb refuses them)
#   scripts/xd.sh lab runs [--strategy ID] [--state succeeded] [--limit 20]
# Exit codes: 0 done (a full test: PASS), 1 refused (located problems on stderr), 2 wrong command line or configuration, 3 failure or timeout,
# 4 a full test with verdict MARGINAL or FAIL, 130 cancelled. A test needs the stack running (deploy/xd-up.sh): xd-api carries the run out; this command stores
# the version, queues the run and waits. The one secret is the database password, read from the Keychain item xdrishti/db/app; `validate` needs none.
set -euo pipefail
source "$(dirname "$0")/_env.sh"
export DOTNET_ENVIRONMENT=Development
export Models__Folder="$ROOT/data/models"
if password="$(security find-generic-password -a xdrishti -s xdrishti/db/app -w 2>/dev/null)"; then
  export Database__Password="$password"
elif [[ "${1:-} ${2:-}" != "strategy validate" ]]; then
  echo "xd.sh: the Keychain has no item xdrishti/db/app (start the stack once with deploy/xd-up.sh)" >&2
  exit 2
fi
unset password
# the positional file of `strategy validate|test FILE` becomes an absolute path (the script changes directory below); no other argument is touched
args=("$@")
if [[ "${1:-}" == "strategy" && -n "${3:-}" && -f "$3" ]]; then
  args[2]="$(cd "$(dirname -- "$3")" && pwd)/$(basename -- "$3")"
fi
cd "$ROOT/backend/src/XDrishti.Api"
exec dotnet run -c Release --no-launch-profile -- xd "${args[@]+"${args[@]}"}"
