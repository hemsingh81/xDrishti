# BK-09 — Relative-strength features and sector strength

- **Status:** Draft (written 2026-10-25; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P8-P9 window feature slice — U4, U9 (stock side), U5 and the `RS_ROTATION` test of `docs/research/basket-leverage-uses.md`
- **Design refs:** docs/design/11-strategy-lab.md §2 (context features; `sector_rs(n)` "not planned until a strategy needs it") · docs/design/07-strategies.md (`RS_ROTATION`) · P3-01e D8 · P3-01b/c · P6-04 · P6-17b · ADR 0081
- **Needs:** [BK-06](BK-06-basket-index-and-profile-metrics-engine-pure.md) (`EqualWeightIndex`, `RelativeStrength`); P3-01c/d (index series incl. Nifty 50 in `market.context_daily`); the strategy DSL and Lab (built P3-02/P3-04); BK-01..BK-05 for the pre-registered baskets the rank is taken within (Nifty 50, Nifty 100, F&O, liquid-100 and the sector baskets); `instruments.sector` (the master's `industry`, per P3-01e D8).
- **Size:** 5 agent-days (kernels 1.2, daily table and job 1.3, DSL feature wiring with the lag rule 1.2, `RS_ROTATION` as stored text and its Lab run 0.6, tests incl. truncation 0.7)
- **Safety:** no. Computes features and one stored-text strategy; nothing is selected, sized or ordered from them; the P7 ranking and gate are not touched.

## Goal
Strategies and reports can use a share's relative strength inside a named basket (6- and 12-month momentum skipping the latest month, volatility-adjusted), its 20-day strength against the Nifty 50, and its sector's rank, all as of the previous close; and the swing strategy `RS_ROTATION` ("strongest shares in the strongest sectors") exists as stored text so the Lab can test it honestly.

## Scope
**In:** pure `RelativeStrengthRank` (`mom_rank`), `IndexRelative` (`rs_vs_index`), `SectorStrength` (equal-weight member return and rank), insert-once tables, a daily upkeep step, DSL features `mom_rank(basket, lookback, skip)`, `rs_vs_index(n)`, `sector_rs(n)` with the existing one-session lag, the stored text of `RS_ROTATION` (stored-text-only, BUY only, like ADR 0081), a Lab run recipe in the spec's Test plan, tests, ADR.
**Out:** using any of these in P7 ranking or as a gate (a separate owner-approved change after evidence); Nifty Midcap 150 and Smallcap 250 relative strength for the gate (needs an index loader; not in this slice); adding the features to the P6-04 catalog `tf-1` (see Decision 6); machine-learning use (U10); a long/short momentum spread; parameter sweeps over lookbacks; a UI beyond the existing feature lists.

## Definitions (fixed, not tuned)
- **Momentum return** over lookback L with skip s: R_i = c_i(t - s) / c_i(t - L) - 1, t the last closed session; defaults pre-registered at (L, s) = (126, 21) and (252, 21) only.
- **Daily volatility** v_i = sample stdev of daily returns over the same L - s sessions.
- **Momentum score** S_i = R_i / v_i (volatility-adjusted, the NSE index family's method).
- **Percent rank** within the basket's members as of t: p_i = (number of members with a lower score + 0.5 × number tied excluding i) / (N - 1), in [0, 1]; N ≥ 10 voters, else `insufficient`.
- **Index-relative strength** `rs_vs_index(n)` = stock n-session return minus the Nifty 50 n-session return, in percentage points; n = 20 only in v1.
- **Sector strength**: sector index = equal-weight (daily rebalanced, BK-06 `EqualWeightIndex`) of the members of that sector in the tracked set; windows 21, 63, 126 sessions; rank 1 = strongest; needs ≥ 5 members with the full window, else `insufficient`.

## Acceptance criteria
1. **Momentum rank by hand.** Given five members with (c(t-126) → c(t-21), daily vol): A 100 → 130, 1.5 %; B 100 → 120, 0.8 %; C 100 → 110, 1.25 %; D 100 → 90, 1.0 %; E 100 → 100, 1.0 %, then R = +0.30, +0.20, +0.10, −0.10, 0.00 and S = 20, 25, 8, −10, 0; the ranks by score are B > A > C > E > D and p = B 1.00, A 0.75, C 0.50, E 0.25, D 0.00. With N = 5 the real rule gives `insufficient` (N below 10); the test calls the kernel with `MinVoters` 5 to prove the arithmetic, and a second test with 12 members proves the production floor.
2. **Ties.** Given two members with an equal score, both receive the average of the ranks they occupy; swapping their order in the input changes nothing.
3. **Volatility zero.** Given a member with zero volatility over the window (all closes equal) then its score is `not_evaluable` and it is excluded from the ranking and counted; never a division by zero, never an infinite score.
4. **Index-relative by hand.** Given a share 105 → 110 (+4.7619 %) and the Nifty 50 22,000 → 22,440 (+2.0000 %) over 20 sessions, then `rs_vs_index(20)` = +2.7619 pp (within 1e-3); with fewer than 21 closes or no index bars it is `unavailable` with the reason; never 0.
5. **Sector strength by hand.** Given a sector of five members with 63-session returns +4 %, +2 %, +6 %, 0 %, +3 % then the sector return is +3.0 % (equal weight); another sector with four members is `insufficient` (n 4, min 5); ranks among sectors with a value are 1..k with ties on the lower rank; a sector with an `insufficient` state has no rank.
6. **No look-ahead (property).** Cutting or replacing every bar after day k leaves every feature value and rank for days up to k byte-identical (the P6-04 truncation property applied here). A feature read by a strategy at decision time on day D uses the value as of the previous closed session (the lag rule of the DSL, P3-02f: read at build).
7. **Point-in-time membership.** The rank on day D uses `members(asOf = D)` of the named basket (BK-01..05) where dated rows exist, else the single snapshot with `membership_basis single_snapshot` stored on the row; a run that reads the feature prints the basis in its label ("members as of …").
8. **Tables, insert-once.** `market.relative_strength_daily (as_of, instrument_id, basket_id, lookback, skip, score, pct_rank, n_ranked, state, membership_basis)` and `market.sector_strength_daily (as_of, sector, window_sessions, ret, rank, n_members, state)`; rerunning a date inserts nothing; the app role has insert and select only; a rebuild verb `xd market rs --from D --to D2` refuses to overwrite and says what exists.
9. **DSL.** Given the strategy text `mom_rank("Nifty 100", 126, 21) >= 0.67 and sector_rs(63) <= 3`, then it compiles; a basket name outside the pre-registered set, a lookback other than 126 or 252, a skip other than 21, `rs_vs_index` with n other than 20, or a sector window other than 21, 63 or 126 is a compile error with a message that names the allowed values (no free parameters means no grid search by accident); the value `insufficient` makes the condition false and the trade is counted as `feature_unknown`, never as a pass.
10. **`RS_ROTATION` as stored text.** The strategy exists as a stored version (swing, BUY only, 1D, entry at the close and fill at the next open, one `FIXED_RR` exit, a hold of at most 5 sessions, stop at the lowest low of the last 5 sessions, as the ADR 0081 batch) with the condition `mom_rank(basket, 126, 21) >= 0.67 and sector_rs(63) <= 3`; it passes the DSL compiler and the Lab's `validate`; it is **not** marked Active and is not read by selection.
11. **Trials are counted.** Every Lab run of `RS_ROTATION` over a basket counts as one variant in `MultipleTesting.Factor(N)` (VERIFIED in `Domain/Lab`) for its strategy family; the run label stores the basket id, the basket's `params_hash` and the membership basis.
12. **Performance.** The daily step for a 500-member basket and 12 sectors finishes in under 4 s; one DSL read is a point lookup (no per-bar cross-sectional work in the strategy loop).

## Contract
- **Domain (pure):** `Domain/Market/RelativeStrength` (`RelativeStrengthRank`, `IndexRelative`, `SectorStrength`, option and result records reusing `Metric`/`MetricState`). **Application:** `RelativeStrengthUpkeep` (after EOD aggregation), `IRelativeStrengthStore`, a feature provider for the DSL frame (reads the stored row for the previous session).
- **Data:** the two tables above, migration `AddRelativeStrengthDaily` with `Down`; grants insert and select to `xd_app`.
- **DSL:** three features registered in the Lab feature catalog (the catalog has 56 names per the research note, the stored catalog `tf-1` for the learning side has 41 and is untouched); allowed parameters listed in the compile error.
- **API:** none new in this slice beyond the existing Lab validate/run; a read `GET /api/market/relative-strength?basket=&asOf=` (newest 500 rows) for the Lab and the reports; tag `Market`.
- **Events/jobs:** a daily step; idempotent by primary key; failure leaves the previous rows, the features read `unavailable`. Log events: step done (rows, ms), counts only; next free id block at build time.

## Non-functional
Daily step under 4 s; table growth about 2 × baskets × members rows a day (retention keep; compress with the hypertable policy if it grows, UNVERIFIED need). No logging of symbols per row.

## Test plan
- **Unit:** criteria 1 to 5; N = 9 versus 10 voters; two lookbacks; skip boundary (the latest 21 sessions never enter R).
- **Properties:** truncation (6); permutation of members leaves ranks unchanged; adding a member below everyone changes every higher rank's p predictably (by hand for N = 12).
- **Application (fakes):** the daily step with dated membership and with a single snapshot; rerun inserts nothing; a share with a split-flagged window is excluded and counted (`corporate_action_unresolved`, as the system-baskets note requires).
- **Integration (Testcontainers):** migration up and down, grants, the DSL compile errors, a Lab validate of `RS_ROTATION` on the seeded database with the ledger family label and the Factor count.
- **Architecture:** no order-named member; the stored text is not referenced by `Selection`.
- **Lab recipe (evidence, not a test):** run `RS_ROTATION` and, as a control, the same 15-strategy swing batch with and without the rank filter (two runs per strategy, both counted in N), over the pre-registered baskets only; report with the P5 gates; **"no edge" is an accepted outcome** and goes in the Lab record, not in a change to any strategy.

## Decisions (researched)
1. **Lookbacks 126 and 252 with a 21-day skip, volatility-adjusted, nothing else.** Question: which momentum windows? Chosen: the two conventional ones with the last month skipped to avoid short-term reversal, scored return over volatility as the Nifty 200 Momentum 30 methodology does. Reason: few parameters reduce the overfit surface (research §U4); a grid would turn a literature-backed signal into a data-mined one. Rejected: sweeping 3 to 12 months (every cell would count in N). Source: Nifty 200 Momentum 30 factsheet (snippet only); Jegadeesh-Titman style evidence via Indian-market summaries (UNVERIFIED, secondary).
2. **Percent rank in the basket, not a z-score.** Reason: ranks are scale-free and robust to one outlier; the terciles used in strategy text (`>= 0.67`) read the same in every basket. Rejected: z-scores (a single huge winner distorts everyone).
3. **Long-only reading.** The feature ranks; it says nothing about shorting. Reason: shorting delivery shares is not available to us and SLB depth is thin (research §4 item 7, UNVERIFIED primary), so a long/short spread is not tradable; the stored strategy is BUY only.
4. **Sector index is ours, equal-weight from `instruments.sector`.** Reason: the master carries the industry label (P3-01e D8), the index is a pure function of data we hold, and it is point-in-time wherever membership is. Rejected: fetching NSE sector indices (a loader and licensing question for a number we can compute; the research note keeps it as an option). Honest limit: tracked-set sectors with fewer than 5 members read `insufficient`, so small sectors never rank.
5. **`rs_vs_index` is n = 20 only and Nifty 50 only.** Midcap and smallcap relative strength for the gate needs index bars we do not load; one definition now, more after the probe. Source: research §U9 (index instruments exist in the master, loader missing).
6. **The learning catalog `tf-1` is frozen.** Adding features would change stored training sets and the reproducibility of P6-05 verdicts (ADR 0066); U10 will introduce `tf-2` with an ablation test if and when the baseline model exists. The features are available to the Lab DSL and the reports first.
7. **Insufficient makes the condition false, and is counted.** A strategy must not enter because a feature was unknown; the count shows how often it happened. Matches the repo's "unavailable never a pass" rule.
8. **Sector rotation is tested as a filter on swing strategies, not sold as a strategy first.** The business-cycle rotation literature is sceptical after costs (Molchanov and Stangl 2023 via the research note), industry momentum a by-product of factor momentum (UNVERIFIED), so the honest claim is "does the filter lift the existing swing batch out of sample", with the multiple-testing count.

## Risks and honest limits
- Two to three years of history (ISS-19): a 252-session lookback leaves about one year of valid ranks; 126 is the workhorse and the 12-1 variant is marked `short_history` early on.
- Survivorship: a basket of today's members flatters momentum (losers that left are missing); the `membership_basis` and the label say so.
- Momentum has sharp reversals at regime turns (a documented May effect in Indian studies is UNVERIFIED); a single regime in the data cannot show it.
- Sector equal-weight returns of a small tracked set are noisy; rank flips are common near ties.
- Corporate actions: a share with an unresolved split in the window is excluded and counted rather than repaired.

## UNVERIFIED
- The exact lag rule text of P3-02f (named in the research note; to be read at build).
- That `instruments.sector` is filled for all tracked shares and the sector names are stable.
- The Lab feature-catalog count (56) and the exact registration mechanism.
- Indian cross-sectional momentum evidence (secondary summaries only).
- Pre-registered basket names and ids (BK-01..BK-05).

## Alignment with BK-01..BK-05 (read 2026-10-25)
BK-00..BK-05 now exist (Draft). What this slice relies on, as they state it: `kind` in `manual|portfolio|index|list|derived` and `system` (BK-01); `GET /api/baskets/{id}/members?asOf=` with `membersKnownSince` (BK-01); derived baskets stored as immutable dated versions in `market.basket_versions` (`as_of`, `effective_from`, `params_hash`, member rows with `valid_from`/`valid_to`) written by a job that is OFF by default (BK-03); list baskets for F&O, ASM/GSM and MTF (BK-05). Wherever this spec says `rule_version` it means the basket's version id plus `params_hash` (null for manual and index baskets, whose history starts at the first fetch). Index baskets are point in time only from the first fetch; the screens say so. Re-check names and ids at Approval; nothing here edits BK-00..BK-05.

## Open questions
1. Which baskets are the pre-registered rank universes? Recommended: Nifty 100 and the liquid-100 derived basket only (largest, least survivorship noise); F&O as a third.
2. Should `RS_ROTATION` be tested only once the dated membership has at least 6 months of history (so the rank is point in time), or now with the survivorship label? Recommended: build now, run the Lab study later.

## Done when
- [ ] All acceptance criteria have evidence; `scripts/check.sh` green; reviewer: no blockers/majors (told to look for look-ahead and for a free parameter)
- [ ] Migration added with `Down`; `scripts/gen-api.sh` if the read endpoint is kept
- [ ] ADR (next free number at build time); doc 11 §2 "Not yet built" paragraph and doc 07 `RS_ROTATION` line updated; `scripts/build-docs.sh`

## Review fixes 2026-10-25
- The thresholds 0.67 (`mom_rank`) and rank <= 3 (`sector_rs`) in the `RS_ROTATION` text are fixed in this spec; changing either is a new variant and counts in N.
- State t is the last closed session; the DSL's one-session lag must not apply a second shift to these stored values (a test with a known-value fixture proves the feature at decision day D equals the stored row of D-1, not D-2).
- `rule version` replaced by `params_hash` in the body. Size unchanged: 5 agent-days.
