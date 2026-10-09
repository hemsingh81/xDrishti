# docs

| Folder | What it is |
|--------|-----------|
| [`design/`](design/) | The design documents (01–21) — Markdown, the source of truth. Start with [01 Overview](design/01-overview.md) and [03 Target architecture](design/03-target-architecture.md). |
| [`engineering/`](engineering/README.md) | Engineering standards: SOLID, modularity, reuse, testing, performance, definition of done. |
| [`adr/`](adr/) | Architecture decision records — one short file per significant decision, added as implementation goes. |
| [`prototype/`](prototype/index.html) | Clickable UX prototype (offline, demo data). Reference for the React app; frozen once the real UI exists. |
| [`site/`](site/index.html) | Generated, searchable HTML version of `design/`. Do not edit — run `scripts/build-docs.sh`. |

Editing rules: change the Markdown in `design/`, then rebuild the site. When implementation changes a design
decision, update the design doc **and** add an ADR.
