# P14-11 — IV and event features: the extension `iv-1` (ATM IV, IV rank and percentile, term slope, IV minus realised, the Nifty IV rank, put-call OI, results before expiry) read at the decision instant, NaN with a closed reason for every unknown, and evaluated by P13-05's comparison

- **Status:** Built 2026-11-04 (ADR 0199). Approved (owner, 2026-11-03, in chat; written 2026-11-03, decisions researched under the owner's delegation). Owner-only acts named in the spec (terms, allow-list, labelling) are still yours.
- **Phase / roadmap row:** P14 — slice P14-11 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("IV and event features ... as an extension feature set evaluated like P13/P15").
- **Design refs:** docs/design/09 §3 (extension mechanism), 20 §B, §C · P13-05 (the comparison core, `research.trade_features_ext`, `research.extension_evaluations`) · P15-05 (the extractor pattern: decision-instant reads, NaN with closed reasons, an extraction record) · P15-10 open question 3 (a KEEP is a record only) · ADR 0183, 0185, 0186, 0177 consequence (bars only through a reader slice). New ADR: **next free number at build time**.
- **Needs:** P14-03 (built; **data after the owner's probe and switch**), P14-04 (built: usable key-sessions), P14-05 (unit conversion; no model prices), P13-05 (built). The rolling-bar reader `IRollingBarReader` of P14-08 (whichever of P14-08 and P14-11 is built first adds it). P15-06 (built: `EventCalendarSnapshot`) for the results flag. **Not** gated on P14-09 (the plan's "gate for P14-10/11" is corrected in its review fixes: this slice is a feature set, not an expression). **History:** the comparison needs the cash trades of 5 years (CF-04 Part B); with today's 2.1 years it is NOT_EVALUABLE.
- **Size:** 2.5 agent-days (estimate; the plan said 2; +0.5 for the daily IV series and its extraction record).
- **Safety:** no — research features written to an insert-only table and one comparison record. No scoring switch is built (a KEEP is a record, as in P15); no decision path reads the features.

## Goal
For every labelled cash trade the system holds a small set of option-implied features as they stood at the decision instant, and one command answers with P13-05's paired comparison whether adding them to `tf-1` improves out-of-sample net R per candidate. Instruments without options get NaN with the reason, never a zero.

## Verified facts (read 2026-11-03 in `backend/src`)
- `ExtensionCatalogs.GroupOf` knows `ev-1` and `fu-1` only; `research.trade_features_ext.extension` must match `^[a-z]{2,8}$` and `version` `^<extension>-[0-9]{1,3}$`, so `iv` / `iv-1` fit. `ExtensionComparisonOptions` has presets `ForDepth()` (month folds, train 2 months, day blocks + 5-session sensitivity, 2,000 candidates, 300 flips) and `ForFilings()` (quarter, train 12 months, season blocks, 1,000 candidates, no flips).
- `ExtensionFamilies.All` (the P15 families) is iterated by the P15 phase check; the route refuses other families (see P14-09).
- `options.rolling_bars.iv` is `real` null, unit UNVERIFIED (percent assumed); `oi` null; offsets ±10 (indices), ±3 (20 shares), codes `W1`, `W2`, `M1` (NIFTY), `M1`, `M2` (BANKNIFTY), `M1` (shares).
- `EventCalendarSnapshot` gives `days_to_results` per instrument and decision instant with the fail-closed `unavailable` reasons of ADR 0181/0182.

## Scope
**In:**
- Domain (pure): `IvFeatureCatalog` (`iv-1`, ordered names below), `IvFeatures.Compute(inputs)`, `DailyAtmIv` (one value per usable session: the mean of the CE and PE offset-0 IV of the last bar of the session), `ExtensionComparisonOptions.ForOptions()` preset; `ExtensionCatalogs.GroupOf("iv-1")`.
- Application: the existing `ExtractExtensionFeaturesHandler` (P15-05, verb `xd learn features` with its extension option) learns `iv-1` through an `IvFeatureExtractor`, writing `research.trade_features_ext` and the extraction record in `research.extension_extractions` (`lag_minutes` 0, the calendar SHA-256 of the snapshot used, `parser_version` and `filings_watermark` null, NaN counts per feature and reason; the archive watermark and quality run ids go into the evaluation record's `inputs`); the evaluation through the existing `EvaluateExtensionHandler` path with family `ext:iv-1` (protocol X with the `ForOptions()` preset); the route accepts `ext:iv-1` without adding it to `ExtensionFamilies.All`.
- Tests, ADR, doc 09 §3, doc 20 §C.

**Out:** any scoring use or switch; a surface or smile feature; Greeks as features (vendor Greeks exist only in our snapshots from 2026-11 on, too short); features from `options.chain_snapshots` (too short for a 5-year comparison; a later version may add them).

## The features (`iv-1`, in this order; all at the decision instant `t`)
| Name | Definition | NaN reasons |
|---|---|---|
| `iv_atm` | Mean of the CE and PE offset-0 IV (÷ 100) of the nearest series with at least 3 sessions to expiry, from the last usable bar **ending at or before `t`** | `no_options`, `no_bar_before_t`, `iv_missing`, `archive_unusable` |
| `iv_rank_252` | `(x − min) ÷ (max − min)` of `DailyAtmIv` with `x` = the previous session's value and min/max over the 252 sessions before the trade date | `short_history` (< 200 values), `flat_history` (max = min), `no_options` |
| `iv_pct_252` | Share of those 252 values below `x` | as above |
| `iv_term_slope` | `iv_atm` of the second series minus that of the first (NIFTY `W2 − W1`; BANKNIFTY `M2 − M1`) | `no_term_series` (shares), others as `iv_atm` |
| `iv_minus_rv20` | `iv_atm` minus the annualised standard deviation of 20 previous daily log returns of the underlying (√252 scaling) | `short_history`, others as `iv_atm` |
| `nifty_iv_rank_252` | `iv_rank_252` of NIFTY, for every trade | as `iv_rank_252` |
| `pcr_oi` | Sum of PE OI ÷ sum of CE OI over offsets −3..+3 of the nearest series, last bar ending at or before `t` | `oi_missing`, `zero_call_oi`, `no_options` |
| `results_before_expiry` | 1 when the instrument's next results session (P15 calendar, visible at `t`) falls on or before the nearest expiry, 0 when a visible entry falls after it | `event_unavailable` (any fail-closed reason of ADR 0182), `no_options` |

A trade whose instrument is not an option underlying of the archive gets `no_options` for its own features and still gets `nifty_iv_rank_252`.

## Acceptance criteria
1. **Hand values.** Fixture daily ATM IVs 10, 11, …, 261 (%, 252 values) and a previous-session value of 136 % → `iv_rank_252` = (1.36 − 0.10) ÷ (2.61 − 0.10) = 0.502; `iv_pct_252` = 126 ÷ 252 = 0.5. CE IV 15.0, PE IV 17.0 → `iv_atm` 0.16. PE OI 1,200, CE OI 800 → `pcr_oi` 1.5. 199 values → `short_history`.
2. **No look-ahead (property, 200 seeds).** Changing any bar ending after `t`, any daily value of the trade date or later, or any calendar row intimated after `t` changes no feature of that trade; a control change before `t` can.
3. **NaN, never zero.** Each NaN reason is reached by a fixture; the extraction record counts NaN per feature and reason; no unknown is stored as 0 (a test scans the stored arrays against the record).
4. **Comparison.** `ext:iv-1` runs through P13-05 with `ForOptions()` (calendar quarters, `MinTrainMonths` 12, embargo 5 sessions, day blocks with the 5-session sensitivity, ≥ 2,000 paired candidates, ≥ 300 flips, Δ log loss and Δ Brier required as `ForDepth()`), ledger first, hold-out once; placebo features → not KEEP at N = 1 and 16; a planted feature → KEEP at 7 test quarters and NOT_EVALUABLE at 6.
5. **Versions.** A stored record names `iv-1`, the archive watermark, the quality run ids, the calendar SHA-256 and the IV unit label; a later archive watermark makes the old record `stale` for P14-17.
6. **Families apart.** The P15 phase check output is unchanged (golden); `GET /api/research/extensions?family=ext:iv-1` lists the records.
7. **No decision use.** Architecture test: `IvFeatureCatalog`, the extractor and the records are referenced only by the verbs, the route, P14-16/17 and tests; no scorer, selection, planning or execution path reads `iv-1`.

## Contract
- **Verbs:** `xd learn features --run <run-key>` with the extension `iv-1` (the existing option); `xd learn extension-eval --family ext:iv-1 [--holdout]`.
- **Data:** none new (`research.trade_features_ext`, `research.extension_extractions`, `research.extension_evaluations`).
- **API:** the shared route accepts `ext:iv-1`.

## Non-functional
The daily IV series for 22 underlyings over 5 years ≤ 1 minute (one bounded read per underlying and window); extraction for 50,000 trades ≤ 2 minutes (UNVERIFIED).

## Test plan
- **Unit:** criterion 1, each NaN reason, `DailyAtmIv` on a session with only one side, the term slope per underlying.
- **Property:** criterion 2; determinism and order invariance of the extraction.
- **Integration (Timescale):** extraction on a seeded archive and run; the evaluation record; grants unchanged.
- **Architecture:** criterion 7.

## Decisions (researched)
1. **A feature set, not an expression.** IV tells whether a cash signal works better in some volatility states; that is P13-05's question, not protocol O's. So it needs no P14-09 verdict.
2. **Daily ranks from the previous session.** A rank that includes the trade date's own close would read the future for an intraday decision.
3. **A market-wide Nifty IV rank for every trade.** Most tracked shares have no option series in the archive; the index IV is the one option-implied value every trade can have (like a VIX proxy).
4. **Quarterly folds with day blocks.** Five years of option IV fit quarterly folds; IV regimes persist for days, which the 5-session sensitivity covers.

## Risks and honest limits
- The vendor's IV unit and model are UNVERIFIED (P14-02 pilot, P14-03 probe); a wrong unit scales every value but not the ranks.
- ATM series switch strikes as the spot moves; `iv_atm` is a moving-strike value by design.
- NOT_EVALUABLE until CF-04 Part B gives 5 years of cash trades; even then the extension is likely INCONCLUSIVE (P13-05 risk).

## Open questions (owner)
None blocking. Defaults: the `ForOptions()` preset as above; a KEEP is a record only (no scoring switch in P14).

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any read after `t`, any zero for an unknown, any P14 family in the P15 phase check)
- [ ] No migration; contract regenerated if the route changed
- [ ] ADR written; docs 09 §3, 20 §C updated
