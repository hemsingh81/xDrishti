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
| [0007](0007-accounts-credential-references-and-step-up.md) | Accounts: credential references instead of secrets in the database, step-up (password re-entry), one primary Data account, archive instead of delete | Accepted (decision 1 superseded by 0008) |
| [0008](0008-broker-credentials-vault-and-connection.md) | Broker credentials in an encrypted vault; broker-first accounts; connecting Dhan (consent) and Upstox (authorization code) | Accepted (narrowed by 0009) |
| [0009](0009-dhan-consent-flow-only.md) | Accounts: Dhan only, API key & secret consent flow only; account status in the header | Accepted |
