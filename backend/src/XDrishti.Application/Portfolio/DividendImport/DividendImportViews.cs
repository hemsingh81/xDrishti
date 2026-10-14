using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>Turns the plans and the stored batches into the shapes of the API.</summary>
internal static class DividendImportViews
{
    public const string Format = "dividends";

    public static DividendPreviewReport Report(string fileName, DividendPlan plan)
    {
        var toAdd = plan.Rows.Where(r => DividendStatuses.IsToAdd(r.Status)).ToList();
        var byYear = toAdd.GroupBy(r => r.Row.PayDate.Year).OrderBy(g => g.Key).Select(g => new DividendYearTotal(g.Key, g.Count(), g.Sum(r => r.Row.Amount))).ToList();
        var before = plan.FirstLotDate is { } first ? toAdd.Where(r => r.Row.PayDate < first).ToList() : [];
        var file = plan.File;
        return new DividendPreviewReport(
            Format,
            new DividendFileSummary(
                fileName,
                file.Rows,
                file.Parsed,
                file.ParsedAmount,
                file.Repeated,
                file.Items.Count,
                file.Repeats,
                file.Bad,
                file.BadTotal,
                plan.Count(DividendStatus.Imported),
                plan.Count(DividendStatus.PossibleDuplicate),
                toAdd.Count,
                plan.Count(DividendStatus.NeedsMatch),
                plan.Count(DividendStatus.Ambiguous)),
            [.. plan.Rows.Select(Item)],
            new DividendTotals(toAdd.Count, toAdd.Sum(r => r.Row.Amount), byYear),
            new DividendBeforeFirstLot(before.Count, before.Sum(r => r.Row.Amount), plan.FirstLotDate),
            plan.Digest);
    }

    public static DividendPreviewItem Item(PlannedDividend planned) => new(
        planned.Row.Line,
        planned.Row.PayDate,
        planned.Row.Name,
        planned.Row.NameKey,
        planned.Row.PerShare,
        planned.Row.Quantity,
        planned.Row.Amount,
        DividendStatuses.Name(planned.Status),
        Match(planned.Match),
        planned.Warnings);

    public static DividendMatchView Match(DividendMatch match) => new(
        DividendStatuses.Name(match.Kind),
        match.HoldingId,
        match.InstrumentId,
        match.Holding?.TradingSymbol ?? match.Share?.Symbol,
        match.Holding?.Isin ?? match.Share?.Isin,
        match.Holding?.Name ?? match.Share?.Name,
        [.. match.Candidates.Select(c => new DividendCandidate(c.Id, c.TradingSymbol, c.Name))]);

    public static DividendBatchView Batch(DividendBatchRow row, string accountName) => new(
        row.Batch.Id,
        row.Batch.AccountId,
        accountName,
        row.Batch.FileName,
        row.Batch.Sha256,
        row.Batch.Parsed,
        row.Batch.Imported,
        row.Batch.Duplicates,
        row.Batch.Bad,
        row.Batch.Amount,
        row.Batch.Status.ToString().ToLowerInvariant(),
        row.ActiveDividends,
        row.Batch.CreatedBy,
        row.Batch.CreatedAt,
        row.Batch.UndoneAt);
}
