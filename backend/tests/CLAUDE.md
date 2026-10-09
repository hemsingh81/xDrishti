# Test rules (backend)

- Framework: xUnit v3 on Microsoft Testing Platform (`global.json`), Shouldly assertions, NSubstitute fakes,
  `FakeTimeProvider` for time. Shared packages come from `tests/Tests.props`.
- Projects: `<Layer>.Tests` for unit tests, `Architecture.Tests` for layer rules, `Api.IntegrationTests` for HTTP +
  real TimescaleDB (Testcontainers on Podman; `ContainerRuntime` finds the Podman socket automatically).
- Naming: class `<Subject>Tests`; method `What_happens_when_condition` (underscores allowed in tests).
- Arrange–Act–Assert with blank lines; one behaviour per test; use `[Theory]` for input tables.
- Unit tests never touch the network, disk, clock or database. Integration tests share one container per collection
  (`[Collection(ApiTestSuite.Name)]`) and must not depend on each other's data.
- Test builders/fixtures over copy-pasted setup. No `Thread.Sleep`/`Task.Delay` — use `FakeTimeProvider`.
- Bug fix = failing test first.
