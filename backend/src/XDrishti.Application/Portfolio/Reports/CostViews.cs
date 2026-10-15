using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>Turns the stored cost batches into the shapes of the API.</summary>
internal static class CostViews
{
    public static CostBatchView Batch(CostBatchRow row, string accountName) => new(
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
        row.ActiveEntries,
        row.Batch.CreatedBy,
        row.Batch.CreatedAt,
        row.Batch.UndoneAt);
}
