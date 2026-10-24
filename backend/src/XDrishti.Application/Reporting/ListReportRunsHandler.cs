using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>Lists the runs a report may use (P8-06). Net figures are <c>unavailable</c> with the reason until the owner's stored confirmation of the charge sheet exists; the confirmation is read from storage here, never from the request.</summary>
internal sealed class ListReportRunsHandler(IReportTradeSource source, IChargeSheetConfirmationSource confirmations) : IQueryHandler<ListReportRunsQuery, Result<RunsView>>
{
    public async ValueTask<Result<RunsView>> HandleAsync(ListReportRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var runs = await source.ListRunsAsync(cancellationToken);
        var confirmation = await confirmations.FindAsync(cancellationToken);
        var (chosen, notes) = RunSelection.Default(runs);
        var inDefault = chosen.Select(r => r.RunKey).ToHashSet(StringComparer.Ordinal);
        var items = runs
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.RunKey, StringComparer.Ordinal)
            .Select(r => new RunListItemView(
                r.RunKey,
                r.StrategyId,
                r.UniverseKind,
                r.UniverseLabel,
                r.WindowStart,
                r.WindowEnd,
                r.Costed,
                r.CostsSheetId,
                r.Style,
                r.StyleSource,
                r.Exit,
                r.ExitSource,
                r.ClosedTrades,
                r.OpenAtEnd,
                confirmation is not null && r.Costed && string.Equals(r.CostsSheetId, confirmation.SheetId, StringComparison.Ordinal) && string.Equals(r.CostsRatesSha256, confirmation.RatesSha256, StringComparison.Ordinal),
                inDefault.Contains(r.RunKey),
                notes.GetValueOrDefault(r.RunKey),
                r.CreatedAt))
            .ToList();
        var net = confirmation is null
            ? new NetBasisView("unavailable", "The owner has not confirmed the stored charge sheet as their own (ISS-25), so every report is gross and no net figure is shown.", null)
            : new NetBasisView("value", null, confirmation.Text);
        return Result.Success(new RunsView(items, net, RunSelection.RuleText));
    }
}
