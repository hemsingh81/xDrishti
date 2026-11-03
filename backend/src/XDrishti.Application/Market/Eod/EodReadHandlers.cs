using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Eod;

/// <summary><c>market.eod_universe(asOf)</c>: the securities in the daily table on the last loaded day at or before the date (and within a week of it). Lists dead names too and never adds anything to the tracked instruments.</summary>
public sealed record ShowEodUniverseQuery(DateOnly AsOf) : IQuery<IReadOnlyList<EodUniverseRow>>;

internal sealed class ShowEodUniverseHandler(IEodStore store) : IQueryHandler<ShowEodUniverseQuery, IReadOnlyList<EodUniverseRow>>
{
    public ValueTask<IReadOnlyList<EodUniverseRow>> HandleAsync(ShowEodUniverseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return store.UniverseAsync(query.AsOf, cancellationToken);
    }
}

/// <summary>The daily-versus-minute findings of a date range (CF-04 AC 15). Reads only; works on stored data, so it needs no switch.</summary>
public sealed record CheckDailyAgainstMinuteQuery(DateOnly From, DateOnly To) : IQuery<Result<IReadOnlyList<DailyMinuteFinding>>>;

internal sealed class CheckDailyAgainstMinuteHandler(IEodStore store, IEodSettings settings) : IQueryHandler<CheckDailyAgainstMinuteQuery, Result<IReadOnlyList<DailyMinuteFinding>>>
{
    public async ValueTask<Result<IReadOnlyList<DailyMinuteFinding>>> HandleAsync(CheckDailyAgainstMinuteQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.To < query.From || query.To.DayNumber - query.From.DayNumber > 366)
        {
            return Result.Failure<IReadOnlyList<DailyMinuteFinding>>(Error.Validation("history.bad_range", "Give a range of at most 366 days."));
        }

        var rows = await store.DailyAgainstMinuteAsync(query.From, query.To, cancellationToken);
        return Result.Success(DailyMinuteComparer.Compare(rows, settings.VolumeTolerancePct));
    }
}
