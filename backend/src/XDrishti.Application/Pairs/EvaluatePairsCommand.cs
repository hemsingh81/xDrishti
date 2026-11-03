using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Pairs;

/// <summary><c>xd pairs eval --family pairs:intraday-1 [--holdout]</c> (P14-13): the verdict of protocol S over the stored trades. <see cref="HoldOut"/> reads the reserved last six months, once, after a stored development KEEP.</summary>
public sealed record EvaluatePairsCommand(string Family, bool HoldOut) : ICommand
{
    /// <summary>The one family of the pairs study, <c>pairs:intraday-1</c> (a constant, so the host that parses the line names no domain type).</summary>
    public const string IntradayFamily = PairFamilies.Intraday;
}
