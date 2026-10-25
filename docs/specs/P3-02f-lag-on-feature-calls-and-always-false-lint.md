# P3-02f — A lag on a feature call (`highest(20)[1]`) and a warning for comparisons that cannot be true

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it). **Decision recorded: GO**, in two parts that can be built and merged separately (D1).
- **Phase / roadmap row:** P3 — row P3-02f (Later, optional); issue ISS-24 (traps of the rules language). Supersedes an approved restriction, so Part B needs an ADR and the owner's Approved.
- **Design refs:** docs/design/11-strategy-lab.md §2 (*Lags*, *Lint*) · docs/design/07-strategies.md §3 · docs/design/18-roadmap.md (P3-02f row, ISS-24)
- **Needs:** built: the parser, binder, lint and compiler (`XDrishti.Strategies`), the feature frame (`FeatureFrame.At`). ADRs 0034 (Amendment 8: a subscript only after a reference; the lint table), 0035 (per-session features keep the history of the session only), 0031/0032 (frame history). Spec P3-02, criterion 1.
- **Size:** 2 agent-days (roadmap size kept): Part A lint 0.7 · Part B grammar, binder, lag rules 0.8 · tests (oracle, audit, warm-up, near-miss corpus) 0.4 · docs and ADR 0.1.
- **Safety:** no. A pure library change; no data, network, order or UI write.

## Goal
An author can write "the close is above the highest high of the previous 20 bars" as `close > highest(20)[1]`, and a comparison that can never be true, which today gives a rule that silently never fires, produces a warning that names the working spelling.

## Scope
**In (Part A, no grammar change):** a lint warning `strategy.lint.never_true` for comparisons that cannot be true by construction, with the correct spelling as the suggestion; a table of value ranges of catalog features. **In (Part B):** a subscript after a catalog call or a `tf("…")`-qualified call: `highest(20)[1]`, `rsi(14)[2]`, `tf("1h").ema(20)[1]`, with the meaning of `x[k]` on a reference (the value k closed bars of the feature's own timeframe ago); the binder, lag rules, printer and the shapes the oracle and audit tests generate.
**Out:** a lag on a parenthesised expression, an arithmetic expression, a literal, a parameter or a second subscript (`(a+b)[1]`, `x[1][2]` stay `subscript_on_non_reference` / `lag_on_non_feature`); `x[0]` (stays refused: `negative_or_zero_lag`, ADR 0034 Amendment 1); a lag as a parameter; a lag on `time`; contradictions between two parts of one condition (`x > 5 and x < 3`); the language's other gaps listed in ISS-24 (3).

## Acceptance criteria
**Part A**
1. Given each of the 14 pinned impossible shapes (below) in a condition, when it is compiled, then exactly one warning `strategy.lint.never_true` is raised on the offending comparison, with its span, and a suggestion; the condition still compiles (a warning never blocks a run).
2. Given each of the 14 near-miss shapes (the nearest satisfiable comparison), then no warning is raised.
3. Given a strategy parameter that decides the comparison (`rsi(14) > rsi_hi` with `rsi_hi` = 120 declared), then the warning is raised; with `rsi_hi` = 70 it is not (parameters are constants at compile time, as for `constant_condition`).
4. The suggestion for `close > highest(n)` is `close >= highest(n, source=close)` before Part B exists and `close > highest(n)[1]` after it (a flag in the suggestion table, one line to change); for `low < lowest(n)` the mirror; for a range violation, the clamped literal or "remove the condition".
5. The 15 stored strategies compile with no new warning (a regression test over `Seed/Strategies/*.yaml`), proving the rule does not fire on real, working text.

**Part B**
6. Given `highest(20)[1]`, `lowest(5)[2]`, `rsi(14)[1]`, `atr(14)[3]`, `tf("1h").ema(20)[1]` and `nr(7)[1]`, when compiled and run on hand-calculated series, then each equals the same feature read k closed bars earlier (hand tables in the Test plan), including at the first bars (not ready until warm-up + k bars).
7. `crosses_above(close, highest(20)[1])` is true on a bar whose close first exceeds the previous 20-bar high and false on the next bar if it stays above (the previous value of the operand is read one bar further back: lags add, as for references).
8. The pinned invalid sources `rsi(14)[1]` and `tf("1h").ema(20)[1]` of `InvalidSources.cs` move from the invalid list to the valid list; `(a)[1]`, `x[1][2]`, `1[1]`, `time[1]`, `(close + open)[1]`, `highest(20)[0]`, `highest(20)[-1]`, `highest(20)[1.5]` stay refused with their present codes (`subscript_on_non_reference`, `lag_on_non_feature`, `negative_or_zero_lag`, `bad_subscript`).
9. The history depth of the plan is the lag plus the feature's warm-up as for a reference; `lag_too_deep` is raised when the lag read plus 1 exceeds 256, counting the lag of the call inside `crosses_*` and `rising`; `mixed_timeframes_in_lag` and `lag_across_session` apply to a lagged call exactly as to a lagged reference (`or_high(30)[1]` on 15m is not warned when a session holds more than 1 bar, and is unknown on the first bar of a session, ADR 0035).
10. The printer prints a lagged call back (`highest(20)[1]`), and `parse(print(tree)) == tree` holds for the generated trees that now include lagged calls (`ExpressionPrinterRoundTripTests`).
11. The look-ahead property (`LookAheadPropertyTests`: truncation and perturbation, 240 seeds, 1,200 conditions) and the interpreter oracle (`InterpreterOracleTests`, with its mutants) include lagged calls and pass; a new mutant "a lagged call read one bar nearer" fails on at least 10 seeds.
12. The compiler warm-up (`CompilerWarmup`, `CompilerWarmupTests`) covers a lagged call so the first compile stays within the budget of criterion 12 of P3-02 (warm compile at most 3 ms).

## Contract
- **Part A.** `LintCodes.NeverTrue = "strategy.lint.never_true"` (warning; the lint count goes from 16 to 17, `LintTableTests` updated); `Lint/NeverTrueRule.cs` and `Lint/ValueRanges.cs` (a closed table: `rsi`, `plus_di`, `minus_di`, `adx`, `index_vol_pct`, `vix_pct`, `breadth`: 0 to 100; `regime_vol` 0 to 2; `regime` 1 to 9; `supertrend_dir`, `engulfing`: −1 to 1; `inside_bar`, `nr`, `is_expiry_day`: 0 to 1; `day_of_week` 1 to 7; `atr`, `volume`, `rel_volume`, `prev_day_volume`, `bb_width`: at least 0; `gap_pct`: more than −100). The rule works on a comparison of one catalog feature against a constant, and on a raw bar field against a window feature (below); it sees parameters through the compile scope. It reports the whole comparison once.
- **The 14 impossible shapes** (each with a near-miss): (1) `close > highest(n)`; (2) `high > highest(n)`; (3) `close > highest(n, source=close)`; (4) `crosses_above(close, highest(n))`; (5) `low < lowest(n)`; (6) `close < lowest(n, source=close)`; (7) `rsi(n) > 100`; (8) `rsi(n) < 0`; (9) `adx(n) > 100`; (10) `volume < 0`; (11) `regime_vol > 2`; (12) `supertrend_dir > 1`; (13) `breadth > 100`; (14) `day_of_week > 7`. Near misses: `close >= highest(n)` (possible when the close is the high), `open > highest(n, source=close)` (the open is not in the window), `rsi(n) >= 100` (possible with no losses), `rsi(n) <= 0`, `adx(n) >= 100`, `volume <= 0`, and the like. Window rule: a raw field `F` of the same timeframe compared `>` with `highest(n, source=S)` is impossible when `F ≤ S` in every bar (`F = S`, or `S = high`, or `F = low`); `<` with `lowest` is symmetric.
- **Part B grammar.** `Subscript.Target` may be a `Reference` or a `Call` (including the `tf("…")` qualifier); the parser accepts `[digits]` or `[-digits]` after a call's closing parenthesis; `SubscriptOnNonReference` remains for every other target. `Binder.BindSubscript` binds the call as a feature (the same `BoundFeature` with `Lag` set, as for a reference) and applies the lag rules of `LagRules` unchanged.
- **Semantics.** `f(args)[k]` is the value of the feature `f(args)` as of the bar `k` closed bars of its own timeframe before the last closed one (`FeatureFrame.At(handle, k)`), `k` from 1 to 255. It is not the feature of a series shifted by `k`: both are the same for a rolling feature, and for a per-session feature the frame's session rule decides (unknown when `k` reaches into the previous session).
- **No API, data or settings change.** Messages and codes appear in the Lab validate response (`strategy.lint.never_true` is a warning there).
- **Log events:** none.

## Non-functional
Compile budgets of P3-02 unchanged (warm ≤ 3 ms, first after warm-up ≤ 20 ms); evaluation allocates 0 bytes per step (the allocation probes include a lagged call); the parser's work bound and nesting limit (64) unchanged. Architecture tests unchanged (the library still references `Domain` and `Indicators` only).

## Test plan
- **Part A:** `LintCases` gains 14 failing and 14 near-miss rows with the expected span and suggestion; parameter cases (criterion 3); the regression over the 15 stored strategies; the suggestion flag for Part B.
- **Part B hand tables:** a ramp `close = 1, 2, …, 30` on 1m with `high = close + 0.5` and `low = close - 0.5`: `highest(5)` at bar 10 = 10.5 and `highest(5)[1]` = 9.5; `lowest(5)[2]` at bar 10 = 3.5 (the lowest low of bars 4 to 8); `rsi(14)[1]` at bar 20 equals the RSI of bar 19 from the P3-01 RSI examples; `tf("1h").ema(20)[1]` at the 21st hourly close equals the EMA at the 20th; at bar 5, `highest(5)[1]` is not ready (needs 6 bars). `close > highest(5)[1]` is true on the first bar whose close exceeds 9.5-style prior highs and false on a flat series.
- **Oracle:** the interpreter oracle's generator and printer learn the shape; the tree audit (`Support/ExpressionAudit.cs`) accepts a `Subscript` over a `Call`; the mutants list gains "call lagged one bar nearer".
- **Syntax:** `ValidSources` / `InvalidSources` updated as in criterion 8; recovery for `highest(20)[` and `highest(20)[1`; `ParserWorkBoundTests` with a lagged call at depth 64.
- **Property:** truncation and perturbation include lagged calls; a lagged call never reads a bar after the last closed one (the existing guard).

## Decisions (researched)
**D1 — Go, in two parts.** *Reasons.* (a) The language cannot say "above the previous N-bar high" on one timeframe: `highest(n)` includes the current bar, so `close > highest(n)` and `crosses_above(close, highest(n))` never fire (ISS-24). (b) The workaround `close >= highest(n, source=close)` is a different rule (closes, not highs; the current bar is in the window; a tie counts), and the repo shows the strain: `NR7_ID_BO` spells `nr(7)[1]` as five raw high/low lags, `HIGH_52W_MOMENTUM` uses the closing form, and `FAILED_BREAKOUT` (P6-17b) cannot be written at all. (c) A rule that never fires gives zero trades and looks safe; the owner's goal is a system that is honest about that. (d) The cost is two days and an ADR. *Part A* ships first, supersedes nothing, and removes the silent failure for every author including the AI assistant (P9) that will draft strategies. *Part B* supersedes the restriction of P3-02 criterion 1 and ADR 0034 Amendment 8, so its Approved is the owner's act. Alternative rejected: no-go (leaves the strongest classic breakout unwritable); alternative rejected: a new builtin `prev(x, k)` (a second spelling of the same thing, a larger vocabulary).
**D2 — Only on a single feature call.** `x[k]` keeps one meaning: the feature's own value k closed bars ago, exactly `At(handle, k)`, which the frame already serves for references and which `crosses_*` already uses on any operand. Excluding expressions avoids the ambiguity of `(a+b)[1]` (the sum of lags or the lag of the sum; the same only for linear parts).
**D3 — `x[0]` stays refused.** A subscript asks for the past; allowing `[0]` later would not break stored text, refusing it later would (ADR 0034 decided this under delegation, kept).
**D4 — A warning, not an error.** A comparison can be impossible on purpose in a switched-off part (`x and false` is the same family); a warning with the working spelling keeps the editor useful and never blocks a run. It is also not duplicated by `constant_condition` (that rule sees literals and parameters only).
**D5 — Range table is closed and conservative.** Only bounds that the definition of the feature guarantees (RSI is in 0 to 100 with the equality cases possible; ADX is in 0 to 100; the percentile features are 0 to 100). Each row cites its definition in the catalog text. A feature without a row is never warned.
**D6 — Order.** Part A before Part B; both before P6-17b's `FAILED_BREAKOUT`. Not on the critical path of P3 or P5.

## Risks and honest limits
The range table must follow the catalog (a test compares the table's names with `IndicatorCatalog` and fails on an unknown name). The warning cannot see contradictions between two conditions of a list. A lagged call of a long window needs `n + k` bars of warm-up, which the plan's history depth and the strategy's warm-up report must show (criterion 9). Part B changes a documented restriction in doc 11 and the stored invalid-source list; stored strategies are unaffected (they could not contain the shape).

## Open questions
Owner's act: the Approved of Part B (it supersedes ADR 0034 Amendment 8). Default until then: Part A only; Part B stays unbuilt and `subscript_on_non_reference` stands.

## Done when
- [ ] All acceptance criteria have evidence (Part A and Part B separately mergeable)
- [ ] `scripts/check.sh` green; reviewer agent: no blockers or majors
- [ ] No API change; Lab validate golden updated for the new warning code
- [ ] doc 11 §2 (*Lags*, lint table, "Not yet built"), ISS-24 and the roadmap row updated; an ADR (next free number at build time) records Part B and what it supersedes; `scripts/build-docs.sh`
