# 15 — Smart Insights & Automation

How xDrishti turns data into **decisions you can act on**, for both trading and portfolio management —
without taking decisions away from you. Everything here is deterministic rules and statistics evaluated by the
services; the LLM only explains ([doc 14](14-ai-assistant.md)).

## 1. Where the system is "smart"

| Area | Smart behaviour | Where you see it |
|------|-----------------|------------------|
| Next-day plan | Learned setups, calibrated probabilities, top-N selection ([docs 09–10](09-learning-engine.md)) | Plan review |
| **Pre-trade checklist** | Every proposed ticket is checked against your holdings, sector limits, results dates and data quality | Plan review (per ticket) |
| **Portfolio Action center** | Rules flag positions that need a decision, per account | Portfolio |
| **Price alerts** | Your alerts are watched by the services, not your browser | Portfolio, notifications |
| **Data quality gate** | Instruments failing validation are skipped by the nightly scan and flagged on tickets | Data quality, Plan review |
| **Report auto-insights** | Best/worst strategy, basket gap, side bias, best exit, regime, weekday, calibration — in plain sentences | Reports → Overview |
| **Schedule intelligence** | Dependency and timing checks; run-after chains; catch-up after sleep; automatic data failover | Services, Accounts |
| **Decision feedback** | Your approvals/rejections are graded against the system's proposal | Reports → Decisions |
| **Auto-approval (autonomy)** | Tickets above your probability threshold with every check passing are approved at the cutoff | Plan review, Learning → Autonomy |
| **Lessons & execution feedback** | Problems detected by monitors become lessons with a fix and a measured result; grading uses real fills | Learning |
| **Tweak & re-run** | Change a learning knob and replay 12 months walk-forward with honest comparison rules | Learning → Tweak & re-run |
| **Smart rules** | Market gate (no-trade days), equity-curve throttle, meta-labelling, learned entry windows, correlation-aware plan ([doc 09](09-learning-engine.md) §14) | Plan review banner & tickets, Learning → Smart rules |
| **Graded on fills** | Live and paper trades graded at executed prices; R at plan prices kept for the execution gap | Today, Reports → Trade list, Learning → Plan vs actual |
| **Automatic loss tags** | Every loss tagged (gap, gave back profit, against regime, late entry, slippage, news) and linked to a lesson | Learning → Lessons, Reports → Trade list |
| **Weekly learning digest** | One summary instead of daily approvals | Today, Learning, notification |

## 2. Pre-trade checklist (per proposed ticket)

| Check | Rule | Result |
|-------|------|--------|
| Data quality | Instrument's latest validation status | ✗ fail blocks one-click approval (you can still approve with a confirmation, and it is recorded) |
| Holdings conflict | SELL ticket on an instrument you hold long in any account; or an open trade already exists | ⚠ |
| Sector exposure | Open trades + already-approved tickets in the same sector ≥ profile limit | ⚠ |
| Event risk | Quarterly results within 3 days | ⚠ |
| (later) Correlation | 60-day correlation with open trades > 0.7 | ⚠ |

Checks are shown on the ticket card; "approved despite" reasons are stored and appear in the Decisions report.

## 3. Portfolio Action center

Evaluated per account by the **Price alerts & action center** service (every minute in market hours, and after
each sync). Thresholds are configurable (Settings → Action center thresholds).

| Rule | Default trigger | Suggested action |
|------|-----------------|------------------|
| Below cost | Unrealised ≤ −10% (high if ≤ −20%) | Hold with a stop alert, average only if the thesis holds, or exit |
| Large gain | Unrealised ≥ +25% | Book part of the gain or set a trailing-stop alert |
| Overweight | Weight > max(15%, 1.5 × equal-weight share) of the account (≥ 3 positions) | Trim or rebalance |
| Sector concentration | One sector > 35% of the account | Diversify |
| Max holding exceeded | Trading-bucket position older than the 5-session limit | Exit per plan rules |
| Results due | Results within 7 days | Decide before the event |
| Long-term soon | Profitable holding becomes > 12 months within 30 days | Check tax impact before selling early (informational only) |
| Lagging Nifty | Held > 180 days and XIRR more than 10 pp below the same cash flows in Nifty 50 | Review the thesis / replace |
| Not in a basket | Holding not assigned to any basket | Assign for basket analytics |

Each item offers **Open**, **Set alert**, **Add to basket**, **Snooze 7 days** or **Done**; every action is
audited. Per-account tiles on the Portfolio page show value, P&L, XIRR, day change, top holding and the number
of open action items, so you can focus on the account that needs attention.

## 4. Price alerts

- Conditions: price **at or below** / **at or above** a level (quick presets ±5/10/20%), optional note.
- Evaluated by services: alert symbols join the polled set (or the live set if already streamed); triggered
  alerts create a notification and audit entry. Alerts survive logout and browser close.
- Later: indicator alerts (e.g. close below 50 DMA), trailing alerts, alert on basket-level drawdown.

## 5. Data quality gate

After every end-of-day fetch the **Data validation** service re-checks the last N sessions of every tracked
instrument ([doc 05](05-data-and-brokers.md) §10b). Results drive:
- a data quality score (pass = 1, warning = ½) shown on the Data quality page and Today,
- **per-instrument fail-closed**: failing instruments are skipped by the nightly scan and flagged on any ticket,
- one-click repairs (re-fetch the day, refresh aggregates) followed by automatic re-validation.

## 6. Report auto-insights

Computed from graded trades for the current filters (minimum 15 trades per group):
best and weakest strategy (pause candidate), best vs worst basket gap, long vs short bias, best exit policy,
most/least favourable regime, best/worst weekday, calibration quality (predicted vs actual win rate).
The local LLM can turn these into the weekly review text; numbers always come from the report engine.

## 7. Schedule intelligence

- Rules checked on every schedule change: pre-open check after the review cutoff and before 09:15 (and ideally
  after NSE pre-open price discovery ~09:08); EOD fetch after 15:35; nightly pipeline after EOD fetch and
  portfolio sync; live window covering the session; no backups in market hours; no circular "run after" chains.
  Errors block saving; warnings are shown.
- **Run-after chains** (e.g. data validation after EOD fetch; AI suggestions after the nightly pipeline).
- **Catch-up** of missed runs after the machine wakes; **automatic data failover** to the standby Data account.

## 8. Further smart features (roadmap candidates)

| Idea | Value |
|------|-------|
| Rebalancing to basket target weights | Suggested buy/sell list to bring long-term baskets back to targets |
| Regime-aware sizing | Scale risk per trade down in high-volatility regimes (learned) |
| Correlation clusters | Avoid hidden concentration across accounts |
| What-if on approvals | Show portfolio heat / sector exposure if you approve a ticket |
| Post-trade review prompts | Journal questions after each closed trade; discipline score |
| Tax-lot aware selling | Prefer lots that minimise short-term gains (informational) |
| Earnings & corporate-action calendar | Feed event filters and alerts |
| Dividend tracking in XIRR | Dividends from ledger as cash flows |
| Volatility-scaled position sizing | Size so each trade risks the same rupees after slippage, capped by liquidity (capacity) |

