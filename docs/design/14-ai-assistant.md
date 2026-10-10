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
- Tools are served by **`xd-mcp`** (ModelContextProtocol C# SDK) over a **read-only DB role**:
  `list_tables`, `describe_table`, `run_sql(read_only, row limit)`, `get_report(type, filters)`,
  `get_plan(date, account)`, `get_cell(key)`, `get_portfolio(group_by, as_of)`, `get_basket(name)`,
  `make_chart(spec)`, `lab_validate(yaml)`, `lab_run(yaml)`.
- Guardrails: numbers only from tool results; no write tools except `lab_run`; no promotion; no config
  changes (only proposed diffs you apply in the Config screen); prompts and tool calls logged in `ai.*`;
  broker secrets never in context. If the LLM is down, the nightly plan is unaffected.

## 4. Features

1. **Daily briefing** (≤ 300 words): regime, the tickets with one-line rationale, risks, system changes.
2. **Ask your data**: "Expectancy of 15m SELL setups in high-vol regimes in 2025?" → query + numbers + chart.
   Also portfolio and baskets: "XIRR of MyLongTerm vs Nifty this year?", "Which basket worked best for 15m setups?"
3. **Strategy drafting**: English idea → DSL YAML → validation → you confirm → Lab run → summary.
4. **Hypothesis proposals** for the learning loop ([doc 09](09-learning-engine.md) §5).
5. **Event flags** (later): classify announcements for the event-risk filter.

## 5. Eval set (re-run on every model/prompt change)

| Task | Test | Pass |
|------|------|------|
| Data questions | 30 questions with known answers | ≥ 90% exact numbers |
| Briefing | 10 historical plans | No numbers absent from input; all tickets covered |
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
