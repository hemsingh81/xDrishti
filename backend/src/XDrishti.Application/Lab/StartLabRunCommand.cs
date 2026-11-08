using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Lab;

/// <summary>
/// Posts a Lab run (spec P5-02): a stored strategy version (the latest when <paramref name="Version"/> is null), exactly one of a basket and a list of symbols, the method (<c>quick</c>) and optionally the window, optionally <c>slippage: conservative</c> (tiered slippage floors; CF-05, D2) and <c>stress</c> (the cost stress block; on by default for <c>robust</c>, off for <c>quick</c> and <c>full</c>). <paramref name="Nulls"/> false leaves the chance and plateau block out of a <c>robust</c> run and <paramref name="Draws"/> sets its number of random-entry seeds (CF-07, ADR 0231); both make the run a labelled what-if. The run is queued, never executed here.
/// </summary>
public sealed record StartLabRunCommand(string? StrategyId, int? Version, Guid? BasketId, IReadOnlyList<string>? Symbols, string? Method, DateOnly? From, DateOnly? To, string? Slippage = null, bool? Stress = null, bool? Nulls = null, int? Draws = null) : ICommand;
