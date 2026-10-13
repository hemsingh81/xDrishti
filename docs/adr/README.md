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
