using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Live;

/// <summary>The portfolio side of the live feed in memory: the instruments the open holdings hold and the batches written into them.</summary>
internal sealed class FakeLivePrices : ILivePrices
{
    public HashSet<Guid> Held { get; } = [];

    public List<IReadOnlyList<LivePrice>> Applied { get; } = [];

    public int Lists { get; private set; }

    /// <summary>Thrown by the next <see cref="ApplyAsync"/> (then cleared).</summary>
    public Exception? FailNext { get; set; }

    public ValueTask<IReadOnlySet<Guid>> ListHeldInstrumentsAsync(CancellationToken cancellationToken)
    {
        Lists++;
        return ValueTask.FromResult<IReadOnlySet<Guid>>(Held);
    }

    public List<HeldSecurity> Securities { get; } = [];

    public ValueTask<IReadOnlyList<HeldSecurity>> ListHeldSecuritiesAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<HeldSecurity>>([.. Securities]);

    public ValueTask<int> ApplyAsync(IReadOnlyList<LivePrice> prices, CancellationToken cancellationToken)
    {
        if (FailNext is { } failure)
        {
            FailNext = null;
            throw failure;
        }

        Applied.Add(prices);
        return ValueTask.FromResult(prices.Count);
    }
}

internal sealed class FakeLiveQuotes : ILiveQuotes
{
    public List<IReadOnlyList<LivePrice>> Upserts { get; } = [];

    public int Clears { get; private set; }

    public ValueTask<int> UpsertAsync(IReadOnlyList<LivePrice> prices, CancellationToken cancellationToken)
    {
        Upserts.Add(prices);
        return ValueTask.FromResult(prices.Count);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        Clears++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeLiveNotifier : ILiveNotifier
{
    public int Prices { get; private set; }

    public int ActiveSet { get; private set; }

    public ValueTask PricesChangedAsync(CancellationToken cancellationToken)
    {
        Prices++;
        return ValueTask.CompletedTask;
    }

    public ValueTask ActiveSetChangedAsync(CancellationToken cancellationToken)
    {
        ActiveSet++;
        return ValueTask.CompletedTask;
    }
}
