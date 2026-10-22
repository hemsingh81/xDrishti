# P5-01 — Lab API: validate and the strategy library

- **Status:** Approved (owner, 2026-10-22, "approve all": the questions stand as recommended). Written 2026-10-22 from `main` at f4294e31. It **depends on P3-02c (strategy file) and P3-02d (storage and seeding), neither of which is built or specced**; every name taken from them (the parser, the schema generator, the hash, the store port, the tables) is **UNVERIFIED against code** and the spec must be re-read when they are written. Other code facts were not re-read for this draft and are marked UNVERIFIED.
- **Phase / roadmap row:** P5 — Strategy Lab v1; slice 1 of the plan [P5-00](P5-00-strategy-lab-plan.md).
- **Design refs:** docs/design/11-strategy-lab.md §1 (channels), §2 (language, model as built), §3 step 1 and 2, §6 (storage) · 03-target-architecture.md §6 · [ADR 0002](../adr/0002-config-and-strategies-in-app.md) (strategies in the database) · [ADR 0034](../adr/0034-the-rules-expression-language-lint-and-compiler.md), [ADR 0036](../adr/0036-the-typed-strategy-model-and-the-compile-of-its-sections.md) · P3-02c, P3-02d (future).

## Goal
The owner (page, upload or CLI later) can send a strategy text and get back either located problems or a stored, immutable, hashed version in his library; list his strategies and versions, download any version as a file, and clone one as the parent of a new family member. No run yet.

## Scope
**In:**
1. `POST /api/lab/validate`: text in, problems out (parse of P3-02c, then schema, then `StrategyCompiler.Compile` for every declared side and timeframe); no state is written.
2. `POST /api/lab/strategies`: create a family from a text (id from the text, `version` as written) or add a version to an existing family; the new version is stored with its text, canonical hash and parent hash; refused if invalid.
3. Reads: `GET /api/lab/strategies` (families with the latest version and counts), `GET /api/lab/strategies/{id}` (versions), `GET /api/lab/strategies/{id}/versions/{n}` (text, hash, problems-free flag), `GET …/versions/{n}/file` (download as YAML), `GET /api/lab/schema` (the JSON Schema of P3-02c for the editor).
4. Clone: `POST /api/lab/strategies/{id}/versions/{n}/clone` returns the text with a new id/version stub and the parent hash noted; it stores nothing until posted (the "change ONE variable" rule of doc 11 §3 is a UI hint, not enforced here).
5. A built-in (seeded by P3-02d) can be read, downloaded and cloned, never edited or deleted.
6. Located problems in the existing `Problem` shape (path, span, code, message; UNVERIFIED mapping) and problem-details codes `lab.invalid_strategy`, `lab.not_found`, `lab.version_exists`, `lab.builtin_readonly`.

**Out (not in this slice):** running anything (P5-02); the ledger (P5-03); delete and archive (a family may be hidden later; owner question 3); the assistant and the form builder; C# plugins; the page (P5-08a); import of a file from disk by the CLI (P5-07).

**Size.** About 2 to 2.5 agent-days (P5-00; not calibrated). Itemised, UNVERIFIED: validate endpoint and pipeline 0.6; store port use and create/version rules 0.6; reads and download 0.4; schema endpoint 0.1; API contract, grants and integration tests 0.5. Cut order: clone endpoint (the client can build the stub), then download.

## What exists (UNVERIFIED unless an ADR says it)
The parser and schema (P3-02c) and `lab.strategies` / `lab.strategy_versions` with the store port (P3-02d) are assumed per their roadmap rows: canonical text and SHA-256 "taken from the plan, not from the printed text"; versions immutable (`xd_app` has no UPDATE on a version row); the three built-ins seeded on first start. `StrategyCompiler.Compile(definition, timeframe, side)` returns problems located by path (ADR 0036). The Api host registers endpoint modules through `IEndpointModule` (a file listing of `Endpoints/` showed that interface; the pattern for problem details is UNVERIFIED).

## Acceptance criteria
1. Given the doc 11 §2 example text, when posted to validate, then the response has no problems and the canonical hash; given the same text with white space and key order changed, the hash is equal.
2. Given 10 mutated texts (a misspelt word in `buy.trigger[1]`, a fifth param, `[BUY, SELL]` without a sell block, a swing SELL, a filter with no data, an unknown key, a wrong type, an oversize input, an alias bomb, a duplicate key), when validated, then each returns the located problem named by its code and none returns 500.
3. Given a valid text, when posted to create, then a family and version 1 exist with text, hash, parent null and creation time; a second identical post returns the same version (idempotent by hash) and writes nothing.
4. Given an existing family, when a changed text with the next version number is posted, then a new version is stored with the parent hash of the previous latest; when the version number already exists with different content, then `409 lab.version_exists`; no row is updated, ever (a grants test as `xd_app`).
5. Given an invalid text, when posted to create, then 422 with the problems and no row.
6. Given a built-in id, when a create/version post targets it, then `409 lab.builtin_readonly`; read, download and clone work.
7. Given a stored version, when downloaded, then the file re-posted to validate gives the same hash (round trip).
8. The list and detail reads return only what is stored; a missing id or version is 404 `lab.not_found`.
9. Validate does not write, does not run a simulation and answers within 50 ms for the example text (p95, warm; UNVERIFIED budget, the compile of one side was 0.5 ms in P3-02b).
10. The OpenAPI contract is regenerated (`scripts/gen-api.sh`), every endpoint has the documentation entries `EndpointDocumentation` requires (UNVERIFIED), and the architecture tests stay green.

## Contract
- **API:**
  - `POST /api/lab/validate` body `{ "text": string, "format": "yaml" }` → `200 { valid: bool, hash?: string, problems: [{ path, span, code, message, severity }], sides: [...] }`; invalid text is still 200 (the answer is the problems); `413`/`422` only for transport faults (size over a limit, UNVERIFIED value).
  - `POST /api/lab/strategies` body `{ "text": string, "note"?: string }` → `201 { id, version, hash, parentHash }` or `200` on the idempotent repeat; `422 lab.invalid_strategy`, `409 lab.version_exists | lab.builtin_readonly`.
  - `GET /api/lab/strategies` → `[ { id, name, style, latestVersion, versions, builtIn, createdAt } ]`; `GET …/{id}`; `GET …/{id}/versions/{n}`; `GET …/{id}/versions/{n}/file` (`application/yaml`, attachment); `GET /api/lab/schema` (`application/schema+json`).
  - Auth: the existing owner session; no new identity (UNVERIFIED how the assistant identity of P5-00 criterion 6 is represented; no endpoint here is callable by it as a promote).
- **Data:** none new; uses `lab.strategies` and `lab.strategy_versions` of P3-02d (UNVERIFIED columns). If P3-02d lacks `parent_hash` or `note`, this slice adds them in migration `AddLabVersionParent` with `Down`. Insert-only for `xd_app` on versions.
- **UI:** none (P5-08a). The shapes are the page's contract; problem spans are item-relative as in ADR 0036 and the editor maps them.
- **Events/jobs:** none. Log events (new ids, UNVERIFIED which are free): `lab.strategy.created`, `lab.strategy.versioned`, `lab.validate.rejected` with family id, version, hash and counts; never the text.

## Non-functional
Untrusted text: size limit, parse to a generic tree first, alias and anchor limits (P3-02c). No secret, no network, no file path from the client. Validate is CPU-bound and bounded; a limit on text length applies before parsing. Idempotent writes by hash.

## Test plan
Unit: the validate pipeline order (parse, schema, compile) on tables of good and bad texts; the create/version rules (idempotent, conflict, built-in). Integration (Testcontainers): the endpoints end to end, the migration up/down if added, the grants test as `xd_app` (no UPDATE/DELETE on versions), concurrent create of the same hash returns one row. Contract: regenerated OpenAPI diff reviewed. Architecture: the Lab module may reach `XDrishti.Strategies` only through its public API; the parser package stays outside it (P3-02c). Edge cases: empty text; BOM; CRLF; very long single line; a text valid on BUY and invalid on SELL; two sides with different timeframes; a version number gap; a clone of a clone.

## Open questions (for the owner; each with a recommendation)
1. **Order.** Build P3-02c and P3-02d first (5 d)? Recommended: yes; this spec is unbuildable without them and should be re-read after they are specced.
2. **Version numbers.** Is `version` in the text authoritative (the owner bumps it) or assigned by the server? Recommended: server-assigned, the text's `version` must equal the next number or be omitted; one source of truth.
3. **Delete.** Allow hiding a family from the list (a flag) but never deleting versions (ledger and run records point at hashes)? Recommended: yes, as a later slice; nothing here.
4. **Format.** YAML only in the API, or also JSON? Recommended: YAML only; the canonical JSON is internal.
5. **Size limit.** 64 KB of text? Recommended: yes (UNVERIFIED value; a strategy is a few hundred lines).

## Done when
- [ ] The owner has answered questions 1 to 5 and the status is Approved; P3-02c and P3-02d are Built and this spec re-read
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added only if a column is missing
- [ ] ADR (UNVERIFIED next number) for the Lab API module and version rules; doc 11 §1, §6 and doc 03 §6 updated; `scripts/build-docs.sh` run
