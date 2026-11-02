namespace XDrishti.Domain.Filings;

/// <summary>What ingest does with a new file of a group: <see cref="Supersedes"/> is the id of the revised filing, or null.</summary>
public sealed record RevisionDecision(RevisionOutcome Outcome, Guid? Supersedes);

/// <summary>The closed outcomes of <see cref="PointInTimeRule.Decide"/>.</summary>
public enum RevisionOutcome
{
    Store,
    StoreOutOfOrder,
    AmbiguousOrder,
    BadSupersede,
}
