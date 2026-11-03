using XDrishti.Application.Abstractions.Market.Eod;

namespace XDrishti.Application.Market.Eod;

/// <summary>What a bhavcopy parse produced: rows of the wanted series, rows left out for the series, rows rejected (unreadable numbers), and the layout name; or the refusal of the whole file.</summary>
internal sealed record BhavParse(IReadOnlyList<EodRow> Rows, int OtherSeries, int Rejected, string Layout);

/// <summary>Delivery figures by (symbol, series) as the file states them, plus rows that could not be read.</summary>
internal sealed record DeliveryParse(IReadOnlyDictionary<(string Symbol, string Series), DeliveryFigures> Figures, int Rejected);

internal sealed record DeliveryFigures(long Quantity, decimal? FilePercent);

/// <summary>The outcome of putting delivery on the price rows: rows that got delivery, rows whose delivery figure was rejected (delivery above traded, or the file's own percent off by more than 0.01 pp).</summary>
internal sealed record DeliveryMerge(IReadOnlyList<EodRow> Rows, int Merged, int Rejected);
