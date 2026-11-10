# P14-15 — Basis evaluation: cash-and-carry on stored end-of-day futures, one trade per instrument and expiry, net of both legs' costs against the owner's alternative rate, with expiry blocks, dividends as a one-sided bound, and the verdict that is expected to be DROP

- **Status:** Approved (owner, 2026-11-03, in chat; written 2026-11-03, decisions researched under the owner's delegation). Owner-only acts named in the spec (terms, allow-list, labelling) are still yours.
- **Phase / roadmap row:** P14 — slice P14-15 of [P14-00](P14-00-options-and-stat-arb-plan.md) ("Basis evaluation (cut first)"); protocol S item 5.
- **Design refs:** P14-00 protocol S.5, D12, risk 6 · P13-05 (`BlockBootstrap`, ledger-first trials, `research.extension_evaluations`, hold-out once) · ADR 0042/0044 (costs), 0059 (ledger). New ADR: **next free number at build time** (only if built; the cut is the default).
- **Needs:** P14-14 (futures EOD, carry model), P14-07 (the `Futures` and `Delivery` charge lines), P13-05 (built). **Owner acts (defaults if missing):** the F&O files and their terms (P14-14; default: NOT_EVALUABLE `no_futures`); the futures and delivery charge lines (default gross: KEEP impossible, DROP possible); `Basis:MarginPct` (default unset: KEEP impossible, DROP judged at zero margin, the best case); `Basis:AlternativeRate` (default unset: NOT_EVALUABLE `alternative_rate_missing`).
- **Size:** 2 agent-days (estimate).
- **Safety:** no — read-only research over stored rows; one record. No margin call, order or position.

## Goal
Settle with stored evidence whether a cash-and-carry basis trade (long delivery cash, short the same month's future, held to the session before expiry) earns more than the owner's alternative after both legs' costs, for this account, and store the verdict.

## Verified facts (read 2026-11-03)
- No futures prices exist until P14-14 ingests the owner's files; no dividend ex-date exists for any instrument (`market.corporate_actions`: split and bonus only); swing SELL of cash is refused by the simulator (the short leg here is the future, which this slice values from settlement prices, not the simulator).
- `BlockBootstrap.Bounds` accepts arbitrary block keys; `research.extension_evaluations` takes protocol `S` records with fold unit `none`.

## Scope
**In:** Domain (pure) `CarryTrade` (entry, exit, P&L, capital, annualised return), `ProtocolBasis`; Application `EvaluateBasisHandler`, verb `xd basis eval --family basis:carry-1 [--holdout]`; ledger trials; the shared route lists the family; tests.
**Out:** reverse carry (short cash swing is not possible, D10/doc 07 §7); intraday basis; index basis without index spot bars; margin calls; any live or paper use.

## The rules
- **Trades.** For each instrument with a futures row and each monthly expiry: entry at the settlement and spot close of the first session at least `EntryDays` (20) calendar days before expiry; exit at the settlement and spot close of the last session before the expiry session. P&L per share = `(S_exit − S_entry) − (F_exit − F_entry)` = `basis_entry − basis_exit`. Any missing price → `unavailable` with the reason (`no_spot`, `no_futures`), counted, never dropped silently.
- **Capital and return.** Capital = `S_entry` (cash leg) + `MarginPct × F_entry` (futures margin, owner-set). Net return = (P&L − both legs' costs on entry and exit) ÷ capital, annualised by days held; **excess** = that minus `AlternativeRate` (annual) over the same days.
- **Dividends, one-sided.** The long cash leg would receive any dividend; none is known, so the realised P&L **understates** the trade: a KEEP computed without dividends is conservative. For DROP, each trade's return is raised by the bound `MaxDividendYieldPct` (3 % a year, UNVERIFIED, setting) × days ÷ 365 first; DROP holds only if even that is below the alternative.
- **Blocks and bounds.** One block per expiry month (all instruments of an expiry share the market); `BlockBootstrap`, 10,000 draws, seed 1337, one-sided 90 %.
- **Trials.** Family `basis:carry-1`; variants `EntryDays` ∈ {20, 10} → N = 2 (`Factor` 1.25); each appended to the ledger before its numbers. Hold-out = the last 6 expiries, read once.

| Verdict | When |
|---|---|
| NOT_EVALUABLE | fewer than 8 expiries with trades; `alternative_rate_missing`; no futures rows; a bound unavailable. |
| KEEP | net costs; `MarginPct` set; lower bound of mean annualised excess (no dividends) ≥ 0.5 % a year × `Factor(N)` (a margin, UNVERIFIED starting value, option); positive in ≥ 2/3 of expiries; ≥ 8 expiries. |
| DROP | ≥ 8 expiries and the upper bound of mean annualised excess **with the dividend bound and at the best-case margin (the set value, or 0 when unset)**, gross or net, < 0. |
| INCONCLUSIVE | otherwise. |

## Acceptance criteria
1. **Hand trade.** S 1,000 → 1,010, F 1,006 → 1,010.2 over 25 days: P&L = 10 − 4.2 = 5.8 a share; capital at `MarginPct` 0.2 = 1,000 + 201.2 = 1,201.2; gross return 0.483 % for 25 days = 7.05 % a year; against `AlternativeRate` 6.5 % → excess +0.55 % a year before costs; with a TEST sheet's costs the hand net value (paise exact).
2. **One-sided dividends.** A fixture whose gross excess upper bound is −0.2 % a year without dividends but +0.4 % with the 3 % bound is not DROP; with the bound still negative, DROP.
3. **Gross and margin defaults.** Without the charge lines or `MarginPct`, a KEEP-like fixture is INCONCLUSIVE with the reasons; a DROP-like fixture is DROP.
4. **Honest states.** 7 expiries → NOT_EVALUABLE `expiries 7 of 8`; no alternative rate → `alternative_rate_missing`; no digit for a non-value in the evidence JSON.
5. **Ledger first, hold-out once, determinism, no decision use** — as P14-09 criteria 3, 7, 9, 10.

## Contract
- **Verb:** `xd basis eval --family basis:carry-1 [--holdout]` (exit 0 whatever the verdict).
- **Settings:** `Basis:EntryDays` 20, `Basis:MarginPct` unset (0.05-1), `Basis:AlternativeRate` unset (0-0.2), `Basis:MaxDividendYieldPct` 3 (0-10).
- **Data:** none new. **API:** the shared route lists `basis:carry-1`.

## Non-functional
Five years of monthly expiries for 200 instruments (about 12,000 trades) evaluated in ≤ 30 s.

## Test plan
Unit: the hand trade, the dividend bound, each verdict row. Property: determinism, order invariance, placebo not KEEP. Integration (Timescale): the verb over seeded futures rows. Architecture: no decision-path reader.

## Decisions (researched)
1. **Hold to the session before expiry.** Convergence is the trade; the expiry-day extra margin and settlement mechanics are not testable here (as D6 for options).
2. **Expiry blocks.** All instruments of one expiry share funding and market conditions; trades are not independent within it.
3. **A one-sided dividend rule.** Unknown dividends can only help the long-cash leg, so they may block a DROP but never create a KEEP.

## Risks and honest limits
- Expected verdict: DROP (P14-00 S.5: an excess of about 0.02 % against a round trip of at least about 0.25 %).
- The margin, the alternative rate and the costs are the owner's numbers; the verdict depends on them and prints them.
- End-of-day settlement prices are not fills.

## Open questions (owner)
1. Cut P14-14 and P14-15 (recommended by the plan: "cut first")? **Default: cut; the family is recorded as cut in P14-17.**
2. If kept: `AlternativeRate` and `MarginPct`? **Default: unset (NOT_EVALUABLE without the alternative rate).**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any dividend that makes a KEEP, any gross KEEP, any unavailable price dropped silently)
- [ ] No migration; ADR only if built

## Review 2026-11-10
- **Needs:** P13-05 (ADR 0183: `BlockBootstrap`, ledger-first `ExtensionTrial`, the hold-out read once) and P14-07 (ADR 0194: the `Futures` cost line) are built. P14-14 is not.
- **Phase check, built (ADR 0202):** the family `basis:carry-1` is listed. Unbuilt and uncut, it reads `none stored` (open), so the P14 exit cannot be MET until it is built and resolved or cut with `xd options phase-check --cut basis:carry-1`. The recommended cut is recorded there too.
- **Hold-out:** "the last 6 expiries, read once" is a family hold-out like the built P14-13 one (ADR 0196: the last six months), not the Lab block of ADR 0207.
- **Settings:** `Basis:*` sits under the section descriptor P14-14 adds (ADR 0246).
