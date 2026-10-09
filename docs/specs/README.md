# Specs

One short spec per implementable slice of a roadmap phase (`docs/design/18-roadmap.md`). A spec is the contract between
you and the coding agents: it says **what** and **how we know it is done**, not how to code it (the rules live in the
`CLAUDE.md` files and `docs/engineering/README.md`).

- File name: `P<phase>-<nn>-<slug>.md` (e.g. `P1-01-instrument-master.md`). Copy [TEMPLATE.md](TEMPLATE.md).
- Status flows `Draft → Approved → Done`. Agents implement only **Approved** specs.
- Specs trace to `docs/design/*`; if implementation forces a design change, update the design doc and add an ADR.
- Keep a spec small enough for one vertical slice (about 1–3 days of agent work). Split otherwise.

Workflow: [docs/engineering/README.md §5](../engineering/README.md#5-agentic-development-workflow) · decision:
[ADR 0004](../adr/0004-agentic-development-workflow.md).
