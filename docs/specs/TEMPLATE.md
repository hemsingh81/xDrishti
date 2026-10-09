# P<phase>-<nn> — <Title>

- **Status:** Draft | Approved | Done
- **Phase / roadmap row:** P<n> — <name>
- **Design refs:** docs/design/<nn>-<name>.md §<section>

## Goal
One or two sentences: the user-visible outcome.

## Scope
**In:** …  
**Out (not in this slice):** …

## Acceptance criteria
Numbered, each testable. The agent maps every item to a test or a verifiable check.
1. Given … when … then …
2. …

## Contract
- **API:** `METHOD /api/<module>/<resource>` → request/response shape, error cases (problem details codes).
- **Data:** tables/columns/indexes, hypertable? retention? migration name.
- **UI:** screen/route, states (loading, empty, error), match prototype `docs/prototype/<screen>`.
- **Events/jobs:** schedule, idempotency, failure handling.

## Non-functional
Performance budget (e.g. p95 latency, rows/s, bundle size), security, observability (log events, metrics).

## Test plan
Unit (domain/application), integration (API + DB), frontend (MSW), architecture impact. Edge cases to cover.

## Open questions
Anything the user must decide before the spec is Approved.

## Done when
- [ ] All acceptance criteria have evidence
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); migration added
- [ ] Design docs/ADR updated if behaviour changed
