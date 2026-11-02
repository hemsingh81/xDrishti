namespace XDrishti.Domain.Filings;

/// <summary>What the market knew about an instrument's shareholding at an instant: <c>value</c> with the current group (and the previous quarter's, for a change) or <c>unavailable</c> with the reason <c>no_entry</c>.</summary>
public sealed record ShareholdingAnswer(string State, ShareholdingSnapshot? Current, ShareholdingSnapshot? Previous, string? Reason)
{
    public const string Value = "value";

    public const string Unavailable = "unavailable";

    public const string NoEntry = "no_entry";
}
