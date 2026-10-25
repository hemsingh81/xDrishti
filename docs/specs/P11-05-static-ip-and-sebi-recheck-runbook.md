# P11-05 — Static IP and SEBI re-check: the runbook, the questions and the decision record

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P11 — Small live capital, slice P11-05 of [P11-00](P11-00-small-live-plan.md). Source of the task: doc 19 §5 "Static-IP decision (Q6, 2026-10-09): not needed until Phase 12; ask the ISP and re-check circulars at the P11 exit"; doc 01 Q6; doc 17 ("ISP static IP: only when automated orders are enabled (P12); revisit at the P11 exit").
- **Design refs:** docs/design/19-risks-and-compliance.md §5 · 01-overview.md Q6 · 17-deployment-and-operations.md (ISP static IP row) · 20 §A (prerequisites: ISP static IP whitelisted at Dhan; daily login/2FA; <= 2 orders/s) · 04 §1 (Dhan notes: "A static IP is needed only for placing orders") · P12-11 (daily login, token and static IP operations, later) · ADR 0008, 0009 (Dhan consent flow).
- **Needs:** nothing (start first). **Size:** 2 agent-days of building plus the owner's waiting time (an ISP lead time of days and, once an IP is set at Dhan, a lock of 7 days). **Safety:** **yes (light)** — one owner-written record from the UI; the record never holds an IP address, a secret or a client id; external enquiries are the owner's acts, not the system's.

## Goal
Settle early, with evidence and in writing, the questions that decide whether P12 can start: does the owner's home connection give a **static public IPv4** that Dhan will whitelist, what exactly do SEBI's retail algo rules and the exchanges' circulars require of a personal, self-built system that sends API orders, and does Dhan's whitelist also affect today's read-only calls. The deliverable is a runbook (this spec carries it in full), a dated decision record the P11 phase check reads, and the doc updates that follow. Nothing here places an order or opens a port.

## Scope
**In:** (1) the runbook below copied to `docs/research/p11-static-ip-and-sebi-runbook.md` (the questions are filled in and dated as the owner gets answers); (2) the compliance record: migration `AddComplianceRecords` (`trading.compliance_records`, insert-only), `POST/GET /api/live-trading/compliance`, `xd live compliance show`, the **Compliance** tab of `/live` (list + one dialog), the derived notice "re-check due"; (3) at the end: doc 19 §5, doc 17 row, doc 01 Q6 and the roadmap row updated with the decision, an ADR, `scripts/build-docs.sh`.
**Out:** buying or configuring the static IP (the owner's act), setting the IP at Dhan, any order, storing the IP anywhere in the repo or database, a VPN or relay, any change of ports (every published port stays on 127.0.0.1), the P12 operations (P12-11).

## Acceptance criteria
1. Given the runbook, then every question in sections 3 and 4 (D1 to D8, I1 to I7) and every check in section 5 has an owner column ("who answers"), a "why it matters" line, a place to write the answer with its date and source, and each external fact in section 2 has its source URL and the date it was read; unconfirmed facts are marked `UNVERIFIED`.
2. Given `POST /api/live-trading/compliance` with a valid body, when the session and CSRF are valid, the capability gate `Live:Writes:Enabled` is on and the body carries the owner's **fresh password** (checked on this call with the sign-in lockout rules and audit, without opening or using the 5-minute step-up window: the `Elevate: false` pattern of `PUT /api/autonomy/settings`), then one row is inserted (insert-only) with the actor from the session, `201` and the stored view; a wrong or missing password is 401 `auth.*` as the autonomy route, a closed capability gate is 403 `live.capability_off`; the call is audited (kind, outcome, decided date; never the free text).
3. Given a body whose free-text fields (`summary`, `isp`, `sources[]`) contain something that looks like an IPv4 address, an IPv6 address, a Dhan client id (a whole word of 7-12 digits) or a token-like string (a whole word of >= 24 URL-safe characters without spaces), then 422 `live.invalid` naming the field, and nothing is stored. The screens apply to `summary` and `isp` only; `sources[]` (validated by their own pattern in 4), `decidedOn`, `whitelistedAt` and `nextReviewOn` (typed dates; a compact form such as `20261024` is a date, not a client id) are exempt, so the runbook's own `https://` URLs and circular ids such as `NSE/INVG/73992` are accepted.
4. Given `kind` not in the three kinds, an `outcome` not allowed for the kind, `decidedOn` in the future, `nextReviewOn` not after `decidedOn` or more than 400 days ahead, `sources` longer than 10 items or an item longer than 300 characters or not an `https://` URL or a circular id (`^(SEBI|NSE|BSE)/[A-Za-z0-9/._-]{3,60}$`), then 422 `live.invalid`.
5. Given a `static_ip` record with `staticIpState = whitelisted`, then `whitelistedAt` is required and `lockEndsOn` is derived as `whitelistedAt + 7` days by the server (a client value is ignored); a state other than `whitelisted` refuses both dates.
6. Given records, then `GET` lists newest first with `latest` per kind; the latest record per kind is what the P11 phase check reads; none for a kind is `empty` (not "no change").
7. Given `nextReviewOn` has passed with no newer record of that kind, then the derived notice "Compliance re-check overdue" exists (route button only); it resolves when a newer record exists.
8. `xd_app` can insert and select `compliance_records`, not update, delete or truncate it (grants test); the database refuses an unknown kind, an outcome outside the kind's set, a `whitelisted` state without `whitelisted_at`, and a text longer than its column.
9. The tab and the dialog call no broker, show the sentence "This record is your note of what you were told; the system checks nothing at Dhan or the ISP", and the form is unreachable by a read-only test (a mutation import exists only in this tab's dialog).

## Contract
- **API:** `GET /api/live-trading/compliance?kind=&limit=` → `ComplianceView { items[ComplianceRecordView], latest{ static_ip, sebi_recheck, dhan_ip_read_policy } }`; `POST /api/live-trading/compliance` `{ kind, outcome, decidedOn, summary, sources[], staticIpState?, isp?, monthlyCost?, ipVersion?, whitelistedAt?, nextReviewOn, password }` → 201 `ComplianceRecordView`. Errors: 422 `live.invalid`, 401 (session, CSRF, wrong password; sign-in rate limit applies), 403 `live.capability_off`. **Reader:** P12-11 reads this table (kind `sebi_recheck`, `static_ip`, `dhan_ip_read_policy`) instead of making its own store; one store for the whole compliance trail. Route has no `orders` segment.
- **Data (`trading.compliance_records`, insert-only):** `id bigint identity`, `kind varchar(24)` in (`static_ip`, `sebi_recheck`, `dhan_ip_read_policy`), `outcome varchar(28)` in (`static_ip`: `not_needed_for_manual`, `requested`, `obtained`, `whitelisted`, `declined`, `blocked`; `sebi_recheck`: `rechecked_no_change`, `rechecked_changed`, `blocked`; `dhan_ip_read_policy`: `reads_not_ip_restricted`, `reads_ip_restricted`, `unanswered`), `decided_on date`, `summary varchar(1000)`, `sources text[]` (<= 10), `static_ip_state varchar(12) null` in (`none`,`requested`,`active`,`whitelisted`), `isp varchar(80) null`, `monthly_cost numeric(10,2) null`, `ip_version varchar(2) null` in (`v4`,`v6`), `whitelisted_at date null`, `lock_ends_on date null` (server-derived), `next_review_on date not null`, `actor varchar(64)` like `owner:%`, `created_at`; checks as criterion 8. Migration `AddComplianceRecords` with `Down`, `MakeAppendOnlyForAppRole`. Log id 5140.
- **UI:** `/live` tab **Compliance**: the latest record per kind as cards (outcome in words, decided date, next review date, "overdue" with icon + word), the history table, a "New record" dialog (shared dialog shell, password field, fresh password per save), the runbook link text (the repo path; no external link opened by the app), the sentence of criterion 9. States: `empty` ("Nothing recorded yet. Start with the runbook, section 3").
- **Events/jobs:** none. The notice is derived at read time.

## The runbook (deliverable; copy it to `docs/research/` and keep it dated)

### 1. Why now, and what is NOT needed in P11
Orders typed by the owner in Dhan's own app or website are not API orders, so no static IP and no algo registration are needed to run P11 (the P11 plan and doc 19 §5 agree; the framework text is UNVERIFIED, see 2). The enquiry starts at week 0 because the long items are outside the owner's control: an ISP lead time, Dhan's reply time, and a **7-day lock** after an IP is set at Dhan. Target: records `static_ip`, `dhan_ip_read_policy` and `sebi_recheck` written before the P11 exit check; P12 does not start without them.

### 2. What is known (read 2026-10-24; none of it is a primary legal text)
| Fact | Source (date read) | Confidence |
|---|---|---|
| Dhan: static IP is required for the Order Placement APIs (Orders, Super Order, Forever Order); order and trade detail fetches do not need it. One primary and one secondary IP; set on Dhan Web or via the Set IP API; once set it cannot be edited for 7 days "or as recommended by the exchange"; IPv4 and IPv6 both supported; "each individual needs to have a unique static IP". | https://dhanhq.co/docs/v2/authentication/ and https://dhanhq.co/docs/v2/orders/ (2026-10-24) | High (vendor docs) |
| Dhan support FAQ says the opposite in scope: "IP whitelisting is a mandatory security requirement for API access and cannot be disabled"; "All API requests must originate from the IP address that has been whitelisted"; no exemption; contact apihelp@dhan.co. | https://dhan.co/support/platforms/dhanhq-api/could-you-please-disable-ip-whitelisting-for-my-account-or-provide-an-alternative-solution-that-would-allow-api-access-from-a-dynamic-ip/ (2026-10-24) | Conflict with the line above: ask (D1) |
| Dhan access tokens: 24 hours from Dhan Web or the TOTP flow; the API key and secret are valid 12 months; the key flow returns an `expiryTime`. | https://dhanhq.co/docs/v2/authentication/ (2026-10-24) | High |
| Dhan API limits: order APIs 10 per second, 250 per minute, 1,000 per hour, 7,000 per day; data APIs 5 per second; non-trading APIs 20 per second; order modifications capped at 25 per order. | https://dhanhq.co/docs/v2/ (2026-10-24) | High |
| SEBI retail algo framework in force from 1 April 2026: orders accepted only from a registered app on a whitelisted static IP; 2FA once every trading day, no continuous refresh-token sessions; a cap of 10 orders per second; exchange algo ids on algo orders; market orders converted to market-price-protection (broker-dependent). | https://support.fyers.in/portal/en/kb/articles/what-are-the-new-sebi-rules-for-retail-algo-trading-from-april-01-2026 (2026-10-24; last updated 2026-03-31); https://www.sahi.com/blogs/sebi-algo-trading-rules-2026-what-every-retail-trader-must-know-before-april | Medium (broker notices) |
| Circular chain (titles and dates only): SEBI 4 Feb 2025 master circular on safer participation of retail investors in algorithmic trading; NSE 5 May 2025 implementation standards; NSE 22 Jul 2025 operational modalities; NSE 19 Sep 2025 corrigendum; SEBI 30 Sep 2025 extension and glide path (broker milestones 31 Oct, 30 Nov 2025, 3 Jan 2026; no new retail algo clients for non-compliant brokers from 5 Jan 2026; full applicability 1 April 2026); NSE/INVG/70541 (30 Sep 2025); NSE/INVG/73992 (30 Apr 2026, formats for retail algo client direct-API registration, as cited on NSE's decision-support-tools page). | https://algobulls.com/blog/industry-insights-and-updates/sebi-new-algotrading-regulations-for-retail-investors-2026 (2026-10-24, a blog); web search results naming the NSE circular numbers (2026-10-24) | Low to medium: **the circular texts were not opened** (nseindia.com timed out) |
| Threshold: 10 orders per second per exchange per client "initially"; below it, sources differ on whether a self-built algo needs registration (one says no registration below 10; another says algos below the threshold register with the exchange and get a generic algo id). Self-built algos by a retail client: the "Client Direct API" definition was added by the Sep 2025 corrigendum. | AlgoBulls blog (above); Aliceblue and others (2026-10-24 search) | **Unresolved: ask (D2)** |
| Static IP sharing: one or two static IPs per client; sharing only within a family group with 2FA-verified consent; retail algos to be hosted on Indian servers. | AlgoBulls blog (above) | Low |
| ISP route: ACT Broadband gave a static IP on an existing plan for Rs 350 a month, twelve months paid upfront, activated during one support call; checked at openalgo.in/ip and after a router restart; inbound access needs port forwarding (not needed: xDrishti only makes outbound calls). | https://www.marketcalls.in/openalgo/how-i-got-a-static-ip-from-act-broadband-for-sebi-compliance-to-run-algo-trading-systems.html (post of 2026-03-29, read 2026-10-24) | Medium (one account; prices change) |
| ISP pitfalls: carrier-grade NAT (CGNAT) gives many customers one public address, so a "static" address that is shared cannot be whitelisted for one person; one Dhan user reported DH-905 "Invalid IP" after adding IPs. | search results 2026-10-24 (quotaguard.com, dhan.co support thread titles) | Low |

### 3. Questions for Dhan (owner writes to apihelp@dhan.co; keep the reply text in the record's sources as a reference, not pasted)
| # | Question (send as written) | Why it matters | Answer, date, who |
|---|---|---|---|
| D1 | "Is IP whitelisting enforced on read-only API calls (holdings, trade history `/v2/trades/{from}/{to}/{page}`, ledger, trade book `/v2/trades`, order book `/v2/orders`, market quote), or only on order placement, modification and cancellation? Your API page says the latter and your support FAQ says all API requests." | Decides whether today's read sync already breaks when the home IP changes, and whether P11 capture needs the IP | |
| D2 | "I run a personal, self-built, rules-based system for my own account on my own machine. It sends orders through your API. Under the SEBI retail algo framework (in force 1 April 2026), (a) is my app a client-developed algo that must be registered with the exchange, or does your platform register it; (b) at fewer than 10 orders per second, do I receive a generic or a unique algo id and how is it attached to my orders; (c) is my API key bound to one static IP, and may the same IP serve my second Dhan account?" | Decides the P12 registration path and the order tag | |
| D3 | "After I set the primary IP, what exactly happens if my ISP address changes: do orders fail with a specific error code, can I edit the IP before the 7-day lock ends, and what is the fastest supported way to change it?" | Sets the outage runbook of P12-11 | |
| D4 | "Is a secondary IP usable at the same time (for example a second ISP line), and are both subject to the 7-day lock?" | Resilience option | |
| D5 | "Does an order placed by hand in the Dhan app carry any algo id or algo flag, and are my hand-placed orders outside the framework?" | Confirms P11's premise | |
| D6 | "What is the daily 2FA flow for an API-key (consent flow) app under the framework: does the access token still last 24 hours without a manual re-login, and does a lapse stop reads?" | Daily login operations (P12-11), capture gaps (P11-01) | |
| D7 | "Does Dhan sandbox testing need a whitelisted IP?" | P12-02 | |
| D8 | "Are market orders still accepted for API users, or only limit orders and market-price-protection orders?" | P12-02 order types | |

### 4. Questions for the ISP (phone or chat; ask for a ticket number)
| # | Question | Why | Answer, date |
|---|---|---|---|
| I1 | "Do you offer a **static public IPv4** address for my home connection? Is it a dedicated address or behind carrier-grade NAT?" | CGNAT cannot be whitelisted | |
| I2 | "Is it assigned only to my line and does it stay when the router restarts or the line re-authenticates?" | Static must survive restarts | |
| I3 | "Price per month, one-time fee, minimum period, cancellation terms, and the date it becomes active." | Cost and lead time (reference: Rs 350 a month, 12 months upfront at ACT in March 2026) | |
| I4 | "Does it change my plan, speed or data cap, and do you block outbound traffic to api.dhan.co on port 443?" | Side effects | |
| I5 | "Is IPv6 offered, and does my router prefer IPv6 for outbound traffic?" | Dhan accepts both, but the address Dhan sees must be the one whitelisted; prefer IPv4 | |
| I6 | "If the line fails, is there a failover or a second static address?" | P12 outage plan | |
| I7 | "Can the static address be moved to a replacement router or to a second connection?" | Hardware change | |

### 5. Checks the owner can do without anyone's answer
- **Evidence you already have for D1:** the Services screen shows the `portfolio-broker-history` and holdings syncs; if they succeed from the current dynamic home address while **no** IP is whitelisted at Dhan, then reads are not blocked by the whitelist today. Write the dates of the last successes in the record (this is a fact from the app, not a promise about the future).
- **Public address and CGNAT:** note the address a "what is my IP" service shows and compare it with the router's WAN address; if they differ, or the WAN address is in 100.64.0.0/10 or a private range, the line is behind CGNAT. Do not write the address in the record; write "matches" or "differs".
- **After the ISP activates the static IP:** restart the router, check the public address again, and note only "unchanged after restart".
- **Egress path:** the Podman machine's outbound traffic leaves through the Mac and the router, so the whitelisted address is the router's WAN address; a VPN, a phone hotspot or a second line changes it. Keep VPNs off the Mac while trading.
- **Re-check the texts:** read the SEBI circular of 4 Feb 2025 and the 30 Sep 2025 extension on sebi.gov.in (Legal > Circulars), the NSE circulars named in section 2 on nseindia.com (Resources > Circulars), and Dhan's API changelog; note any change of the 10-orders-per-second threshold, the static-IP rule or the algo-id rule in the `sebi_recheck` summary with the circular id in `sources`.

### 6. Options and the recommendation
| Option | Verdict |
|---|---|
| ISP static IPv4 on the home line (outbound only, no port forwarding) | **Recommended**: simplest, local-only, about Rs 350 a month in one reported case |
| VPS or cloud relay with a fixed IP forwarding orders | Rejected: puts a remote component with the order credential outside the Mac, against "local only"; adds a failure and attack surface |
| Dynamic address, re-whitelist daily | Rejected: the 7-day lock makes it impossible |
| Trade by hand forever | Valid fallback if the answers are unfavourable; P12 then stays at `assisted` |

### 7. Decision record format
The record in the app has the fields of the Contract. Write three records before the P11 exit: (a) `dhan_ip_read_policy` with outcome from D1 (or `unanswered` and the date asked), (b) `static_ip` with the state (`requested` / `active` / `whitelisted`), the ISP name, monthly cost, IP version, the date whitelisted and the derived lock end, (c) `sebi_recheck` with `rechecked_no_change` or `rechecked_changed`, a summary of what was read (circular ids and dates in `sources`), `nextReviewOn` about 90 days ahead. Never write an IP address, client id or token anywhere.

### 8. Re-check calendar
At the P11 exit; then every 90 days while P12 is planned or live; at once on any Dhan or exchange notice. The notice in the bell is the reminder.

## Non-functional
The write is a handful of rows a year. No external call is made by the app. Free text is length-capped, stored as data, rendered as text (a markup test), and screened for network identifiers and tokens (criterion 3). Audit text: kind, outcome, date.

## Test plan
- **Unit (Domain):** positive cases with the runbook's own sources: `https://dhanhq.co/docs/v2/authentication/`, `https://support.fyers.in/portal/en/kb/articles/what-are-the-new-sebi-rules-for-retail-algo-trading-from-april-01-2026`, `NSE/INVG/73992`, `SEBI/HO/MIRSD/2025/0000013` pass in `sources[]`; `decidedOn` 2026-10-24 passes; `ComplianceRecord.Create` (every refusal of criteria 3 to 5; the IP / client id / token screens with positive and negative cases: `192.168.1.10`, `2001:db8::1`, `1100000001`, a 40-character token are refused; `7 days`, `Rs 350` pass); lock-end derivation; kind-to-outcome table.
- **Application (fakes):** latest per kind; overdue notice appears and resolves; a fresh password is required on every save and a step-up window alone is refused; the capability gate off refuses.
- **Integration (Testcontainers):** migration, constraints, grants, the two endpoints, 422 cases.
- **Frontend (MSW):** tab states, dialog validation messages, overdue chip with words, the statement of criterion 9; a read-only tab test elsewhere still passes.
- **Docs check:** a test or script that the runbook file exists and every row of sections 3 and 4 has the four columns (a simple grep test in `scripts/check.sh` is enough).

## Decisions (researched)
1. **Q: Is a static IP needed in P11?** A: No, for hand-placed orders; yes before P12. Why: Dhan documents the static IP for order placement, modification and cancellation only, and the framework concerns API orders. Rejected: buying it now "to be safe" (a cost and a 12-month commitment before the owner knows P11 passes). Source: Dhan docs (2026-10-24).
2. **Q: Start the enquiry now or at the exit?** A: Now. Why: the lock of 7 days and an ISP lead time would otherwise add weeks between the P11 exit and the first sandbox order. Rejected: wait (doc 19's literal wording "at the P11 exit" is kept as the deadline, not the start).
3. **Q: Which option?** A: ISP static IPv4 (section 6). Why: local-only, outbound-only, cheapest reported route. Rejected: VPS relay, daily re-whitelisting.
4. **Q: Where does the decision live?** A: A dated, insert-only record in the app plus the runbook in `docs/research/`. Why: the phase check can read the record; the repo holds the reasoning; neither holds an address. Rejected: only a markdown note (the check could not see it), storing the IP (personal network data).
5. **Q: Who does the enquiry?** A: The owner; the system cannot write to Dhan or phone an ISP, and it must not send messages on the owner's behalf. Rejected: an automated email to Dhan.
6. **Q: How is the Dhan scope conflict handled?** A: Asked in writing (D1) and tested against the evidence the app already has (section 5). Why: it decides whether the current sync is at risk. Rejected: choosing one of the two Dhan pages.

## Risks and honest limits
The facts in section 2 are vendor documents and broker or blog summaries read on 2026-10-24; **the SEBI and NSE circular texts were not read** and the threshold wording is disputed between sources. Enforcement details for a personal, self-built system are unclear until Dhan answers D2. ISP offers and prices change. A static address does not protect against the router's address changing after a hardware swap. The record is the owner's note; the system verifies nothing at Dhan or the ISP.

## Open questions
Pure owner acts: write to Dhan (D1 to D8), call the ISP (I1 to I7), decide, and enter the three records. Default until then: no record, the phase check reads `empty` and the compliance criterion is NOT EVALUABLE.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration `AddComplianceRecords` added
- [ ] Runbook copied to `docs/research/` ; ADR written (next free number at build time); docs 19 §5, 17, 01 Q6 and roadmap updated once the records exist; `scripts/build-docs.sh`

## Review fixes 2026-10-25
1. Criteria 3/4: the network-identifier screens now cover `summary` and `isp` only; `sources[]` and the date fields are exempt; positive tests with the runbook's URLs and circular ids added.
2. Every UI write needs a fresh password (the `Elevate: false` pattern) plus the capability gate `Live:Writes:Enabled` (option, default false; 403 `live.capability_off`); the step-up window is not used.
3. `trading.compliance_records` is the single compliance store; P12-11 reads it.
4. Log id here is 5140; the highest log id in the repo was 4801 when this was reviewed and the next ADR number was 0102: both are "next free at build time", re-check then.
