using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Lab;

/// <summary>
/// Posts a Lab run (spec P5-02): a stored strategy version (the latest when <paramref name="Version"/> is null), exactly one of a basket and a list of symbols, the method (<c>quick</c>) and optionally the window. The run is queued, never executed here.
/// </summary>
public sealed record StartLabRunCommand(string? StrategyId, int? Version, Guid? BasketId, IReadOnlyList<string>? Symbols, string? Method, DateOnly? From, DateOnly? To) : ICommand;
