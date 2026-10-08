# backend — .NET 10

Modular monolith: one solution, several hosts (Api, Worker, Feed, Mcp, Cli). Engine modules (Indicators,
Strategies, Simulation, Learning, Planning) are pure and have no I/O dependencies.

```
backend/
├── XDrishti.sln               # created in Phase 0
├── Directory.Build.props      # shared settings: nullable, analyzers, warnings as errors
├── src/XDrishti.*/            # projects — see docs/design/03-target-architecture.md §4
└── tests/                     # unit, integration (Testcontainers), look-ahead and golden-trade tests
```

Design: [architecture](../docs/design/03-target-architecture.md) · [roadmap](../docs/design/18-roadmap.md)
