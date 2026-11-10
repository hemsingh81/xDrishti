namespace XDrishti.Infrastructure.Market.DerivedBaskets;

/// <summary>A row of <c>market.basket_versions</c>: one computed version of a derived basket. Insert-only for the runtime role (the point-in-time record is never edited).</summary>
internal sealed class BasketVersionRecord
{
    public Guid Id { get; set; }

    public Guid BasketId { get; set; }

    public DateOnly AsOf { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateTimeOffset? FormedAt { get; set; }

    public string Params { get; set; } = "{}";

    public string ParamsHash { get; set; } = string.Empty;

    public int UniverseSize { get; set; }

    public int MemberCount { get; set; }

    public int BelowThreshold { get; set; }

    public string Excluded { get; set; } = "[]";

    public string Outcome { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string Provenance { get; set; } = string.Empty;
}

/// <summary>A row of <c>market.basket_member_scores</c>: the score and rank of a ranked instrument of a version (the members and the next non-members). Insert-only.</summary>
internal sealed class BasketMemberScoreRecord
{
    public Guid VersionId { get; set; }

    public Guid InstrumentId { get; set; }

    public double Score { get; set; }

    public int Rank { get; set; }

    public bool IsMember { get; set; }

    public int SessionsHeld { get; set; }
}

/// <summary>A row of <c>market.derived_definitions</c>: the switch (default off) and the selected parameter set of a derived basket.</summary>
internal sealed class DerivedDefinitionRecord
{
    public Guid BasketId { get; set; }

    public int DefinitionNo { get; set; }

    public bool Enabled { get; set; }

    public string Cadence { get; set; } = string.Empty;

    public Guid? ParamsSetId { get; set; }
}

/// <summary>A row of <c>market.basket_param_sets</c>: an immutable set of parameter overrides; a change is a new row. Insert-only.</summary>
internal sealed class BasketParamSetRecord
{
    public Guid Id { get; set; }

    public int DefinitionNo { get; set; }

    public string Params { get; set; } = "{}";

    public string ParamsHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
