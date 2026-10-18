#!/usr/bin/env bash
# Cold-start figures of the rules compiler (ISS-12, ADR 0034 amendment 2): runs the explicit compile benchmarks of the Strategies tests in a fresh process, N times, and reports the minimum, median and maximum.
#   scripts/bench-coldstart.sh [runs] [--no-build]        (default: 15 runs)
# Three figures: process-cold (the very first compile of a process, no warm-up: reported, not judged), the first compile after CompilerWarmup.Run (what a person waits for) and the warm compile.
# Exit code 1 when a median is over its target: first compile after the warm-up > 20 ms, warm compile > 3 ms, warm-up > 150 ms. Run it before a release and whenever the Strategies or Indicators
# libraries or the way the hosts are published change; it is not part of check.sh because it needs a quiet machine (close other work first: a busy machine shifts every figure by 10 to 15 percent).
set -euo pipefail
source "$(dirname "$0")/_env.sh"

RUNS=15
BUILD=1
for arg in "$@"; do
  case "$arg" in
    --no-build) BUILD=0 ;;
    '' | *[!0-9]*) echo "usage: scripts/bench-coldstart.sh [runs] [--no-build]" >&2; exit 2 ;;
    *) RUNS="$arg" ;;
  esac
done

cd "$ROOT/backend"
PROJECT=tests/XDrishti.Strategies.Tests
if [ "$BUILD" = 1 ]; then
  say "Build (Release)"
  dotnet build "$PROJECT/XDrishti.Strategies.Tests.csproj" -c Release --nologo -v q
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
: >"$WORK/metrics.txt"

say "Running $RUNS fresh processes of each benchmark"
for ((run = 1; run <= RUNS; run++)); do
  # Interleaved, so that a change in the load of the machine is spread over both benchmarks.
  for test in Compile_bind_and_build_ten_conditions_cold_and_warm Compile_ten_conditions_first_run_after_the_warmup; do
    # A failed assertion of the benchmark still prints its figures; the medians below decide.
    dotnet test --project "$PROJECT" -c Release --no-build --output Detailed -- --show-stdout All --explicit only --filter-method "*$test*" >"$WORK/run.txt" 2>&1 || true
    grep -E '^[[:space:]]*metric ' "$WORK/run.txt" | awk '{ print $2, $3 }' >>"$WORK/metrics.txt" || true
  done
done

# name, unit, target (0 = reported only)
FAILED=0
printf '\n%-28s %9s %9s %9s %9s   %s\n' "figure" "min" "median" "max" "target" "runs"
for spec in "process_cold_ms:ms:0" "process_cold_jit_methods:methods:0" "warmup_ms:ms:150" "after_warmup_first_ms:ms:20" "after_warmup_jit_methods:methods:0" "warm_median_ms:ms:3"; do
  IFS=: read -r name unit target <<<"$spec"
  grep -E "^$name " "$WORK/metrics.txt" | awk '{ print $2 }' | sort -n >"$WORK/$name.txt" || true
  count="$(wc -l <"$WORK/$name.txt" | tr -d ' ')"
  if [ "$count" -lt 1 ]; then
    printf '%-28s %s\n' "$name" "no figures: the benchmark did not run"
    FAILED=1
    continue
  fi
  # The median is the middle one of the sorted figures (the upper one of the two when there is an even number).
  read -r low mid high < <(awk '{ v[NR] = $1 } END { print v[1], v[int(NR / 2) + 1], v[NR] }' "$WORK/$name.txt")
  if [ "$target" = 0 ]; then
    shown="-"
  else
    shown="<= $target"
    if awk -v m="$mid" -v t="$target" 'BEGIN { exit !(m > t) }'; then
      FAILED=1
      shown="$shown  OVER"
    fi
  fi
  if [ "$count" -ne "$RUNS" ]; then
    FAILED=1
    shown="$shown  (only $count of $RUNS runs gave a figure)"
  fi
  printf '%-28s %9.2f %9.2f %9.2f %9s   %s\n' "$name" "$low" "$mid" "$high" "$shown" "$count"
done

echo
echo "process_cold_ms  the first compile of the ten conditions in a fresh process, no warm-up (reported, not judged: ISS-12)"
echo "warmup_ms        CompilerWarmup.Run, once per process, on a background thread of the host"
echo "after_warmup_first_ms  the first compile of the ten conditions after the warm-up; the JIT methods are what the warm-up did not cover"
echo "warm_median_ms   the compile of the ten conditions in a process that has done it many times"
if [ "$FAILED" != 0 ]; then
  echo "A median is over its target, or a benchmark gave no figure." >&2
  exit 1
fi
say "Within the targets"
