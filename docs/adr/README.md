# Architecture decision records

One file per decision: `NNNN-short-title.md`, using [the template](0000-template.md). Never edit an accepted
ADR's decision — supersede it with a new one and link both ways.

| # | Decision | Status |
|---|----------|--------|
| [0001](0001-foundation-stack-and-repository.md) | Foundation: stack, modular monolith, local-only, monorepo layout | Accepted |
| [0002](0002-config-and-strategies-in-app.md) | Configuration and strategies are managed in the application (database), not as repo files | Accepted |
| [0003](0003-local-stack-compose-and-secrets.md) | Local stack: Docker Compose on Podman; secrets as files written from the Keychain | Accepted |
| [0004](0004-agentic-development-workflow.md) | Agentic development: Claude Code native tooling + lightweight specs, no external framework | Accepted |
| [0005](0005-local-llm-runtime-and-models.md) | Local LLM: container GPU runtime on the 24 GB machine, Qwen3-30B-A3B (alt gpt-oss-20b), on demand | Proposed (owner to confirm) |
| [0006](0006-owner-auth-sessions.md) | Owner authentication: server-side cookie sessions, PBKDF2, derived CSRF token, closed-by-default API | Accepted (cookie SameSite amended by 0008) |
| [0007](0007-accounts-credential-references-and-step-up.md) | Accounts: credential references instead of secrets in the database, step-up (password re-entry), one primary Data account, archive instead of delete | Accepted (decision 1 superseded by 0008; seeded buckets amended by 0011) |
| [0008](0008-broker-credentials-vault-and-connection.md) | Broker credentials in an encrypted vault; broker-first accounts; connecting Dhan (consent) and Upstox (authorization code) | Accepted (narrowed by 0009) |
| [0009](0009-dhan-consent-flow-only.md) | Accounts: Dhan only, API key & secret consent flow only; account status in the header | Accepted |
| [0010](0010-instrument-master-sources-and-scope.md) | Instrument master: Dhan scrip master + NSE sector list, NSE shares/ETFs/indices/futures only, manual + daily refresh | Accepted |
| [0011](0011-default-portfolio-bucket-per-account.md) | Portfolio: one automatic default bucket per Portfolio account (no presets); acquisition dates derived from Dhan trades and ledger, no manual history date | Accepted (acquisition-date part superseded by 0012; bucket choices in Accounts removed by 0015) |
| [0012](0012-holdings-acquisition-dates-and-returns-model.md) | Holdings: owner-set acquisition dates (default 1 Jan 2025), lot model, auto refresh, returns/XIRR | Accepted |
| [0013](0013-portfolio-import-with-settings-and-daily-refresh.md) | Portfolio: saving the account's Portfolio settings imports the holdings; refresh Off or Daily; Sync now on the Portfolio screen | Accepted (date read-only rule amended by 0015) |
| [0014](0014-portfolio-buckets-appear-as-system-baskets.md) | Portfolio buckets appear on the Baskets page as read-only, protected system baskets | Accepted |
| [0015](0015-first-import-date-editable-and-no-bucket-ui-in-accounts.md) | The first import date stays editable (re-dates the default-dated lots); no bucket choices in Accounts | Accepted |
| [0016](0016-market-bars-csv-load-dhan-fetch-and-app-managed-aggregates.md) | Market bars: CSV folder load, Dhan intraday fetch, app-managed aggregates for every timeframe | Accepted |
| [0017](0017-trading-calendar-eod-schedule-quality-adjustments-and-csv-undo.md) | Trading calendar, scheduled end-of-day run with gap backfill, data quality, price adjustments, CSV undo | Accepted |
| [0018](0018-services-framework-and-editable-schedules.md) | Services framework: schedules in the database, one scheduler, the Services screen | Accepted |
| [0019](0019-holdings-quotes-day-change-and-the-first-interval-service.md) | Holdings quotes every 5 minutes (Dhan quote API), day change, and the first interval service | Accepted |
| [0020](0020-portfolio-value-history-benchmark-and-charts.md) | Portfolio value history and benchmark computed from lots and stored daily bars; ECharts for charts | Accepted |
| [0021](0021-portfolio-grouping-and-allocation-charts.md) | Portfolio grouping by account, bucket, sector or basket, computed on the server, with allocation charts | Accepted |
| [0022](0022-action-center-and-price-alerts.md) | Portfolio Action center evaluated on read, and one-shot price alerts checked by the quotes service | Accepted |
| [0023](0023-trade-import-and-lot-reconstruction.md) | Trades from CSV or by hand are stored as transactions and rebuild a holding's lots with real dates | Accepted |
| [0024](0024-live-feed-xd-feed-and-live-prices.md) | Live feed in its own container (`xd-feed`), live prices in the holding price columns, SignalR hub for the Portfolio screen | Accepted |
| [0025](0025-dhan-trade-history-and-ledger-sync.md) | Dhan trade history and ledger are read nightly; lots are rebuilt automatically where it is safe | Accepted |
| [0026](0026-dividends-entered-by-the-owner-and-charges-in-net-returns.md) | Dividends are entered by the owner; charges and dividends form a net return next to the gross one | Accepted |
| [0027](0027-self-healing-sync-and-actionable-notifications.md) | Syncs heal themselves; what only the owner can do arrives as one actionable notification | Accepted |
| [0028](0028-dividend-csv-import-with-name-matching.md) | Dividend files are recognised by their headers and matched to holdings by name, with remembered corrections | Accepted |
| [0029](0029-dhan-report-imports-and-source-precedence.md) | Dhan's downloaded reports are imported by kind, and API trades take precedence over bill-level report rows | Accepted |
| [0030](0030-read-only-notices-retention-and-structured-failures.md) | Notices are read-only, attention rows are kept for 30 days, and failures carry a kind | Accepted |
| [0031](0031-streaming-indicators-and-the-feature-frame.md) | Streaming indicators and the feature frame: closed bars only, one code path | Accepted (decision 4 and the "last closed bar" of decision 2 narrowed for the per-session features by 0035) |
| [0032](0032-frame-history-closed-flags-and-the-remaining-indicators.md) | The frame exposes closed flags and value history; the remaining indicators and patterns | Accepted (decision 2, the value history, narrowed for the per-session features by 0035) |
| [0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md) | Weekly and monthly timeframes, calendar features and a daily market context read as of the previous session | Accepted |
| [0034](0034-the-rules-expression-language-lint-and-compiler.md) | The rules expression language: one grammar, three-valued logic, a look-ahead lint and a compiler to expression trees | Accepted (close-out 2026-10-17: Amendments 1, 3, 4, 7, 14 and 18 decided on research under the owner's delegation, the owner may revert; Amendment 2 closed by ISS-12; Amendment 19 superseded by 0035; the six deviations of 0033 still await the owner) |
| [0035](0035-per-session-features-keep-the-history-of-the-session-only.md) | Per-session features keep the history of the session only | Accepted (two decisions delegated to research, the owner may revert; supersedes in part 0031 decision 4 and Amendment 19 of 0034) |
| [0036](0036-the-typed-strategy-model-and-the-compile-of-its-sections.md) | The typed strategy model (schema 1) and the compile of its sections: lists evaluated at list level and never joined, all sections together at each closed bar, one feature list with a sentinel | Accepted (owner, 2026-10-18; parameters of policies P3 does not run are provisional until P4) |
| [0037](0037-simulation-conventions-and-the-two-stage-seam.md) | Simulation conventions: how an entry is filled, how a trade leaves, and the two-stage seam (candidate and walker); Amendment 1 (P3-03a): the entry scanner, its counters, the `Simulator` with its refusals and header labels, and ORB v1; Amendment 2 (P3-04): the window-independent replay, the data notes, the segment restart, the run record and its key; Amendment 3 (P3-04a): the run over the ports, the universe, the reads, the refusals before any read and the engine port | Accepted (letter i, the five-minute bound on the fill delay, decided on research, the owner may revert; decisions D3, D4, D5 and D7 are superseded by 0038) |
| [0038](0038-fill-realism-the-entry-limit-gaps-slippage-ticks-and-the-order-inside-a-minute.md) | Fill realism: the entry limit, gaps, slippage, ticks and the order inside one minute (decimal arithmetic, schema 2 of the trade, `sim-2`) | Accepted (owner, 2026-10-18; nine decisions taken on research in the spec, the owner may revert; supersedes decisions D3, D4, D5 and D7 of 0037; the stored tick size is still unverified) |
| [0039](0039-an-engine-module-is-reached-from-an-adapter-in-infrastructure-never-from-application.md) | An engine module is reached from an adapter in Infrastructure, never from Application: Application references Domain only; a use case reaches an engine through a port, handler → port → adapter → engine | Accepted with spec P3-04a (the owner approved the spec that requests it, 2026-10-18; refines the dependency rule of doc 03 §4) |
