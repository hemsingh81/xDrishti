# P2-17 — Dhan report imports (global transaction report, DP charges, demat holding)

- **Status:** Approved (owner, 2026-10-15: supplied the reports and asked "analysis them and update the system"; the scope below is the owner's request made concrete — shout if the precedence rule, criterion 9, is not what you want) · decision [ADR 0029](../adr/0029-dhan-report-imports-and-source-precedence.md)
- **Phase / roadmap row:** P2 — completes the history side of the account sync (trade history, ledger/charges) for files the owner already has; extends the import pattern of P2-03 and P2-15
- **Design refs:** docs/design/12-portfolio-monitoring.md §3 ("Trades and the rebuild of lots", "Dhan sync (P2-13)", "Dividends, charges and net returns (P2-14)") · [ADR 0023](../adr/0023-trade-import-and-lot-reconstruction.md) · [ADR 0025](../adr/0025-dhan-trade-history-and-ledger-sync.md) · [ADR 0026](../adr/0026-dividends-entered-by-the-owner-and-charges-in-net-returns.md) · [ADR 0028](../adr/0028-dividend-csv-import-with-name-matching.md)
- **Samples and analysis:** `data/dhan/reports/` (git-ignored; the originals carry personal details in their first lines)

## Goal
The owner drops any of Dhan's downloaded reports on the import screen; the app recognises the report by its headers, shows what it would add, and imports it with Undo — trades with true dates, sides and charges from the global transaction report, depository charges from the DP-charges sheets, and the demat holding statement as a check and a name→ISIN source — without double counting what the Dhan sync already read.

## What the reports are (analysis in `data/dhan/reports/ANALYSIS.md`)
- **Global transaction report** (CSV): one row per bill, per scrip, per day, with buy/sell quantity and value and every charge (brokerage, GST, STT, SEBI, stamp duty, transaction, other). No ISIN, no time. Rows for futures/options (`FUT …`, `OPT …`) and MCX are in the same file. Charges add up to the report's own "Total Charges".
- **DP charges** (legacy `.xls`): date, ISIN, security name, quantity, buy/sell, type, charge (₹12.50 on a sell day; ₹0 "DP Fees" lines).
- **Demat holding** (CSV): one row per security with ISIN, depository quantity and closing price.
- **Realised P&L / P&L** (CSV): per scrip per year; used as a **test oracle** here (FIFO over the transaction rows reproduces Dhan's realised P&L for most cash scrips; the others miss earlier buys) and recognised by the detector so the app can say "this report is not imported; it is a statement of results" — a reconciliation feature is a follow-up.

## Scope
**In:**
1. **Report detector** (extends the P2-15 `FileKindDetector`): global transaction report, DP charges, demat holding, realised P&L, P&L, dividend payout, trade file with side, tradebook without side, unknown — by title line / headers; `.csv` and legacy `.xls` (BIFF8) and `.xlsx`.
2. **Global transaction report → trades** through the existing trade preview/import/undo: side from the buy or sell quantity, price = value ÷ quantity (rounded to 6 decimals; quantity × price reproduces the value to the paisa), charges = the sum of the seven charge columns (≥ 0), `trade_date` from the date, identity = `g:` + bill no. + date + normalised name + side; a bill line with both buy and sell quantities becomes two trades (buy first, charges split by value). Futures/options/MCX rows and rows that cannot be tied to an equity or ETF are skipped and **counted per reason** (never silently dropped). The metadata lines (name, client code, mobile, e-mail) are never read into the model, stored or logged.
3. **ISIN resolution** for report rows, in this order: remembered alias (ADR 0028 table, now keyed by ISIN too), name equality with the account's holdings, the instrument master by name, the ISINs learned from a demat-holding or DP-charges file imported earlier. A name that cannot be resolved is imported ISIN-less like a CSV trade with a symbol only and shown in the preview as *needs a match* (choose once, remembered).
4. **Venue:** a BSE row is imported when its ISIN is one of the account's securities (the same ISIN trades on both exchanges; the lot is the same) — the CSV rule "BSE rows are skipped" stays for rows that match nothing the account holds.
5. **DP charges → account costs:** each line becomes a cost entry (class `charges`, description "DP charges", the ISIN, date, quantity, source `import`, batch) stored with the ledger entries so the P2-14 net figures count it (it passes `LedgerCosts.IsAccountLevel`); ₹0 lines and buy-side lines are listed and not stored. `.xls` is read natively (ExcelDataReader, MIT, pinned) — no conversion step.
6. **Demat holding → check and names:** never creates or changes holdings (the broker's holdings API stays authoritative). It (a) teaches the name→ISIN map, (b) compares the depository quantity with the synced holdings and lists differences in the preview and as a notice (securities held at Dhan but not synced, quantity differences, synced holdings absent from the statement).
7. **Source precedence and de-duplication (ADR 0029):** per (account, trade date, ISIN, side) a group of Dhan-API trades (execution level) beats an imported bill row. Importing a bill row whose group already exists from the API is skipped as *covered by Dhan*; when the API sync later stores a group for which an imported bill row exists, that row is voided as *superseded*. Differences in total quantity are shown, not hidden.
8. **UCC check:** when the report's client code differs from the chosen account's Dhan client id, the preview warns (a file of another account is not imported unless the owner ticks it).
9. Frontend: one **Import** entry (Trades drawer and Cash & dividends panel both link to it) with drag-and-drop of one or several files; per-file card with the detected kind, counts, warnings and the same preview → import → Undo flow; a file of a kind that belongs on another screen is routed there with a button.

**Out (not in this slice):** futures/options/commodity positions and their P&L; reconciliation against the P&L/Realised-P&L statements (follow-up P2-18, uses the same detector); per-execution trades from the tradebook file without sides (replaced by the Dhan API read); splitting a bill line across lots by execution; XIRR changes.

## Acceptance criteria
**Detector and readers (pure; anonymised fixtures built from the real shapes — never the owner's files)**
1. Every shape listed in Scope 1 is recognised from its title line and headers; the header sets of the sample files are fixtures (names, client code, mobile and e-mail replaced). Unknown shapes answer 400 with the accepted kinds.
2. Global transaction report reader: all date formats seen (`dd Mon yyyy hh:mm:ss`, `Sept`), thousands separators, negative STT (a refund is kept as negative charge on that row, total ≥ 0 is not required per row), 7 charge columns, footer lines ignored, metadata lines skipped; buy-only, sell-only and mixed rows; the shapes of the sample rows parse (fixture of ≥ 40 anonymised rows covering the cases: ETF, BSE, `FUT`, `OPT`, MCX, mixed bill, name variants, a multi-bill day).
3. DP-charges reader (xlsx/xls): header row detection, ISIN/date/quantity/side/type/charge, total line and signature line ignored, ₹0 lines classified, off-market line classified; `.xls` fixture committed (a small synthetic BIFF8 file, no personal data).
4. Demat reader: free + locked-in + safe-keep + pledges summed as the depository quantity; `Valuation`/count footer ignored.

**Matching, charges, identity**
5. The sample name variants resolve to one ISIN each (`ORION STEEL LTD.` ↔ `Orion Steel`, `NOVA OXIDES & CHEM LTD` ↔ `…CHEMICALS LTD.`, `ZENITH ENERGY SYSTEM LTD`, `ARCADIA BEVERAGES LIMITED`); ambiguity is never guessed.
6. Value reproduction: quantity × price equals the bill value to the paisa for every row of the fixtures; charges equal the sum of the seven columns; per-file charge totals equal the report's footer "Total Charges" to ₹0.01 in a test over the anonymised fixtures.
7. Identity is stable: the same file again stores nothing (`alreadyImported`); a corrected file adds only what is new; importing after Undo stores again.

**Precedence (ADR 0029)**
8. Bill row vs API trades of the same (date, ISIN, side): skipped when the API group exists; voided with reason `superseded` when the API group arrives later; a different total is reported in both cases. Covered by application tests with fakes and one integration test with the real database.

**Behaviour**
9. Futures/options/MCX rows and unresolved/unlisted rows are counted per reason in the preview and the result. BSE rows with a held ISIN are imported.
10. DP lines of ₹0 and buy-side lines are listed, not stored; stored lines raise the account's costs in the P2-14 figures exactly once (dedupe key: account, date, ISIN, quantity, side, type, amount); a DP line that equals a ledger-API `charges` entry of the same account and day (same amount) is skipped as *already in the ledger*.
11. Demat check: counts of matching securities, quantity differences, held-at-Dhan-not-synced and synced-not-in-statement; none of them changes holdings; a notice item is raised when any exist (P2-16 notice shape when available).
12. UCC warning when the report's code differs from the account's Dhan client id; the metadata lines never appear in logs, audit text or responses.
13. Lot rebuild: imported trades follow ADR 0023 (rebuild by explicit preview step, `LotsRebuilt` audit; hand-dated lots are replaced only on the owner's confirmation) — the automatic rebuild of P2-13 does not run for imports.

**API and UI**
14. `POST /api/portfolio/imports/preview` (multipart `accountId` + one or more `files`) answers one result per file `{kind, supported, summary, warnings, ...kind-specific preview}`; `POST /api/portfolio/imports/import` imports the chosen files (digest per file) and answers batches; Undo and history reuse the trade, dividend and cost batch endpoints. Contract regenerated.
15. The Import screen shows every file card with its states (detected, needs a match, warnings, nothing new, error), shows the DP/demat/P&L kinds correctly, and redirects a dividend file to the dividend import; component tests with MSW for each kind.

## Contract
- **Data:** `portfolio.ledger_entries` gains `source` (`broker` | `import`), `batch_id`, `isin`, `quantity` (nullable); `portfolio.cost_batches` (or reuse a generic `import_batches` with a kind column — decide in the migration `AddReportImports`); `portfolio.security_aliases` (ISIN-keyed names learned from files); transactions: `source` gains `bill` for report rows (so precedence can tell them from `csv`), `superseded_by` nullable; no DELETE for the runtime role.
- **Package:** ExcelDataReader 3.7.0 (+ its code-page provider), pinned in `Directory.Packages.props`.

## Non-functional
Preview of a sample set of a few hundred rows per file ≤ 2 s. Personal header lines never leave the reader. Names are untrusted text.

## Test plan
Unit: detector, three readers, name resolver on the real name variants (anonymised), identity/dedupe, precedence rules, charge sums vs footers, FIFO realised P&L over the transaction fixtures reproducing the P&L-report fixture for a complete scrip (oracle test). Application: preview/import/undo for each kind, UCC warning, demat check, DP overlap with ledger. Integration: endpoints, migration, grants, API-sync-vs-import precedence. Architecture. Frontend: import screen states.

## Open questions
1. **Precedence** (criterion 8/ADR 0029): Dhan API trades replace imported bill rows. The alternative — keeping both and letting the owner choose — would double count unless handled by hand.
2. **The tradebook file** (no sides) remains unimportable; the report above plus the Dhan API read replace it. Executions missing from the report (a few date-and-scrip groups in the sample) come from the API read from an earlier date.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added
- [ ] Design docs/ADR 0029 updated (doc 12, 13)
- [ ] Owner's upload of their real exports: totals and counts match the local analysis in `data/dhan/` (not committed)
