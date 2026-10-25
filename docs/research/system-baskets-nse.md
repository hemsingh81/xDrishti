# Research: predefined system baskets for xDrishti (NSE)

Status: research only, no code. Date of probes: 2026-10-25 (all URL probes done with `curl -A "Mozilla/5.0"`).
Context: ADR 0014 (system baskets are read-only, `kind` field, `baskets.system` 409), doc 06 §3.1/§3.4 (dated membership
`valid_from`/`valid_to`, `asOf`), ISS-22 (no historical index membership; runs say "members as of today"), doc 05 (Dhan
instrument master with `ASM_GSM_FLAG`, F&O flag from active stock futures, daily + 1m bars, `market.bars_agg`).

Legend: VERIFIED = opened/fetched in this research. UNVERIFIED = not opened or only known from secondary sources.

---------------------------------------------------------------------------------------------------------------------

## 0. Recommendation in one screen

1. Ship **two families** of system baskets, both read-only, both with dated membership:
   - `kind = index` (official NSE index constituents, fetched weekly, change events dated).
   - `kind = derived` (computed from our own bars, recomputed nightly/weekly, dated by `as_of`).
   - Also `kind = list` for regulatory lists (F&O universe, ASM/GSM, ban list) because they are not indices.
2. Start with the **free, verified CSV set** (Section 1.2). Everything below is a plain public CSV, no login, no cookie.
3. **Do not claim historical index membership.** Store forward-only dated membership from the first fetch (Section 3).
   Behaviour baskets, by contrast, can be rebuilt point-in-time from our own bars (Section 2), which is the only honest
   way to get a bias-free "universe on date D" for simulations.
4. Licensing is the weak spot: constituent CSVs are free to download, but redistribution / commercial use terms could
   not be read (Section 1.5). Personal single-owner, local use is the safe reading; do not republish or sell.

---------------------------------------------------------------------------------------------------------------------

## 1. Official index and list baskets

### 1.1 Common facts (VERIFIED unless noted)

- Methodology (single consolidated PDF, "Nifty Indices Methodology Document", September 2026 edition, 345 KB-class, ~340 pages):
  https://www.niftyindices.com/Methodology/Method_NIFTY_Equity_Indices.pdf (downloaded and text-extracted).
- **Broad market indices review: semi-annual, based on six-month average data for the months ending January and July.**
  Quote of substance: "review ... undertaken semi-annually based on data for six months ending January and July";
  Nifty 50 replacements are implemented "from the last trading day of March and September", announced to participants
  in advance (typically ~4 weeks; the document states a minimum 5 working days' notice for reconstitutions and
  3 working days for corporate-action-driven changes). So effective dates are approx. the last trading day of March
  and September; announcement approx. late Feb and late Aug. Exact announcement lead time per index: UNVERIFIED.
- **Ad hoc changes** (merger, demerger, delisting, suspension, capital restructuring) happen between reviews with
  minimum 5 trading days notice (3 working days for some corporate actions). Hence a semi-annual refresh alone is not
  enough; poll weekly (Section 3.3).
- **Strategy indices differ**: Nifty Alpha 50 and High Beta 50 methodology (Sept 2019 PDFs, VERIFIED) say reconstitution is
  **quarterly**, replacements "from the first working day after F&O expiry of March, June, September and December"
  (Alpha 50 text). Nifty Momentum/Quality/Low-Vol families are semi-annual (June/December) per NSE press release
  https://niftyindices.com/Press_Release/ind_prs25082020.pdf (search snippet only; the PDF itself UNVERIFIED).
- Constituent file columns (identical for every index CSV tested): `Company Name,Industry,Symbol,Series,ISIN Code`.
  No weights, no effective date, no free-float column in these CSVs. Weights are in the factsheet PDFs only
  (https://www.niftyindices.com/Factsheet/Factsheet_Nifty_Alpha50.pdf, VERIFIED to exist via search; layout UNVERIFIED).
- HTTP behaviour (VERIFIED): `HTTP/2 200`, `content-type: text/csv`, strong cache validators `ETag` and `Last-Modified`
  (nifty50 list: `last-modified: Sat, 24 Oct 2026 03:31:20 GMT`). So conditional GET (`If-None-Match`) works and is the
  right polling primitive. Two hosts serve identical bytes (same 3344 bytes for Nifty 50):
  - `https://niftyindices.com/IndexConstituent/<file>.csv`
  - `https://nsearchives.nseindia.com/content/indices/<file>.csv` (also `archives.nseindia.com/content/indices/...`).
  Preferred: nsearchives (static CDN, no WAF cookie dance; the niftyindices.com HTML pages and `WebFetch` timed out,
  while `curl` with a browser User-Agent to the CSV paths returned 200).
- Row counts include the header (VERIFIED, 2026-10-25).

### 1.2 Verified CSV catalogue

URL base A = `https://nsearchives.nseindia.com/content/indices/`; base B = `https://niftyindices.com/IndexConstituent/`.
"on A/B" = which host returned 200 (VERIFIED). Members = rows minus header.

| Basket (suggested name) | File | Host | Members | Selection rule (summary; source) | Review cadence |
|---|---|---|---|---|---|
| Nifty 50 | `ind_nifty50list.csv` | A,B | 50 | Top 50 by free-float mcap from eligible universe (F&O-eligible stocks only; methodology doc) | semi-annual Mar/Sep |
| Nifty Next 50 | `ind_niftynext50list.csv` | A | 50 | Next 50 by ff-mcap after Nifty 50 within Nifty 100 | semi-annual |
| Nifty 100 | `ind_nifty100list.csv` | A | 100 | Nifty 50 + Next 50 | semi-annual |
| Nifty 200 | `ind_nifty200list.csv` | A | 200 | ranks 1-200 by average full mcap | semi-annual |
| Nifty 500 | `ind_nifty500list.csv` | A | 501 (header+500 = 502 lines; trailing line count quirk, verify at ingest) | ranks 1-500 | semi-annual |
| Nifty Midcap 150 | `ind_niftymidcap150list.csv` | A | 150 | ranks 101-250 of Nifty 500 | semi-annual |
| Nifty Smallcap 250 | `ind_niftysmallcap250list.csv` | A | 250 (251 data lines incl. possible blank) | ranks 251-500 | semi-annual |
| Nifty MidSmallcap 400 | `ind_niftymidsmallcap400list.csv` | A | ~400 | midcap 150 + smallcap 250 | semi-annual |
| Nifty LargeMidcap 250 | `ind_niftylargemidcap250list.csv` | A | 250 | Nifty 100 + Midcap 150 | semi-annual |
| Nifty Microcap 250 | `ind_niftymicrocap250_list.csv` (note underscore) | B | ~250 | ranks 501-750 | semi-annual |
| Nifty Total Market | `ind_niftytotalmarket_list.csv` (underscore) | B | 755 | Nifty 500 + Microcap 250; already used by our sector enrichment (doc 06 §2.1) | semi-annual |
| Nifty Bank | `ind_niftybanklist.csv` | A | 14 | liquid, large private+PSU banks | semi-annual |
| Nifty Financial Services | `ind_niftyfinancelist.csv` | B | 20 | top 20 financial-services stocks | semi-annual |
| Nifty Financial Services 25/50 | `ind_niftyfinancialservices25_50list.csv` | A | 20 (see note) | capped variant | semi-annual |
| Nifty IT | `ind_niftyitlist.csv` | A | 10 | top 10 IT | semi-annual |
| Nifty Pharma | `ind_niftypharmalist.csv` | A | 20 | | semi-annual |
| Nifty Auto | `ind_niftyautolist.csv` | A | 15 | | semi-annual |
| Nifty FMCG | `ind_niftyfmcglist.csv` | A | 15 | | semi-annual |
| Nifty Metal | `ind_niftymetallist.csv` | A | 15 | | semi-annual |
| Nifty Energy | `ind_niftyenergylist.csv` | A | 40 (A shows 41 lines) | | semi-annual |
| Nifty Realty | `ind_niftyrealtylist.csv` | A | 10 | | semi-annual |
| Nifty Healthcare | `ind_niftyhealthcarelist.csv` | A | 20 | | semi-annual |
| Nifty Consumer Durables | `ind_niftyconsumerdurableslist.csv` | A | 14 | | semi-annual |
| Nifty Oil & Gas | `ind_niftyoilgaslist.csv` | B | 15 | | semi-annual |
| Nifty PSU Bank | `ind_niftypsubanklist.csv` | B | 12 | | semi-annual |
| Nifty Media | `ind_niftymedialist.csv` | B | 10 | | semi-annual |
| Nifty200 Momentum 30 | `ind_nifty200momentum30_list.csv` (underscore) | B | 30 | top 30 of Nifty 200 by normalised momentum score: 6m and 12m price return divided by daily-return volatility (press release above) | semi-annual (Jun/Dec) |
| Nifty100 Quality 30 | `ind_nifty100Quality30list.csv` (mixed case) | B | 30 | quality score (ROE, leverage, earnings variability) from Nifty 100 | semi-annual |
| Nifty200 Alpha 30 | `ind_nifty200alpha30_list.csv` | B | 30 | highest alpha from Nifty 200 (rule UNVERIFIED, inferred from name) | semi-annual (UNVERIFIED) |
| Nifty100 Low Volatility 30 | `ind_nifty100LowVolatility30list.csv` | B | 30 | from Nifty 100, 1-year daily return volatility, lowest 30 (methodology PDF, VERIFIED: https://www.niftyindices.com/Methodology/Method_Nifty100_Low_Volatility_30.pdf) | semi-annual (UNVERIFIED which months) |

Case and punctuation of file names are irregular (`list` vs `_list`, mixed case); treat the file name as data in a
catalogue table, never derive it from the index name. Probing 404s return an HTML page with status 404 (VERIFIED), so a
real fetch must check `Content-Type: text/csv` and the header line, not only the status.

**Not found (tried and 404 on both hosts, so UNVERIFIED location):** Nifty Alpha 50, Nifty High Beta 50, Nifty 50 Equal
Weight, Nifty Alpha Low-Volatility 30, Nifty Low Volatility 50, Nifty200 Quality 30, Nifty Midcap150 Momentum 50,
Nifty F&O list under `/indices/`. Their file names are not guessable; the index pages on niftyindices.com are
JavaScript-rendered and exposed no `IndexConstituent/*.csv` links via plain `curl`. Action: a one-time manual capture of
the download link from each index page (browser dev tools) into the catalogue table. Methodology (VERIFIED for Alpha 50
and High Beta 50, Sept 2019 PDFs, https://www.niftyindices.com/Methodology/Method_Nifty_Alpha_50.pdf and
`Method_Nifty_High_Beta_50.pdf`):
- Universe: top 300 companies by average free-float mcap AND average daily turnover over last six months; listing history
  at least 1 year; trading frequency 100% in the last year.
- Alpha 50: alpha from 1-year trailing adjusted prices (vs Nifty 50 as market, UNVERIFIED which benchmark), rank descending,
  top 50, buffer: existing constituent kept if rank within top 100; negative alpha excluded unless nothing else.
- High Beta 50: beta vs Nifty 50 from 1-year trailing prices, top 50, same buffer, beta must exceed 1.
- Weights proportional to alpha / beta (theme weighting). Rebalance quarterly (Mar/Jun/Sep/Dec after F&O expiry).
- "Nifty 50 Equal Weight" / "Alpha Low-Vol 30": rules in the consolidated methodology (VERIFIED present, not read in
  detail); "equal weight" is a weighting, membership equals Nifty 50, so it is **redundant** as a basket.

**Recommendation for index baskets (v1):** Nifty 50, Next 50, 100, 200, 500, Midcap 150, Smallcap 250, Microcap 250,
Bank, Financial Services, IT, Pharma, Auto, FMCG, Metal, Energy, Realty, PSU Bank, Oil & Gas, Healthcare, Consumer
Durables, Media, Nifty200 Momentum 30, Nifty100 Quality 30, Nifty100 Low Vol 30, Nifty200 Alpha 30. Add Alpha 50 / High
Beta 50 only once their download link is captured. Skip Equal Weight variants (same members).
Note the strategy indices overlap strongly with our own derived baskets (Section 2) which are point-in-time capable.

### 1.3 F&O, ban, lot sizes (VERIFIED)

- **F&O lot sizes / underlyings:** `https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv` (47,736 bytes). Header:
  `UNDERLYING,SYMBOL,OCT-26,NOV-26,DEC-26,MAR-27,JUN-27,...` with space-padded fields (trim every cell), one row per
  underlying incl. indices (first row `NIFTY BANK, BANKNIFTY, 30, 30, ...`). Columns are expiry months, values are lot
  sizes. Basket "F&O stocks" = rows whose SYMBOL is an equity (exclude the index rows: match against our instrument
  master type = share). Update lag: NSE revises lot sizes at contract introduction and on SEBI-driven revisions, and
  adds/removes underlyings via circulars; the file changes within the day of effect. Cross-check with our own derived
  flag from the Dhan master (doc 06 §2.1: stock futures present = 213 underlyings on 2026-10-11). Prefer the Dhan-derived
  flag as the primary; use `fo_mktlots.csv` as an independent check and for lot size (doc 06 §2.3 stores `lot size`).
- **F&O ban list (securities in ban period, MWPL > 95%):** `https://nsearchives.nseindia.com/content/fo/fo_secban.csv`.
  Format (VERIFIED): first line `Securities in Ban For Trade Date 26-OCT-2026:`, then `1,AMBUJACEM`, `2,KAYNES`, `3,SAIL`.
  Daily list, published each evening for the next day, `last-modified` 2026-10-23 13:11 GMT. Basket kind `list`,
  membership dated by the trade date in line 1. Useful as an **exclusion filter** (no new positions) rather than a
  tradable universe. Empty list is a valid state (line 1 only).
- **MTF / leverage-eligible:** the Dhan master already carries `MTF leverage` (doc 06 §2.3 flags), so no external file needed.
  NSE-published "MTF securities" file: tried `MW-Securities-available-for-MTF.csv` and `MTF_list.csv`, both 404:
  UNVERIFIED. **MIS (intraday leverage) lists** are broker-specific (not NSE); Dhan publishes a margin/MIS multiplier
  via its own API or scrip master (`MIS_LEVERAGE` / `BUY_CO_MIN_MARGIN_PER` style columns; exact names UNVERIFIED, check
  the stored master columns). Basket "MIS-eligible" should be sourced from Dhan, not NSE.

### 1.4 Equity universe, SME, surveillance

- **All listed equity:** `https://nsearchives.nseindia.com/content/equities/EQUITY_L.csv` (VERIFIED, 2,614 lines, `last-modified`
  2026-10-22). Columns: `SYMBOL,NAME OF COMPANY, SERIES, DATE OF LISTING, PAID UP VALUE, MARKET LOT, ISIN NUMBER, FACE VALUE`
  (note leading spaces in some headers). `DATE OF LISTING` is useful for a "listed for N days" eligibility rule.
- **SME:** `https://nsearchives.nseindia.com/content/equities/SME_EQUITY_L.csv` (VERIFIED; only 1 data row at probe time,
  series `SM`; likely truncated or SME moved to another file: treat as UNVERIFIED completeness). Our master keeps series
  `SM`/`ST` shares (doc 06 §2.1) so an "SME" basket can be derived from `series IN ('SM','ST')` without any download.
- **Securities with price bands / series:** `https://nsearchives.nseindia.com/content/equities/sec_list.csv` (VERIFIED, 3,599 lines),
  columns `Symbol,Series,Security Name,Band,Remarks`. Band is the circuit limit percent (2, 5, 10, 20, `No Band`). A
  "circuit band 2% or 5%" basket (illiquid/BE-series) is useful as an **exclusion** list.
- **ASM / GSM:** doc 06 already derives these from Dhan's `ASM_GSM_FLAG` and category, daily, which is sufficient and
  point-in-time-able (we can store the flag per refresh). NSE-side CSV candidates tried and 404: `eq_asm_list.csv`,
  `gsm_list.csv`, `surveillance_list.csv`, `archives.nseindia.com/content/equities/ASM_GSM.csv`. The NSE web pages
  (`https://www.nseindia.com/regulations/...` surveillance measures) use JSON APIs requiring cookies: UNVERIFIED and not
  recommended. **Use the Dhan flag.**
- Baskets: `Surveillance: ASM/GSM` (kind `list`, from master), `SME / Trade-for-trade` (series `BE/BZ/SM/ST`, from master),
  `In F&O ban` (from `fo_secban.csv`).

### 1.5 Terms, licensing, robots, automation

- `https://www.nseindia.com/robots.txt` (VERIFIED): `User-agent: *  Allow: /  Disallow: /market-data-test`, sitemap listed.
- `https://niftyindices.com/robots.txt` (VERIFIED): `User-agent: *  Allow: /` plus four `Disallow` lines for specific
  presentation pages (Nifty-Next-50 sub-page, `/reports/nifty-passive-insights`, two `strategy-indices/...top-15/20-equal-weight`
  pages). The `/IndexConstituent/` and `/content/indices/` CSV paths are not disallowed.
- **Terms of use / data licence: UNVERIFIED.** `WebFetch` of `https://www.niftyindices.com/terms-conditions` timed out; a
  curl of the same page returned only the site menu and the footer `Copyright (c) 2018 NSE Indices Limited`, no readable
  clauses. NSE's disclaimer page (`https://www.nseindia.com/regulations/disclaimer`) also yielded nothing readable.
  Industry understanding (not verified here): NSE Indices IP; index values and constituents are NSE Indices' property;
  redistribution, commercial use and real-time derived products need a licence; personal informational use of the public
  files is tolerated. Therefore:
  - xDrishti is local, single-owner; storing a private copy of the public CSVs is the minimum-risk reading.
  - Never expose these lists through any public endpoint, never ship them in the repo (data stays under `data/` / DB),
    never display index weights (not in these files anyway).
  - Owner should read the terms himself and add a line in an ADR before building (action item).
- **Automation etiquette:** one conditional GET per file per day at most (weekly steady state), a fixed descriptive
  User-Agent plus contact (a browser UA was needed in probes, the CDN returned 200 to it; non-browser UAs untested),
  respect `ETag`/`Last-Modified`, back off on 403/429 for 24h, never hit the cookie-gated `www.nseindia.com/api/*` JSON
  endpoints from the job (the calendar refresh in doc 05 §4.1 already notes NSE "often wants a browser-like client and a
  cookie"; keep that path best-effort only).
- **Update lag:** CSVs reflect the new composition on/just before the effective date (the Nifty 50 file I fetched was
  modified Sat 2026-10-24 03:31 GMT, i.e. NSE regenerates files outside market hours). Announcement precedes effect by
  days to weeks, but the CSV shows only the *current* list, so announced future changes are not visible in it. Press
  releases (`https://niftyindices.com/Press_Release/ind_prs<ddmmyyyy>.pdf`, VERIFIED pattern from search result) carry
  the announcement; parsing them is out of scope (UNVERIFIED stability).

---------------------------------------------------------------------------------------------------------------------

## 2. Derived "behaviour" baskets from our own bars

Common definitions (all computed on adjusted daily bars `bars_agg` timeframe `1D`, India dates; intraday ones on `5m`/`1m`):

- Universe `U(D)` = tracked, non-retired-as-of-D shares, series EQ-like, with at least `N_min` complete daily bars before D.
  Because tracked set = owner picks (ISS-22), also offer a variant over the **F&O universe** and the **Nifty 500** index
  basket, so behaviour is ranked within a defensible population. A behaviour basket always states its universe.
- All windows end at the close of day D-1 for a basket "used on D" (strict no look-ahead). Store `as_of = D-1`.
- `ret_t = close_t / close_{t-1} - 1` using split/bonus-adjusted closes (corporate-action adjustment from doc 05 §4.2;
  if the action detector has unresolved suggestions for the instrument in the window, **exclude the instrument** rather
  than risk a fake gap).
- `TR_t = max(H_t-L_t, |H_t-C_{t-1}|, |L_t-C_{t-1}|)`, `ATR_n = Wilder mean of TR over n`, `ATR% = ATR_n / C`.
- Selection forms: **top-k** (fixed size, e.g. 30/50), **top-decile/quantile**, or **threshold**. Prefer top-k with a buffer
  (keep an incumbent while rank <= 1.5k) to cut churn, modelled on NSE's own 100%-buffer for Alpha 50 (VERIFIED above).
- Refresh: daily after the EOD bar aggregation job (membership effective next trading day), or weekly (Friday) for slow
  signals (momentum, Hurst, correlation clusters). Min history stated per basket. Everything uses adjusted data, but note
  that **adjustments are restated retroactively**: recomputation of an old date with today's adjusted series is slightly
  different from what was knowable then; acceptable for ranking (ranks are scale-free), flag it.

| # | Basket (name) | Exact definition | Window / min history | Threshold / size | Cadence |
|---|---|---|---|---|---|
| 1 | **Most volatile** | `ATR%_14` (or `rv_20 = stdev(ret_t, 20) * sqrt(252)`), rank descending | 20 d; need 60 bars | top decile or top 50 | daily |
| 2 | **Most moving (today-ish)** | `mean(\|ret_t\|, 5)`; second variant range `mean((H-L)/C, 5)` | 5 d (+20 d for z-score) | top 30 | daily |
| 3 | **Most liquid** | `ADTV_20 = median(C_t*V_t, 20)` in rupees (median, not mean, to resist single-day spikes) | 20 d; need 60 bars | top 100 or ADTV >= rupees 20 crore | daily |
| 4 | **Gappers** | `gap_D = O_D/C_{D-1} - 1` (adjusted); in basket if `\|gap\| >= max(2%, 1.5*ATR%_14)` and `V_pre` (first 5m volume) high; **intraday-formed** at ~09:20 IST | 14 d | all passing, cap 30 | intraday, once at 09:20, frozen for the day |
| 5 | **High beta vs Nifty 50** | `beta = cov(ret_i, ret_NIFTY)/var(ret_NIFTY)` on 250 daily returns (NSE uses 1y trailing too, VERIFIED) | 250 d; need 200 | top 50 with beta > 1 | weekly |
| 6 | **Momentum 12-1** | `R = C_{t-21}/C_{t-252} - 1` (skip last month); variant 6-1: `C_{t-21}/C_{t-126}-1`; optionally divide by `rv_252` (NSE's Momentum 30 does vol-adjust, press release above) | 252 d | top 30 / 50 | monthly (weekly check) |
| 7 | **Mean-reverting** | `Hurst` via R/S or variance-ratio `VR(q=5)=var(ret5)/(5*var(ret1))` over 250 d; and lag-1 return autocorrelation `rho1 < 0`; basket: `VR < 0.85` and `rho1 < -0.05` and `ER_20 < 0.3` | 250 d | bottom 50 by `VR` | weekly |
| 8 | **Trending** | Kaufman efficiency ratio `ER_n = \|C_t - C_{t-n}\| / sum\|C_i - C_{i-1}\|` (n=20), plus ADX_14 > 25; rank by ER | 20-60 d | ER_20 > 0.5 and ADX_14 > 25, top 50 | daily |
| 9 | **Breakout near 52w high** | `C_{D-1} / max(H, 252) >= 0.97` and `ADTV` above floor and `C > SMA_200` | 252 d | all passing, cap 50 | daily |
| 10 | **Low volatility** | `rv_252` ascending (NSE Low Vol 30 uses 1y daily returns, VERIFIED), restricted to ADTV top 200 | 252 d | bottom 30 | monthly |
| 11 | **High relative volume** | `RVOL = V_{D-1}/median(V, 20)` (daily); intraday variant: cumulative volume to time-of-day vs median of same 5m slot over 20 d | 20 d | RVOL >= 2 and price > Rs 50 | daily / intraday |
| 12 | **Opening-range wide** | `OR% = (max(H over 09:15-09:30) - min(L)) / O` from 1m/5m bars, vs its 20-day median; wide if `OR% >= 1.5 * median` and absolute >= 0.8% | 20 d of 1m bars | top 30 | intraday, frozen at 09:30 |
| 13 | **Intraday-tradable (spread/liquidity proxy)** | no bid/ask stored (depth not built, P7), so proxy: `ADTV_20 >= Rs 10 crore`, median 1m bar traded value, `fraction of 1m bars with zero volume <= 5%`, median 1m `(H-L)/C >= 3 * tick/C` (tick-size noise guard), not in ASM/GSM, not in ban, series EQ | 20 d of 1m | passing | daily |
| 14 | **Sector clusters by return correlation** | correlation matrix of daily returns over 120 d (Spearman or Pearson on winsorised returns), distance `d=sqrt(0.5*(1-rho))`, hierarchical clustering (average linkage), cut to k clusters (or at d threshold); name each cluster by its majority NSE industry | 120 d; need 100 | k ~ 10-15, min size 5 | monthly |

Per-basket notes: why useful, and failure modes.

1. **Most volatile / 2. Most moving.** Useful: intraday scalping scan scope, large-ATR stop sizing, event days. Failure: a corporate
   action not adjusted looks like a 50% move (excluded above); circuit-limit stocks (5% band) rank high with no tradability;
   ranking on the very window used to test a volatility strategy is circular (in-sample); volatility clusters but decays,
   rank by a longer window for stability and a shorter for timeliness (use both, report both).
3. **Most liquid.** Useful: the realistic tradable universe, base for slippage assumptions. Failure: turnover includes
   block/bulk deals and index rebalance days (use median); a one-time IPO-listing-month spike; liquidity is not bid-ask spread.
4. **Gappers.** Useful: gap-and-go / fade scans. Failure: the basket is only known after 09:15 (formed intraday), so a
   simulation must form it from bars up to the formation time only; gaps on ex-dates for dividends/splits are not real; earnings
   gaps are one-off (exclude by event calendar if available, UNVERIFIED availability).
5. **High beta.** Useful: leverage on market views. Failure: beta from daily returns is unstable and biased by illiquid/asynchronous
   trading; regime-dependent; look-ahead if estimation window includes the test period; high-beta basket is a high-vol
   basket, not an alpha source.
6. **Momentum.** Useful: classic factor basket; equity-market evidence (12-1) is long-documented. Failure: momentum crashes at
   regime turns; needs 252 d, our history is ~2.1 years (ISS-19), so only the first ~1.1 years of any backtest can use 12-1
   (use 6-1 for shorter history); survivorship (delisted losers missing) inflates it; look-ahead if the ranking date
   leaks.
7. **Mean-reverting.** Useful: scope for range/RSI-fade strategies. Failure: Hurst/VR estimates are very noisy at n=250
   (confidence band around 0.5 is about +/-0.07 for R/S), regime flips quickly, circular if the same data later scores the
   fade strategy; require both statistics to agree.
8. **Trending.** Useful: scope for breakout/trend-follow. Failure: trend measured with the same window that later
   "confirms" it is look-back-only; ER of any trailing window says nothing about the next one without out-of-sample testing.
9. **Near 52w high.** Useful: breakout watchlist (George and Hwang style anchoring). Failure: 252-bar high uses H of the last
   year adjusted for corporate actions (a bonus issue unadjusted breaks it); with ~2.1 years history the 52w window is
   fine but a backtest has only the last ~1.1 years valid; many members in a bull market (cap size).
10. **Low volatility.** Useful: defensive sleeve, low-risk long-term scope. Failure: concentration in banks/FMCG; defensive
    stocks lag in rallies; overlaps with Nifty100 Low Vol 30 (compare as a sanity check, expect partial overlap only).
11. **High relative volume.** Useful: "something is happening" detector. Failure: volume spikes at index rebalance, F&O
    expiry, bulk deals; intraday RVOL needs same-time-of-day normalisation or the opening always screams.
12. **Opening-range wide.** Useful: ORB strategy scoping. Failure: first 15 min includes auction/pre-open artifacts (our
    bars start 09:15, so no pre-open); depends on 1m coverage and gaps in `bars_1m` (data-quality exclusion needed).
13. **Intraday-tradable.** Useful: removes names you cannot enter/exit. Failure: proxy only (no depth), can exclude
    stocks that trade fine; ADTV threshold is a judgement number (owner setting).
14. **Sector clusters.** Useful: finds real co-movement groups (beyond NSE industry labels), pairs ideas, concentration
    control. Failure: unstable clusters across windows (compare adjusted Rand index across runs, store the stability
    score), dominated by the market factor unless returns are market-neutralised (subtract Nifty return or use
    residuals), small k arbitrary.

**General failure modes, apply to every derived basket**
- **Look-ahead:** every basket carries `as_of` (last bar used) and `effective_from` (next session); consumers must take the
  version whose `effective_from <= decision time`. The compute job must read bars with `ts < as_of_end` only, and a test
  must prove that appending a future bar does not change a past version.
- **Overfitting:** thresholds above are defaults to be stored as parameters (versioned), not tuned against results.
  Never define a basket on the P&L of the strategy it will then be tested on. Record `params_hash` per version.
- **Survivorship / membership look-ahead:** a basket formed from "tracked instruments" inherits the owner's picks (ISS-22).
  For simulations use the dated version at the run date, which is point-in-time **by construction** for derived
  baskets (unlike index baskets) as long as `U(D)` includes instruments retired later that had bars on D (doc 06 §3.3 already
  replays retired instruments with bars).
- **Small samples:** minimum history is part of the definition; instruments below it are reported as `insufficient_history`
  (not silently dropped), consistent with the "every exclusion named" rule of ADR 0102.
- **Churn:** use buffer rules and a minimum dwell (e.g. no flip-flop within 5 sessions) for slow baskets, none for
  intraday ones.
- **Data not stored:** bid/ask, depth, free-float market cap, fundamentals (quality factor), earnings dates. Baskets needing
  them (true Quality, Value, spread) are not buildable now; do not fake them.

---------------------------------------------------------------------------------------------------------------------

## 3. Storing membership changes without pretending to have history

### 3.1 What ISS-22 implies

- No source gives us historical membership (the CSVs show only "now"; announcements are PDFs). So **index baskets are
  forward-dated only**: the first fetch creates rows with `valid_from = first_seen_date` and `source_asof = fetch date`, and
  a run over a period before that must keep the doc 06 label `members as of today`.
- Never back-fill `valid_from` into the past by guess. If the owner later imports a historical file manually (e.g. NSE
  index-change PDFs), such rows get `provenance = 'imported'` and may carry an earlier `valid_from`.
- Derived baskets are different: they can be recomputed for any past date from our bars. Store the **computed versions**
  (one per `as_of`) so a simulation reads exactly what was published; optionally a backfill job may compute past versions
  from bars (provenance `recomputed`, no look-ahead because inputs are cut at `as_of`).

### 3.2 Schema sketch (extends doc 06 §3.4; no code)

Reuse `market.baskets` with `kind IN ('manual','portfolio','index','list','derived')` and `system = true` for the new
kinds (ADR 0014 protection; kinds other than `portfolio` ARE stored rows because their membership has history).

- `market.basket_sources` (catalogue): `basket_id`, `source_url`, `file_name`, `host_alt`, `format_version`, `cadence`,
  `expected_min`, `expected_max` (sanity bounds, e.g. Nifty 50: 50..50), `enabled`, `license_note`.
- `market.basket_members` (existing): `basket_id`, `instrument_id`, `valid_from`, `valid_to`, plus new columns
  `provenance` (`fetched | imported | recomputed | manual`), `source_version_id`. Keep the unique index
  `(basket_id, instrument_id) WHERE valid_to IS NULL`. Adds are `valid_from = effective date`, removals set `valid_to`.
  Effective date rule: use the **fetch date (IST) of the first fetch that shows the change**, not the (unknown) NSE effective
  date, unless the owner enters the NSE date; record `detected_at` separately from `valid_from`. (Honest and
  conservative: a stock added "on the last trading day of March" appears the next morning in our data.)
- `market.basket_fetches`: one row per fetch attempt (`basket_id`, `fetched_at`, `http_status`, `etag`, `last_modified`,
  `sha256`, `rows`, `outcome` = `unchanged | applied | rejected | failed`, `added`, `removed`, `warnings`, `error`), mirroring
  `market.master_refreshes` (doc 06 §2.2).
- `market.basket_versions` (for derived kind): `version_id`, `basket_id`, `as_of`, `effective_from`, `params` (json),
  `params_hash`, `universe_size`, `member_count`, `excluded` (json: instrument + reason). Members of a derived version are
  `basket_members` rows with `valid_from = effective_from` and `valid_to = next effective_from` (so the dated model
  and `asOf` queries work unchanged). Scores/ranks go to a side table `basket_member_scores(version_id, instrument_id, score, rank)`
  so the UI can show "why" and buffer rules can read the previous rank.
- Identity: map CSV rows to instruments by **ISIN first, then symbol** (symbols change on renames; ISIN is stable except
  on some corporate actions). A row with no master match is kept in `unmatched` in the fetch record and the basket shows
  "N members not in your instrument list". Index members that are not tracked: auto-tracking is the owner's call (doc 06
  bulk-add rule), the system basket lists them regardless but data-dependent features show "no data".
- Retired or delisted: the instrument is retired in master (never deleted), its membership closes (`valid_to`) at the fetch
  that no longer shows it, and history keeps the row. This is the only place where our future will not suffer survivorship.

### 3.3 Refresh job design (service in the existing services framework, like `instrument-master`)

- **Job `system-baskets`**, one run at a time (advisory lock + running row, same pattern as doc 06 §2.2), India-time schedule:
  - index baskets: daily 07:30 IST conditional GET for each enabled source (steady-state ~1 CPU-second, 304s mostly);
    full verification run on the first trading day after Mar/Sep month end, and a **catch-up on API start** like the master job.
  - `fo_secban.csv`: daily 19:00 IST (evening publication) and again 08:30; `fo_mktlots.csv`: daily 08:40, after the Dhan master at 08:20.
  - Dhan-flag lists (ASM/GSM, F&O, MTF, SME series): derived inside the instrument-master run, no extra fetch.
  - derived baskets: after the EOD aggregation job (daily), weekly/monthly ones on their day; intraday ones (gappers,
    OR-wide) triggered by the live bar close at 09:20/09:30 and frozen.
- **Fetch pipeline:** conditional GET with stored `ETag`/`Last-Modified` -> verify `Content-Type` is CSV and header equals the
  expected column list -> parse (trim cells, strip BOM, ignore blank lines) -> `rows` in `[expected_min, expected_max]`
  -> match to instruments (ISIN, then symbol) -> compute diff vs current open memberships -> apply **in one transaction**
  (close removed rows, insert added rows, write fetch record) -> audit event `BasketMembershipChanged` with counts (no
  symbols in audit, same rule as tracking audit in doc 06).
- **Failure handling (fail closed, keep last good, never half-apply):**
  - 404/HTML body, wrong header, row count out of bounds, or more than 20% of rows unmatched (or more than 3 members changing
    in a 50-member list outside a review week): `rejected`, **change nothing**, warn, keep serving the previous membership.
    A plain `rejected` note is shown on the basket ("last updated 2026-10-03; latest fetch rejected: ...").
  - 403/429/timeouts: retry with backoff (3 tries, 5/30/120 min), then mark the source `stale` after 3 consecutive days
    without a successful check, surfaced on the basket header and in the services screen; do not hammer.
  - Tripwire like the master: a file that would remove more than half of the members is refused unless forced by the owner.
  - Idempotent: same sha256 -> `unchanged`, no writes.
  - Review week watch: around the end of Mar and Sep, poll those lists twice a day for a week; also log (do not apply)
    differences between the NSE CSV and `fo_mktlots.csv` for the F&O universe.
  - The job never touches orders or the trading configuration; system baskets are read-only inputs (P7 places none).
- **Auto-approval alignment** (CLAUDE.md standing instruction): a membership change that alters a live scan scope is
  informational by default (basket changes are data, not approvals); if later a "re-test on membership change" approval
  is added, it must follow the auto/manual setting and be recorded as auto.
- **UI:** read-only baskets in a "System" group with a `kind` chip, a "as of / source / last checked" line, a history drawer
  (unlike portfolio baskets in ADR 0014 these have dated membership so the drawer and `asOf` control are shown), and the
  mandatory label on index baskets: "membership known since {first_seen}; earlier dates use today's members (ISS-22)".
- **Tests worth having:** fixture CSVs for each kind (clean, extra column, 404 HTML, truncated, duplicate symbol, symbol
  rename same ISIN), a review-week diff (2 out, 2 in), `asOf` boundary day, derived version no-look-ahead property test,
  retired-instrument-with-bars replay, and a lock test.

### 3.4 Survivorship notes specific to our data

- Index baskets (forward-only): runs before `valid_from` must say `members as of today` (ISS-22 unchanged). Do not call
  anything "Nifty 50 in 2024" until two or more reviews of dated history exist. After a year of operation we will have
  2 semi-annual reviews plus ad hoc changes, so dated membership becomes real for the new period only.
- Derived baskets: point-in-time if (a) bars of retired instruments stay in `bars_agg` (they do: retired never deleted), (b)
  `U(D)` is built from instruments with bars on D, not from today's tracked set, and (c) corporate-action adjustments are
  applied consistently. Residual bias: instruments the owner never tracked have no bars, so the universe is still the
  owner's pick unless the backfill covers the full Nifty 500 (recommend tracking all Nifty 500 members so the derived
  universe is the index, not the owner's taste; ~500 instruments x 1m history is the cost to weigh).
- Publish a one-line "what this basket is NOT" in each description (e.g. "not NSE's Momentum index; our own 12-1 rule").

---------------------------------------------------------------------------------------------------------------------

## 4. Suggested v1 scope (smallest useful)

1. Index baskets from the 30 verified CSVs in Section 1.2 (catalogue table + one generic fetcher; no per-index code).
2. List baskets: `F&O stocks` (Dhan flag, cross-check `fo_mktlots.csv`), `F&O ban today`, `ASM/GSM`, `Trade-for-trade/SME`.
3. Derived baskets, first wave (cheap, daily-bar only): Most liquid (ADTV top 100), Most volatile (ATR% top 50),
   Low volatility 30, Momentum 12-1/6-1 top 30, Near 52w high, Trending (ER+ADX), High beta 50. Second wave: intraday ones
   (gappers, ORB-wide, RVOL), then mean-reverting and correlation clusters.
4. Decide with the owner: track all Nifty 500 (so derived universes are market-wide), the licence reading in Section 1.5,
   and the numeric thresholds (all as versioned parameters, defaults above).

## 5. Sources actually opened (this research)

- https://nsearchives.nseindia.com/content/indices/ind_nifty50list.csv (and 20 sibling files in Section 1.2), HTTP 200
- https://niftyindices.com/IndexConstituent/ind_nifty200momentum30_list.csv, ind_nifty100Quality30list.csv, ind_nifty200alpha30_list.csv,
  ind_nifty100LowVolatility30list.csv, ind_niftytotalmarket_list.csv, ind_niftymicrocap250_list.csv, ind_niftyfinancelist.csv,
  ind_niftyoilgaslist.csv, ind_niftypsubanklist.csv, ind_niftymedialist.csv, HTTP 200
- https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv, fo_secban.csv; /content/equities/EQUITY_L.csv, SME_EQUITY_L.csv, sec_list.csv
- https://www.niftyindices.com/Methodology/Method_NIFTY_Equity_Indices.pdf (Sept 2026), Method_Nifty_Alpha_50.pdf,
  Method_Nifty_High_Beta_50.pdf, Method_Nifty100_Low_Volatility_30.pdf (text extracted by hand-rolled parser, may contain
  spacing artefacts)
- https://www.nseindia.com/robots.txt, https://niftyindices.com/robots.txt
- Search-only (not opened): https://niftyindices.com/Press_Release/ind_prs25082020.pdf, Factsheet_Nifty_Alpha50.pdf,
  Factsheet_NIFTY_Alpha_Quality_Low-Volatility_30.pdf
- Could not open (timeouts / unreadable): niftyindices.com HTML pages (broad-based-indices, terms-conditions), NSE disclaimer
  page, any ASM/GSM/MTF CSV: all UNVERIFIED.
