using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Market.Lists;

/// <summary><c>xd data lists-probe --dates</c>: asks NSE for at most five dated ban files, stores nothing and reports the status and the parse result.</summary>
public sealed record ListsProbeCommand(IReadOnlyList<DateOnly> Dates) : ICommand;

public sealed record ListsProbeReport(IReadOnlyList<string> Lines);

internal sealed class ListsProbeHandler(IListHistorySettings settings, IListArchiveSource source, TimeProvider clock) : ICommandHandler<ListsProbeCommand, ListsProbeReport>
{
    public const int MaxDates = 5;

    public async ValueTask<Result<ListsProbeReport>> HandleAsync(ListsProbeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!settings.Enabled)
        {
            return Result.Failure<ListsProbeReport>(ListHistoryErrors.Disabled());
        }

        var dates = command.Dates.Distinct().ToList();
        if (dates.Count is 0 or > MaxDates)
        {
            return Result.Failure<ListsProbeReport>(ListHistoryErrors.BadRange($"Give 1 to {MaxDates} dates."));
        }

        var lines = new List<string>();
        var first = true;
        foreach (var date in dates)
        {
            if (!first)
            {
                await Task.Delay(settings.Delay, clock, cancellationToken);
            }

            first = false;
            var fetch = await source.FetchBanAsync(date, cancellationToken);
            switch (fetch.Status)
            {
                case ListFetchStatus.NotAllowed:
                    return Result.Failure<ListsProbeReport>(ListHistoryErrors.NotAllowed(fetch.Message ?? "The endpoint is not allowed."));
                case ListFetchStatus.Ok:
                    var file = BanListParser.Parse(System.Text.Encoding.UTF8.GetString(fetch.Content!));
                    lines.Add(file.Ok
                        ? $"{date:yyyy-MM-dd}: ok, trade date {file.TradeDate:yyyy-MM-dd}{(file.TradeDate == date ? string.Empty : " (DIFFERS from the requested date)")}, {file.Symbols.Count} symbols"
                        : $"{date:yyyy-MM-dd}: file refused by the parser ({file.Reason})");
                    break;
                case ListFetchStatus.Absent:
                    lines.Add($"{date:yyyy-MM-dd}: no such file at NSE");
                    break;
                default:
                    lines.Add($"{date:yyyy-MM-dd}: fetch failed ({fetch.Message})");
                    break;
            }
        }

        lines.Add("nothing was stored; ASM/GSM has no dated archive known here and stays forward-only");
        return Result.Success(new ListsProbeReport(lines));
    }
}
