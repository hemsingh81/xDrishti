# P15-01 — Filing sources and the folder drop: the port, one local adapter, the owner's drop script, and the public-page fetcher designed but not built

- **Status:** Approved (owner, 2026-11-02: pre-approved in the autonomous loop; open questions resolved with the recommended/safer answers listed in the spec)
- **Phase / roadmap row:** P15 — slice P15-01 of [P15-00](P15-00-fundamentals-and-events-plan.md) ("Point-in-time filings ...").
- **Design refs:** docs/design/20 §D · 19 §1, §5 · ADR 0133 (allow-list rule), 0097 (pinned list), 0083 (no order). P15-00 decisions D1 (folder drop is the default source) and D3 (point in time by broadcast time). New ADR: **next free number at build time** (the folder-drop sidecar contract and the recorded decision not to fetch).
- **Needs:** P1 (instruments by symbol, only for P15-02). **Needed by:** P15-02 (the store ingests what this adapter lists), P15-04 (the CSV drops).
- **Size:** 2 agent-days (estimate; the plan said 3 with the fetcher, which is not built here).
- **Safety:** **yes** — it reads files an outside party produced (untrusted bytes, untrusted names) and it is the slice that decides how the system may ever reach an exchange website. This slice sends **nothing**: no network code, no allow-list row, no setting that could start a request.

## Goal
The owner can put an exchange filing (results XBRL, shareholding, board-meeting list, announcement) into `data/filings/inbox/` together with a small sidecar that says when the exchange broadcast it, and the system can list those files in a fixed order with a hash, the broadcast time (or the honest word that there is none) and a reason for every file it refuses. Nothing else happens: no network, no database, no parsing.

## Verified facts (read 2026-11-02)
**Code:** a search of `backend/src` for filing, `IFilingsSource`, `filings.`, XBRL or announcement finds no code, table or port. The only P15 traces are the refusal text of `Simulator` ("there is no results calendar, P15"), the `news_results` line in `LossTagReport.NotBuilt`, and the `DaysToResults` fact of both checklists that nothing fills (see P15-04). The recorders of P14 have `Options:*` classes with `ValidateOnStart`, a paused service, and an allow-list guard test that pins exact lines of `BrokerEndpoints` and `PlanPhaseCheck.PinnedAllowList`.
**Plan facts that stay UNVERIFIED:** the exchange terms of use (P15-00: pages timed out; the snippets say content is the exchange's property and bulk scraping is banned on the data portal), the existence and shape of any public endpoint, XBRL file sizes (0.2 to 0.5 MB assumed).

## Scope
**In:**
- Domain (pure): `FilingKind` (closed list), `FilingSidecar` parse and validation, the broadcast-time rule, the file-name and extension rules.
- Application port `IFilingsSource` (`Abstractions/Filings`): `ListAsync` and `OpenAsync`, returning `FilingListing` (accepted entries plus rejected entries with reasons).
- Infrastructure `FolderDropFilingsSource` and `Filings:*` options (`ValidateOnStart`).
- `scripts/filings-drop.sh`: copies one file into the inbox and writes its sidecar from arguments (no network).
- Tests; the ADR; `docs/design/20` §D and `deploy/CLAUDE.md` notes (folder, 0700).

**Out (not in this slice):**
- The **public-page fetcher** (below, "Designed, not built").
- Any database table, any ingest, any copy of a file out of the inbox, any parser (P15-02, P15-03).
- A downloader script. The owner downloads files in a browser; `filings-drop.sh` only files them.
- Any API, UI or compose change (the mount comes with the first host that reads the folder, P15-02).

## The folder and the sidecar
**Layout** (`Filings:InboxPath`, default `data/filings/inbox`; inside the container `/data/filings/inbox`):
- The inbox is created `0700` by the script and by the first host that needs it. A file in it is `0600`.
- The adapter **never writes, moves or deletes** in the inbox. A re-scan lists the same files.

**A file** is a regular file in the inbox directory itself (no sub-folders are read, no symlink is followed). Accepted names match `^[A-Za-z0-9][A-Za-z0-9._-]{0,119}$`; accepted extensions (case-insensitive): `.xml`, `.xbrl`, `.csv`, `.pdf`, `.txt`. Its sidecar is the file named `<file>.meta.json` beside it. Names that end in `.meta.json` are sidecars, never filings. The name carries **no** meaning (no date, no symbol is read from it).

**Sidecar** (UTF-8 JSON, at most 4 KB, schema 1, no unknown keys):

| Key | Required | Rule |
|---|---|---|
| `schema` | yes | the number `1` |
| `symbol` | yes | NSE symbol, `^[A-Z0-9&-]{1,20}$`; resolved to an instrument by P15-02. The single character `*` is allowed only for the `row_level_ts` files below (a multi-company `.csv`; the rows name their symbols, P15-04) |
| `kind` | yes | `results`, `shareholding`, `board_meeting`, `announcement` |
| `period_end` | for `results`, and `shareholding` unless `row_level_ts` | `YYYY-MM-DD`, the quarter or year end the filing is for |
| `broadcast_ts` | see below | ISO-8601 **with an offset or `Z`**, e.g. `2026-07-24T16:12:00+05:30` |
| `source_ref` | no | free text up to 200 characters (a human note; a URL is allowed as text and never fetched) |

**Broadcast time rule (D3, look-ahead guard).** `broadcast_ts` is the exchange's dissemination time. It is **never** taken from the file's modified time, the file name, the sidecar's own creation time or the ingest time. It is `unavailable` (listed with `broadcast_ts: null` and `broadcast: unavailable` and a reason) when it is: absent or `null`; a date without a time; a time without an offset; unparsable; before 2000-01-01; or later than the current time plus 5 minutes (`broadcast_in_future`). It is stored as a UTC instant. The one exception to "required": for `kind` `board_meeting` and `shareholding` in a `.csv` file the **rows** carry their own times (P15-04) and the listing marks the entry `row_level_ts`.

## Acceptance criteria
1. **Listing.** Given an inbox with files, then `ListAsync` returns for each accepted file: symbol, kind, period end, broadcast time or `null`, the broadcast state (`ok`, `unavailable`, `row_level_ts`), the file name, the size in bytes and the SHA-256 of its bytes (lowercase hex). The list is ordered by symbol, kind, period end, then SHA-256, all ordinal, so any directory order gives the same list (property test with shuffled creation order).
2. **Idempotent and read-only.** Given two scans of an unchanged inbox, then the lists are equal and no file in the inbox changed (modified time, mode, content, set of names). Nothing is moved, renamed or deleted.
3. **Rejections are listed, never silent.** A file that cannot be accepted appears in `Rejected` with a closed reason: `no_sidecar`, `invalid_sidecar` (with the first problem), `bad_name`, `bad_extension`, `too_large` (above `Filings:MaxFileMb`, 20), `empty`, `not_a_regular_file` (a symlink, a directory, a device), `unreadable`. A sidecar without its file is `orphan_sidecar`. A rejected file is never opened beyond what the reason needs, and a symlink is never followed.
4. **Broadcast time.** Hand cases: `2026-07-24T16:12:00+05:30` gives the instant 10:42:00 UTC and `ok`; `2026-07-24T16:12:00Z` gives 16:12 UTC; `2026-07-24` (date only) gives `unavailable/date_only`; `2026-07-24T16:12:00` (no offset) gives `unavailable/no_offset`; `null`, absent and `"soon"` give `unavailable`; a time 10 minutes in the future of the clock (`TimeProvider`) gives `unavailable/broadcast_in_future`. In every `unavailable` case the file is still listed, so P15-02 can store and count it; the date is **never** substituted.
5. **Folder safety.** Given an inbox directory that is group- or world-accessible (mode wider than `0700`) on a POSIX host, then `ListAsync` returns no entries and one problem `inbox_permissions` (the owner fixes it with `chmod 700`); the adapter never changes a mode itself. A missing inbox is an empty list with `inbox_missing`, not an error. A path that resolves outside `Filings:InboxPath` (traversal in a name, a symlinked directory) is refused.
6. **Bounded reads.** Files are hashed by streaming (no file is read whole into memory) and at most `Filings:MaxFiles` (5,000) entries are listed per scan; more than that is `truncated: true`, never a silent cut. A file that grows or changes between the size check and the hash is `unreadable`.
7. **No network (transport test).** Given the folder-drop adapter, the port and the Domain types, then an architecture test shows none of them references `System.Net.Http`, `System.Net.Sockets`, `HttpClient`, `IHttpClientFactory`, `WebClient` or the broker clients; and a run of the adapter under a test host whose `HttpMessageHandler` throws on any call completes with zero calls. `BrokerEndpoints.Allowed`, `Describe()` and `PlanPhaseCheck.PinnedAllowList` are **unchanged** in this slice (the existing allow-list guard test passes with no diff to those lines).
8. **No fetch setting exists.** The string `filings.fetch` and the key `Filings:Fetch` appear nowhere in `backend/src` (architecture test). The setting is only *reserved* in docs/design/16 as "not built, default false".
9. **Drop script.** `scripts/filings-drop.sh --symbol RELIANCE --kind results --period-end 2026-06-30 --broadcast "2026-07-24T16:12:00+05:30" <file>` creates the inbox `0700` if missing, copies the file to `<SYMBOL>_<kind>_<period_end>_<sha8>.<ext>` (`0600`), writes the sidecar (`0600`), and prints the two paths. It refuses: a missing argument, a `--broadcast` without an offset (it says "give the time as shown by the exchange with +05:30"), an unknown kind, a name or extension the adapter would refuse, a file above the size limit, and a target that exists with different content (the same content is a no-op, exit 0). Without `--broadcast` it writes `"broadcast_ts": null` and says the filing will be unavailable for backtests. It contains no `curl`, `wget`, `nc`, `ssh`, `scp` or `python` (a test greps it).
10. **Untrusted content stays data.** File bytes are not parsed, decoded or executed in this slice; an XML file with a DTD or entity is hashed like any other; a sidecar `source_ref` containing a URL or an instruction is carried as text and never acted on. A hostile fixture set (name with `../`, a 1 GB sparse file, a symlink to `/etc/passwd`, a sidecar of 5 MB, a sidecar with duplicate keys, invalid UTF-8) is rejected with the right reasons and the scan finishes.
11. **Settings** (`Filings:*`, `ValidateOnStart`): `InboxPath` (default `data/filings/inbox`), `MaxFileMb` (20, range 1-200), `MaxFiles` (5,000, range 100-50,000). Logs `filings.scan` carry counts and reason codes only, never a file's content or a sidecar's `source_ref`.

## Contract
- **Port:** `IFilingsSource.ListAsync(FilingQuery, CancellationToken)` → `FilingListing { Accepted, Rejected, Problems, Truncated }`; `OpenAsync(FilingRef, CancellationToken)` → a read-only stream that re-verifies the SHA-256 on completion and fails `content_changed` when it differs. `FilingQuery` filters by symbol, kind and a broadcast range, all optional; the filter never changes the order.
- **API / data / UI / jobs:** none in this slice. (The first caller is the `filings-ingest` service of P15-02.)

## Designed, not built: the public-page fetcher (recorded out-of-scope decision)
**Decision (owner, 2026-11-02, safer answer to P15-00 open question 1): the fetcher is not built in this slice, and no code, host, allow-list row, setting or gate for it is added.** Reasons: the exchange terms are unverified (P15-00 sources), the endpoints are undocumented and cookie-gated, the system needs about 4,300 results files in five years which a folder drop handles (D1), and an automated fetch of an exchange website is a different kind of act from reading a local file.

**It may be built only as a later slice (provisionally `P15-01b`) after the owner has read the exchange terms and says so in chat.** That slice's spec must carry, at least:
- the owner's written statement of what the terms allow (the build agent does not decide this);
- exact-path allow-list rows added in the same commit as their use, with the pin and the guard test (ADR 0133/0097), one row per endpoint, no wildcard, no query;
- `filings.fetch.enabled` default **false**, changed only by the owner with a fresh password (the P14-02 pattern: deployment flag, owner's switch, paused service, three gates);
- at most one request per 3 seconds for the whole process, an honest `User-Agent`, `robots.txt` read first and obeyed, HTTP 429 and `Retry-After` obeyed (spacing doubles; three limited requests in a row pause the fetcher for the day), no cookie jar reuse across sessions, no browser impersonation, no CAPTCHA handling (a CAPTCHA or a block ends the run and the owner is told);
- fetched bytes written **only** into the inbox with a sidecar whose `broadcast_ts` comes from the response body when the exchange states it and is otherwise `null` (never the fetch time);
- a disk cap (the P15-02 `Filings:MaxFilesGb` and free floor) and logs with counts only.
Until then the owner downloads files by hand and files them with `scripts/filings-drop.sh`.

## Non-functional
Listing 1,000 files of 0.5 MB in ≤ 3 s (hash-bound; UNVERIFIED). Memory under 20 MB extra. The adapter holds no state between calls. No filing content in logs.

## Test plan
- **Unit (Domain):** the sidecar parser (every required key, unknown key, duplicate key, size, encoding), the broadcast rule table of criterion 4 on a fake clock, name and extension rules, ordering.
- **Infrastructure (temp directory):** criteria 1 to 6 and 10 with real files, including the hostile set; mode check skipped on a host without POSIX modes and marked as such.
- **Script test:** criterion 9 against a temp inbox (bash test; the no-network grep).
- **Architecture:** criteria 7 and 8; the allow-list guard test unchanged and green.

## Decisions (researched)
1. **Sidecar, not the file name, carries the metadata.** A name can be mangled by a browser or an unzip; a broadcast time inside a name invites guessing. Rejected: parsing `SYMBOL_QTR_DATE.xml` names (P15-00 allowed "sidecar or the name").
2. **An offset is mandatory.** A bare local time is exactly the look-ahead error D3 exists to prevent (it could be read as UTC or as a server's zone).
3. **No downloader in the script.** The plan said "a script is part of P15-01" and "a download script the owner runs"; a downloader is the fetcher by another name and carries the same terms question, so the script only files what the owner already saved.
4. **The adapter is read-only and refuses a loose folder.** Files in the inbox are untrusted; a world-writable inbox would let another local user plant a filing.
5. **A file without a usable broadcast time is listed, not hidden.** P15-02 stores and counts it as `unavailable` (D3); hiding it would hide the coverage hole.
6. **The fetcher is a recorded decision, not a toggle.** A `filings.fetch.enabled` flag that exists but is wired to nothing invites a later one-line enable; there is no key until the slice that implements it.

## Risks and honest limits
- The owner must supply the broadcast time by hand; a wrong time is a look-ahead error nothing here can detect. The script's help text says to copy the time the exchange shows on the filing.
- Sidecars add manual work for about 4,300 files; a batch helper (reading a table the owner prepares) is a later convenience, not this slice.
- `.pdf` and `.txt` are accepted and stored but nothing reads them before P15-08.

## Open questions (owner), with the resolution used
1. Build the fetcher now? **Resolved: no; designed only, built after the owner reads the terms.**
2. Accept the sidecar contract and the 20 MB limit? **Resolved: yes as written; limits are settings.**
3. Does the script download? **Resolved: no; it files local files only.**

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer: no blockers/majors (told to look for any network type, any allow-list diff, any path that trusts a file name or a modified time, any write into the inbox)
- [ ] No migration, no API change (so no `scripts/gen-api.sh`)
- [ ] ADR written; doc 20 §D and doc 16 (reserved key, settings) updated; `deploy/CLAUDE.md` notes the folder and its mode
