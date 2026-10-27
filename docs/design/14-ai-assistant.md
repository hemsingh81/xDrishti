# 14 — AI Assistant (local LLM)

## 1. Roles

| Job | Who | Why |
|-----|-----|-----|
| Signals, stops, targets, sizes | Deterministic .NET engine | Exact, testable |
| P(win), expected R | Gradient-boosted trees in ML.NET (trained & scored in .NET) | Measurable accuracy |
| Exit choice, ranking, selection | Deterministic rules on learned statistics | Auditable |
| Daily briefing, weekly review | **LLM** | Language summarisation |
| Questions about your data | **LLM + read-only tools** | Numbers come from tools |
| Draft strategy rules for the Strategy Lab | **LLM** (you confirm; tests decide) | Faster research |
| Propose hypotheses (one variable) | **LLM** (engine validates) | Research help |
| Suggest instruments / basket changes / next-day focus | **LLM + engine evidence** → suggestions inbox; **you accept or reject** | Human in the loop |
| Extract data from filings/announcements (later) | **LLM** (validated) | Unstructured text |
| Predict prices / make trade decisions | **Never** | Unverifiable |

## 2. Setup

| Component | Choice |
|-----------|--------|
| Model server | `xd-llm`: llama.cpp server (image `quay.io/ramalama/ramalama`, pinned) on the **libkrun** Podman machine with GPU (Vulkan), OpenAI-compatible API; started on demand ([ADR 0005](../adr/0005-local-llm-runtime-and-models.md)) |
| Main model | **Qwen3-30B-A3B Q4_K_M** (MoE, ≈ 18.6 GB; measured 41 tok/s at 8K context in the container); alternate **gpt-oss-20b** (12 GB) — final pick by the eval set (§5). Context 16K default, 32K supported; keep prompts ≤ 8K tokens |
| Small model | none — the MoE main model is faster than a dense 8B; a dense 14B was rejected (first token 27 s) |
| Embeddings | nomic-embed-text or bge-m3 (for search over notes/docs) |
| Fallback | Host-native llama.cpp (Metal) — measured 1.1–1.4× faster generation and 1.4–1.8× faster prompt reading than the container, loads in 5–10 s; endpoint change only. The container meets the speed thresholds, so it stays the default |

Model choice is config; select by our eval set, not leaderboards.

## 3. Integration (.NET)

- `xd-api` assistant endpoint uses **Microsoft.Extensions.AI** with tool calling; streams to React.
- Tools are served by **`xd-mcp`** (ModelContextProtocol C# SDK 2.3.0, Streamable HTTP, stateless, a bearer token per client,
  [ADR 0137](../adr/0137-the-assistants-tools-run-in-a-separate-read-only-server-under-two-database-roles-that-can-select-only-from-reviewed-views-and-the-switch-lives-in-the-database.md)) under **two read-only DB roles** (`xd_mcp_sql` for `run_sql`, `xd_mcp_read` for the typed tools),
  each able to `SELECT` only from the reviewed views of schema `aiv` (36 views; the portfolio ones are empty until the owner turns the
  portfolio context on, in the database itself). **Built (P9-02):** `list_tables`, `describe_table`, `run_sql(sql, maxRows)` over
  `aiv` (one SELECT, 200 rows, 5 s, labelled "ad-hoc query, not checked by the report engine"), `lab_validate(yaml)` (stores nothing).
  **Built (P9-05, [ADR 0139](../adr/0139-the-ask-your-data-tools-reuse-the-report-engine-and-read-only-views-the-read-role-gets-column-grants-on-four-report-source-tables-and-calc-and-charts-take-no-number-from-the-model.md)):**
  `get_report(report, query)` (the report engine's own handlers, gross only, each figure a state), `list_runs(kind)`, `get_plan(date)`,
  `get_cell(key | strategy, timeframe, side)`, `get_portfolio(groupBy)` (empty until the portfolio context is on), `get_basket(name)`,
  `get_instrument(symbol, bars)`, `search_notes(query)`, `get_learning(kind)`, `get_system_status()`, `calc(expression)` (exact decimals: the only
  source of a derived number) and, inside the API, `make_chart(spec)` (columns of earlier results of the turn, never numbers). Sixteen tools in all, under
  2,200 tokens of schema. `lab_run` is dropped (P9-00 D9): a run is proposed, never started by the model.
- **The endpoint (P9-03, [ADR 0138](../adr/0138-the-assistant-endpoint-runs-the-tool-loop-in-the-application-streams-events-marks-figures-not-found-in-the-tool-results-and-stores-every-turn-insert-only.md)):**
  `POST /api/assistant/chat` streams `turn`, `status`, `tool_call`, `tool_result`, `token`, `iteration_discard`, `ping`, `done`, `error`. The loop
  (up to the owner's budget of 1 to 12 tool calls, default 6; history of the last 6 messages with one line per old call, never old results; 10,000
  characters of results per turn; 900 answer tokens; 180 s) is the application's, over a model port and the tool-server port. **Off by default**
  (no `ai.settings` row = `409 assistant.disabled`, nothing called); turning it on or raising a setting is the owner's act with a fresh password in
  the body, switching off needs none. **Grounding:** every figure of the answer is looked up in the tool results of that turn (and the question and
  the tool arguments); unmatched figures are marked, not removed, and the message is `ungrounded` (a figure computed by the model itself is
  unmatched: derived numbers come from `calc`). **Audit:** `ai.prompts`, `ai.conversations`, `ai.messages`, `ai.tool_calls` and `ai.model_calls`
  are insert-only for the application role; each turn can be replayed from them. One turn at a time; no chat while the nightly run is running.
- Guardrails: numbers only from tool results; no write tools except `lab_run`; no promotion; no config
  changes (only proposed diffs you apply in the Config screen); prompts and tool calls logged in `ai.*`;
  broker secrets never in context. If the LLM is down, the nightly plan is unaffected.

## 4. Features

1. **Daily briefing** (≤ 300 words): regime, the tickets with one-line rationale, risks, system changes. Built in P9-06 ([ADR 0140](../adr/0140-the-briefing-text-is-phrased-by-the-model-from-app-built-facts-and-a-deterministic-guard-rejects-any-figure-symbol-or-claim-it-cannot-verify-the-fixed-format-text-is-the-fallback.md)): the app builds the facts, the model only phrases, a deterministic guard rejects any number, symbol or claim not in the facts, and a fixed-format text is stored instead; the weekly review works the same way. Both services start paused and the model is used only when the assistant and `Assistant:Briefing:UseModel` are both on.
2. **Ask your data**: "Expectancy of 15m SELL setups in high-vol regimes in 2025?" → query + numbers + chart.
   Also portfolio and baskets: "XIRR of MyLongTerm vs Nifty this year?", "Which basket worked best for 15m setups?"
3. **Strategy drafting**: English idea → DSL YAML → validation → you confirm → Lab run → summary.
4. **Hypothesis proposals** for the learning loop ([doc 09](09-learning-engine.md) §5).
5. **Event flags** (later): classify announcements for the event-risk filter.

## 5. Eval set (re-run on every model/prompt change)

| Task | Test | Pass |
|------|------|------|
| Data questions | 30 questions with known answers | ≥ 90% exact numbers |
| Briefing | 10 historical plans | No numbers absent from input; all tickets covered (the guard of P9-06 is the checker; set B measures how many first attempts it accepts) |
| Strategy drafting | 10 ideas | Valid DSL; matches intent on review |
| Event flags | 200 labelled announcements | Precision on "material" ≥ 80% |

## 6. Hermes Agent (Nous Research) — decision

**What it is:** open-source (MIT) always-on agent: persistent memory, self-written skills, built-in cron,
MCP client, messaging gateways (Telegram, WhatsApp…), works with local OpenAI-compatible models.

**Decision:** not part of the core. Build the built-in assistant first. Evaluate Hermes in Phase 9 as an
**optional front-end** (phone access, scheduled summaries) connected only to `xd-llm` and `xd-mcp`.

**Why cautious:** a third-party review (Sept 2026) cites 44 published CVEs including high-severity
remote-code-execution and supply-chain issues, and an audit found unrestricted shell and credential-file
access in default config; self-written skills/memory can persist errors or injected instructions; it needs
≥ 32–64K context.

**If adopted, mandatory hardening:** own image from a pinned release (no `curl | bash`); shell, browser,
web-search, file-system and media tools disabled; only `xd-mcp` registered; no secrets mounted; egress only
to `xd-llm`/`xd-mcp`; skills git-tracked and memory reviewed weekly; messaging off by default; monthly CVE
check.

**PoC pass criteria:** hardened container works with the local model · ≥ 90% on the data-question eval ·
8/10 strategy drafts valid · cron summary delivered 5 days running · prompt-injection probe (malicious journal
note) causes no unintended tool calls · runs alongside the core stack without memory pressure · clear benefit
over the built-in assistant.
