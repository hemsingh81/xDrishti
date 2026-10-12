namespace XDrishti.Domain.Market.Bars;

/// <summary>
/// One 1-minute bar as it is read from a file or from Dhan: <see cref="EpochSeconds"/> is the start of the minute (UTC, a multiple of 60). Prices are raw
/// (not adjusted for corporate actions). A plain value: the checks that make it storable are in <see cref="BarRules"/>.
/// </summary>
public readonly record struct Bar(long EpochSeconds, double Open, double High, double Low, double Close, long Volume)
{
    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeSeconds(EpochSeconds);
}
