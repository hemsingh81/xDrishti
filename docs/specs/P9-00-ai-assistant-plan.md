# P9-00 — AI assistant: the plan, the split, the safety line and the tool contract

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P9 — AI assistant and Hermes PoC (3 weeks, after P8). Row: "`xd-llm`, `xd-mcp`, assistant endpoint & chat; briefing; data Q&A (incl. portfolio & baskets); strategy drafting; eval set; Hermes PoC". Exit: "Eval set passes; Hermes adopt/reject decided".
- **Design refs:** docs/design/14-ai-assistant.md (all) · 03 §3, §6, §8 · 17 §2 to §4 · 19 §3, §4 · 04 §4.3 (suggestions inbox) · 13 (Assistant row) · ADR 0005 · docs/research/p0-15-llm-benchmark.md · P8-00 (D1 to D3)
- **Needs:** P8-05 (built: `Domain/Reporting` `MetricState`, `EvidenceBlock`, `ReportEvidence`), P8-06 (only P9-05's `get_report`), P8-01 (built: `@/shared/analytics`, bundle guard), P7 (built), ADR 0005 (still Proposed; container runtime taken as decided, Open questions)
- **Size:** this plan 0.5 agent-day; slices P9-01 2, P9-02 3, P9-03 3, P9-04 3, P9-05 3 = 14. E2's P9-06 to P9-10 add about 13. The roadmap's 3 weeks is optimistic (P4 to P8 overran).
- **Safety:** yes. A container with GPU device access, a database role and views a model can read, a streaming endpoint, stored prompts. No order, no write tool. Every switch defaults OFF and is an owner act.

## Goal
The owner asks about their own stored data in plain words and gets an answer in which every number was returned by a tool in that turn, shown beside the tool calls, rows and evidence behind it, from a model that runs on this Mac only, cannot change anything, and whose absence changes nothing else.

## Verified 2026-10-24
- **Nothing of P9 exists**: no `Assistant`/`Mcp`/`Llm` code in `backend/src` or `frontend/src`; `deploy/compose.yaml` has no `ai` service; `xd-up.sh` starts `--profile ops` only; the Dockerfile has targets `api`, `worker`, `feed`; nav `/suggestions` has `phase: 'P9'`.
- **No read-only role exists.** `deploy/db/init/10-roles.sh` creates `xd_app` only (DML on all tables by default privileges) and runs once per volume, so roles must come from a migration; `MigrationBuilderExtensions` has `xd_app` helpers only.
- **The Lab cannot run a text:** `POST /api/lab/validate` writes nothing, `POST /api/lab/strategies` stores a version, `POST /api/lab/runs` takes `StrategyId` + `Version`; each variant tightens the gates (`GET /api/lab/strategies/{id}/ledger`).
- **Suggestions inbox** = `workflow.suggestions` (insert-only, ADR 0092) read by `GET /api/plans/{id}/suggestions`, not doc 03's `/api/suggestions`.
- **Packages:** no `Microsoft.Extensions.AI`/`ModelContextProtocol` in `Directory.Packages.props`; `Microsoft.Extensions.*` pinned 10.0.12 in `Directory.Packages.props` but M.E.AI 10.11.0 needs `>= 10.0.13` (nuget.org, 2026-10-24); the Dockerfile already uses `aspnet:10.0.13-noble-chiseled`.
- **Frontend:** no streaming call exists; entry + modulepreload is 195.2 KB of 200 KB (ADR 0098). **Caddy:** `encode zstd gzip` site-wide; CSP `img-src 'self' data:; connect-src 'self'`.
- **Report types:** `MetricState` = `Value, Provisional, Insufficient, Unavailable, NotEvaluable, Empty`; `EvidenceBlock(N, FirstDate, LastDate, Basis, Provisional, RunKeys)`; `ReportEvidence(Population, Limits, UnavailableInputs, DuplicateTradesDropped, NetBasisConfirmation)`.

## Scope
**In:** slice split and order, cut line, honest limits, safety line, data classification, tool contract (schemas, limits, timeouts), read-only SQL design, `ai.*` tables, settings, the citation rule.
**Out:** E2's P9-06 to P9-10, event flags, embeddings, any write tool, voice, phone/PWA access to the assistant, push.

## Slices
| Slice | What | Days | Needs | Safety |
|---|---|---|---|---|
| **P9-01** | `xd-llm` compose service (profile `ai`, digest-pinned, GPU, API key secret), `scripts/xd-ai.sh`, model list, `IChatClient` (M.E.AI) over the OpenAI-compatible endpoint, `GET /api/assistant/llm-status`, package bump, measurements | 2 | ADR 0005 | yes |
| **P9-02** | `xd-mcp` host (ModelContextProtocol 2.3.0, HTTP, stateless, bearer), roles + `aiv` views, `ai.settings` + `ai.context_allows`, tool runner, tools `list_tables`, `describe_table`, `run_sql`, `lab_validate` | 3 | P9-01 | yes |
| **P9-03** | `POST /api/assistant/chat` (SSE), MCP client, turn loop, versioned system prompt, token budget, number-grounding check, `ai.*` audit tables, settings endpoints, `IAssistantTurnRunner` | 3 | P9-01, P9-02 | yes |
| **P9-04** | `/assistant` lazy screen: history, streaming answer, tool-call chips with rows, honest states, owner switches with password | 3 | P9-03 contract | no |
| **P9-05** | Data tools (below), golden answers on a seeded database | 3 | P9-02, P8-06 | yes |

Order: 01 -> 02 -> 03, then **04 (frontend) and 05 (backend) in parallel**. 01 and 02 both edit `compose.yaml`/`xd-up.sh`: never parallel. E2: P9-06 needs 03; P9-09 needs 05; P9-10 needs 02.
**Cut line.** Minimum useful assistant = 01 to 04 + `get_report`, `get_plan`, `calc`, `get_system_status` from 05. Cut first: `make_chart`, `search_notes`, `get_learning`, `get_basket`, `get_instrument`. P9-03 splits into 03a (tables, settings, status) and 03b (chat, loop, grounding) if day one shows it is over 3 days.

## Honest limits
1. **The model never decides a trade.** No tool returns a buy/sell instruction; "should I buy X" gets the stored evidence for X (or "not evaluable") and the sentence that engine and owner decide. System prompt, tools and a test say so.
2. **Numbers only from tools.** The server checks each number of the answer against that turn's tool results, the question and the tool arguments; unmatched numbers are marked and counted. This does not stop wrong prose around right numbers.
3. **A small local model errs.** Only smoke-tested (10/10 valid tool calls, P0-15); quality is P9-09's job (90 % exact on 30 questions). Until it passes the screen says "not yet evaluated".
4. **Prompt injection cannot be ruled out by prompt text** (OWASP LLM01:2025: no method fully prevents it, genai.owasp.org, 2026-10-24). Defence is structural: read-only tools, no egress, no rendered links/images, call and row budgets, visible tool calls. Worst case: a misleading answer, never a change.
5. **`run_sql` is outside the report engine**: no metric states, no evidence block; an answer resting only on it says "ad-hoc query, not checked by the report engine" and shows the SQL.
6. **Data limits are inherited** (about 2.1 years, replayed history, modelled fills, in-sample cells, members as of today, gross unless the charge sheet is the owner's: ISS-19, ISS-22, ISS-25); tools return them as `evidence.limits` and the prompt requires repeating them when ranking.
7. **One request at a time** (`-np 1`); load 22 to 105 s; about 26 s to first token at 8K in the container (P0-15).
8. **Off until the owner turns it on; the model server is started by the owner** (`scripts/xd-ai.sh start`). The API holds no container socket.

## Decisions (researched)
**D1. Tool server = separate `xd-mcp` container, Streamable HTTP, stateless, bearer token, `back` network only.** Tools must run under a role that cannot write, in a process with no write code; `xd-api` is `xd_app`. Hermes (P9-10) reuses it. Rejected: tools inside `xd-api`; stdio (cannot spawn across containers). Pin `ModelContextProtocol.AspNetCore` **2.3.0** (nuget.org "last updated 2026-10-20"; v2.0.0 2026-07-28 made `Stateless` default, v2.2.0 added `HttpServerSessionMode`; github.com/modelcontextprotocol/csharp-sdk/releases; docs csharp.sdk.modelcontextprotocol.io/v2/concepts/getting-started.html; read 2026-10-24). Client: `McpClient.CreateAsync` + `ListToolsAsync`; `McpClientTool` is an `AIFunction`.
**D2. LLM client = `Microsoft.Extensions.AI` 10.11.0 + `.OpenAI` 10.11.0** (nuget.org, released 2026-10-14; OpenAI package needs `OpenAI >= 2.14.0`, net10.0). `IChatClient.GetStreamingResponseAsync`, `UseFunctionInvocation()`; iteration/error limits on `FunctionInvokingChatClient` used if present (names UNVERIFIED; else the loop is bounded in our code). Rejected: Semantic Kernel, raw OpenAI SDK.
**D3. Runtime = `quay.io/ramalama/ramalama:0.25.0`, digest `sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e`.** quay.io 2026-10-24: 0.25.0 (2026-09-25) is still the newest numbered tag and equals `latest`. The image's llama.cpp build is not in the release notes (github.com/containers/ramalama/releases), so P9-01 records `llama-server --version` and checks the flags it uses against `--help`. Native Metal stays the fallback. Container Venus/Vulkan was 40 % slower than native on an M2 Max (ggml-org/llama.cpp discussion 12985); P0-15 measured 71 to 88 % here.
**D4. Models.** Default `main` Qwen3-30B-A3B Q4_K_M (ADR 0005; HF repo unchanged since 2025-05-21); alternate `gptoss` gpt-oss-20b MXFP4 (pinned commit `ef9b12f2ff56` stays valid though the repo head moved 2026-10-19). **New candidate `qwen-instruct`**: Qwen3-30B-A3B-Instruct-2507 (non-thinking only, 262,144 native context, BFCL-v3 65.1, Apache-2.0; huggingface.co/Qwen/Qwen3-30B-A3B-Instruct-2507, 2026-10-24): same architecture and speed, a later tool-use-oriented release. No `Qwen/` GGUF repo exists (most-downloaded conversion `unsloth/Qwen3-30B-A3B-Instruct-2507-GGUF`); 18.6 GB would pass the 60 GB disk cap, so it waits for the owner. No Qwen3.5/3.6 GGUF in the official list; Qwen3-Next-80B does not fit. The pick is the eval's (P9-09; ADR 0005 says "P9-07", now P9-09). Thinking off for chat via server flag `--chat-template-kwargs '{"enable_thinking":false}'` per model (`xd-ai.sh`); reasoning content is never shown or stored. Rejected: a small second model, a dense 14B.
**D5. No embeddings in P9-01 to P9-05.** `search_notes` = Postgres full text over a few thousand free-text rows; vectors add a server and a store for no measured gain. If later needed: nomic-embed-text-v1.5 Q8_0 (ADR 0005; needs `search_document:`/`search_query:` prefixes) vs bge-m3 vs Qwen3-Embedding-0.6B, decided by a 200-note recall test (public comparisons, premai.io/surrealdb.com 2026-10-24, do not cover financial notes).
**D6. `xd-llm` on `back` only, API key by secret.** No egress, no published port, local files only (`-m`, never `-hf`), `--api-key-file` (Keychain `xdrishti/llm/api-key`, created by `xd-up.sh` like the DB passwords). `/health` is open inside the network (503 while loading, 200 ready; llama.cpp server README 2026-10-24).
**D7. The owner starts the model server** (`scripts/xd-ai.sh start`); the screen shows `down` with the command. Starting from the app needs the container socket in `xd-api` (root-equivalent on the VM; "job start from UI = SAFETY"). Rejected: socket mount; always-on (host swaps 28 to 42 GB).
**D8. Streaming = SSE over `POST /api/assistant/chat`** (`TypedResults.ServerSentEvents`, ASP.NET Core 10, learn.microsoft.com 2026-10-24), read with `fetch`. Caddy flushes `text/event-stream` itself (caddyserver.com reverse_proxy docs); compression must be off for it: `encode @compressible zstd gzip` with `@compressible not path /api/assistant/chat` (tested by `CaddyfileTests`, `scripts/check-proxy.sh`). Rejected: SignalR (second contract and auth path), WebSocket.
**D9. No write tool in v1; `lab_run` becomes a proposal (P9-07).** Doc 14's "no write tools except `lab_run`" is tightened: P9-01 to P9-05 have none; `lab_validate` is a pure compile. P9-07's `lab_propose_run` stores a proposal and executes nothing; the owner starts the run in the Lab (quick method only, existing validation, one per hour, three per day, each in the test ledger). **A proposal made in chat is never run automatically.** The only automatic path is the dedicated drafting job on the owner's own typed idea, behind the setting `assistant.auto_lab_quick_test` (default OFF, owner act with a fresh password, recorded as auto with the settings snapshot; P9-07 sets limits). Every UI action that starts a job or changes research or book state needs a fresh password (`Elevate:false`, as `AutonomyEndpoints`) plus a capability gate (a setting, default OFF). Reason: runs cost compute and raise the Lab's multiple-testing bar. Design change: doc 14 §3, §4(3); ADR at P9-07 (next free ADR number at build time).
**D10. Read-only SQL = curated views + a separate login role; no raw base-table access.** `run_sql` exists but only over schema `aiv` (about 30 views, column-by-column projections omitting what must not reach the model) through role `xd_mcp_sql`, whose only privileges are `USAGE` on `aiv` and `SELECT` on its views (the boundary is the privilege, not a parser). Layers: (1) role settings `default_transaction_read_only=on`, `statement_timeout=5s`, `lock_timeout=1s`, `idle_in_transaction_session_timeout=10s`, `temp_file_limit=64MB`, `CONNECTION LIMIT 3`, `work_mem=16MB`, `search_path=aiv`, `public` schema revoked; (2) the query runs as `DECLARE c NO SCROLL CURSOR FOR <sql>; FETCH 201 FROM c` inside `BEGIN READ ONLY`, rolled back (server-side row cap; a cursor refuses `SELECT INTO` and data-modifying CTEs); (3) lexical pre-check (one statement, `SELECT`/`WITH`, no `;` but one trailing, no comments, 4,000 chars) for good messages, not safety; (4) views call `ai.context_allows('<class>')` so a switched-off class returns no rows from the database itself. Typed tools use role `xd_mcp_read` (`aiv` + an explicit list of report-source tables). Views are owner-executed (default `security_invoker=false`), so the roles need no base-table grants. Rejected: free `run_sql` as `xd_app` or a copy of it (reaches `accounts.*`, `auth.*`, secrets); a SQL-parser dependency (UNVERIFIED for .NET, weaker than a privilege); typed tools only (cannot answer "expectancy of 15m SELL setups in high-vol regimes in 2025").
**D11. Data classification.** *C0 never:* broker secrets/tokens/vault key/passwords/sessions/owner e-mail; broker client id, account number, demat/DP id, PAN, phone, address; `accounts.*`, `auth.*`, `platform.app_settings`, `portfolio.ledger_entries`, funds and margin. No view has them; an output scrubber (PAN pattern, 9+ digit runs, JWT-like, 32+ char base64) writes `[redacted]` and counts it, **on free-text cells only**: ids, hashes, run keys (64 hex) and amounts are marked structured in the manifest and never scrubbed (P9-02 AC 7). *C1 allowed:* instruments, bars, baskets, strategies, Lab results, simulated runs/trades, cells, models, lessons, hypotheses, experiments, plans, tickets, grades, suggestions, quality and service status. *C2 only while `portfolio_context` is ON (default OFF, owner act):* holdings, lots, value history, returns, imported/broker trades, journal fills; identity = account **label** (`name`), never client id; no balances in v1. *U untrusted free text* (instrument names from the broker master file, lesson/hypothesis/strategy-note/journal/suggestion text, basket names): control, zero-width and bidi characters stripped, 300 chars, `<`/`>` escaped, inside the envelope the prompt calls data, `injection_flags` counted for instruction-like patterns. Reason: the model is local with no egress, so C2 stays on the Mac; the exposure is the stored prompt copy and a wrong answer, hence default OFF instead of a ban. Rejected: masking amounts (kills P&L questions).
**D12. One envelope, JSON only, hard limits** (Contract) so grounding, UI and audit read the same fields.
**D13. No chat during the nightly pipeline.** The LLM's effect on api/worker was never measured (ADR 0005). Until P9-01 measures it, a turn answers 409 `assistant.nightly_running` while service `nightly` has a run in state running started under 60 minutes ago. One query; removable once measured.
**D14. Audit by `xd-api`, insert-only, full prompts and tool results.** `xd-api` sees every call, so audit does not depend on the tool server; `xd-mcp` logs name, argument hash, rows, ms only. No retention in v1 (about 20 KB/turn, 150 MB/year at 20 turns/day); revisit at 500 MB with a purge as `xd_owner`.
**D15. Model text renders as plain text** (paragraphs, lists, bold); never HTML, links, images or tables. Tables come from stored tool rows via the analytics kit. Removes the exfiltration-by-markdown channel even if the CSP loosens.

## Contract: tools (v1)
**Envelope (all tools):** `{ok, tool, asOf, rows|null, data|null, rowCount, totalRows|null, truncated, evidence:{basis, population[], dateRange, n, limits[], unavailable:[{input,reason}]}|null, notes[], error:{code,message}|null}`. Metrics use P8-06's `MetricView`: `{state: value|provisional|insufficient|unavailable|not_evaluable|empty, number, reason}` with `number` null unless `value`/`provisional`; groups carry their `n`.
**Limits:** rows default 50, max 200; cell 300 chars; text to the model cut to **6,000 chars** (rows dropped from the end, `truncated:true`, `totalRows` kept); stored envelope <= 64 KB; tool timeout 10 s (SQL 5 s); 6 calls per turn (setting 1..12); bad arguments -> `ok:false, error.code "tool.invalid_arguments"`. Names contain no order word (the no-order architecture test scans member names). Annotations: read-only, non-destructive, idempotent, closed-world (property names in 2.3.0 UNVERIFIED). All schemas `additionalProperties:false`.
| Tool (slice) | Arguments | Returns |
|---|---|---|
| `list_tables` (02) | `{prefix?: string[1..40]}` | `aiv` views: name, description, grain, classes, approxRows (<=100) |
| `describe_table` (02) | `{name: ^[a-z0-9_]{1,63}$}` | columns (name, type, unit, description, class), grain, limits; `table.unknown` |
| `run_sql` (02) | `{sql: string[1..4000], maxRows?: int[1..200]=50}` | columns, rows, rowCount, truncated, elapsedMs; basis "ad-hoc query, not checked by the report engine", `sqlHash`; `sql.rejected`, `sql.timeout`, `sql.denied` |
| `lab_validate` (02) | `{yaml: string[1..65536]}` | `{valid, hash, problems:[{line,column,code,message}]}`; stores nothing |
| `get_report` (05) | `{report: summary\|groups\|pivot\|what_works\|equity\|distribution\|insights, query?: object of up to 12 string pairs}`; `query` is exactly the query string of the matching `GET /api/reports/*` endpoint of P8-06 (`runs`, group/pivot dimensions, filters, window), validated by the same parser; **gross basis only in v1** (net needs the owner's confirmation read from `platform.app_settings`, which no role may read) | P8-06's view, metric states, evidence; its `reports.*` codes passed through |
| `list_runs` (05) | `{kind: sim\|lab, strategy?, limit?: 1..20=10}` | runKey, window, basis, universe, costed |
| `get_plan` (05) | `{date?: yyyy-MM-dd, accountLabel?}` | tickets: symbol, side, state, stored levels, `pWinLower`, expected R, reasons, grade; `empty` if none |
| `get_cell` (05) | `{key?, strategy?, timeframe?, side?}` | n, wins, posterior, windows all/250d/60d, state |
| `get_portfolio` (05) | `{groupBy: none\|bucket\|basket\|sector\|account, asOf?, includeClosed?=false}` | value, invested, unrealised, XIRR with states; `unavailable: portfolio context is off` |
| `get_basket` (05) | `{name: string[1..80]}` | members as of now, counts, last report row |
| `get_instrument` (05) | `{symbol: string[1..40], bars?: 0..60=20}` | master facts, tracked, coverage, last daily bars |
| `search_notes` (05) | `{query: string[2..200], from?, to?, limit?: 1..20=10}` | `{source,id,date,text(300)}`, `untrusted:true` |
| `get_learning` (05) | `{kind: lessons\|hypotheses\|changelog\|experiments\|models, status?, limit?: 1..20=10}` | stored rows, no state moves |
| `get_system_status` (05) | `{}` | newest bar date, last nightly outcome, services, quality gate |
| `calc` (05) | `{expression: string[1..200]}` over numbers, `+ - * / ( )`, `round abs min max` | decimal text + places; the only source of derived numbers |
| `make_chart` (05; runs inside `xd-api`, the only tool not on `xd-mcp`, because it needs this turn's stored results) | `{kind: bar\|line\|scatter, title<=80, x:{callIndex,column}, y:[{callIndex,column,label}]}` | spec **referencing columns of earlier results of this turn**; the model passes no numbers; `chart.bad_reference` |
Kept from doc 14: `list_tables`, `describe_table`, `run_sql`, `get_report`, `get_plan`, `get_cell`, `get_portfolio`, `get_basket`, `make_chart`, `lab_validate`. Dropped: `lab_run` (D9). Added: `list_runs`, `get_instrument`, `search_notes`, `get_learning`, `get_system_status`, `calc`.
**Token budget (16K context, prompt kept <= 8K):** system <= 900, tool schemas <= 2,200 (a unit test estimates and fails above), history <= 1,500 (last 6 messages; older tool results replaced by one line), this turn's results <= 3,000, answer <= 900 (`max_tokens`).

## Contract: `ai.*` (migrations `AddAiReadAccess` P9-02, `AddAiAudit` P9-03; insert-only for `xd_app`)
- `ai.settings` (02): `id bigint identity`, `enabled bool`, `portfolio_context bool`, `max_tool_calls int 1..12`, `changed_at` (DB clock), `changed_by text like 'owner:%'`, `reason <=500`, `snapshot_hash char(64)` (trigger). Newest row rules; **no row = disabled, no portfolio** (fail closed). Pattern of `workflow.autonomy_settings` (ADR 0091).
- `ai.prompts` (03): `hash char(64) PK`, `version`, `text`, `created_at`.
- `ai.conversations` (03): `id uuid PK`, `created_at`, `created_by`, `title <=120`, `settings_id FK`, `model_id`, `llm_build`.
- `ai.messages` (03): `id bigint identity`, `conversation_id FK`, `turn_id uuid`, `seq int` (unique per conversation), `role in ('user','assistant')`, `content`, `created_at`, `prompt_hash FK`, `model_id`, `prompt_tokens`, `completion_tokens`, `first_token_ms`, `total_ms`, `finish_reason in ('stop','length','tool_limit','cancelled','error')`, `grounding_status in ('grounded','ungrounded','no_numbers','not_checked')`, `ungrounded jsonb`.
- `ai.tool_calls` (03): `id bigint identity`, `conversation_id`, `turn_id`, `call_index`, `tool_name`, `arguments jsonb`, `status in ('ok','error','timeout','refused','disabled')`, `error_code`, `result jsonb` (<=64 KB), `result_bytes`, `rows`, `truncated`, `evidence jsonb`, `redactions`, `injection_flags`, `started_at`, `duration_ms`.
Grants: `xd_app` SELECT+INSERT only (`MakeAppendOnlyForAppRole`); `xd_mcp_read` SELECT on `ai.settings`; `xd_mcp_sql` none; tested as `xd_app` like `PlanGrantsTests`.

## The citation rule ("an answer cites its tool calls")
1. Each assistant message is stored with its tool calls (`turn_id`); the screen shows them under the answer, always visible: chip `#n tool(arguments) rows · as of · state`, expanding to arguments, stored rows (`DataTable`) and evidence (`EvidenceBlock`). Chips are drawn from stored rows, not from model text.
2. The model writes `[n]` after a figure; the server checks figures, not markers. An unmatched figure is marked "not from a tool" and the message is `ungrounded`; numbers with no tool call are always `ungrounded`.
3. A non-value state is repeated by its word (insufficient, unavailable, not evaluable, no records yet), never as a number.
4. A `run_sql`-only answer carries limit 5's label and the SQL.

## Acceptance criteria (of the phase; slices carry their own)
1. Given the assistant is off (default), when the chat endpoint is called then 409 `assistant.disabled` and no `xd-mcp`/`xd-llm` call; with it on and the model down the screen says `down` and how to start it; nightly, API and worker behave as before in both (test: nightly with `xd-llm` unreachable).
2. No tool writes: the `xd-mcp` assembly references no command handler or write store (architecture test); both roles fail INSERT/UPDATE/DELETE/DDL at the database; every listed tool is annotated read-only.
3. No C0 data in any tool result: a seeded broker secret, client id, owner e-mail and PAN-like string are asked for by every tool and by `run_sql` (`accounts.broker_secrets` -> `sql.denied`; no view column; the scrubber catches a PAN in a note).
4. The grounding check marks an invented number in 10 of 10 seeded cases and leaves 30 of 30 grounded answers unmarked.
5. An injected instruction in an instrument name or note cannot cause a write, egress or more than the call budget (probe in P9-03 and P9-09).
6. Every turn is reproducible from `ai.*` (prompt hash, model, settings id, messages, calls with arguments and results).
7. Initial JavaScript within the guard; one lazy route; `scripts/gen-api.sh` per API change; `scripts/check.sh` green per slice.

## Non-functional
Container latency (P0-15): first token <= 15 s at 4K, >= 10 tokens/s; turn timeout 180 s; `get_*` p95 < 1 s on the seeded fixture; `run_sql` < 5 s. `xd-llm` memory limit measured in P9-01 (start 6 GB, UNVERIFIED), `xd-mcp` 512 MB. `[LoggerMessage]` ids **6000 to 6199** (01: 6000-6019, 02: 6020-6059, 03: 6060-6119, 05: 6120-6159, E2: 6160-6199; the highest id in use on 2026-10-25 is 4801); never a prompt, answer, argument or result in a log line. Seq events `assistant.turn` (ids, tokens, ms, calls, grounding) and `mcp.call` (tool, argument hash, rows, ms).

## Test plan (of the phase)
Pure: grounding and number normalisation, SQL pre-check, scrubber and untrusted-text cleaner, `calc`, budget, settings rules. Integration (Testcontainers, real roles): grants of both roles; every view column classified (a manifest test; a new base column is not exposed); `ai.context_allows` switching rows off in the database; SQL timeouts and row cap; insert-only `ai.*`. Contract: in-process MCP server + client round trip (tool list, schemas, annotations); a scripted fake `IChatClient` (loop, budget, cancellation); SSE framing on real Kestrel. Frontend (MSW): every state, a stream with tools, the mark on an ungrounded number, abort. Architecture: `XDrishti.Mcp` references no write code; nightly code references no assistant code. Golden answers reuse P8-06's seeded two-run, three-basket database.

## Risks and honest limits (plan)
1. **Fluent prose trusted** (doc 19 §3): citation rule, ungrounded mark, "not yet evaluated" banner.
2. **Injection** via third-party instrument names and stored text: D11/D15, no egress, no writes, probes.
3. **GPU device vs hardening:** `/dev/dri` with `read_only`, `cap_drop: [ALL]`, non-root is untested (UNVERIFIED); fallback = documented exception (root in container, no capabilities, no egress, only the API key). P9-01 decides and updates doc 17.
4. **Host swap** (28 to 42 GB): on-demand start, D13, `gptoss` as the smaller model, `xd-ai.sh status` prints swap.
5. **Third-party GGUF** (`qwen-instruct`) has no vendor checksum: commit + SHA-256 recorded at first download, provenance stated.
6. **MCP SDK churn** (1.0 on 2026-03-05, 2.0 on 2026-07-28, 2.3 on 2026-10-20): exact pin, one adapter class, a contract test of tools and schemas.
7. **Schedule** (see Size). 8. **Stored prompts hold financial data** in the same database and backup; C2 switch is the control; no export of `ai.*` exists.

## Contradictions found
Doc 03 §8/doc 17 §2 "read-only role" vs none existing (P9-02) · doc 14 `lab_run(yaml)` vs the Lab API (D9) · ADR 0005 "P9-07 eval" vs P9-09 (P9-01 amends) · doc 03 §6 `/api/suggestions` vs `/api/plans/{id}/suggestions` (E2 P9-08) · doc 14 "nomic or bge-m3" vs ADR 0005 (D5) · `Microsoft.Extensions.*` 10.0.12 vs 10.0.13 (P9-01).

## Open questions (pure owner acts; default until the owner acts)
1. **Turn the assistant on** (`ai.settings.enabled`, fresh password; turning it off needs none). Default OFF.
2. **Turn the portfolio context on** (fresh password; off needs none). Default OFF; tools answer `unavailable: portfolio context is off`.
3. **Start the model server** (`scripts/xd-ai.sh start`). Default: not running.
4. **Download `qwen-instruct`** (18.6 GB, over the 60 GB cap) or free space by `podman volume rm xd-models` (duplicate 18.6 GB). Default: neither; the eval compares `main` and `gptoss`.
5. **ADR 0005 is "Proposed"**: confirm the container runtime. Default: container.

## Done when
- [ ] The five slice specs are Approved by the owner and match this contract (names, limits, tables, settings)
- [ ] Design docs updated at build time: doc 14 §3/§4, doc 17 §2 to §4, doc 03 §6; one ADR (next free ADR number at build time; 0102 on 2026-10-25); then `scripts/build-docs.sh`
- [ ] The contradictions above are fixed in the docs by the slices that touch them

## Review fixes 2026-10-25
1. D9: a `lab_propose_run` proposal made in chat is never auto-run; the auto path is only the drafting job on the owner's typed idea; UI writes that start jobs or change state need a fresh password (`Elevate:false`) plus a capability gate.
2. D11: the scrubber applies to free text only; run keys, hashes and amounts are structured and untouched.
3. Settings: turning the assistant or portfolio context OFF needs no password; ON needs a fresh one; actor format `owner:<email>`.
4. Facts: highest log id 4801; ADR 0102 is next, "next free at build time"; `Microsoft.Extensions.*` 10.0.12 vs Dockerfile 10.0.13 stated.
5. `aiv` views must not be writable by `xd_app` (P9-02 AC 13a); C2 views are `security_barrier`; `ai.settings` hash covers every column.
