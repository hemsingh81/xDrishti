# BK-12b — Basket profile and ranking for the assistant via the API

- **Status:** Approved (owner, 2026-11-11, in chat: "Yes, write + build")
- **Phase / roadmap row:** P9 addendum — BK-12b (closes [BK-12](BK-12-ai-basket-tools.md) criteria 2, 3, 4, 7 and the numeric parts of 10 to 12; ADR 0264 "Owner decision 2026-11-11")
- **Design refs:** docs/design/14-ai-assistant.md §3, §4(2) · ADR 0264 (deviation: the tool server cannot host the BK-07 handlers) · ADR 0139 (P9-05 tools, `IAiReportReader`) · ADR 0262 (BK-07 endpoints, cache) · P9-00 (data classes C0 to C2, envelope of 6,000 characters) · ADR 0143 (eval)
- **Needs (VERIFIED in code at a793b745 unless marked):**
  - `BasketReportReader` (`Application/Ai`) resolves `IQueryHandler<GetBasketProfileQuery, Result<BasketProfileView>>` and `IQueryHandler<GetBasketRankingQuery, Result<BasketRankingView>>` from a scope and answers `tool.unavailable` when the host has none. Name resolution (`BasketNameResolver`, `basket.unknown`) and shaping (`BasketReportShaper`, `SeriesSampler`) already happen in the reader.
  - API routes `GET /api/reports/baskets/{id}/profile` (`from`, `to`, `index`, `membersAsOf`) and `GET /api/reports/baskets/ranking` (`basket` = ids comma separated, `from`, `to`, `index`); both read only; 422 `reports.invalid`, 404 `reports.not_found`.
  - The API's authorization: a fallback policy needing a signed-in owner session (`SessionAuthentication`), then `CsrfProtectionMiddleware` and `StepUpMiddleware`. The API has **no** service credential today.
  - How `xd-mcp` reaches anything today: **only the database**, as `xd_mcp_read` and `xd_mcp_sql` (compose `Database__*ConnectionString`). It makes no HTTP call except its own health probe. The API calls `xd-mcp` (`Assistant__Mcp__Url`) with the bearer `mcp_token`; the tool server checks inbound tokens per client (`Mcp:Tokens:Api`, `Mcp:Tokens:Hermes`, `McpGateMiddleware`, `McpOptions.Validate` refuses shared token values). Both containers are on the `back` network.
  - Eval stack: `xd-mcp-eval` over the fixture database `xdrishti_eval`; there is **no API over the fixture** (no `xd-api-eval` service).
- **Size:** 2.5 agent-days (API service scheme and policy 0.6, HTTP handlers in `xd-mcp` 0.6, compose, secrets and `xd-up.sh` 0.3, eval API over the fixture 0.5, goldens BQ-01/02/04/06 and tests 0.5)
- **Safety:** yes (class C1/C2 data reaches the model). Read only: the credential opens exactly two GET routes; no write, no setting, no owner act; the tool server's database roles are unchanged; an API error fails closed.

## Goal
`get_report(report="basket_profile" | "basket_ranking")` answers on the real tool server with the figures the basket screens show, instead of `tool.unavailable`, without widening what the tool server can read from the database.

## Scope
**In:** a read-only service credential (`mcp_api_token`) and an API authentication scheme and policy that accept it on exactly the two routes; in `xd-mcp`, HTTP-backed implementations of the two query handlers registered in its composition (the reader is unchanged); compose and secret wiring; an API instance over the fixture for the eval (`xd-api-eval`, eval profile only); real goldens for BQ-01, BQ-02, BQ-04, BQ-06; tests; doc 14 §3 note; an ADR.
**Out:** any new tool or report value; any database grant to `xd_mcp_read` / `xd_mcp_sql`; any other API route reachable with the credential; the owner session in the tool server; any change to the P9 data-class rules (C0 never returned; names untrusted text); the context report (works today); Hermes changes.

## Decisions
1. **Route: the tool server calls the API with its own read-only bearer token**, checked by a dedicated scheme `ServiceRead` that only the two endpoints accept (policy `AssistantBasketRead` = an owner session **or** the service principal). The fallback policy still guards every other route, so the token opens nothing else; the database roles stay as narrow as ADR 0264 left them. Rejected: (a) reusing `mcp_token` (the API's credential towards the tool server; one secret both ways cannot be revoked alone); (b) trusting the `back` network (any container there would read reports); (c) forwarding the owner's cookie (it also opens write routes); (d) read-only ports in `xd-mcp` (owner: no).
2. **Same handler interfaces, another implementation.** `xd-mcp` registers `ApiBasketProfileHandler` and `ApiBasketRankingHandler` implementing the two `IQueryHandler` interfaces over a typed `HttpClient`; `BasketReportReader` and its shaping stay untouched, so the model's body is what the in-process handlers would give (BK-12 AC 2 compares with the endpoint JSON).
3. **Fail closed.** Transport error, timeout, a status other than 200/404/422, a body that does not deserialize or exceeds the cap, or a missing base URL or token: `tool.unavailable` with a fixed text. 404 maps to `basket.unknown`, 422 to `tool.invalid_args` with the problem `detail` as untrusted text. Never a partial body.
4. **The eval reads the fixture through its own API.** `xd-mcp-eval` is never pointed at the production API (production figures would leak into fixture goldens). `xd-api-eval` (API image, eval profile, `xdrishti_eval`, no `egress` network, no published port, no broker secrets, its own `mcp_api_token_eval`) serves the two routes; without it the eval tool server answers `tool.unavailable` as today.

## Acceptance criteria
1. **Credential scope.** Given `Authorization: Bearer <mcp_api_token>` and no session, then the two basket routes answer 200 on the seeded database; every other route (a test enumerates all endpoints from the endpoint data source, any method) answers 401; a non-GET to the two paths is never executed.
2. **Token checks.** Given a wrong, empty or missing token, then 401 with no detail; the comparison is fixed-time; given `Api:ServiceTokens:Mcp` unset or empty, the scheme accepts nothing; the API refuses to start when the service token equals another configured token.
3. **No owner powers.** Given the service principal, then it has no owner claim: `StepUpMiddleware` and every policy other than `AssistantBasketRead` refuse it (test over the policy list), and its calls write no session or audit row.
4. **Profile by hand via the API.** Given the BK-06/BK-07 hand fixture (A 100→110, B 50→45, C 200→250 and the index), when the real tool server runs `get_report(report="basket_profile", query={basket:"<name>", from, to})`, then the body equals the BK-12 shaping of the endpoint's `BasketProfileView` field for field, including `Hold` +8.3333 % and `Members.Counted` 3.
5. **States carried.** Given a four-member basket, then breadth, dispersion and correlation are `insufficient` with n 4, min 5 and no `number`, through the HTTP path (BK-12 AC 3).
6. **Ranking separation.** Given three baskets inside the noise rule, then `separated: false` and no rank field; given separated gaps, ranks (BK-12 AC 4).
7. **Fail closed.** Given the API down, a 500, a timeout above `Mcp:Api:TimeoutSeconds` (default 8, inside the 10 s tool timeout), malformed JSON, or no `Mcp:Api:BaseUrl`/token, then `tool.unavailable` with "basket reports are not reachable now" and `mcp.report` status `unavailable`; no exception reaches the model; 404 is `basket.unknown`; 422 is `tool.invalid_args` with the detail wrapped as untrusted text.
8. **Limits.** Given the fixture, the profile answers in under 1 s end to end; the 120-point sampling and list caps of ADR 0264 apply unchanged; a response body above `Mcp:Api:MaxResponseBytes` (default 2 MiB) is `tool.unavailable`.
9. **Database roles unchanged.** No migration in this slice; `ReadOnlyRolesTests` show `xd_mcp_read` and `xd_mcp_sql` with exactly ADR 0264's grants; an architecture test asserts `XDrishti.Mcp` has no `DbContext` and no write port, and the HTTP client's allow-list holds exactly the two path templates.
10. **No C0, no token in logs.** The P9-05 property test passes on the two reports through the HTTP path; the token never appears in a log event, exception text or tool body (scrub test).
11. **Eval goldens.** Given the eval stack with `xd-api-eval`, then BQ-01 ("not separated" with returns and n), BQ-02 (vol, drawdown, beta as the fixture gives, numbers or states), BQ-04 (Jaccard 0.4) and BQ-06 ("not enough data (4 of at least 5)") expect real answers in `eval/sets/basket-questions.jsonl`; the "figures equal the screen" check runs on them; the forbidden phrases stay. Without an eval API they are `not_evaluable`, never a pass.
12. **Eval isolation.** A compose lint test reads `deploy/compose.yaml`: `xd-mcp-eval` has no setting pointing at `xd-api`; `xd-api-eval` has no `egress` network, no published port, no Dhan or broker secret.
13. **Logging.** `mcp.report` (6120: report, status, ms) and a new `mcp.api_call` (next free id from 6124: route name, status code, ms); no basket name, symbol or token.

## Contract
- **API:** no new route. The two basket routes get `.RequireAuthorization("AssistantBasketRead")`. New scheme `ServiceRead` (`Api/Infrastructure`), option `Api:ServiceTokens:Mcp` (secret file), validated at start. `scripts/gen-api.sh` for the security note.
- **Tool server:** `Mcp:Api:BaseUrl`, `Mcp:Api:Token` (secret), `Mcp:Api:TimeoutSeconds` 8 `[Range(1,9)]`, `Mcp:Api:MaxResponseBytes`; handlers registered only when `BaseUrl` is set.
- **Deploy:** secrets `mcp_api_token`, `mcp_api_token_eval` created by `deploy/xd-up.sh` from the Keychain like the other tokens; `xd-api` mounts `Api__ServiceTokens__Mcp`, `xd-mcp` `Mcp__Api__Token`; `xd-api-eval` (profile `eval`) per Decision 4.
- **Data:** none.
- **Docs:** doc 14 §3, new ADR linked from ADR 0264.

## Non-functional
One extra hop inside `back`: p95 under 300 ms above the handler time on the fixture; the BK-07 cache serves repeats. No retry (it could pass the tool timeout).

## Test plan
- **API integration (Testcontainers):** AC 1 to 3 with endpoint enumeration.
- **Tool server unit:** adapters over a fake `HttpMessageHandler`: 200, 404, 422, 500, timeout, oversize, malformed, unset config.
- **End to end:** API and tool server test hosts on the seeded database (AC 4 to 6, 10).
- **Architecture:** AC 9. **Eval:** AC 11, 12.

## Open questions
1. `xd-api-eval` costs about 300 to 500 MB while the eval runs. Accept (eval profile only), or leave BQ-01/02/04/06 `not_evaluable`? Recommended: accept.

## UNVERIFIED
- The API's container port in `back` (assumed 8080) and whether the API image runs with every hosted and scheduled service off (needed by `xd-api-eval`; if not, a reports-only host switch adds about 0.5 d).
- That `CsrfProtectionMiddleware` passes a cookieless bearer GET (expected: GET is exempt).

## Done when
- [ ] Every criterion has evidence; `scripts/check.sh` green; `scripts/gen-api.sh` run; reviewer: no blockers/majors (told to look for any route the token opens besides the two, and any C0 leakage)
- [ ] BK-12 build evidence and the roadmap row updated
- [ ] ADR written; doc 14 §3 updated; `docs/site` not regenerated
