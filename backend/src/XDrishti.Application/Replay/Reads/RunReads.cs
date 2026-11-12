using System.Text.Json;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Reads;

/// <summary>The runs, newest first (SR-01 part C): id, created, range, mode, state, completeness, final equity, max drawdown, trial number.</summary>
public sealed record ListReplayRunsQuery(int Skip, int Take) : IQuery<Result<IReadOnlyList<ReplayRunListItem>>>;

internal sealed class ListReplayRunsHandler(IReplayReads reads) : IQueryHandler<ListReplayRunsQuery, Result<IReadOnlyList<ReplayRunListItem>>>
{
    public async ValueTask<Result<IReadOnlyList<ReplayRunListItem>>> HandleAsync(ListReplayRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Skip < 0 || query.Take is < 1 or > 200)
        {
            return Result.Failure<IReadOnlyList<ReplayRunListItem>>(Error.Validation(ReplayDriver.InvalidCode, "skip is 0 or more and take is 1 to 200"));
        }

        return Result.Success<IReadOnlyList<ReplayRunListItem>>([.. (await reads.ListRunsAsync(query.Skip, query.Take, cancellationToken)).Select(ReplayReadSupport.ListItem)]); // the last equity comes with the run row: one query
    }
}

/// <summary>The summary of one run (AC 14): readable while it runs.</summary>
public sealed record GetReplayRunQuery(Guid RunId) : IQuery<Result<ReplayRunView>>;

internal sealed class GetReplayRunHandler(IReplayReads reads) : IQueryHandler<GetReplayRunQuery, Result<ReplayRunView>>
{
    public async ValueTask<Result<ReplayRunView>> HandleAsync(GetReplayRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await reads.GetRunAsync(query.RunId, cancellationToken) is not { } run)
        {
            return Result.Failure<ReplayRunView>(ReplayErrors.NotFound);
        }

        var p = ReplayReadSupport.ParamsOf(run);
        var days = await reads.EquityAsync(run.Id, null, null, cancellationToken); // no session record is loaded here
        var trades = await reads.TradesAsync(run.Id, cancellationToken);
        var labels = await reads.LabelsAsync(run.Id, cancellationToken);
        var summary = await reads.SummaryAsync(run.Id, cancellationToken); // PaperLedger over the stored trades: equity, drawdown (AC 7)
        var figures = p is null ? null : ReplayStats.Figures(p.Capital, run.From, days, summary, trades);
        return Result.Success(new ReplayRunView(
            ReplayReadSupport.Labels(p, labels),
            run.Id,
            run.PairId,
            run.CreatedAt,
            run.FinishedAt,
            run.From,
            run.To,
            run.Mode,
            run.State,
            run.FailReason,
            ReplayReadSupport.Completeness(run),
            ReplayReadSupport.CostsLabel(p),
            ReplayReadSupport.Basis(p),
            run.TrialNumber,
            run.InputHash,
            run.ResultHash,
            JsonDocument.Parse(run.ParamsJson).RootElement.Clone(),
            JsonDocument.Parse(run.SettingsJson).RootElement.Clone(),
            figures,
            ReplayStats.Contribution(trades)));
    }
}

/// <summary>The error every replay read answers for a run that is not there.</summary>
internal static class ReplayErrors
{
    public static Error NotFound { get; } = Error.NotFound("replay.not_found", "No such replay run.");
}
