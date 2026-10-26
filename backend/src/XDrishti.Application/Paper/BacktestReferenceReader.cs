using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;

namespace XDrishti.Application.Paper;

/// <summary>The backtest trades of one cell before the epoch started, with how they were costed.</summary>
/// <param name="Costed">Whether the run charged the trades (R is net).</param>
/// <param name="SheetId">The charge sheet the run names, or null.</param>
/// <param name="Results">R of each trade with an exit date before the start.</param>
internal sealed record ReferenceCell(bool Costed, string? SheetId, IReadOnlyList<double> Results);

/// <summary>What the reference read found: a reason it is not there, or the cells.</summary>
/// <param name="Problem">Why there is no reference (<c>unavailable</c>), or null.</param>
/// <param name="SnapshotKey">The snapshot in force at the start.</param>
/// <param name="AsOf">Its as-of date.</param>
/// <param name="RunKeys">The runs read.</param>
/// <param name="Cells">The cells by key.</param>
internal sealed record ReferenceRead(string? Problem, string? SnapshotKey, DateOnly? AsOf, IReadOnlyList<string> RunKeys, IReadOnlyDictionary<string, ReferenceCell> Cells)
{
    public static ReferenceRead Unavailable(string problem, string? snapshot = null, DateOnly? asOf = null) =>
        new(problem, snapshot, asOf, [], new Dictionary<string, ReferenceCell>(StringComparer.Ordinal));
}

/// <summary>
/// Reads the point-in-time reference of spec P10-04 (criterion 2): the newest cell snapshot whose as-of date is before the epoch's start, its source runs through <see cref="CellSourceReader"/>, the closed trades with an exit date before
/// the start, unweighted. Read only.
/// </summary>
internal sealed class BacktestReferenceReader(ICellStatsStore stats, IRunStore runs)
{
    public async ValueTask<ReferenceRead> ReadAsync(DateOnly start, CancellationToken cancellationToken)
    {
        var rows = await stats.ListLatestBeforeAsync(start, null, null, cancellationToken);
        if (rows.Count == 0)
        {
            return ReferenceRead.Unavailable("no cell statistics snapshot exists from before the epoch's start");
        }

        var first = rows[0];
        var sources = ParseSources(first.Sources);
        if (sources is null)
        {
            return ReferenceRead.Unavailable("the snapshot's sources cannot be read", first.SnapshotKey, first.Row.AsOf);
        }

        var read = await CellSourceReader.ReadAsync(runs, sources, cancellationToken);
        if (read.IsFailure)
        {
            return ReferenceRead.Unavailable("the snapshot's source runs cannot be read: " + read.Error.Code, first.SnapshotKey, first.Row.AsOf);
        }

        var sheets = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (await runs.FindAsync(source.RunKey, cancellationToken) is { } header)
            {
                sheets[header.StrategyId] = header.CostsSheetId;
            }
        }

        var cells = new Dictionary<string, ReferenceCell>(StringComparer.Ordinal);
        foreach (var cell in read.Value.Cells)
        {
            var results = cell.Trades.Where(t => t.ExitDate < start).Select(t => t.R).ToList();
            cells[cell.Cell.Key] = new ReferenceCell(cell.Costed, sheets.GetValueOrDefault(cell.Cell.Strategy), results);
        }

        return new ReferenceRead(null, first.SnapshotKey, first.Row.AsOf, [.. sources.Select(s => s.RunKey).Order(StringComparer.Ordinal)], cells);
    }

    private static List<CellSource>? ParseSources(string text)
    {
        var result = new List<CellSource>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(':', 3);
            if (pieces.Length != 3)
            {
                return null;
            }

            result.Add(new CellSource(pieces[0], pieces[1], pieces[2]));
        }

        return result.Count == 0 ? null : result;
    }
}
