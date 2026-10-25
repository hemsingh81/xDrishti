# Delivery plan (project management view)

Static page — no build step. Open `index.html`, or run the stack and visit http://localhost:8080/prototype/delivery/
(`deploy/xd-up.sh` serves `docs/prototype` read-only through the proxy). `scripts/serve.sh` also serves it on :8766.

- **Data:** `plan-data-1.js` (meta, epics P0–P6) and `plan-data-2.js` (P7–P15, operations track, success criteria,
  open questions, risks, decisions). Derived from `docs/design/18-roadmap.md` and the requirements; keep them in sync.
  P8–P15 stories use the spec ids of `docs/specs/` (index: `docs/specs/JOURNEY.md`); `-O<n>` ids are owner-only acts, `P12-B1` and the like are extra slices with no spec file yet. A story may carry `spec` (its real spec file), `safety` and `owner` flags.
- **Progress (what is really built):** `plan-progress.js` — phases and slices with status, ADR numbers, "Needs you" and honest limits; first tab. Update it (slice status, `asOf`) whenever a slice lands. A slice row is `[id, title, state, adr, {d: agent-days, o: 1 if SAFETY or an owner security decision, s: spec file}]`. The Backlog/Board story ticks are the original product plan and are not updated per slice.
- **App:** `plan-app.js` — views Progress, Overview, Roadmap, Flow, Backlog, Board, Sprints, Risks & decisions, How we work.
- **Your changes** (status, notes, acceptance ticks, answers, custom stories) are stored in this browser's
  localStorage; export them from the menu. To make a change permanent, edit the data files.
- **Effort:** 1 pt ≈ 1 focused hour of the owner's time (agents write the code); capacity ≈ 11 pts/week. Agent build effort is in agent-days (uncalibrated) in each story's note and each slice on the Progress tab; it is not part of the points.
- **Schedule:** epic dates follow the roadmap (each epic starts when the one it follows ends); story dates and sprints
  are a forecast spread over the epic's dates in flow order.
- Each story can produce a pre-filled spec (`docs/specs/TEMPLATE.md` shape) and an agent prompt (ADR 0004).
