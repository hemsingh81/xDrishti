namespace XDrishti.Api.Endpoints;

/// <summary>The owner's limits sheet. A missing value is unset and allows nothing. Times are India time HH:mm. The password is needed only for a raise. The actor is never taken from the body.</summary>
internal sealed record LimitsChangeBody(
    int? MaxOrdersPerDay,
    decimal? MaxNotionalPerOrder,
    decimal? MaxNotionalPerDay,
    decimal? MaxLossPerDay,
    int? MaxOpenPositions,
    IReadOnlyList<string>? AllowList,
    string? WindowStart,
    string? WindowEnd,
    IReadOnlyList<string>? Products,
    bool? ExitAllOnLossHalt,
    string? Reason,
    string? Password);
