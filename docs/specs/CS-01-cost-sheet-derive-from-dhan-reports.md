# CS-01 — `cost-sheet derive`: propose a charge sheet from the owner's Dhan reports

- **Status:** Approved (coordinator's recommendation under the owner's standing instruction of 2026-11-11; every decision as recommended). Not built.
- **Phase / roadmap row:** CS — costs, row CS-01 (new; closes the "derive" half of ISS-25, docs/design/18-roadmap.md:347)
- **Size:** about 2 agent-days (estimate) · **Needs:** P3-03f (built, ADR 0044), P2-17 importers (built, ADR 0029), P8-23 (built, ADR 0127)
- **Safety:** not a SAFETY slice. It writes one local file and nothing else; loading, checking and confirming a sheet stay the owner's acts. No order, no rate is stored by this verb.
- **Design refs:** docs/design/16-configuration.md §2 `costs`; ADR 0029, 0044, 0127

## Goal
The owner runs one verb over his original Dhan global transaction report files and gets a proposed charge-sheet JSON he can read, edit and then load with the existing `cost-sheet load`. Today every real run is gross because no sheet exists (GAP-03).

## Verified facts (main at 30777841)
- ISS-25 is Open: doc 16 §2 leaves `stt_ctt`, `exchange_txn_pct`, `stamp_duty_buy_pct`, `sebi_fee_per_cr` null (docs/design/18-roadmap.md:347).
- **Correction to the review:** the stored bills cannot be the source. The importers keep **one charge total per trade** (`Transaction.Charges`, backend/src/XDrishti.Domain/Portfolio/Transaction.cs:84-85; roadmap :347 "only the original report files hold the lines"). The derive must read the original report files, as `cost-sheet check` does.
- `cost-sheet check` already reads those files (scripts/cost-sheet.sh:6; reader `backend/src/XDrishti.Application/Portfolio/Reports/GlobalTransactionReport.cs`) and computes per class and line the implied rate min / median / max (`CostCheckImplied`, backend/src/XDrishti.Application/Research/CostSheets/CostCheckReport.cs:51-59; `Implied`, CostBillCheck.cs:221-285) and prints it as "a proposal only" (backend/src/XDrishti.Api/CostSheet/CostCheckText.cs:103). The lines are `brokerage, gst, stt, sebi, stamp duty, txn charges, other` (CostBillCheck.cs:29); the check tolerance is ₹0.05 per row (CostBillCheck.cs:15).
- The sheet text is parsed by `CostSheetJson.Parse` (backend/src/XDrishti.Domain/Research/CostSheetJson.cs:11), used by `cost-sheet load` (backend/src/XDrishti.Infrastructure/Research/CostSheetStore.cs:31).
- Confirming is a separate owner act: Settings → Charge sheet → "Allow confirming", then Confirm, each with a fresh password (frontend/src/features/settings/components/ChargeSheetActions.tsx:75-99; ADR 0127).
- Intraday coverage of the importers is UNVERIFIED (ISS-05, roadmap :347).

## Decisions (each as recommended)
1. **Reuse, do not re-derive.** `derive` calls the same reader and the same `Implied` computation as `check`; the new code is the step from implied medians to a sheet JSON and the residual table. *Recommended* (one rate algorithm, not two).
2. **Split the rows.** Rows are split by date into a derive set (older 70 %) and a check set (newest 30 %); rates come from the derive set only and the residuals are printed for both. ISS-25 requires a derived sheet to stay `unchecked` until it passes against rows it was not derived from. *Recommended* over deriving from all rows.
3. **A line with no billed rows is `unavailable`** in the printout and `null` in the JSON (which `CostSheetJson.Parse` refuses, so an incomplete sheet cannot be loaded by accident). Never a zero, never a default.
4. **Output is a file only**, written `0600` under `data/cost-sheets/` (git-ignored `data/`); the id is `derived-<yyyy-MM-dd>` and the `checked` field is absent. *Recommended* over writing to the database.

## Scope
**In:** verb `cost-sheet derive FILE... [--out PATH] [--split 0.7]`; the JSON writer; the per-bill residual table; tests.
**Out:** loading, checking, confirming (existing owner acts); options/F&O lines; effective-date changes inside the window (a rate that moves is reported as `unstable`, see AC 5, never split automatically).

## Acceptance criteria
1. `scripts/cost-sheet.sh derive a.csv b.csv` writes one JSON file and prints its path; the database is not written (asserted by a test that counts `platform.app_settings` rows before and after).
2. The JSON round-trips through `CostSheetJson.Parse` when every line has rows; with a line without rows the file holds `null` there and the printout names the line `unavailable`.
3. Re-pricing the **derive set** with the proposed sheet reproduces each bill's billed total within ₹1 on ≥ 95 % of bills; the printout gives the share for the derive set and, separately, for the check set.
4. The residual table lists per bill: date, class, billed total, modelled total, difference; the ten largest are printed and all rows go to `<out>.residuals.csv`.
5. A line whose implied rate has (max − min) / median > 5 % over the derive set is flagged `unstable` with its date range.
6. No personal line (order id, client id) is printed or written; only date, class and amounts (same rule as `check`).
7. Exit codes match `cost-sheet`: 0 done, 1 refused (no readable row), 2 wrong command line, 3 failure.

## Contract
- **CLI:** `cost-sheet derive FILE... [--out PATH] [--split FRACTION]` (0.5–0.9). No API, no UI, no migration.
- **File:** the doc 16 §2 shape that `CostSheetJson.Parse` reads, with `source: "derived from N bills, YYYY-MM-DD..YYYY-MM-DD"`.

## Test plan
Unit: implied medians → sheet; null line → `unavailable`; split by date; unstable flag. Integration: synthetic report files (two classes, one missing line) → file written, nothing stored; AC 3 on a synthetic set priced by a known sheet (must recover it within ₹1 per bill on 100 %).

## Risks
Bills without intraday rows (ISS-05) give an intraday sheet of `unavailable` lines; GST rounding per leg can move a bill by paise (well inside ₹1); a mid-window budget change shows as `unstable` and needs the owner's effective date.

## Done when
- [ ] ACs 1–7 have evidence; `scripts/check.sh` green; reviewer: no blockers
- [ ] docs/design/18-roadmap.md ISS-25 row notes the derive path (by the roadmap owner)
