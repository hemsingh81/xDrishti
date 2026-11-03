using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.OptionCosts;

/// <summary><c>xd options spread-model build [--underlying SYMBOL]</c> (P14-07): builds the half-spread model of one underlying, or of every underlying with chain sessions when <paramref name="Underlying"/> is null.</summary>
public sealed record BuildSpreadModelCommand(string? Underlying) : ICommand;
