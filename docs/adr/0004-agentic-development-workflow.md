# 0004 — Agentic development: Claude Code native tooling + lightweight specs

- **Status:** Accepted
- **Date:** 2026-10-09

## Context
xDrishti is built part-time (~10–12 h/week) by one developer working with AI coding agents. We want agents to produce
code that follows the engineering rules (SOLID, modular, testable, fast, secure) without constant correction, and we
want the process itself to live in the repository. Candidates: spec-driven toolkits (GitHub Spec Kit, OpenSpec,
BMAD-METHOD), Python agent frameworks (LangGraph, CrewAI, AutoGen), or Claude Code's built-in extension points.

## Decision
Use **Claude Code's native features**, versioned in the repo, plus a **light spec layer**:
- `CLAUDE.md` rule files at every level (already in place) — loaded automatically.
- `docs/specs/` — one short spec per vertical slice (template with acceptance criteria, contract, test plan).
- `.claude/agents/` — `backend-dev`, `frontend-dev`, `test-writer`, and a read-only `reviewer` for independent review.
- `.claude/skills/` — `/new-feature`, `/add-endpoint`, `/new-migration`, `/phase-done`.
- `.claude/hooks/` + `.claude/settings.json` — block secret-looking content, format on edit, require a green
  `scripts/check.sh` before an agent finishes work on changed code; deny rules for generated files, Keychain/secret
  reads, force-push and destructive Podman commands.
- The quality gate (`scripts/check.sh`, analyzers, architecture tests, ESLint boundaries) remains the authority on "done".
- Agents do not commit or push unless asked.

External spec toolkits and Python agent frameworks are **not adopted**: they add process and a second toolchain for a
solo developer, while the checked-in specs plus the automated gate give the same control. Revisit if the team grows or
spec volume makes the template insufficient.

## Consequences
- Every session starts with identical rules and guardrails; mistakes found in review are fixed once in a rule file,
  analyzer or test.
- The Stop hook adds a `check.sh` run (a few minutes) after code changes; documentation-only changes are exempt.
- Hooks require `jq` and the local .NET SDK / `frontend/node_modules`; they degrade silently when a tool is missing.
- Product-side agents (P9 assistant, Hermes PoC) are a separate concern, governed by `docs/design/14-ai-assistant.md`.
