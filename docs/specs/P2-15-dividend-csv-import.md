# P2-15 — Dividend CSV import (Dhan "Dividend Payout" format and similar files)

- **Status:** Approved (owner, 2026-10-15: asked for the feature with a Dhan dividend payout export as the sample: "if user upload in this format file then it should automatically handle by application") · decision recorded as [ADR 0028](../adr/0028-dividend-csv-import-with-name-matching.md) (an amendment of [ADR 0026](../adr/0026-dividends-entered-by-the-owner-and-charges-in-net-returns.md)) · **amended while building** (identity without an occurrence index, master matched by equality only, containment for holdings only — ADR 0028 amendments a, b)
- **Phase / roadmap row:** P2 — the "dividend CSV import (P2-15)" follow-up of P2-14
- **Design refs:** docs/design/12-portfolio-monitoring.md §3 ("Dividends, charges and net returns (P2-14)", "Trades and the rebuild of lots" for the preview/import/undo pattern) · [ADR 0023](../adr/0023-trade-import-and-lot-reconstruction.md) · [ADR 0026](../adr/0026-dividends-entered-by-the-owner-and-charges-in-net-returns.md)
- **Sample and analysis:** `data/dhan/dividends/` (git-ignored: original file, normalised copy, `ANALYSIS.md`)

## Goal
The owner drops a dividend payout file on the Cash & dividends panel; the app recognises the format by itself, matches the scrip names to holdings (remembering the owner's corrections), shows what would be added, and imports it with Undo — so dividends of a whole history need no typing, and the same file again adds nothing.

## What the sample file is (analysis)
`Date, Scrip Name, Dividend Per Share, Quantity, Dividend Paid` — a few hundred rows over a couple of years; names only (no symbol/ISIN); dates `dd Mon yyyy` with `Sept`; amounts may have thousands separators; per share × quantity = paid on every row; many names match a current holding and many are stocks not held now; a row may be repeated identically (e.g. Orion Steel on one day); a company may have two different payments the same day (e.g. 5 and 4.5 per share); some rows are dated before the account's default first-import date.

## Scope
**In:**
- **Format detection** (pure): a dividend file is recognised by headers — a date, a scrip name (or symbol/ISIN) and a paid amount (`Dividend Paid`, `Amount`, `Net amount`, `Payout`…); optional per-share and quantity columns. Header aliases are case/punctuation-insensitive. The same detector is used by the trade import: a dividend file dropped on the trade import is recognised and the owner is told, with a button to the dividend import; a tradebook with no side column (like a sideless Dhan tradebook export) is explained instead of rejected cryptically.
- **Reader** (pure, table-driven): dates `dd MMM yyyy` incl. `Sept`, `yyyy-MM-dd`, `dd-MM-yyyy`, `dd/MM/yyyy`, `dd-MMM-yyyy`; numbers with `,` thousands and `.` decimals; quantity whole, per share ≥ 0, paid > 0; future dates are bad rows; limits 10 MB / 20,000 rows; bad rows listed (≤ 200) and never imported.
- **Name matching** to the chosen account's holdings, in this order: (1) a remembered alias (`portfolio.dividend_aliases`: normalised file name → holding or instrument, saved when the owner confirms or corrects a match); (2) ISIN or symbol columns when present; (3) normalised-name equality with an open holding (case, punctuation, `&`/`and`, `Ltd/Limited/Corporation/India/Industries` ignored); (4) a **unique** containment match with an open holding, only when the shorter name has **at least two significant words**; (5) the instrument master (NSE shares/ETFs) by **equality** of normalised name, ISIN or symbol only (never containment) → the dividend keeps instrument/ISIN/symbol but no holding; (6) otherwise the dividend is imported **unallocated with the file's name as text**. Ambiguous matches are never guessed: they are listed for the owner to choose.
- **Preview → Import → Undo** exactly like trades: nothing is stored by the preview; the import stores one batch (`portfolio.dividend_batches`: file name, SHA-256, counts, status, who/when), dividends get `source = csv`, `batch_id`, `external_id` (identity) and `quantity`; undo voids the batch's dividends.
- **Identity and duplicates:** identity = hash(account, pay date, normalised name, per share, quantity, amount) — *amended: no occurrence index; see ADR 0028 amendment a* — so the same file again adds nothing and a corrected file adds only what is new. **Identical rows inside one file collapse in the reader and count once** (the preview lists them: "Orion Steel on 03 Jul appears twice — counted once; add the second by hand if it is real"). A row that equals an already stored manual dividend (same account, date, amount, holding) is a *possible duplicate*: listed, skipped by default, importable by an explicit tick.
- **Checks shown in the preview:** per-share × quantity ≠ paid (by more than ₹0.01 — the paid amount wins), dividends dated **before the account's first lot** (net XIRR cannot treat an inflow before the first outflow → says "read earlier trade history"), names not matched, ambiguous names, totals by year, count and sum to be added.
- Frontend: **Import dividends** in the Cash & dividends panel (file picker + drop zone, account chosen by the panel), the preview (summary tiles, tables: to add / already imported / possible duplicates / needs a match / bad rows; a holding picker per unmatched or ambiguous name, "remember this match" ticked by default), the result with Undo, and the import history; Dividends list shows a "CSV" chip, quantity and the file name for unallocated rows.

**Out (not in this slice):** reading the owner's tradebook (no side; see P2-16 notes), TDS, dividend reinvestment, splitting a payment across lots, auto-matching by record-date quantity, other brokers' dividend formats beyond the header aliases.

## Acceptance criteria
**Detector and reader (pure; committed sample files, anonymised fixtures built from the owner's file shape)**
1. The sample header `Date,Scrip Name,Dividend Per Share,Quantity,Dividend Paid` is detected as a dividend file; a file with `Order Type`/`Segment` and no side is detected as a "tradebook without sides" and explained; a trade file with a side column is detected as trades; anything else is unknown (400 with the accepted names).
2. Dates `13 Jun 2024`, `23 Sept 2024`, `2024-06-13`, `13-06-2024`, `13/06/2024`; amounts `"1,134.00"`; whole quantities; per share ≥ 0 or absent; paid > 0; a future date, an empty name, a cell longer than 64 characters (date/number) or an unreadable number is a bad row (line, reason; ≤ 200 listed, all counted). UTF-8 with or without BOM; ≤ 10 MB / 20,000 rows.
3. An anonymised fixture shaped like the sample (thousands separators, `Sept`, one identical repeat, two payments of one company on one day) parses with the expected row count, total, 0 bad rows and 1 identical repeat; per share × quantity = paid on all its rows.

**Matching (pure, table-driven; fixtures from the sample names)**
4. Normalisation: case, punctuation, `&`↔`and`, corporate suffixes and `Limited/Ltd` ignored; `Sept` is not part of names. Exact normalised equality beats containment; containment applies to **open holdings only**, only when the shorter name has at least two significant words, and counts only when exactly one open holding fits; two fits = ambiguous (not applied). *(Amended: ADR 0028 amendment b.)*
5. Aliases win over everything and are scoped to the account; saving an alias is explicit (preview choice) and audited; an alias never changes already imported dividends. A choice is one of holding, instrument or *none* (keep unallocated). *(Amended: ADR 0028 amendment e.)*
6. A name (or ISIN/symbol) that equals exactly one NSE share/ETF in the master but no holding → instrument/ISIN/symbol set, holding null; a partial name that would need containment, or no match at all → unallocated with the name as text. Against a database that holds the current holdings, names that match a holding are allocated and the others are unallocated (counts shown, not asserted on the owner's data). *(Amended: ADR 0028 amendment b.)*

**Storage and API**
7. `portfolio.dividend_batches`, `portfolio.dividend_aliases`; `portfolio.dividends` gains `batch_id`, `external_id` (unique per account among non-voided rows), `quantity`, `scrip_name` (the file's text for unallocated), source `csv`; migration `AddDividendImport`; no DELETE for the runtime role.
8. `POST /api/portfolio/dividends/preview` (multipart `accountId` + `file`; read-only) → `{format, file {rows, parsed, bad[], identicalRepeats[], …}, items[{line, payDate, name, perShare, quantity, amount, status: new|imported|possible_duplicate|needs_match|ambiguous, match {kind: alias|isin|symbol|name|contains|master|none, holdingId?, instrumentId?, candidates[]}, warnings[]}], totals {count, amount, byYear}, beforeFirstLot {count}, digest}`.
9. `POST /api/portfolio/dividends/import` (same body + `choices[{name, holdingId|instrumentId|none, remember}]`, `includeDuplicates[]`, `digest`) stores new rows as one batch, saves remembered aliases, answers the batch; `409 portfolio.preview_stale` when holdings/aliases/dividends changed since the preview (the digest); `409 portfolio.sync_running` is not needed. `GET /api/portfolio/dividend-batches`, `POST /api/portfolio/dividend-batches/{id}/undo` (voids the batch's dividends; twice = no-op). Audit events `DividendsImported`, `DividendsUndone`, `DividendAliasSaved` carry ids and counts only.
10. Dividends from a batch appear in the figures exactly as manual ones (P2-14): allocated ones in their holding, bucket and sector groups, unallocated at account level; voiding or undoing removes them; caches dropped by the unit of work.
11. Re-importing the same file stores nothing (`alreadyImported`); importing it after undo stores it again.

**Frontend**
12. The Cash & dividends panel shows **Import dividends**; the wizard shows the detected format, the summary, the tables of criterion 8 with a holding picker per unmatched/ambiguous name (type-ahead over the account's holdings and the master, "remember this match"), the pre-first-lot warning with a button to the earlier-history read, the confirm step, the result with Undo, and the history. A dividend file dropped on the trade import shows the "this is a dividend file" redirect; a tradebook without sides shows the explanation. Tests with MSW for every state.

## Contract
- **API / data / UI:** as above; contract regenerated (`scripts/gen-api.sh`).
- **Jobs:** none.

## Non-functional
Preview of 20,000 rows ≤ 5 s (name matching uses an in-memory index of the account's holdings and the master names, no query per row). No file content, names or amounts in logs or audit text (counts only). Names are untrusted text (plain text everywhere).

## Test plan
Unit: detector, reader (sample file, formats, bad rows), normaliser and matcher (every rule, ambiguity, alias precedence), identity/dedupe (identical repeats, two payments of one company on the same day, re-upload, a golden test that pins the identities), checks (amount mismatch, before first lot). Application: preview/import/undo/aliases with fakes; digest staleness. Integration: endpoints, migration, grants, audit. Architecture. Frontend (MSW): wizard states, redirects, alias choice, undo, history.

## Open questions
1. The file has no account column: one upload goes to the account chosen in the panel. If you hold several Dhan accounts, upload one file per account.
2. Whether Dhan's own "dividend payout" page can export this file directly is unknown; the app reads whatever file has these columns.

## Follow-ups from the second review (minor, not blocking)
- A second concurrency conflict in an import or undo answers the generic "holding was changed meanwhile" text; give dividend flows their own message (`PortfolioUnitOfWork.RunAsync` / `PortfolioErrors.HoldingChanged`).
- `DividendUploads.Lines`: cap each `includeDuplicates` value and stop at `MaxRows` before parsing the rest.
- ADR 0028 and doc 12 still describe a success response `report` and omit the `statement` kind and the plain-Amount rule in two places; amendment i should say "dividend, per share, payout or DPS".
- Toast `returnTo` can go stale after a toast times out with focus elsewhere (clear it when no open toast holds focus). The audible-above-modals hook exposes the toast to screen readers while a modal is open but a focus trap keeps it out of Tab reach (Ctrl/Cmd+Z remains); check with VoiceOver and consider a permanent live region.
- NFC/NFD forms of one name give different keys (accented names only). A DP-charges file dropped on the dividend import gets the "import it as trades" refusal; P2-17 replaces it.
- Display precision of per-share amounts (4 stored, 2 shown) is unchanged from P2-14.

## Done when
- [ ] All acceptance criteria have evidence
- [x] `scripts/check.sh` green; reviewer agent: no blockers/majors (second pass, 2026-10-15)
- [x] Contract regenerated (`scripts/gen-api.sh`); migration added
- [x] Design docs/ADR 0028 updated (doc 12, 13)
- [ ] Owner's upload of their real export: totals and counts match the local analysis in `data/dhan/` (not committed)
