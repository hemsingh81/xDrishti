using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Lets a test act "meanwhile": the next read of the applied corporate actions, which the trade use cases make inside their unit of work after they have read the holdings and before they save, first runs <see cref="Arm"/>
/// (for example a sync or a quote changing the holding behind the use case's back). Armed by one test at a time (the suite's tests run one after the other).
/// </summary>
public sealed class TradeHooks
{
    private Func<Task>? _once;

    public void Arm(Func<Task> action) => _once = action;

    public bool Armed => _once is not null;

    public async Task RunAsync()
    {
        var action = Interlocked.Exchange(ref _once, null);
        if (action is not null)
        {
            await action();
        }
    }
}

internal sealed class HookedTradeReferenceData(ITradeReferenceData inner, TradeHooks hooks) : ITradeReferenceData
{
    public ValueTask<IReadOnlyList<TradeInstrument>> FindInstrumentsAsync(IReadOnlyCollection<string> isins, IReadOnlyCollection<string> symbols, CancellationToken cancellationToken) =>
        inner.FindInstrumentsAsync(isins, symbols, cancellationToken);

    public async ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<AppliedAction>>> ListAppliedActionsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        await hooks.RunAsync();
        return await inner.ListAppliedActionsAsync(instrumentIds, cancellationToken);
    }
}
