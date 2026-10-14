# 0029 — Dhan's downloaded reports are imported by kind, and API trades take precedence over bill-level report rows

- **Status:** Accepted (owner, 2026-10-15: supplied the reports and asked for the system to be updated — [P2-17](../specs/P2-17-dhan-report-imports.md)) — builds on [ADR 0023](0023-trade-import-and-lot-reconstruction.md), [ADR 0025](0025-dhan-trade-history-and-ledger-sync.md), [ADR 0026](0026-dividends-entered-by-the-owner-and-charges-in-net-returns.md) and [ADR 0028](0028-dividend-csv-import-with-name-matching.md)
- **Date:** 2026-10-15

## Context
A set of Dhan downloads (analysis in `data/dhan/reports/ANALYSIS.md`): the global transaction reports (bill-level trades with side and all charges), DP-charges sheets (legacy `.xls`), a demat holding statement (ISINs), and the Realised P&L / P&L statements. No single file is complete: against the owner's earlier tradebook, some date-and-scrip groups are missing from the transaction reports and some are only there; the depository quantity differs from the net of the trades for a few securities (IPO allotments, demergers, transfers, missing early buys). The transaction-report fees add up exactly to each report's "Total Charges"; first-in first-out over its rows reproduces Dhan's realised P&L for most cash scrips.

## Decisions
1. **One detector, many kinds.** Files are recognised by title line and headers (`.csv`, `.xls`, `.xlsx`): global transaction report, DP charges, demat holding, realised P&L, P&L, dividend payout, trade file with side, tradebook without side. A file of a kind that belongs on another screen is routed there; kinds that are statements of results (the P&L reports) are recognised and not imported.
2. **The global transaction report becomes trades** through the existing preview/import/Undo flow: side from the buy or sell quantity, price = value ÷ quantity, charges = the seven charge columns, identity from bill number, date, normalised name and side. Futures, options and MCX rows are counted and skipped. Bill-level rows are marked `source = bill` so they can be told from CSV trades.
3. **ISIN comes from names** the app already knows: aliases, holdings, the instrument master, and names learned from a demat-holding or DP-charges file; unresolved names are chosen once and remembered.
4. **A BSE row is imported when its ISIN is one the account holds**; otherwise the existing rule (skip) applies.
5. **DP charges are account costs**: stored as ledger entries of class `charges` with `source = import`, counted by the P2-14 net figures once; a line equal to a ledger-API charge of the same day is skipped.
6. **The demat holding statement never changes holdings** (the broker API stays authoritative, ADR 0012); it teaches names and produces a comparison with the synced holdings, raised as a notice.
7. **Precedence:** per (account, trade date, ISIN, side) a group of Dhan-API trades beats an imported bill row. An imported row whose group already exists from the API is skipped (*covered by Dhan*); when the API later stores that group, the bill row is voided (*superseded*). Total differences are shown, not hidden. This prevents double counting without asking the owner to choose.
8. **Personal header lines** (name, client code, mobile, e-mail) are skipped by the readers; the client code is only compared with the account's Dhan client id for a warning.
9. **Spreadsheet reading uses a pinned library** (ExcelDataReader, MIT) because the owner's DP-charges files are legacy `.xls`.

## Consequences
- The owner can load years of trades with sides and charges from files, and the Dhan API read fills what the files miss; the two never count a trade twice.
- Bill-level rows give one average price per scrip per day, so lot costs on days with several fills differ slightly from execution level; quantity and total value are exact.
- Futures, options and commodity trading (a large part of the owner's 2023-25 activity) stays outside the app's figures.
- A reconciliation of lot reconstruction against Dhan's realised P&L is possible later with the same detector.
