# P3 hand check — the protocol and the record

**Since 2026-10-21 the exit criterion of P3 is the automated gate (section 12, ADR 0048); the manual protocol below stays valid as optional extra evidence.** The original text of this file: the exit criterion of P3 is the owner's: about 20 trades drawn at random, balanced over strategy, instrument and year, plus one trade of each edge category that occurs, each checked against a chart by hand. This file holds the protocol (fixed in advance so that the check is not a matter of taste) and, at the end, the **record** of every pack that was drawn. The tool is `scripts/verify-pack.sh` ([ADR 0046](../adr/0046-the-verification-pack-and-the-20-trade-check.md), [spec P3-09](../specs/P3-09-verification-pack-and-the-20-trade-check.md)).

**Status of the values below.** They are the recommendations of the spec (the owner's Q6, protocol, and Q7, data). They are not final until the owner confirms them in chat before the first draw. **The record holds no price, no R value and no count of any group** (roadmap, *What a P3 number means*: no number from a real-data run is quoted outside its run folder); the trade keys name the owner's symbols, so whether this repository may hold them is the owner's call.

**Confirmed by the owner in chat (2026-10-19, "yes to all recommendations").** Seed `p3-check-1`. Hold-out: the last months are kept out, so `--to 2025-10-31` (`--from 2024-04-01`). Round A first (ORB v1, modelled fills, `--gross`); Round B (`VWAP_PULLBACK`, `BASE_BREAKOUT`) is a second pack with another seed; closing P3 on ORB alone is not taken. External chart: TradingView 1-minute, bar-start labels in India time, at least 5 rows. Instruments: the whole stored set (basket name still to be given by the owner, otherwise a `--symbols` list).

Nothing in a pack is evidence of an edge. The check says that the mechanics checked match a chart. It does not say that the engine is free of defects, and it says nothing about whether a strategy works.

## 1. Before the draw

1. Write the **seed** here (below, in the record) and the **window** (`--from`, `--to`) before running anything. The seed is 1 to 40 characters of letters, digits, `.`, `_` and `-`; the tool has no default and no clock or machine value in the draw. Suggested names: `p3-check-1`, then `p3-check-2` after the first fix. A seed tried repeatedly until the sample looks easy defeats the check, so **every seed ever used stays in the record**, and a seed is never reused.
2. Decide the **hold-out** (Q7). If the last months are to be kept untouched until the P5 verdict (for example from 2025-11-01), give the verb `--to` before them: looking at the trades of a month is looking at the month. If nothing is decided, the record says that all the stored months were looked at and there is no hold-out.
3. Decide the **round**: Round A is ORB v1 on the stored data, modelled fills, `--gross`. Round B is `VWAP_PULLBACK` and `BASE_BREAKOUT`, a second pack with a different seed. Closing P3 on ORB alone is a change of the exit criterion, not a reading of it (the roadmap lets only `BASE_BREAKOUT` slide to P4); if the owner takes it, say so here and ADR 0046 gets an amendment.
4. Look at no trade of the window.

```
scripts/verify-pack.sh --strategies ORB --basket <name> --from <yyyy-MM-dd> --to <yyyy-MM-dd> --seed <your seed> --gross
```

## 2. What is in the pack

`data/exports/verify/<pack-id>/`: `README.txt` (the banner of every run first), `index.html` (the trades with a link to the card of each, and the spot checks), `cards/…` (one page per trade; the file name has no exit reason), `check-log.csv` (**your** log, the one file you edit), `engine-values.csv` (the engine's side, with the source and categories of each row, **kept closed until your row is written**), `pack.json` (identity and sources), `manifest.json` (size and SHA-256 of every file).

## 3. Conventions

- **Bar label.** A bar is labelled by its **start**, in India time. The card, the log and the stored data agree on it. An external chart must be set to the same; whether it is for each vendor is for the owner to confirm on the first trade.
- **Comparison view.** The **1-minute chart** of the trade day, resampled by the chart itself to the strategy's timeframe with the first bucket anchored at 09:15 (15m buckets begin 09:15, 09:30, …). Not official daily candles, and not a chart whose daily bar differs by session definition.
- **Prices** are as the vendor adjusts them. The stored data is adjusted at the source (ISS-18 c); a trade in an `adjusted_instrument` row can show an unadjusted figure on an external chart.

## 4. Per trade, in this order

1. With the rule text of the card, find the evaluation bar and read the levels from the chart (for ORB: the high and low of the first `or_min` minutes, whether the close crossed, the stop kind).
2. **Do not open the card's marks, the index's links or `engine-values.csv` first.** Write the entry minute and price you expect, the stop, the target and the exit (the instant and the price) into `check-log.csv`. The log has no engine column for this reason, and the file names and the index say nothing about how a trade ended. The first five rows, the external ones, are done blind.
3. Compare with the card.
4. For the rows against an external chart, read the open, high, low and close of the minutes at the entry, the stop, the target and the exit **on the external chart** and write them in the log (`ext_open` … `ext_close` for the minute at the entry; further minutes in the notes).
5. Sign the row: `verdict` is `agree`, `disagree`, `not_decidable` (only where the 1-minute data cannot tell the order inside a minute, with a note) or `skipped` (a spot check, with a reason).

Then run the tool on the log. It compares what you saw with what the engine did, field by field, and prints which rows agree, which are unexplained and whether the pack is complete. It never says that P3 passes.

```
scripts/verify-pack.sh --check data/exports/verify/<pack-id>/check-log.csv
```

## 5. Tolerances

| You write | Engine column | Tolerance |
|---|---|---|
| `seen_entry_price` | `entry_price` | one tick (the tick printed on the card and in `engine-values.csv`) |
| `seen_stop` | `stop_price` | one tick |
| `seen_target` | `target_price` (empty when none) | one tick |
| `seen_exit_price` | `exit_price` | one tick |
| `seen_entry_time`, `seen_exit_time` | `entry_ist`, `exit_ist` | the minute, exact (`HH:mm`, or `yyyy-MM-dd HH:mm`) |
| `seen_exit_reason` (`stop`, `target`, `square_off`, `max_hold`) | `exit_reason` | exact match |
| `seen_r` (your own arithmetic from the exact levels) | `r_exact` | 0.01 |

**Filled prices are not read off a chart.** The filled entry and exit depend on the tick rounding and on slippage (`a = 1 tick + 0.05 × the minute's range`, plus 2 ticks on a stop exit; ADR 0038). They are a calculator check from the operands printed on the card, in `engine-values.csv` as `entry_filled_price`, `exit_filled_price`, `entry_fill`, `slippage_r` and `r_filled` (and `r_net` and the charge total when the pack is costed). The tool compares them only when you add the optional columns `calc_entry_filled`, `calc_exit_filled` and `calc_r_filled` to the log. Net R by hand needs the owner's charge sheet (Q3, ISS-25) and the bill check of P3-03f; until then the check is gross and every card says `costs: none`.

## 6. Which trades go to an external chart

At least **5**, of which at least one is a `sell`, one is an `adjusted_instrument` row if the category occurs, and one has the 09:15 bar repaired by the importer where the card says so (ISS-21). The rest are checked against the card's own chart, which is drawn from the stored bars: those rows check the engine against the data, and the external rows check the data and the conventions as well. The 1-minute history of a charting product may not reach back to 2024 (UNVERIFIED for each vendor and plan): draw the pack first, then pick the external rows from the trades the chosen chart can show, most recent dates first, and write the source and the reason in the record. These are not a random 5 of the 20.

The spot checks (an excluded session, a flagged day, a position the data cut short) are one chart lookup each. They are optional for P3 and signed or marked `skipped` with a reason; the recommendation is to do the `flagged_day` and the `excluded_session` lines.

## 7. Disagreements: the classes, the triage and the exit rule

**Triage, cheapest test first.** For a row beyond tolerance or marked `disagree`:

1. **Data.** Compare the stored bars the card shows with the external chart at the cited minutes. If the stored bar differs beyond the tolerance it is a data defect, whatever the engine did. The known causes first: ISS-21 (the widened 09:15 bar), ISS-20 (an unadjusted jump), the adjusted-at-source prices off the tick grid, ISS-18 c.
2. **Model.** The stored bars are right and the engine did what its ADR says, but the ADR's convention is not what the owner intends or is ambiguous (the entry limit at the trigger close plus 0.1 %, the minute-path rule, a fill at the open of a gap, the square-off at the open of 15:15). The owner decides the rule; the fix is an ADR amendment and then code.
3. **Implementation.** The engine contradicts its ADR or spec: a bug. The fix starts with a test that fails (the place for it is P3-08's oracle or the walker's tests) and the engine version changes.

A finding has an id, a class, the trade key, the evidence (the minute and both values), the decision and the commit or ADR that closed it. **In the log** write it in the `finding` cell as `<class>: <text>` with the class `data`, `model` or `implementation` (for example `data: ISS-21 widened 09:15 bar`); when a model or implementation finding is fixed, add the word `closed` and the commit or ADR to its text. The tool counts findings by class, keeps the pack incomplete while a finding has no class, and while a model or implementation finding is not marked closed (exit rule c): write `closed <commit-or-ADR>` exactly, for example `model: square-off at the open, closed ADR 0048` or `implementation: wrong tick, closed 3fa91c2`; `not closed yet` or `closed?` does not count. **More than 3 rows `not_decidable` keep the pack incomplete** (the count is printed; the limit of 3 is a recommendation for the owner to confirm with Q6). The external minute you write must satisfy low <= open and close <= high.

**Keep the evidence.** Copy or commit `check-log.csv` before the first `--check` (the first-pass log is the evidence, before any finding is written to explain a row); `--check` prints the SHA-256 of the log it read, so the copy can be tied to the run. The index links to cards that show the engine's marks: the blindness is a convention, so write your row before you follow a link. The row counts in the console and `pack.json` are totals only; `session_notes_cut` in `pack.json` says when an instrument's session notes hit the cap of 100 and a kind of spot check may not have been seen. The price tolerance is the instrument's tick printed in `engine-values.csv` (the NSE default 0.05 when the engine has none). A row marked `agree` with a field beyond tolerance is `inconsistent` and counts as unexplained until it is changed or given a finding.

**The exit rule (recommended; Q6).** The pack passes the owner's check when **all** hold:

- (a) every row (the trades and the spot checks not skipped with a reason) is signed `agree`, or `disagree` with a closed or accepted finding;
- (b) **zero unexplained disagreements** (a row beyond tolerance with no finding, or `inconsistent`);
- (c) **zero open model or implementation defects**: every one has been fixed and the check redone on a new pack with a new seed that has itself no such finding;
- (d) a data defect is recorded as an issue of the roadmap and is either a known class that does not change the trade's mechanics (ISS-20, ISS-21) or is accepted in chat by the owner as not affecting the pass;
- (e) at least 5 rows against an external chart, 20 random trades at least, and one trade of each category that occurs;
- (f) the record below is committed;
- (g) **the owner says in chat that P3 passes.**

The rule is stated to be honest about its power. 20 random trades with no disagreement give, only informally, an upper bound of about 14 to 15 % (the rule of three, 3/20; exact 13.9 %) on the share of *independent units* that would fail. That is **not a bound on the share of trades**: the sample is equal-weight over cells, the trades of one instrument are not independent, and the edge picks are not part of it. Read it as "a defect that touches more than about a seventh of the instruments or cells would probably have shown". The edge picks are there for the rare paths. Neither says that a strategy has an edge, or that the engine is free of defects.

## 8. After any model or implementation fix

The pack is void (the engine version, and so the run key and the pack key, change). Draw a **new seed and a new pack** and check it in full, edge picks included. The earlier rows stay in the record as history, and the earlier seed is never reused. A fix to a data defect that changes the stored bars also changes the bar-stream hashes, so the pack is redrawn then too.

## 9. Categories

A trade can belong to several. `occurs: false` in `pack.json` says that no trade of the pooled runs belongs to a category, which is a path not checked by hand, and the record says so.

| Code | A trade belongs to it when |
|---|---|
| `sell` | the side is sell |
| `stop_gap` | exit reason stop and the exit price strictly beyond the stop (the minute opened beyond it); whether a real jump or a missing minute needs the bars on the card |
| `stop_in_entry_minute` | exit reason stop and the exit minute is the entry minute |
| `entry_at_limit` | the fill minute opened beyond the limit and traded back to it |
| `entry_after_hole` | the entry is later than the decision instant on the decision date (minutes after the decision were missing or empty) |
| `first_evaluation` | the decision instant is the first entry time of the recorded definition (reading UNVERIFIED against the roadmap's wording) |
| `overnight_entry` | the fill date is later than the decision date (swing only) |
| `exit_target`, `exit_square_off`, `exit_max_hold` | the exit reason |
| `incomplete_trigger_bar` | a minute of the evaluation bucket was missing |
| `adjusted_instrument` | an applied action has an ex-date after the trade date |
| `tick_fallback` | the NSE default tick was used and not the master's |

Not trades, so spot checks: a special session (the replay excludes it, so no trade is dated on one), a flagged day, a position the data cut short.

## 10. The record

One block per pack. **No price, no R value and no count of a group.** The trade keys come from `engine-values.csv` (source and categories) and the log; findings are listed with their class and the commit or ADR that closed them.

| Field | Pack 1 |
|---|---|
| Seed (written before the draw) | `p3-check-1` |
| Date of the draw | 2026-10-19 |
| Pack id | `p3-check-1-ea89b084629b` |
| Window (`--from`, `--to`), hold-out decision | 2024-04-01 to 2025-10-31; later months held out |
| Strategies, cost choice (`--gross` or the sheet id) | ORB, `--gross`; all 214 stored symbols given, 4 skipped as `tick_size_invalid` (BOSCHLTD, PAGEIND, POWERINDIA, SHREECEM) |
| Run key per strategy | |
| Engine version | |
| Data range (first and last bar) | |
| Bar-stream hash per instrument that has a row | |
| Trade keys with source and categories | |
| External chart, history depth and why these rows | |
| Rows against an external chart | |
| Spot checks done, skipped (with reason) | |
| Findings (id, class, trade key, closing reference) | |
| Open model or implementation defects | |
| Owner's statement and date | |

Seeds used so far (all of them, including abandoned ones): `p3-check-1`.

## 11. The assisted check (P3-09c, ADR 0046, Amendment 1 (the assisted check))

`verify-pack --assist <pack-folder> [--second-source-folder <dir>]` gives the pack an automatic second read. **It is not a signed check**: it never writes a verdict, never touches `check-log.csv`, and its readings are not the owner's.

**Procedure.**
1. Run `scripts/verify-pack.sh --assist data/exports/verify/<pack-id>` (second source: Dhan `charts/intraday`, one request per distinct symbol and day, spaced by the connector; the token is the Data account's, read by the connector and never printed) or with `--second-source-folder <dir>` holding one file per symbol and day named `<TICKER>_<yyyy-MM-dd>.csv` with the columns `time,open,high,low,close[,volume]` (for example a TradingView export; `time` is Unix seconds or an ISO time, India time when it has no offset). The bars are kept in `second-source-bars.csv`; a rerun does no network for a day it holds.
2. The tool reads each trade **blind**: the chart reader (a pure function written from sections 4 and 5 above and ADRs 0037 and 0038, sharing no code with the engine) reads the trade off the second source's day. It writes `assist-log.csv` (the columns of `check-log.csv`; `chart` = `auto:<second source>`; `ext_*` empty; `verdict`, `finding` and `signed_on` empty), hashes it, and only then reads `engine-values.csv`. The report prints both hashes.
3. It compares the stored bars with the second source's at the minutes that matter (the opening range, the trigger bar, the entry and the exit minute, each with the minute before and after) and says per minute `match`, `differs by <n> ticks`, `missing in stored` or `missing in second source` (a difference at 09:15 names ISS-21 first; a difference of at most one tick is a `match`, because two vendor feeds disagree by a tick or two on a minute's first trade; volume is not compared; open and close tolerate 2 ticks plus 0.02 % of the price, high and low one tick; when the stored prices are a constant ratio r of the second source's (adjusted history against as-traded) the row carries a `price level offset` note and the second source is scaled by r first, and a ratio that changes inside the day makes the minutes differ; the report prints per row the minutes compared, how many differ by one tick and the largest difference). The tick is the master's, in rupees.
4. `assist-report.html` gives each row a verdict of the **assistant**: `agree_all`, `bars_differ` (gross problems only, after `reader_differs`: a minute missing on either side, a price level ratio outside 0.5 to 1.5 or not constant within the day, or a cited-minute gap above `GrossGapPercent` = 1.0 % of price (the 24 row maxima of the first real run were 0.04 % to 0.35 %, median 0.13 %: feed noise; 1.0 % is almost three times the largest); a data candidate; it comes before a reader difference), `reader_differs` (the reader on the stored bars differs from the engine, with the fields; a model or implementation candidate), `data_sensitive` (the stored reading agrees, the second-source reading differs: a data finding), `cannot_read` (the reader found no trade) or `source_unavailable` (401, 429, an empty or a partial day). It counts rows per verdict, without percentages, and lists the rows to read on a real chart: every `bars_differ` row and `agree_all` rows picked by a fixed rule (SHA-256 of the seed and the trade key, smallest first; at least one Sell when one agrees), up to 5 in all. `assist-manifest.json` holds the sizes and hashes of the three files (the pack's own `manifest.json` is not changed, so a pack is never "different" after an assisted read).
5. The owner reads every row that is not `agree_all`, classifies each as data, model or implementation (section 7), reads the conventions list, checks **at least 5 rows on a real chart** (the listed ones), and signs `check-log.csv` (`agree` or `disagree`). Rows the owner accepts from the assisted log may copy its readings into the `seen_*` columns and keep `chart = auto`; rows the owner read personally keep `card` or `external:<chart>`. The spot checks stay the owner's.

**The exit rule, amended.** Section 7 stands with two changes. A row signed on the strength of an assisted reading (`chart` = `auto`) counts as signed but **not** as one of the "at least 5 rows against an external chart" (only the owner's own reading on a real chart counts). The record says how many rows were read by the machine and how many by the owner. An assisted pass is weaker than 20 owner-read trades only where the owner's eye is the only independent look at the *model*; it is stronger on *data and implementation*, because every row is read and not a sample. A reader written by the same model family can share a misreading of the rule tables, which is why the rows on a real chart stay.

**Record fields (added to section 10).** Rows read by machine; rows read by owner; second source and the SHA-256 of `second-source-bars.csv`; the SHA-256 of `assist-log.csv`; whether the stored bars came from the CSV files or from Dhan (not recorded per minute by the importer: UNVERIFIED, so a Dhan second source proves little for a minute the Dhan backfill supplied).

**Limits.** The tick of the readings is the master's, not `engine-values.csv`'s (the reader needs it before the engine's values may be read). Prices are compared raw; an adjusted instrument can show a reader difference that is the adjustment. The assisted run's numbers stay inside the pack folder.

**Deliberate choices and open items of the assisted check.** The bar comparison no longer decides the verdict except for gross problems (ADR 0046, Amendment 1 (the assisted check)): the first real run, pack p3-check-1-ea89b084629b with Dhan, gave `bars_differ` on all 24 rows although the reader equalled the engine on all 24, because the two feeds differ by 1 to 2 ticks in some minutes' opens (and in volume), so an any-difference rule carried no information. The size of the noise is printed per row, and the rows with a price level offset note are counted.

**What the verdicts mean.** The reader reads each trade twice: on the stored bars (the data the engine used), which tests the rules and the implementation, and on the second source's bars, which tests the sensitivity to the data. Precedence: `source_unavailable`, `cannot_read`, `reader_differs` (stored reading differs from the engine), `bars_differ` (gross problems only), `data_sensitive` (stored reading agrees, second-source reading differs: the trade depends on the feed), `agree_all`. `agree_all` means that the engine's trade equals an independent reading on both the stored and the second-source bars; it does **not** mean that the two data sources agree. Bar gaps are data findings for the owner, printed per row (minutes compared, how many differ, median and maximum gap in ticks and in percent of price) and summarised for the run; rows whose largest gap reaches 0.5 % are listed for a look on a real chart. When a price level offset is found the second source is read scaled by the ratio. The report counts, over all rows and whatever the verdict, the rows where each reader differs and the rows with a gross problem. `assist-log.csv` holds the second-source reading in `seen_*` and the stored reading in `stored_seen_*`.

**On a real chart.** A dividend-adjustment setting on the chart (TradingView) can shift the prices: a row with an offset note is expected to show a different level on a raw chart, and the same shape. Compare the shape and the times, and turn the adjustment off (or on) to match the pack before reading levels. The per-minute origin of a stored bar (csv or dhan) is not shown, because the importer does not record it. The hashes of the assisted files are in `assist-manifest.json`, not in the pack's `manifest.json`. The second-source cache is never invalidated (move `second-source-bars.csv` away to fetch again); the report lists the sources in it. Closed: the reader equals the real engine on 12,000 comparisons over 6,000 seeded invented days (no disagreement; the oracle's series cannot be read by a chart reader, so this replaces criterion 3), and a fake token in a failure text reaches no line, file or report. The Dhan path needs the broker vault key: `scripts/verify-pack.sh --assist` now reads it from the Keychain (`xdrishti/broker/vault-key`) for that path only. Open: the owner's real run (criterion 8), which needs the Data account connected to Dhan.

## 12. The automated gate (P3-09d, ADR 0048): the exit of P3

**The owner's decision of 2026-10-21 ("fully automated"):** the manual chart check and the owner's signature are no longer the P3 exit. The project runs the verification, analyses the result by explicit rules and decides the next step. Sections 1 to 11 remain valid as the optional owner check and as the description of the pack and the assisted read.

**Run it.** `scripts/verify-pack.sh --gate data/exports/verify/<pack-id> [--second-source-folder <dir>]` (the assisted read, then the rules). It writes `gate-decision.json` and `gate-decision.md` into the pack folder, prints the status and the numbered next actions, and never touches `check-log.csv` or writes a verdict.

| Status | Exit | Meaning | Next actions |
|---|---|---|---|
| `BLOCKED_DEFECT` | 12 | the reader on the stored bars differs from the engine, cannot read the trade, or the engine row is absent; a spot check is contradicted; the pack has no trade | failing test from the minimal repro of each row, fix, new pack with a new seed, re-run |
| `DATA_REPAIR_NEEDED` | 11 | a gross bar problem (missing minute, price level not constant, a gap above 1 % of price): instrument and days listed | run the existing gap backfill (doc 05 section 6.1) for those days, then re-run the gate |
| `RERUN_NEEDED` | 10 | a row with no second source | reconnect or fill the folder, re-run |
| `PASS_WITH_DATA_FINDINGS` | 0 | no defect, repair or rerun; some rows are `data_sensitive` (listed with the fields that change and the gap sizes) | record them as open data-quality findings; not P3 blockers |
| `PASS` | 0 | all clear | none |

**The exit rule.** P3 passes when the gate says `PASS` or `PASS_WITH_DATA_FINDINGS` on a pack drawn with the owner's seed, and `scripts/check.sh` is green. Record in section 10: the seed, the pack id, the engine version, the gate status, the counts of the gate and the SHA-256 of its inputs (they are in `gate-decision.json`); no price, no R value, no count of a group in the committed text beyond the status counts.

**What the machine checks of the spot lines.** An excluded day carries no trade of the pack; a short session has fewer than 90 % of the 375 regular minutes in the stored bars; a weekend is not a regular session; a missing session has no stored bar; a flagged day shows a jump above 20 % against the previous session on the stored bars. **Not machine-checkable (listed and counted, never a silent pass):** a special session on a weekday and whether a weekday was a trading day (the calendar is in the database); a flagged day whose jump the stored bars do not reproduce (the replay tests adjusted prices); a missing session on a weekend; `open_at_end`. They change no status and remain for the optional owner chart check.

**The honest limit.** The gate shows that the engine follows its rules on an independent reading of the bars (stored and second source). It is not a chart-by-eye check. A misreading of the rules shared by the engine, the oracle and the reader (one model family) stays possible, and so does a feed that is wrong the same way in both sources. A third independent source (for example a TradingView export in `--second-source-folder`) or an optional owner chart check of a few rows can be added later.
