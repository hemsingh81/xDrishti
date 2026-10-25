# P8-22 — Audit log screen: a read-only view over the application's insert-only record of who did what

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P8 — slice P8-22 of [P8-00](P8-00-reports-and-ui-plan.md) ("Audit log screen (optional)"; wanted before any write slice P8-18 to P8-21 ships). Doc 13 §3: "Audit: logins, configuration changes, approvals, service actions; filterable log".
- **Design refs:** docs/design/13 §3 · 04 §3-§5 · 03 §5 (`ops.audit_log` named) · 19 §4 · ADR 0083, 0091 (autonomy rows), 0073 (learning changelog), 0098 (honest-state kit). New ADR: **next free ADR number at build time**.
- **Needs:** P8-01 (`DataTable`, `EvidenceBlock`, honest-state words, bundle guard). `useUrlState` is built (P8-02, `shared/analytics/useUrlState.ts`) and is reused; P8-06 (report endpoints) and P8-15 (plan notices, `GET /api/plans/notices`) are built and untouched by this slice. Optional later source: P7-20's `trading.ticket_notice_acks`.
- **Size:** 3 agent-days (estimate).
- **Safety:** no. `GET` only, no export, nothing leaves the app. It does show sign-in IP addresses and user agents to the signed-in owner; criterion 6 and the field policy are the control. Reading writes nothing.

## Goal
The owner opens **Audit log** and sees what happened, when, and who or what did it, across sign-ins, security changes, configuration, service actions, plan decisions, autonomy and learning changes, with filters and pagination, no secret in any row, and a plain statement when one record could not be read.

## Verified facts (code opened 2026-10-24)
- **`ops.audit_log` of docs 03/04 does not exist** (no entity, table or migration). The audit is six insert-only stores:
  1. `auth.audit_events` (`AuditEvent`): uuid v7 `id`, `at`, `event` (13 `AuditEventType` names), `email` as typed (<= 256), `ip`, `user_agent`, `detail` (<= 512); index on `at`. Read today only by `GET /api/auth/audit?take=` (max 200) for the Profile page.
  2. `accounts.account_events` (`AccountEvent`): uuid v7 `id`, `at`, nullable `account_id`, `event` (73 `AccountEventType` names today: account, broker, risk profile, basket, calendar, corporate action, CSV undo, **service** schedule/pause/resume/run, price alert, Action center, trade/lot/dividend/cost import and void), `detail` (<= 512, secret-free by rule), `actor` (e-mail or `system:*`); index `(account_id, at)`. It is the general audit of configuration and service actions.
  3. `workflow.approvals` (`kind` owner|auto, `decision`, `actor` `owner:<email>` or `rule:autonomy`, `reason`, `confirm_reason`, `modifications` JSON <= 1000, `cutoff`, `decided_at`, settings id/hash, policy version, plan and item ids).
  4. `trading.plan_events` (plan/ticket state moves: `from_state`, `to_state`, `actor`, `reason`, `approval_id`, `created_at`).
  5. `workflow.autonomy_settings` (switch values, `snapshot_hash`, `actor owner:%`, `reason`, `changed_at`).
  6. `research.learning_changelog` (`kind` transition|designation|revert, `subject_kind`, `subject`, `from_state`, `to_state`, `reason`, `actor`, `evidence_ids`, `before_ref`, `after_ref`, `created_at`).
  All are append-only for `xd_app`. Not audit: `platform.service_runs`, `trading.plan_runs`, `portfolio.price_alerts`, `workflow.suggestions`.
- Frontend: nav entry `/audit` ("Audit log", `phase: 'P8'`) shows the "coming in phase" page; `Api/Endpoints` has no audit read besides `GET /api/auth/audit` (P8-00 had this UNVERIFIED; now verified). `LikePattern.Escape` exists for safe ILIKE.

## Scope
**In:** Domain `Audit` (pure: sources, categories, outcomes, actor kinds, `AuditEntry`, `AuditRedactor`, `AuditCursor`, merge/paging, kind-to-category table); Application `Audit` (port `IAuditSource` per store, three handlers); Infrastructure readers; four GET endpoints; migration `AddAuditReadIndexes` (indexes only); the `/audit` screen (`frontend/src/features/audit`); tests; ADR; docs.
**Out:** export, download, copy-all; any write or delete; a unified `ops.audit_log` table; changing how any module writes audit; `GET /api/auth/audit` (unchanged); service-run and plan-run history; alerting on audit events; configuration diffs; versioned-config activation audit (ADR 0002, not built).

## Acceptance criteria
1. Given rows in all six stores, when `GET /api/audit/entries` is called without filters, then the newest `limit` (default 50, 1-200) rows of the last 30 days come back merged, ordered by `at` desc, source ordinal desc, id desc; rows with equal `at` keep that order on every call.
2. Given more rows than `limit`, when `nextCursor` is followed to the end, then every row appears exactly once, also when many share one `at` (property test: random timestamps with ties, 1-6 sources, limits 1-200); a tampered or foreign cursor is `422 audit.invalid`.
3. Given `from`/`to` (India dates, inclusive; default today-29 to today; span over 366 days refused), `source`, `category`, `kind`, `outcome`, `actor` (owner|system|rule|cli|other), `account` and `q` (3-64 characters, case-insensitive, matched **only against the redacted summary, the kind and the displayed actor, never against raw stored text**), then each narrows exactly as a hand-filtered fixture and several combine with AND; an unknown value is `422 audit.invalid` naming the field; `q` is never logged. Given a planted `password=hunter2` in a `detail` and `q=password=hun` or `q=hunter`, then the row is **not** found (the search cannot reveal what the redactor hides), while `q=redacted` finds it; the test runs the same probe for each secret shape of criterion 5. Because the text is redacted in the application, `q` is applied there: each source is read in chunks of 500 rows after the cursor, redacted, filtered, until `limit + 1` matches or `MaxScan` (5,000 rows per source per request) is reached; when the cap is hit `limits[]` says "search covered the newest N rows of a source; narrow the date range".
4. Given a source that cannot be read, then the response is still 200 with the others and `sources[]` marks it `unavailable` with a fixed reason code (`empty` when it has no rows in the window); the screen says "N of 6 records could not be read" and never presents the page as complete.
5. Given a planted secret (JWT-shaped string, 40-hex or 64-character token, `password=hunter2`, `token: abc123`, `Bearer eyJ...`, `client_id=1100000001`) in `detail`, `reason`, `confirm_reason` or `email`, then every response shows `[redacted]` with `redacted: true` and contains the secret nowhere, nor do the logs or problem details; ordinary text ("5 holdings imported") is unchanged.
6. Given a failed sign-in whose `email` is not the owner's address, then the row shows `actorKind = other_address` and no address. The **list** never contains `ip`, `userAgent`, `confirm_reason`, raw `modifications` or hashes; only the **detail** of an `auth` row adds `ip` and `userAgent` (redacted like any text); a test scans every response field name against the forbidden list in "Field policy".
7. Given `GET /api/audit/entries/{id}` for `auth|account:<uuid>` or `approval|plan|autonomy|learning:<n>`, then the entry and server-built labelled `fields` come back; an unknown prefix, malformed key or missing row is `404 audit.not_found`, never 500.
8. Given `GET /api/audit/summary`, then counts per source, category and outcome of the window return with the same `sources[]` states; the screen prints "Showing N of M".
9. Given every value of `AuditEventType` and `AccountEventType`, then each has exactly one category and an outcome in a table, and a test fails when a value is added without a row (so a later writer slice cannot ship an unclassified kind). `failed`: LoginFailed, StepUpFailed, BrokerConnectFailed; `refused`: AccountLocked, LoginRefusedLocked; every other kind and every approval decision (including reject) is `ok`, meaning "recorded", not "verified".
10. Given any request, no INSERT, UPDATE or DELETE is issued (statement-recording test), no audit row is written for the read, a session is required (401), `Cache-Control: no-store` is sent, grants are unchanged.
11. Given 100,000 rows in `account_events` and 20,000 in each other store, then the first page of a 30-day window answers in p95 under 300 ms and `summary` under 500 ms on the test container (measured, written in the ADR); every source query is bounded by the window and `limit + 1`.
12. Screen: loading, empty ("No entries in this range"), error with Retry, the partial-sources note; table >= 768 px, cards below; detail drawer; filter bar (presets 24 h, 7 d, 30 d, 90 d, custom; source chips; category, outcome, actor, account, kind, text); "Load more"; filters in the URL (a link reproduces a view, Back works); source and outcome as word plus icon, never colour alone; server text rendered as text (test with markup); no export or copy-all control; India time via the shared formatters; a failed sign-in or refused step-up of the last 24 hours is on the first screen of an unfiltered view.
13. `/audit` is a lazy route without the "coming in phase" flag; eager bundle growth <= 0.3 KB gzip; the route chunk budget is measured at build and written in the ADR (UNVERIFIED baseline; measured + 10 %); `npm run size` stays green.

## Contract
- **API (session, GET only, `no-store`, tag `Audit`):**
  - `GET /api/audit/entries?from=&to=&source=&category=&kind=&outcome=&actor=&account=&q=&limit=&cursor=` -> `{ window:{from,to}, items: AuditEntryView[], nextCursor: string|null, sources: [{source, state: "value"|"empty"|"unavailable", reason: string|null}], limits: string[] }` (list parameters take comma lists).
  - `AuditEntryView { id ("auth:<uuid>", "account:<uuid>", "approval:<n>", "plan:<n>", "autonomy:<n>", "learning:<n>"), at, source, category, kind, outcome ("ok"|"failed"|"refused"), actorKind ("owner"|"system"|"rule"|"cli"|"other"|"other_address"), actor?, accountId?, accountName?, summary (<= 300, redacted), redacted, planId?, itemId? }`.
  - `GET /api/audit/entries/{id}` -> view + `fields:[{label,value}]` (plain text; auth adds IP address and User agent; approvals add Cutoff, Confirm reason, a one-sentence Modification built from the parsed JSON, Policy version; autonomy the four switch values; learning Subject, From -> To, Before, After, Evidence ids).
  - `GET /api/audit/summary?from=&to=` -> `{ window, total, bySource, byCategory, byOutcome, sources }`; `GET /api/audit/kinds` -> kinds per source with category (from the enums; drives the filter list).
  - Errors `422 audit.invalid`, `404 audit.not_found`, 401. `ApiJsonContext`; `scripts/gen-api.sh`.
- **Data:** no new table. Migration `AddAuditReadIndexes` adds only missing indexes (check migrations first): `ix_account_events_at (at DESC)`, and `decided_at` on `workflow.approvals`, `created_at` on `trading.plan_events` and `research.learning_changelog` if absent; `Down` drops them; no grant change.
- **Category table (Domain, exhaustive test):** `sign_in` (Login*, AccountLocked, Logout, SessionExpired*), `security` (StepUp*, PasswordChanged, OwnerProvisioned, OwnerPasswordReset, ProfileUpdated, SecretsReplaced, RolesChanged, PrimaryDataChanged), `broker` (ConnectionUpdated, Broker*), `service` (Service*, and `DepthRecorderSwitched` once P7-16 is built), `alert` (PriceAlert*, Action*), `configuration` (AccountCreated/Archived, Trading/Portfolio/DataUpdated, RiskProfile*, Bucket*, refresh and benchmark settings, FirstImportDateChanged), `market_data` (Basket*, Instruments*, Calendar*, CorporateAction*, CsvLoadUndone), `portfolio_data` (Holdings*, LotDateChanged, Trade*, Lots*, Dividend*, Costs*, SecurityNamesLearned, BrokerHistorySynced), plus `plan_decision`, `plan_state`, `autonomy`, `learning` for the other four stores.
- **UI:** route `/audit`, `useInfiniteQuery` on `['audit','entries',params]`, `DataTable` and honest-state words from `@/shared/analytics`, the page frame of Services/Quality; phone: cards, filters in a drawer.
- **Logs:** event ids after the highest in use (4801 on 2026-10-25; `LogEventIdTests` decides at build time); `audit.read` (source states, row count, filters as booleans; never `q` or a value), `audit.source_failed` (source, exception type).

## Non-functional
Budgets per criteria 11 and 13. Security: owner session only; readers `SELECT` named columns, never `*`, and never touch `auth.sessions`, `auth.owner_accounts`, the vault or broker secret tables; redaction is a second line behind the stores' own no-secrets rule; no HTML is built from server text.

## Field policy
| List may show | Detail only | Never selected, returned or logged |
|---|---|---|
| id, at, source, category, kind, outcome, actorKind, actor (owner e-mail, `system:*`, `rule:*`, `cli:*`), accountId, accountName, redacted summary, planId, itemId | `ip`, `userAgent` (auth rows), confirm reason, modification sentence, cutoff, policy version, autonomy values, changelog subject/before/after/evidence ids | password or hash, session id or token, CSRF token, access/refresh token, vault key, client id, broker secret values, `settings_hash`, `snapshot_hash`, raw `modifications`, a non-owner e-mail typed at sign-in, `q` |

## Test plan
Unit (Domain): redactor table (each secret shape, near misses that must survive, idempotent), cursor round trip and tamper, merge/paging property, category/outcome exhaustiveness over both enums, actor-kind table, window validation. Application (fakes): filter combinations, one or all sources failing, limit clamp. Integration (Testcontainers): fixture across all six stores incl. equal timestamps, each filter, `q` with `%` and `_`, the oracle probes of criterion 3 and the scan cap, paging, detail per prefix, 401/422 matrix, no-write recording, forbidden-field scan, planted secrets inserted through the real writers, the volumes of criterion 11, grants unchanged. Frontend (MSW, `pinClock`): states, partial note, URL filters and Back, Load more, drawer, phone layout, markup as text, no export control. Architecture: readers in Infrastructure only; Domain/Audit has no I/O (add it to the Domain namespace list); every audit endpoint is a GET.

## Decisions (researched)
1. **A read model over the existing stores, not a new `ops.audit_log`.** The six stores are insert-only, owner-gated and in use; a new table needs every writer changed and duplicates rows. Rejected: building it as in doc 03; reading only `auth.audit_events` (what `GET /api/auth/audit` does; misses configuration, service, plan and learning actions, the ones the write slices add). **Design change:** doc 03 §5, doc 04 §5, doc 13 §3; ADR at build time.
2. **Which stores:** actor-stamped append-only records of a decision or security event. Excluded: machine telemetry (`service_runs`, `plan_runs`), `price_alerts` (their create/trigger is already `PriceAlert*` in `account_events`), suggestion inbox state. A later writer registers an `IAuditSource` and a kind table (P7-20's acks first).
3. **Merge by key in memory, not SQL `UNION ALL`.** Each store is read with its own index, the window and `limit + 1` rows after the cursor, merged by `(at, source ordinal, id)` (cursor carries the triple). Sources fail independently (criterion 4) and cost is bounded by `sources x (limit + 1)`. Rejected: one UNION (a failing store fails the page), offset paging (skips or repeats as rows arrive).
4. **Keyset pagination, 50 default, 200 max** (the ceiling of `GetAuditQuery.MaxTake`), 366-day window, default 30 days, "Load more" button.
5. **Retention: keep everything; no delete exists or is added.** About 20 to 60 rows a day over all stores, so under 25,000 rows and about 6 MB a year (estimate, UNVERIFIED until measured). The 30-day default is a view limit, said on screen. Revisit if a store passes 5 million rows.
6. **Pattern redaction as a second line.** Docs and code forbid secrets in audit text; the screen adds a deterministic redactor so a writer's mistake or a secret the owner typed into a reason cannot reach a screen or screenshot. It cannot find every shape (stated on screen). Rejected: trusting writers alone; hiding matching rows (it would hide the event that needs review).
7. **A failed sign-in's e-mail is shown only if it is the owner's.** People type passwords into the user field; anything else shows as `other_address`.
8. **IP and user agent only in a sign-in's detail**, to recognise a foreign sign-in (doc 19 §4) without putting personal data in every list row; no export, so nothing leaves in bulk.
9. **Reading the log is not audited:** an `AuditViewed` row per GET would break the read-only rule of P8-00 D6 and add nothing the Seq access log lacks.
10. **Outcome is derived from the kind;** no store records success or failure and a new column is out of scope. `ok` means recorded.
11. **Why before the write slices:** P8-18 to P8-21 and P12 add real-effect actions; each must add its audit kind and category in the same change or the exhaustiveness test fails.

## Risks and honest limits
- **Only what is recorded is shown.** Direct database changes as `xd_owner`, file changes, reads and anything older than the audit tables are absent; this sentence is in `limits[]` of every response.
- Free text is the writer's: `account_events.detail` and the owner's typed reasons show after pattern redaction; an unusual secret shape can slip through.
- `actor` is as stored: `system:*` and `rule:autonomy` are labelled and never shown as the owner, so automatic approvals stay distinguishable (ADR 0091).
- Account names are read at display time (a renamed account shows its current name). Keyset paging is stable, not a snapshot: a new event appears at the top of a fresh load, not in pages already fetched.
- Not yet seen in a browser; the 360 px to 4K check is P8-16's pass.

## Open questions
None. (The owner's Approved is the owner's act; until then the placeholder stays.)

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any secret path into a response and any write on a GET)
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddAuditReadIndexes` with `Down`
- [ ] ADR written; docs 03 §5, 04 §5, 13 §3 and the roadmap P8-22 row updated; `scripts/build-docs.sh` run

## Review fixes 2026-10-25
- `q` no longer matches raw stored text in SQL (that was an oracle that could reveal redacted secrets): it matches the redacted summary, kind and displayed actor in the application, with a scan cap and a test per secret shape (criterion 3, test plan).
- Facts: `AccountEventType` has 73 values (not 66); `useUrlState` exists (P8-02 built); P8-06 and P8-15 are built and not touched; the highest log id is 4801; the next ADR (0102 today) stays "next free at build time".
