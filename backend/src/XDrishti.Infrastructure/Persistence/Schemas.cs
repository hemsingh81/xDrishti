namespace XDrishti.Infrastructure.Persistence;

/// <summary>PostgreSQL schema per module (see docs/design/03-target-architecture.md).</summary>
public static class Schemas
{
    public const string Platform = "platform";

    public const string Auth = "auth";

    public const string Accounts = "accounts";

    public const string Market = "market";

    public const string Portfolio = "portfolio";

    /// <summary>Simulation results (spec P3-10): <c>sim_runs</c> and <c>sim_trades</c>, insert-only for the runtime role.</summary>
    public const string Research = "research";

    /// <summary>Stored strategies (spec P3-02d, ADR 0056): <c>strategies</c> and <c>strategy_versions</c>, insert-only for the runtime role.</summary>
    public const string Lab = "lab";

    /// <summary>The plan record (spec P7-03, ADR 0083): plans, items, allocations, state events and runs, all insert-only for the runtime role.</summary>
    public const string Trading = "trading";

    /// <summary>Decisions (spec P7-03): <c>approvals</c>, insert-only for the runtime role, owner decisions only.</summary>
    public const string Workflow = "workflow";

    /// <summary>The paper record (spec P10-01, ADR 0116): epochs, entries, exits, skips and capture runs, all insert-only for the runtime role.</summary>
    public const string Paper = "paper";

    /// <summary>The assistant's switches (spec P9-02, ADR 0137): <c>settings</c>, insert-only for the runtime role. The views the model reads are in schema <c>aiv</c>, which no EF entity maps.</summary>
    public const string Ai = "ai";

    /// <summary>The option archive's reference data (spec P14-01, ADR 0175): <c>contracts</c>, <c>lot_sizes</c> and <c>market_epochs</c>, raw SQL, no EF entity; kept for good.</summary>
    public const string Options = "options";

    /// <summary>The point-in-time filing store (spec P15-02, ADR 0180): <c>filings</c>, <c>result_facts</c>, <c>shareholding</c>, <c>announcements</c> and <c>calendar</c>, raw SQL, no EF entity, all insert-only for the runtime role.</summary>
    public const string Filings = "filings";

    /// <summary>The kill rules of the decay monitor (spec CF-12 part 12b, ADR 0219): <c>decay_checks</c> and <c>kill_settings</c>, insert-only for the runtime role, and <c>kill_proposals</c>, insert plus a conditional update of four decision columns. Raw SQL, no EF entity.</summary>
    public const string Confidence = "confidence";

    /// <summary>The readiness scorecard (spec CF-11 part 1, ADR 0221): <c>settings</c> (the owner's enforce switch) and <c>scorecard_snapshots</c>, both insert-only for the runtime role. Raw SQL, no EF entity. A schema of its own, so that the grants of the <c>confidence</c> schema are not touched.</summary>
    public const string Readiness = "readiness";

    /// <summary>The learned setting changes (spec AU-10, ADR 0247): <c>learned_setting_changes</c>, insert-only for the runtime role except a column grant on the decision columns. Raw SQL, no EF entity. A schema of its own, so that no other grant is touched.</summary>
    public const string Config = "config";
}
