using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// A stored strategy read, hash-checked and compiled once for a cell (spec P7-04, ADR 0088), with the exit of the cell matched to one of the strategy's exit candidates. Built once per cell and used for every instrument.
/// <see cref="Problem"/> says why it cannot be used (then every ticket from it is Unavailable). Immutable.
/// </summary>
public sealed class PreparedTicketStrategy
{
    private PreparedTicketStrategy(CompiledSide? side, string strategyId, string hash, string sideText, string? problem, ExitMatch? exit, bool dailyOnly)
    {
        Compiled = side;
        StrategyId = strategyId;
        Hash = hash;
        SideText = sideText;
        Problem = problem;
        Exit = exit;
        DailyOnly = dailyOnly;
    }

    internal CompiledSide? Compiled { get; }

    /// <summary>The strategy id (empty when the text did not read).</summary>
    public string StrategyId { get; }

    /// <summary>The version hash.</summary>
    public string Hash { get; }

    internal string SideText { get; }

    /// <summary>Why the strategy cannot be used, or null.</summary>
    public string? Problem { get; }

    internal ExitMatch? Exit { get; }

    /// <summary>Whether everything the side reads is on 1D or longer, so that daily bars are enough.</summary>
    public bool DailyOnly { get; }

    /// <summary>Reads the text, checks the hash, compiles the side and matches the cell's exit and style.</summary>
    public static PreparedTicketStrategy Prepare(string sourceText, string expectedHash, Timeframe timeframe, Side side, string cellStyle, string cellExit)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(expectedHash);
        var sideText = side == Side.Buy ? "BUY" : "SELL";
        if (StrategyFile.ReadForRun(sourceText) is not { } definition)
        {
            return new PreparedTicketStrategy(null, string.Empty, expectedHash, sideText, "the stored strategy text does not read", null, false);
        }

        if (!string.Equals(CanonicalText.Sha256Hex(CanonicalStrategy.Json(definition)), expectedHash, StringComparison.Ordinal))
        {
            return new PreparedTicketStrategy(null, definition.Id, expectedHash, sideText, "the stored text does not give the stored hash", null, false);
        }

        var compiled = StrategyCompiler.Compile(definition, timeframe, side);
        return compiled.Compiled is not { } compiledSide
            ? new PreparedTicketStrategy(null, definition.Id, expectedHash, sideText, "the strategy does not compile for " + timeframe.ToCode() + " " + sideText, null, false)
            : FromSide(compiledSide, expectedHash, cellStyle, cellExit);
    }

    /// <summary>Wraps an already compiled side (tests, callers that compiled it themselves).</summary>
    public static PreparedTicketStrategy FromSide(CompiledSide side, string hash, string cellStyle, string cellExit)
    {
        ArgumentNullException.ThrowIfNull(side);
        var sideText = side.Side == Side.Buy ? "BUY" : "SELL";
        var styleText = side.Style == Style.Swing ? "swing" : "intraday";
        if (!string.Equals(cellStyle, styleText, StringComparison.OrdinalIgnoreCase))
        {
            return new PreparedTicketStrategy(null, side.StrategyId, hash, sideText, "the cell style `" + cellStyle + "` is not the strategy's style `" + styleText + "`", null, false);
        }

        var dailyOnly = side.Features.All(f => f.Timeframe >= Timeframe.D1) && !side.NeedsContext && !side.NeedsExpiryDays
            && side.Items.All(i => !i.Condition.ReadsClock && !i.Condition.NeedsContext && !i.Condition.NeedsExpiryDays);
        return new PreparedTicketStrategy(side, side.StrategyId, hash, sideText, null, ExitMatch.Find(side.Exits, cellExit), dailyOnly);
    }
}
