using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Quotes;

/// <summary>The portfolio side of the quote poll in memory: the securities the open holdings hold and what was written into them.</summary>
internal sealed class FakeHoldingQuotes : IHoldingQuotes
{
    public QuoteTargets Targets { get; set; } = new(0, []);

    public List<(IReadOnlyList<HoldingQuote> Quotes, DateTimeOffset At)> Applied { get; } = [];

    public int Lists { get; private set; }

    public ValueTask<QuoteTargets> ListTargetsAsync(CancellationToken cancellationToken)
    {
        Lists++;
        return ValueTask.FromResult(Targets);
    }

    public ValueTask<int> ApplyAsync(IReadOnlyList<HoldingQuote> quotes, DateTimeOffset at, CancellationToken cancellationToken)
    {
        Applied.Add((quotes, at));
        return ValueTask.FromResult(quotes.Count);
    }

    /// <summary>Open securities of which <paramref name="nse"/> are on the NSE, with security ids "1001", "1002" …</summary>
    public static QuoteTargets Held(int open, int nse) =>
        new(open, [.. Enumerable.Range(1, nse).Select(i => new QuoteTarget(Guid.Parse($"00000000-0000-7000-8000-{i:D12}"), (1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)))]);
}

/// <summary>A quote source that answers from a function and remembers what it was asked.</summary>
internal sealed class FakeQuoteSource : IQuoteSource
{
    public List<IReadOnlyCollection<string>> Calls { get; } = [];

    /// <summary>By default every asked security is quoted at 100 with a previous close of 98.</summary>
    public Func<IReadOnlyCollection<string>, QuoteResult> Respond { get; set; } = ids => new QuoteResult([.. ids.Select(i => new SecurityQuote(i, 100m, 98m))], null, 1);

    public ValueTask<QuoteResult> QuoteAsync(IReadOnlyCollection<string> securityIds, CancellationToken cancellationToken)
    {
        Calls.Add(securityIds);
        return ValueTask.FromResult(Respond(securityIds));
    }
}

internal sealed class FakeDataAccount(DataAccountState state) : IDataAccountStatus
{
    public DataAccountState State { get; set; } = state;

    public ValueTask<DataAccountState> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(State);

    public ValueTask<XDrishti.Domain.Accounts.SecretValue?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<XDrishti.Domain.Accounts.SecretValue?>(null);

    public static FakeDataAccount Connected() => new(new DataAccountState(Guid.NewGuid(), "Primary", DataAccountStatuses.Connected, null));
}
