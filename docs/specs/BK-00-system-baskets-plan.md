# BK-00 — System baskets programme: plan

- **Status:** Draft (written 2026-10-25 under the owner's delegation; every decision below was researched and is answered under "Decisions (researched)"; the owner's Approved is still needed before an agent builds a slice)
- **Phase / roadmap row:** BK — system baskets (index, derived, list), the base of the basket-leverage uses in [basket-leverage-uses](../research/basket-leverage-uses.md); not yet a roadmap row (the roadmap is not edited by this spec set)
- **Design refs:** docs/design/06-instruments-and-baskets.md §2.1, §2.3, §3.1, §3.4 · [ADR 0014](../adr/0014-portfolio-buckets-appear-as-system-baskets.md) · ISS-22 (docs/design/19-risks-and-compliance.md) · research [system-baskets-nse](../research/system-baskets-nse.md), [basket-leverage-uses](../research/basket-leverage-uses.md) · P8-07 / ADR 0102, P8-09 / ADR 0105
- **Needs:** built P1-15 / P1-15a (baskets, dated members, system protection), the instrument-master service, the services framework (`IScheduledService`), `bars_agg` daily bars.
- **Size:** BK-01 4 + BK-02 4 + BK-03 4 + BK-04 3 + BK-05 3 = **18 agent-days** for the five slices of this set (BK-06 to BK-12 are specified elsewhere and sized there)
- **Safety:** yes. Network fetches of NSE files are **OFF** until the owner enables them (after reading the terms and recording an ADR); every new setting defaults OFF; no order is ever placed or implied; baskets are read-only inputs.

## Goal
The Baskets screen offers ready-made, read-only baskets (official indices, our own behaviour baskets, regulatory and broker lists) with dated membership and an honest "members as of" label, so the owner and the later basket uses (rule scopes, reports, learning cells) have a defensible universe without hand-typing it.

## Scope
**In:** the slice split, order and dependencies; the eight proposed pre-registered baskets; the default benchmark recommendation; the shared rules (provenance, point-in-time, fail closed, settings OFF); the owner questions.
**Out:** any code; the uses of baskets (BK-06 to BK-12); editing the roadmap, JOURNEY.md or design docs (done at build time per slice).

## What exists now (checked in the repo 2026-10-25)
| Claim | Result |
|---|---|
| `market.baskets` / `market.basket_members` with dated membership (`valid_from`, `valid_to`, unique open row per basket and instrument) | VERIFIED in `BasketMemberConfiguration.cs`: checks `valid_to >= valid_from`, `(valid_to IS NULL) = (removed_by IS NULL)`, filtered unique index `ux_basket_members_active`. |
| `Basket` entity has no `kind`, no `system` column | VERIFIED (`Basket.cs`: name, colour, purpose, description, position, created/archived, revision). Portfolio "kind" is computed from buckets (ADR 0014), nothing stored. So BK-01 adds the column. |
| ADR 0014: portfolio buckets are read-only system baskets, `baskets.system` 409, "rule-based and the other system baskets later reuse the same `kind` and `baskets.system` protection" | VERIFIED (file read). |
| ISS-22: membership is as of one date, runs say "members as of today"; point-in-time membership not built | VERIFIED text in doc 19 §risk table; the roadmap row itself was not opened (UNVERIFIED). |
| Instrument-master service, daily 08:45 IST, `IScheduledService`, catch-up `Latest` | VERIFIED in `InstrumentMasterService.cs`. (The research note said 08:20; the code says 08:45, so the NSE lot-size job must be scheduled after 08:45.) |
| `master_refreshes` table | VERIFIED (`RefreshRunConfiguration.cs`, doc 06 §2.2). |
| Dhan flags `AsmGsmFlag`, `AsmGsmCategory`, `MtfLeverage` decimal(12,6) | VERIFIED (`InstrumentConfiguration.cs`). |
| `IPortfolioBenchmark` / `PortfolioBenchmarkStore` exist | VERIFIED (files exist); default value Nifty 50 per P8-07 text. |

## Slices, sizes, order, dependencies
| Slice | Title | Days | Needs | Notes |
|---|---|---|---|---|
| BK-01 | Index basket catalogue, fetcher and versioned membership | 4 | built baskets | adds `kind`, `system`, sources, fetches, provenance; network OFF |
| BK-02 | Derived baskets engine (pure) | 4 | none (pure) | can start in parallel with BK-01 (no shared files) |
| BK-03 | Derived baskets refresh job and scores | 4 | BK-01 (columns, kinds), BK-02 | writes versions after the EOD aggregation |
| BK-04 | System baskets on the Baskets screen | 3 | BK-01 (API), BK-03 for derived cards | read-only cards, provenance, as-of label, source status |
| BK-05 | Broker flags and F&O baskets | 3 | BK-01 | ASM/GSM, MTF/MIS from the Dhan master; lot sizes and ban list (ban list is a network fetch, OFF) |

Order: BK-01 and BK-02 in parallel (different files), then BK-03, BK-05, BK-04 (BK-04 last so it shows every kind). Elapsed with two writers is about 14 to 15 days (BK-03 needs 01+02, BK-04 needs 03+05); 11 was optimistic.

## The eight pre-registered baskets (proposed)
"Pre-registered" = fixed in advance, never tuned on results (anti-overfit, leverage note §4). Proposed:
1. Nifty 50 (index, BK-01) · 2. Nifty 100 (index) · 3. Nifty Midcap 150 (index) · 4. F&O stocks (list, BK-05, Dhan flag) · 5. Most liquid, top 100 by 20-day median traded value (derived #3) · 6. Low volatility 30 (derived #10) · 7. Momentum 12-1 top 30 (derived #6) · 8. Nifty Bank (index; the largest single sector, one sector basket stands in for the research's "one per 4 largest sectors" to keep the pre-registered set to eight, see Decision 3).
They are only *enabled by default as visible cards*, not tuned; enabling the fetch that creates the index ones is the owner's act.

## Decisions (researched)
1. **Default benchmark: keep Nifty 50 as the app-wide default; show Nifty 500 as the recommended choice for basket reports once its index series is tracked.** Question: Nifty 500 or 50? Reason: P8-07 and ADR 0020 §2 use one benchmark across the app (`IPortfolioBenchmark`); the baskets are equal-weight over a broad, mid-heavy tracked universe, for which a broad index (500 = about 96 % of listed free-float market cap per NSE's own description, UNVERIFIED figure) is the fairer comparison, but a Nifty 500 index series is not known to be in the master (UNVERIFIED) and changing a global setting silently would move every existing report. So: no code change now; BK-04 shows the owner a one-line hint, and the owner picks in Portfolio. Rejected: switching the global default to Nifty 500 now (unverified data, silent change of existing figures); a separate basket-only benchmark (P8-07 Decision 6 already rejected it). Source: P8-07 spec Decisions 5 and 6; research basket-leverage-uses Q2 (the question, no answer).
2. **Forward-only index membership, never invented history.** Reason: the CSVs show only the current list; ISS-22. Rejected: back-filling `valid_from` by guess; parsing press-release PDFs (stability UNVERIFIED). Source: research §3.1, §1.5.
3. **Pre-registered set is eight and includes one sector index.** Reason: the leverage note suggests 8 including "one per 4 largest sectors", which does not fit in eight with the seven others; one sector keeps the set at eight and the others are available unregistered. Rejected: 4 sectors (set of 11). Source: basket-leverage-uses Q1.
4. **Network off until the owner enables it.** Reason: licence terms unread (research §1.5). Rejected: default ON with a polite client. Source: research §1.5, owner instruction 2026-10-25.
5. **Setting namespace `Baskets:System:*`**, all defaults OFF, validated on start; enabling is an owner act (authenticated), recorded in the audit log with the setting snapshot; no setting ever allows an order (P7 places none). Source: CLAUDE.md standing instructions (auto option, 2026-10-23).
6. **Basket changes are data, not approvals.** A membership change is informational (notice, not an approval gate); if a later "re-test on change" approval is added it follows the auto/manual setting. Source: research §3.3.

## Acceptance criteria (for the plan itself)
1. Given the five slice specs, when read in the order above, then each lists only dependencies that are earlier in the order or built.
2. Given any BK slice, when built, then with every `Baskets:System:*` setting at its default, no outbound network call is made (a test with a counting HTTP handler proves zero calls).
3. Given the eight pre-registered baskets, then each appears in exactly one spec: 1-3 and 8 in BK-01 (index), 5 = derived #3 and 6 = derived #10 and 7 = derived #6 in BK-02/03, 4 in BK-05 (list).
4. Given the sizes, then the sum is 18 agent-days (4+4+4+3+3).

## Contract
No API or data here; see BK-01 to BK-05. Shared vocabulary: `kind` in `manual | portfolio | index | list | derived`; **stored** kinds are `manual | index | list | derived` (portfolio is computed from buckets, never stored); `system` is true for `index`, `list`, `derived` (and for the computed portfolio view); `provenance` in `fetched | imported | recomputed | derived | manual`.

## Non-functional
Each slice states its own budget. Programme rule: a system basket is a view of stored data; none adds a hot-path read to scans.

## Test plan
Per slice. Programme-level: the architecture test that no BK type has an order-named member; one end-to-end check at the end (fixture CSV in, basket card out, with the settings ON in a test host only).

## Risks
- Licence of NSE index files (the weakest point): mitigated by the OFF default, local-only storage, no weights, no public endpoint.
- Derived baskets look like alpha: each carries "what this basket is NOT" text and the in-sample warning (BK-02/04).
- Tracked universe is the owner's picks (ISS-22): a derived basket over untracked stocks is empty of bars; the owner question below.

## Open questions (owner)
1. May the system fetch NSE index files? (Read the terms at niftyindices.com and nseindia.com first; the answer is an ADR; until then OFF.)
2. Confirm the eight pre-registered baskets (Decision 3 and the list above).
3. Track all Nifty 500 members so derived universes are market-wide? Cost: about 500 instruments of bars/1m history; benefit: derived baskets stop being "your picks".
4. Benchmark: stay Nifty 50, or move to Nifty 500 once tracked (Decision 1)?
5. Do you want the Dhan MIS/MTF lists as baskets now (BK-05) or only in P12?
6. Numeric thresholds of the 14 derived baskets are defaults (BK-02); accept as pre-registered or change before build.

## UNVERIFIED
Roadmap row of ISS-22 text; `master_refreshes` table in code; Nifty 500 index series availability; NSE terms of use; stored column names of Dhan flags; the exact next free ADR number (taken at build time).

## Done when
- [ ] Owner answers the questions and sets each slice Approved
- [ ] Each slice's own Done-when is met; this plan is updated with the built ADR numbers

## Review fixes 2026-10-25
- Wording: portfolio is computed, never stored; stored kinds are `manual|index|list|derived`. AC3 now names the baskets by number (5 = derived #3, 6 = derived #10, 7 = derived #6).
- `master_refreshes` and the Dhan columns (`AsmGsmFlag`, `AsmGsmCategory`, `MtfLeverage` decimal(12,6)) are VERIFIED (table above corrected).
- Elapsed estimate corrected to about 14 to 15 days. Effort now **20 agent-days** (BK-01 4, BK-02 5, BK-03 4, BK-04 4, BK-05 3).
- **Question order:** "Track all Nifty 500?" must be answered before BK-03 starts (it fixes the universe); moved to question 1 of the build gate. Other questions follow.
- **Reservations:** ADR 0106 (fetch permission and terms, BK-01), 0107 (derived baskets, BK-02/03), 0108 (list baskets, BK-05); log event id blocks to be reserved in the first BK commit: `basket.fetch`, `basket.derived`, `basket.list` each get one block of 10 from the next free number (written into the logging catalogue at build, not guessed here).
- **Migration order:** `AddSystemBaskets` (BK-01, including `kind`, `system`, `note`-capable members, `source_ref uuid` + `source_kind`), `AddDerivedBasketVersions` (BK-03), `AddListBaskets` (BK-05). None edits an applied migration; later ones only add.
- **Shared column name:** one reference on `basket_members`: `source_ref uuid null` + `source_kind text null` (`fetch|version|refresh`). BK-01/03/05 use it (replaces `source_fetch_id`, `version_id`, `source_version_id`).
- **Settings state:** `GET /api/baskets` carries `fetchEnabled` and `derivedEnabled` (BK-01 contract).
