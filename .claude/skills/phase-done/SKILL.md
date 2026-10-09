---
name: phase-done
description: Verify a roadmap phase against its exit criterion and the Definition of Done before it is declared finished. Use at the end of a phase or a large slice.
---

# Phase / slice done?

1. Read the phase row in `docs/design/18-roadmap.md` (deliverables + **exit criterion**) and the spec in `docs/specs/`.
2. Build a table: each deliverable and acceptance criterion → evidence (test name, URL, screenshot, query result).
   Anything without evidence is **not done**.
3. Run `scripts/check.sh` (must be green) and the `reviewer` agent on the cumulative diff.
4. Start the stack (`deploy/xd-up.sh`) and confirm `http://localhost:8080/system` is green; exercise the new screens.
5. Check Definition of Done (`docs/engineering/README.md` §4): tests, contract regenerated, migration, design/ADR
   updates, `scripts/build-docs.sh` if `docs/design` changed.
6. Report: done / not done per item, open risks, and what the exit criterion still needs from the user (e.g. a
   manual hand-check of 20 trades). Do not mark the phase finished on your own — the user confirms the exit criterion.
