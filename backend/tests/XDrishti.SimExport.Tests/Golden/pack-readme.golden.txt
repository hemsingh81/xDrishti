export xd-verify-pack-1
exploration: signal-level mechanics on past data, not evidence of an edge
fills: modelled
entry limit: +0.1 %
costs: none
portfolio limits: not simulated
sample: in-sample
universe: instruments as listed; survivorship is bounded and disclosed, not avoided (ISS-22)
data: 2 instruments; first bars 2024-04-01 to 2024-12-18, last bars 2026-04-30 to 2026-04-30; length from about 1.4 to about 2.1 years (days / 365.25, one decimal); no hold-out (ISS-19)
engine: not yet verified (P3-08)
tick: today's master tick; rounding to the grid is approximate
opening bar: the stored 09:15 bar can be widened by the CSV importer and the first 1-minute open is not the auction open (ISS-21, ISS-18)
not modelled: auction_open: The first 1-minute open of the data can be the auction price. A daily strategy enters there, and a position held overnight that gaps through a stop or a target is filled in that minute (a stop at the open, a target at its level); a repaired opening bar has a wider range, so its slippage is larger, never smaller.
not modelled: circuit_locks: Locked circuits are not modelled: there is no price-band data, so a fill at a locked price is not refused.
not modelled: touch_is_fill: A resting limit fills when the price touches it; real queues fill less often, so a fill on a target is optimistic, and the base slippage charged on it is only an approximation of what a resting order costs.
not modelled: no_queue_or_impact: There is no queue position, partial fill, market impact of the order size or liquidity cap.
not modelled: grid_approximate: The tick is today's tick from the instrument master; old prices can lie on a finer grid or off the grid, so rounding to the tick is approximate.
not modelled: minute_path: Inside one minute only the open, high, low and close are known; a minute that reaches both stop and target is taken as the stop.
not modelled: hole_is_no_trade: An absent minute and a minute with no volume are read as a minute without a trade; the order rests through it, up to the 5-minute bound.
not modelled: one_minute_entry: The entry order lives for one minute; a miss is never retried.
not modelled: no_latency: The order is live at the instant the trigger bar closes and fills at the first print of the next minute; decision and network latency are zero.
not modelled: dividends_not_modelled: Cash dividends are neither adjusted in the prices nor received: a position held over an ex-date shows the price drop without the dividend.

This folder (p3-check-1-0123456789ab) is a sheet for checking the simulator by hand against charts. Nothing in it is evidence of an edge.
seed: p3-check-1  sampler: verify-sampler-1

  index.html          the trades of the pack with a link to the card of each, and the spot checks
  cards/              one page per trade: the day's bars, the rule text and the engine's marks
  check-log.csv       your log, one row per trade and per spot check; this is the one file you edit
  engine-values.csv   the engine's side of the comparison; keep it closed until your row is written
  pack.json           the identity and the sources of the pack
  manifest.json       the size and SHA-256 of every file

The protocol in short (the full text is docs/research/p3-hand-check.md):
  1. The seed and the window were written down before the draw. Look at no trade of the window first.
  2. A bar is labelled by its start, India time. Use the 1-minute chart of the day, resampled to the timeframe of the strategy with the first bucket at 09:15.
  3. For each trade, read the levels off the chart and write the entry, stop, target and exit into check-log.csv before you open the card's marks or engine-values.csv.
  4. Tolerances: a price one tick, R 0.01, a time one minute, the exit reason exactly.
  5. At least 5 rows against an external chart, one of them a sell; for these write the open, high, low and close of the minute at the entry.
  6. Sign each row: verdict agree or disagree. A disagreement gets a finding: data, model or implementation.
  7. Run the check mode on your log to compare it with the engine. After any fix draw a new pack with a new seed.

Keep this folder on this machine.
