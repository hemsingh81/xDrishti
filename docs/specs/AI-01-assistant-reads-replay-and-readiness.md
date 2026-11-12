# AI-01 — The assistant reads replay, readiness and shadow results

- **Status:** Built 2026-11-12, not deployed; the owner re-accepts the eval baseline. Approved (coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended).
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
- [x] ACs 1–6 have evidence (see As built); `scripts/check.sh` not run by the builder (main runs the gate); reviewer pass pending
- [x] ADR 0267 amended (the token's route list), new ADR 0280
- [ ] Owner re-accepts the eval baseline (owner act: `scripts/eval-db.sh reset`, run, `scripts/xd-eval.sh accept RUN`)

## As built (2026-11-12)
- **Policy and routes:** `AssistantEvidenceRead` (`ServiceReadAuthentication.AssistantEvidenceReadPolicy`, registered in `ApiSetup.AddAuth`) on the six GETs (`ListReplayRuns`, `GetReplayRun`, `GetReplayEquity`, `ListScorecards`, `GetScorecard`, `GetShadowSummary`); `ReplayEndpoints.cs` gains only the three `.RequireAuthorization(...)` lines. AC 1: `ServiceReadTokenTests` (route count 2 + 6 with every other endpoint and method 401; the policy list pinned by name; the principal passes exactly the two policies; writes and sessions/trades 401). No summary text changed, so the contract is unchanged (`gen-api` first step run, no diff).
- **Tool:** `get_evidence` (`XDrishti.Mcp/GetEvidenceTool.cs`) over `IAiEvidenceReader` (`Application/Abstractions/Ai`), implemented by `ApiEvidenceReader` + `EvidenceApiClient` + `EvidenceApiEndpointGuard` (`Infrastructure/Ai/ReadOnly/EvidenceApi`, registered by `AddAiEvidenceApi`, same `Mcp:Api` settings and token). AC 2 and 3: `EvidenceApiTests` (33 cases: labels and fence verbatim, five routes, bad arguments call nothing, no address or token, 401/403/5xx, 404, 422, transport error, timeout, oversize, non-JSON, no token or id in the log, guard table). The schema budget is kept at 2,300 (see ADR 0280 consequences); `McpToolCatalogTests` golden regenerated (17 tools incl. `make_chart`).
- **Eval:** `eval/fixture/fixture.json` gains `evidence` (one replay run, two strategy versions, one shadow session of 12 graded candidates), seeded by `FixtureSeeder.SeedEvidenceAsync`; `eval/sets/evidence-questions.jsonl` EQ-01..EQ-04; `AnswerGrader` marks an EQ question with `tool.unavailable` not evaluable. `EvalEvidenceGoldenTests` (real API over the seeded fixture, reports-only host) holds the numbers; `EvidenceQuestionsTests` (Domain) grades scripted answers. The eval was NOT run and no baseline accepted.
- **Reports-only host:** `Api:ReportsOnly` now also maps the replay, readiness and shadow modules, behind `ReportsOnlyGate` (404 for every route of them except the six, before authentication); `EvalBasketGoldenTests` route assertion updated accordingly.
- **Deviations:** (1) the fixture run is gross, so EQ-01 asks for "its result and labels" with the before-costs label, not a net result; (2) `survivorship` is in the run answer (`replay_run`) only, as the API derives it from the sessions; the list carries the labels of the parameters; (3) the question set is an addendum file like `basket-questions.jsonl` and is not yet wired into `xd-eval.sh run --set evidence` (the runner reads the locked sets only; wiring it changes the eval fingerprint and is the owner's call); (4) output is one `data` object (`kind`, `fence`, `body`) rather than rows with a `state` column, because the API body is passed on unchanged and each item already carries its own `state`; (5) descriptions of eight other tools were shortened to fit the schema budget. (6) two architecture rules over the replay tables gained the fixture seeder as a named exemption (insert-only, eval database only; ADR 0280).
