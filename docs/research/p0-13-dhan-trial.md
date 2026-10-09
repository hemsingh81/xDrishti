# P0-13 — Dhan Data API trial

- **Spec:** [P0-13](../specs/P0-13-dhan-trial-api-key-secret-token-flow-and-1-minut.md) · **Status:** tool built and tested offline; **live run pending** (owner runs it; see below)
- **Tool:** `backend/spikes/XDrishti.DhanTrial` (read-only, allow-listed endpoints, Keychain credentials, redacted output)

## How to run (owner, in your own terminal)
```bash
scripts/dhan-secrets.sh            # once: client id, API key, API secret (hidden prompts, stored in the Keychain)
scripts/dhan-trial.sh run          # after market close: login, profile, renew, master, history, analysis → data/trial/report.md
scripts/dhan-trial.sh run --try-totp   # optional: also test the PIN + TOTP (no-browser) route, on its own token file
scripts/dhan-trial.sh run --burst      # optional: small over-limit burst (8 calls, no retries); skipped by default because Dhan may block abusive callers
scripts/dhan-trial.sh renew        # next morning: renew the stored token and log how much lifetime was left
scripts/dhan-trial.sh analyze      # re-analyse the cached responses offline
```
Browser step of `run`: the tool prints a Dhan login URL; log in (2FA), copy the `tokenId` value from the address bar
after the redirect, paste it at the hidden prompt. Share only `data/trial/report.md` (contains no secrets); sanitised
samples are in `data/trial/samples/`.

## Safety properties (tested)
- Only authentication, profile, instrument-master and historical-candle endpoints can be called (allow-list; orders, positions, funds, IP setup are rejected before sending). HTTP redirects are disabled.
- Credentials come from the Keychain; the access token is stored outside the repository in a mode-600 file; every printed or written string is redacted; nothing under `data/trial/` contains a secret (asserted in tests).
- Dhan constraint worth knowing: `generateAccessToken` takes the PIN and TOTP **as query parameters** and `consumeApp-consent` takes the `tokenId` the same way. They are masked in all output, but they do travel in URLs — one more reason to prefer the consent flow unless unattended renewal needs TOTP.
- The report is reproducible: a live run saves `run.json` (date, profile, limits, probes) so `analyze` on any later day gives the same tables from the cached responses.

## What the Dhan documentation says (read 2026-10-09; the trial verifies each point)
| Topic | Documentation | To verify |
|---|---|---|
| Token routes | Web-generated token (24 h) · `POST auth.dhan.co/app/generateAccessToken` (PIN + TOTP) · consent flow: `generate-consent` → browser login with 2FA → `consumeApp-consent` | Which routes work for a headless daily job |
| Renewal | `GET /v2/RenewToken` (headers `access-token`, `dhanClientId`); "only for tokens generated from Dhan Web"; expired tokens cannot be renewed | Does it work for a consent-flow token? How much lifetime may remain? |
| Profile | `GET /v2/profile` → `tokenValidity`, `dataPlan`, `dataValidity` | Data plan active on the primary account |
| Intraday history | `POST /v2/charts/intraday`, intervals 1/5/15/25/60, 90 days per request, 5 years deep | Real depth, rows per request, timestamp base and bar label |
| Daily history | `POST /v2/charts/historical`, `toDate` non-inclusive, back to inception | Matches the day derived from 1-minute bars? |
| Instrument master | `https://images.dhan.co/api-data/api-scrip-master.csv` (compact) | Column names; index rows; NSE_EQ / IDX_I mapping |
| Limits | Data APIs 5/second, 100,000/day; errors DH-904 / 805 throttling, DH-902 / 806 not subscribed, DH-907 no data | Observed throttling behaviour and latency |
| Static IP | Mandatory only for order APIs | Not needed for this trial |

## Findings
_To be written after the live run (acceptance criteria 1–7), with the report attached or summarised here._

| Question | Answer |
|---|---|
| Token route usable unattended? | — |
| Renewal works for a consent-flow token? Lifetime left when renewed? | — |
| Data API plan active? | — |
| Timestamp meaning (epoch base, bar start/end) | — |
| Raw or adjusted prices? Volume units (indices)? | — |
| 1-minute depth actually available | — |
| Gaps / completeness on the 20 symbols | — |
| Daily vs derived-from-minute mismatches | — |
| Rate limit behaviour; EOD fetch for ~200 instruments | — |
| Design impact (docs 05/17, ADR?) | — |
