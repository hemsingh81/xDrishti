using System.Security.Cryptography;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Eod;

/// <summary>Loads the daily files of the weekdays in [From, To] that are not in the ledger yet (CF-04 Part A). Both default from the settings: <c>Years</c> back to yesterday.</summary>
public sealed record LoadEodCommand(DateOnly? From = null, DateOnly? To = null) : ICommand;

/// <summary>What one run did. <see cref="Stopped"/> is null when the range was finished; otherwise the closed reason the run stopped early (<c>max_per_day</c>, <c>fetch_failed</c>).</summary>
public sealed record EodLoadSummary(int DaysLoaded, int DaysAbsent, int DaysAlreadyDone, int Rows, int RowsRejected, int DeliveryRejected, int Requests, string? Stopped)
{
    public string Text => $"loaded {DaysLoaded} days ({Rows} rows), {DaysAbsent} without a file, {DaysAlreadyDone} already done, {RowsRejected + DeliveryRejected} rows rejected, {Requests} requests" + (Stopped is null ? string.Empty : $", stopped: {Stopped}");
}

/// <summary>
/// One file per trading day, throttled by <see cref="IEodSettings.Delay"/>, newest first, resumable (the ledger skips days done), idempotent (a day is written again by upsert). With the setting off nothing is requested at all (<c>history.disabled</c>).
/// An unknown header, a wrong date or a refused endpoint stops the run and writes nothing for that day; a network failure stops the run too (the next run continues from there). A day NSE has no file for is recorded as absent, not as an error.
/// </summary>
internal sealed class LoadEodHandler(IEodSettings settings, IEodSource source, IEodStore store, TimeProvider clock) : ICommandHandler<LoadEodCommand, EodLoadSummary>
{
    public async ValueTask<Result<EodLoadSummary>> HandleAsync(LoadEodCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!settings.Enabled)
        {
            return Result.Failure<EodLoadSummary>(EodErrors.Disabled());
        }

        var today = IndiaTime.Today(clock.GetUtcNow());
        var to = command.To ?? today.AddDays(-1);
        var from = command.From ?? today.AddYears(-settings.Years);
        var done = await store.ListDoneDatesAsync(from, to, cancellationToken);
        var days = Weekdays(from, to).Where(d => !done.Contains(d)).Reverse().ToList();
        var state = new State { AlreadyDone = Weekdays(from, to).Count(done.Contains) };
        foreach (var day in days)
        {
            if (state.Requests >= settings.MaxPerDay)
            {
                state.Stopped = "max_per_day";
                break;
            }

            var step = await LoadDayAsync(day, state, cancellationToken);
            if (step.IsFailure)
            {
                return Result.Failure<EodLoadSummary>(step.Error);
            }

            if (state.Stopped is not null)
            {
                break;
            }
        }

        return Result.Success(new EodLoadSummary(state.Loaded, state.Absent, state.AlreadyDone, state.Rows, state.Rejected, state.DeliveryRejected, state.Requests, state.Stopped));
    }

    internal static IEnumerable<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                yield return d;
            }
        }
    }

    private async ValueTask<Result> LoadDayAsync(DateOnly day, State state, CancellationToken cancellationToken)
    {
        var bhav = await FetchAsync(EodFileKind.Bhavcopy, day, state, cancellationToken);
        if (bhav.Status == EodFetchStatus.Absent)
        {
            await store.MarkAbsentAsync(day, cancellationToken);
            state.Absent++;
            return Result.Success();
        }

        if (Check(bhav, state) is { } refused)
        {
            return refused;
        }

        var parsed = await EodParser.ParseBhavcopyAsync(bhav.Content!, day, settings.Series, cancellationToken);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error);
        }

        var delivery = await FetchAsync(EodFileKind.Delivery, day, state, cancellationToken);
        if (delivery.Status != EodFetchStatus.Absent && Check(delivery, state) is { } refusedDelivery)
        {
            return refusedDelivery;
        }

        var rows = parsed.Value.Rows;
        string? deliverySha = null;
        if (delivery.Status == EodFetchStatus.Ok)
        {
            var figures = await EodParser.ParseDeliveryAsync(delivery.Content!, cancellationToken);
            if (figures.IsFailure)
            {
                return Result.Failure(figures.Error);
            }

            var merge = EodParser.Merge(rows, figures.Value);
            rows = merge.Rows;
            state.DeliveryRejected += merge.Rejected + figures.Value.Rejected;
            deliverySha = Hex(delivery.Content!);
        }

        await store.WriteDayAsync(day, rows, Hex(bhav.Content!), deliverySha, cancellationToken);
        state.Loaded++;
        state.Rows += rows.Count;
        state.Rejected += parsed.Value.Rejected;
        return Result.Success();
    }

    // A refused endpoint is a failure of the command; a network failure stops the run (state.Stopped) so the next run continues here.
    private static Result? Check(EodFetch fetch, State state)
    {
        switch (fetch.Status)
        {
            case EodFetchStatus.NotAllowed:
                return Result.Failure(EodErrors.NotAllowed(fetch.Message ?? "The endpoint is not allowed."));
            case EodFetchStatus.Failed:
                state.Stopped = "fetch_failed";
                return Result.Success();
            default:
                return null;
        }
    }

    private async ValueTask<EodFetch> FetchAsync(EodFileKind kind, DateOnly day, State state, CancellationToken cancellationToken)
    {
        if (state.Requests > 0)
        {
            await Task.Delay(settings.Delay, clock, cancellationToken);
        }

        state.Requests++;
        return await source.FetchAsync(kind, day, cancellationToken);
    }

    private static string Hex(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private sealed class State
    {
        public int Loaded { get; set; }

        public int Absent { get; set; }

        public int AlreadyDone { get; set; }

        public int Rows { get; set; }

        public int Rejected { get; set; }

        public int DeliveryRejected { get; set; }

        public int Requests { get; set; }

        public string? Stopped { get; set; }
    }
}
