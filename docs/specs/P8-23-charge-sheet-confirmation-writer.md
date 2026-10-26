# P8-23 — Charge-sheet confirmation writer (the owner's act that makes a figure "net")

- **Status:** Approved by the owner on 2026-10-26 (the owner approved building this SAFETY slice; the decisions below were researched when it was written and are the owner's to revert).
- **Phase / roadmap row:** P8 — Reports & UI, slice P8-23 (the "confirmation writer" that [ADR 0100](../adr/0100-the-report-endpoints-read-the-stored-runs-through-one-query-model-with-an-evidence-block-and-net-only-by-the-owners-stored-confirmation.md) decision 6 and ISS-25 left to a separate slice, and that [P10-04](P10-04-backtest-vs-paper-report.md) and [P10-06](P10-06-paper-phase-check.md) wait for).
- **Design refs:** docs/design/04-accounts-access-and-operations.md §4.1 ("Only you"), §2 (step-up, lockout) · 16-configuration.md (`costs`) · 19-risks-and-compliance.md (cost blindness) · ADR 0044 (the stored charge sheet), 0099 and 0100 (net only by the owner's confirmation), 0122 and 0123 (the paper comparison and phase check read the fact), 0091 (the owner-act pattern of the autonomy switches).
- **Size:** 3 agent-days.
- **Safety:** **yes.** An owner act: it decides when the system may call a figure "net". It places no order, edits no stored run and no stored sheet, and changes no existing report number unless the owner confirms.

## Goal
The owner can confirm ONE stored charge sheet as their own, at the SHA-256 of the rates they were shown, with their password typed again for that act, behind a switch that is OFF until they turn it on. From then on, and only while the stored sheet still has exactly those rates, the reports print net figures for runs costed with that sheet and the paper comparison can evaluate the cost-dependent criteria (E3, E4), so a real MET of the P10 check is reachable, and only with real evidence.

## Scope
**In:** a table `workflow.charge_sheet_events` (insert-only); a domain act and standing rule; one writer handler and one status handler; four endpoints; a seventh audit source; the production `ICostConfirmationFact` over the same confirmation; the verb `xd charge-sheet show`; a lazy Settings tab; ADR 0127; tests.
**Out:** confirming from the terminal (the verb refuses: no password check, and `cli:xd` is not the owner); a list of the stored sheets (the owner names the sheet); editing or deleting a sheet; checking the sheet against bills (P3-03f's `check` does that); confirming per run or per account; any automatic confirmation; any change to a stored run, trade or sheet; exports.

## Acceptance criteria
1. **Default OFF.** Given a fresh database, then `GET /api/charge-sheet/confirmation` says `state: not_confirmed`, `capabilityOn: false`, `inForce: null`; `IChargeSheetConfirmationSource.FindAsync` returns null; `ICostConfirmationFact` returns null for every sheet. *Integration.*
2. **The switch refuses.** Given the capability is off or was never set, when the owner posts a confirmation with the right password, then 403 `chargesheet.capability_off` and no row. A direct SQL insert of a confirmation while the newest capability row is not "on" fails with check violation (23514), also as `xd_app`. *Integration, grants test.*
3. **A fresh password for every act.** Given a missing or wrong password, when any of confirm, revoke or capability is called, then 401 `auth.invalid_credentials` and no row; an open step-up window does not stand in for the password and is neither closed nor extended by the act; five wrong passwords lock as at sign-in; the screen does not treat the 401 as an ended session. *Integration; Vitest.*
4. **Owner only, from the session.** The actor of every row is `owner:<session e-mail>`; a body that names an actor, an `owner`, or a `confirmed` flag changes nothing; without a session 401. The database refuses a row whose actor is not `owner:...` (`cli:xd`, `rule:autonomy`, empty). *Integration, domain, grants test.*
5. **Bound to the rates.** A confirmation names the sheet id and the SHA-256 of its rates as the owner was shown; if the stored sheet's hash differs now, 409 `chargesheet.rates_changed` and no row; an unknown sheet is 404 `chargesheet.sheet_not_found`; a `test-` sheet, a malformed id or hash, a blank or over-long reason is 400. *Integration, application.*
6. **Stale is not confirmed.** Given a confirmation and then the stored sheet saved again with other rates, then the status says `stale`, `IChargeSheetConfirmationSource` returns null (so `basis=net` is 422 `reports.net_not_confirmed`) and the paper fact is null; the first row is still there, unedited; confirming again at the new hash makes it stand again. A confirmation whose sheet is no longer stored reads `sheet_missing` and is not confirmed. *Integration, application, domain.*
7. **Revoke is another row.** The revoke names the sheet and hash it withdraws (taken from the confirmation in force), ends the confirmation, works also while confirming is switched off, and writes nothing when nothing is confirmed (`changed: false`). A second confirmation after a revoke is a new row and needs the capability on. No row is ever updated or deleted: `xd_app` has SELECT and INSERT only. *Integration, grants test.*
8. **Idempotent under concurrency.** Five confirmations of the same sheet at the same hash at once write one row; all answer 200 with `confirmed`, one with `changed: true`. *Integration.*
9. **Nothing confirms by itself.** No setting, job, service, rule or CLI verb writes the confirmation: the write port is used by one handler, the act command by one endpoint module; the capability row only allows the owner to confirm. A hand-written `platform.app_settings` row `reports.charge_sheet_confirmation` confirms nothing. *Architecture, integration.*
10. **Audit.** Every row appears in `GET /api/audit/entries?source=chargesheet` with its kind, the owner, the sheet and the first 12 characters of the hash, and the reason (redacted like every free text); no password anywhere. The log event ids 4950 to 4952 carry row ids, kinds and codes only. *Integration.*
11. **The reports and the paper comparison read it.** After the owner's confirmation `basis=net` is possible for runs costed with that sheet and hash, and the evidence prints the sheet, the first 12 characters of the hash and the date; after a revoke it is 422 again. The production `ICostConfirmationFact` returns the confirmed day for the confirmed sheet only; `NotConfirmed` stays the Application and test default; every "never MET on unconfirmed evidence" test of P10-04 and P10-06 is unchanged and green. *Integration, infrastructure, existing tests.*
12. **Screen.** Settings has a tab "Charge sheet" loaded on demand (first-load JavaScript unchanged apart from about 0.1 KB): the state in words (a chip carrying the word), the sentence of what confirming means, the sheet (label, source, checked-against, schedules, the full rates hash), the history, a reason box and a password box, and the three acts; Confirm is disabled until a sheet is shown, confirming is on, and there is a reason and a password; the password is cleared after every try; typed text is shown as text. *Vitest with MSW.*
13. **Verb.** `xd charge-sheet show [--sheet ID]` prints the state, the switch, the sheet and hash and the history and writes nothing; `confirm` and `revoke` exit 1 and say why; control characters in typed text are replaced. *Integration.*
14. `scripts/gen-api.sh` run; problem codes are in the OpenAPI summaries; migration `AddChargeSheetEvents` has a `Down`, grants its own table and does not re-grant the schema; log ids unique; `scripts/check.sh` green.

## Contract
- **API (session + CSRF; writes: the password in the body, checked on every request, rate limit policy `login`; `Cache-Control: no-store`):**
  - `GET /api/charge-sheet/confirmation?sheetId=` -> `ChargeSheetStatusView { capabilityOn, state (not_confirmed|confirmed|stale|sheet_missing|revoked), text, inForce { sheetId, ratesSha256, confirmedOn, actor, at }?, sheet { id, label, source, checked?, schedules, firstEffective, lastEffective, ratesSha256 }?, sheetNote?, meaning, history[20] }`; 400 `chargesheet.sheet_invalid`.
  - `POST /api/charge-sheet/confirmation` `{ sheetId, ratesSha256, reason, password }` -> `ChargeSheetActOutcome { changed, status }`; 401, 423, 429, 403 `chargesheet.capability_off`, 404 `chargesheet.sheet_not_found`, 409 `chargesheet.rates_changed`, 400 `chargesheet.sheet_invalid|rates_invalid|reason_invalid`.
  - `POST /api/charge-sheet/confirmation/revoke` `{ reason, password }` -> outcome. `PUT /api/charge-sheet/capability` `{ on, reason, password }` -> outcome.
- **Data:** `workflow.charge_sheet_events (id identity, kind in gate_on|gate_off|confirm|revoke, sheet_id?, rates_sha256?, confirmed_on?, actor owner:%, reason, created_at)`; a check on the shape; a `BEFORE INSERT` trigger draws id and time under an advisory lock, refuses a confirmation unless the newest capability row is `gate_on`, and stamps `confirmed_on` (India date); index `(kind, id)`; `xd_app` SELECT and INSERT only (own-table grant). Migration `AddChargeSheetEvents`.
- **UI:** Settings, tab `Charge sheet` (`?tab=charge-sheet`), one lazy component, MUI, no new route.
- **Events/jobs:** none. Log events 4950 `chargesheet.act`, 4951 `chargesheet.act.repeated`, 4952 `chargesheet.refused`.

## Non-functional
A status read is two small selects and the sheet read; an act is one transaction. No secret in logs (row ids, kinds, codes). The password hash check is the existing sign-in one. Bundle: one lazy chunk of 6.6 KB (about 2.4 KB gzip); eager growth about 0.1 KB.

## Test plan
Domain (act validation, standing table), application (handlers with an in-memory log: off, on, rates changed, unknown sheet, stale, missing, idempotent, revoke, non-owner, reason), architecture (one writer, one endpoint module, read handlers without a write port, readers of the source, insert-only SQL, per-table grant, no schema re-grant), integration with the real database (HTTP matrix, concurrency, trigger, audit, verb, reports and paper fact), grants (`xd_app` cannot update, delete or truncate; the trigger overwrites a chosen id, time and date), frontend (MSW: states, disabled rules, password cleared, 401 not a sign-out, 409 text, stale, revoke, markup shown as text).

## Decisions (researched)
- **D1 A table, not the settings row.** ADR 0100 named the row `reports.charge_sheet_confirmation`; a settings row is updated in place and has no history. The owner asked for append-only with actor and time, and a revoke as another row. The source now reads the table; the old constant is gone and a stray settings row is ignored.
- **D2 One log for the capability and the acts.** One table, one lock, one migration; the newest capability row is the switch, the newest confirm-or-revoke row is the confirmation. Rejected: a second table for the switch (two locks to order).
- **D3 The capability is also a row and also an owner act with a password.** Default OFF means no row. Turning it on needs the password; turning it off needs it too (simplest rule: every act). It gates confirming only: a revoke is always allowed (it can only make figures gross), and switching off does not revoke.
- **D4 The hash is the binding.** The body carries the hash the owner saw; the handler compares it with the stored sheet NOW. The store overwrites a sheet in place, so a name alone would silently cover other rates.
- **D5 One confirmation in force.** Confirming another sheet supersedes the first (newest row wins); the history keeps both.
- **D6 Idempotent, not an error.** A repeated identical confirmation, a revoke with nothing to revoke and a capability already in that state write nothing and answer `changed: false`, decided under the lock.
- **D7 The fact lives in Infrastructure.** `StoredCostConfirmation` reads `IChargeSheetConfirmationSource`; the ADR 0100 list of Application readers is unchanged; the Application keeps `NotConfirmed` (`TryAdd`) as default and test default.
- **D8 Verb: show only.** The terminal has no password check and its actor `cli:xd` is not an owner; confirm and revoke would break "a fresh password per act".
- **D9 Audit by a seventh source** on the audit screen (category `configuration`), not an `account_events` row (that table is per account).
- **D10 Where in the UI:** Settings (not Reports or Paper): both are read-only screens guarded by a source test that forbids any write call and any import of another feature.
- **D11 An unchecked sheet can be confirmed.** The owner's act is the confirmation; the screen shows "Not recorded: this sheet is unchecked" beside it. Rejected: refusing it (it would make the owner's own judgement a rule of ours).

## Risks and honest limits
- The system cannot know the sheet is right; it records that the owner said so, when, and at which rates. A wrong confirmation makes every net figure carry the wrong costs.
- The paper fact is by sheet id. A paper trade records the sheet id, not the rates hash it was costed with: if the owner changes a sheet's rates and confirms again, earlier paper trades of that id count as confirmed at the new rates.
- "Owner" is a string with a database check (as in P7-12): a process holding `xd_app` could insert an owner row only while the capability is on, and the trigger still stamps it; the control is that the only writer is the endpoint behind session, CSRF, rate limit and password.
- The audit row exists by construction (same table); a read of the audit is not audited.
- Nothing in the running stack was switched on by this slice: the capability is off and no row exists until the owner acts.

## Open questions
None blocking. Owner acts after the build: deploy, open Settings > Charge sheet, load the real sheet with `scripts/cost-sheet.sh`, check it against the bills (`check`), switch confirming on, confirm.

## Done when
- [x] All acceptance criteria have evidence
- [x] `scripts/check.sh` green; reviewer pass
- [x] Contract regenerated (`scripts/gen-api.sh`); migration added
- [x] Design docs/ADR updated (ADR 0127; doc 04, 16, 18)
