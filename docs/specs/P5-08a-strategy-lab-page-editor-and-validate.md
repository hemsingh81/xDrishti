# P5-08a — Strategy Lab page: editor and validate

- **Status:** Approved (owner's standing run instruction, 2026-10-22: questions take the recommended answer).
- **Phase / roadmap row:** P5 — Strategy Lab v1; slice P5-08a of the plan [P5-00](P5-00-strategy-lab-plan.md); depends on P5-01 (built, [ADR 0057](../adr/0057-the-lab-api-validate-store-and-read-strategy-versions.md)).
- **Design refs:** docs/design/11-strategy-lab.md §1, §3 step 1; docs/prototype (Strategy Lab page, `app-pages-2.js`); P5-01 Contract.

## Assumptions (recommended answers taken under the standing instruction)
1. **Monaco weight.** `monaco-editor` 0.57.0 and `monaco-yaml` 5.5.1, both pinned exactly, are loaded only by the lazy `/lab` route and only when the editor mounts (a second dynamic import inside the route). The initial bundle does not change. No bundle-size gate exists in `scripts/check.sh` (UNVERIFIED budget of 200 KB gzip is a rule in `frontend/CLAUDE.md`); the lazy chunk is expected to be large (hundreds of KB gzip) and is paid only on this page.
2. **No CDN.** The editor and its workers are bundled by Vite (`new URL(..., import.meta.url)` workers); nothing loads from a third-party host.
3. **Live validate** is a debounced (500 ms) call to `POST /api/lab/validate` as the text changes; the server is the authority. The served JSON Schema (`GET /api/lab/schema`) gives the editor completion, hover and structural squiggles; the server's located problems are shown as markers and in a list.
4. **Problem placement.** The editor marks a problem where the server gives `line`/`column` (the field's place in the file). Rules-language spans (`start`/`length`, item-relative) are shown in the list with their path and code only; mapping them into the text is left out (the server does not give the item offset).
5. **Save.** "Save" posts the text (`POST /api/lab/strategies`); the server assigns the version (P5-01 question 2). A clone carries its `parentHash`. A built-in cannot be saved over (server `409 lab.builtin_readonly`, shown as a message).
6. **Download** is a plain link to the `…/file` endpoint (attachment); **upload** reads a local `.yaml`/`.yml` file into the editor as an unsaved draft (nothing is sent until Save; size over 256 KB is refused by the client with the server's limit).
7. **Runs are out.** The page shows the run area as "arrives with the next slice" (P5-08b); no run button, no promote.
8. **Test double.** jsdom cannot run Monaco; tests replace the editor module with a labelled textarea (same approach as the chart stub). Monaco itself is verified by the production build, not by unit tests.

## Goal
The owner opens Strategy Lab, browses his library (built-ins and own), opens a version in a YAML editor with schema help, sees problems as he types, and can save a valid text as a new family or version, clone, upload and download.

## Scope
**In:** route `/lab` (lazy) and nav entry (the "planned" mark removed); library list with latest version and counts; version list of the selected family; open a version; editor (Monaco YAML, schema from the API); live validate with located problems; Save; Clone; Upload; Download.
**Out:** running a test, progress, verdict, report, ledger (P5-08b); promote (P6); delete or hide (later); the form builder and the assistant.

## Acceptance criteria
1. Given the library has strategies, when the page opens, then each family is listed with its style, latest version, version count and a "Built-in" label where it applies; while loading a status is shown, and a failed read shows an error.
2. Given a family is chosen, then its versions are listed (number, hash prefix, note); choosing one loads its text into the editor.
3. Given the owner types, when 500 ms pass, then the text is sent to validate; a valid answer shows "Valid" with the hash prefix; an invalid one lists every problem with severity, code, path, message and place.
4. Given the editor shows a text, then the schema from `GET /api/lab/schema` is given to it; a failed schema read shows a notice and the editor still works without schema help.
5. Given a valid text, when Save is pressed, then it is posted and the new or existing version is selected with a confirmation; given an invalid text, Save is disabled; given a server refusal (409/422), then its message is shown and the text is kept.
6. Given a stored version, when Clone is pressed, then the editor holds the clone text as an unsaved draft; saving posts it with the parent hash.
7. Given a YAML file, when it is uploaded, then its text replaces the editor content as a draft and is validated; an oversize file is refused with a message.
8. Given a stored version is selected, then Download links to its file endpoint.
9. The Strategy Lab nav entry opens the page (no "coming in phase" screen) and the route is lazy.
10. `scripts/check.sh` is green.

## Contract
- **API:** uses only P5-01 endpoints: `GET /api/lab/strategies`, `GET /api/lab/strategies/{id}`, `GET …/versions/{n}`, `GET …/versions/{n}/file`, `POST …/clone`, `POST /api/lab/validate`, `POST /api/lab/strategies`, `GET /api/lab/schema`. No backend change; no regenerate.
- **Data:** none.
- **UI:** `features/lab` (`api/`, `components/`, `lib/`), public `LabPage`; two columns on wide screens (library left, editor and problems right), stacked on narrow; hue `lab`; match the prototype's Strategy Lab layout (Rules card with validation messages), minus the run controls and result card.
- **Events/jobs:** none.

## Non-functional
Accessible: labelled editor, problems as a list with text severity (not colour alone), file input labelled. Validate is cancelled by the debounce and by TanStack Query when the text changes. No strategy text in logs.

## Test plan
Vitest + Testing Library + MSW with the editor stub: library loading/error/list; open a version; validate valid and invalid; save success and refusal; clone then save with parent hash; upload; download link; nav entry present. Marker mapping is a pure function with its own unit test.

## Open questions
None open (all taken as recommended above).

## Done when
- [x] Owner answers taken as recommended (standing instruction 2026-10-22), status Approved
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green
- [x] No contract change, no migration
