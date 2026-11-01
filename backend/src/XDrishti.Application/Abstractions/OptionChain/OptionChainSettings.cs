namespace XDrishti.Application.Abstractions.OptionChain;

/// <summary>Which expiries of an index the recorder asks every slot: the next <paramref name="Weekly"/> expiries and the next <paramref name="Monthly"/> month-end expiries of its expiry list (the same date counts once).</summary>
/// <param name="Symbol">The canonical symbol of the index, <c>NSE:NIFTY</c>.</param>
/// <param name="Weekly">How many of the earliest expiries of the list.</param>
/// <param name="Monthly">How many of the earliest month-end expiries of the list.</param>
public sealed record ChainExpiryRule(string Symbol, int Weekly, int Monthly);

/// <summary>
/// The numbers of the option-chain recorder (<c>Options:Chain:*</c>, P14-02, documented in doc 16), checked at start by the host. <see cref="Enabled"/> is the deployment gate: with it false nothing reads credentials or builds a request.
/// <see cref="Spacing"/> is the gap between any two requests of the process and is never below <see cref="MinSpacing"/> (Dhan: one unique request every 3 seconds; UNVERIFIED whether that is per user, so the strictest reading is used).
/// </summary>
public sealed record OptionChainSettings(
    bool Enabled,
    int IntervalMinutes,
    TimeSpan Spacing,
    int StrikesEachSide,
    int StockStrikesEachSide,
    IReadOnlyList<ChainExpiryRule> IndexExpiries,
    int StockExpiries,
    TimeSpan Timeout,
    long MaxStoredBytes,
    long MinFreeBytes)
{
    /// <summary>One gibibyte.</summary>
    public const long Gb = 1024L * 1024 * 1024;

    /// <summary>The lowest spacing there is, whatever the setting says.</summary>
    public static readonly TimeSpan MinSpacing = TimeSpan.FromSeconds(3);

    /// <summary>The default expiries: Nifty the next 3 weekly and the next monthly, Bank Nifty the next 2 monthly.</summary>
    public static IReadOnlyList<ChainExpiryRule> DefaultIndexExpiries { get; } = [new("NSE:NIFTY", 3, 1), new("NSE:BANKNIFTY", 0, 2)];

    /// <summary>The defaults: OFF.</summary>
    public static OptionChainSettings Off { get; } = new(false, 5, MinSpacing, 30, 15, DefaultIndexExpiries, 2, TimeSpan.FromSeconds(10), 4 * Gb, 20 * Gb);
}
