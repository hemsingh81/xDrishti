namespace XDrishti.Application.Replay;

/// <summary>A run that passed its checks and is ready to be stepped: its context, its book (empty before the first session) and the trading sessions from D to E in order.</summary>
/// <param name="Run">The context every session of the run shares.</param>
/// <param name="Book">The replay book the sessions fill in.</param>
/// <param name="Sessions">The trading sessions of the range.</param>
internal sealed record ReplayPrepared(ReplayRunContext Run, ReplayBook Book, IReadOnlyList<DateOnly> Sessions);
