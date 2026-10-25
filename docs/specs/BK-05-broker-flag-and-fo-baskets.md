# BK-05 — Broker flag and F&O baskets

- **Status:** Draft (written 2026-10-25 under the owner's delegation; decisions below researched; owner's Approved needed before build)
- **Phase / roadmap row:** BK — system baskets, slice BK-05 of [BK-00](BK-00-system-baskets-plan.md)
- **Design refs:** docs/design/06-instruments-and-baskets.md §2.1, §2.3 (flags: F&O eligible, ASM/GSM, MTF leverage, lot size, freeze quantity, retired) · docs/design/05 (Dhan instrument master) · research [system-baskets-nse](../research/system-baskets-nse.md) §1.3, §1.4 · [basket-leverage-uses](../research/basket-leverage-uses.md) §3 · [ADR 0014](../adr/0014-portfolio-buckets-appear-as-system-baskets.md)
- **Needs:** [BK-01](BK-01-index-basket-catalogue-fetcher-and-versioned-membership.md) (columns, protection, fetcher, guards), the instrument-master service (VERIFIED `InstrumentMasterService`, daily 08:45 IST).
- **Size:** 3 agent-days (flag-derived list baskets inside the master run 1, F&O file parsers, ban list, lot-size cross-check 1.5, tests and ADR 0.5)
- **Safety:** yes. Two NSE file fetches (`fo_secban.csv`, `fo_mktlots.csv`) are **OFF** until the owner enables them (same gate as BK-01: terms read, ADR). The Dhan-derived lists add no network call. These lists are information and exclusion filters; **no order** and no margin is computed or assumed. Margin multipliers and the 20 % floor are never constants (leverage note §4.10).

## Goal
Regulatory and broker lists (F&O stocks, ASM/GSM surveillance, MTF-eligible, MIS-eligible when the master carries it, SME/trade-for-trade series, the daily F&O ban list) and the F&O lot sizes become dated, read-only list baskets that scans, the plan and reports can read as "usable now / excluded now".

## Scope
**In:** list baskets derived from the stored Dhan master fields inside the master run; the NSE F&O ban file parser and basket; the NSE lot-size file parser, a dated lot-size store and a cross-check against the master; per-list dated membership via BK-01 tables; owner-act fetch now; tests and ADR.
**Out:** MIS leverage multipliers or margin amounts (broker-specific, P12); the NSE ASM/GSM web pages or JSON APIs (cookie-gated, rejected); `sec_list.csv` price-band baskets and `EQUITY_L.csv` (candidates for a later slice, named in Decisions); any "bad stock" judgement; any order or margin logic; the screen (BK-04 renders `kind = list`).

## The baskets
| Basket | Source | Network | Rule | Cadence |
|---|---|---|---|---|
| F&O stocks | Dhan master flag (stock futures present) | no | F&O-eligible flag = true and type share (213 underlyings on 2026-10-11 per doc 06) | with the master run |
| Surveillance: ASM/GSM | Dhan master `AsmGsmFlag` != `N`, category | no | flag set | with the master run |
| MTF-eligible | Dhan master MTF leverage field | no | `MtfLeverage` > 0 (field name UNVERIFIED in storage) | with the master run |
| MIS-eligible | Dhan master intraday-leverage field if the stored master has one | no | field present and > 1; if the field is not stored, the basket stays `unavailable` (not faked) | with the master run |
| SME / trade-for-trade | series in `BE, BZ, SM, ST` from the master | no | series match | with the master run |
| In F&O ban | `https://nsearchives.nseindia.com/content/fo/fo_secban.csv` | yes (OFF) | members listed for the trade date in line 1 | 19:00 IST and 08:30 IST |
| F&O lot sizes | `https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv` | yes (OFF) | not a basket: a dated table plus a cross-check report | 08:50 IST (after the master) |

## Acceptance criteria
1. Given the master run at 08:45 IST on 2026-10-26 flags 213 stock-futures underlyings and the "F&O stocks" basket had 212 open members, when the run completes, then the one added instrument has `valid_from = 2026-10-26`, `provenance = derived`, `source_version_id` = the master refresh id; a flag turned off sets `valid_to` to that date; instruments retired are closed, never deleted.
2. Given `AsmGsmFlag` values `N`, `Y`, `S` (anything but `N` counts) for three instruments, then the ASM/GSM basket contains the two non-`N` ones with the category stored as the member `note`, and an instrument moving from `N` to a flag gets a new row dated that refresh day (so a past date's membership is what was flagged then, point-in-time from the first recorded refresh).
3. Given no stored intraday-leverage field, then the MIS-eligible basket exists with state `unavailable` and reason "the stored master has no intraday leverage column", with zero members and no fabricated list; given the field, members are those with value > 1 and the multiplier is not displayed as a promise (label "broker-reported, may change").
4. Given `fo_secban.csv` with first line `Securities in Ban For Trade Date 26-OCT-2026:` and rows `1,AMBUJACEM`, `2,KAYNES`, `3,SAIL`, when fetched (enabled), then 3 members are matched by symbol to share instruments, with `valid_from = 2026-10-26` (the trade date in the file, an NSE-stated date, `provenance = fetched`), and the previous day's list closes with `valid_to = 2026-10-26`; given a file with the first line only, the basket is empty and valid (not a rejection); given a first line without a parseable date, `rejected`.
5. Given the ban list fetched twice the same evening (19:00 and 08:30 next morning, same bytes/sha256), then the second is `unchanged`; given a symbol not in the master, it counts as unmatched and is listed in the fetch record.
6. Given a ban-list member that is also in an open plan, then nothing here changes the plan (the basket is an input; consumers decide), and the BK-04 card shows "Excluded from new positions: ban period" text taken from the basket description.
7. Given `fo_mktlots.csv` with header `UNDERLYING,SYMBOL,OCT-26,NOV-26,DEC-26,...`, space-padded cells, and a first row `NIFTY BANK, BANKNIFTY, 30, 30, ...`, then every cell is trimmed, index rows (the SYMBOL is not a share in the master) are skipped and counted, and for each share row the nearest-expiry column is the lot size, stored dated by the fetch IST date.
8. Given a lot size that differs from the master's stored lot size (master 250, NSE file 275), then the dated table gets the NSE value, a `lot_size_mismatch` warning is written to the fetch record naming the symbol and both values, and the master value is **not** overwritten (the master is primary); given equal values, nothing is written beyond the dated row if changed.
9. Given the F&O universe cross-check, when the NSE file has 212 equity underlyings and the master has 213, then the fetch record carries `fo_universe_diff { onlyMaster: 1, onlyNse: 0 }` and a warning; the basket membership stays the master-derived one (no auto-correction).
10. Given the parser, then a 404 HTML body, wrong header, a non-numeric lot size or an empty file is `rejected`, nothing applied; `fo_mktlots.csv` rows below 100 or above 400 share underlyings (bounds `expected_min/max` in `basket_sources`) are `rejected`.
11. Given `Baskets:System:Lists:FetchEnabled` OFF (default), then neither NSE file is requested (counting handler = 0) while the five Dhan-derived lists still refresh with the master; given the master run fails, the lists keep the previous membership and the source status shows `failed`.
12. Given a change route on any list basket, then 409 `baskets.system`; copy out is allowed; the lists appear in `GET /api/baskets` with `kind = list`.
13. Given membership of the Dhan lists on a past date before the first recorded refresh, then the API serves the first known membership and sets `membersKnownSince` (as BK-01 AC 11); it never claims the flag history before our first refresh.

## Contract
- **Data / migration** `AddListBaskets` (after BK-01): seed list baskets (`kind = list`, `system = true`) and their `basket_sources` rows (Dhan ones with `source_url` null, `enabled` true because no network; NSE ones `enabled = false`); `market.fo_lot_sizes(instrument_id fk, effective_from date, lot_size int check > 0, expiry_label text, fetch_id fk, primary key (instrument_id, effective_from))`, insert-only for `xd_app`. `basket_fetches` (BK-01) reused for the two NSE files.
- **Application:** a pure `FlagListBuilder` (master rows -> member sets per list), `BanListParser`, `LotSizeParser` (Domain, pure), `ListBasketApplier` reusing BK-01's `IBasketMembershipApplier` and `FetchGuard`; the master service calls the builder after its own commit (same transaction boundary as the master write is not required; a failure there is `failed` for the lists only, never rolls back the master).
- **API:** none new beyond BK-01 (`GET /api/baskets`, `/members?asOf=`, `/source`, `POST /api/baskets/system/fetch` accepts the two NSE list ids). `GET /api/instruments/{id}` may later show the dated lot size (out of scope; the table exists).
- **Settings** (`Baskets:System:Lists`, `[Range]`, `ValidateOnStart`): `FetchEnabled` false; `BanRunTimes` 19:00 and 08:30 IST; `LotSizeRunAt` 08:50 IST; `FoUnderlyingBounds` 100..400; `UserAgent` shared with BK-01.
- **Events/jobs:** Dhan lists inside `instrument-master` (08:45 IST daily, VERIFIED); NSE lists in the `system-baskets` service of BK-01 with their own times; idempotent by sha256; one run at a time.

## Non-functional
Dhan lists add under 200 ms to the master run (set arithmetic on about 120k rows already in memory: the master has far more rows than listed shares, filter first). Two small GETs per day plus one retry at most per file; backoff as BK-01. Log events `basket.list` (basket, outcome, members, unmatched, ms), no symbols in logs or audit.

## Test plan
- **Unit (Domain):** `FlagListBuilder` over a table of flag values; `BanListParser` (three rows; first line only; bad date; BOM; blank lines; duplicate symbol); `LotSizeParser` (padded cells, index row skipped, non-numeric value, expiry columns varying in count); mismatch and universe-diff calculators.
- **Application (fakes):** disabled makes zero calls; master failure keeps previous; unchanged by sha256; unmatched symbol path.
- **Integration (Testcontainers):** master run with a fixture that flips one F&O flag and one ASM flag (dates as in AC 1 and 2); ban list on two consecutive trade dates (close/open boundary day); lot-size mismatch warning; 409 routes; `xd_app` cannot update `fo_lot_sizes`; contract snapshot.
- **Architecture:** no order-named member; no margin amount type.

## Decisions (researched)
1. **Use the Dhan flag as primary for F&O, ASM/GSM and MTF; NSE files are an independent check and a source of the ban list and lot-size dates.** Reason: the flags are already in the daily master (no new fetch), the NSE ASM/GSM CSVs tried were 404 and the NSE web pages need cookies (UNVERIFIED, not recommended). Rejected: scraping NSE surveillance pages; trusting the NSE file over the master for membership. Source: research §1.3, §1.4 (candidates `eq_asm_list.csv`, `gsm_list.csv`, `surveillance_list.csv` returned 404 on 2026-10-25).
2. **The ban list is a `list` basket dated by the trade date in line 1, empty is valid.** Reason: VERIFIED format; published in the evening for the next day (`last-modified` seen 2026-10-23 13:11 GMT); an exclusion filter, not a tradable universe. Rejected: treating an empty file as a failure (would stall on a quiet day). Source: research §1.3.
3. **Lot sizes stored dated; master stays primary; mismatches are warnings.** Reason: NSE revises lots by circular and the file changes on the day of effect; an automatic overwrite from a second source could silently change sizing in later slices. Rejected: auto-sync; ignoring NSE. Source: research §1.3; doc 06 §2.3.
4. **MIS-eligible only if the stored master has the column; otherwise `unavailable`.** Reason: MIS lists are broker-specific; the exact Dhan column names (`MIS_LEVERAGE`, `BUY_CO_MIN_MARGIN_PER` style) are UNVERIFIED and must be checked against the stored master columns at build. Rejected: a hard-coded list or multiplier. Source: research §1.3; leverage note §4.10 ("no unreviewed regulatory numbers in code").
5. **Run the NSE lot-size job after the master (08:50), not 08:40.** Reason: the research placed the master at 08:20 but the code's default is 08:45 (VERIFIED in `InstrumentMasterService.cs`); the cross-check needs the fresh master. Rejected: keeping 08:40 (compares to yesterday's master). Source: repo code.
6. **Price-band and SME file baskets deferred.** `sec_list.csv` (band 2/5/10/20/No Band, VERIFIED 3,599 lines) would give an illiquid-stock exclusion list, and `EQUITY_L.csv` gives `DATE OF LISTING` for a "listed for N days" rule; both are cheap but add two more network sources. SME is derived from series in the master instead (no download). Rejected: including them now (scope and licence surface). Source: research §1.4.
7. **Lists are point-in-time only from our first recorded refresh.** Reason: no history source; same honesty as BK-01. Source: research §3.1.
8. **No margin or leverage numbers are computed.** MTF/MIS membership says "the broker reports it"; amounts come from the broker in P12 or a confirmed fact row. Source: leverage note §3, §4.10.

## Risks and honest limits
- Dhan field names and presence are unverified in storage; two baskets may be `unavailable` at first.
- The ban file lists names only after the exchange publishes (evening); a morning gap is possible if the 19:00 fetch failed (the 08:30 retry covers it).
- Index rows in `fo_mktlots.csv` are skipped; a new equity underlying not yet in the master is `unmatched`.
- Licence of the NSE files is unread (same gate as BK-01).

## Open questions
1. Enable the two NSE list fetches with the BK-01 permission, or Dhan-only first? (suggest Dhan-only first: they need no network.) 2. Does the owner want MIS/MTF as baskets now or wait for P12 (BK-00 q5)? 3. Add the `sec_list.csv` price-band exclusion list later?

## UNVERIFIED
Stored names of the Dhan master columns for MTF leverage and any MIS/intraday multiplier; the `ASM_GSM_FLAG` value set beyond `N`; NSE terms for `fo_*` files; whether `fo_mktlots.csv` keeps its header and index-first layout; the F&O underlying count today (213 is from doc 06 as of 2026-10-11); `IScheduledService` ordering of the 08:50 job after the 08:45 master (only the schedule types were seen); the master-refresh id usable as `source_version_id`.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added
- [ ] ADR (list baskets and NSE list fetch gate) and doc 06 §2.3/§3.1 updated; `scripts/build-docs.sh`

## Review fixes 2026-10-25
- **Category note:** ASM/GSM category is stored in `basket_members.note` (exists in the entity config, `BasketLimits.MaxNoteLength`); BK-01's migration confirms the column and BK-05 adds no side table.
- **MIS-eligible:** the stored master has no intraday-leverage column (only `MtfLeverage`), so the basket ships `unavailable` and is **dropped from the first release's seeds** (a test asserts the basket does not appear with members; the unavailable state is shown only in the Settings list). Revisit when a column exists.
- **MTF test:** `MtfLeverage` is decimal(12,6): member if `MtfLeverage > 0`; null and 0 both mean not eligible (tested). No multiplier is shown.
- **Ban list dating:** a fetch at 19:00 IST writes `valid_from` = the trade date in line 1 (the next trading date, as the file states); the 08:30 fetch of the same bytes is `unchanged`.
- **Schedule:** two daily times (19:00 and 08:30) are not in the seen `ServiceSettings`/`Schedule.Daily(one time)`; whether `IScheduledService` allows two times is UNVERIFIED: fallback is two service entries (`system-baskets-ban-evening`, `-morning`) or `Schedule.After`. BK-01's single `RunAt` 07:30 covers index files only.
- **AC7:** nearest expiry = the first expiry column whose month-end date is on or after the fetch IST date.
- **Bounds:** `expected_min/max` (100..400 for the lots file, ban 0..200) live only in `basket_sources` (the setting `FoUnderlyingBounds` is removed).
- **Column name:** `source_ref` + `source_kind = 'refresh'` for master-derived lists (replaces `source_version_id`).
- Size stays 3 d.
