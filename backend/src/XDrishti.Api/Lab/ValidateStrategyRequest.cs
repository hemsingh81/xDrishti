namespace XDrishti.Api.Lab;

/// <summary>Body of <c>POST /api/lab/validate</c>: the strategy text and its format (only <c>yaml</c>; omitted means yaml).</summary>
internal sealed record ValidateStrategyRequest(string Text, string? Format = null);
