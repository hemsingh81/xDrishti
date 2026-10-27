# P9-10 — Hermes Agent PoC: decision record

**Status: PoC not run (image digest still the placeholder, UNVERIFIED).**

- Date: 2026-10-27
- Hermes image: docker.io/nousresearch/hermes-agent:0.22.5@sha256:0000000000000000000000000000000000000000000000000000000000000000
- Model and server mode: not measured (alias, context 65536, YaRN settings: fill in)
- Repository commit: 15e0943e
- Run by: not measured

## Checks
| Id | Check | Result | Evidence | Note |
|---|---|---|---|---|
| H-1 | Healthy, non-root, read-only | not measured | not measured | not measured |
| H-2 | Completion at context 65536 | not measured | not measured | not measured |
| H-3 | Secrets, capabilities, ports, mounts | not measured | not measured | not measured |
| H-4 | No route out | not measured | not measured | not measured |
| H-5 | Registered tools = include list | not measured | not measured | not measured |
| H-6 | Data questions (27 of 30, grounded) | not measured | not measured | not measured |
| H-7 | Ideas (8 of 10 valid) | not measured | not measured | not measured |
| H-8 | Five scheduled runs | not measured | not measured | not measured |
| H-9 | Injection probes (6 of 6) | not measured | not measured | not measured |
| H-10 | 24 h memory and latency | not measured | not measured | not measured |
| H-11 | CVE status | not measured | not measured | not measured |
| H-12 | Owner verdicts and paired comparison | not measured | not measured | not measured |

## Findings
not measured (surprises; UNVERIFIED items now verified)

## Cost
not measured (memory, swap, first-token times at 65536 versus the built-in at 16384)

## CVE status
not measured (advisory sources read, with dates; see data/hermes-poc/<date>/H-11-sources.txt)

## Owner verdict by day
- Day 1: not measured
- Day 2: not measured
- Day 3: not measured
- Day 4: not measured
- Day 5: not measured

## Verdict
PENDING (rule suggests: DEFER)

## Reason
not measured

## Conditions
For ADOPT_OPTIONAL: monthly `scripts/hermes-poc.sh cve`, weekly `review`, a version bump is a deliberate re-check (`pin` then `check all`). Messaging needs its own SAFETY spec.

## Signed
not signed 
