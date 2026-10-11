namespace XDrishti.Application.Abstractions.Market;

/// <summary>A registered refresh waiting to be executed.</summary>
public sealed record RefreshJob(Guid RunId);
