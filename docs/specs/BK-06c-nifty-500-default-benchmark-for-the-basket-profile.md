# BK-06c — Nifty 500 as the default benchmark of the basket profile

- **Status:** Approved (owner, 2026-11-11, in chat: "Yes, write + build")
- **Phase / roadmap row:** BK follow-up — BK-06c (answers BK-06 open question 1 and BK-07 open question 2; ADR 0257 and 0262 "Owner decision 2026-11-11")
- **Design refs:** ADR 0257 (profile engine) · ADR 0262 (endpoints, cache, window resolver) · ADR 0020 §2 (portfolio benchmark, Nifty 50)
- **Needs (VERIFIED in code at a793b745 unless marked):**
  - `ProfileWindowResolver` takes the index from the `index` query or else `IPortfolioBenchmark.GetAsync` (the **portfolio** setting `AppSettingKeys.PortfolioBenchmarkInstrument`, default `PortfolioBenchmark.DefaultCanonicalSymbol` `NSE:NIFTY` / display "Nifty 50"); `IndexIsDefault` flows into the view; with no index the resolver uses today and a calendar window and the benchmark figures are unavailable (`noBenchmark` in `GetBasketRankingHandler`).
  - The BK-07 cache key includes the index and its newest bar (ADR 0262).
  - The Nifty 500 index **list** is a system basket source (`ind_nifty500list.csv`, `AddSystemBaskets`); whether a Nifty 500 **index instrument** with daily bars exists in the master and is tracked is UNVERIFIED.
  - Other users of the portfolio benchmark (holdings XIRR, P8-07 report benchmarks) must keep Nifty 50.
- **Size:** 1 agent-day (setting and resolver 0.4, honest states 0.3, tests 0.3)
- **Safety:** no (report figures only; no gate, plan or order reads the profile's benchmark).

## Goal
The basket profile and ranking compare against Nifty 500 unless the owner picks another index, and say plainly when Nifty 500 has no stored bars instead of quietly using Nifty 50.

## Scope
**In:** a basket-only benchmark setting `reports.basket_benchmark_instrument` with default "Nifty 500" (resolved from the master), used by `ProfileWindowResolver`; honest states when it is missing or has no bars; the setting in the Settings catalog (owner may change it; not password-guarded, it loosens nothing); the screen label; tests.
**Out:** changing the portfolio benchmark or any other report; the derived engine's Nifty 50 series (baskets 5 and 14); fetching or tracking Nifty 500 bars (the owner tracks the instrument; see Open questions).

## Acceptance criteria
1. **Default.** Given no stored basket benchmark setting and a master index instrument `NSE:NIFTY500` (UNVERIFIED symbol; display "Nifty 500" matched case-insensitively as the portfolio store does), then a profile with no `index` uses it and `indexIsDefault` is true; the portfolio benchmark still resolves to Nifty 50 (test reads both).
2. **Owner choice.** Given the setting set to an index id, then that index is used and `indexIsDefault` false; given the `index` query, the query wins (unchanged).
3. **Missing instrument.** Given no Nifty 500 instrument in the master, then the profile answers 200 with every benchmark-relative figure (beta, alpha, tracking error, capture, relative strength, regime) `unavailable` with reason "Nifty 500 is not in the instrument master", the basket's own figures computed, and **no** fall-back to Nifty 50; the ranking has no rank (states only).
4. **No bars.** Given the instrument exists with no daily bar in the window, then the same `unavailable` states with reason "Nifty 500 has no stored daily bars in this window" and the window falls back to the calendar (`window.source` says `calendar`), as the resolver does today; given bars covering only part of the window, the engine's existing coverage rules decide the states (no new rule).
5. **Cache.** Given the default changes from Nifty 50 to Nifty 500 (deploy), then no cached answer computed against Nifty 50 is served (the key holds the index id; test).
6. **Screen and assistant.** The profile screen's index label reads "Nifty 500 (default)" or the chosen name; the assistant's `basket_profile` body carries the same index name and state (no change in BK-12 shaping beyond the value).
7. **Settings catalog.** `reports.basket_benchmark_instrument` appears in the Settings catalog (ADR 0246 tier Advanced) with its default and an audit row on change.

## Contract
- **Application:** `IBasketBenchmark` (`GetAsync` → `ConfiguredBenchmark?` with a reason when missing), implemented in Infrastructure beside `PortfolioBenchmarkStore` (shared lookup helper, extended not modified); `ProfileWindowResolver` takes `IBasketBenchmark` instead of `IPortfolioBenchmark` for the default only (`FindAsync` for an explicit id is unchanged).
- **Data:** none (an `AppSetting` row only when the owner sets it).
- **API:** response unchanged in shape; summary texts of the two routes say "default Nifty 500"; `scripts/gen-api.sh`.
- **UI:** label text only.

## Non-functional
One extra small read per uncached profile; no measurable change.

## Test plan
- **Integration (Testcontainers):** AC 1 to 5 with fixtures with and without the instrument and its bars.
- **Frontend (MSW):** label (AC 6).

## Open questions
1. Is a Nifty 500 index instrument with daily bars stored today? If not, the owner tracks it (instrument master + daily bars) or the profile shows `unavailable` until then. Recommended: track it when enabling the Nifty 500 list.

## UNVERIFIED
- The canonical symbol of Nifty 500 in the Dhan master (`NSE:NIFTY500` assumed) and whether its daily bars are ingested.

## Done when
- [ ] Every criterion has evidence; `scripts/check.sh` green; `scripts/gen-api.sh`; reviewer: no blockers/majors
- [x] ADR 0257/0262 build note; roadmap row updated (the full gate and the reviewer pass belong to the caller)

## Build notes (2026-11-11)
- **UNVERIFIED resolved by reading the code.** The master names an index by `CanonicalSymbol.ForIndex(symbol name)`: upper case, runs of whitespace as `_`. So Nifty 500 is `NSE:NIFTY_500` (not `NSE:NIFTY500`), display "Nifty 500" in the Dhan style ("Nifty Midcap 150" is `NSE:NIFTY_MIDCAP_150`). The lookup takes the canonical symbol first and then the display name case-insensitively, so either spelling of the live master matches. Whether the **live** master carries the Nifty 500 index and stores its daily bars is not visible in the code (the test sample `dhan-master-sample.csv` has no Nifty 500 row): the owner tracks it (Open question 1), and until then the profile says "Nifty 500 is not in the instrument master" or "Nifty 500 has no stored daily bars in this window".
- **Setting write path.** The spec lists the setting as owner-changeable with an audit row (AC 7) but no route; built as `PUT /api/reports/basket-benchmark` body `{instrumentId}` (index, listed, session and CSRF token, no password), audited as `EssentialSettingChanged` with `key=symbol (was ...)`. The catalog descriptor is Advanced, default text "Nifty 500".
- **View shape.** `index` gains an optional `name` (additive) so the label reads "Nifty 500 (default)"; the assistant `basket_profile` body carries it because the view is serialised as is.
- **Reasons.** `BasketProfileMapper.NoBenchmark` now says "Nifty 500 is not in the instrument master" / "<name> has no stored daily bars in this window"; the older "track it in Portfolio" text is gone for the basket reads only (the P8-07 report benchmarks keep theirs).
