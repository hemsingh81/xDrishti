# BK-03b — Opening-minute bars from the live feed for baskets 4 and 12

- **Status:** Approved (owner, 2026-11-11, in chat: "Yes, write + build")
- **Phase / roadmap row:** BK follow-up — BK-03b (closes [BK-03](BK-03-derived-baskets-refresh-job-and-scores.md) AC 6; ADR 0258 "Owner decision 2026-11-11"; supersedes REVIEW-2026-11-10 question 2 for baskets 4 and 12)
- **Design refs:** BK-02 table rows 4 (Gappers, formed 09:20 IST) and 12 (Opening-range wide, frozen 09:30 IST) · ADR 0255 (pure engine, no look-ahead) · ADR 0258 (derived job, OFF) · ADR 0024 (live feed) · ADR 0173 (trade-tick recorder: its own connection and gates) · docs/design/05 (`market.bars_1m_live`, never built)
- **Needs (VERIFIED in code at a793b745 unless marked):**
  - The engine: `DerivedCatalogue` marks 4 and 12 `DerivedCadence.FrozenIntraday`, `NeedsIntraday: true`; `InstrumentBars.Intraday` (`IntradayBar[]`) and `ScoreContext.Formation`; `DerivedScorers.OpeningRange` uses `IntradaySessions.Build(intraday, asOf, formation)`, today's session must be after `asOf`, prior sessions need `Basket12.MinBars`; `RangePct` takes whatever bars the first N minutes hold (**it does not check completeness**).
  - The live feed (`XDrishti.Feed`): one Dhan connection, `Live:MaxWsInstruments` 200 (alerts, then indices, then holdings), writes the **latest price every 2 s**; no 1-minute bars are built or stored (BK-03 review 2026-11-10). Subscription in ticker (15) or quote (17) mode; packet layouts UNVERIFIED in code comments.
  - The trade-tick recorder (P7-16b) has its own connector (`TickFeedConnector`), a deployment gate `Ticks:Enabled` (false), the owner's stored switch and a paused service: the three-gate pattern this slice copies.
  - Stored `Timeframe.M1` bars (ADR 0016) are the history for the 20-day medians; how current they are each morning is UNVERIFIED.
- **Size:** 3.5 agent-days (bar builder, pure 0.8; recorder host loop and its own connection 1.0; store and migration 0.4; freeze wiring with completeness 0.6; tests 0.7)
- **Safety:** yes (a new broker socket and a writer). OFF by three gates; read-only market data (no order, no account call); a missed minute gives `not_formed`, never a guessed basket; no look-ahead.

## Goal
On a trading day with the recorder on, the derived job forms Gappers at 09:20 and Opening-range wide at 09:30 IST from complete 1-minute bars recorded live, once, frozen, or says plainly why it could not.

## Scope
**In:** a pure `MinuteBarBuilder` (quote packets → 1-minute OHLCV bars, IST minute boundaries, exchange time); an `opening-bars` recorder loop in `xd-feed` on its own connection, from session open to `RecordUntil` (default 09:31 IST, at least 09:30); the instrument set = the derived universe U(D) of BK-03 for the enabled intraday baskets, capped; table `market.bars_1m_live`; the 09:20 / 09:30 freeze in the `derived-baskets` service reading those bars plus stored history; completeness rules; settings, gates, tests, ADR.
**Out:** recording after `RecordUntil`; replacing stored M1 history or the CSV import; using these bars for scans, plans, alerts or simulation; basket 11 variant; backfill of past days from live bars (none exist); any order or account call.

## Definitions
- **Complete bar** for minute m (09:15 ≤ m < formation): built only from packets whose exchange time lies in [m, m+1 min); it is written when the first packet of a later minute arrives or at m+1 min + `GraceSeconds` (default 5 s), whichever is first. Volume = cumulative day volume at the last packet in m minus that at the last packet before m (first minute: minus 0). A minute with no packet for an instrument is **missing**, not filled forward.
- **Window complete** for an instrument and a formation time F: every minute 09:15 ≤ m < F has a complete bar, and the recorder's connection had no gap (disconnect, silence above `Live:SilenceSeconds`) overlapping [09:15, F).

## Acceptance criteria
1. **Gates.** Given `OpeningBars:Enabled` false (default), or the owner switch `baskets.opening_bars` off (default), or the `opening-bars` service paused (default), then no credential is read, no socket opened, no row written (counting fakes); each gate alone stops it.
2. **Bar building (pure).** Given packets for one instrument at 09:15:01 (ltp 100, cumvol 1,000), 09:15:40 (102, 1,500), 09:15:59 (99, 2,000), 09:16:03 (101, 2,600), then the 09:15 bar is O 100 H 102 L 99 C 99 V 2,000, built from exchange time, and the 09:16 bar opens 101; a packet with exchange time earlier than the current minute is dropped and counted `late`.
3. **No look-ahead.** Given formation at 09:20, then the bars handed to the engine are only those with minute start < 09:20 (the 09:20 bar is excluded even if written); a property test over random packet streams asserts no input timestamp ≥ F.
4. **Complete or not formed.** Given one instrument missing the 09:17 minute, then for basket 4 that instrument is excluded with reason `opening_minute_missing` (counted, named in the version's exclusions); given a connection gap overlapping [09:15, 09:20), then the **whole** basket 4 version for that day is `not_formed` with reason `feed_gap`; given the recorder off or not running at 09:15, `not_formed` with `no_live_bars`. A missing input is never a pass, and `RangePct` is only called with a complete window (engine guard added: an incomplete session is `Insufficient`).
5. **Freeze once.** Given the 09:20 complete-bar point is reached (09:20 + `GraceSeconds`), then basket 4 is formed once with `as_of` = previous session, `formed_at` 09:20 and frozen; a later run that day writes nothing (BK-03 AC 6); same for basket 12 at 09:30. A restart after 09:20 does not form it later from partial bars: a version not formed by `FormBy` (default F + 2 min) is `not_formed` with `late`.
6. **History.** Given basket 12, the 20 prior sessions' opening ranges come from stored M1 bars (and earlier live rows); fewer than `Basket12.MinBars` complete prior sessions → instrument `insufficient` (as now). Given basket 4, the first-5-minute volume median uses the same sources; a prior session with a missing opening minute does not vote.
7. **Instrument set and cap.** Given U(D) of 450 and `OpeningBars:MaxInstruments` 500, all are subscribed on the recorder's own connection by 09:14 IST; given U(D) above the cap, the most liquid by ADV20 are kept and the rest are excluded with `not_subscribed` (counted), never silently dropped. The live feed's own set and cap are unchanged (test: the price feed's subscription list is identical with the recorder on).
8. **Store.** `market.bars_1m_live(instrument_id, minute_start timestamptz, o, h, l, c, v, packets int, recorded_at, primary key (instrument_id, minute_start))`, insert-only for the runtime role (`ON CONFLICT DO NOTHING`; a second write of a minute is counted `duplicate`); retention `OpeningBars:KeepDays` (default 60) by a daily delete owned by the migration role job, UNVERIFIED pattern.
9. **Stops at RecordUntil and on low disk.** Given 09:31 IST, the recorder unsubscribes and closes; given free space under the shared `Depth:MinFreeGb`, it stops and the day's baskets are `not_formed` with `low_disk`.
10. **Non-trading day.** Given a holiday or weekend by the trading calendar, then no connection and no `not_formed` row (nothing is due).
11. **Isolation.** A failure of the recorder (exception, broker error) neither stops nor slows the live feed, the depth recorder or the tick recorder (own loop, own try/catch, as `TickService`).
12. **Service and status.** The `opening-bars` service appears on the Services screen (schedule 09:10 to 09:32 IST, paused by default) with the last day's counts: instruments, bars, missing minutes, late, duplicates, gaps.
13. **Logging.** `feed.opening_bars` (counts, ms) only; no symbols, no token.

## Contract
- **Data:** migration `AddOpeningMinuteBars` (with `Down`): `market.bars_1m_live` as AC 8; grants insert/select to the runtime role; a stored setting `baskets.opening_bars` (bool, false, owner act).
- **Settings** (`OpeningBars:*`, `[Range]`, `ValidateOnStart`): `Enabled` false; `MaxInstruments` 500 `[50,5000]`; `RecordUntil` 09:31 (must be ≥ 09:30 and ≥ the latest enabled formation time + 1 min); `GraceSeconds` 5 `[1,30]`; `FormBy` offset 2 min; `KeepDays` 60.
- **Feed:** `OpeningBarsService` + supervisor in `xd-feed`, its own `DhanFeedConnection` in quote mode (17) for volume; universe read from BK-03's U(D) for `as_of` = previous session.
- **Derived job:** the `derived-baskets` service's intraday freeze reads `IOpeningBarsQueries` and passes complete windows only.
- **API:** none new beyond the service status already shown.

## Non-functional
500 instruments × 16 minutes ≈ 8,000 rows a day; batched inserts each minute (one statement). Memory under 20 MB in `xd-feed`. Dhan limit of instruments per connection 5,000 (from `LiveOptions` comment).

## Test plan
- **Unit:** builder (AC 2, 3 property), completeness and freeze decisions over a table of gap cases (AC 4, 5), cap ordering (AC 7).
- **Application (fakes):** gates (AC 1), non-trading day, low disk, isolation from the other loops.
- **Integration (Testcontainers):** store insert-only and duplicate; a recorded fixture day forming baskets 4 and 12 by hand-worked numbers (BK-02 example 11: OR 1.5 % vs median 0.8 % → wide).

## Open questions
1. Is a second Dhan feed connection allowed on the owner's plan (the tick and depth recorders already open their own)? UNVERIFIED limit of connections per account; if only one, the recorder must share the live feed's connection for 16 minutes (about +0.5 d).
2. `MaxInstruments` 500 covers U(D) today? Recommended: yes; measured at build.

## UNVERIFIED
- Dhan quote packet fields (LTT as exchange time, cumulative volume) and connections per account.
- Freshness of stored M1 history each morning (if yesterday's bars are not yet imported at 09:20, history comes from the live rows of earlier days only, and the first 20 recorded days are mostly `insufficient`).
- The retention job pattern for a plain (non-hyper) table.

## Done when
- [ ] Every criterion has evidence; `scripts/check.sh` green; reviewer: no blockers/majors (told to look for look-ahead, a filled-forward minute and any order or account path)
- [ ] Migration with `Down`; ADR (or ADR 0258 amendment); doc 05 updated for `market.bars_1m_live`; BK-03 AC 6 evidence; roadmap row updated
