# AI-01 — The assistant reads replay, readiness and shadow results

- **Status:** Approved (coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended). Not built.
- **Phase / roadmap row:** AI — assistant, row AI-01 (new; GAP-10)
- **Size:** about 2 agent-days (estimate) · **Needs:** P9-02 tool server, BK-12b service token (ADR 0267), SR-01 replay API (ADR 0272), CF-11 readiness (ADR 0221), CF-08 shadow summary (ADR 0212), P9-09 eval (ADR 0143)
- **Safety:** not a SAFETY slice. GET routes only, through the read-only service token; no write route accepts the token. The eval baseline re-acceptance is the owner's act.
- **Design refs:** docs/design/14-ai-assistant.md (tools); ADR 0143, 0267, 0272, 0221, 0212

## Goal
The owner can ask the assistant "how did the last replay do?", "is strategy X ready?" or "what does the shadow record say?" and get figures read from the stored results, with their labels and states, not guesses.

## Verified facts (main at 30777841)
- The service credential is scheme `xd-service-read`, policy `AssistantBasketRead` (backend/src/XDrishti.Api/Auth/ServiceReadAuthentication.cs:9,12; ApiSetup.cs:411), accepted on exactly two routes: `GET /api/reports/baskets/{id}/profile` and `/ranking` (ReportsEndpoints.cs:111,118). Pinned by `backend/tests/XDrishti.Api.IntegrationTests/Auth/ServiceReadTokenTests.cs`.
- The tool server calls the API through `BasketApi` with `Mcp:Api:BaseUrl` / `Mcp:Api:Token`, fail-closed to `tool.unavailable` (backend/src/XDrishti.Infrastructure/Ai/ReadOnly/AiReadOnlyRegistration.cs:54-65; ADR 0267 §3-4).
- `get_report` kinds are `summary|groups|pivot|what_works|equity|distribution|insights|basket_profile|basket_ranking|basket_context` (backend/src/XDrishti.Mcp/GetReportTool.cs:14). No tool, catalogue entry or reader in `XDrishti.Mcp` or `Infrastructure/Ai` mentions replay, readiness or shadow (grep: none).
- The data exists behind owner-session GETs: `GET /api/replay/runs`, `/runs/{id}`, `/runs/{id}/equity`, `/runs/{id}/sessions` (backend/src/XDrishti.Api/Endpoints/ReplayEndpoints.cs:18-46); `GET /api/confidence/scorecards`, `/scorecards/{strategyId}/versions/{version}` (ReadinessEndpoints.cs:19-26); `GET /api/paper/shadow/summary` (PaperShadowEndpoints.cs:19).
- The eval runs against `xd-api-eval` over the fixture (deploy/CLAUDE.md, ADR 0267 §5); question sets live in `eval/sets/` (`basket-questions.jsonl` has 8); `scripts/xd-eval.sh accept RUN` is the owner's baseline act (scripts/xd-eval.sh:8).

## Decisions (each as recommended)
1. **Through the API, same token, new narrow policy** `AssistantEvidenceRead` (same scheme), placed on exactly six GETs: `replay/runs`, `replay/runs/{id}`, `replay/runs/{id}/equity`, `confidence/scorecards`, `confidence/scorecards/{strategyId}/versions/{version}` and `paper/shadow/summary`. *Recommended* over direct SQL: the API already computes the labels (hindsight, gross/net, states) and the eval reads its own API.
2. **One new tool** `get_evidence` with `kind = replay_list | replay_run | readiness_list | readiness_card | shadow_summary`, not three: one argument schema, one log line.
3. **Labels are part of the answer:** every replay answer carries its hindsight labels and the "research, not evidence" fence of ADR 0272; every readiness item its state; `unavailable` is passed through as text, never turned into a number.
4. **Replay sessions and trades stay out** (row-heavy; the owner reads them on `/simulation/replay`).

## Scope
**In:** policy and the six route markings; `EvidenceApi` client in `Infrastructure/Ai/ReadOnly`; tool `get_evidence`; fixture rows for one replay run, two scorecards and a shadow summary; 4 eval questions; tests.
**Out:** any write route; replay start/cancel; owner acts; changing the eval thresholds.

## Acceptance criteria
1. With the service token, the six GETs return 200; every other route (including `POST /api/replay/runs` and `PUT /api/paper/shadow/settings`) returns 401/403 (extend `ServiceReadTokenTests`; the route count is pinned at 2 + 6).
2. `get_evidence` with a missing token or base URL, a timeout, a non-200/404/422 status or an oversized body answers `tool.unavailable` and calls nothing else (as ADR 0267 §4).
3. A replay answer contains the run's labels verbatim (e.g. `filters_without_history`, `survivorship`) and the fence text; a test fails if a label is dropped.
4. Four new eval questions (new set `eval/sets/evidence-questions.jsonl`): last replay's net result with its labels; a strategy's readiness state; why a readiness item is unavailable; the shadow record's mean R with its interval and the "separate count" statement. Each forbids "ready to trade", "guaranteed", "buy".
5. The eval run passes the existing thresholds (eval/thresholds.json) on the new set; the owner then re-accepts the baseline with `scripts/xd-eval.sh accept RUN` (owner act, not done by the build).
6. `xd-mcp` and `xd-mcp-eval` compose rules are unchanged (no new secret, no new network) — `McpComposeRulesTests`, `EvalComposeRulesTests` green.

## Contract
- **API:** no new route; six existing GETs gain `RequireAuthorization(AssistantEvidenceRead)`. `scripts/gen-api.sh` only if the descriptions change.
- **Tool:** `get_evidence(kind, id?, strategyId?, version?)`; output rows with a `state` column.
- **Data:** fixture JSON rows only; no migration.

## Test plan
Integration: token accepted on six, refused elsewhere. Unit: tool argument validation, fail-closed paths, label pass-through. Eval: the four questions in `xd-eval.sh run --set evidence`.

## Risks
Widening the token's reach: kept to six named GETs and pinned by test; revoking the token still closes all eight. Replay figures quoted out of context: AC 3 keeps the fence in every answer.

## Done when
- [ ] ACs 1–6 have evidence; `scripts/check.sh` green; reviewer: no blockers
- [ ] ADR 0267 amended (the token's route list); owner re-accepts the eval baseline
