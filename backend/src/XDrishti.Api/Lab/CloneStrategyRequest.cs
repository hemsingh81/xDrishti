namespace XDrishti.Api.Lab;

/// <summary>Body of the clone endpoint: the id of the new strategy; omitted keeps the id (a built-in gets <c>&lt;ID&gt;_COPY</c>).</summary>
internal sealed record CloneStrategyRequest(string? NewId = null);
