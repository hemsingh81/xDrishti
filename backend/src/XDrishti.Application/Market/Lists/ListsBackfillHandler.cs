using System.Globalization;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Market.Lists;

/// <summary><c>xd data lists-backfill --from --to [--dry-run]</c>: loads the dated ban files of the weekdays in the range that are not stored yet. A dry run fetches and parses but stores nothing.</summary>
public sealed record ListsBackfillCommand(DateOnly From, DateOnly To, bool DryRun = false) : ICommand;

public sealed record ListsBackfillSummary(int Requested, int Stored, int AlreadyStored, int Absent, int Rejected, int Failed, bool Capped, bool DryRun, string? Stopped = null)
{
    public string Text => string.Create(CultureInfo.InvariantCulture, $"{(DryRun ? "dry run: " : string.Empty)}requested {Requested}, stored {Stored}, already stored {AlreadyStored}, no file {Absent}, rejected {Rejected}, failed {Failed}{(Capped ? ", stopped at the per-run cap (run again)" : string.Empty)}{(Stopped is null ? string.Empty : ", stopped: " + Stopped)}");
}

internal sealed class ListsBackfillHandler(IListHistorySettings settings, IListArchiveSource source, IListDayStore store, IBasketSourceStore instruments, TimeProvider clock) : ICommandHandler<ListsBackfillCommand, ListsBackfillSummary>
{
    public const int MaxRangeDays = 3_700;

    public async ValueTask<Result<ListsBackfillSummary>> HandleAsync(ListsBackfillCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!settings.Enabled)
        {
            return Result.Failure<ListsBackfillSummary>(ListHistoryErrors.Disabled());
        }

        var today = IndiaTime.Today(clock.GetUtcNow());
        if (command.From > command.To || command.To >= today || command.To.DayNumber - command.From.DayNumber > MaxRangeDays)
        {
            return Result.Failure<ListsBackfillSummary>(ListHistoryErrors.BadRange("From must not be after to, to must be before today, and the range at most about ten years."));
        }

        var covered = await store.CoveredAsync(ListBaskets.Ban.Id, command.From, command.To, cancellationToken);
        int requested = 0, stored = 0, already = 0, absent = 0, rejected = 0, failed = 0;
        var capped = false;
        string? stopped = null;
        for (var day = command.From; day <= command.To; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            if (covered.Contains(day))
            {
                already++;
                continue;
            }

            if (requested >= settings.MaxPerDay)
            {
                capped = true;
                break;
            }

            if (requested > 0)
            {
                await Task.Delay(settings.Delay, clock, cancellationToken);
            }

            requested++;
            var fetch = await source.FetchBanAsync(day, cancellationToken);
            switch (fetch.Status)
            {
                case ListFetchStatus.NotAllowed:
                    return Result.Failure<ListsBackfillSummary>(ListHistoryErrors.NotAllowed(fetch.Message ?? "The endpoint is not allowed."));
                case ListFetchStatus.Absent:
                    absent++;
                    continue;
                case ListFetchStatus.Failed:
                    failed++;
                    stopped = "fetch_failed: " + (fetch.Message ?? "the request failed"); // a refusal or timeout is not worked around: stop, report, run again later
                    break;
            }

            if (stopped is not null)
            {
                break;
            }

            var file = BanListParser.Parse(System.Text.Encoding.UTF8.GetString(fetch.Content!));
            if (!file.Ok || file.TradeDate != day)
            {
                rejected++; // fail closed: a bad file, or one for another date, stores nothing
                continue;
            }

            var ids = (await instruments.FindInstrumentsAsync([], file.Symbols, cancellationToken))
                .GroupBy(i => i.Symbol, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(c => c.RetiredAt is not null).First().Id).ToList();
            if (ids.Count < file.Symbols.Count)
            {
                rejected++; // a banned symbol the catalogue cannot match would read "not banned": the day is not stored
                continue;
            }

            if (command.DryRun)
            {
                continue; // fetched, parsed and matched against the catalogue (read-only); nothing stored
            }

            if (await store.InsertAsync(ListBaskets.Ban.Id, day, ids, ListArchive.SourceArchive, cancellationToken))
            {
                stored++;
            }
            else
            {
                already++;
            }
        }

        return Result.Success(new ListsBackfillSummary(requested, stored, already, absent, rejected, failed, capped, command.DryRun, stopped));
    }
}
