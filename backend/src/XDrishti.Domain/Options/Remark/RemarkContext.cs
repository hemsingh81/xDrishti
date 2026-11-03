using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// What the re-mark of one underlying's trades is given besides the trades and the bars (spec P14-08): the trading calendar, the lot rows, the half-spread model (or the owner's assumption), the liquidity thresholds, the owner's charge sheet (null: the cash run was
/// gross, so the option side is gross too; <see cref="SheetChanged"/> when the stored sheet no longer hashes to the one the cash run used), the oldest vendor IV a fallback may use, and the underlying's price: <see cref="SpotPaiseBefore"/> gives the close, in paise, of the
/// one-minute bar that ended at the instant (null when there is none), so a model price never reads a price later than the bar it prices.
/// </summary>
public sealed class RemarkContext
{
    public required TradingCalendar Calendar { get; init; }

    public required Guid UnderlyingId { get; init; }

    public required IReadOnlyList<LotSizeEntry> Lots { get; init; }

    public HalfSpreadModel? SpreadModel { get; init; }

    public decimal? AssumedHalfSpreadPct { get; init; }

    public required LiquidityThresholds Liquidity { get; init; }

    public CostSheet? Sheet { get; init; }

    public bool SheetChanged { get; init; }

    public required int MaxIvAgeMinutes { get; init; }

    public required Func<DateTimeOffset, int?> SpotPaiseBefore { get; init; }
}
