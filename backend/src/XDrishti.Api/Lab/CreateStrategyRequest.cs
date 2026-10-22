namespace XDrishti.Api.Lab;

/// <summary>Body of <c>POST /api/lab/strategies</c>: the text, an optional note (at most 500 characters) and the hash of the version it was cloned from. There is no way to ask for a built-in: the request cannot set it.</summary>
internal sealed record CreateStrategyRequest(string Text, string? Note = null, string? ParentHash = null);
