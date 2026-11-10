# P15-07 — Announcement eval set and labelling: an announcements loader, a pre-registered category baseline, a stratified sample of 200, the owner's labels, and set D of the P9-09 eval

- **Status:** Built 2026-11-03 (ADR 0188). Approved (owner, 2026-11-03, blanket approval of all stories)
- **Phase / roadmap row:** P15 — slice P15-07 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Announcement eval set and labelling"), the first slice of the LLM branch (cut line: cut before PEAD).
- **Design refs:** docs/design/14 §4 item 5, §5 (200 labelled announcements, precision on "material" ≥ 80 %) · 20 §D · P15-00 protocol L, D10 · P9-09 (set D, `announcements.schema.json`, `AnnouncementGrader`) · ADR 0143 (the eval), 0179 (the inbox and sidecar), 0180 (envelopes). New ADR: **next free number at build time** (the announcements csv, the sample and the baseline), plus an ADR 0179 amendment (a third `row_level_ts` kind).
- **Needs:** P15-02 (built), P9-09 (built: set D grades no rows today and says `not_evaluable`). **Owner acts:** the announcements csv files (the owner prepares them from the exchange pages, as for P15-04), and the labels (an estimated 2 to 3 hours, UNVERIFIED). **Needed by:** P15-08 (protocol L).
- **Size:** 2.5 agent-days (estimate; the plan said 2; +0.5 for the loader the plan did not list).
- **Safety:** no — local files, its own rows and files in `data/`; no model call, no network. The announcement text is untrusted third-party text: it is stored and exported as data, never interpreted.

## Goal
The owner can drop announcements (symbol, broadcast time, exchange category, subject, text) as a csv, the system stores each as a point-in-time envelope, draws a fixed, stratified sample of 200 for labelling, and, once the owner has labelled them, set D of `xd eval run` has 200 rows to grade. A deterministic baseline by exchange category is fixed **before** the labels exist, so the model in P15-08 has an honest bar to beat.

## Verified facts (read 2026-11-02)
**Code:**
- `filings.announcements(filing_id pk, category varchar(60) null, headline varchar(300) null)` exists and **nothing writes it**; the text is meant to live in the stored file. The P15-01 sidecar (schema 1, no unknown keys) has **no** category or headline key; `FilingSidecar.IsRowLevel` is true only for `.csv` of `board_meeting` and `shareholding`. So no slice of the plan stores an announcement's category, subject or text today.
- `eval/sets/announcements.schema.json` requires `id, symbol, published_at, source, text, label` with `label` in `material | non_material` and `additionalProperties: false`: a row with an empty label is invalid, so the plan's "exported with label empty" cannot be that file.
- `RunEvalHandler` grades set D with `AnnouncementGrader.Grade([], …)` (always empty); `eval/thresholds.json` has `events: {precision_material_min 0.80, min_rows 200}`. The eval fingerprint includes the sets hash.

## Scope
**In:**
- Announcements csv (format below), `AnnouncementCsv` parser (Domain, pure), `LoadAnnouncementsHandler` in the `filings-ingest` run (after the other loaders), `IsRowLevel` extended to `announcement` + `.csv`.
- `AnnouncementSampler` (Domain, pure, seeded), `AnnouncementCategoryBaseline` (Domain) and `eval/sets/announcement-baseline.json`.
- Verbs `xd eval announcements sample|import|show`.
- Set D wiring: the labelled file is read by the eval; `AnnouncementGrader` receives rows (predictions come in P15-08; until then the set is `not_evaluable: no classifier yet (P15-08)`).
- `eval/README.md` section "Labelling announcements" (the definition of *material* given to the owner).
- Tests; ADR; doc 14 §5 and doc 20 §D notes.

**Out:** any model call (P15-08); PDF attachments (stored if dropped as files, never read); labelling UI (the owner edits a csv); a fetcher; news outside exchange filings; a change to the thresholds (P15-08).

## The announcements csv
UTF-8, RFC 4180, header exactly `symbol,broadcast_ts,category,subject,text`, at most 20,000 rows and the P15-01 size limit; sidecar `kind announcement`, symbol `*`, no `broadcast_ts`.
| Column | Rule |
|---|---|
| `symbol` | tracked instrument (P15-02 resolution) |
| `broadcast_ts` | ISO-8601 with offset (P15-01 rule); blank or bad = `unavailable` |
| `category` | the exchange's category as shown, 1-60 characters |
| `subject` | 1-300 characters → `headline` |
| `text` | 1-4,000 characters; longer = `bad_row/too_long` (never truncated) |

Each row is one envelope (`kind announcement`, `period_end` null, `broadcast_ts` from the row) plus one `filings.announcements` row in one transaction; the canonical row text (instrument, time to the microsecond, category, subject, text, NFC-normalised) is written into the files store under its SHA-256, which is the envelope's `content_sha256`, so the same row in two files is one row. Announcements are never grouped (P15-02: no period).

## Sample and labels
- **Baseline, pre-registered.** `eval/sets/announcement-baseline.json` maps exchange categories to `material` or `non_material` (unknown categories → `non_material`), is committed **before** the first sample, and its SHA-256 is written into the sample. Starting list (category names UNVERIFIED, adjusted to the owner's files before the sample): results, outcome of board meeting with results, credit rating, order win / award of contract, acquisition, merger or demerger, fund raising, change in auditor, change of KMP at board level, litigation or regulatory action, default → `material`; trading window, newspaper publication, certificates under the depository regulations, investor-meet intimation, shareholder-meeting notices, loss of share certificates → `non_material`.
- **Sample.** `xd eval announcements sample --from --to --seed 1337` draws from envelopes with state `ok`: 240 announcements (200 and 40 reserve, in a fixed order), half from baseline-`material` categories and half from the others (so that the model's material class can reach the ≥ 30 predicted cases protocol L needs), and within each half proportional by year with at least one per non-empty (category, year) stratum. It writes `data/eval/announcements/to-label.csv` (`id, filing_id, symbol, published_at, category, subject, text, label`) with `label` blank, `0600`, and `sample.json` (sampler version, seed, range, baseline hash, strata counts, the inclusion weight of each row).
- **Labels.** The owner writes `material`, `non_material` or `unsure` in each row (definition in `eval/README.md`, written before the sample). `xd eval announcements import <csv>` checks that every id is from the sample, the text columns are unchanged (hash per row), the baseline hash is unchanged since the sample (else refused `eval.baseline_changed`), and replaces each `unsure` by the next reserve row (which must then be labelled; fewer than 200 definite labels = refused `eval.too_few_labels`). It writes `data/eval/sets/announcements.jsonl` (the P9-09 schema, git-ignored) and the committed `eval/sets/announcements.manifest.json` (ids, filing ids, row hashes, label-file hash, sampler version, seed, baseline hash, class counts; **no text**).

## Acceptance criteria
1. **Loader.** Given a csv of announcements with a valid sidecar, then each good row is one envelope and one `filings.announcements` row and one stored text file, idempotent (reload inserts 0); rejects counted per reason (`bad_header`, `bad_row` with line and column, `too_long`, `unknown_instrument`, `ambiguous_symbol`, `too_many_rows`, `bad_encoding`); an undated row is stored `unavailable` and counted; logs carry counts and codes only, never a subject or text.
2. **Point in time.** An announcement is visible at `t` only from `broadcast_ts + lag` (the P15-02 rule; property test as P15-02 criterion 8 on `kind announcement`).
3. **Sidecar rule.** `IsRowLevel(announcement, .csv)` is true; an announcement `.txt` or `.pdf` with a single sidecar time is still an ordinary envelope (no row in `filings.announcements`, counted `no_metadata`, never sampled). ADR 0179 amended.
4. **Sampler.** Same envelopes, seed and baseline → byte-identical `to-label.csv` and `sample.json` in any insert order (property); the halves and the per-stratum minimum hold; undated, rejected or non-csv announcements are never sampled; with fewer than 240 eligible rows it refuses `eval.too_few_announcements` and names the count.
5. **Import.** Each refusal above is tested; a changed text cell, an unknown id, a duplicate id, a label outside the three words, or fewer than 200 definite labels refuse with the row named; a valid import writes the jsonl (which validates against `announcements.schema.json`, `source` = `nse-announcement`, `category` = the exchange category) and the manifest; re-importing the same file is a no-op.
6. **Baseline.** `AnnouncementCategoryBaseline.Predict(category)` is a pure lookup; a category the file does not list is `non_material`; the baseline file's hash is in the manifest and in the eval fingerprint.
7. **Set D reads the file.** With `data/eval/sets/announcements.jsonl` present and valid, `xd eval run --set events` loads 200 rows and reports `not_evaluable: no classifier yet (P15-08)` (not the old "arrive with P15" text); absent, it reports the old state; invalid, it fails the set with the row named. The sets hash (P9-09 fingerprint) includes the manifest and the label file's hash.
8. **No text in git.** A test asserts that nothing under `eval/` contains a `text` field of the sample, and `.gitignore` covers `data/eval/`.
9. **Reporting honesty.** `xd eval announcements show` prints the class counts, the label distribution per half, and states that precision on this sample is a stratified estimate, with the population-weighted precision formula P15-08 will print.

## Contract
- **Verbs:** `xd eval announcements sample|import|show`. **No API.**
- **Data:** no migration (`filings.announcements` and the files store exist); files under `data/eval/announcements/` and `data/eval/sets/` (`0700`/`0600`), `eval/sets/announcement-baseline.json` and `announcements.manifest.json` committed.
- **Jobs:** the loader inside `filings-ingest` (paused by default).

## Non-functional
Loading 32,000 rows ≤ 30 s; sampling over 160,000 envelopes ≤ 5 s. No network, no model.

## Test plan
- **Unit:** csv parser (each reject), canonical text and hash, sampler strata and determinism, baseline lookup, import checks.
- **Integration (Timescale):** loader end to end with a synthetic drop, idempotency, the point-in-time property, set D reading a synthetic labelled file.
- **Architecture:** announcements and the sampler are referenced only by the loader, the verbs, the eval and tests.

## Decisions (researched)
1. **A csv with text, not one sidecar per announcement.** About 32,000 announcements a year cannot carry a hand-written sidecar each; the P15-04 row pattern fits. Rejected: PDF text extraction (D4 and a new library).
2. **The baseline is fixed before the labels.** A baseline written after seeing labels would be fitted to them.
3. **Half the sample from baseline-material categories.** A proportional sample may hold too few material cases for a precision with ≥ 30 predictions; the weights are kept so the population figure can still be printed.
4. **Third-party text stays in `data/`.** The exchange terms are unverified (P15-00); the repo keeps a manifest only.

## Risks and honest limits
- The owner's labels are one person's judgement; the definition in the README is the only guard.
- The category names on the exchange pages are UNVERIFIED and can change; an unknown category falls to `non_material`.
- Stratified precision is not population precision; both are printed in P15-08.

## Open questions (owner)
1. Prepare the announcements csv and label 200 (+ replacements)? **Default if not done: set D stays `not_evaluable`, the LLM branch waits.**
2. Keep the labelled set out of git (manifest only)? **Default: yes.**
3. Accept the baseline category table? **Default: the starting list, adjusted to your files' category names before the sample.**

## Done when
- [ ] All acceptance criteria have evidence (the labels are the owner's)
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any announcement text in git or logs, any sample that depends on order, any baseline change after the sample)
- [ ] ADR and the ADR 0179 amendment; doc 14 §5, doc 20 §D and `eval/README.md` updated
