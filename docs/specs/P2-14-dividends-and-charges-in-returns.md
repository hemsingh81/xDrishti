# P2-14 — Dividends and charges in returns

- **Status:** Approved (owner, 2026-10-14: "approved, continue", on the revised text after the research into Dhan's published API and support pages) · needs [P2-13](P2-13-dhan-trade-history-and-ledger-sync.md) built · decision [ADR 0026](../adr/0026-dividends-entered-by-the-owner-and-charges-in-net-returns.md)
- **Phase / roadmap row:** P2 — "XIRR, returns" (doc 12 §3: *Total return % = (realised + unrealised + dividends) / invested*; the "Not included in v1" notes of P2-05 and P2-13)
- **Design refs:** docs/design/12-portfolio-monitoring.md §3 (metrics, "Dhan sync (P2-13)"), §4, §9 · [ADR 0012](../adr/0012-holdings-acquisition-dates-and-returns-model.md) §4 · [ADR 0020](../adr/0020-portfolio-value-history-benchmark-and-charts.md) · [ADR 0024](../adr/0024-live-feed-xd-feed-and-live-prices.md) (live twins, golden fixtures) · [ADR 0025](../adr/0025-dhan-trade-history-and-ledger-sync.md)

## What the research found (Dhan docs and support, read 2026-10-14) and what it changes
1. **Dividends are probably not in the Dhan trading ledger.** Dhan's support says a dividend is *credited to the bank account* of the owner for shares held on the record date; the only documented ledger dividend entry is the transfer between lender and borrower of *rented* stocks. The documented ledger entries are funds added/withdrawn (`FUNDS WITHDRAWAL`, voucher `PAYBNK`), trade settlements and "other charges" (e.g. `DPC Interest : dd/mm/yyyy to dd/mm/yyyy` = delayed-payment charges). No narration for an ordinary dividend is documented anywhere. Dhan's app shows dividends under *Money → Dividends* but no API for it was found. **So the ledger cannot be the dividend source.** The earlier plan (match ledger narrations to holdings) is dropped; dividends are entered by the owner (below), and a `dividend`-class ledger entry, if one ever appears, is offered as a suggestion only.
2. **Dhan's trade rows carry the charges per trade** (`brokerageCharges`, `stt`, `sebiTax`, `serviceTax`, `exchangeTransactionCharges`, `stampDuty`, rupee amounts) and an `isin`. P2-13 had stored `charges = 0` for broker trades and dropped rows whose `tradingSymbol` is null (Dhan's own sample has it null) — fixed in a P2-13 follow-up before this story. **Trade charges are therefore known per trade and per holding**; only non-trade costs (DP charges, AMC, delayed-payment interest) come from the ledger, at account level. No double counting: trade charges come from trades, the ledger supplies only the `charges`-class entries that are not trade settlements.
3. **History depth is undocumented.** Dhan states no maximum date span and no limit on how far back the trade history or the ledger goes. The first read starts at the account's first-import date as today; the owner can start a read **earlier** from the sync dialog (the API already accepts an earlier `from`), and a refusal by Dhan is shown in plain words. The figures always say from which date they are complete.

## Decisions taken by the owner (2026-10-14), adjusted by the research
1. **Dividends** are held per holding, so they reach holding, group and total figures without guessing. Source: **entered by the owner** (manual form in the holding drawer and on the Cash & dividends panel; a dividend CSV import is a possible follow-up). At account level an entry may be *unallocated* (no holding chosen, e.g. a holding long since sold and removed).
2. **Charges** are a separate line in a **net** return: trade charges per trade/holding, ledger costs at account level. The cost basis, Dhan's average cost and the lots are **not** changed.

## Goal
The Portfolio screen shows what the portfolio really earned: next to the price-only (*gross*) return and XIRR it shows dividends received and costs paid, and a **net** total return and XIRR that include them — with the period the data covers stated, never guessed.

## Scope
**In:**
- `portfolio.dividends` (immutable except void, like transactions): account, holding (nullable), instrument/ISIN/symbol, India `pay_date`, `amount` (> 0, rupees received), optional per-share amount and note, `source` (manual), who/when, `voided_at/by`; add and void endpoints; audit events (counts only).
- Suggestions: a ledger entry of class `dividend` (if Dhan ever posts one) is listed as a suggested dividend the owner can accept (creating a manual dividend) or dismiss; nothing is added automatically.
- Income figures on the holdings summary (account / bucket / total scopes), holding rows and group rows: `dividends`, `charges` (trade charges; plus ledger costs at account and total scope), `unallocated`, `totalReturnPct`, `netXirr` and its state, `coverageFrom`.
- Benchmark XIRR and alpha stay **price-only**; the alpha basis text says "price return, before dividends and charges".
- The pure TypeScript twins (live recompute) take the same income flows so live tiles agree with a re-read; golden fixtures extended.
- Frontend: Dividends, Costs and Net XIRR in the summary tiles, holding drawer (list + add/void), group table; the Cash & dividends panel gains "Add dividend", the suggestions list and the earlier-start option for reading the ledger.

**Out (not in this slice):** dividend CSV import and any scraping of public dividend data (follow-up P2-15); automatic dividend estimates from corporate-action data; TDS and dividend reinvestment; charges inside the cost basis; a total-return index benchmark; splitting one payment across holdings; positions and intraday P&L (separate story).

## Acceptance criteria
**Dividends (storage and API)**
1. `portfolio.dividends` as above; `POST /api/portfolio/dividends` (account, optional holding, pay date not in the future, amount > 0, optional per-share and note ≤ 200 chars), `POST /api/portfolio/dividends/{id}/void`, `GET /api/holdings/{id}/dividends`, `GET /api/accounts/{id}/dividends` (newest first, voided flagged, paged). A holding must belong to the account; the same account/holding/date/amount twice is allowed (two genuine payments) but the second asks for confirmation (`409 portfolio.possible_duplicate`, repeat with `confirm: true`). Runtime role has no DELETE; migration `AddDividends`.
2. Suggestions: `GET /api/accounts/{id}/dividends/suggestions` lists ledger entries of class `dividend` not yet accepted or dismissed; accept creates the dividend (holding chosen by the owner or none) and links the entry; dismiss hides it. Nothing is created without the owner.

**Figures (pure calculators, table-driven; spreadsheet cases)**
3. For a scope and its period: `dividends` = Σ non-voided dividends in the scope (account scope includes unallocated; holding/bucket/sector/basket scopes only their holdings'); `unallocated` = dividends with no holding (account and total scope only; the group table shows them as an *Unallocated* row so account/bucket/total columns add up).
4. `charges` = Σ trade charges of the scope's non-voided transactions (CSV, manual and broker) **plus**, at account and total scope only, ledger entries of class `charges` **that name an account-level cost** (DP, AMC, delayed-payment / DPC interest, penalty, platform fee, pledge / payout / withdrawal charges, call & trade) dated within the ledger coverage. Trade-related phrases (brokerage, STT, stamp duty, SEBI, transaction charges, settlements, bills) are never summed. *(Amended, see below: a).*
5. **Total return %** = (realised + unrealised + dividends) ÷ invested, where invested is the cost of what is **still held** (open lots), as for the gross P&L %; realised profit is in the numerator, so for a partly sold position the figure can look high. **Net total return %** = the same minus charges. With no dividends and nothing realised, total return equals the gross P&L %; with no charges, net equals total. *(Amended, see below: e).*
6. **Net XIRR** = XIRR of the existing lot cash flows, plus dividend inflows and charge outflows on their dates, with the terminal value as today; same `insufficient` / `no_data` / `n_m` rules as the gross XIRR; the flows of holdings without a usable price are left out, as in the gross XIRR. A holding's net XIRR uses its own dividends and trade charges. It is worked out for the rows of a page only (*amended: c*).
7. Only data that exists is counted: dividends before the first dividend the owner entered are not invented; trade charges only for trades stored; ledger costs only from the ledger coverage start. The figures carry the dates of their parts (`dividendsFrom`, `tradeChargesFrom`, `ledgerChargesFrom`, `chargesFrom`) and `coverageFrom` = the **later** start of the sources that exist for the scope ("not entered yet" / "none read yet" when a source has nothing). *(Amended: b.)* A scope with no dividends and no charges shows gross figures only (no zero net row).
8. Gross figures, lots, invested, value, the value history and the benchmark are unchanged (regression tests on existing fixtures). Alpha stays price-only (alpha basis text updated).

**Live and golden**
9. The live twins take the `income` of the live-base read and move the two total return percentages with a pushed price; exact (`toBe`) equality with the server over the 150-seed property test and golden fixtures (schema 3) that now include dividends, trade charges, ledger costs and an unallocated dividend. **The net XIRR is not moved live**: it stays as the last read gave it until the 5-minute read. *(Amended: f.)*

**API contract**
10. `GET /api/holdings/summary`, `/api/holdings`, `/api/holdings/groups` gain an `income` object (`dividends`, `charges`, `ledgerCharges?`, `unallocated?`, `totalReturnPct`, `netTotalReturnPct`, `netXirr`, `netXirrState`, `coverageFrom`, `dividendsFrom`, `chargesFrom`, `tradeChargesFrom`, `ledgerChargesFrom`); null when there is nothing to show. `GET /api/holdings` also sorts by `dividends`; `GET /api/holdings/groups` also sorts by `dividends` and adds the `~unallocated` group. Regenerated with `scripts/gen-api.sh`.

**Ledger start (the earlier-start option)**
11. The Sync trades & ledger dialog offers *Read from an earlier date* (optional date; default unchanged). An earlier date re-reads and stores what Dhan returns; if Dhan refuses the range or has nothing that old, the run says so in plain words. The ledger's `coverageFrom` shown in the notes is the earliest ledger date stored.

**Frontend**
12. Summary tiles keep the gross figures and add **Dividends**, **Costs** and **Net XIRR** with "from <date>" notes; without any income data the tiles invite "Add a dividend" / "Read the ledger". The holding drawer lists its dividends with Add and Void; the Cash & dividends panel gains Add dividend and the suggestions list; group table and holding rows add a Dividends column (hidden below `md`). Component tests with MSW: nothing entered, dividends only, charges only, both, unallocated row, duplicate confirmation, void, suggestion accept/dismiss, earlier-start dialog, live tick keeps net figures equal to a re-read.

## Contract
- **Data:** `portfolio.dividends` (+ `portfolio.dividend_suggestions` state: ledger entry id → accepted dividend id / dismissed) ; migration `AddDividends`. No change to existing tables except none (suggestion state in the new table).
- **Audit:** `DividendAdded`, `DividendVoided`, `DividendSuggestionAccepted/Dismissed`: counts and ids only, no amounts in the event text.

## Non-functional
Summary and group reads stay within their budgets (income is one grouped query for the scope; no per-row queries). Notes and narrations are untrusted text (plain text, length-capped).

## Test plan
Unit: net XIRR / total-return calculators (known spreadsheet cases; no income equals gross; charges only; dividend before first lot; unallocated), charge aggregation (trade vs ledger, no double count). Application: add/void/duplicate-confirm, suggestions, summary/groups/rows with and without income, additivity with the Unallocated row. Integration: endpoints, migration, grants, audit without amounts. Live: twins vs server over the property test and golden. Frontend: states in 12. Documents: doc 12 §3/§4/§9, doc 13, ADR 0026.


## Amendments after the first review (2026-10-14)
The independent review of the first implementation changed the criteria above in six places (the ADR records the decisions: [ADR 0026](../adr/0026-dividends-entered-by-the-owner-and-charges-in-net-returns.md)). Behaviour is described here, not the wording of the screen, which may still change.
- **a. Ledger costs are account-level only (AC4).** Of the ledger entries of class `charges` only those that name an account-level cost count (`LedgerCosts.IsAccountLevel`); an entry with a trade-related phrase is never summed, even with an account-level phrase beside it; a doubt leaves the cost out. The class of an entry is not changed; a `dividend`-class entry stays a suggestion.
- **b. Coverage in parts (AC7, AC10).** The figures carry `dividendsFrom`, `tradeChargesFrom`, `ledgerChargesFrom` and `chargesFrom` (the later of the last two) and `coverageFrom` = the **later** start of the sources that exist; `ledgerCharges` shows the ledger part of `charges`. Earlier wording ("the earliest date") would have presented the net figures as complete from before a source begins.
- **c. Net XIRR is lazy (AC6, non-functional).** A page of `GET /api/holdings` works out the net XIRR for its rows only, after ordering and paging; ordering uses the cheap sums (`sort=dividends`). Income is a fixed number of statements: 6 per page, 7 when the page's holdings are not all the account's, and the ledger is never read for rows (ADR 0026 *Load*).
- **d. Enrich step (AC4, ADR 0025 amendment h).** A re-read completes broker trades stored earlier with the ISIN, ticker or charges they lack (counted as `enriched`, counts-only audit, never quantity, price, date or side, never CSV or manual trades), so charges and holding matches are right for trades read before this story.
- **e. AC5 wording.** Total return divides by the cost of what is still held, with realised profit in the numerator, and can look high for a partly sold position; with no dividends it equals the gross P&L % only when nothing was realised.
- **f. Net XIRR is not live (AC9).** The twins move the two return percentages; the net XIRR stays as last read.
- **Extra fields and behaviour.** `income` also has `ledgerCharges`, `dividendsFrom`, `chargesFrom`, `tradeChargesFrom` and `ledgerChargesFrom`; the `~unallocated` row appears only when neither a bucket nor a text narrows the scope; the group figures at the default status (open) leave out the dividends and trade charges of closed holdings that the summary tiles include.

## Open questions
1. **Where will your dividend data come from?** Dhan's app lists them under *Money → Dividends*; if it can export a report, a dividend CSV import (P2-15) is the natural follow-up. Until then they are typed in. Say if you want the CSV import in this story instead.
2. **Records before the ledger/trade coverage** are not in the net figures (stated, not guessed); the earlier-start read helps only as far as Dhan's history goes, which Dhan does not document.

## Follow-ups from the second review (minor, not blocking)
- Ledger cost phrases are incomplete until a real ledger is read: MTF interest, `DP CHRG`/`DP CHGS`, a bare demat or maintenance charge and a bare GST line are not counted yet; a DP/AMC line whose narration also says "settlement"/"bill" is left out on purpose. Tune `LedgerCosts` / `LedgerClassifier` after the first real read.
- `CashDrawer` still says the ledger's charges count in the net return; only account-level costs do — reword.
- `invalidateBrokerHistory` does not refresh the dividend suggestions list when a run ends with the Cash panel open.
- Total return % and net XIRR use different bases in a scope that mixes priced and unpriced holdings (dividends and charges of unpriced holdings are in the percentage, not in the XIRR); add a mixed-case test and state the rule.
- Every row with income computes a net XIRR on the live-base read (it is shown only in the drawer); ADR 0024's "full XIRR for its rows" wording needs the same sentence. Add a statement-budget test for `GET /api/holdings` with dividends.
- `perShare` is shown with 2 decimals though stored with 4; an ISIN added by the enrich step can stop a symbol-matched trade matching a holding whose ISIN differs (corporate-action ISIN change) — note in ADR 0025 amendment h.

## Done when
- [ ] All acceptance criteria have evidence
- [x] `scripts/check.sh` green; reviewer agent: no blockers/majors (second pass, 2026-10-15)
- [x] Contract regenerated (`scripts/gen-api.sh`); migration added
- [x] Design docs/ADR 0026 updated (doc 12, 13, 05, 18; ADR 0024, 0025, 0026)
- [ ] Owner's spreadsheet check: net XIRR of one account matches within rounding
