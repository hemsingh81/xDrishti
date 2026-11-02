namespace XDrishti.Domain.Filings;

/// <summary>One stored row of <c>filings.result_facts</c> for a filing: <see cref="Value"/> is null exactly when the state is <c>unavailable</c>.</summary>
public sealed record StoredResultFact(string Field, string Span, string Basis, decimal? Value, string Unit, string? Reason)
{
    public string State => Value is null ? "unavailable" : "value";
}
