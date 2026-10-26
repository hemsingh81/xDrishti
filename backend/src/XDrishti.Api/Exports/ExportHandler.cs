using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.SimExport.Csv;

namespace XDrishti.Api.Exports;

/// <summary>
/// Makes the file of one export (spec P8-20, ADR 0131). It asks the capability gate itself (the second layer after the endpoint), checks the kind and the format against their whitelists, turns the query with the very whitelist of the report endpoints and calls the very handler of the
/// screen: there is no second implementation of a number. Rows are streamed into one buffer; the row and byte caps refuse (<c>exports.too_large</c>), never cut. The bytes depend only on the stored runs and the query: no clock, no random value, no machine fact is inside.
/// The only clock use is the date of the file NAME. Nothing is written to a disk, sent or kept.
/// </summary>
internal sealed class ExportHandler(
    ICapabilityGate gate,
    IQueryHandler<GetReportGroupsQuery, Result<GroupsView>> groups,
    IQueryHandler<GetReportPivotQuery, Result<PivotView>> pivot,
    IQueryHandler<GetReportWhatWorksQuery, Result<WhatWorksView>> whatWorks,
    IQueryHandler<GetReportEquityQuery, Result<EquityView>> equity,
    IQueryHandler<PageReportTradesQuery, Result<TradesView>> trades,
    TimeProvider clock,
    IOptions<ExportOptions> options) : ICommandHandler<ExportCommand, ExportFile>
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    public async ValueTask<Result<ExportFile>> HandleAsync(ExportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var open = await gate.RequireAsync(Domain.Capabilities.Capabilities.Exports, cancellationToken);
        if (open.IsFailure)
        {
            return Result.Failure<ExportFile>(open.Error);
        }

        if (!ExportKinds.All.Contains(command.Kind, StringComparer.Ordinal))
        {
            return Result.Failure<ExportFile>(ExportErrors.KindUnknown());
        }

        if (command.Format is not null && !string.Equals(command.Format, "csv", StringComparison.Ordinal))
        {
            return Result.Failure<ExportFile>(ExportErrors.FormatUnknown());
        }

        var input = command.Query with { Limit = null, Cursor = null };
        var parsed = ReportQuery.Parse(input);
        if (parsed.IsFailure)
        {
            return Result.Failure<ExportFile>(parsed.Error);
        }

        var limits = new Limits(options.Value.MaxCsvRows, options.Value.MaxCsvBytes);
        var body = new ArrayBufferWriter<byte>();
        var built = command.Kind switch
        {
            ExportKinds.Groups => await GroupsAsync(parsed.Value, body, limits, cancellationToken),
            ExportKinds.Pivot => await PivotAsync(parsed.Value, body, limits, cancellationToken),
            ExportKinds.WhatWorks => await WhatWorksAsync(parsed.Value, body, limits, cancellationToken),
            ExportKinds.Equity => await EquityAsync(parsed.Value, body, limits, cancellationToken),
            _ => await TradesAsync(parsed.Value, body, limits, cancellationToken),
        };
        if (built.IsFailure)
        {
            return Result.Failure<ExportFile>(built.Error);
        }

        return Result.Success(Finish(command.Kind, built.Value.Evidence, built.Value.Rows, body, FilterCount(input)));
    }

    private static int FilterCount(ReportQueryInput i) =>
        new[] { i.Runs, i.Basis, i.Lens, i.Strategy, i.Timeframe, i.Side, i.Style, i.Exit, i.Basket, i.Symbol, i.From, i.To, i.Metric }.Count(v => !string.IsNullOrWhiteSpace(v));

    private ExportFile Finish(string kind, List<string> evidence, int rows, ArrayBufferWriter<byte> body, int filters)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(body.WrittenSpan));
        var file = new ArrayBufferWriter<byte>(body.WrittenCount + 4096);
        evidence.Add("complete: every row of the table is in this file; no row was left out and nothing was cut");
        evidence.Add(string.Create(CultureInfo.InvariantCulture, $"table-rows: {rows}"));
        evidence.Add("table-sha256: " + sha + " (of the header and the data rows below; without these comment lines)");
        ReportCsvEvidence.WriteLines(file, evidence);
        file.Write(body.WrittenSpan);
        ReportCsvEvidence.WriteEnd(file, rows);
        var date = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(India).DateTime);
        var name = string.Create(CultureInfo.InvariantCulture, $"xd-{kind}-{date:yyyyMMdd}-{sha[..8]}.csv");
        return new ExportFile(file.WrittenSpan.ToArray(), name, kind, rows, filters);
    }

    private static Result<(List<string> Evidence, int Rows)> Over(long rows, Limits limits) =>
        Result.Failure<(List<string>, int)>(ExportErrors.TooLarge(rows, limits.Rows, limits.Bytes));

    private async ValueTask<Result<(List<string> Evidence, int Rows)>> GroupsAsync(ReportQuery query, ArrayBufferWriter<byte> sink, Limits limits, CancellationToken ct)
    {
        var result = await groups.HandleAsync(new GetReportGroupsQuery(query), ct);
        if (result.IsFailure)
        {
            return Result.Failure<(List<string>, int)>(result.Error);
        }

        var view = result.Value;
        if (view.Groups.Count > limits.Rows)
        {
            return Over(view.Groups.Count, limits);
        }

        var body = new ReportCsvBody(sink, ExportTables.GroupsColumns);
        var cells = new List<ExportCell>();
        foreach (var group in view.Groups)
        {
            cells.Clear();
            ExportTables.Group(cells, group);
            body.Add(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells));
            if (sink.WrittenCount > limits.Bytes)
            {
                return Over(view.Groups.Count, limits);
            }
        }

        return Result.Success((ExportTables.Evidence(ExportKinds.Groups, view.Evidence, [$"lens: {Join(view.Dimensions)}", $"groups are additive: {(view.NonAdditive ? "no; a trade can be in more than one group; so do not add the groups up" : "yes")}"]), body.Rows));
    }

    private async ValueTask<Result<(List<string> Evidence, int Rows)>> PivotAsync(ReportQuery query, ArrayBufferWriter<byte> sink, Limits limits, CancellationToken ct)
    {
        var result = await pivot.HandleAsync(new GetReportPivotQuery(query), ct);
        if (result.IsFailure)
        {
            return Result.Failure<(List<string>, int)>(result.Error);
        }

        var view = result.Value;
        if (view.Cells.Count > limits.Rows)
        {
            return Over(view.Cells.Count, limits);
        }

        var body = new ReportCsvBody(sink, ExportTables.PivotColumns(view.Metric));
        var cells = new List<ExportCell>();
        foreach (var cell in view.Cells)
        {
            cells.Clear();
            ExportTables.PivotRow(cells, cell);
            body.Add(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells));
            if (sink.WrittenCount > limits.Bytes)
            {
                return Over(view.Cells.Count, limits);
            }
        }

        return Result.Success((ExportTables.Evidence(ExportKinds.Pivot, view.Evidence, [$"rows are {view.RowDimension}; columns are {view.ColumnDimension}; the figure is {view.Metric}", $"cells are additive: {(view.NonAdditive ? "no; a trade can be in more than one cell" : "yes")}"]), body.Rows));
    }

    private async ValueTask<Result<(List<string> Evidence, int Rows)>> WhatWorksAsync(ReportQuery query, ArrayBufferWriter<byte> sink, Limits limits, CancellationToken ct)
    {
        var result = await whatWorks.HandleAsync(new GetReportWhatWorksQuery(query), ct);
        if (result.IsFailure)
        {
            return Result.Failure<(List<string>, int)>(result.Error);
        }

        var view = result.Value;
        var count = view.Ranked.Count + view.NotRanked.Count;
        if (count > limits.Rows)
        {
            return Over(count, limits);
        }

        var body = new ReportCsvBody(sink, ExportTables.WhatWorksColumns);
        var cells = new List<ExportCell>();
        foreach (var ranked in view.Ranked)
        {
            cells.Clear();
            ExportTables.WhatWorksRow(cells, ranked.Rank, ranked.Group, ranked.LowerBoundPositive, null);
            body.Add(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells));
        }

        foreach (var other in view.NotRanked)
        {
            cells.Clear();
            ExportTables.WhatWorksRow(cells, null, other.Group, null, other.Reason);
            body.Add(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells));
        }

        if (sink.WrittenCount > limits.Bytes)
        {
            return Over(count, limits);
        }

        var notes = new List<string>
        {
            $"headline: {view.Headline}",
            $"selection effect: {view.SelectionNote}",
            string.Create(CultureInfo.InvariantCulture, $"cells looked at {view.CellsLookedAt}; comparable {view.CellsComparable}; base minimum {view.MinTrades} trades; required to rank {view.RequiredTrades} trades; largest cell {view.LargestN} trades"),
            string.Create(CultureInfo.InvariantCulture, $"positive lower bounds {view.PositiveLowerBound}; about {view.ExpectedChancePositives:0.##} would be positive by chance alone"),
            $"cells are made of: {Join(view.Dimensions)}",
        };
        notes.AddRange(view.UnavailableDimensions.Select(d => "dimension left out: " + d));
        return Result.Success((ExportTables.Evidence(ExportKinds.WhatWorks, view.Evidence, notes), body.Rows));
    }

    private async ValueTask<Result<(List<string> Evidence, int Rows)>> EquityAsync(ReportQuery query, ArrayBufferWriter<byte> sink, Limits limits, CancellationToken ct)
    {
        var result = await equity.HandleAsync(new GetReportEquityQuery(query), ct);
        if (result.IsFailure)
        {
            return Result.Failure<(List<string>, int)>(result.Error);
        }

        var view = result.Value;
        long count = view.Series.Sum(s => (long)Math.Max(1, s.Points.Count));
        if (count > limits.Rows)
        {
            return Over(count, limits);
        }

        var body = new ReportCsvBody(sink, ExportTables.EquityColumns);
        var cells = new List<ExportCell>();
        foreach (var series in view.Series)
        {
            foreach (var point in series.Points.Count == 0 ? new EquityPointView?[] { null } : series.Points.Select(p => (EquityPointView?)p))
            {
                cells.Clear();
                ExportTables.EquityRow(cells, series, point);
                body.Add(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells));
            }

            if (sink.WrittenCount > limits.Bytes)
            {
                return Over(count, limits);
            }
        }

        return Result.Success((ExportTables.Evidence(ExportKinds.Equity, view.Evidence, [$"lens: {Join(view.Dimensions)}", $"curves are additive: {(view.NonAdditive ? "no; a trade can be in more than one curve" : "yes")}", "a group below the minimum sample has no curve: one row with the state and the reason and no number"]), body.Rows));
    }

    private async ValueTask<Result<(List<string> Evidence, int Rows)>> TradesAsync(ReportQuery query, ArrayBufferWriter<byte> sink, Limits limits, CancellationToken ct)
    {
        var body = new ReportCsvBody(sink, ExportTables.TradesColumns);
        var cells = new List<ExportCell>();
        ReportEvidenceView? evidence = null;
        string? cursor = null;
        var total = -1;
        do
        {
            var page = await trades.HandleAsync(new PageReportTradesQuery(query with { Page = new PageRequest(PageRequest.MaxSize, cursor) }), ct);
            if (page.IsFailure)
            {
                return Result.Failure<(List<string>, int)>(page.Error);
            }

            var view = page.Value;
            evidence ??= view.Evidence;
            if (total < 0)
            {
                total = view.Total;
            }
            else if (view.Total != total)
            {
                return Result.Failure<(List<string>, int)>(ExportErrors.Changed());
            }

            if (view.Total > limits.Rows)
            {
                return Over(view.Total, limits);
            }

            foreach (var trade in view.Items)
            {
                cells.Clear();
                ExportTables.TradeRow(cells, trade);
                body.Add(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells));
            }

            if (sink.WrittenCount > limits.Bytes)
            {
                return Over(view.Total, limits);
            }

            cursor = view.NextCursor;
        }
        while (cursor is not null);

        if (body.Rows != total)
        {
            return Result.Failure<(List<string>, int)>(ExportErrors.Changed());
        }

        return Result.Success((ExportTables.Evidence(ExportKinds.Trades, evidence!, ["one row for each closed trade the report counts; in the order of the exits; R is on the basis above"]), body.Rows));
    }

    private static string Join(IEnumerable<string> values) => string.Join(" / ", values);

    private readonly record struct Limits(int Rows, int Bytes);
}
