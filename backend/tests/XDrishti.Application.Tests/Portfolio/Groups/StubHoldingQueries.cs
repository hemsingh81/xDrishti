using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Groups;

/// <summary>Holdings read models served as they are, with the account, bucket, status and text filters applied like the real query; counts the reads and keeps the last filter.</summary>
internal sealed class StubHoldingQueries(params HoldingData[] holdings) : IHoldingQueries
{
    public List<HoldingData> Holdings { get; } = [.. holdings];

    public int Reads { get; private set; }

    public HoldingFilter? LastFilter { get; private set; }

    public ValueTask<IReadOnlyList<HoldingData>> ListAsync(HoldingFilter filter, CancellationToken cancellationToken)
    {
        Reads++;
        LastFilter = filter;
        var rows = Holdings.AsEnumerable();
        if (filter.AccountId is { } account)
        {
            rows = rows.Where(h => h.AccountId == account);
        }

        if (filter.BucketId is { } bucket)
        {
            rows = rows.Where(h => h.BucketId == bucket);
        }

        if (filter.Status is { } status)
        {
            rows = rows.Where(h => h.Status == status);
        }

        if (filter.Text is { } text)
        {
            rows = rows.Where(h => h.TradingSymbol.Contains(text, StringComparison.OrdinalIgnoreCase) || h.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        return ValueTask.FromResult<IReadOnlyList<HoldingData>>([.. rows]);
    }

    public ValueTask<HoldingData?> GetAsync(Guid holdingId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Holdings.FirstOrDefault(h => h.Id == holdingId));
}
