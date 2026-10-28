using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>An in-memory <see cref="ILiveCaptureStore"/> with the real unique keys: the same trade, the same order revision and the same ticket are stored once.</summary>
internal sealed class InMemoryLiveStore : ILiveCaptureStore
{
    public List<LiveFill> Fills { get; } = [];
    public List<LiveOrder> Orders { get; } = [];
    public List<NewCaptureRun> Runs { get; } = [];
    public Dictionary<Guid, decimal> Charges { get; } = [];
    public List<StoredOutcome> Outcomes { get; } = [];

    public ValueTask<int> InsertFillsAsync(IReadOnlyList<LiveFill> fills, string source, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var f in fills.Where(f => !Fills.Any(x => x.AccountId == f.AccountId && x.BrokerTradeId == f.BrokerTradeId)))
        {
            Fills.Add(f);
            added++;
        }

        return ValueTask.FromResult(added);
    }

    public ValueTask<int> InsertOrdersAsync(IReadOnlyList<LiveOrder> orders, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var o in orders.Where(o => !Orders.Any(x => x.AccountId == o.AccountId && x.BrokerOrderId == o.BrokerOrderId && x.Status == o.Status && x.FilledQty == o.FilledQty)))
        {
            Orders.Add(o);
            added++;
        }

        return ValueTask.FromResult(added);
    }

    public ValueTask InsertRunAsync(NewCaptureRun run, CancellationToken cancellationToken)
    {
        Runs.Add(run);
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> InsertChargesAsync(IReadOnlyList<(Guid FillId, decimal Charges)> charges, CancellationToken cancellationToken)
    {
        foreach (var (id, amount) in charges)
        {
            Charges[id] = amount;
        }

        return ValueTask.FromResult(charges.Count);
    }

    public ValueTask<IReadOnlyList<LiveFill>> FillsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<LiveFill>>([.. Fills.Where(f => f.AccountId == accountId && f.Date == date)]);

    public ValueTask<IReadOnlyDictionary<Guid, decimal>> ChargesAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, decimal>>(Charges);

    public ValueTask<IReadOnlyList<LiveOrder>> LatestOrdersAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<LiveOrder>>([.. Orders.Where(o => o.AccountId == accountId && o.Date == date).GroupBy(o => o.BrokerOrderId).Select(g => g.Last())]);

    public ValueTask<IReadOnlyList<CaptureRun>> RunsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<CaptureRun>>([.. Runs.Where(r => r.AccountId == accountId && r.ForDate == date).Select(r => new CaptureRun(r.Kind, r.StartedAt, r.FinishedAt, r.Outcome))]);

    public ValueTask<IReadOnlyList<StoredCaptureRun>> RunDetailsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredCaptureRun>>([.. Runs.Where(r => r.AccountId == accountId && r.ForDate == date).Select(r => new StoredCaptureRun(r.Kind, r.StartedAt, r.FinishedAt, r.Outcome, r.FillsNew, r.OrdersNew))]);

    public ValueTask<int> InsertOutcomesAsync(IReadOnlyList<NewTicketOutcome> outcomes, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var o in outcomes.Where(o => !Outcomes.Any(x => x.ItemId == o.ItemId && x.AccountId == o.AccountId)))
        {
            Outcomes.Add(new StoredOutcome(Outcomes.Count + 1, o.ItemId, o.AccountId, o.ForDate, o.AllocatedQty, o.Outcome, DateTimeOffset.UnixEpoch));
            added++;
        }

        return ValueTask.FromResult(added);
    }

    public ValueTask<IReadOnlyList<StoredOutcome>> OutcomesOfDateAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredOutcome>>([.. Outcomes.Where(o => o.AccountId == accountId && o.ForDate == date)]);

    public ValueTask<StoredOutcomePage> ListOutcomesAsync(Guid? accountId, DateOnly from, DateOnly to, int skip, int take, CancellationToken cancellationToken)
    {
        var all = Outcomes.Where(o => (accountId is null || o.AccountId == accountId) && o.ForDate >= from && o.ForDate <= to).OrderByDescending(o => o.ForDate).ThenByDescending(o => o.Id).ToList();
        return ValueTask.FromResult(new StoredOutcomePage([.. all.Skip(skip).Take(take)], all.Count));
    }
}
