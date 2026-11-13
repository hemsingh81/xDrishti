# 0288 — P13 (tick and depth features) is dropped; the depth and tick recorders are removed; the feature-set comparison stays

- **Status:** Accepted (owner, 2026-11-13, in chat: "drop and remove").
- **Date:** 2026-11-13.
- **Relation to other ADRs:** Amends [0173](0173-the-trade-tick-recorder-stores-last-trade-updates-of-the-depth-set-when-cumulative-volume-rises-on-a-second-quote-socket-behind-three-gates-its-own-cap-and-no-reader.md) (the trade-tick recorder), [0135](0135-the-depth-recorder-stores-the-20-level-book-of-up-to-50-shares-about-once-a-second-behind-three-gates-a-disk-guard-and-no-reader.md) (the depth recorder) and [0183](0183-the-feature-set-comparison-fits-one-recipe-twice-on-identical-rows-widens-its-margins-for-the-ledger-count-and-reads-the-hold-out-once.md) (the comparison core).

## Context
P13 asked whether order-book and tick features add out-of-sample value, from at least six months of data. The system had no history of its own, so P7-16 and P7-16b recorded the 20-level book and the trade ticks live, to be used about eleven months later.

## Research facts
- Dhan sells live depth only; it has no historical depth or tick data.
- NSE historical tick and depth data is sold to institutions, not at a personal-user price.
- Other vendors were not verified.
- The remaining route, recording for about eleven months before the first look, was offered and **declined**.

## Decision
1. P13 is dropped. P13-00 is marked Dropped; the other P13 slices were never built and have no spec on disk.
2. The depth recorder (P7-16) and the trade-tick recorder (P7-16b) are removed: services, supervisors, stores, ports, the Dhan 20-level connector and parser, the owner switches and their endpoints (`/api/depth/recorder`, `/api/ticks/recorder`), the `Depth:*` and `Ticks:*` options, the compose settings, the two Services rows and the frontend cards.
3. The 20-level depth socket (`wss://depth-api-feed.dhan.co/twentydepth`) leaves the pinned allow-list; the list only shrinks.
4. One migration, `DropDepthAndTickRecording`, drops `market.depth_snapshots`, `depth_session_instruments`, `depth_sessions`, `depth_recorder_switch`, `trade_ticks`, `tick_session_instruments`, `tick_sessions`, `tick_recorder_switch` and the two recorders' service rows and heartbeats. The applied migrations that created them are untouched. It is not reversible: the recordings are gone.
5. Pieces the other recorders shared were renamed, not removed: the state words, the disk guard and the end reasons are now `RecorderStates`, `RecorderDiskGuard` and `RecorderEndReasons` (`XDrishti.Application.Recording`). The opening-minute recorder has its own free-space floor, `OpeningBars:MinFreeGb` (5-500, default 20), instead of the shared `Depth:MinFreeGb`.

## What stays
- P13-05, the feature-set comparison core (`research.trade_features_ext`, `extension_evaluations`, `Extension*Store`, the two extension migrations): P14 and P15 use it (ADR 0183).
- The BK-03b opening-minute bar recorder (ADR 0269): it is not P13 and has its own connector and stores.
- The `AccountEventType` values `DepthRecorderSwitched` and `TickRecorderSwitched`: audit rows already carry them.
- The applied migrations `AddDepthRecording` and `AddTickRecording`, as history.

## Consequences
- Any later wish for microstructure features needs a new decision and a data source the owner can buy.
- The allow-list is one entry shorter; no new egress.
