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
| [0031](0031-streaming-indicators-and-the-feature-frame.md) | Streaming indicators and the feature frame: closed bars only, one code path | Accepted |
| [0032](0032-frame-history-closed-flags-and-the-remaining-indicators.md) | The frame exposes closed flags and value history; the remaining indicators and patterns | Accepted |
| [0033](0033-weekly-monthly-timeframes-calendar-features-and-the-market-context.md) | Weekly and monthly timeframes, calendar features and a daily market context read as of the previous session | Accepted |
