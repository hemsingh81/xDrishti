# 19 — Risks & Compliance

## 1. Research risks

| Pitfall | Countermeasure |
|---------|----------------|
| Overfitting | Walk-forward OOS only, ≤ 4 params, plateaus, min samples, paper stage |
| Multiple-testing luck | Test ledger, deflated thresholds, consistency across folds/years |
| Look-ahead bias | Closed-bar-only DSL, point-in-time features, single code path, automated tests |
| Survivorship bias | Historical constituents incl. delisted stocks |
| Intrabar ambiguity | Resolve on 1-minute path; if still ambiguous assume the worse outcome |
| Unrealistic fills | Gap fills at open, slippage model, no fills at locked circuits |
| Cost blindness | Costs in every simulation; `max_cost_R` filter |
| Regime dependence | Regime-conditioned stats, drift monitor, auto-pause |
| ML leakage | Purged & embargoed walk-forward |

## 2. Market risks

| Risk | Countermeasure |
|------|----------------|
| Overnight gaps (swing) | Heat cap, event exits, max hold ≤ 5 sessions |
| Correlated losses | Sector/correlation limits, net exposure caps |
| Liquidity/slippage | Turnover filters; live slippage measured |
| Extreme days | Daily loss limit, gap/VIX filter, max-drawdown halt |
| Short constraints | Swing shorts via futures only |
| F&O ban, surveillance, margin changes | Nightly exclusion filters |

## 3. Behavioural risks

| Risk | Countermeasure |
|------|----------------|
| Skipping/adding trades | Auto-journal + discipline report quantifies deviations |
| Tweaking after every loss | Changes only via hypothesis loop; versioned config |
| Over-trusting LLM prose | Numbers only from tools; LLM never decides trades |
| Scaling too early | Paper ≥ 12 weeks, then reduced-size live |

## 4. Operational risks

| Risk | Countermeasure |
|------|----------------|
| Bad data → wrong plan | Quality gates; fail closed |
| Misconfigured schedules (e.g. pre-open before cutoff, nightly before data) | Dependency & timing rules block invalid schedules; timeline view; catch-up |
| Automation over-reach (system changes too much on its own) | Autonomy levels; weekly cap on automatic changes; always-ask list (risk increases, new strategies, live execution, accounts); change log with revert; automatic rollback after 60-trade monitoring |
| Overfitting through repeated experiments | Required improvement rises with trials (multiple-testing guard); 12 OOS folds; judged after live gap and on total R; shadow before apply |
| Smart rules filtering too much (e.g. too many no-trade days) or learned on noise | Evidence shown per rule; review flag when a rule removes > 50% of trades or its skipped trades are not worse than taken ones; rules learned from walk-forward data, not only live trades |
| Learning from plan prices that cannot be achieved | Grade on actual fills; slippage model per liquidity bucket; capacity score |
| Alert fatigue (too many action items) | Smarter thresholds (relative to account size), severity filter, snooze/done, per-account focus |
| Plan not reviewed in time | Unreviewed tickets lapse (safe default); reminders before cutoff; phone review via PWA |
| Account role misconfiguration (no data account, wrong trading account) | Validation rules (exactly one primary Data account), password re-entry, audit, Services screen warnings |
| Unauthorised access | Login (strong password), lockout, idle timeout, LAN-only, audit of logins |
| CSV import errors (wrong timezone, bar-end labels, adjusted vs raw prices, duplicates) | Import profiles, preview before commit, session-boundary checks, conflict log, undo per import job |
| Live-feed overload or disconnects | Live data only for the active set (cap `max_ws_instruments`); auto-reconnect; EOD official data replaces live bars |
| Incomplete portfolio history (API history window, other brokers) | CSV/manual transaction import; reconciliation of computed vs broker holdings each night |
| Misleading XIRR on short holdings | Absolute return below `min_days_for_xirr`; XIRR flagged "short period" |
| Silent job failure | Hangfire retries + in-app notification + Seq alerts |
| Data loss | Nightly restic backups; monthly restore test |
| Secret leakage | Keychain → Podman secrets; never in config/logs/LLM |
| Broker API changes / token expiry | Adapter isolation, retries, fallback provider, token status on Accounts screen |
| Mac asleep/offline | Power settings, UPS, wired network; broker-side stops once execution exists |
| VM disk/memory exhaustion | Machine sizing, Timescale compression, disk alerts, external SSD for ticks |
| Runaway automated orders (later) | Pre-trade checks, ≤ 2 orders/s, idempotency, kill switch, reconciliation freeze |
| Third-party agent vulnerabilities (Hermes) | Optional only; hardened container; pinned version; CVE checks |

## 5. Compliance (India)

- **Personal use only**: one user, own accounts. Sharing or selling recommendations could require SEBI
  Research Analyst / Investment Adviser registration — out of scope by design.
- **SEBI retail algo framework** (mandatory since 1 April 2026) applies when orders are placed via broker APIs:
  registered app on a **whitelisted static IP**, daily login/2FA, broker as principal. Own strategies staying
  **≤ 10 orders/second per exchange** are tagged with the broker's generic algo ID; above that, exchange
  approval is needed. v1 places no orders; Phase 12 caps at 2 orders/s. Re-check circulars before Phase 12.
  Static-IP decision (Q6, 2026-10-09): not needed until Phase 12; ask the ISP and re-check circulars at the P11 exit.
- Data APIs used within the broker's terms.
- Forex/CFD trading via offshore platforms (e.g. MT5 brokers) is not part of this system.

## 6. Expectations

Good systematic setups typically show 40–60% win rates and +0.1R to +0.4R expectancy after costs. Backtests
showing 80% wins and +1R/trade are almost certainly overfit or leaking. Losing weeks and multi-week drawdowns
are normal; the Monte Carlo report shows what to expect. The value is consistency, risk control and learning
from evidence.
