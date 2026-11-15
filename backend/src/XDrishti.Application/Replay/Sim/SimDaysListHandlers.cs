using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>The simulations, newest first, a page at a time (SR-04). Read only.</summary>
internal sealed class ListSimDaysHandler(ISimDaysStore store) : IQueryHandler<ListSimDaysQuery, Result<SimDaysListView>>
{
    public async ValueTask<Result<SimDaysListView>> HandleAsync(ListSimDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (SimDaysPaging.Read(query.Limit, query.Cursor) is not { } page)
        {
            return Result.Failure<SimDaysListView>(SimDays.Invalid(SimDaysPaging.Message));
        }

        var rows = await store.ListAsync(page.Offset, page.Limit + 1, cancellationToken);
        var items = rows.Take(page.Limit).Select(r => new SimDaysListItemView(
            r.Id, r.Phase, SimDays.IsActive(r.Phase), r.Auto, r.Mode, new SimDaysScopeView(r.Baskets, r.Instruments), r.Start, r.End, r.Cursor, r.Capital, r.LastEquity ?? r.Capital, r.DaysDone, r.CreatedAt)).ToList();
        return Result.Success(new SimDaysListView(items, SimDaysPaging.Next(page, rows.Count)));
    }
}

/// <summary>The stored days of one run, a page at a time (SR-04): narrow columns, never the record text. Read only.</summary>
internal sealed class ListSimDayDaysHandler(ISimDaysStore store) : IQueryHandler<ListSimDayDaysQuery, Result<SimDayListView>>
{
    public async ValueTask<Result<SimDayListView>> HandleAsync(ListSimDayDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (SimDaysPaging.Read(query.Limit, query.Cursor) is not { } page)
        {
            return Result.Failure<SimDayListView>(SimDays.Invalid(SimDaysPaging.Message));
        }

        if (query.Sort is not (null or "asc" or "desc"))
        {
            return Result.Failure<SimDayListView>(SimDays.Invalid("sort is asc or desc"));
        }

        if (await store.GetAsync(query.Id, cancellationToken) is null)
        {
            return Result.Failure<SimDayListView>(SimDays.NotFound);
        }

        var rows = await store.ListDaysAsync(query.Id, query.Sort == "desc", page.Offset, page.Limit + 1, cancellationToken);
        var items = rows.Take(page.Limit).Select(r => new SimDayListItemView(r.Session, r.EquityClose, r.Trades, r.Picks, r.SeenHoldOut)).ToList();
        return Result.Success(new SimDayListView(items, SimDaysPaging.Next(page, rows.Count)));
    }
}

/// <summary>The simulation's defaults, so the screen does not hard-code them (SR-04). Read only.</summary>
internal sealed class GetSimDaysSettingsHandler(SimDaysOptions options) : IQueryHandler<GetSimDaysSettingsQuery, SimDaysSettingsView>
{
    public ValueTask<SimDaysSettingsView> HandleAsync(GetSimDaysSettingsQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new SimDaysSettingsView(options.DefaultCapital, options.DefaultSpeedSecondsPerMinute, options.AutoContinue));
}

/// <summary>The offset paging of the simulation lists: <c>limit</c> 1 to <see cref="MaxLimit"/> (default <see cref="DefaultLimit"/>), the cursor <c>o&lt;offset&gt;</c> the previous page gave.</summary>
internal static class SimDaysPaging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
    public const string Message = "limit is a whole number from 1 to 100 and the cursor one this endpoint returned";

    public static (int Limit, int Offset)? Read(string? limit, string? cursor)
    {
        var size = DefaultLimit;
        if (!string.IsNullOrEmpty(limit) && (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > MaxLimit))
        {
            return null;
        }

        var offset = 0;
        if (cursor is not null && (cursor.Length is < 2 or > 7 || cursor[0] != 'o' || !int.TryParse(cursor.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out offset)))
        {
            return null;
        }

        return (size, offset);
    }

    public static string? Next((int Limit, int Offset) page, int read) => read > page.Limit ? "o" + (page.Offset + page.Limit).ToString(CultureInfo.InvariantCulture) : null;
}
