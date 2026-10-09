# P0-13 — Dhan trial: API key/secret token flow and 1-minute history for 20 symbols

- **Status:** Approved
- **Phase / roadmap row:** P0 — Foundations & data trial
- **Design refs:** docs/design/05-data-and-brokers.md §1, §6, §7, §8, §10; 04 §1; 02 FR-1, NFR-2, NFR-7, NFR-8; 19 §4

## Goal
Find out, with real calls and before building the P1 adapter, what the Dhan Data API actually does: how the 24-hour
token is obtained and renewed, what 1-minute history looks like, and what its limits are. The findings decide whether the
unattended end-of-day fetch (15:50 IST) is feasible as designed and feed the data-quality note (P0-17) and the P1
adapter stories (P1-03, P1-04, P1-12).

## Scope
**In**
- A throwaway **trial tool** that, read-only: authenticates, downloads the instrument master, fetches 1-minute (and daily)
  history for 20 symbols, and analyses the result.
- A **findings document** and **sanitised sample responses** (no secrets) usable as recorded fixtures for P1-04 contract tests.
- A secure way for the owner to supply credentials (Keychain), and a single command the owner runs.

**Out (not in this slice)**
- Any order, position-changing or funds-moving endpoint. Live WebSocket feed (P2-07), account sync (P2-01).
- Importing the data into the database or the production adapter (P1-03/04/12). Nothing is written to PostgreSQL.
- The CSV profile (P0-14).

## Approach (recommended — change it if you prefer another)
- Location: `backend/spikes/XDrishti.DhanTrial` (console) and `XDrishti.DhanTrial.Tests`, in the solution under a
  `/spikes/` folder so `scripts/check.sh` builds, formats and tests them under the normal rules. Not shipped in images.
  Pure logic (bar-count/gap detection, timestamp-semantics checks, daily-vs-derived comparison) is separated from
  HTTP so it is unit-tested with synthetic bars. When P1 starts, useful pieces are promoted into `XDrishti.Infrastructure`
  behind the broker interfaces in doc 05 §7; the rest is deleted.
- Credentials: the owner stores them in the macOS Keychain under `xdrishti/dhan/main/*` with a helper that reads
  hidden input (`scripts/dhan-secrets.sh`), and runs `scripts/dhan-trial.sh` in their own terminal. **The agent never
  sees credentials or tokens**; it builds and tests everything offline, and works from the redacted report the tool writes.
- Output goes to `data/trial/` (git-ignored). Only the written findings and small sanitised samples are committed.

## Acceptance criteria
1. **Token flow documented and working.** Using the API key/secret flow for the primary Data account, a token is
   obtained and then renewed **before** its 24-hour expiry. The findings state exactly which steps are needed, whether
   renewal works **unattended** (no interactive login/2FA), the token's real lifetime and what an expired or invalid
   token returns. If unattended renewal is not possible, the findings propose the fallback (e.g. a daily reminder and
   one-tap login) and record it as a risk for P1-04/P1-12.
2. **Instrument master.** The master is downloaded; the 20 symbols resolve to Dhan `security_id` + segment and a
   canonical symbol (doc 05 §7). Ambiguous or missing mappings are listed, never guessed.
3. **1-minute history for 20 symbols.** For each symbol, 1-minute candles are fetched for a recent window (default: the
   last 30 trading days). For 3 symbols a deeper window (default: 12 months) is fetched to measure chunking and how far
   back 1-minute data really goes (doc 05 assumes ≤ ~5 years). The tool is resumable and idempotent.
4. **Gaps and quality measured.** Per symbol and day the tool reports bars vs the expected session count
   (09:15–15:30 IST), missing minutes, zero-volume bars, duplicates, out-of-order or out-of-session timestamps, and
   price sanity (high ≥ low, OHLC within range). Results are summarised in a table in the findings.
5. **Semantics confirmed.** The findings state, with evidence: timestamp meaning (bar start or end), timezone/epoch
   format, raw vs adjusted prices (check across a known split/bonus if one is in the symbol list), and volume units
   (including for indices).
6. **Daily candle cross-check.** For each symbol, the official daily candle is compared with the day derived from
   1-minute bars (open, high, low, close, volume); mismatches are counted and explained.
7. **Limits measured.** Rate limits (per second/minute/day if any), maximum range and rows per request, error codes
   (throttling, no-data, bad token), and observed latency. From these the findings **extrapolate the EOD fetch time for
   ~200 instruments** and compare it with NFR-2 (nightly cycle < 45 min), with a chunking/throttling recommendation.
8. **Safe and read-only.** The tool calls only market-data/instrument endpoints; a test asserts that no order/position/
   fund endpoint is reachable from the client. Secrets and tokens are never logged, printed, written to files or exposed in
   errors (a test redacts known patterns); nothing under `data/` is committed.
9. **Feeds the design.** The findings are in `docs/research/p0-13-dhan-trial.md`; any contradiction with doc 05/17
   (limits, token flow, history depth) updates those docs and, if an accepted decision changes, adds an ADR. 3–5 small
   sanitised response samples are committed under `docs/research/dhan-samples/` for P1-04 fixtures. P0-17 can consume the
   findings directly.

## Contract
- **API / Data / UI / Jobs:** none in the product. Dhan endpoints used (read-only): instrument master, intraday and
  daily historical candles, plus whatever the token flow requires — the exact list is recorded in the findings.
- **Files (expected):** `backend/spikes/XDrishti.DhanTrial*`, `scripts/dhan-secrets.sh`, `scripts/dhan-trial.sh`,
  `docs/research/p0-13-dhan-trial.md`, `docs/research/dhan-samples/`, small updates to `backend/XDrishti.slnx`,
  `.gitignore` (if needed), doc 05, `scripts/README.md`.
- **Symbols (proposed 20):** 14 liquid large caps (e.g. RELIANCE, HDFCBANK, ICICIBANK, INFY, TCS, SBIN, ITC, LT, AXISBANK,
  BHARTIARTL, KOTAKBANK, HINDUNILVR, MARUTI, SUNPHARMA), the NIFTY 50 and BANKNIFTY indices, **and 4 of your own
  names** from MyChoice/Best stocks — ideally one mid/small cap that trades thinly (gaps), and one with a recent
  split/bonus (adjustment check).

## Non-functional
- Security (NFR-8): Keychain only; hidden input; redaction; no secrets in repo, logs, prompts, URLs or error messages.
  Use the broker within its terms: data stays local, no redistribution.
- Politeness: respects the measured rate limit with back-off; the 20-symbol run should finish in minutes, not hours.
- Reproducible: same inputs give the same report; raw responses cached in `data/trial/raw/` so analysis can be re-run offline.
- Code follows backend rules (warnings as errors, `CancellationToken`, `TimeProvider`, no static state), even though it is a spike.

## Test plan
- **Unit (offline, no network):** session-minute counting incl. half-day/special sessions; gap/duplicate/out-of-order
  detection; timestamp-semantics heuristics; daily-vs-derived comparison; JSON parsing against the committed
  sanitised samples; redaction of secrets/tokens; allow-list of endpoints rejects order/fund paths.
- **Live (owner-run):** `scripts/dhan-trial.sh` produces `data/trial/report.md` with the tables for ACs 1–7;
  re-running without network analyses the cached responses and produces the same report.
- `scripts/check.sh` stays green. A secret-pattern scan of the diff finds nothing.

## Decisions (owner, 2026-10-09)
1. Data API subscription: assumed active on the primary Data account; the trial's profile check (`dataPlan`) confirms it.
2. Extra symbols: "pick for me" — TATAPOWER, PERSISTENT, ZENSARTECH (thinly traded), GRSE; edit `symbols.json` to change.
3. One account only (the primary Data account).
4. The owner runs the live command and shares only the redacted report.
5. Run after market close, then once more the next morning.

## Research notes (Dhan docs, read 2026-10-09 — the trial verifies each against reality)
- Token routes: web-generated token (24 h); `POST auth.dhan.co/app/generateAccessToken` with PIN + TOTP (fully scriptable);
  API key/secret consent flow (browser login with 2FA, then `consumeApp-consent`). `GET /v2/RenewToken` is documented as
  working only for web-generated tokens — the trial checks whether it works for a consent-flow token.
- `GET /v2/profile` returns `tokenValidity` and `dataPlan`/`dataValidity` (used to answer decision 1).
- History: `POST /v2/charts/intraday` (intervals 1/5/15/25/60, 90 days per request, 5 years deep) and
  `POST /v2/charts/historical` (daily). Timestamp base is not documented — the trial determines it.
- Documented limits: Data APIs 5/second and 100,000/day. Errors: DH-904 / 805 throttling, DH-902 / 806 not subscribed.

## Done when
- [ ] All acceptance criteria have evidence (report + findings document)
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Findings reflected in doc 05/17 (and an ADR if a decision changed); samples committed sanitised
- [ ] Plan updated: P0-13 done; P0-17 (data-quality note) unblocked for the Dhan part
