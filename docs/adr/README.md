# Architecture decision records

One file per decision: `NNNN-short-title.md`, using [the template](0000-template.md). Never edit an accepted
ADR's decision — supersede it with a new one and link both ways.

| # | Decision | Status |
|---|----------|--------|
| [0001](0001-foundation-stack-and-repository.md) | Foundation: stack, modular monolith, local-only, monorepo layout | Accepted |
| [0002](0002-config-and-strategies-in-app.md) | Configuration and strategies are managed in the application (database), not as repo files | Accepted |
| [0003](0003-local-stack-compose-and-secrets.md) | Local stack: Docker Compose on Podman; secrets as files written from the Keychain | Accepted |
| [0004](0004-agentic-development-workflow.md) | Agentic development: Claude Code native tooling + lightweight specs, no external framework | Accepted |
