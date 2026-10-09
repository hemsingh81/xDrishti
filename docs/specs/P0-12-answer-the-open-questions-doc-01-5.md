# P0-12 — Answer the open questions (doc 01 §5)

- **Status:** Done
- **Phase / roadmap row:** P0 — Foundations & data trial
- **Design refs:** docs/design/01-overview.md §5; 02 FR-3/FR-8; 10 §6; 12; 16; 17 §1; 19 §5

## Goal
Turn the open questions into recorded decisions, so the stories they block (P0-14, P1-07, P1-15, P2-01, P4-04, P7-02,
P12-09, …) can be specified without guessing. This is a decision story: no application code changes.

## Scope
**In:** the six questions in doc 01 §5 plus two added by the delivery plan (Q7 repository hosting, Q8 Podman machine
size); recording each answer in the design docs; unblocking the dependent stories.
**Out (not in this slice):** importing the sample file (P0-14), building anything that uses the answers (P1+),
choosing instruments for baskets beyond naming the initial baskets and their purpose.

## Decisions to take
Each row needs one answer from the owner. "Proposed" is a default so you can reply "accept defaults" to any row.

| # | Question | Options | Proposed default | Blocks | Lands in |
|---|----------|---------|------------------|--------|----------|
| Q1 | Provide one sample 1-minute CSV (columns, timestamp format, bar start/end, raw vs adjusted) | file path or paste of the first ~20 lines | — (needs your file; keep P0-14 blocked until given) | P0-14, P1-07 | 05 §3 import profile |
| Q2 | Trading capital and risk per trade / daily loss limit | any numbers | 0.5% per trade, 1.5% daily loss, capital as you state it | P4-04, P7-02 | 16 `risk_profiles`, 08 §6–7 |
| Q3 | Initial baskets and their purpose | e.g. MyChoice = intraday, MyLongTerm = holdings only, Best stocks = swing | the three examples above | P1-15 | 06 §3, 16 |
| Q4 | Several accounts: replicate or distribute | replicate / distribute | replicate | P7-02 | 10 §6, 16 `allocation.mode` |
| Q5 | How far back portfolio history goes | Dhan trade-history window only / plus contract-note CSV back to a date | Dhan window now; CSV import for older lots (P2-03) | P2-01, P2-03 | 12 §1 |
| Q6 | Static IP from your ISP | yes / later / no | later — only needed before P12 | P12-09 | 17 §1, 19 §5 |
| Q7 | Repository hosting | GitHub remote / local-only | local-only until you decide (no CI dependence) | OPS-05 | ADR (if it changes ADR 0001) |
| Q8 | Podman machine size before xd-llm and tick data | keep 24 GB / 120 GB / grow | keep now; revisit at P0-15 (xd-llm benchmark) | P0-15, P9-01, P13-01 | 17 §1 |

## Acceptance criteria
1. Q1–Q8 each have a recorded answer, or an explicit "decided later, by story X" with the reason. Q1 may stay open only
   if P0-14 stays **Blocked**.
2. Doc 01 §5 is rewritten from "Open questions" to "Decisions" (question, answer, date); no question is left
   unanswered without an owner and a deadline story.
3. Every answer is reflected in the design doc listed in "Lands in", and the numbers match `docs/design/16-configuration.md`
   defaults or the config reference notes the intentional difference.
4. Any answer that changes an accepted decision (e.g. Q7) is recorded as a new ADR in `docs/adr/`; the older ADR is not
   edited, only linked.
5. The delivery plan reflects the outcome: answered questions are marked in *Risks & decisions*, P0-12 is set to Done,
   and stories whose blocker is gone are moved from Blocked/Backlog to Ready (P0-14 stays Blocked until Q1 is supplied).
6. `scripts/build-docs.sh` is run and `docs/site` reflects the edited design docs.

## Contract
- **API / Data / UI / Jobs:** none — documentation and plan data only.
- **Files touched (expected):** `docs/design/01-overview.md` and the docs in the table, `docs/design/16-configuration.md`
  (only if a default changes), `docs/adr/` (if Q7 or another answer needs one), `docs/prototype/delivery/plan-data-*.js`
  (question text/status, story statuses), regenerated `docs/site/`.

## Non-functional
Decisions stay consistent across documents (one source per number). No secrets or account identifiers in docs:
capital may be stated as a figure or range but never broker credentials, client IDs or account numbers.

## Test plan
No code, so verification is mechanical:
- `grep` doc 01 for the string "Open questions" → none; every Q1–Q8 id present with an answer.
- Each "Lands in" doc contains the recorded value (spot-check by grep).
- `scripts/build-docs.sh` exits 0 and `docs/site/01-overview.html` shows the Decisions table.
- `scripts/check.sh` still green (docs-only change leaves code untouched).
- Delivery plan loads without console errors and shows the expected statuses.

## Open questions
Q1–Q8 above are the work itself. Answered in chat: Q4–Q8 accept the defaults; Q2/Q3 deferred to P1-16/P1-15 (app data); Q1 still needs the sample CSV.

## Done when
- [x] All acceptance criteria have evidence
- [x] Reviewer agent: docs consistent, no blockers/majors
- [x] `scripts/build-docs.sh` run; ADR added if an accepted decision changed
- [x] Plan statuses updated; dependent stories unblocked
