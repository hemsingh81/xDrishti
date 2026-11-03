using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.OptionExpression;

/// <summary>
/// <c>xd options expression-eval --family expr:option_buy --run RUNKEY [--profile HASH] [--holdout]</c> (P14-09): protocol O over an expression run of a stored full-test cash run. Without a profile the pre-registered primary profile is judged. <see cref="HoldOut"/> reads the reserved last six months, once,
/// after a stored development KEEP.
/// </summary>
public sealed record EvaluateExpressionCommand(string Family, string RunKey, string? ProfileHash, bool HoldOut) : ICommand
{
    /// <summary>The one family of this slice (a constant, so the host that parses the line names no domain type).</summary>
    public const string OptionBuyFamily = "expr:option_buy";
}
