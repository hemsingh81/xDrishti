using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.OptionExpression;

/// <summary><c>xd options condors --profile HASH --from DATE --to DATE [--underlying NSE:NIFTY]</c> (P14-10): simulates the iron condor of the index range family under a registered <c>iron_condor</c> profile.</summary>
public sealed record SimulateCondorsCommand(string ProfileHash, DateOnly From, DateOnly To, string Underlying) : ICommand
{
    /// <summary>The default underlying.</summary>
    public const string DefaultUnderlying = "NSE:NIFTY";
}
