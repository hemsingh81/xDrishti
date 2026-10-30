using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Execution;

/// <summary>
/// The rungs of the execution ladder that exist in this build (spec P12-07, ADR 0163): <c>paper</c>, <c>manual_live</c> and <c>assisted</c>. <c>semi_auto</c> and <c>auto_live</c> belong to P12-08 and P12-09 and are not values here:
/// every parser answers null for them, so a stored or posted one reads as unknown and never as a mode.
/// </summary>
public enum LadderMode
{
    Paper,
    ManualLive,
    Assisted,
}

/// <summary>Names, ranks and the one-step rule of the ladder. Pure.</summary>
public static class ExecutionModes
{
    public static string Name(LadderMode mode) => mode switch
    {
        LadderMode.Paper => "paper",
        LadderMode.ManualLive => "manual_live",
        LadderMode.Assisted => "assisted",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>The mode of an API or stored name; null for anything else, including <c>semi_auto</c> and <c>auto_live</c>.</summary>
    public static LadderMode? TryParse(string? name) => name switch
    {
        "paper" => LadderMode.Paper,
        "manual_live" => LadderMode.ManualLive,
        "assisted" => LadderMode.Assisted,
        _ => null,
    };

    public static int Rank(LadderMode mode) => (int)mode;

    /// <summary>A promotion moves exactly one rung up.</summary>
    public static bool IsAdjacentPromotion(LadderMode from, LadderMode to) => Rank(to) == Rank(from) + 1;

    public static bool IsDemotion(LadderMode from, LadderMode to) => Rank(to) < Rank(from);

    /// <summary>The configured mode of the account's Trading role as a rung.</summary>
    public static LadderMode Configured(TradingMode mode) => mode == TradingMode.ManualLive ? LadderMode.ManualLive : LadderMode.Paper;

    /// <summary>
    /// The effective mode: the newest trail row in force, else the configured one. Never above what the configuration allows (a configuration of <c>paper</c> is the ceiling: a <c>manual_live</c> or <c>assisted</c> row on such an account reads <c>paper</c>; configured <c>manual_live</c> permits up to <c>assisted</c>, the only rung above it that exists), and a trail name that is not
    /// a rung reads <c>paper</c>: the safer side.
    /// </summary>
    public static LadderMode Effective(string? trailMode, LadderMode configured)
    {
        if (trailMode is null)
        {
            return configured;
        }

        var parsed = TryParse(trailMode);
        if (parsed is null)
        {
            return LadderMode.Paper;
        }

        return configured == LadderMode.Paper ? LadderMode.Paper : parsed.Value;
    }
}
